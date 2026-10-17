using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hengchuan.Api;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 160 * 1024);
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
builder.Services.ConfigureHttpJsonOptions(o => { o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow; });
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins("http://127.0.0.1:5173", "http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));
var esOptions = builder.Configuration.GetSection("Elasticsearch").Get<DataSourceOptions>() ?? new();
builder.Services.AddSingleton(esOptions);
builder.Services.AddSingleton(new ElasticsearchAdapter(esOptions));
builder.Services.AddSingleton<MetricsService>();
var dbPath = Path.GetFullPath(builder.Configuration["Storage:Path"] ?? "data/hengchuan.db", builder.Environment.ContentRootPath);
var store = new AtlasStore(dbPath);
await store.InitializeAsync();
builder.Services.AddSingleton(store);
var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; frame-ancestors 'none'; connect-src 'self'";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    try
    {
        var ip = context.Connection.RemoteIpAddress;
        if ((ip is not null && !IPAddress.IsLoopback(ip)) || context.Request.Host.Host is not ("127.0.0.1" or "localhost" or "::1" or "[::1]"))
            throw new AtlasException(403, "local_only", "首版仅供本机回环演示；尚未配置生产身份认证。");
        if (context.Request.Path.StartsWithSegments("/api") && context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
        {
            if (context.Request.Headers["X-Hengchuan-Write"] != "1") throw new AtlasException(403, "write_header_required", "写入须来自受支持的本地客户端。");
            var origin = context.Request.Headers.Origin.ToString();
            var currentOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
            if (origin.Length > 0 && origin != currentOrigin && origin is not ("http://127.0.0.1:5173" or "http://localhost:5173"))
                throw new AtlasException(403, "invalid_origin", "拒绝来自其他站点的写入请求。");
        }
        await next();
    }
    catch (AtlasException ex)
    {
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = ex.StatusCode;
        await context.Response.WriteAsJsonAsync(new { ex.Code, ex.Message });
    }
    catch (BadHttpRequestException ex)
    {
        context.Response.StatusCode = ex.StatusCode;
        await context.Response.WriteAsJsonAsync(new { Code = "invalid_request", Message = "请求格式不正确或超过160KiB限制。" });
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { context.Response.StatusCode = 499; }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Local request failed: {Method} {Path}", context.Request.Method, context.Request.Path);
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new { Code = "server_error", Message = "操作未能完成，请查看本地服务运行记录。" });
    }
});
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => new { Status = "ok", Mode = "demo", Product = "衡川观测" });
app.MapGet("/api/meta", async (AtlasStore db, MetricsService metrics, CancellationToken ct) =>
{
    var s = await db.ReadAsync(ct);
    return Results.Ok(new { Product = "衡川观测", Version = "0.1.0", Mode = "demo", Now = metrics.Now, s.Revision, s.State.Spaces,
        SourceStatus = new[] {
            new { Id = "demo", Label = "隔离合成数据", Kind = "demo", State = "demo", Detail = "当前样本为原创合成指标及事件，不代表已接入企业监控。" },
            new { Id = "es-main", Label = "Elasticsearch只读适配", Kind = "elasticsearch", State = esOptions.Enabled ? "configured" : "disabled", Detail = esOptions.Enabled ? "服务端已配置；不代表真实连接已验证，指标查询失败会明确显示。" : "默认关闭。需服务端允许索引、服务、环境及数值字段配置；未经企业实测。" },
            new { Id = "oracle", Label = "Oracle来源扩展", Kind = "oracle", State = "unconnected", Detail = "尚未接入；未访问Oracle、alert/trace/listener或AWR/ASH。" }
        } });
});

app.MapGet("/api/overview", async (HttpRequest r, AtlasStore db, MetricsService metrics, CancellationToken ct) =>
{
    var s = await db.ReadAsync(ct); var space = Api.SpaceId(r, s.State); var search = Api.Query(r, "search");
    var window = new TimeWindow(metrics.Now.AddDays(-30), metrics.Now, "最近30天滚动窗口；UTC；事件停机区间取并集");
    var businesses = s.State.Businesses.Where(b => b.SpaceId == space && (Api.Contains(b.Name, search) || Api.Contains(b.Owner, search) || Api.Contains(b.Service, search)));
    return Results.Ok(new { s.Revision, Window = window, Items = businesses.Select(b => MetricsService.Summary(b, s.State.Events, s.State.Indicators, window)).ToList() });
});
app.MapGet("/api/businesses/{id}", async (string id, AtlasStore db, CancellationToken ct) =>
{
    var s = await db.ReadAsync(ct); var b = Api.Business(s.State, id);
    return Results.Ok(new { s.Revision, Data = b, Indicators = s.State.Indicators.Where(i => i.SpaceId == b.SpaceId && i.BusinessIds.Contains(id)).ToList() });
});
app.MapGet("/api/businesses/{id}/dashboard", async (string id, HttpRequest r, AtlasStore db, MetricsService metrics, CancellationToken ct) =>
{
    var s = await db.ReadAsync(ct); var b = Api.Business(s.State, id);
    return Results.Ok(await metrics.DashboardAsync(s.State, b, metrics.DashboardWindow(Api.Date(r, "from"), Api.Date(r, "to")), Api.Query(r, "aggregation") ?? "avg", Api.Query(r, "comparison") ?? "none", ct));
});
app.MapPost("/api/spaces", async (WriteRequest<SpaceInput> body, AtlasStore db, CancellationToken ct) =>
{
    var data = Api.Data(body); var id = Api.Id("space");
    var s = await db.UpdateAsync(body.Revision, state => state.Spaces.Add(new() { Id = id, Name = Api.Trim(data.Name) }), ct);
    return Results.Ok(new { s.Revision, Data = s.State.Spaces.Single(x => x.Id == id) });
});
app.MapPost("/api/businesses", async (WriteRequest<Business> body, AtlasStore db, CancellationToken ct) =>
{
    var data = Api.Data(body) with { Id = Api.Id("b"), UpdatedAt = DateTimeOffset.UtcNow };
    var s = await db.UpdateAsync(body.Revision, state => state.Businesses.Add(data), ct);
    return Results.Ok(new { s.Revision, Data = data });
});
app.MapPut("/api/businesses/{id}", async (string id, WriteRequest<Business> body, AtlasStore db, CancellationToken ct) =>
{
    var data = Api.Data(body) with { Id = id, UpdatedAt = DateTimeOffset.UtcNow };
    var s = await db.UpdateAsync(body.Revision, state => { Api.Business(state, id); state.Businesses.RemoveAll(b => b.Id == id); state.Businesses.Add(data); }, ct);
    return Results.Ok(new { s.Revision, Data = data });
});
app.MapDelete("/api/businesses/{id}", async (string id, long revision, AtlasStore db, CancellationToken ct) =>
{
    var s = await db.UpdateAsync(revision, state => { Api.Business(state, id); state.Businesses.RemoveAll(b => b.Id == id); }, ct);
    return Results.Ok(new { s.Revision, DeletedId = id });
});

app.MapGet("/api/indicators", async (HttpRequest r, AtlasStore db, CancellationToken ct) =>
{
    var s = await db.ReadAsync(ct); var space = Api.SpaceId(r, s.State); var businessId = Api.Query(r, "businessId");
    if (businessId is not null && Api.Business(s.State, businessId).SpaceId != space) throw new AtlasException(400, "scope_mismatch", "业务筛选与当前空间不一致。");
    var sourceId = Api.Query(r, "sourceId"); var enabled = Api.Bool(r, "enabled"); var unreferenced = Api.Bool(r, "unreferenced") ?? false;
    var search = Api.Query(r, "search"); var sourceName = Api.Query(r, "sourceName");
    var page = Api.Number(r, "page", 1, 1, 100000); var pageSize = Api.Number(r, "pageSize", 10, 1, 100);
    var query = s.State.Indicators.Where(i => i.SpaceId == space && (sourceId is null || i.SourceId == sourceId) && (enabled is null || i.Enabled == enabled)
        && (businessId is null || i.BusinessIds.Contains(businessId)) && (!unreferenced || i.BusinessIds.Count == 0) && Api.Contains(i.Name, search) && Api.Contains(i.SourceName, sourceName)).OrderBy(i => i.Name).ToList();
    return Results.Ok(new { s.Revision, Items = query.Skip((page - 1) * pageSize).Take(pageSize), Total = query.Count, Page = page, PageSize = pageSize });
});
app.MapPost("/api/indicators", async (WriteRequest<Indicator> body, AtlasStore db, CancellationToken ct) =>
{
    var data = Api.Data(body) with { Id = Api.Id("i"), UpdatedAt = DateTimeOffset.UtcNow, UpdatedBy = "demo-operator" };
    var s = await db.UpdateAsync(body.Revision, state => state.Indicators.Add(data), ct);
    return Results.Ok(new { s.Revision, Data = data });
});
app.MapPut("/api/indicators/{id}", async (string id, WriteRequest<Indicator> body, AtlasStore db, CancellationToken ct) =>
{
    var data = Api.Data(body) with { Id = id, UpdatedAt = DateTimeOffset.UtcNow, UpdatedBy = "demo-operator" };
    var s = await db.UpdateAsync(body.Revision, state => { Api.Indicator(state, id); state.Indicators.RemoveAll(i => i.Id == id); state.Indicators.Add(data); }, ct);
    return Results.Ok(new { s.Revision, Data = data });
});
app.MapDelete("/api/indicators/{id}", async (string id, long revision, AtlasStore db, CancellationToken ct) =>
{
    var s = await db.UpdateAsync(revision, state => { Api.Indicator(state, id); state.Indicators.RemoveAll(i => i.Id == id); }, ct);
    return Results.Ok(new { s.Revision, DeletedId = id });
});
app.MapPost("/api/indicators/batch", async (BatchRequest body, AtlasStore db, CancellationToken ct) =>
{
    if (body.Ids is null || body.Ids.Count is < 1 or > 100 || body.Ids.Distinct().Count() != body.Ids.Count) throw new AtlasException(400, "invalid_batch", "批量操作须包含1至100个不重复指标。");
    var s = await db.UpdateAsync(body.Revision, state =>
    {
        if (!state.Spaces.Any(x => x.Id == body.SpaceId)) throw new AtlasException(404, "space_not_found", "空间不存在。");
        foreach (var id in body.Ids) if (Api.Indicator(state, id).SpaceId != body.SpaceId) throw new AtlasException(400, "scope_mismatch", "批量指标须全部属于当前空间。");
        for (int n = 0; n < state.Indicators.Count; n++) if (body.Ids.Contains(state.Indicators[n].Id)) state.Indicators[n] = state.Indicators[n] with { Enabled = body.Enabled, UpdatedAt = DateTimeOffset.UtcNow, UpdatedBy = "demo-operator" };
    }, ct);
    return Results.Ok(new { s.Revision, UpdatedCount = body.Ids.Count });
});

app.MapGet("/api/statuspage", async (AtlasStore db, MetricsService metrics, CancellationToken ct) =>
{
    var s = await db.ReadAsync(ct); var now = metrics.Now; var start = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddDays(-6);
    var days = Enumerable.Range(0, 7).Select(n => start.AddDays(n).ToString("yyyy-MM-dd")).ToList();
    var spaces = s.State.Spaces.Select(space => new { space.Id, space.Name, Businesses = s.State.Businesses.Where(b => b.SpaceId == space.Id).Select(b => new
    {
        b.Id, b.Name, LatestStatus = s.State.Events.Any(e => e.BusinessId == b.Id && e.StartedAt <= now && (e.EndedAt is null || e.EndedAt > now)) ? "incident" : "healthy",
        Days = Enumerable.Range(0, 7).Select(n =>
        {
            var from = start.AddDays(n); var to = from.AddDays(1) < now ? from.AddDays(1) : now;
            var m = MetricsService.Calculate(b, s.State.Events, new(from, to, "UTC日窗口"));
            return new { Date = days[n], Status = m.Availability is null ? "no-data" : m.EventCount > 0 ? "incident" : "healthy", m.Availability, m.EventCount };
        }).ToList()
    }).ToList() }).ToList();
    return Results.Ok(new { Days = days, Spaces = spaces, Timezone = "UTC", WindowLabel = "最近7个UTC自然日；今日截至当前；最近状态表示当前未恢复事件" });
});
app.MapGet("/api/stability", async (HttpRequest r, AtlasStore db, MetricsService metrics, CancellationToken ct) =>
{
    var s = await db.ReadAsync(ct); var space = Api.SpaceId(r, s.State); var year = Api.Number(r, "year", metrics.Now.Year, 2020, 2100);
    var level = Api.Query(r, "level") ?? "all";
    if (level is not ("all" or "P1" or "P2" or "P3")) throw new AtlasException(400, "invalid_level", "事件等级筛选无效。");
    var window = MetricsService.StabilityWindow(year, Api.Query(r, "period") ?? "year", Api.Query(r, "anchor"), metrics.Now);
    var businesses = s.State.Businesses.Where(b => b.SpaceId == space).ToList();
    var events = s.State.Events.Where(e => e.SpaceId == space && (level == "all" || e.Level == level) && e.StartedAt < window.To && (e.EndedAt ?? window.To) > window.From).OrderByDescending(e => e.StartedAt).ToList();
    var values = businesses.Select(b => MetricsService.Calculate(b, events, window)).ToList();
    var items = window.To > window.From ? businesses.Select(b => MetricsService.Summary(b, events, s.State.Indicators, window)).ToList() : [];
    return Results.Ok(new { s.Revision, Window = window, Summary = new { BusinessCount = items.Count, BelowTargetCount = values.Select((v, n) => v.Availability is not null && v.Availability < businesses[n].TargetAvailability).Count(v => v), EventCount = events.Count, ConsumedMinutes = values.Sum(v => v.ConsumedMinutes), QuotaMinutes = values.Sum(v => v.QuotaMinutes) }, Items = items, Events = events });
});
app.MapGet("/api/events/{id}", async (string id, AtlasStore db, CancellationToken ct) => { var s = await db.ReadAsync(ct); return Results.Ok(new { s.Revision, Data = Api.Event(s.State, id) }); });
app.MapPost("/api/events", async (WriteRequest<IncidentEvent> body, AtlasStore db, CancellationToken ct) =>
{
    var data = Api.Data(body) with { Id = Api.Id("e") }; Api.CheckEventTime(data);
    var s = await db.UpdateAsync(body.Revision, state => state.Events.Add(data), ct);
    return Results.Ok(new { s.Revision, Data = data });
});
app.MapPut("/api/events/{id}", async (string id, WriteRequest<IncidentEvent> body, AtlasStore db, CancellationToken ct) =>
{
    var data = Api.Data(body) with { Id = id }; Api.CheckEventTime(data);
    var s = await db.UpdateAsync(body.Revision, state => { Api.Event(state, id); state.Events.RemoveAll(e => e.Id == id); state.Events.Add(data); }, ct);
    return Results.Ok(new { s.Revision, Data = data });
});

app.MapGet("/api/screens", async (HttpRequest r, AtlasStore db, CancellationToken ct) =>
{
    var s = await db.ReadAsync(ct); var space = Api.SpaceId(r, s.State); var search = Api.Query(r, "search");
    var page = Api.Number(r, "page", 1, 1, 100000); var pageSize = Api.Number(r, "pageSize", 10, 1, 100);
    var query = s.State.Screens.Where(x => x.SpaceId == space && Api.Contains(x.Name, search)).OrderByDescending(x => x.UpdatedAt).ToList();
    return Results.Ok(new { s.Revision, Items = query.Skip((page - 1) * pageSize).Take(pageSize), Total = query.Count, Page = page, PageSize = pageSize });
});
app.MapGet("/api/screens/{id}", async (string id, AtlasStore db, CancellationToken ct) => { var s = await db.ReadAsync(ct); return Results.Ok(new { s.Revision, Data = Api.Screen(s.State, id) }); });
app.MapGet("/api/screens/{id}/preview", async (string id, AtlasStore db, MetricsService metrics, CancellationToken ct) =>
{
    var s = await db.ReadAsync(ct); var screen = Api.Screen(s.State, id); var b = Api.Business(s.State, screen.BusinessId);
    var window = metrics.DashboardWindow(null, null); var charts = new List<ChartResult>();
    foreach (var panel in screen.Panels) charts.Add(await metrics.ChartAsync(Api.Indicator(s.State, panel.IndicatorId), b, window, panel.Aggregation, "none", ct));
    return Results.Ok(new { s.Revision, Screen = screen, Dashboard = new BusinessDashboard(b.Id, window, "per-panel", "none", charts, charts.All(c => c.SourceId == "demo") ? "demo" : "mixed") });
});
app.MapGet("/api/screens/{id}/export", async (string id, AtlasStore db, CancellationToken ct) =>
{
    var s = await db.ReadAsync(ct); var screen = Api.Screen(s.State, id);
    return Results.Ok(new ScreenDocument("hengchuan-screen", 1, new(screen.Name, screen.BusinessId, screen.Columns, screen.Panels)));
});
app.MapPost("/api/screens", async (WriteRequest<Screen> body, AtlasStore db, CancellationToken ct) =>
{
    var data = Api.Data(body) with { Id = Api.Id("s"), UpdatedAt = DateTimeOffset.UtcNow, UpdatedBy = "demo-operator" };
    var s = await db.UpdateAsync(body.Revision, state => state.Screens.Add(data), ct);
    return Results.Ok(new { s.Revision, Data = data });
});
app.MapPut("/api/screens/{id}", async (string id, WriteRequest<Screen> body, AtlasStore db, CancellationToken ct) =>
{
    var data = Api.Data(body) with { Id = id, UpdatedAt = DateTimeOffset.UtcNow, UpdatedBy = "demo-operator" };
    var s = await db.UpdateAsync(body.Revision, state => { Api.Screen(state, id); state.Screens.RemoveAll(x => x.Id == id); state.Screens.Add(data); }, ct);
    return Results.Ok(new { s.Revision, Data = data });
});
app.MapDelete("/api/screens/{id}", async (string id, long revision, AtlasStore db, CancellationToken ct) =>
{
    var s = await db.UpdateAsync(revision, state => { Api.Screen(state, id); state.Screens.RemoveAll(x => x.Id == id); }, ct);
    return Results.Ok(new { s.Revision, DeletedId = id });
});
app.MapPost("/api/screens/import", async (ScreenImportRequest body, AtlasStore db, CancellationToken ct) =>
{
    if (body.Document?.Format != "hengchuan-screen" || body.Document.Version != 1 || body.Document.Screen is null)
        throw new AtlasException(400, "invalid_format", "仅支持hengchuan-screen version 1 JSON格式。");
    if (JsonSerializer.SerializeToUtf8Bytes(body.Document).Length > 128 * 1024) throw new AtlasException(413, "import_too_large", "导入文档不能超过128KiB。");
    var imported = body.Document.Screen;
    var data = new Screen { Id = Api.Id("s"), SpaceId = body.SpaceId, Name = imported.Name, BusinessId = imported.BusinessId, Columns = imported.Columns, Panels = imported.Panels, UpdatedAt = DateTimeOffset.UtcNow, UpdatedBy = "demo-operator" };
    var s = await db.UpdateAsync(body.Revision, state => state.Screens.Add(data), ct);
    return Results.Ok(new { s.Revision, Data = data });
});

app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api")) { context.Response.StatusCode = 404; await context.Response.WriteAsJsonAsync(new { Code = "not_found", Message = "API不存在。" }); return; }
    var index = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "index.html");
    if (context.Request.Method is not ("GET" or "HEAD") || !File.Exists(index)) { context.Response.StatusCode = 404; return; }
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(index);
});
app.Run();

public sealed record WriteRequest<T>(long Revision, T? Data) where T : class;
public sealed record SpaceInput(string Name);
public sealed record BatchRequest(long Revision, string SpaceId, List<string> Ids, bool Enabled);
public sealed record ScreenImportData(string Name, string BusinessId, int Columns, List<ScreenPanel> Panels);
public sealed record ScreenDocument(string Format, int Version, ScreenImportData Screen);
public sealed record ScreenImportRequest(long Revision, string SpaceId, ScreenDocument? Document);

public static class Api
{
    public static string Id(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N");
    public static string Trim(string? value) => value?.Trim() ?? "";
    public static T Data<T>(WriteRequest<T> request) where T : class => request.Data ?? throw new AtlasException(400, "missing_data", "缺少data对象。");
    public static bool Contains(string value, string? search) => search is null || value.Contains(search, StringComparison.OrdinalIgnoreCase);
    public static string? Query(HttpRequest r, string key)
    {
        if (r.Query[key].Count > 1) throw new AtlasException(400, "invalid_query", "筛选参数不能重复。");
        var v = r.Query[key].ToString().Trim();
        if (v.Length > 200 || v.Any(char.IsControl)) throw new AtlasException(400, "invalid_query", "筛选参数过长或包含控制字符。");
        return v.Length == 0 ? null : v;
    }
    public static bool? Bool(HttpRequest r, string key) => Query(r, key) is { } value ? bool.TryParse(value, out var result) ? result : throw new AtlasException(400, "invalid_query", "布尔筛选值须为true/false。") : null;
    public static int Number(HttpRequest r, string key, int fallback, int min, int max) => Query(r, key) is { } value ? int.TryParse(value, out var result) && result >= min && result <= max ? result : throw new AtlasException(400, "invalid_query", "分页或年度范围无效。") : fallback;
    public static DateTimeOffset? Date(HttpRequest r, string key)
    {
        var value = Query(r, key); if (value is null) return null;
        if (!(value.EndsWith('Z') || System.Text.RegularExpressions.Regex.IsMatch(value, "[+-][0-9]{2}:[0-9]{2}$")) || !DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var date))
            throw new AtlasException(400, "invalid_date", "时间须为含时区的ISO8601格式。");
        return date.ToUniversalTime();
    }
    public static string SpaceId(HttpRequest r, AtlasState state)
    {
        var id = Query(r, "spaceId") ?? "space-default";
        if (!state.Spaces.Any(x => x.Id == id)) throw new AtlasException(404, "space_not_found", "空间不存在。");
        return id;
    }
    public static Business Business(AtlasState s, string id) => s.Businesses.SingleOrDefault(b => b.Id == id) ?? throw new AtlasException(404, "business_not_found", "业务不存在。");
    public static Indicator Indicator(AtlasState s, string id) => s.Indicators.SingleOrDefault(i => i.Id == id) ?? throw new AtlasException(404, "indicator_not_found", "指标不存在。");
    public static Screen Screen(AtlasState s, string id) => s.Screens.SingleOrDefault(i => i.Id == id) ?? throw new AtlasException(404, "screen_not_found", "大屏不存在。");
    public static IncidentEvent Event(AtlasState s, string id) => s.Events.SingleOrDefault(i => i.Id == id) ?? throw new AtlasException(404, "event_not_found", "事件不存在。");
    public static void CheckEventTime(IncidentEvent e) { if (e.StartedAt > DateTimeOffset.UtcNow || e.EndedAt > DateTimeOffset.UtcNow) throw new AtlasException(400, "future_event", "事件时间不能位于未来。"); }
}

public partial class Program { }
