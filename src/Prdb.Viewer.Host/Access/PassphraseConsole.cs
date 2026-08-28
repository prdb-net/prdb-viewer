using System.Text;

namespace Prdb.Viewer.Host.Access;

/// <summary>
/// Reads a Backup Archive passphrase without ever exposing it. It is never accepted as a
/// command-line argument, so it cannot appear in a process listing, and it is never echoed,
/// printed, or logged.
/// </summary>
public static class PassphraseConsole
{
    /// <summary>The shortest passphrase a new archive may be protected with.</summary>
    public const int MinimumLength = 12;

    public static string? Read(string prompt, TextWriter error, bool confirm = false)
    {
        // Automation pipes the passphrase in rather than typing it, which keeps it out of the
        // command line without requiring an interactive terminal.
        if (Console.IsInputRedirected)
        {
            return Console.In.ReadLine()?.Trim();
        }

        var passphrase = Prompt(prompt);

        if (!confirm)
        {
            return passphrase;
        }

        if (!string.Equals(passphrase, Prompt("Repeat the passphrase: "), StringComparison.Ordinal))
        {
            error.WriteLine("The passphrases do not match.");
            return null;
        }

        return passphrase;
    }

    private static string Prompt(string prompt)
    {
        Console.Write(prompt);
        var passphrase = new StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return passphrase.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (passphrase.Length > 0)
                {
                    passphrase.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                passphrase.Append(key.KeyChar);
            }
        }
    }
}
