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

    public DbSet<VideoMetadataRow> VideoMetadata => Set<VideoMetadataRow>();

    public DbSet<IdentificationClaimRow> IdentificationClaims => Set<IdentificationClaimRow>();

    public DbSet<IdentificationCandidateRow> IdentificationCandidates =>
        Set<IdentificationCandidateRow>();

    public DbSet<IdentificationDecisionRow> IdentificationDecisions =>
        Set<IdentificationDecisionRow>();

    public DbSet<PersonalVideoStateRow> PersonalVideoStates => Set<PersonalVideoStateRow>();

    public DbSet<PlaybackAttemptRow> PlaybackAttempts => Set<PlaybackAttemptRow>();

    public DbSet<PlaybackReportRow> PlaybackReports => Set<PlaybackReportRow>();

    public DbSet<PlaybackAttemptVideoFileRow> PlaybackAttemptVideoFiles =>
        Set<PlaybackAttemptVideoFileRow>();

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
            video.HasIndex(row => row.SurvivingVideoId);
        });

        builder.Entity<VideoMetadataRow>(metadata =>
        {
            metadata.ToTable("video_metadata");
            metadata.HasKey(row => row.VideoId);
            metadata.Property(row => row.VideoId).ValueGeneratedNever();
            metadata.Property(row => row.PrdbVideoId).IsRequired();
            metadata.Property(row => row.Title).IsRequired();
            metadata.HasIndex(row => row.PrdbVideoId);
            metadata.HasOne(row => row.Video)
                .WithOne(row => row.Metadata)
                .HasForeignKey<VideoMetadataRow>(row => row.VideoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<IdentificationClaimRow>(claim =>
        {
            claim.ToTable("identification_claim");
            claim.HasKey(row => row.Id);
            claim.Property(row => row.Id).ValueGeneratedNever();
            claim.Property(row => row.Dimension).HasConversion<string>();
            claim.Property(row => row.Status).HasConversion<string>();
            claim.Property(row => row.Source).HasConversion<string>();
            claim.Property(row => row.EvidenceClass).HasConversion<string>();
            claim.Property(row => row.TargetKey).IsRequired();
            claim.Property(row => row.TargetTitle).IsRequired();
            claim.HasIndex(row => new { row.VideoId, row.Dimension, row.Status });
            claim.HasIndex(row => new { row.Dimension, row.TargetKey, row.Status });
            claim.HasOne(row => row.Video)
                .WithMany(row => row.IdentificationClaims)
                .HasForeignKey(row => row.VideoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<IdentificationCandidateRow>(candidate =>
        {
            candidate.ToTable("identification_candidate");
            candidate.HasKey(row => row.Id);
            candidate.Property(row => row.Id).ValueGeneratedNever();
            candidate.Property(row => row.Dimension).HasConversion<string>();
            candidate.Property(row => row.Status).HasConversion<string>();
            candidate.Property(row => row.EvidenceClass).HasConversion<string>();
            candidate.Property(row => row.Reason).HasConversion<string>();
            candidate.Property(row => row.TargetKey).IsRequired();
            candidate.Property(row => row.TargetTitle).IsRequired();
            candidate.Property(row => row.EvidenceKey).IsRequired();
            candidate.HasIndex(row => new { row.VideoId, row.Dimension, row.Status });
            candidate.HasIndex(row => new
            {
                row.VideoId,
                row.Dimension,
                row.TargetKey,
                row.EvidenceKey,
            });
            candidate.HasIndex(row => new { row.Status, row.EvidenceClass, row.CreatedAt });
            candidate.HasOne(row => row.Video)
                .WithMany(row => row.IdentificationCandidates)
                .HasForeignKey(row => row.VideoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<IdentificationDecisionRow>(decision =>
        {
            decision.ToTable("identification_decision");
            decision.HasKey(row => row.Id);
            decision.Property(row => row.Id).ValueGeneratedNever();
            decision.Property(row => row.Dimension).HasConversion<string>();
            decision.Property(row => row.Action).HasConversion<string>();
            decision.Property(row => row.PriorState).IsRequired();
            decision.Property(row => row.ResultingState).IsRequired();
            decision.HasIndex(row => new { row.VideoId, row.CreatedAt });
        });

        builder.Entity<VideoFileRow>(videoFile =>
        {
            videoFile.ToTable("video_file");
            videoFile.HasKey(row => row.Id);
            videoFile.Property(row => row.Id).ValueGeneratedNever();
            videoFile.Property(row => row.Availability).HasConversion<string>();
            videoFile.Property(row => row.DirectPlayClassification).HasConversion<string>();
            videoFile.Property(row => row.HashState).HasConversion<string>();
            videoFile.Property(row => row.PreviewState).HasConversion<string>();
            videoFile.HasIndex(row => row.PublicDeliveryId).IsUnique();
            videoFile.HasIndex(row => row.PublicPreviewId).IsUnique();
            videoFile.HasIndex(row => new { row.LibraryDirectoryId, row.Availability, row.HashState });
            videoFile.HasIndex(row => new { row.LibraryDirectoryId, row.Availability, row.PreviewState });
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

        builder.Entity<PersonalVideoStateRow>(state =>
        {
            state.ToTable("personal_video_state");
            state.ToTable(table => table.HasCheckConstraint(
                "CK_personal_video_state_PersonalRating",
                "\"PersonalRating\" IS NULL OR \"PersonalRating\" BETWEEN 1 AND 5"));
            state.HasKey(row => new { row.AccountId, row.VideoId });
            state.Property(row => row.PlayState).HasConversion<string>();
            state.HasIndex(row => new { row.AccountId, row.LastQualifiedActivityAt });
            state.HasIndex(row => new { row.AccountId, row.FavouriteAddedAt });
            state.HasIndex(row => new { row.AccountId, row.WatchLaterAddedAt });
            state.HasOne(row => row.Account)
                .WithMany(row => row.PersonalVideoStates)
                .HasForeignKey(row => row.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
            state.HasOne(row => row.Video)
                .WithMany(row => row.PersonalStates)
                .HasForeignKey(row => row.VideoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PlaybackAttemptRow>(attempt =>
        {
            attempt.ToTable("playback_attempt");
            attempt.HasKey(row => row.Id);
            attempt.Property(row => row.Id).ValueGeneratedNever();
            attempt.HasIndex(row => new { row.AccountId, row.VideoId, row.AttemptedAt });
            attempt.HasIndex(row => new { row.AccountId, row.EndedAt, row.LastActivityAt });
            attempt.HasOne(row => row.Account)
                .WithMany(row => row.PlaybackAttempts)
                .HasForeignKey(row => row.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
            attempt.HasOne(row => row.Video)
                .WithMany(row => row.PlaybackAttempts)
                .HasForeignKey(row => row.VideoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PlaybackReportRow>(report =>
        {
            report.ToTable("playback_report");
            report.HasKey(row => row.Id);
            report.Property(row => row.Id).ValueGeneratedNever();
            report.HasIndex(row => new { row.PlaybackAttemptId, row.Sequence });
            report.HasIndex(row => new { row.ActivityStartedAt, row.ActivityEndedAt });
            report.HasOne(row => row.PlaybackAttempt)
                .WithMany(row => row.Reports)
                .HasForeignKey(row => row.PlaybackAttemptId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PlaybackAttemptVideoFileRow>(participation =>
        {
            participation.ToTable("playback_attempt_video_file");
            participation.HasKey(row => new { row.PlaybackAttemptId, row.VideoFileId });
            participation.HasOne(row => row.PlaybackAttempt)
                .WithMany(row => row.VideoFiles)
                .HasForeignKey(row => row.PlaybackAttemptId)
                .OnDelete(DeleteBehavior.Cascade);
            participation.HasOne(row => row.VideoFile)
                .WithMany(row => row.PlaybackAttempts)
                .HasForeignKey(row => row.VideoFileId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
