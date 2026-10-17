using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hengchuan.Api;

/// <summary>Read-only, bounded ES numeric series adapter. It has no demo fallback, model, script, or collector.</summary>
public sealed class ElasticsearchAdapter : IDisposable
{
    private const int MaxResponseBytes = 1024 * 1024;
    private const int MaxRequestBytes = 8 * 1024;
    private const int MaxBuckets = 1000;
    private static readonly HashSet<string> NumericTypes = new(StringComparer.Ordinal)
        { "byte", "short", "integer", "long", "unsigned_long", "half_float", "float", "double", "scaled_float" };
    private readonly DataSourceOptions options;
    private readonly HttpClient http;
    private readonly bool ownsHttp;

    public ElasticsearchAdapter(DataSourceOptions options, HttpClient? httpClient = null)
    {
        this.options = options;
        ownsHttp = httpClient is null;
        http = httpClient ?? new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });
    }

    public async Task<EsSeriesResult> QueryAsync(EsSeriesQuery query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var metricField = Validate(query);
        var endpoint = ValidateEndpoint();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 30)));
        try
        {
            // Verify every request: stale mapping/cache must never turn a text/histogram field into a numeric metric.
            await ValidateMetricMappingAsync(endpoint, query.Index, metricField, deadline.Token);
            var intervalMilliseconds = checked((long)query.IntervalSeconds * 1000);
            var fromMilliseconds = query.From.ToUnixTimeMilliseconds();
            var toMilliseconds = query.To.ToUnixTimeMilliseconds();
            if (DateTimeOffset.FromUnixTimeMilliseconds(toMilliseconds) < query.To) toMilliseconds++;
            var offset = ((fromMilliseconds % intervalMilliseconds) + intervalMilliseconds) % intervalMilliseconds;
            var body = new
            {
                size = 0,
                _source = false,
                track_total_hits = false,
                timeout = Math.Clamp(options.TimeoutSeconds, 1, 30) + "s",
                query = new
                {
                    @bool = new
                    {
                        filter = new object[]
                        {
                            new { term = new Dictionary<string, string> { [options.ServiceField] = query.Service } },
                            new { term = new Dictionary<string, string> { [options.EnvironmentField] = query.Environment } },
                            new { range = new Dictionary<string, object> { [options.TimestampField] = new { gte = query.From.ToUniversalTime().ToString("O"), lt = query.To.ToUniversalTime().ToString("O") } } }
                        }
                    }
                },
                aggs = new
                {
                    timeline = new
                    {
                        date_histogram = new
                        {
                            field = options.TimestampField,
                            fixed_interval = query.IntervalSeconds.ToString(CultureInfo.InvariantCulture) + "s",
                            offset = offset.ToString(CultureInfo.InvariantCulture) + "ms",
                            time_zone = "UTC",
                            min_doc_count = 0,
                            extended_bounds = new { min = fromMilliseconds, max = toMilliseconds - 1 },
                            hard_bounds = new { min = fromMilliseconds, max = toMilliseconds - 1 }
                        },
                        aggs = new Dictionary<string, object>
                        {
                            ["metric_value"] = new Dictionary<string, object> { [query.Aggregation] = new { field = metricField } },
                            ["sample_count"] = new { value_count = new { field = metricField } }
                        }
                    }
                }
            };
            using var document = await SendAsync(endpoint, query.Index + "/_search", body, deadline.Token);
            return ParseSeries(document.RootElement, query, fromMilliseconds, toMilliseconds, intervalMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw Error(504, "es_timeout", "Elasticsearch 查询超时；未回退演示数据"); }
        catch (HttpRequestException)
        { throw Error(502, "es_unavailable", "Elasticsearch 网络或 TLS 连接失败；未回退演示数据"); }
    }

    private string Validate(EsSeriesQuery query)
    {
        if (!options.Enabled) throw Error(503, "es_disabled", "Elasticsearch 数据源默认关闭；需由服务端显式配置并启用");
        if (query is null) throw Error(400, "invalid_query", "缺少时序查询");
        if (string.IsNullOrWhiteSpace(query.Index) || !IsIndex(query.Index) ||
            !options.AllowedIndices.Contains(query.Index, StringComparer.Ordinal))
            throw Error(403, "scope_denied", "索引必须为服务端允许的单个具体索引，不支持通配符、别名或多个索引");
        if (string.IsNullOrWhiteSpace(query.Service) || query.Service.Length > 128 || !options.AllowedServices.Contains(query.Service, StringComparer.Ordinal) ||
            string.IsNullOrWhiteSpace(query.Environment) || query.Environment.Length > 128 || !options.AllowedEnvironments.Contains(query.Environment, StringComparer.Ordinal))
            throw Error(403, "scope_denied", "服务或环境不在服务端允许范围内");
        if (string.IsNullOrWhiteSpace(query.Metric) || query.Metric.Length > 128 || !options.MetricFields.TryGetValue(query.Metric, out var field) || !IsField(field))
            throw Error(403, "metric_denied", "指标未映射到服务端允许的数值字段");
        if (!IsField(options.TimestampField) || !IsField(options.ServiceField) || !IsField(options.EnvironmentField))
            throw Error(503, "invalid_es_config", "Elasticsearch 字段映射配置无效");
        if (query.From >= query.To || query.To - query.From > TimeSpan.FromDays(7))
            throw Error(400, "invalid_window", "必须指定递增的绝对时间范围，最长 7 天");
        if (query.IntervalSeconds is < 1 or > 86400 || Math.Ceiling((query.To - query.From).TotalSeconds / query.IntervalSeconds) > MaxBuckets)
            throw Error(400, "invalid_interval", "聚合间隔须为 1–86400 秒且查询最多生成 1000 个时间桶");
        if (query.Aggregation is not ("avg" or "sum"))
            throw Error(422, "unsupported_es_aggregation", "真实 Elasticsearch 首版只支持 avg/sum 聚合，不支持 raw 或脚本；未伪造原始采样");
        return field;
    }

    private Uri ValidateEndpoint()
    {
        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("https" or "http") ||
            endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            throw Error(503, "invalid_es_config", "需配置有效的 ES HTTPS 地址；URL 不能包含凭据、查询参数或片段");
        var literalLoopback = IPAddress.TryParse(endpoint.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address);
        if (endpoint.Scheme == "http" && !literalLoopback)
            throw Error(503, "invalid_es_config", "HTTP 仅限字面量回环 IP；其他 Elasticsearch 地址必须使用 HTTPS");
        if (!string.IsNullOrEmpty(options.ApiKey) && (options.ApiKey.Length > 4096 || options.ApiKey.Any(char.IsControl)))
            throw Error(503, "invalid_es_config", "Elasticsearch 凭据配置格式无效");
        return endpoint;
    }

    private async Task ValidateMetricMappingAsync(Uri endpoint, string index, string field, CancellationToken ct)
    {
        using var document = await SendAsync(endpoint, index + "/_field_caps", new { fields = new[] { field, options.ServiceField, options.EnvironmentField, options.TimestampField }.Distinct(StringComparer.Ordinal).ToArray() }, ct);
        var root = document.RootElement;
        if (root.TryGetProperty("failures", out var failures) && (failures.ValueKind != JsonValueKind.Array || failures.GetArrayLength() > 0))
            throw Error(502, "invalid_metric_mapping", "Elasticsearch 指标映射部分失败；未继续查询");
        if (!Object(root, "fields", out var fields) || !Object(fields, field, out var mapping) || !mapping.EnumerateObject().Any())
            throw Error(502, "invalid_metric_mapping", "Elasticsearch 指标字段不存在或未返回有效的数值映射");
        foreach (var entry in mapping.EnumerateObject())
        {
            if (!NumericTypes.Contains(entry.Name) || entry.Value.ValueKind != JsonValueKind.Object ||
                !entry.Value.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != entry.Name ||
                !entry.Value.TryGetProperty("aggregatable", out var aggregatable) || aggregatable.ValueKind != JsonValueKind.True ||
                (entry.Value.TryGetProperty("non_aggregatable_indices", out var nonAggregatable) && (nonAggregatable.ValueKind != JsonValueKind.Array || nonAggregatable.GetArrayLength() > 0)))
                throw Error(502, "invalid_metric_mapping", "Elasticsearch 指标必须是可聚合数值字段；不接受文本、脚本、histogram 或混合类型");
        }
        ValidateDimensionMapping(fields, options.ServiceField, ["keyword", "constant_keyword"]);
        ValidateDimensionMapping(fields, options.EnvironmentField, ["keyword", "constant_keyword"]);
        ValidateDimensionMapping(fields, options.TimestampField, ["date", "date_nanos"]);
        if (!root.TryGetProperty("indices", out var indices) || indices.ValueKind != JsonValueKind.Array || indices.GetArrayLength() != 1 ||
            indices.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String || item.GetString() != index))
            throw Error(502, "invalid_index_mapping", "Elasticsearch 返回了其他索引映射；只支持配置的具体索引");
    }

    private static void ValidateDimensionMapping(JsonElement fields, string field, string[] allowedTypes)
    {
        if (!Object(fields, field, out var mapping) || !mapping.EnumerateObject().Any())
            throw Error(502, "invalid_scope_mapping", "Elasticsearch 范围或时间字段缺少映射；未扩大查询范围");
        foreach (var entry in mapping.EnumerateObject())
        {
            if (!allowedTypes.Contains(entry.Name, StringComparer.Ordinal) || entry.Value.ValueKind != JsonValueKind.Object ||
                !entry.Value.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != entry.Name ||
                !entry.Value.TryGetProperty("searchable", out var searchable) || searchable.ValueKind != JsonValueKind.True ||
                (entry.Value.TryGetProperty("non_searchable_indices", out var nonSearchable) && (nonSearchable.ValueKind != JsonValueKind.Array || nonSearchable.GetArrayLength() > 0)))
                throw Error(502, "invalid_scope_mapping", "Elasticsearch 服务/环境必须是可精确检索的 keyword/constant_keyword，时间必须是 date/date_nanos；拒绝分词字段造成范围扩大");
        }
    }

    private static EsSeriesResult ParseSeries(JsonElement root, EsSeriesQuery query, long fromMilliseconds, long toMilliseconds, long intervalMilliseconds)
    {
        if (!root.TryGetProperty("timed_out", out var timedOut) || timedOut.ValueKind != JsonValueKind.False)
            throw Error(504, "es_partial", "Elasticsearch 标记查询超时；不将不完整数据作为正常时序结果");
        if (!root.TryGetProperty("_shards", out var shards) || shards.ValueKind != JsonValueKind.Object || !shards.TryGetProperty("failed", out var failed) || failed.ValueKind != JsonValueKind.Number || !failed.TryGetInt32(out var failedCount) || failedCount != 0)
            throw Error(502, "es_partial", "Elasticsearch 分片状态异常或部分失败；未返回正常时序结果");
        if (!Object(root, "aggregations", out var aggs) || !Object(aggs, "timeline", out var timeline) ||
            !timeline.TryGetProperty("buckets", out var buckets) || buckets.ValueKind != JsonValueKind.Array || buckets.GetArrayLength() > MaxBuckets)
            throw Error(502, "invalid_es_response", "Elasticsearch 未返回有界的时序聚合结果");
        var points = new List<EsSeriesPoint>();
        var warnings = new List<string>();
        var missing = 0;
        long? previous = null;
        foreach (var bucket in buckets.EnumerateArray())
        {
            if (bucket.ValueKind != JsonValueKind.Object || !bucket.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.Number || !key.TryGetInt64(out var milliseconds) ||
                milliseconds < fromMilliseconds || milliseconds >= toMilliseconds || (milliseconds - fromMilliseconds) % intervalMilliseconds != 0 ||
                (previous is not null && milliseconds <= previous) || !bucket.TryGetProperty("doc_count", out var count) || count.ValueKind != JsonValueKind.Number || !count.TryGetInt64(out var documentCount) || documentCount < 0 ||
                !Object(bucket, "metric_value", out var metric) || !metric.TryGetProperty("value", out var value) ||
                !Object(bucket, "sample_count", out var samples) || !samples.TryGetProperty("value", out var sampleValue) || sampleValue.ValueKind != JsonValueKind.Number || !sampleValue.TryGetInt64(out var sampleCount) || sampleCount < 0)
                throw Error(502, "invalid_es_response", "Elasticsearch 时间桶、顺序或指标结构无效；拒绝范围外或重复时间点");
            previous = milliseconds;
            if (sampleCount == 0)
            {
                if (value.ValueKind != JsonValueKind.Null && !(value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var emptyValue) && emptyValue == 0))
                    throw Error(502, "invalid_es_response", "Elasticsearch 无样本时间桶错误地包含非零数值");
                missing++;
                continue;
            }
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number) || documentCount == 0)
                throw Error(502, "invalid_es_response", "Elasticsearch 指标不是有限数值，或空桶错误地包含数值");
            DateTimeOffset timestamp;
            try { timestamp = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds); }
            catch (ArgumentOutOfRangeException) { throw Error(502, "invalid_es_response", "Elasticsearch 时间超出有效范围"); }
            // ES uses millisecond buckets. Clip only a sub-millisecond first label to the exact requested start.
            points.Add(new(timestamp < query.From ? query.From : timestamp, number));
        }
        if (missing > 0) warnings.Add($"{missing} 个时间桶没有有效指标样本；保留数据缺口，不补零或演示值");
        if (points.Count == 0) warnings.Add("指定范围内没有有效的数值指标样本");
        return new(points, warnings.ToArray());
    }

    private async Task<JsonDocument> SendAsync(Uri endpoint, string path, object body, CancellationToken ct)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(body);
        if (payload.Length > MaxRequestBytes) throw Error(400, "request_too_large", "Elasticsearch 请求超过安全上限");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.AbsoluteUri.TrimEnd('/') + "/" + path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(options.ApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", options.ApiKey);
        request.Content = new ByteArrayContent(payload);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw Error(502, "es_http_error", response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? "Elasticsearch 拒绝配置的凭据或权限" : "Elasticsearch HTTP 请求失败；未回退演示数据");
        if (response.Content.Headers.ContentLength > MaxResponseBytes) throw Error(502, "response_too_large", "Elasticsearch 响应超过 1MiB 安全上限");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes) throw Error(502, "response_too_large", "Elasticsearch 响应超过 1MiB 安全上限");
            buffer.Write(chunk, 0, read);
        }
        try
        {
            var document = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 24 });
            if (document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.TryGetProperty("error", out _)) return document;
            document.Dispose();
            throw Error(502, "invalid_es_response", "Elasticsearch 响应不是成功的 JSON 对象");
        }
        catch (JsonException) { throw Error(502, "invalid_es_response", "Elasticsearch 响应不是有效的有界 JSON"); }
    }

    private static bool Object(JsonElement value, string field, out JsonElement result)
    {
        result = default;
        return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(field, out result) && result.ValueKind == JsonValueKind.Object;
    }
    private static bool IsIndex(string value) => Regex.IsMatch(value, @"^[a-z0-9.][a-z0-9._-]{0,254}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) && value is not "." and not "..";
    private static bool IsField(string value) => !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, @"^[A-Za-z_@][A-Za-z0-9_@.-]{0,127}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static EsDataSourceException Error(int status, string code, string message) => new(status, code, message);
    public void Dispose() { if (ownsHttp) http.Dispose(); }
}
