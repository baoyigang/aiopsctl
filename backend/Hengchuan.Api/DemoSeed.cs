namespace Hengchuan.Api;

/// <summary>Original, isolated synthetic fixtures. No account credentials or enterprise records.</summary>
public static class DemoSeed
{
    public static AtlasState Create(DateTimeOffset now)
    {
        now = now.ToUniversalTime();
        var state = new AtlasState
        {
            Spaces = [
                new Space { Id = "space-default", Name = "核心交易" },
                new Space { Id = "space-growth", Name = "用户增长" },
                new Space { Id = "space-platform", Name = "基础平台" }
            ],
            Businesses = [
                Business("biz-checkout", "space-default", "订单结算", "交易应用组", "checkout", 99.95, now),
                Business("biz-payment", "space-default", "支付网关", "支付应用组", "payment", 99.9, now),
                Business("biz-member", "space-growth", "会员中心", "用户应用组", "member", 99.9, now),
                Business("biz-marketing", "space-growth", "营销活动", "增长应用组", "marketing", 99.5, now),
                Business("biz-notification", "space-platform", "消息通知", "平台应用组", "notification", 99.5, now),
                Business("biz-storage", "space-platform", "对象存储", "存储应用组", "storage", 99.9, now)
            ]
        };
        (string Id, string Name, string Metric, string Unit)[] checkout = [
            ("ind-request", "请求总量", "requests", "次"),
            ("ind-success", "请求成功率", "success-rate", "%"),
            ("ind-latency", "接口响应时间", "latency", "ms"),
            ("ind-order", "订单创建量", "orders", "单"),
            ("ind-payment", "支付完成量", "payments", "笔"),
            ("ind-error", "应用错误量", "errors", "次"),
            ("ind-database", "数据库访问耗时", "db-latency", "ms"),
            ("ind-queue", "队列积压", "queue-depth", "条"),
            ("ind-cpu", "计算资源利用率", "cpu", "%"),
            ("ind-memory", "内存使用率", "memory", "%"),
            ("ind-connection", "应用连接数", "connections", "个"),
            ("ind-cache", "缓存命中率", "cache-hit", "%"),
            ("ind-empty", "待采集的合成指标", "pending-sample", "次")
        ];
        foreach (var item in checkout)
            state.Indicators.Add(Indicator(item.Id, "space-default", "biz-checkout", item.Name, item.Metric, item.Unit, now) with
            { Behavior = item.Id == "ind-empty" ? "empty" : "normal" });
        state.Indicators.AddRange([
            Indicator("ind-payment-success", "space-default", "biz-payment", "支付请求成功率", "payment-success-rate", "%", now),
            Indicator("ind-member-active", "space-growth", "biz-member", "活跃会员数量", "active-members", "人", now),
            Indicator("ind-marketing-order", "space-growth", "biz-marketing", "活动订单量", "marketing-orders", "单", now),
            Indicator("ind-notification", "space-platform", "biz-notification", "消息投递量", "notification-delivery", "条", now),
            Indicator("ind-storage", "space-platform", "biz-storage", "存储访问成功率", "storage-success-rate", "%", now)
        ]);

        var yearStart = new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
        // The historical fixture remains within the chosen year even in early January.
        var historical = yearStart + TimeSpan.FromTicks((now - yearStart).Ticks / 3);
        state.Events.AddRange([
            Event("evt-checkout-recovered", "space-default", "biz-checkout", "合成演示：结算链路延迟已恢复", "P2", now.AddDays(-3).AddHours(-2), now.AddDays(-3).AddHours(-1.7)),
            Event("evt-checkout-active", "space-default", "biz-checkout", "合成演示：订单接口错误率升高", "P2", now.AddMinutes(-18), null),
            Event("evt-payment-recovered", "space-default", "biz-payment", "合成演示：支付回调短时积压", "P1", now.AddDays(-1).AddHours(-3), now.AddDays(-1).AddHours(-2.8)),
            Event("evt-member-recovered", "space-growth", "biz-member", "合成演示：会员读取超时", "P3", now.AddDays(-5), now.AddDays(-5).AddMinutes(9)),
            Event("evt-platform-recovered", "space-platform", "biz-storage", "合成演示：存储访问短时波动", "P2", historical, historical.AddMinutes(Math.Min(24, Math.Max(0, (now - historical).TotalMinutes)))),
            Event("evt-notification-active", "space-platform", "biz-notification", "合成演示：通知消费延迟", "P3", now.AddMinutes(-7), null),
            Event("evt-marketing-year", "space-growth", "biz-marketing", "合成演示：年度活动容量回顾", "P3", historical.AddMinutes(-20), historical.AddMinutes(-5))
        ]);
        state.Screens.AddRange([
            Screen("screen-checkout", "space-default", "biz-checkout", "交易值守大屏", checkout.Select((x, index) => Panel(x.Id, index)).ToList(), now),
            Screen("screen-growth", "space-growth", "biz-member", "会员运营大屏", [new ScreenPanel { IndicatorId = "ind-member-active", Kind = "bar", Aggregation = "sum" }], now),
            Screen("screen-platform", "space-platform", "biz-storage", "基础设施大屏", [new ScreenPanel { IndicatorId = "ind-storage", Kind = "line", Aggregation = "avg" }], now)
        ]);
        AtlasValidation.Validate(state);
        return state;
    }

    private static Business Business(string id, string spaceId, string name, string owner, string service, double target, DateTimeOffset now) => new()
    {
        Id = id, SpaceId = spaceId, Name = name, Owner = owner, Service = service,
        Description = "隔离合成业务，仅用于演示配置、图表与稳定性统计。", Environment = "demo",
        TargetAvailability = target, UpdatedAt = now
    };
    private static Indicator Indicator(string id, string spaceId, string businessId, string name, string metric, string unit, DateTimeOffset now) => new()
    {
        Id = id, SpaceId = spaceId, Name = name, MetricName = metric, Unit = unit,
        Owner = "演示应用组", SourceId = "demo", SourceName = "隔离合成数据", Visibility = "space",
        BusinessIds = [businessId], Behavior = "normal", Enabled = true, UpdatedAt = now, UpdatedBy = "demo-operator"
    };
    private static IncidentEvent Event(string id, string spaceId, string businessId, string title, string level, DateTimeOffset start, DateTimeOffset? end) => new()
    {
        Id = id, SpaceId = spaceId, BusinessId = businessId, Title = title, Level = level,
        StartedAt = start, EndedAt = end, Description = "原创合成事件，未连接任何企业生产系统；不执行自动修复。"
    };
    private static ScreenPanel Panel(string id, int index) => new()
    { IndicatorId = id, Kind = index % 3 == 1 ? "bar" : index % 3 == 2 ? "funnel" : "line", Aggregation = index % 4 == 0 ? "sum" : "avg" };
    private static Screen Screen(string id, string spaceId, string businessId, string name, List<ScreenPanel> panels, DateTimeOffset now) => new()
    { Id = id, SpaceId = spaceId, BusinessId = businessId, Name = name, Columns = 3, Panels = panels, UpdatedAt = now, UpdatedBy = "demo-operator" };
}
