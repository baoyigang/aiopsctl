# 衡川观测 API v1（实现契约）

原创实现，规格来自父线程合法登录后的只读观察文字；未提供截图，未复用 Flashcat 源码/商标/素材，REA页面分析未成功。服务默认仅127.0.0.1，默认合成demo。camelCase JSON，时间ISO8601 UTC，数值不使用NaN/Infinity。变更请求必须有`X-Hengchuan-Write: 1`；浏览器Origin限同源或本地Vite。未实现生产身份认证，不可公开部署。

## 核心持久化领域（namespace Hengchuan.Api，record with init）

`AtlasState`: SchemaVersion(int=1), Spaces(List<Space>), Businesses(List<Business>), Indicators(List<Indicator>), Events(List<IncidentEvent>), Screens(List<Screen>).

- `Space`: Id, Name (string)。
- `Business`: Id, SpaceId, Name, Owner, Description, Service, Environment (string), TargetAvailability(double,90..99.999), UpdatedAt(DateTimeOffset)。
- `Indicator`: Id, SpaceId, Name, SourceId(`demo`或`es-main`), SourceName, MetricName, Unit, Owner, Visibility(`space`/`public`), Enabled(bool), BusinessIds(List<string>), Behavior(`normal`/`empty`), UpdatedAt(DateTimeOffset), UpdatedBy(string)。
- `IncidentEvent`: Id, SpaceId, BusinessId, Title, Level(`P1`/`P2`/`P3`), StartedAt(DateTimeOffset), EndedAt(DateTimeOffset?), Description(string)。
- `Screen`: Id, SpaceId, Name, BusinessId(string), Columns(int,1..3), Panels(List<ScreenPanel>), UpdatedAt(DateTimeOffset), UpdatedBy(string)。
- `ScreenPanel`: IndicatorId(string), Kind(`line`/`bar`/`funnel`), Aggregation(`avg`/`sum`/`raw`)。
- `StoreSnapshot`: Revision(long), State(AtlasState)。`AtlasException(int statusCode,string code,string message)`，属性StatusCode/Code。

`AtlasStore(string dbPath)`，`InitializeAsync(CancellationToken=default)`，`ReadAsync(CancellationToken=default)`，`UpdateAsync(long expectedRevision,Action<AtlasState> mutate,CancellationToken=default)`返回StoreSnapshot。SQLite单行JSON快照，schema版本/revision，事务原子更新；每次返回隔离快照。写前全量校验引用、空间一致性、长度/控制字符、重复名称、枚举、时间、数值；错revision抛409。`DemoSeed.Create(DateTimeOffset now)`生成纯合成数据。`AtlasValidation.Validate(AtlasState)`供导入/测试。至少3空间6业务18指标，`biz-checkout`业务关联13指标（其中`ind-empty`真正空态），含已恢复/进行中及近期/本年事件，至少3大屏。稳定种子ID可在README记录。

## 通用写入

POST/PUT body `{revision: number,data: entityPayload}`，成功`{revision,data: entity}`。DELETE body无需data，用`?revision=N`，成功`{revision,deletedId}`。批量及导入也是显式revision。失败统一`{code,message,currentRevision?}`。所有写操作原子，失败不改变revision；updatedBy固定`demo-operator`，明确演示身份而非认证。Id由服务端生成`b-/i-/s-/e-`+GUID，客户端不能指定创建Id。PUT是完整实体可编辑字段（不含Id/UpdatedAt/UpdatedBy）。

## 读取

- `GET /api/health` => `{status:"ok",mode:"demo",product:"衡川观测"}`。
- `GET /api/meta` => `{product,version,mode:"demo",now,revision,spaces,sourceStatus:[{id,label,kind,state,detail}]}`。状态demo/es disabled/oracle unconnected，配置不等于真实接通。
- `GET /api/overview?spaceId=space-default&search=` => `{revision,window:{from,to,label,timezone:"UTC"},items:[BusinessSummary]}`。窗口最近30天。
- BusinessSummary为Business字段加`availability,chartCount,quotaMinutes,consumedMinutes,remainingMinutes,consumedPercent,eventCount`。availability/百分比double。consumedPercent可大于100，前端条宽clamp但文本真实。
- `GET /api/businesses/{id}` => `{revision,data:Business,indicators:Indicator[]}`。
- `GET /api/businesses/{id}/dashboard?from=ISO&to=ISO&aggregation=avg&comparison=none` => `{businessId,window:{from,to,label,timezone},aggregation,comparison,charts:[ChartResult],mode:"demo"}`。时间<=7天且不未来；aggregation avg/sum/raw，comparison none/yesterday/lastWeek；默认最近12h。
- ChartResult `{indicatorId,name,unit,sourceId,points:[{timestamp,value,comparisonValue?}],stats:{sum,max,min,average,lastUpdated,comparisonDeltaPercent},emptyReason?,error?,warnings?,intervalSeconds?}`。空态points=[]且stats数值null。按5分钟原始合成采样，avg/sum从from起用30min桶（>24h用6h），raw保留5min原值；stats基于返回采样点，比较对应前1/7天，仅用时间配对采样计算变化率，覆盖不完整显式警告。缺桶不补零且前端断线。不将合成值称真实监控。存在ES图时mode为mixed；各图sourceId仍为最终来源依据。
- `GET /api/indicators?spaceId=&sourceId=&enabled=true|false&businessId=&sourceName=&search=&unreferenced=true&page=1&pageSize=10` => `{revision,items:Indicator[],total,page,pageSize}`，pageSize1..100。跨空间businessId必须拒绝；筛选为AND，unreferenced只取BusinessIds空。
- `GET /api/statuspage` => `{days:["YYYY-MM-DD"],spaces:[{id,name,businesses:[{id,name,latestStatus:"healthy"|"incident",days:[{date,status:"healthy"|"incident"|"no-data",availability,eventCount}]}]}],timezone:"UTC",windowLabel}`。最近7个UTC自然日含今日，今日计算到now。
- `GET /api/stability?spaceId=&year=2026&level=all&period=year|month|week|day&anchor=YYYY-MM-DD` => `{revision,window:{from,to,label,timezone},summary:{businessCount,belowTargetCount,eventCount,consumedMinutes,quotaMinutes},items:[BusinessSummary加mttrMinutes?,mtbfMinutes?],events:IncidentEvent[]}`。year必须2020..2100；period年/月/ISO周/日由anchor选择，窗口与所选year相交并截至now；anchor默认现在或选年末。level all/P1/P2/P3筛选事件，必须标明该口径影响所有事件派生统计。未来无观测窗口返回空态，禁止负数/假100%。
- `GET /api/events/{id}` => `{revision,data:IncidentEvent}`。
- `GET /api/screens?spaceId=&search=&page=1&pageSize=10` => `{revision,items:Screen[],total,page,pageSize}`。
- `GET /api/screens/{id}` => `{revision,data:Screen}`。`GET /api/screens/{id}/preview` => `{revision,screen:Screen,dashboard:BusinessDashboard}`，仅取screen配置panels映射图；每panel独立aggregation，kind由panel。
- `GET /api/screens/{id}/export` => `{format:"hengchuan-screen",version:1,screen:{name,businessId,columns,panels}}`（不含id、时间或凭据）。

## 编辑端点

`POST /api/spaces` data `{name}`。
`POST /api/businesses`、`PUT /api/businesses/{id}` data业务全部可编辑字段；`DELETE`仅无指标/事件/大屏引用可删。
`POST /api/indicators`、`PUT /api/indicators/{id}` data上述Indicator除id/updatedAt/updatedBy；`DELETE`仅未引用业务且未被大屏使用可删。
`POST /api/indicators/batch` body `{revision,ids:string[],enabled:boolean}` => `{revision,updatedCount}`；1..100个去重ID且同space（body另含spaceId），全量校验任一不存在/跨space则失败不部分更新。
`POST /api/events` data `{spaceId,businessId,title,level,startedAt,endedAt?,description}`，编辑为独立设计；`PUT /api/events/{id}` 同字段；不提供自动修复。
`POST /api/screens`、`PUT /api/screens/{id}` data `{spaceId,name,businessId,columns,panels}`；DELETE支持。
`POST /api/screens/import` body `{revision,spaceId,document:exportObject}`。JSON最大128KiB、固定format/version、禁止未知导入字段，校验存在的同空间business与指标引用、columns/panels数量(1..24)/枚举/名称，无脚本/URL/HTML执行，重复name返回409不覆盖。

## 统计定义

首页最近30天；稳定性选定自然年/月份/ISO周/日窗口，均UTC并截至now，口径不强行统一。downtime为窗口内事件区间并集，重叠不重复计；持续事件截止窗口末。availability=(1-downtime/observedMinutes)*100；quota=observedMinutes*(1-target/100)；remaining=max(0,quota-downtime)；eventCount为相交原始事件数。MTTR为相交已恢复事件在窗口内截断时长平均，无已恢复事件null；MTBF=(observedMinutes-downtime)/eventCount，无事件null。level筛选作用于事件集合，应界面注明。健康矩阵同定义按日。

## 数据源

根维护DataSourceOptions/ElasticsearchAdapter。demo默认；ES只读可配置但未经真实验证，Oracle明确未接入。禁止访问公司ES/Oracle、索要凭据、自动配置连接或把外部请求失败变演示成功。Adapter接口见对应文件，不由客户端DSL选择范围。演示与真实源在每次结果显式标识。
