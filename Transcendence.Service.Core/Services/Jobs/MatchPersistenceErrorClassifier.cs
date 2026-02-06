using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Transcendence.Service.Core.Services.Jobs;

internal static class MatchPersistenceErrorClassifier
{
    private const string DuplicateKeySqlState = "23505";
    private const string MatchIdConstraintName = "IX_Matches_MatchId";

    public static bool IsDuplicateMatchIdViolation(DbUpdateException ex)
    {
        if (ex.InnerException is not PostgresException postgresException)
            return false;

        return string.Equals(postgresException.SqlState, DuplicateKeySqlState, StringComparison.Ordinal)
               && string.Equals(postgresException.ConstraintName, MatchIdConstraintName, StringComparison.Ordinal);
    }
}
