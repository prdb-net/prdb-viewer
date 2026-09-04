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

    public DbSet<WorkIssueItemRow> WorkIssueItems => Set<WorkIssueItemRow>();

    public DbSet<VideoRow> Videos => Set<VideoRow>();

    public DbSet<VideoActorRow> VideoActors => Set<VideoActorRow>();

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

    public DbSet<SiteDirectoryEntryRow> SiteDirectoryEntries => Set<SiteDirectoryEntryRow>();

    public DbSet<ProposedWorkRow> ProposedWorks => Set<ProposedWorkRow>();

    public DbSet<ClientPlaybackAssessmentRow> ClientPlaybackAssessments =>
        Set<ClientPlaybackAssessmentRow>();

    public DbSet<ObservedPlaybackOutcomeRow> ObservedPlaybackOutcomes =>
        Set<ObservedPlaybackOutcomeRow>();

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
            work.Property(row => row.StateBeforePause).HasConversion<string>();
            work.Property(row => row.Trigger).HasConversion<string>();
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
            issue.Property(row => row.RetryDisposition).HasConversion<string>();
            issue.Property(row => row.Category).HasConversion<string>();
            issue.Property(row => row.Reference).IsRequired();
            issue.Property(row => row.AggregationKey).IsRequired();
            issue.Property(row => row.Summary).IsRequired();
            issue.Property(row => row.Detail).IsRequired();
            issue.Property(row => row.Phase).IsRequired();
            issue.Property(row => row.SafeCause).IsRequired();
            issue.Property(row => row.ExpectedResolutionEvidence).IsRequired();
            issue.HasIndex(row => new { row.BackgroundWorkId, row.ResolvedAt });
            issue.HasIndex(row => new { row.AggregationKey, row.ResolvedAt });
            issue.HasIndex(row => new { row.ResolvedAt, row.Severity, row.LastOccurredAt });
            issue.HasIndex(row => row.Reference).IsUnique();
            issue.HasOne(row => row.BackgroundWork)
                .WithMany()
                .HasForeignKey(row => row.BackgroundWorkId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WorkIssueItemRow>(item =>
        {
            item.ToTable("work_issue_item");
            item.HasKey(row => row.Id);
            item.Property(row => row.Id).ValueGeneratedNever();
            item.Property(row => row.Scope).IsRequired();
            item.HasIndex(row => new { row.WorkIssueId, row.Scope }).IsUnique();
            item.HasOne(row => row.WorkIssue)
                .WithMany()
                .HasForeignKey(row => row.WorkIssueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<VideoRow>(video =>
        {
            video.ToTable("video");
            video.HasKey(row => row.Id);
            video.Property(row => row.Id).ValueGeneratedNever();
            video.Property(row => row.BestClassification).HasConversion<string>();
            video.Property(row => row.Availability).HasConversion<string>();
            // Stored as its ordinal rather than as a name, unlike the enumerations beside it: a
            // band is an order, and `ORDER BY` over the names would sort 1080p above 4K.
            video.Property(row => row.Quality).HasConversion<int>();
            video.Property(row => row.DisplayLabel).IsRequired();
            video.Property(row => row.SearchText).IsRequired();
            video.HasIndex(row => row.DiscoveryDate);
            video.HasIndex(row => row.SurvivingVideoId);
            video.HasIndex(row => row.ProjectedAt);

            // The two orders discovery offers, each narrowed first by what admits a Video to it.
            video.HasIndex(row => new
            {
                row.SurvivingVideoId,
                row.Availability,
                row.BestClassification,
                row.DiscoveryDate,
            });
            video.HasIndex(row => new
            {
                row.SurvivingVideoId,
                row.Availability,
                row.BestClassification,
                row.DisplayLabel,
            });
            video.HasIndex(row => new
            {
                row.SurvivingVideoId,
                row.Availability,
                row.BestClassification,
                row.Quality,
            });
        });

        builder.Entity<VideoActorRow>(actor =>
        {
            actor.ToTable("video_actor");
            actor.HasKey(row => row.Id);
            actor.Property(row => row.Id).ValueGeneratedNever();
            actor.Property(row => row.Name).IsRequired();
            actor.Property(row => row.NormalizedName).IsRequired();
            actor.HasIndex(row => new { row.VideoId, row.NormalizedName }).IsUnique();
            actor.HasIndex(row => row.NormalizedName);
            actor.HasOne(row => row.Video)
                .WithMany(row => row.ProjectedActors)
                .HasForeignKey(row => row.VideoId)
                .OnDelete(DeleteBehavior.Cascade);
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
            candidate.Property(row => row.Source).HasConversion<string>();
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
            // The retained facts outlive the candidate that first needed them: a rejected proposal
            // leaves them behind for the next Video that proposes the same work.
            candidate.HasOne(row => row.ProposedWork)
                .WithMany()
                .HasForeignKey(row => row.ProposedWorkId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ProposedWorkRow>(work =>
        {
            work.ToTable("proposed_work");
            work.HasKey(row => row.Id);
            work.Property(row => row.Id).ValueGeneratedNever();
            work.Property(row => row.PrdbVideoId).IsRequired();
            work.Property(row => row.Title).IsRequired();
            work.Property(row => row.ArtworkState).HasConversion<string>();
            work.HasIndex(row => row.PrdbVideoId).IsUnique();
            work.HasIndex(row => row.PublicArtworkId).IsUnique();
            work.HasIndex(row => row.ArtworkState);
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
            // The admission question discovery asks of every Video: which of its Available
            // occurrences a client could play. Carrying the classification and the Profile Key in
            // the index answers it without reading the rows themselves.
            videoFile.HasIndex(row => new
            {
                row.VideoId,
                row.Availability,
                row.DirectPlayClassification,
                row.ProfileKey,
            });
            videoFile.HasIndex(row => row.ProfileKey);
            videoFile.HasIndex(row => new { row.LibraryDirectoryId, row.Availability, row.SiteRecognisedPath });
            videoFile.HasOne(row => row.Video)
                .WithMany(row => row.VideoFiles)
                .HasForeignKey(row => row.VideoId)
                .OnDelete(DeleteBehavior.Restrict);
            videoFile.HasOne(row => row.LibraryDirectory)
                .WithMany()
                .HasForeignKey(row => row.LibraryDirectoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ClientPlaybackAssessmentRow>(assessment =>
        {
            assessment.ToTable("client_playback_assessment");
            assessment.HasKey(row => new { row.AccountId, row.ClientContextKey, row.ProfileKey });
            assessment.Property(row => row.Verdict).HasConversion<string>();
            assessment.Property(row => row.Method).IsRequired();
            assessment.HasOne(row => row.Account)
                .WithMany()
                .HasForeignKey(row => row.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ObservedPlaybackOutcomeRow>(outcome =>
        {
            outcome.ToTable("observed_playback_outcome");
            outcome.HasKey(row => new { row.AccountId, row.ClientContextKey, row.VideoFileId });
            outcome.Property(row => row.Outcome).HasConversion<string>();
            outcome.Property(row => row.FailureCategory).HasConversion<string>();
            outcome.Property(row => row.ContentSha256).IsRequired();
            outcome.HasOne(row => row.Account)
                .WithMany()
                .HasForeignKey(row => row.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
            outcome.HasOne(row => row.VideoFile)
                .WithMany()
                .HasForeignKey(row => row.VideoFileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SiteDirectoryEntryRow>(site =>
        {
            site.ToTable("site_directory_entry");
            site.HasKey(row => row.SiteKey);
            site.Property(row => row.SiteKey).ValueGeneratedNever();
            site.Property(row => row.Title).IsRequired();
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
