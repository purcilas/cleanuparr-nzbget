using Cleanuparr.Infrastructure.Tests.TestHelpers;
using Cleanuparr.Persistence;
using Cleanuparr.Persistence.Models.Configuration;
using Cleanuparr.Persistence.Models.State;
using Cleanuparr.Persistence.Providers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace Cleanuparr.Infrastructure.Tests.Features.DownloadClient.Usenet;

[Collection("SeedParity")]
public sealed class UsenetMigrationTests
{
    [Fact]
    public async Task SQLite_migrations_match_models_and_preserve_observations_across_reopen()
    {
        using var data = SqliteTestDatabase.Create("usenet-data-migration");
        using var events = SqliteTestDatabase.Create("usenet-events-migration");
        await VerifyAsync(data.CreateContext<DataContext>, events.CreateContext<EventsContext>);
    }

    [SkippableFact]
    public async Task PostgreSQL_migrations_match_models_and_preserve_observations_across_reopen()
    {
        string? connection = Environment.GetEnvironmentVariable("CLEANUPARR_TEST_POSTGRES");
        Skip.If(string.IsNullOrEmpty(connection), "Set CLEANUPARR_TEST_POSTGRES to a disposable PostgreSQL database");
        DataContext Data() => new(new DbContextOptionsBuilder<DataContext>().UseNpgsql(connection,
            x => x.MigrationsAssembly("Cleanuparr.Persistence.Postgres")).UseLowerCaseNamingConvention().UseSnakeCaseNamingConvention().Options, new PostgresDatabaseProvider());
        EventsContext Events() => new(new DbContextOptionsBuilder<EventsContext>().UseNpgsql(connection,
            x => x.MigrationsAssembly("Cleanuparr.Persistence.Postgres")).UseLowerCaseNamingConvention().UseSnakeCaseNamingConvention().Options, new PostgresDatabaseProvider());
        await VerifyAsync(Data, Events);
    }

    private static async Task VerifyAsync(Func<DataContext> dataFactory, Func<EventsContext> eventsFactory)
    {
        var config = NzbGetServiceTests.Config with { Enabled = true, UsenetOptions = new() { StallMinutes = 90 } };
        Guid otherClient = Guid.NewGuid();
        await using (var data = dataFactory())
        {
            data.Database.HasPendingModelChanges().ShouldBeFalse();
            await data.Database.MigrateAsync();
            data.DownloadClients.Add(config); await data.SaveChangesAsync();
        }
        await using (var events = eventsFactory())
        {
            events.Database.HasPendingModelChanges().ShouldBeFalse();
            await events.Database.MigrateAsync();
            events.UsenetObservations.AddRange(new UsenetObservation { Id = $"{config.Id}:7", ClientId = config.Id, Samples = 3, ActionState = "Uncertain" },
                new UsenetObservation { Id = $"{otherClient}:7", ClientId = otherClient, Samples = 1 });
            await events.SaveChangesAsync();
        }
        await using (var data = dataFactory())
        {
            (await data.DownloadClients.SingleAsync(x => x.Id == config.Id)).UsenetOptions.StallMinutes.ShouldBe(90);
        }
        await using (var events = eventsFactory())
        {
            (await events.UsenetObservations.FindAsync($"{config.Id}:7"))!.ActionState.ShouldBe("Uncertain");
            (await events.UsenetObservations.FindAsync($"{otherClient}:7"))!.Samples.ShouldBe(1);
        }
    }
}
