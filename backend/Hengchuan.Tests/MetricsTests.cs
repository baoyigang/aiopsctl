using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Hengchuan.Api;
using Xunit;

namespace Hengchuan.Tests;

public sealed class MetricsTests
{
    private static readonly DateTimeOffset Now = At("2026-10-17T10:00:00Z");
    private static readonly DateTimeOffset Start = At("2026-10-17T08:00:00Z");
    private static DateTimeOffset At(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
    private static Business Business() => new()
    {
        Id = "biz-metrics", SpaceId = "space-metrics", Name = "Synthetic metrics business",
        Owner = "synthetic-owner", Description = "隔离合成指标测试", Service = "checkout", Environment = "demo",
        TargetAvailability = 99, UpdatedAt = Now
    };
    private static Indicator Indicator(string id = "ind-metrics") => new()
    {
        Id = id, SpaceId = "space-metrics", Name = "Synthetic requests", SourceId = "demo", SourceName = "Synthetic fixture",
        MetricName = "requests", Unit = "次", Owner = "synthetic-owner", Enabled = true, Visibility = "space",
        BusinessIds = ["biz-metrics"], Behavior = "normal", UpdatedAt = Now, UpdatedBy = "demo-operator"
    };
    private static AtlasState State(params Indicator[] indicators) => new()
    {
        Spaces = [new Space { Id = "space-metrics", Name = "Metrics synthetic space" }],
        Businesses = [Business()], Indicators = indicators.ToList()
    };
    private static IncidentEvent Incident(string id, double fromMinute, double? toMinute, string level = "P2", string businessId = "biz-metrics") => new()
    {
        Id = id, SpaceId = "space-metrics", BusinessId = businessId, Title = "Synthetic incident " + id, Level = level,
        StartedAt = Start.AddMinutes(fromMinute), EndedAt = toMinute is { } end ? Start.AddMinutes(end) : null,
        Description = "合成事件，不来自实际企业系统"
    };
    private static TimeWindow Hour() => new(Start, Start.AddHours(1), "Synthetic hour");
    private static void Near(double expected, double actual) => Assert.Equal(expected, actual, 8);

    [Fact]
    public void AvailabilityUsesIntervalUnionAndCountsIntersectingEventsSeparately()
    {
        var result = MetricsService.Calculate(Business(), [
            Incident("event-overlap-a", 0, 20), Incident("event-overlap-b", 10, 30), Incident("event-ongoing", 45, null)
        ], Hour());

        Near(45, result.ConsumedMinutes);
        Near(25, result.Availability!.Value);
        Near(0.6, result.QuotaMinutes);
        Near(0, result.RemainingMinutes);
        Near(7500, result.ConsumedPercent);
        Assert.Equal(3, result.EventCount);
        Near(20, result.MttrMinutes!.Value);
        Near(5, result.MtbfMinutes!.Value);
    }

    [Fact]
    public void AvailabilityClipsWindowEdgesAndExcludesTouchingAndOtherBusinessEvents()
    {
        var result = MetricsService.Calculate(Business(), [
            Incident("event-left", -20, 10), Incident("event-right", 50, 90),
            Incident("event-ended-before", -20, 0), Incident("event-started-after", 60, 80),
            Incident("event-other-business", 0, 60, businessId: "biz-other")
        ], Hour());

        Near(20, result.ConsumedMinutes);
        Near(100 * 2.0 / 3, result.Availability!.Value);
        Assert.Equal(2, result.EventCount);
        Near(10, result.MttrMinutes!.Value);
        Near(20, result.MtbfMinutes!.Value);
    }

    [Fact]
    public void ContainedAndAdjacentIntervalsNeverDoubleCountDowntime()
    {
        var result = MetricsService.Calculate(Business(), [
            Incident("event-wide", 10, 40), Incident("event-inside", 20, 25), Incident("event-adjacent", 40, 50)
        ], Hour());
        Near(40, result.ConsumedMinutes);
        Assert.Equal(3, result.EventCount);
        Near(15, result.MttrMinutes!.Value);
    }

    [Fact]
    public void OngoingIncidentIsCappedAtWindowEndAndHasNoResolvedMttr()
    {
        var result = MetricsService.Calculate(Business(), [Incident("event-ongoing", -30, null)], Hour());
        Near(60, result.ConsumedMinutes);
        Near(0, result.Availability!.Value);
        Assert.Equal(1, result.EventCount);
        Assert.Null(result.MttrMinutes);
        Near(0, result.MtbfMinutes!.Value);
    }

    [Fact]
    public void NoEventsHasObservedAvailabilityAndNoInventedRepairStatistics()
    {
        var result = MetricsService.Calculate(Business(), [], Hour());
        Near(100, result.Availability!.Value);
        Near(0, result.ConsumedMinutes);
        Near(result.QuotaMinutes, result.RemainingMinutes);
        Near(0, result.ConsumedPercent);
        Assert.Equal(0, result.EventCount);
        Assert.Null(result.MttrMinutes);
        Assert.Null(result.MtbfMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NoObservationWindowNeverInventsHealthyAvailability(int durationMinutes)
    {
        var result = MetricsService.Calculate(Business(), [Incident("event-any", -10, null)], new(Start, Start.AddMinutes(durationMinutes), "No observed time"));
        Assert.Null(result.Availability);
        Near(0, result.QuotaMinutes);
        Near(0, result.ConsumedMinutes);
        Near(0, result.RemainingMinutes);
        Assert.Equal(0, result.EventCount);
        Assert.Null(result.MttrMinutes);
        Assert.Null(result.MtbfMinutes);
    }

    [Fact]
    public void SelectedLevelChangesEveryEventDerivedStatistic()
    {
        IncidentEvent[] events = [Incident("event-p1", 0, 10, "P1"), Incident("event-p2", 20, 40, "P2")];
        var all = MetricsService.Calculate(Business(), events, Hour());
        var selected = MetricsService.Calculate(Business(), events.Where(e => e.Level == "P1"), Hour());
        Near(30, all.ConsumedMinutes);
        Near(10, selected.ConsumedMinutes);
        Near(50, all.Availability!.Value);
        Near(100 * 5.0 / 6, selected.Availability!.Value);
        Assert.Equal(2, all.EventCount);
        Assert.Equal(1, selected.EventCount);
        Near(15, all.MttrMinutes!.Value);
        Near(10, selected.MttrMinutes!.Value);
        Near(15, all.MtbfMinutes!.Value);
        Near(50, selected.MtbfMinutes!.Value);
    }

    [Fact]
    public void DifferentObservationWindowsKeepTheirOwnDenominatorAndClippedDuration()
    {
        var incident = Incident("event-partial", 30, 90);
        var oneHour = MetricsService.Calculate(Business(), [incident], Hour());
        var twoHours = MetricsService.Calculate(Business(), [incident], new(Start, Start.AddHours(2), "Two hours"));
        Near(30, oneHour.ConsumedMinutes);
        Near(60, twoHours.ConsumedMinutes);
        Near(0.6, oneHour.QuotaMinutes);
        Near(1.2, twoHours.QuotaMinutes);
        Near(30, oneHour.MttrMinutes!.Value);
        Near(60, twoHours.MttrMinutes!.Value);
    }

    [Fact]
    public void SummaryCountsAllBusinessIndicatorsIncludingEmptyAndDisabled()
    {
        var result = MetricsService.Summary(Business(), [], [Indicator(), Indicator("ind-empty") with { Behavior = "empty" }, Indicator("ind-disabled") with { Enabled = false }, Indicator("ind-other") with { BusinessIds = ["biz-other"] }], Hour());
        var json = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(3, json.GetProperty("chartCount").GetInt32());
        Assert.Equal("biz-metrics", json.GetProperty("id").GetString());
        Assert.Equal(100, json.GetProperty("availability").GetDouble());
    }

    [Theory]
    [InlineData(2026, "year", "2026-06-15", "2026-01-01T00:00:00Z", "2026-10-17T10:00:00Z")]
    [InlineData(2024, "year", "2024-06-15", "2024-01-01T00:00:00Z", "2025-01-01T00:00:00Z")]
    [InlineData(2026, "month", "2026-02-15", "2026-02-01T00:00:00Z", "2026-03-01T00:00:00Z")]
    [InlineData(2026, "month", "2026-10-01", "2026-10-01T00:00:00Z", "2026-10-17T10:00:00Z")]
    [InlineData(2026, "week", "2026-01-01", "2026-01-01T00:00:00Z", "2026-01-05T00:00:00Z")]
    [InlineData(2025, "week", "2025-12-31", "2025-12-29T00:00:00Z", "2026-01-01T00:00:00Z")]
    [InlineData(2026, "week", "2026-10-14", "2026-10-12T00:00:00Z", "2026-10-17T10:00:00Z")]
    [InlineData(2026, "day", "2026-10-15", "2026-10-15T00:00:00Z", "2026-10-16T00:00:00Z")]
    [InlineData(2026, "day", "2026-10-17", "2026-10-17T00:00:00Z", "2026-10-17T10:00:00Z")]
    public void StabilityUsesNaturalUtcWindowsClippedBySelectedYearAndNow(int year, string period, string anchor, string from, string to)
    {
        var result = MetricsService.StabilityWindow(year, period, anchor, Now);
        Assert.Equal(At(from), result.From);
        Assert.Equal(At(to), result.To);
        Assert.Equal("UTC", result.Timezone);
        Assert.Equal(TimeSpan.Zero, result.From.Offset);
        Assert.Equal(TimeSpan.Zero, result.To.Offset);
    }

    [Theory]
    [InlineData(2027, "year", null)]
    [InlineData(2026, "month", "2026-11-15")]
    [InlineData(2026, "day", "2026-10-18")]
    public void FutureStabilityPeriodIsAnEmptyObservationWindow(int year, string period, string? anchor)
    {
        var window = MetricsService.StabilityWindow(year, period, anchor, Now);
        Assert.Equal(window.From, window.To);
        Assert.Null(MetricsService.Calculate(Business(), [], window).Availability);
    }

    [Theory]
    [InlineData(2019, "year", null, "invalid_year")]
    [InlineData(2101, "year", null, "invalid_year")]
    [InlineData(2026, "quarter", null, "invalid_period")]
    [InlineData(2026, "day", "2025-12-31", "invalid_anchor")]
    [InlineData(2026, "day", "2026-02-30", "invalid_anchor")]
    [InlineData(2026, "day", "17/10/2026", "invalid_anchor")]
    public void StabilityRejectsInvalidYearPeriodAndAnchor(int year, string period, string? anchor, string code)
    {
        var exception = Assert.Throws<AtlasException>(() => MetricsService.StabilityWindow(year, period, anchor, Now));
        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public void StabilityDefaultAnchorUsesCurrentDateOrSelectedPastYearEnd()
    {
        var currentDay = MetricsService.StabilityWindow(2026, "day", null, Now);
        Assert.Equal(At("2026-10-17T00:00:00Z"), currentDay.From);
        Assert.Equal(Now, currentDay.To);
        var pastDay = MetricsService.StabilityWindow(2024, "day", null, Now);
        Assert.Equal(At("2024-12-31T00:00:00Z"), pastDay.From);
        Assert.Equal(At("2025-01-01T00:00:00Z"), pastDay.To);
    }

    [Fact]
    public void DashboardWindowDefaultsToTwelveHoursAndNormalizesOffsets()
    {
        using var harness = new Harness();
        var window = harness.Metrics.DashboardWindow(null, null);
        Assert.Equal(Now.AddHours(-12), window.From);
        Assert.Equal(Now, window.To);
        var offsetWindow = harness.Metrics.DashboardWindow(At("2026-10-17T15:00:00+08:00"), At("2026-10-17T17:00:00+08:00"));
        Assert.Equal(At("2026-10-17T07:00:00Z"), offsetWindow.From);
        Assert.Equal(At("2026-10-17T09:00:00Z"), offsetWindow.To);
        Assert.Equal(TimeSpan.Zero, offsetWindow.From.Offset);
        Assert.Equal(TimeSpan.Zero, offsetWindow.To.Offset);
        Assert.Equal(TimeSpan.FromDays(7), harness.Metrics.DashboardWindow(Now.AddDays(-7), Now).To - harness.Metrics.DashboardWindow(Now.AddDays(-7), Now).From);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(-10081, 0)]
    [InlineData(-60, 0.5)]
    public void DashboardRejectsEmptyReversedOversizeAndFutureWindows(double fromMinutes, double toMinutes)
    {
        using var harness = new Harness();
        var exception = Assert.Throws<AtlasException>(() => harness.Metrics.DashboardWindow(Now.AddMinutes(fromMinutes), Now.AddMinutes(toMinutes)));
        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("invalid_window", exception.Code);
    }

    [Fact]
    public async Task RawSamplingIsFiveMinutesAndUsesAHalfOpenWindow()
    {
        using var harness = new Harness();
        var chart = await harness.Metrics.ChartAsync(Indicator(), Business(), Hour(), "raw", "none", default);
        Assert.Equal(12, chart.Points.Count);
        Assert.Equal(Start, chart.Points[0].Timestamp);
        Assert.Equal(Start.AddMinutes(55), chart.Points[^1].Timestamp);
        Assert.All(chart.Points, p => Assert.True(p.Timestamp >= Start && p.Timestamp < Start.AddHours(1)));
        Assert.All(chart.Points.Zip(chart.Points.Skip(1)), pair => Assert.Equal(TimeSpan.FromMinutes(5), pair.Second.Timestamp - pair.First.Timestamp));
        Assert.Equal(chart.Points[^1].Timestamp, chart.Stats.LastUpdated);
        Assert.Null(chart.EmptyReason);
        Assert.Null(chart.Error);
        Assert.Equal(0, harness.Handler.Calls);
    }

    [Fact]
    public async Task AvgAndSumAggregateTheSameRawSamplesWithoutInventingMissingData()
    {
        using var harness = new Harness();
        var metric = Indicator();
        var raw = await harness.Metrics.ChartAsync(metric, Business(), Hour(), "raw", "none", default);
        var average = await harness.Metrics.ChartAsync(metric, Business(), Hour(), "avg", "none", default);
        var sum = await harness.Metrics.ChartAsync(metric, Business(), Hour(), "sum", "none", default);
        Assert.Equal(2, average.Points.Count);
        Assert.Equal(2, sum.Points.Count);
        for (var index = 0; index < 2; index++)
        {
            var samples = raw.Points.Skip(index * 6).Take(6).Select(p => p.Value).ToArray();
            Near(samples.Average(), average.Points[index].Value);
            Near(samples.Sum(), sum.Points[index].Value);
            Assert.Equal(Start.AddMinutes(index * 30), average.Points[index].Timestamp);
        }
        Near(raw.Stats.Sum!.Value, sum.Stats.Sum!.Value);
        Near(raw.Stats.Average!.Value, average.Stats.Average!.Value);
    }

    [Fact]
    public async Task LongWindowUsesSixHourAggregationAndRetainsFiveMinuteRawSampling()
    {
        using var harness = new Harness();
        var window = new TimeWindow(Now.AddHours(-30), Now, "Synthetic thirty hours");
        var raw = await harness.Metrics.ChartAsync(Indicator(), Business(), window, "raw", "none", default);
        var aggregate = await harness.Metrics.ChartAsync(Indicator(), Business(), window, "sum", "none", default);
        Assert.Equal(360, raw.Points.Count);
        Assert.InRange(aggregate.Points.Count, 5, 6);
        Assert.All(aggregate.Points, p => Assert.True(p.Timestamp >= window.From && p.Timestamp < window.To));
        Near(raw.Points.Sum(p => p.Value), aggregate.Points.Sum(p => p.Value));
    }

    [Theory]
    [InlineData("yesterday", 1)]
    [InlineData("lastWeek", 7)]
    public async Task ComparisonAlignsPriorSamplesAndReportsTheAverageDelta(string comparison, int days)
    {
        using var harness = new Harness();
        var indicator = Indicator();
        var window = Hour();
        var compared = await harness.Metrics.ChartAsync(indicator, Business(), window, "raw", comparison, default);
        var previous = await harness.Metrics.ChartAsync(indicator, Business(), window with { From = window.From.AddDays(-days), To = window.To.AddDays(-days) }, "raw", "none", default);
        Assert.Equal(previous.Points.Count, compared.Points.Count);
        for (var index = 0; index < compared.Points.Count; index++)
        {
            Assert.Equal(previous.Points[index].Timestamp.AddDays(days), compared.Points[index].Timestamp);
            Assert.Equal(previous.Points[index].Value, compared.Points[index].ComparisonValue);
        }
        var expectedDelta = (compared.Stats.Average!.Value - previous.Stats.Average!.Value) / Math.Abs(previous.Stats.Average.Value) * 100;
        Near(expectedDelta, compared.Stats.ComparisonDeltaPercent!.Value);
    }

    [Fact]
    public async Task EmptyAndDisabledIndicatorsHaveNoFakeZeroPointsOrStatistics()
    {
        using var harness = new Harness();
        foreach (var indicator in new[] { Indicator("ind-empty") with { Behavior = "empty" }, Indicator("ind-disabled") with { Enabled = false } })
        {
            var chart = await harness.Metrics.ChartAsync(indicator, Business(), Hour(), "avg", "yesterday", default);
            Assert.Empty(chart.Points);
            Assert.NotNull(chart.EmptyReason);
            Assert.Null(chart.Error);
            AssertEmptyStats(chart.Stats);
        }
        Assert.Equal(0, harness.Handler.Calls);
    }

    [Fact]
    public async Task WindowWithoutAlignedSampleReturnsRealEmptyState()
    {
        using var harness = new Harness();
        var chart = await harness.Metrics.ChartAsync(Indicator(), Business(), new(Start.AddSeconds(1), Start.AddSeconds(2), "No sample"), "raw", "none", default);
        Assert.Empty(chart.Points);
        Assert.NotNull(chart.EmptyReason);
        AssertEmptyStats(chart.Stats);
    }

    [Fact]
    public async Task DashboardShowsOnlyTheSelectedSpaceAndBusinessAndKeepsItsEmptyChart()
    {
        using var harness = new Harness();
        var included = Indicator();
        var empty = Indicator("ind-empty") with { Behavior = "empty" };
        var unreferenced = Indicator("ind-unreferenced") with { BusinessIds = [] };
        var otherBusiness = Indicator("ind-other-business") with { BusinessIds = ["biz-other"] };
        var otherSpace = Indicator("ind-other-space") with { SpaceId = "space-other" };
        var dashboard = await harness.Metrics.DashboardAsync(State(included, empty, unreferenced, otherBusiness, otherSpace), Business(), Hour(), "avg", "none", default);
        Assert.Equal("demo", dashboard.Mode);
        Assert.Equal("biz-metrics", dashboard.BusinessId);
        Assert.Equal(["ind-metrics", "ind-empty"], dashboard.Charts.Select(c => c.IndicatorId).ToArray());
        Assert.NotEmpty(dashboard.Charts[0].Points);
        Assert.Empty(dashboard.Charts[1].Points);
        Assert.NotNull(dashboard.Charts[1].EmptyReason);
    }

    [Theory]
    [InlineData("median", "none")]
    [InlineData("raw", "tomorrow")]
    public async Task DashboardRejectsUnsupportedSeriesOptions(string aggregation, string comparison)
    {
        using var harness = new Harness();
        var exception = await Assert.ThrowsAsync<AtlasException>(() => harness.Metrics.DashboardAsync(State(Indicator()), Business(), Hour(), aggregation, comparison, default));
        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("invalid_series_options", exception.Code);
        Assert.Equal(0, harness.Handler.Calls);
    }

    [Fact]
    public async Task CallerCancellationPropagatesEvenForEmptyOrDisabledOrNoIndicators()
    {
        using var harness = new Harness();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Metrics.DashboardAsync(State(), Business(), Hour(), "avg", "none", cancellation.Token));
        foreach (var indicator in new[] { Indicator(), Indicator("ind-empty") with { Behavior = "empty" }, Indicator("ind-disabled") with { Enabled = false } })
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Metrics.ChartAsync(indicator, Business(), Hour(), "raw", "none", cancellation.Token));
        Assert.Equal(0, harness.Handler.Calls);
    }

    [Fact]
    public async Task DisabledElasticsearchNeverFallsBackToSyntheticPoints()
    {
        using var harness = new Harness(new DataSourceOptions { AllowedIndices = ["synthetic-metrics"] });
        var indicator = Indicator() with { SourceId = "es-main" };
        var dashboard = await harness.Metrics.DashboardAsync(State(indicator), Business(), Hour(), "avg", "none", default);
        var chart = Assert.Single(dashboard.Charts);
        Assert.NotEqual("demo", dashboard.Mode);
        Assert.Equal("es-main", chart.SourceId);
        Assert.Empty(chart.Points);
        Assert.NotNull(chart.Error);
        Assert.Null(chart.EmptyReason);
        AssertEmptyStats(chart.Stats);
        Assert.Equal(0, harness.Handler.Calls);
    }

    [Fact]
    public async Task ElasticsearchRawUnsupportedDoesNotMasqueradeAggregatesAsRaw()
    {
        using var harness = new Harness();
        var chart = await harness.Metrics.ChartAsync(Indicator() with { SourceId = "es-main" }, Business(), Hour(), "raw", "none", default);
        Assert.Empty(chart.Points);
        Assert.Contains("raw", chart.Error!, StringComparison.OrdinalIgnoreCase);
        AssertEmptyStats(chart.Stats);
        Assert.Equal(0, harness.Handler.Calls);
    }

    [Fact]
    public async Task PercentDemoSeriesIsFiniteAndWithinItsSemanticRange()
    {
        using var harness = new Harness();
        var window = new TimeWindow(Now.AddDays(-7), Now, "Percent fixture");
        var chart = await harness.Metrics.ChartAsync(Indicator() with { Unit = "%" }, Business(), window, "raw", "none", default);
        Assert.NotEmpty(chart.Points);
        Assert.All(chart.Points, p => { Assert.True(double.IsFinite(p.Value)); Assert.InRange(p.Value, 0, 100); });
    }

    [Fact]
    public async Task ElasticsearchKnownSeriesStatisticsUseOnlyReturnedSamples()
    {
        using var harness = EsHarness(_ => [2, 6]);
        var chart = await harness.Metrics.ChartAsync(Indicator() with { SourceId = "es-main" }, Business(), Hour(), "avg", "none", default);
        Assert.Null(chart.Error);
        Assert.Equal(2, chart.Points.Count);
        Near(8, chart.Stats.Sum!.Value);
        Near(2, chart.Stats.Min!.Value);
        Near(6, chart.Stats.Max!.Value);
        Near(4, chart.Stats.Average!.Value);
        Assert.Equal(Start.AddMinutes(30), chart.Stats.LastUpdated);
        Assert.Null(chart.Stats.ComparisonDeltaPercent);
        Assert.Equal(2, harness.Handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FiniteElasticsearchInputsThatOverflowStatisticsBecomeExplicitChartErrors(bool comparisonOverflow)
    {
        using var harness = comparisonOverflow
            ? EsHarness(search => search == 1 ? [1e308] : [1e-300])
            : EsHarness(_ => [1e308, 1e308]);
        var chart = await harness.Metrics.ChartAsync(Indicator() with { SourceId = "es-main" }, Business(), Hour(), "avg", comparisonOverflow ? "yesterday" : "none", default);
        Assert.Empty(chart.Points);
        Assert.NotNull(chart.Error);
        Assert.Contains("数值统计", chart.Error);
        Assert.Equal(comparisonOverflow ? 4 : 2, harness.Handler.Calls);
        AssertEmptyStats(chart.Stats);
        _ = JsonSerializer.Serialize(chart);
    }

    private static void AssertEmptyStats(ChartStats stats)
    {
        Assert.Null(stats.Sum); Assert.Null(stats.Max); Assert.Null(stats.Min); Assert.Null(stats.Average);
        Assert.Null(stats.LastUpdated); Assert.Null(stats.ComparisonDeltaPercent);
    }

    private static Harness EsHarness(Func<int, double[]> values)
    {
        var options = new DataSourceOptions
        {
            Enabled = true, Endpoint = "https://synthetic-es.invalid", AllowedIndices = ["synthetic-metrics"],
            AllowedServices = ["checkout"], AllowedEnvironments = ["demo"], MetricFields = new() { ["requests"] = "metric.value" }
        };
        var search = 0;
        return new Harness(options, async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/_field_caps", StringComparison.Ordinal))
                return Response("""
                    {"indices":["synthetic-metrics"],"fields":{
                      "metric.value":{"double":{"type":"double","aggregatable":true}},
                      "service.name":{"keyword":{"type":"keyword","searchable":true}},
                      "labels.environment":{"keyword":{"type":"keyword","searchable":true}},
                      "@timestamp":{"date":{"type":"date","searchable":true}}
                    }}
                    """);
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            using (body)
            {
                var range = body.RootElement.GetProperty("query").GetProperty("bool").GetProperty("filter")[2].GetProperty("range").GetProperty("@timestamp");
                var from = At(range.GetProperty("gte").GetString()!);
                var samples = values(++search);
                var buckets = samples.Select((value, index) => new
                {
                    key = from.AddMinutes(index * 30).ToUnixTimeMilliseconds(), doc_count = 1,
                    metric_value = new { value }, sample_count = new { value = 1 }
                });
                return Response(JsonSerializer.Serialize(new { timed_out = false, _shards = new { failed = 0 }, aggregations = new { timeline = new { buckets } } }));
            }
        });
    }
    private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return callback(request, cancellationToken); }
    }
    private sealed class Harness : IDisposable
    {
        private readonly HttpClient http;
        private readonly ElasticsearchAdapter adapter;
        public FakeHandler Handler { get; }
        public MetricsService Metrics { get; }
        public Harness(DataSourceOptions? options = null, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? callback = null)
        {
            options ??= new DataSourceOptions();
            Handler = new FakeHandler(callback ?? ((_, _) => throw new InvalidOperationException("Synthetic metrics must not send network requests")));
            http = new HttpClient(Handler);
            adapter = new ElasticsearchAdapter(options, http);
            Metrics = new MetricsService(adapter, options, new FixedClock(Now));
        }
        public void Dispose() { adapter.Dispose(); http.Dispose(); }
    }
}
