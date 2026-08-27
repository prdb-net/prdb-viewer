using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Access;

namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class ViewerDbContext(DbContextOptions<ViewerDbContext> options) : DbContext(options)
{
    public DbSet<AccountRow> Accounts => Set<AccountRow>();

    public DbSet<SessionRow> Sessions => Set<SessionRow>();

    public DbSet<BootstrapAuthorizationRow> BootstrapAuthorizations =>
        Set<BootstrapAuthorizationRow>();

    public DbSet<RecoveryCodeRow> RecoveryCodes => Set<RecoveryCodeRow>();

    public DbSet<InstallationConfigurationRow> InstallationConfigurations =>
        Set<InstallationConfigurationRow>();

    public DbSet<LibraryDirectoryStageRow> LibraryDirectoryStages =>
        Set<LibraryDirectoryStageRow>();

    public DbSet<LibraryDirectoryRow> LibraryDirectories => Set<LibraryDirectoryRow>();

    public DbSet<BackgroundWorkRow> BackgroundWork => Set<BackgroundWorkRow>();

    public DbSet<WorkIssueRow> WorkIssues => Set<WorkIssueRow>();

    public DbSet<VideoRow> Videos => Set<VideoRow>();

    public DbSet<VideoFileRow> VideoFiles => Set<VideoFileRow>();

    public DbSet<VideoFileCandidateRow> VideoFileCandidates => Set<VideoFileCandidateRow>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<AccountRow>(account =>
        {
            account.ToTable("account");
            account.HasKey(row => row.Id);
            account.Property(row => row.Id).ValueGeneratedNever();
            account.Property(row => row.Username).IsRequired();
            account.Property(row => row.NormalizedUsername).IsRequired();
            account.Property(row => row.PasswordHash).IsRequired();
            account.Property(row => row.Authority).HasConversion<string>();
            account.Property(row => row.State).HasConversion<string>();
            account.HasIndex(row => row.NormalizedUsername).IsUnique();
        });

        builder.Entity<SessionRow>(session =>
        {
            session.ToTable("session");
            session.HasKey(row => row.Id);
            session.Property(row => row.Id).ValueGeneratedNever();
            session.Property(row => row.TokenHash).IsRequired();
            session.Property(row => row.CsrfTokenHash).IsRequired();
            session.HasIndex(row => row.TokenHash).IsUnique();
            session.HasIndex(row => row.ExpiresAt);
            session.HasOne(row => row.Account)
                .WithMany()
                .HasForeignKey(row => row.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<BootstrapAuthorizationRow>(authorization =>
        {
            authorization.ToTable("bootstrap_authorization");
            authorization.HasKey(row => row.Id);
            authorization.Property(row => row.Id).ValueGeneratedNever();
            authorization.Property(row => row.TokenHash).IsRequired();
            authorization.Property(row => row.DeliveryPath).IsRequired();
        });

        builder.Entity<RecoveryCodeRow>(recovery =>
        {
            recovery.ToTable("recovery_code");
            recovery.HasKey(row => row.Id);
            recovery.Property(row => row.Id).ValueGeneratedNever();
            recovery.Property(row => row.TokenHash).IsRequired();
            recovery.HasIndex(row => row.TokenHash).IsUnique();
            recovery.HasIndex(row => row.ExpiresAt);
            recovery.HasOne(row => row.Account)
                .WithMany()
                .HasForeignKey(row => row.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<InstallationConfigurationRow>(configuration =>
        {
            configuration.ToTable("installation_configuration");
            configuration.HasKey(row => row.Id);
            configuration.Property(row => row.Id).ValueGeneratedNever();
            configuration.Property(row => row.PrdbConnectionStatus).HasConversion<string>();
            configuration.Property(row => row.LastConnectionIssue).HasConversion<string>();
            configuration.HasData(new InstallationConfigurationRow());
        });

        builder.Entity<LibraryDirectoryStageRow>(stage =>
        {
            stage.ToTable("library_directory_stage");
            stage.HasKey(row => row.Id);
            stage.Property(row => row.Id).ValueGeneratedNever();
            stage.Property(row => row.Name).IsRequired();
            stage.Property(row => row.ContainerPath).IsRequired();
            stage.HasIndex(row => row.ExpiresAt);
        });

        builder.Entity<LibraryDirectoryRow>(directory =>
        {
            directory.ToTable("library_directory");
            directory.HasKey(row => row.Id);
            directory.Property(row => row.Id).ValueGeneratedNever();
            directory.Property(row => row.Name).IsRequired();
            directory.Property(row => row.ContainerPath).IsRequired();
            directory.Property(row => row.State).HasConversion<string>();
            directory.Property(row => row.Health).HasConversion<string>();
            directory.HasIndex(row => row.State);
            directory.HasIndex(row => row.ContainerPath)
                .IsUnique()
                .HasFilter("\"State\" = 'Active'");
        });

        builder.Entity<BackgroundWorkRow>(work =>
        {
            work.ToTable("background_work");
            work.HasKey(row => row.Id);
            work.Property(row => row.Id).ValueGeneratedNever();
            work.Property(row => row.Category).HasConversion<string>();
            work.Property(row => row.State).HasConversion<string>();
            work.HasIndex(row => new { row.Category, row.State, row.RequestedAt });
            work.HasIndex(row => new
            {
                row.LibraryDirectoryId,
                row.Category,
                row.ConfigurationGeneration,
            });
            work.HasOne(row => row.LibraryDirectory)
                .WithMany()
                .HasForeignKey(row => row.LibraryDirectoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<WorkIssueRow>(issue =>
        {
            issue.ToTable("work_issue");
            issue.HasKey(row => row.Id);
            issue.Property(row => row.Id).ValueGeneratedNever();
            issue.Property(row => row.Severity).HasConversion<string>();
            issue.Property(row => row.Cause).HasConversion<string>();
            issue.Property(row => row.RemediationOwner).HasConversion<string>();
            issue.HasIndex(row => new { row.BackgroundWorkId, row.ResolvedAt });
            issue.HasOne(row => row.BackgroundWork)
                .WithMany()
                .HasForeignKey(row => row.BackgroundWorkId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<VideoRow>(video =>
        {
            video.ToTable("video");
            video.HasKey(row => row.Id);
            video.Property(row => row.Id).ValueGeneratedNever();
            video.HasIndex(row => row.DiscoveryDate);
        });

        builder.Entity<VideoFileRow>(videoFile =>
        {
            videoFile.ToTable("video_file");
            videoFile.HasKey(row => row.Id);
            videoFile.Property(row => row.Id).ValueGeneratedNever();
            videoFile.Property(row => row.Availability).HasConversion<string>();
            videoFile.HasIndex(row => new { row.LibraryDirectoryId, row.RelativePath });
            videoFile.HasIndex(row => new { row.LibraryDirectoryId, row.Sha256 });
            videoFile.HasOne(row => row.Video)
                .WithMany(row => row.VideoFiles)
                .HasForeignKey(row => row.VideoId)
                .OnDelete(DeleteBehavior.Restrict);
            videoFile.HasOne(row => row.LibraryDirectory)
                .WithMany()
                .HasForeignKey(row => row.LibraryDirectoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<VideoFileCandidateRow>(candidate =>
        {
            candidate.ToTable("video_file_candidate");
            candidate.HasKey(row => row.Id);
            candidate.Property(row => row.Id).ValueGeneratedNever();
            candidate.Property(row => row.State).HasConversion<string>();
            candidate.HasIndex(row => new { row.LibraryScanId, row.RelativePath }).IsUnique();
            candidate.HasIndex(row => new { row.LibraryScanId, row.State });
        });
    }
}
