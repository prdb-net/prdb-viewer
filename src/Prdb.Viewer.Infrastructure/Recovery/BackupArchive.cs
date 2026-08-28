using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Konscious.Security.Cryptography;

using Prdb.Viewer.Core.Recovery;

namespace Prdb.Viewer.Infrastructure.Recovery;

/// <summary>
/// The non-secret archive header. It travels in the clear so a product version can decide whether
/// it may open the archive at all, and it is authenticated with the body so it cannot be altered.
/// </summary>
public sealed record BackupArchiveHeader(
    int FormatVersion,
    string ProductVersion,
    DateTimeOffset CreatedAt,
    string KeyDerivation,
    string Salt,
    int MemoryKibiBytes,
    int Iterations,
    int Parallelism);

/// <summary>
/// Reads and writes the wholly encrypted, integrity-protected Backup Archive envelope. The
/// passphrase is never stored, never logged, and never derived from anything the installation
/// retains, so losing it is deliberately unrecoverable.
/// </summary>
public static class BackupArchive
{
    private static readonly JsonSerializerOptions HeaderJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static byte[] Write(
        BackupDocument document,
        string passphrase,
        string productVersion,
        DateTimeOffset createdAt)
    {
        var salt = RandomNumberGenerator.GetBytes(BackupArchiveFormat.SaltBytes);
        var nonce = RandomNumberGenerator.GetBytes(BackupArchiveFormat.NonceBytes);
        var header = new BackupArchiveHeader(
            BackupArchiveFormat.CurrentVersion,
            productVersion,
            createdAt,
            "argon2id",
            Convert.ToBase64String(salt),
            BackupArchiveFormat.MemoryKibiBytes,
            BackupArchiveFormat.Iterations,
            BackupArchiveFormat.Parallelism);
        var headerBytes = JsonSerializer.SerializeToUtf8Bytes(header, HeaderJson);
        var payload = Compress(BackupDocumentSerializer.Serialize(document));
        var key = DeriveKey(passphrase, salt, header);
        var ciphertext = new byte[payload.Length];
        var tag = new byte[BackupArchiveFormat.TagBytes];

        try
        {
            using var cipher = new AesGcm(key, BackupArchiveFormat.TagBytes);
            cipher.Encrypt(nonce, payload, ciphertext, tag, headerBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(payload);
        }

        var length = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, headerBytes.Length);
        using var archive = new MemoryStream();
        archive.Write(BackupArchiveFormat.Magic);
        archive.Write(length);
        archive.Write(headerBytes);
        archive.Write(nonce);
        archive.Write(tag);
        archive.Write(ciphertext);
        return archive.ToArray();
    }

    /// <summary>
    /// Opens an archive. Every failure reports its condition without emitting decrypted data, and
    /// nothing about the archive or the application is changed by the attempt.
    /// </summary>
    public static BackupArchiveOpenResult Read(byte[] archive, string passphrase)
    {
        if (archive.Length < BackupArchiveFormat.Magic.Length + 4 ||
            !archive.AsSpan(0, BackupArchiveFormat.Magic.Length)
                .SequenceEqual(BackupArchiveFormat.Magic))
        {
            return Failed(BackupValidationFailure.NotAnArchive, "The file is not a Backup Archive.");
        }

        var offset = BackupArchiveFormat.Magic.Length;
        var headerLength = BinaryPrimitives.ReadInt32LittleEndian(archive.AsSpan(offset, 4));
        offset += 4;

        if (headerLength <= 0 || headerLength > 8192 || archive.Length < offset + headerLength)
        {
            return Failed(BackupValidationFailure.Damaged, "The archive header is damaged.");
        }

        var headerBytes = archive.AsSpan(offset, headerLength).ToArray();
        offset += headerLength;
        BackupArchiveHeader? header;

        try
        {
            header = JsonSerializer.Deserialize<BackupArchiveHeader>(headerBytes, HeaderJson);
        }
        catch (JsonException)
        {
            header = null;
        }

        if (header is null)
        {
            return Failed(BackupValidationFailure.Damaged, "The archive header is damaged.");
        }

        if (!BackupArchiveFormat.CanRestoreDirectly(header.FormatVersion))
        {
            return Failed(
                BackupValidationFailure.UnsupportedFormat,
                BackupArchiveFormat.ExplainUnsupported(header.FormatVersion),
                header);
        }

        if (!string.Equals(header.KeyDerivation, "argon2id", StringComparison.Ordinal) ||
            !BackupArchiveFormat.IsAcceptableCost(header.MemoryKibiBytes, header.Iterations))
        {
            return Failed(
                BackupValidationFailure.UnsupportedFormat,
                "The archive uses a key derivation this version does not accept.",
                header);
        }

        var body = BackupArchiveFormat.NonceBytes + BackupArchiveFormat.TagBytes;

        if (archive.Length < offset + body)
        {
            return Failed(BackupValidationFailure.Damaged, "The archive is truncated.", header);
        }

        var nonce = archive.AsSpan(offset, BackupArchiveFormat.NonceBytes).ToArray();
        offset += BackupArchiveFormat.NonceBytes;
        var tag = archive.AsSpan(offset, BackupArchiveFormat.TagBytes).ToArray();
        offset += BackupArchiveFormat.TagBytes;
        var ciphertext = archive.AsSpan(offset).ToArray();
        byte[] salt;

        try
        {
            salt = Convert.FromBase64String(header.Salt);
        }
        catch (FormatException)
        {
            return Failed(BackupValidationFailure.Damaged, "The archive header is damaged.", header);
        }

        var key = DeriveKey(passphrase, salt, header);
        var payload = new byte[ciphertext.Length];

        try
        {
            using var cipher = new AesGcm(key, BackupArchiveFormat.TagBytes);
            cipher.Decrypt(nonce, ciphertext, tag, payload, headerBytes);
        }
        catch (CryptographicException)
        {
            return Failed(
                BackupValidationFailure.WrongPassphraseOrAltered,
                "The passphrase is wrong, or the archive was altered or truncated.",
                header);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        try
        {
            var document = BackupDocumentSerializer.Deserialize(Decompress(payload));

            return document is null
                ? Failed(BackupValidationFailure.MissingRequiredState, "The archive is empty.", header)
                : new BackupArchiveOpenResult(true, header, document, null, null);
        }
        catch (JsonException exception)
        {
            // Unknown or unreadable content is a failure rather than a silent omission, because the
            // field this version cannot read might be precious state.
            return Failed(
                BackupValidationFailure.UnknownContent,
                $"The archive contains content this version cannot read: {exception.Message}",
                header);
        }
        catch (InvalidDataException)
        {
            return Failed(BackupValidationFailure.Damaged, "The archive body is damaged.", header);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private static BackupArchiveOpenResult Failed(
        BackupValidationFailure failure,
        string reason,
        BackupArchiveHeader? header = null) =>
        new(false, header, null, failure, reason);

    private static byte[] DeriveKey(string passphrase, byte[] salt, BackupArchiveHeader header)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(passphrase))
        {
            Salt = salt,
            MemorySize = header.MemoryKibiBytes,
            Iterations = header.Iterations,
            DegreeOfParallelism = header.Parallelism,
        };

        return argon.GetBytes(BackupArchiveFormat.KeyBytes);
    }

    private static byte[] Compress(byte[] payload)
    {
        using var compressed = new MemoryStream();

        using (var brotli = new BrotliStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            brotli.Write(payload);
        }

        return compressed.ToArray();
    }

    private static byte[] Decompress(byte[] payload)
    {
        using var source = new MemoryStream(payload);
        using var brotli = new BrotliStream(source, CompressionMode.Decompress);
        using var expanded = new MemoryStream();
        brotli.CopyTo(expanded);
        return expanded.ToArray();
    }
}

public sealed record BackupArchiveOpenResult(
    bool Opened,
    BackupArchiveHeader? Header,
    BackupDocument? Document,
    BackupValidationFailure? Failure,
    string? Reason);
