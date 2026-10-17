using System.Diagnostics;
using System.Text.Json;
using Hengchuan.Api;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Hengchuan.Tests;

public sealed class StoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "hengchuan-store-tests", Guid.NewGuid().ToString("N"));
    private readonly string database;
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-17T10:00:00Z");

    public StoreTests()
    {
        Directory.CreateDirectory(directory);
        database = Path.Combine(directory, "atlas.db");
    }

    private static AtlasState Fixture() => new()
    {
        Spaces = [new Space { Id = "space-test", Name = "Synthetic space" }],
        Businesses = [new Business
        {
            Id = "biz-test", SpaceId = "space-test", Name = "Checkout", Owner = "synthetic-owner",
            Description = "合成业务测试", Service = "checkout", Environment = "demo", TargetAvailability = 99.9, UpdatedAt = Now
        }],
        Indicators = [new Indicator
        {
            Id = "ind-test", SpaceId = "space-test", Name = "Requests", SourceId = "demo", SourceName = "Synthetic source",
            MetricName = "checkout.requests", Unit = "次/分钟", Owner = "synthetic-owner", Visibility = "space", Enabled = true,
            BusinessIds = ["biz-test"], Behavior = "normal", UpdatedAt = Now, UpdatedBy = "demo-operator"
        }],
        Events = [new IncidentEvent
        {
            Id = "event-test", SpaceId = "space-test", BusinessId = "biz-test", Title = "合成已恢复事件", Level = "P2",
            StartedAt = Now.AddMinutes(-20), EndedAt = Now.AddMinutes(-10), Description = "合成事件描述"
        }],
        Screens = [new Screen
        {
            Id = "screen-test", SpaceId = "space-test", Name = "Checkout overview", BusinessId = "biz-test", Columns = 2,
            Panels = [new ScreenPanel { IndicatorId = "ind-test", Kind = "line", Aggregation = "avg" }], UpdatedAt = Now, UpdatedBy = "demo-operator"
        }]
    };

    private static void ReplaceState(AtlasState target, AtlasState source)
    {
        target.Spaces.Clear(); target.Spaces.AddRange(source.Spaces);
        target.Businesses.Clear(); target.Businesses.AddRange(source.Businesses);
        target.Indicators.Clear(); target.Indicators.AddRange(source.Indicators);
        target.Events.Clear(); target.Events.AddRange(source.Events);
        target.Screens.Clear(); target.Screens.AddRange(source.Screens);
    }

    private async Task<(AtlasStore Store, StoreSnapshot Snapshot)> PreparedStore()
    {
        var store = new AtlasStore(database);
        await store.InitializeAsync();
        var initial = await store.ReadAsync();
        var snapshot = await store.UpdateAsync(initial.Revision, state => ReplaceState(state, Fixture()));
        return (store, snapshot);
    }

    private static string StateJson(AtlasState state) => JsonSerializer.Serialize(state);

    private static void AssertError(int statusCode, string code, AtlasException exception)
    {
        Assert.Equal(statusCode, exception.StatusCode);
        Assert.Equal(code, exception.Code);
    }

    private static void AddOtherSpace(AtlasState state)
    {
        state.Spaces.Add(new Space { Id = "space-other", Name = "Other synthetic space" });
        state.Businesses.Add(state.Businesses[0] with { Id = "biz-other", SpaceId = "space-other", Name = "Other business" });
        state.Indicators.Add(state.Indicators[0] with { Id = "ind-other", SpaceId = "space-other", Name = "Other indicator", BusinessIds = ["biz-other"] });
    }

    [Fact]
    public async Task InitializationCreatesOneSchemaOneSnapshotAndNeverOverwritesIt()
    {
        var store = new AtlasStore(database);
        await store.InitializeAsync();
        var first = await store.ReadAsync();
        Assert.Equal(1L, first.Revision);
        Assert.Equal(1, first.State.SchemaVersion);
        AtlasValidation.Validate(first.State);

        await store.InitializeAsync();
        var reopened = new AtlasStore(database);
        await reopened.InitializeAsync();
        var second = await reopened.ReadAsync();
        Assert.Equal(first.Revision, second.Revision);
        Assert.Equal(StateJson(first.State), StateJson(second.State));

        using var connection = new SqliteConnection($"Data Source={database}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM atlas_snapshot WHERE id=1 AND schema_version=1 AND revision=1";
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
        command.CommandText = "SELECT COUNT(*) FROM atlas_snapshot";
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
        command.CommandText = "PRAGMA user_version";
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task ReadAndUpdateReturnDetachedSnapshots()
    {
        var (store, initial) = await PreparedStore();
        initial.State.Businesses.Clear();
        initial.State.Indicators[0].BusinessIds.Clear();
        initial.State.Screens[0].Panels.Clear();
        var current = await store.ReadAsync();
        Assert.Single(current.State.Businesses);
        Assert.Single(current.State.Indicators[0].BusinessIds);
        Assert.Single(current.State.Screens[0].Panels);

        current.State.Events.Clear();
        Assert.Single((await store.ReadAsync()).State.Events);
    }

    [Fact]
    public async Task CrudWritesIncrementRevisionAndSurviveReopening()
    {
        var (store, initial) = await PreparedStore();
        var created = await store.UpdateAsync(initial.Revision, state => state.Businesses.Add(state.Businesses[0] with { Id = "biz-extra", Name = "Extra synthetic business" }));
        var edited = await store.UpdateAsync(created.Revision, state => state.Businesses[1] = state.Businesses[1] with { Name = "Edited synthetic business" });
        var deleted = await store.UpdateAsync(edited.Revision, state => state.Businesses.RemoveAll(x => x.Id == "biz-extra"));

        Assert.Equal(initial.Revision + 1, created.Revision);
        Assert.Equal(initial.Revision + 2, edited.Revision);
        Assert.Equal(initial.Revision + 3, deleted.Revision);
        Assert.Equal("Edited synthetic business", edited.State.Businesses[1].Name);
        Assert.Single(deleted.State.Businesses);
        var reopened = new AtlasStore(database);
        await reopened.InitializeAsync();
        var persisted = await reopened.ReadAsync();
        Assert.Equal(deleted.Revision, persisted.Revision);
        Assert.Equal(StateJson(deleted.State), StateJson(persisted.State));
    }

    [Fact]
    public async Task StaleRevisionFailsBeforeMutationAndDoesNotChangeState()
    {
        var (store, initial) = await PreparedStore();
        var mutationRan = false;
        var error = await Assert.ThrowsAsync<AtlasException>(() => store.UpdateAsync(initial.Revision - 1, state =>
        {
            mutationRan = true;
            state.Businesses.Clear();
        }));

        AssertError(409, "REVISION_CONFLICT", error);
        Assert.False(mutationRan);
        await AssertUnchanged(store, initial);
    }

    [Fact]
    public async Task ConcurrentStoreInstancesAllowExactlyOneWriteForTheSameRevision()
    {
        var (first, initial) = await PreparedStore();
        var second = new AtlasStore(database);
        await second.InitializeAsync();

        async Task<AtlasException?> Attempt(AtlasStore store, string id)
        {
            try
            {
                await store.UpdateAsync(initial.Revision, state => state.Businesses.Add(state.Businesses[0] with { Id = id, Name = id }));
                return null;
            }
            catch (AtlasException exception) { return exception; }
        }

        var results = await Task.WhenAll(Task.Run(() => Attempt(first, "biz-concurrent-a")), Task.Run(() => Attempt(second, "biz-concurrent-b")));
        Assert.Single(results, result => result is null);
        AssertError(409, "REVISION_CONFLICT", Assert.Single(results.OfType<AtlasException>()));
        var persisted = await first.ReadAsync();
        Assert.Equal(initial.Revision + 1, persisted.Revision);
        Assert.Equal(2, persisted.State.Businesses.Count);
    }

    [Fact]
    public async Task PreCancelledOperationsDoNotChangeRevisionOrState()
    {
        var (store, initial) = await PreparedStore();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ReadAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.UpdateAsync(initial.Revision, state => state.Events.Clear(), cancellation.Token));
        await AssertUnchanged(store, initial);
    }

    [Fact]
    public async Task CancellationInsideMutationRollsBackAndAllowsTheNextWrite()
    {
        var (store, initial) = await PreparedStore();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.UpdateAsync(initial.Revision, state =>
        {
            state.Events.Clear();
            cancellation.Cancel();
        }, cancellation.Token));

        await AssertUnchanged(store, initial);
        var next = await store.UpdateAsync(initial.Revision, state => state.Events.Clear());
        Assert.Equal(initial.Revision + 1, next.Revision);
        Assert.Empty(next.State.Events);
    }

    [Fact]
    public async Task CancellationWhileWaitingForAnExternalWriteLockIsBoundedAndDoesNotWrite()
    {
        var (first, initial) = await PreparedStore();
        using var second = new AtlasStore(database);
        await second.InitializeAsync();
        await using var blocker = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, DefaultTimeout = 1 }.ToString());
        await blocker.OpenAsync();
        var transaction = blocker.BeginTransaction(deferred: false);
        using var cancellation = new CancellationTokenSource();
        Task<StoreSnapshot>? update = null;
        var elapsed = Stopwatch.StartNew();

        try
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            update = Task.Run(async () =>
            {
                started.SetResult();
                return await second.UpdateAsync(initial.Revision, state => state.Events.Clear(), cancellation.Token);
            });
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(50);
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => update.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(4), $"写锁等待取消用了 {elapsed.Elapsed.TotalSeconds:F2} 秒");
        }
        finally
        {
            cancellation.Cancel();
            await transaction.DisposeAsync();
            if (update is not null)
            {
                // Observe cleanup completion even when the bounded assertion above failed.
                try { await update.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch { }
            }
        }

        await AssertUnchanged(first, initial);
        var next = await second.UpdateAsync(initial.Revision, state => state.Events.Clear());
        Assert.Equal(initial.Revision + 1, next.Revision);
        Assert.Empty(next.State.Events);
    }

    [Fact]
    public async Task MutationExceptionRollsBackAndReleasesTheTransaction()
    {
        var (store, initial) = await PreparedStore();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(initial.Revision, state =>
        {
            state.Events.Clear();
            throw new InvalidOperationException("synthetic mutation failure");
        }));

        await AssertUnchanged(store, initial);
        Assert.Equal(initial.Revision + 1, (await store.UpdateAsync(initial.Revision, state => state.Events.Clear())).Revision);
    }

    [Fact]
    public async Task InvalidBatchMutationRollsBackEveryChange()
    {
        var (store, initial) = await PreparedStore();
        var error = await Assert.ThrowsAsync<AtlasException>(() => store.UpdateAsync(initial.Revision, state =>
        {
            state.Indicators[0] = state.Indicators[0] with { Enabled = false };
            state.Indicators.Add(state.Indicators[0] with { Id = "ind-bad", Name = "Broken synthetic indicator", BusinessIds = ["biz-missing"] });
        }));

        AssertError(400, "INVALID_REFERENCE", error);
        await AssertUnchanged(store, initial);
    }

    [Theory]
    [InlineData("business-space")]
    [InlineData("indicator-business")]
    [InlineData("event-business")]
    [InlineData("screen-business")]
    [InlineData("screen-indicator")]
    public async Task UnknownReferencesAreRejectedWithoutWriting(string relationship)
    {
        var (store, initial) = await PreparedStore();
        var error = await Assert.ThrowsAsync<AtlasException>(() => store.UpdateAsync(initial.Revision, state =>
        {
            switch (relationship)
            {
                case "business-space": state.Businesses[0] = state.Businesses[0] with { SpaceId = "space-missing" }; break;
                case "indicator-business": state.Indicators[0] = state.Indicators[0] with { BusinessIds = ["biz-missing"] }; break;
                case "event-business": state.Events[0] = state.Events[0] with { BusinessId = "biz-missing" }; break;
                case "screen-business": state.Screens[0] = state.Screens[0] with { BusinessId = "biz-missing" }; break;
                case "screen-indicator": state.Screens[0].Panels[0] = state.Screens[0].Panels[0] with { IndicatorId = "ind-missing" }; break;
            }
        }));

        AssertError(400, "INVALID_REFERENCE", error);
        await AssertUnchanged(store, initial);
    }

    [Theory]
    [InlineData("indicator-business")]
    [InlineData("event-business")]
    [InlineData("screen-business")]
    [InlineData("screen-indicator")]
    public async Task CrossSpaceReferencesAreRejectedAtomically(string relationship)
    {
        var (store, initial) = await PreparedStore();
        var error = await Assert.ThrowsAsync<AtlasException>(() => store.UpdateAsync(initial.Revision, state =>
        {
            AddOtherSpace(state);
            switch (relationship)
            {
                case "indicator-business": state.Indicators[0] = state.Indicators[0] with { BusinessIds = ["biz-other"] }; break;
                case "event-business": state.Events[0] = state.Events[0] with { BusinessId = "biz-other" }; break;
                case "screen-business": state.Screens[0] = state.Screens[0] with { BusinessId = "biz-other" }; break;
                case "screen-indicator": state.Screens[0].Panels[0] = state.Screens[0].Panels[0] with { IndicatorId = "ind-other" }; break;
            }
        }));

        AssertError(400, "CROSS_SPACE_REFERENCE", error);
        await AssertUnchanged(store, initial);
    }

    [Theory]
    [InlineData("business")]
    [InlineData("indicator")]
    public async Task RemovingAReferencedResourceReturnsConflict(string entity)
    {
        var (store, initial) = await PreparedStore();
        var error = await Assert.ThrowsAsync<AtlasException>(() => store.UpdateAsync(initial.Revision, state =>
        {
            if (entity == "business") state.Businesses.Clear();
            else state.Indicators.Clear();
        }));

        AssertError(409, "RESOURCE_REFERENCED", error);
        await AssertUnchanged(store, initial);
    }

    [Fact]
    public async Task UnlinkingIndicatorsBeforeDeletionPermitsResourceDeletion()
    {
        var (store, initial) = await PreparedStore();
        var unlinked = await store.UpdateAsync(initial.Revision, state =>
        {
            state.Screens.Clear(); state.Events.Clear();
            state.Indicators[0] = state.Indicators[0] with { BusinessIds = [] };
        });
        var updated = await store.UpdateAsync(unlinked.Revision, state => { state.Indicators.Clear(); state.Businesses.Clear(); });

        Assert.Equal(initial.Revision + 2, updated.Revision);
        Assert.Empty(updated.State.Businesses);
        Assert.Empty(updated.State.Indicators);
        Assert.Single(updated.State.Spaces);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    [InlineData("Uppercase")]
    [InlineData("invalid_id")]
    [InlineData("id with space")]
    [InlineData("id-with-final-newline\n")]
    [InlineData("id-with-final-newline\r\n")]
    public async Task InvalidEntityIdentifiersAreRejected(string id)
    {
        var (store, initial) = await PreparedStore();
        var error = await Assert.ThrowsAsync<AtlasException>(() => store.UpdateAsync(initial.Revision, state => state.Businesses.Add(state.Businesses[0] with { Id = id, Name = "Invalid ID fixture" })));

        AssertError(400, "VALIDATION_FAILED", error);
        await AssertUnchanged(store, initial);
    }

    [Theory]
    [InlineData("space")]
    [InlineData("business")]
    [InlineData("indicator")]
    [InlineData("screen")]
    public async Task DuplicateNamesIgnoreCaseAndSurroundingWhitespace(string entity)
    {
        var (store, initial) = await PreparedStore();
        var error = await Assert.ThrowsAsync<AtlasException>(() => store.UpdateAsync(initial.Revision, state =>
        {
            switch (entity)
            {
                case "space": state.Spaces.Add(state.Spaces[0] with { Id = "space-duplicate", Name = " synthetic SPACE " }); break;
                case "business": state.Businesses.Add(state.Businesses[0] with { Id = "biz-duplicate", Name = " checkout " }); break;
                case "indicator": state.Indicators.Add(state.Indicators[0] with { Id = "ind-duplicate", Name = " REQUESTS ", BusinessIds = [] }); break;
                case "screen": state.Screens.Add(state.Screens[0] with { Id = "screen-duplicate", Name = " CHECKOUT OVERVIEW " }); break;
            }
        }));

        AssertError(409, "DUPLICATE_NAME", error);
        await AssertUnchanged(store, initial);
    }

    [Fact]
    public async Task NamesCanBeReusedAcrossDifferentSpacesAndIndicatorsCanBeUnreferenced()
    {
        var (store, initial) = await PreparedStore();
        var result = await store.UpdateAsync(initial.Revision, state =>
        {
            AddOtherSpace(state);
            state.Businesses[1] = state.Businesses[1] with { Name = state.Businesses[0].Name };
            state.Indicators[1] = state.Indicators[1] with { Name = state.Indicators[0].Name, BusinessIds = [] };
        });

        Assert.Equal(initial.Revision + 1, result.Revision);
        Assert.Empty(result.State.Indicators[1].BusinessIds);
        Assert.Equal(result.State.Indicators[0].Name, result.State.Indicators[1].Name);
    }

    [Theory]
    [InlineData("nul")]
    [InlineData("newline")]
    [InlineData("script")]
    [InlineData("html")]
    [InlineData("oversize")]
    public async Task ControlCharactersHtmlScriptsAndExcessiveTextAreRejected(string value)
    {
        var invalid = value switch
        {
            "nul" => "synthetic\0payload", "newline" => "synthetic\npayload",
            "script" => "<script>alert('synthetic')</script>", "html" => "<img src=x onerror=alert('synthetic')>",
            _ => new string('x', 100_000)
        };
        var (store, initial) = await PreparedStore();
        var error = await Assert.ThrowsAsync<AtlasException>(() => store.UpdateAsync(initial.Revision, state =>
            state.Businesses[0] = value == "newline" ? state.Businesses[0] with { Name = invalid } : state.Businesses[0] with { Description = invalid }));

        AssertError(400, "VALIDATION_FAILED", error);
        await AssertUnchanged(store, initial);
    }

    [Theory]
    [InlineData("availability-low")]
    [InlineData("availability-high")]
    [InlineData("availability-nan")]
    [InlineData("availability-infinity")]
    [InlineData("visibility")]
    [InlineData("source")]
    [InlineData("behavior")]
    [InlineData("level")]
    [InlineData("event-order")]
    [InlineData("event-default-time")]
    [InlineData("columns")]
    [InlineData("panel-kind")]
    [InlineData("panel-aggregation")]
    [InlineData("empty-panels")]
    public async Task InvalidNumbersEnumsAndTimesAreRejected(string field)
    {
        var (store, initial) = await PreparedStore();
        var error = await Assert.ThrowsAsync<AtlasException>(() => store.UpdateAsync(initial.Revision, state =>
        {
            switch (field)
            {
                case "availability-low": state.Businesses[0] = state.Businesses[0] with { TargetAvailability = 89.9 }; break;
                case "availability-high": state.Businesses[0] = state.Businesses[0] with { TargetAvailability = 100 }; break;
                case "availability-nan": state.Businesses[0] = state.Businesses[0] with { TargetAvailability = double.NaN }; break;
                case "availability-infinity": state.Businesses[0] = state.Businesses[0] with { TargetAvailability = double.PositiveInfinity }; break;
                case "visibility": state.Indicators[0] = state.Indicators[0] with { Visibility = "private" }; break;
                case "source": state.Indicators[0] = state.Indicators[0] with { SourceId = "production" }; break;
                case "behavior": state.Indicators[0] = state.Indicators[0] with { Behavior = "fabricate" }; break;
                case "level": state.Events[0] = state.Events[0] with { Level = "P0" }; break;
                case "event-order": state.Events[0] = state.Events[0] with { EndedAt = state.Events[0].StartedAt.AddSeconds(-1) }; break;
                case "event-default-time": state.Events[0] = state.Events[0] with { StartedAt = default }; break;
                case "columns": state.Screens[0] = state.Screens[0] with { Columns = 4 }; break;
                case "panel-kind": state.Screens[0].Panels[0] = state.Screens[0].Panels[0] with { Kind = "pie" }; break;
                case "panel-aggregation": state.Screens[0].Panels[0] = state.Screens[0].Panels[0] with { Aggregation = "median" }; break;
                case "empty-panels": state.Screens[0].Panels.Clear(); break;
            }
        }));

        AssertError(400, "VALIDATION_FAILED", error);
        await AssertUnchanged(store, initial);
    }

    [Fact]
    public async Task ScreenIndicatorMustBelongToTheSelectedBusiness()
    {
        var (store, initial) = await PreparedStore();
        var error = await Assert.ThrowsAsync<AtlasException>(() => store.UpdateAsync(initial.Revision, state => state.Indicators[0] = state.Indicators[0] with { BusinessIds = [] }));

        AssertError(400, "INVALID_REFERENCE", error);
        await AssertUnchanged(store, initial);
    }

    [Fact]
    public async Task SqlLikeTextIsPersistedAsDataWithoutChangingTheSnapshotTable()
    {
        const string text = "synthetic'); DROP TABLE atlas_snapshot; --";
        var (store, initial) = await PreparedStore();
        var saved = await store.UpdateAsync(initial.Revision, state => state.Businesses[0] = state.Businesses[0] with { Description = text });

        Assert.Equal(text, saved.State.Businesses[0].Description);
        var reopened = new AtlasStore(database);
        await reopened.InitializeAsync();
        Assert.Equal(text, (await reopened.ReadAsync()).State.Businesses[0].Description);
        using var connection = new SqliteConnection($"Data Source={database}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM atlas_snapshot";
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnsupportedPayloadSchemaIsRejected(int version)
    {
        var invalid = Fixture() with { SchemaVersion = version };
        AssertError(400, "UNSUPPORTED_SCHEMA", Assert.Throws<AtlasException>(() => AtlasValidation.Validate(invalid)));
    }

    [Theory]
    [InlineData("pragma")]
    [InlineData("column")]
    public async Task FutureDatabaseSchemaIsRejectedWithoutOverwritingTheSnapshot(string location)
    {
        var (store, initial) = await PreparedStore();
        using (var connection = new SqliteConnection($"Data Source={database}"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = location == "pragma" ? "PRAGMA user_version=2" : "UPDATE atlas_snapshot SET schema_version=2 WHERE id=1";
            await command.ExecuteNonQueryAsync();
        }

        var error = await Assert.ThrowsAsync<AtlasException>(async () =>
        {
            var reopened = new AtlasStore(database);
            await reopened.InitializeAsync();
            await reopened.ReadAsync();
        });
        AssertError(500, "UNSUPPORTED_SCHEMA", error);
        using var verification = new SqliteConnection($"Data Source={database}");
        await verification.OpenAsync();
        using var query = verification.CreateCommand();
        query.CommandText = "SELECT revision FROM atlas_snapshot WHERE id=1";
        Assert.Equal(initial.Revision, (long)(await query.ExecuteScalarAsync())!);
    }

    [Fact]
    public void DemoSeedMeetsTheSyntheticContractAndAllReferencesAreValid()
    {
        var seed = DemoSeed.Create(Now);
        AtlasValidation.Validate(seed);
        Assert.True(seed.Spaces.Count >= 3);
        Assert.True(seed.Businesses.Count >= 6);
        Assert.True(seed.Indicators.Count >= 18);
        Assert.True(seed.Screens.Count >= 3);
        Assert.Contains(seed.Businesses, business => business.Id == "biz-checkout");
        Assert.Equal(13, seed.Indicators.Count(indicator => indicator.BusinessIds.Contains("biz-checkout")));
        Assert.Contains(seed.Indicators, indicator => indicator.Id == "ind-empty" && indicator.Behavior == "empty");
        Assert.Contains(seed.Events, incident => incident.EndedAt is null);
        Assert.Contains(seed.Events, incident => incident.EndedAt is not null);
        Assert.All(seed.Events, incident => Assert.True(incident.StartedAt <= Now));
    }

    private static async Task AssertUnchanged(AtlasStore store, StoreSnapshot initial)
    {
        var current = await store.ReadAsync();
        Assert.Equal(initial.Revision, current.Revision);
        Assert.Equal(StateJson(initial.State), StateJson(current.State));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
