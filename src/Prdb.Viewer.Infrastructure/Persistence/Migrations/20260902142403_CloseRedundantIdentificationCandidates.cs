using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CloseRedundantIdentificationCandidates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Candidates proposing the identification their own Video already has established.
            // They are no longer created — a proposal that agrees with the current claim confirms
            // it instead — but an installation that ran the earlier releases carries the ones they
            // made, and nothing retires them: they are Pending, so the Video stays in review, and
            // no decision closes them except an Administrator rejecting evidence that was never
            // wrong. Where such a candidate is a Site Recognition on a Video whose Site came with
            // its Work Identification, rejecting is in fact the only decision the review offers.
            //
            // A Video whose case is open in a browser gets a new version, so the screen finds the
            // case changed and reloads rather than deciding against candidates that are gone.
            migrationBuilder.Sql(
                """
                UPDATE video
                SET CaseVersion = CaseVersion + 1
                WHERE EXISTS (
                    SELECT 1
                    FROM identification_candidate candidate
                    JOIN identification_claim claim
                      ON claim.VideoId = candidate.VideoId
                     AND claim.Dimension = candidate.Dimension
                     AND claim.Status = 'Current'
                     AND claim.TargetKey = candidate.TargetKey COLLATE NOCASE
                    WHERE candidate.VideoId = video.Id
                      AND candidate.Status = 'Pending');
                """);

            // Superseded rather than Rejected: the evidence was never wrong, it was overtaken by
            // an answer the library already held. Rejecting it would suppress the same evidence
            // until something materially stronger appeared, which is a decision about the file's
            // path that nobody made.
            migrationBuilder.Sql(
                """
                UPDATE identification_candidate
                SET Status = 'Superseded',
                    ResolvedAt = datetime('now')
                WHERE Status = 'Pending'
                  AND EXISTS (
                      SELECT 1
                      FROM identification_claim claim
                      WHERE claim.VideoId = identification_candidate.VideoId
                        AND claim.Dimension = identification_candidate.Dimension
                        AND claim.Status = 'Current'
                        AND claim.TargetKey = identification_candidate.TargetKey COLLATE NOCASE);
                """);

            // The projection stores whether a Video needs review, and it is derived from the
            // candidates that are still Pending. Closing one without recomputing it would leave
            // the library filtering on an answer that is no longer true.
            migrationBuilder.Sql(
                """
                UPDATE video
                SET ReviewNeeded = EXISTS (
                    SELECT 1
                    FROM identification_candidate candidate
                    WHERE candidate.VideoId = video.Id
                      AND candidate.Status = 'Pending');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: reopening candidates the library has answered would recreate a
            // review that has no decision to make. Migrations here only go forward, and the schema
            // this one runs against is the one it leaves behind.
        }
    }
}
