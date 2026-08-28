using Prdb.Viewer.Core.Library;

using Xunit;

namespace Prdb.Viewer.Core.Tests.Library;

public sealed class WorkIssueRuleTests
{
    [Fact]
    public void Only_a_blocker_or_a_safety_stop_establishes_operational_attention()
    {
        Assert.False(WorkIssueRule.EstablishesOperationalAttention(WorkIssueSeverity.ScopedIssue));
        Assert.True(WorkIssueRule.EstablishesOperationalAttention(
            WorkIssueSeverity.OperationalBlocker));
        Assert.True(WorkIssueRule.EstablishesOperationalAttention(WorkIssueSeverity.SafetyStop));
    }

    [Fact]
    public void An_eligible_automatic_retry_owns_the_issue_before_any_person_does()
    {
        Assert.Equal(
            RemediationOwner.AutomaticRecovery,
            WorkIssueRule.OwnerFor(
                WorkIssueCause.SourceAccess,
                WorkIssueRetryDisposition.AutomaticRetryScheduled));
        Assert.Equal(
            RemediationOwner.InstallationOperator,
            WorkIssueRule.OwnerFor(
                WorkIssueCause.SourceAccess,
                WorkIssueRetryDisposition.RetriesExhausted));
        Assert.Equal(
            RemediationOwner.Administrator,
            WorkIssueRule.OwnerFor(
                WorkIssueCause.ExternalAuthority,
                WorkIssueRetryDisposition.NoAutomaticRetry));
    }

    [Fact]
    public void A_safety_stop_offers_no_retry_and_only_an_operator_handoff()
    {
        var actions = WorkIssueRule.ActionsFor(
            WorkIssueCause.Capacity,
            WorkIssueSeverity.SafetyStop,
            WorkIssueRetryDisposition.NoAutomaticRetry,
            hasAffectedItems: false);

        Assert.DoesNotContain(WorkIssueAction.RetryNow, actions);
        Assert.DoesNotContain(WorkIssueAction.CheckAgain, actions);
        Assert.Equal([WorkIssueAction.CopyOperatorHandoff], actions);
    }

    [Fact]
    public void An_issue_still_owned_by_automatic_recovery_offers_no_manual_attempt()
    {
        var actions = WorkIssueRule.ActionsFor(
            WorkIssueCause.ExternalAvailability,
            WorkIssueSeverity.ScopedIssue,
            WorkIssueRetryDisposition.AutomaticRetryScheduled,
            hasAffectedItems: true);

        Assert.DoesNotContain(WorkIssueAction.RetryNow, actions);
        Assert.Contains(WorkIssueAction.OpenPrdbSettings, actions);
        Assert.Contains(WorkIssueAction.ViewAffectedItems, actions);
    }

    [Fact]
    public void An_exhausted_external_prerequisite_offers_the_check_that_can_advance_it()
    {
        Assert.Contains(
            WorkIssueAction.CheckAgain,
            WorkIssueRule.ActionsFor(
                WorkIssueCause.SourceAccess,
                WorkIssueSeverity.OperationalBlocker,
                WorkIssueRetryDisposition.RetriesExhausted,
                hasAffectedItems: false));
        Assert.Contains(
            WorkIssueAction.RetryNow,
            WorkIssueRule.ActionsFor(
                WorkIssueCause.InvalidContent,
                WorkIssueSeverity.ScopedIssue,
                WorkIssueRetryDisposition.NoAutomaticRetry,
                hasAffectedItems: false));
    }

    [Fact]
    public void Severity_ranks_safety_stops_ahead_of_blockers_and_scoped_issues()
    {
        Assert.True(
            WorkIssueRule.SeverityRank(WorkIssueSeverity.SafetyStop) <
            WorkIssueRule.SeverityRank(WorkIssueSeverity.OperationalBlocker));
        Assert.True(
            WorkIssueRule.SeverityRank(WorkIssueSeverity.OperationalBlocker) <
            WorkIssueRule.SeverityRank(WorkIssueSeverity.ScopedIssue));
    }

    [Fact]
    public void A_summary_states_what_cannot_happen_rather_than_a_technical_failure()
    {
        Assert.True(WorkIssueMessages.IsUsableSummary(WorkIssueMessages.CannotReadFile("a.mp4")));
        Assert.True(WorkIssueMessages.IsUsableSummary(WorkIssueMessages.PrdbUnavailable()));
        Assert.True(WorkIssueMessages.IsUsableSummary(
            WorkIssueMessages.StoppedToProtectLibraryState()));
        Assert.False(WorkIssueMessages.IsUsableSummary("Something went wrong"));
        Assert.False(WorkIssueMessages.IsUsableSummary("UnauthorizedAccessException: denied"));
        Assert.False(WorkIssueMessages.IsUsableSummary("500 while reading"));
        Assert.False(WorkIssueMessages.IsUsableSummary("   "));
    }

    [Fact]
    public void An_operator_handoff_carries_the_condition_and_never_asks_for_container_logs()
    {
        var handoff = OperatorHandoff.Compose(new OperatorHandoffFacts(
            "WI-0A1B2C3D",
            WorkIssueSeverity.OperationalBlocker,
            WorkIssueCause.SourceAccess,
            BackgroundWorkCategory.LibraryScan,
            BackgroundWorkPhases.Traversing,
            "Films/2019",
            "/library/films/2019",
            "The container is not permitted to read the path.",
            12,
            3,
            new DateTimeOffset(2026, 8, 28, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 28, 11, 30, 0, TimeSpan.Zero),
            "Restore the mount or its permissions, then use Check again.",
            "Trustworthy access followed by a completed traversal."));

        Assert.Contains("WI-0A1B2C3D", handoff);
        Assert.Contains("/library/films/2019", handoff);
        Assert.Contains("2026-08-28 09:00:00 UTC", handoff);
        Assert.Contains("automatic retries: 3", handoff);
        Assert.Contains("Trustworthy access followed by a completed traversal.", handoff);
        Assert.DoesNotContain("log", handoff, StringComparison.OrdinalIgnoreCase);
    }
}
