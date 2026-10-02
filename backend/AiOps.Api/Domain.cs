using System.Text.Json;
using System.Text.RegularExpressions;
namespace AiOps;

public record Asset(string Id, string Name, string Kind, string Source = "manual", bool Locked = true, DateTimeOffset UpdatedAt = default);
public record Relation(string Id, string From, string To, string Kind, string Source = "manual", bool Locked = true, DateTimeOffset UpdatedAt = default);
public record Incident(string Id, string Fingerprint, string Name, string Severity, string Target, string Status, int Count, DateTimeOffset FirstSeen, DateTimeOffset UpdatedAt, Diagnosis? Diagnosis = null);
public record LogEntry(string Id, string System, string Level, string Message, DateTimeOffset UpdatedAt);
public record Chart(string Id, string Title, string Dataset, string GroupBy, string Type, int Hours = 24, int Limit = 20);
public record Settings(string PrometheusUrl = "", string ModelUrl = "", string ModelName = "", bool ExternalModel = false, string[]? AllowedExternalFields = null);
public record Diagnosis(string Mode, string[] Facts, string[] Evidence, string[] Candidates, string[] Gaps, string[] Verification, string[] Actions, string? ModelAdvice = null);
public record Audit(string Id, string Action, string Entity, DateTimeOffset At);
public record AnalyzeInput(string Text, bool UseModel = false);
public static class Rules
{
    public static string Redact(string s)
    {
        var bounded = TimeSpan.FromMilliseconds(100);
        s = Regex.Replace(s, @"(?i)bearer\s+[^\s""',;]+", "Bearer [REDACTED]", RegexOptions.None, bounded);
        s = Regex.Replace(s, @"(?i)([""']?(?:password|passwd|token|secret|api[_-]?key|authorization)[""']?\s*[:=]\s*)(?:""[^""]*""|'[^']*'|[^\s,;]+)", "$1[REDACTED]", RegexOptions.None, bounded);
        return Regex.Replace(s, @"(?i)(https?://)[^/\s:@]+:[^/\s@]+@", "$1[REDACTED]@", RegexOptions.None, bounded);
    }

    public static Diagnosis Analyze(string text)
    {
        var timeout = text.Contains("timeout", StringComparison.OrdinalIgnoreCase) || text.Contains("超时");
        var memory = text.Contains("oom", StringComparison.OrdinalIgnoreCase) || text.Contains("内存");
        return new("规则分析（未调用模型）", ["输入仅作为不可信观测数据处理"], [Redact(text)[..Math.Min(Redact(text).Length, 1200)]], memory ? ["内存压力或容器内存限制（待验证）"] : timeout ? ["下游延迟、网络故障或连接池饱和（待验证）"] : ["证据不足，不能确定根因"], ["缺少同时间窗口指标、调用链及变更记录"], ["核对时间、实例与影响范围", "关联指标和真实 trace；检查最近变更"], ["先验证候选原因，再由操作人员按 Runbook 审批处置；本系统不会执行修复"]);
    }
    public static void Require(bool valid, string message) { if (!valid) throw new ArgumentException(message); }
    public static string Text(string? s, int max = 120) { Require(!string.IsNullOrWhiteSpace(s) && s.Length <= max, "字段为空或超长"); return s!; }
    public static void Validate(Chart c) { Text(c.Title); Require(new[] { "assets", "incidents", "logs" }.Contains(c.Dataset), "不允许的数据集"); Require((c.Dataset switch { "assets" => new[] { "kind", "source" }, "incidents" => new[] { "severity", "status" }, _ => new[] { "level", "system" } }).Contains(c.GroupBy), "不允许的分组字段"); Require(new[] { "bar", "pie" }.Contains(c.Type) && c.Hours is >= 1 and <= 168 && c.Limit is >= 1 and <= 50, "图表范围超限"); }
    public static Chart ChartFromText(string text) { Text(text, 500); var ds = text.Contains("日志") ? "logs" : text.Contains("资产") ? "assets" : "incidents"; return new(Guid.NewGuid().ToString("N"), text, ds, ds == "logs" ? "level" : ds == "assets" ? "kind" : "severity", text.Contains("饼") ? "pie" : "bar"); }
}
