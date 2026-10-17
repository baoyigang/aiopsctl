using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace Hengchuan.Api;

public sealed record AtlasState
{
    public int SchemaVersion { get; init; } = 1;
    public List<Space> Spaces { get; init; } = [];
    public List<Business> Businesses { get; init; } = [];
    public List<Indicator> Indicators { get; init; } = [];
    public List<IncidentEvent> Events { get; init; } = [];
    public List<Screen> Screens { get; init; } = [];
}

public sealed record Space
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
}

public sealed record Business
{
    public string Id { get; init; } = "";
    public string SpaceId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Owner { get; init; } = "";
    public string Description { get; init; } = "";
    public string Service { get; init; } = "";
    public string Environment { get; init; } = "";
    public double TargetAvailability { get; init; } = 99.9;
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record Indicator
{
    public string Id { get; init; } = "";
    public string SpaceId { get; init; } = "";
    public string Name { get; init; } = "";
    public string SourceId { get; init; } = "demo";
    public string SourceName { get; init; } = "合成演示";
    public string MetricName { get; init; } = "";
    public string Unit { get; init; } = "";
    public string Owner { get; init; } = "";
    public string Visibility { get; init; } = "space";
    public bool Enabled { get; init; } = true;
    public List<string> BusinessIds { get; init; } = [];
    public string Behavior { get; init; } = "normal";
    public DateTimeOffset UpdatedAt { get; init; }
    public string UpdatedBy { get; init; } = "demo-operator";
}

public sealed record IncidentEvent
{
    public string Id { get; init; } = "";
    public string SpaceId { get; init; } = "";
    public string BusinessId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Level { get; init; } = "P3";
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public string Description { get; init; } = "";
}

public sealed record Screen
{
    public string Id { get; init; } = "";
    public string SpaceId { get; init; } = "";
    public string Name { get; init; } = "";
    public string BusinessId { get; init; } = "";
    public int Columns { get; init; } = 3;
    public List<ScreenPanel> Panels { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; }
    public string UpdatedBy { get; init; } = "demo-operator";
}

public sealed record ScreenPanel
{
    public string IndicatorId { get; init; } = "";
    public string Kind { get; init; } = "line";
    public string Aggregation { get; init; } = "avg";
}

public sealed record StoreSnapshot(long Revision, AtlasState State);

public sealed class AtlasException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

/// <summary>Validates the complete candidate snapshot before a transaction can commit.</summary>
public static class AtlasValidation
{
    private static readonly Regex ValidId = new("\\A[a-z][a-z0-9-]{0,63}\\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex UnsafeText = new("[<>]|javascript\\s*:|data\\s*:\\s*text/html", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static void Validate(AtlasState state)
    {
        if (state is null) Fail("缺少配置快照");
        if (state!.SchemaVersion != 1) throw new AtlasException(400, "UNSUPPORTED_SCHEMA", "仅支持配置 schemaVersion 1");
        ListBound(state.Spaces, 50, "空间");
        ListBound(state.Businesses, 500, "业务");
        ListBound(state.Indicators, 2000, "指标");
        ListBound(state.Events, 10000, "事件");
        ListBound(state.Screens, 500, "大屏");

        var spaces = UniqueIds(state.Spaces, x => x.Id, "空间");
        var businesses = UniqueIds(state.Businesses, x => x.Id, "业务");
        var indicators = UniqueIds(state.Indicators, x => x.Id, "指标");
        UniqueIds(state.Events, x => x.Id, "事件");
        UniqueIds(state.Screens, x => x.Id, "大屏");
        UniqueNames(state.Spaces.Select(x => ("", x.Name)), "空间");
        UniqueNames(state.Businesses.Select(x => (x.SpaceId, x.Name)), "业务");
        UniqueNames(state.Indicators.Select(x => (x.SpaceId, x.Name)), "指标");
        UniqueNames(state.Screens.Select(x => (x.SpaceId, x.Name)), "大屏");

        foreach (var space in state.Spaces) Text(space.Name, "空间名称", 120, required: true);
        foreach (var business in state.Businesses)
        {
            SpaceReference(spaces, business.SpaceId);
            Text(business.Name, "业务名称", 120, required: true);
            Text(business.Owner, "业务负责人", 100, required: true);
            Text(business.Description, "业务说明", 2000, multiline: true);
            Text(business.Service, "服务", 100, required: true);
            Text(business.Environment, "环境", 80, required: true);
            if (!double.IsFinite(business.TargetAvailability) || business.TargetAvailability is < 90 or > 99.999)
                Fail("目标可用性必须在 90 到 99.999 之间");
            Date(business.UpdatedAt, "业务更新时间");
        }
        foreach (var indicator in state.Indicators)
        {
            SpaceReference(spaces, indicator.SpaceId);
            Text(indicator.Name, "指标名称", 120, required: true);
            Enum(indicator.SourceId, ["demo", "es-main"], "数据源");
            Text(indicator.SourceName, "数据源名称", 120, required: true);
            Text(indicator.MetricName, "指标键", 120, required: true);
            Text(indicator.Unit, "指标单位", 32);
            Text(indicator.Owner, "指标负责人", 100, required: true);
            Enum(indicator.Visibility, ["space", "public"], "可见性");
            Enum(indicator.Behavior, ["normal", "empty"], "演示行为");
            Text(indicator.UpdatedBy, "更新者", 100, required: true);
            Date(indicator.UpdatedAt, "指标更新时间");
            ListBound(indicator.BusinessIds, 100, "指标业务引用");
            if (indicator.BusinessIds.Distinct(StringComparer.Ordinal).Count() != indicator.BusinessIds.Count) Fail("指标不能重复引用同一业务");
            foreach (var businessId in indicator.BusinessIds)
            {
                var business = Reference(businesses, businessId, "指标引用的业务");
                SameSpace(indicator.SpaceId, business.SpaceId, "指标与业务");
            }
        }
        foreach (var incident in state.Events)
        {
            SpaceReference(spaces, incident.SpaceId);
            var business = Reference(businesses, incident.BusinessId, "事件引用的业务");
            SameSpace(incident.SpaceId, business.SpaceId, "事件与业务");
            Text(incident.Title, "事件标题", 160, required: true);
            Text(incident.Description, "事件说明", 4000, multiline: true);
            Enum(incident.Level, ["P1", "P2", "P3"], "事件级别");
            Date(incident.StartedAt, "事件开始时间");
            if (incident.EndedAt is { } ended)
            {
                Date(ended, "事件恢复时间");
                if (ended < incident.StartedAt) Fail("事件恢复时间不能早于开始时间");
            }
        }
        foreach (var screen in state.Screens)
        {
            SpaceReference(spaces, screen.SpaceId);
            var business = Reference(businesses, screen.BusinessId, "大屏引用的业务");
            SameSpace(screen.SpaceId, business.SpaceId, "大屏与业务");
            Text(screen.Name, "大屏名称", 120, required: true);
            Text(screen.UpdatedBy, "更新者", 100, required: true);
            Date(screen.UpdatedAt, "大屏更新时间");
            if (screen.Columns is < 1 or > 3) Fail("大屏列数必须在 1 到 3 之间");
            ListBound(screen.Panels, 24, "大屏面板");
            if (screen.Panels.Count == 0) Fail("大屏必须包含 1 到 24 个面板");
            foreach (var panel in screen.Panels)
            {
                if (panel is null) Fail("大屏面板不能为空");
                var indicator = Reference(indicators, panel!.IndicatorId, "面板引用的指标");
                SameSpace(screen.SpaceId, indicator.SpaceId, "面板指标与大屏");
                if (!indicator.BusinessIds.Contains(screen.BusinessId, StringComparer.Ordinal))
                    throw new AtlasException(400, "INVALID_REFERENCE", "面板指标必须属于大屏所选业务");
                Enum(panel.Kind, ["line", "bar", "funnel"], "图表类型");
                Enum(panel.Aggregation, ["avg", "sum", "raw"], "聚合方式");
            }
        }
    }

    internal static void ValidateDeletions(AtlasState previous, AtlasState candidate)
    {
        if (candidate.Businesses is null || candidate.Indicators is null || candidate.Events is null || candidate.Screens is null) return;
        var remainingBusinesses = candidate.Businesses.Where(x => x is not null).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var removed in previous.Businesses.Where(b => !remainingBusinesses.Contains(b.Id)))
        {
            if (candidate.Indicators.Any(i => i?.BusinessIds?.Contains(removed.Id) == true) ||
                candidate.Events.Any(e => e?.BusinessId == removed.Id) || candidate.Screens.Any(s => s?.BusinessId == removed.Id))
                throw new AtlasException(409, "RESOURCE_REFERENCED", "业务仍被指标、事件或大屏引用，请先解除引用");
        }
        var remainingIndicators = candidate.Indicators.Where(x => x is not null).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var removed in previous.Indicators.Where(i => !remainingIndicators.Contains(i.Id)))
        {
            if (removed.BusinessIds.Any(remainingBusinesses.Contains) || candidate.Screens.Any(s => s?.Panels?.Any(p => p?.IndicatorId == removed.Id) == true))
                throw new AtlasException(409, "RESOURCE_REFERENCED", "指标仍关联业务或被大屏引用，请先解除引用");
        }
    }

    private static Dictionary<string, T> UniqueIds<T>(List<T> values, Func<T, string> select, string label) where T : class
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (value is null) Fail($"{label}项不能为空");
            var id = select(value!);
            Id(id, $"{label} ID");
            if (!result.TryAdd(id, value!)) throw new AtlasException(409, "DUPLICATE_ID", $"{label} ID 不能重复");
        }
        return result;
    }
    private static void UniqueNames(IEnumerable<(string Scope, string Name)> values, string label)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (scope, name) in values)
        {
            Text(name, $"{label}名称", 120, required: true);
            if (!seen.Add(scope + "\0" + name.Trim())) throw new AtlasException(409, "DUPLICATE_NAME", $"同一空间的{label}名称不能重复");
        }
    }
    private static T Reference<T>(Dictionary<string, T> values, string id, string label)
    {
        Id(id, label + " ID");
        if (!values.TryGetValue(id, out var item)) throw new AtlasException(400, "INVALID_REFERENCE", label + "不存在");
        return item;
    }
    private static void SpaceReference(Dictionary<string, Space> spaces, string id) => Reference(spaces, id, "引用的空间");
    private static void SameSpace(string left, string right, string label)
    {
        if (left != right) throw new AtlasException(400, "CROSS_SPACE_REFERENCE", label + "必须位于同一空间");
    }
    private static void Id(string? value, string label)
    {
        if (value is null || !ValidId.IsMatch(value)) Fail(label + "必须是 1 到 64 位小写字母、数字、连字符，并以字母开头");
    }
    private static void Text(string? value, string label, int max, bool required = false, bool multiline = false)
    {
        if (value is null || value.Length > max || required && string.IsNullOrWhiteSpace(value)) Fail($"{label}无效或超过 {max} 字符");
        if (value!.Any(c => char.IsControl(c) && !(multiline && c is '\n' or '\r' or '\t')) || UnsafeText.IsMatch(value))
            Fail(label + "不能包含控制字符、HTML 或脚本");
        try { StrictUtf8.GetByteCount(value); }
        catch (EncoderFallbackException) { Fail(label + "包含无效 Unicode 字符"); }
    }
    private static void Enum(string value, string[] allowed, string label)
    {
        if (!allowed.Contains(value, StringComparer.Ordinal)) Fail(label + "不在允许范围");
    }
    private static void Date(DateTimeOffset value, string label)
    {
        if (value == default || value.Year is < 2000 or > 2100) Fail(label + "必须是 2000 到 2100 年的有效时间");
    }
    private static void ListBound<T>(List<T>? values, int maximum, string label)
    {
        if (values is null || values.Count > maximum) Fail($"{label}列表不能为 null 或超过 {maximum} 项");
    }
    [DoesNotReturn]
    private static void Fail(string message) => throw new AtlasException(400, "VALIDATION_FAILED", message);
}
