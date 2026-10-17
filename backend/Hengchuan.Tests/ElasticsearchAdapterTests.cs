using System.Net;
using System.Text;
using System.Text.Json;
using Hengchuan.Api;
using Xunit;

namespace Hengchuan.Tests;

public sealed class ElasticsearchAdapterTests
{
    private const string Index = "metrics-2026.10";
    private const string Field = "system.cpu.total.pct";
    private static readonly DateTimeOffset From = DateTimeOffset.Parse("2026-10-10T08:00:00Z");
    private static DataSourceOptions Options() => new()
    {
        Enabled = true, Endpoint = "https://synthetic-es.invalid", AllowedIndices = [Index],
        AllowedServices = ["checkout"], AllowedEnvironments = ["test"], MetricFields = new() { ["cpu"] = Field }
    };
    private static EsSeriesQuery Query() => new(Index, "checkout", "test", "cpu", From, From.AddHours(1));
    private static HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static string Mapping(string type = "double", bool aggregatable = true, string index = Index, string scopeType = "keyword") => JsonSerializer.Serialize(new
    {
        indices = new[] { index },
        fields = new Dictionary<string, object>
        {
            [Field] = new Dictionary<string, object> { [type] = new { type, aggregatable } },
            ["service.name"] = new Dictionary<string, object> { [scopeType] = new { type = scopeType, searchable = true } },
            ["labels.environment"] = new Dictionary<string, object> { ["keyword"] = new { type = "keyword", searchable = true } },
            ["@timestamp"] = new Dictionary<string, object> { ["date"] = new { type = "date", searchable = true } }
        }
    });
    private static object Bucket(int minute, double? value = 2.5, long samples = 1, long documents = 1) => new
    {
        key = From.AddMinutes(minute).ToUnixTimeMilliseconds(), doc_count = documents,
        metric_value = new { value }, sample_count = new { value = samples }
    };
    private static string Series(params object[] buckets) => JsonSerializer.Serialize(new
    { timed_out = false, _shards = new { failed = 0 }, aggregations = new { timeline = new { buckets } } });
    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; return callback(request, ct); }
    }
    private static ElasticsearchAdapter Adapter(FakeHandler handler, DataSourceOptions? options = null) => new(options ?? Options(), new HttpClient(handler));
    private static FakeHandler WithSeries(string json) => new((request, _) => Task.FromResult(Response(request.RequestUri!.AbsolutePath.EndsWith("/_field_caps") ? Mapping() : json)));

    [Fact]
    public async Task DisabledByDefaultAndMissingConfigNeverSendOrReturnDemo()
    {
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException("Must not send"));
        using var disabled = Adapter(handler, new DataSourceOptions());
        var error = await Assert.ThrowsAsync<EsDataSourceException>(() => disabled.QueryAsync(Query(), default));
        Assert.Equal(503, error.StatusCode);
        Assert.Equal("es_disabled", error.Code);
        var missing = Options(); missing.Endpoint = null;
        using var notConfigured = Adapter(handler, missing);
        Assert.Equal(503, (await Assert.ThrowsAsync<EsDataSourceException>(() => notConfigured.QueryAsync(Query(), default))).StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("*", "checkout", "test", "cpu")]
    [InlineData("metrics-*", "checkout", "test", "cpu")]
    [InlineData("metrics-2026.10,other", "checkout", "test", "cpu")]
    [InlineData("../other", "checkout", "test", "cpu")]
    [InlineData(Index, "payments", "test", "cpu")]
    [InlineData(Index, "checkout", "production", "cpu")]
    [InlineData(Index, "checkout", "test", "script")]
    public async Task ServerScopeAndMetricAllowListsAreEnforced(string index, string service, string environment, string metric)
    {
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException("Must not send"));
        using var adapter = Adapter(handler);
        var query = Query() with { Index = index, Service = service, Environment = environment, Metric = metric };
        Assert.Equal(403, (await Assert.ThrowsAsync<EsDataSourceException>(() => adapter.QueryAsync(query, default))).StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(0, 300)]
    [InlineData(-1, 300)]
    [InlineData(193, 300)]
    [InlineData(1, 0)]
    [InlineData(1, 86401)]
    [InlineData(1, 1)]
    public async Task FixedWindowsAndBucketCountAreBounded(int hours, int interval)
    {
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException("Must not send"));
        using var adapter = Adapter(handler);
        var query = Query() with { To = From.AddHours(hours), IntervalSeconds = interval };
        Assert.Equal(400, (await Assert.ThrowsAsync<EsDataSourceException>(() => adapter.QueryAsync(query, default))).StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("raw")]
    [InlineData("script")]
    public async Task RawAndScriptsAreExplicitlyUnsupported(string aggregation)
    {
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException("Must not send"));
        using var adapter = Adapter(handler);
        Assert.Equal(422, (await Assert.ThrowsAsync<EsDataSourceException>(() => adapter.QueryAsync(Query() with { Aggregation = aggregation }, default))).StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("http://localhost:9200")]
    [InlineData("http://internal.invalid:9200")]
    [InlineData("https://user:FAKE_CONFIG_SECRET@synthetic-es.invalid")]
    [InlineData("https://synthetic-es.invalid?token=FAKE_CONFIG_SECRET")]
    [InlineData("https://synthetic-es.invalid/#fragment")]
    public async Task UnsafeEndpointsAreRejectedWithoutLeakingConfiguration(string endpoint)
    {
        var options = Options(); options.Endpoint = endpoint;
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException("Must not send"));
        using var adapter = Adapter(handler, options);
        var error = await Assert.ThrowsAsync<EsDataSourceException>(() => adapter.QueryAsync(Query(), default));
        Assert.Equal(503, error.StatusCode);
        Assert.DoesNotContain("FAKE_CONFIG_SECRET", error.Message);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("http://127.0.0.1:9200")]
    [InlineData("http://[::1]:9200")]
    public async Task LiteralLoopbackHttpIsSupportedForLocalTesting(string endpoint)
    {
        var options = Options(); options.Endpoint = endpoint;
        var handler = WithSeries(Series());
        using var adapter = Adapter(handler, options);
        Assert.Empty((await adapter.QueryAsync(Query(), default)).Points);
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData("avg")]
    [InlineData("sum")]
    public async Task BuildsOnlyFixedReadOnlyAggregationAndPreservesGaps(string aggregation)
    {
        var options = Options(); options.ApiKey = "SYNTHETIC_KEY_ONLY";
        var handler = new FakeHandler(async (request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("ApiKey", request.Headers.Authorization!.Scheme);
            Assert.Equal("SYNTHETIC_KEY_ONLY", request.Headers.Authorization.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.DoesNotContain("script", body.RootElement.GetRawText());
            if (request.RequestUri!.AbsolutePath.EndsWith("/_field_caps"))
            {
                Assert.Equal(Field, body.RootElement.GetProperty("fields")[0].GetString());
                return Response(Mapping());
            }
            Assert.EndsWith("/" + Index + "/_search", request.RequestUri.AbsoluteUri);
            var root = body.RootElement;
            Assert.Equal(0, root.GetProperty("size").GetInt32());
            Assert.False(root.GetProperty("_source").GetBoolean());
            var filters = root.GetProperty("query").GetProperty("bool").GetProperty("filter");
            Assert.Equal(3, filters.GetArrayLength());
            Assert.Equal("checkout", filters[0].GetProperty("term").GetProperty("service.name").GetString());
            Assert.Equal("test", filters[1].GetProperty("term").GetProperty("labels.environment").GetString());
            var range = filters[2].GetProperty("range").GetProperty("@timestamp");
            Assert.Equal(From, DateTimeOffset.Parse(range.GetProperty("gte").GetString()!));
            Assert.Equal(From.AddHours(1), DateTimeOffset.Parse(range.GetProperty("lt").GetString()!));
            var timeline = root.GetProperty("aggs").GetProperty("timeline");
            Assert.Equal("300s", timeline.GetProperty("date_histogram").GetProperty("fixed_interval").GetString());
            Assert.Equal(Field, timeline.GetProperty("aggs").GetProperty("metric_value").GetProperty(aggregation).GetProperty("field").GetString());
            Assert.Equal(Field, timeline.GetProperty("aggs").GetProperty("sample_count").GetProperty("value_count").GetProperty("field").GetString());
            return Response(Series(Bucket(0), Bucket(5, aggregation == "sum" ? 0 : null, 0, 0), Bucket(10, 0, 1, 1)));
        });
        using var adapter = Adapter(handler, options);
        var result = await adapter.QueryAsync(Query() with { Aggregation = aggregation }, default);
        Assert.Equal(2, handler.Calls);
        Assert.Equal(2, result.Points.Count);
        Assert.Equal(0, result.Points[1].Value);
        Assert.Contains(result.Warnings, warning => warning.Contains("不补零"));
        Assert.False(result.Truncated);
    }

    [Theory]
    [InlineData("keyword", true)]
    [InlineData("text", true)]
    [InlineData("histogram", true)]
    [InlineData("double", false)]
    public async Task NonNumericAndNonAggregatableMappingsAreRejected(string type, bool aggregatable)
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Response(Mapping(type, aggregatable))));
        using var adapter = Adapter(handler);
        var error = await Assert.ThrowsAsync<EsDataSourceException>(() => adapter.QueryAsync(Query(), default));
        Assert.Equal("invalid_metric_mapping", error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("wildcard")]
    public async Task AnalyzedOrUnsupportedScopeFieldsCannotExpandServiceMatching(string type)
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Response(Mapping(scopeType: type))));
        using var adapter = Adapter(handler);
        Assert.Equal("invalid_scope_mapping", (await Assert.ThrowsAsync<EsDataSourceException>(() => adapter.QueryAsync(Query(), default))).Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task AliasOrUnexpectedBackingIndexIsRejected()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Response(Mapping(index: "other-index"))));
        using var adapter = Adapter(handler);
        Assert.Equal("invalid_index_mapping", (await Assert.ThrowsAsync<EsDataSourceException>(() => adapter.QueryAsync(Query(), default))).Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task MappingIsRevalidatedAfterEveryQuery()
    {
        var mappings = 0;
        var handler = new FakeHandler((request, _) => Task.FromResult(Response(request.RequestUri!.AbsolutePath.EndsWith("/_field_caps") ? Mapping(++mappings == 1 ? "double" : "keyword") : Series(Bucket(0)))));
        using var adapter = Adapter(handler);
        Assert.Single((await adapter.QueryAsync(Query(), default)).Points);
        Assert.Equal("invalid_metric_mapping", (await Assert.ThrowsAsync<EsDataSourceException>(() => adapter.QueryAsync(Query(), default))).Code);
        Assert.Equal(3, handler.Calls);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{broken")]
    [InlineData("{\"error\":{\"reason\":\"SYNTHETIC_UPSTREAM_SECRET\"}}")]
    [InlineData("{\"fields\":null}")]
    public async Task InvalidResponsesBecomeSafeErrors(string raw)
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Response(raw)));
        using var adapter = Adapter(handler);
        var error = await Assert.ThrowsAsync<EsDataSourceException>(() => adapter.QueryAsync(Query(), default));
        Assert.Equal(502, error.StatusCode);
        Assert.DoesNotContain("SYNTHETIC_UPSTREAM_SECRET", error.Message);
    }

    [Fact]
    public async Task ResponseSizeLimitAppliesBeforeParsing()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Response(new string(' ', 1024 * 1024 + 1))));
        using var adapter = Adapter(handler);
        Assert.Equal("response_too_large", (await Assert.ThrowsAsync<EsDataSourceException>(() => adapter.QueryAsync(Query(), default))).Code);
    }

    [Theory]
    [InlineData(302)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(500)]
    public async Task RedirectAndHttpFailuresNeverBecomeDemoSuccess(int status)
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Response("SYNTHETIC_UPSTREAM_SECRET", (HttpStatusCode)status)));
        using var adapter = Adapter(handler);
        var error = await Assert.ThrowsAsync<EsDataSourceException>(() => adapter.QueryAsync(Query(), default));
        Assert.Equal(502, error.StatusCode);
        Assert.DoesNotContain("SYNTHETIC_UPSTREAM_SECRET", error.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task CancellationIsNotMisreportedAsTimeoutAndTimeoutIsRealFailure()
    {
        var options = Options(); options.TimeoutSeconds = 1;
        var handler = new FakeHandler(async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(30), ct); return Response(Mapping()); });
        using var adapter = Adapter(handler, options);
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.QueryAsync(Query(), caller.Token));
        var timeout = await Assert.ThrowsAsync<EsDataSourceException>(() => adapter.QueryAsync(Query(), default));
        Assert.Equal(504, timeout.StatusCode);
        Assert.Equal("es_timeout", timeout.Code);
    }

    [Fact]
    public async Task PartialSearchIsRejected()
    {
        foreach (var raw in new[]
        {
            "{\"timed_out\":true,\"_shards\":{\"failed\":0}}",
            "{\"timed_out\":false,\"_shards\":{\"failed\":1}}"
        })
        {
            using var adapter = Adapter(WithSeries(raw));
            var error = await Assert.ThrowsAsync<EsDataSourceException>(() => adapter.QueryAsync(Query(), default));
            Assert.Equal("es_partial", error.Code);
        }
    }

    [Fact]
    public async Task OutOfWindowDuplicateMisalignedAndUnboundedBucketsAreRejected()
    {
        foreach (var buckets in new[]
        {
            new[] { Bucket(-5) }, new[] { Bucket(60) }, new[] { Bucket(0), Bucket(0) },
            new[] { Bucket(5), Bucket(0) }, new[] { Bucket(1) },
            Enumerable.Range(0, 1001).Select(_ => Bucket(0)).ToArray()
        })
        {
            using var adapter = Adapter(WithSeries(Series(buckets)));
            Assert.Equal("invalid_es_response", (await Assert.ThrowsAsync<EsDataSourceException>(() => adapter.QueryAsync(Query(), default))).Code);
        }
    }

    [Fact]
    public async Task InvalidNumbersAndMissingCountsAreRejected()
    {
        var rawValues = new[]
        {
            $"{{\"key\":{From.ToUnixTimeMilliseconds()},\"doc_count\":1,\"metric_value\":{{\"value\":1e999}},\"sample_count\":{{\"value\":1}}}}",
            $"{{\"key\":{From.ToUnixTimeMilliseconds()},\"doc_count\":1,\"metric_value\":{{\"value\":2}}}}",
            $"{{\"key\":{From.ToUnixTimeMilliseconds()},\"doc_count\":0,\"metric_value\":{{\"value\":2}},\"sample_count\":{{\"value\":0}}}}"
        };
        foreach (var bucket in rawValues)
        {
            var series = "{\"timed_out\":false,\"_shards\":{\"failed\":0},\"aggregations\":{\"timeline\":{\"buckets\":[" + bucket + "]}}}";
            using var adapter = Adapter(WithSeries(series));
            Assert.Equal("invalid_es_response", (await Assert.ThrowsAsync<EsDataSourceException>(() => adapter.QueryAsync(Query(), default))).Code);
        }
    }
}
