using System.Globalization;

namespace Hengchuan.Api;

public sealed record TimeWindow(DateTimeOffset From, DateTimeOffset To, string Label, string Timezone = "UTC");
public sealed record AvailabilityResult(double? Availability, double QuotaMinutes, double ConsumedMinutes, double RemainingMinutes, double ConsumedPercent, int EventCount, double? MttrMinutes, double? MtbfMinutes);
public sealed record SeriesPoint(DateTimeOffset Timestamp, double Value, double? ComparisonValue = null);
public sealed record ChartStats(double? Sum, double? Max, double? Min, double? Average, DateTimeOffset? LastUpdated, double? ComparisonDeltaPercent);
public sealed record ChartResult(string IndicatorId, string Name, string Unit, string SourceId, List<SeriesPoint> Points, ChartStats Stats, string? EmptyReason = null, string? Error = null, List<string>? Warnings = null, int? IntervalSeconds = null);
public sealed record BusinessDashboard(string BusinessId, TimeWindow Window, string Aggregation, string Comparison, List<ChartResult> Charts, string Mode);

public sealed class MetricsService(ElasticsearchAdapter adapter, DataSourceOptions options, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    public DateTimeOffset Now => clock.GetUtcNow();

    public static AvailabilityResult Calculate(Business business, IEnumerable<IncidentEvent> source, TimeWindow window)
    {
        double minutes = Math.Max(0, (window.To - window.From).TotalMinutes);
        if (minutes == 0) return new(null, 0, 0, 0, 0, 0, null, null);
        var intervals = source.Where(x => x.BusinessId == business.Id)
            .Select(x => new { Event = x, From = x.StartedAt > window.From ? x.StartedAt : window.From, To = (x.EndedAt ?? window.To) < window.To ? x.EndedAt!.Value : window.To })
            .Where(x => x.To > x.From).OrderBy(x => x.From).ToList();
        double downtime = 0;
        DateTimeOffset? start = null, end = null;
        foreach (var x in intervals)
        {
            if (start is null) { start = x.From; end = x.To; }
            else if (x.From <= end) { if (x.To > end) end = x.To; }
            else { downtime += (end!.Value - start.Value).TotalMinutes; start = x.From; end = x.To; }
        }
        if (start is not null) downtime += (end!.Value - start.Value).TotalMinutes;
        downtime = Math.Clamp(downtime, 0, minutes);
        var quota = minutes * (1 - business.TargetAvailability / 100);
        var resolved = intervals.Where(x => x.Event.EndedAt is not null).Select(x => (x.To - x.From).TotalMinutes).ToList();
        return new(100 * (1 - downtime / minutes), quota, downtime, Math.Max(0, quota - downtime), quota > 0 ? downtime / quota * 100 : 0,
            intervals.Count, resolved.Count > 0 ? resolved.Average() : null, intervals.Count > 0 ? (minutes - downtime) / intervals.Count : null);
    }

    public static object Summary(Business b, IEnumerable<IncidentEvent> events, IEnumerable<Indicator> indicators, TimeWindow window)
    {
        var m = Calculate(b, events, window);
        return new { b.Id, b.SpaceId, b.Name, b.Owner, b.Description, b.Service, b.Environment, b.TargetAvailability, b.UpdatedAt,
            m.Availability, ChartCount = indicators.Count(i => i.BusinessIds.Contains(b.Id)), m.QuotaMinutes, m.ConsumedMinutes, m.RemainingMinutes,
            m.ConsumedPercent, m.EventCount, m.MttrMinutes, m.MtbfMinutes };
    }

    public TimeWindow DashboardWindow(DateTimeOffset? from, DateTimeOffset? to)
    {
        var end = (to ?? Now).ToUniversalTime();
        var start = (from ?? end.AddHours(-12)).ToUniversalTime();
        if (end <= start || end - start > TimeSpan.FromDays(7) || end > Now)
            throw new AtlasException(400, "invalid_window", "时序窗口须为正数、最多7天且不能位于未来。");
        return new(start, end, "所选时序窗口；各图标注数据来源及采样口径");
    }

    public static TimeWindow StabilityWindow(int year, string period, string? anchor, DateTimeOffset now)
    {
        if (year is < 2020 or > 2100) throw new AtlasException(400, "invalid_year", "年度须在2020至2100之间。");
        var yearStart = new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var yearEnd = yearStart.AddYears(1);
        var date = year == now.Year ? DateOnly.FromDateTime(now.UtcDateTime) : new DateOnly(year, year < now.Year ? 12 : 1, year < now.Year ? 31 : 1);
        if (anchor is not null && (!DateOnly.TryParseExact(anchor, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) || date.Year != year))
            throw new AtlasException(400, "invalid_anchor", "日期须为所选年度内的YYYY-MM-DD。");
        var day = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        (DateTimeOffset from, DateTimeOffset to) = period switch
        {
            "year" => (yearStart, yearEnd),
            "month" => (new(year, date.Month, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(year, date.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1)),
            "week" => (day.AddDays(-((int)day.DayOfWeek + 6) % 7), day.AddDays(-((int)day.DayOfWeek + 6) % 7).AddDays(7)),
            "day" => (day, day.AddDays(1)),
            _ => throw new AtlasException(400, "invalid_period", "统计周期支持year/month/week/day。")
        };
        from = from < yearStart ? yearStart : from;
        to = to > yearEnd ? yearEnd : to;
        to = to > now ? now : to;
        if (to < from) to = from;
        return new(from, to, $"{year}年 / {period}；自然窗口与年度相交、截至当前；按所选等级事件统计");
    }

    public async Task<BusinessDashboard> DashboardAsync(AtlasState state, Business b, TimeWindow window, string aggregation, string comparison, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (aggregation is not ("avg" or "sum" or "raw") || comparison is not ("none" or "yesterday" or "lastWeek"))
            throw new AtlasException(400, "invalid_series_options", "聚合或对比选项无效。");
        var charts = new List<ChartResult>();
        foreach (var indicator in state.Indicators.Where(i => i.SpaceId == b.SpaceId && i.BusinessIds.Contains(b.Id)))
        {
            ct.ThrowIfCancellationRequested();
            charts.Add(await ChartAsync(indicator, b, window, aggregation, comparison, ct));
        }
        return new(b.Id, window, aggregation, comparison, charts, charts.All(c => c.SourceId == "demo") ? "demo" : "mixed");
    }

    public async Task<ChartResult> ChartAsync(Indicator i, Business business, TimeWindow window, string aggregation, string comparison, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var emptyStats = new ChartStats(null, null, null, null, null, null);
        if (!i.Enabled) return new(i.Id, i.Name, i.Unit, i.SourceId, [], emptyStats, "指标已停用");
        if (i.Behavior == "empty" && i.SourceId == "demo") return new(i.Id, i.Name, i.Unit, i.SourceId, [], emptyStats, "演示空态：该指标在所选窗口没有采样点");
        try
        {
            var warnings = new List<string>();
            var interval = aggregation == "raw" ? 300 : window.To - window.From > TimeSpan.FromDays(1) ? 21600 : 1800;
            var points = await ValuesAsync(i, business, window, aggregation, warnings, "当前窗口", ct);
            var shift = comparison == "yesterday" ? TimeSpan.FromDays(1) : comparison == "lastWeek" ? TimeSpan.FromDays(7) : TimeSpan.Zero;
            List<SeriesPoint> prior = [];
            if (shift != TimeSpan.Zero)
            {
                prior = await ValuesAsync(i, business, window with { From = window.From - shift, To = window.To - shift }, aggregation, warnings, "对比窗口", ct);
                if (prior.Count == 0) warnings.Add("对比窗口没有采样点，未填零或推断变化率。");
                var map = prior.ToDictionary(p => p.Timestamp + shift, p => p.Value);
                points = points.Select(p => p with { ComparisonValue = map.TryGetValue(p.Timestamp, out var v) ? v : null }).ToList();
            }
            if (points.Count == 0) return new(i.Id, i.Name, i.Unit, i.SourceId, [], emptyStats, "所选窗口没有采样数据；未补零", Warnings: warnings, IntervalSeconds: interval);
            var avg = points.Average(p => p.Value);
            var sum = points.Sum(p => p.Value);
            var paired = points.Where(p => p.ComparisonValue is not null).ToList();
            var compareCurrent = paired.Count > 0 ? paired.Average(p => p.Value) : 0;
            var prev = paired.Count > 0 ? paired.Average(p => p.ComparisonValue!.Value) : 0;
            if (comparison != "none" && paired.Count < points.Count) warnings.Add("对比覆盖不完整；变化率仅计算时间匹配的采样点，无匹配时不计算。");
            double? delta = paired.Count > 0 && prev != 0 ? (compareCurrent - prev) / Math.Abs(prev) * 100 : null;
            if (!double.IsFinite(avg) || !double.IsFinite(sum) || !double.IsFinite(prev) || !double.IsFinite(compareCurrent) || (delta is not null && !double.IsFinite(delta.Value)))
                return new(i.Id, i.Name, i.Unit, i.SourceId, [], emptyStats, null, "数值统计超出有限范围，未返回伪造数据。");
            return new(i.Id, i.Name, i.Unit, i.SourceId, points, new(sum, points.Max(p => p.Value), points.Min(p => p.Value), avg, points[^1].Timestamp, delta), Warnings: warnings, IntervalSeconds: interval);
        }
        catch (EsDataSourceException e) { return new(i.Id, i.Name, i.Unit, i.SourceId, [], emptyStats, null, e.Message); }
    }

    private async Task<List<SeriesPoint>> ValuesAsync(Indicator i, Business b, TimeWindow w, string aggregation, List<string> warnings, string scope, CancellationToken ct)
    {
        if (i.SourceId == "es-main")
        {
            if (aggregation == "raw") throw new EsDataSourceException(422, "ES适配器首版仅支持avg/sum，不支持原值(raw)，未将聚合值冒充原值。");
            if (options.AllowedIndices.Length == 0) throw new EsDataSourceException(503, "ES未配置允许索引，未建立真实连接。");
            var result = await adapter.QueryAsync(new(options.AllowedIndices[0], b.Service, b.Environment, i.MetricName, w.From, w.To, w.To - w.From > TimeSpan.FromDays(1) ? 21600 : 1800, aggregation), ct);
            warnings.AddRange(result.Warnings.Select(message => scope + "：" + message));
            if (result.Truncated) warnings.Add(scope + "：结果不完整，不能视为完整覆盖。");
            return result.Points.Select(p => new SeriesPoint(p.Timestamp, p.Value)).ToList();
        }
        if (i.SourceId != "demo") throw new AtlasException(422, "unknown_source", "未知数据源，未回落演示数据。");
        var sample = TimeSpan.FromMinutes(5).Ticks;
        var ticks = ((w.From.UtcTicks + sample - 1) / sample) * sample;
        var raw = new List<SeriesPoint>();
        for (; ticks < w.To.UtcTicks; ticks += sample)
        {
            ct.ThrowIfCancellationRequested();
            raw.Add(new(new DateTimeOffset(ticks, TimeSpan.Zero), DemoValue(i, ticks / sample)));
        }
        if (aggregation == "raw") return raw;
        var bucket = (w.To - w.From > TimeSpan.FromDays(1) ? TimeSpan.FromHours(6) : TimeSpan.FromMinutes(30)).Ticks;
        return raw.GroupBy(p => (p.Timestamp.UtcTicks - w.From.UtcTicks) / bucket).Select(g => new SeriesPoint(
            new DateTimeOffset(w.From.UtcTicks + g.Key * bucket, TimeSpan.Zero), aggregation == "sum" ? g.Sum(p => p.Value) : g.Average(p => p.Value))).ToList();
    }

    private static double DemoValue(Indicator i, long sample)
    {
        uint hash = 2166136261;
        foreach (var c in i.Id) hash = unchecked((hash ^ c) * 16777619);
        var wave = Math.Sin(sample / 9.0 + hash % 31) * 0.18 + Math.Sin(sample / 2.1 + hash % 7) * 0.045;
        var baseline = i.Unit == "%" ? 98.5 : i.Unit == "ms" ? 40 + hash % 140 : 80 + hash % 500;
        var value = Math.Max(0, baseline * (1 + wave));
        return Math.Round(i.Unit == "%" ? Math.Min(100, value) : value, 3);
    }
}
