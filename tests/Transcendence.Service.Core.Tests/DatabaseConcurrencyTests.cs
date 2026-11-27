using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public sealed class DatabaseConcurrencyTests
{
    [Theory]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    public async Task ProviderTransactionFailure_IsNotRetriedAsSummonerActivityConflict(string sqlState)
    {
        var providerError = new PostgresException("Transaction failed", "ERROR", "ERROR", sqlState);
        var error = new DbUpdateConcurrencyException("Database transaction failed", providerError);
        var failure = new FailFirstSave(error);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SqliteCompatibleTranscendenceContext(new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .AddInterceptors(failure)
            .Options);
        await db.Database.EnsureCreatedAsync();

        db.Summoners.Add(new Summoner { Puuid = "test-puuid", PlatformRegion = "NA1", Region = "AMERICAS" });

        var save = () => db.SaveChangesAsync();
        (await save.Should().ThrowAsync<DbUpdateConcurrencyException>()).Which.Should().BeSameAs(error);
        failure.Attempts.Should().Be(1);
    }

    private sealed class FailFirstSave(DbUpdateConcurrencyException error) : SaveChangesInterceptor
    {
        public int Attempts { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (++Attempts == 1)
                throw error;
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
