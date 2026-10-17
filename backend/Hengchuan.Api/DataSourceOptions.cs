namespace Hengchuan.Api;

/// <summary>Server-owned configuration. No client request may replace this configuration.</summary>
public sealed class DataSourceOptions
{
    public bool Enabled { get; set; }
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public string[] AllowedIndices { get; set; } = [];
    public string[] AllowedServices { get; set; } = [];
    public string[] AllowedEnvironments { get; set; } = [];
    /// <summary>Public metric key to an explicitly allowed numeric ES field, verified through field_caps.</summary>
    public Dictionary<string, string> MetricFields { get; set; } = new(StringComparer.Ordinal);
    public string TimestampField { get; set; } = "@timestamp";
    public string ServiceField { get; set; } = "service.name";
    public string EnvironmentField { get; set; } = "labels.environment";
    public int TimeoutSeconds { get; set; } = 10;
}

public sealed record EsSeriesQuery(string Index, string Service, string Environment, string Metric,
    DateTimeOffset From, DateTimeOffset To, int IntervalSeconds = 300, string Aggregation = "avg");
public sealed record EsSeriesPoint(DateTimeOffset Timestamp, double Value);
public sealed record EsSeriesResult(List<EsSeriesPoint> Points, string[] Warnings, bool Truncated = false);
public sealed class EsDataSourceException(int statusCode, string code, string message) : Exception(message)
{
    public EsDataSourceException(int statusCode, string message) : this(statusCode, "es_error", message) { }
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
