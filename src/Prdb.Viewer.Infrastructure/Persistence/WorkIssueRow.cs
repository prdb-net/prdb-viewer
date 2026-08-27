using Prdb.Viewer.Core.Library;

namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class WorkIssueRow
{
    public Guid Id { get; set; }

    public Guid BackgroundWorkId { get; set; }

    public BackgroundWorkRow BackgroundWork { get; set; } = null!;

    public WorkIssueSeverity Severity { get; set; }

    public WorkIssueCause Cause { get; set; }

    public RemediationOwner RemediationOwner { get; set; }

    public required string AffectedScope { get; set; }

    public required string Impact { get; set; }

    public required string RequiredAction { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }
}
