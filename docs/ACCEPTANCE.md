# 首版验收记录

日期：2026-10-17 UTC。对象：本次在aiopsctl工作区原创实现的「衡川观测」Polaris功能范围。没有访问企业数据源，没有使用真实业务数据或凭据，没有公开部署、推送远程或上传Library。

## 最终运行方式

本环境实际验证 .NET SDK10.0.402/runtime10.0.13、Node24.19.0/npm11.9.0、Chromium151。`scripts/publish-local.sh`已成功运行：构建Vue/TypeScript并复制静态产物，发布Release到`.local/publish`，通过`dotnet Hengchuan.Api.dll`在回环5080同时服务API与前端。

最终前端产物：`index-D_LaTk7w.js`（189.21kB，gzip66.12kB）与`index-DmRVsMeD.css`（26.74kB）。最终API和浏览器证据都来自这个单Host发布版，不只开发服务器。浏览器使用全新测试profile、保留用户命名空间/ seccomp sandbox，不使用`--no-sandbox`；云端执行器启动限制经自动审核允许正常用户上下文执行，未修改系统权限或已有浏览器会话。

## 结果与证据

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 后端单元/边界测试 | 157/157，0跳过、0失败 | [TRX](../tests/results/backend-tests.trx) |
| 其中持久层 | 60/60 | 同TRX：原子事务、版本竞争、取消、锁、重启持久化与引用校验 |
| 其中指标/可用性统计 | 51/51 | 同TRX：区间并集、持续事件、截断、MTTR/MTBF、窗口、聚合/对比、空态与数值溢出 |
| 其中ES HTTP替身 | 46/46 | 同TRX：允许范围、mapping、空桶、大小、超时、取消、畸形/部分失败；没有真实连接 |
| 前端typecheck/build | 通过 | 前端构建也由已实际运行的publish-local脚本执行 |
| 发布版API | 12/12组 | [JSON](../tests/evidence/api-results.json)，07:50:08 UTC |
| 发布版Chromium | 11/11组 | [JSON](../tests/evidence/browser-results.json)，07:51:25 UTC |
| 六个深层路由与JS/CSS | 8项HTTP200 | [发布检查](../tests/evidence/published-static-results.json) |
| 独立安全复核 | 独立HTTP21/21及独立SQLite/缺桶检查通过，无待修P0/P1（限定本地demo） | [独立复核报告](INDEPENDENT-REVIEW.md) |
| 首页自动刷新/布局专项 | 7/7页面集成，另10项composable专项 | [页面集成记录](../tests/evidence/frontend-overview-integration.json)，独立浏览器时钟控制；不冒称所有项均为真实墙钟等待 |

前端实现阶段另用真实Chromium验证了15项SVG图表、5项受控ES来源/警告/断线、4项只读桌面/移动巡查；这些是实现团队的补充临时专项，不能与正式核心11组简单合并成同一套测试总数。[矩阵](../tests/MATRIX.md)标明各项归属和覆盖范围。

## 核心交互

首页搜索无数据、业务创建/取消/编辑/保存后刷新、显式删除；双列卡片/紧凑布局与0/30/60秒自动刷新选择，隐藏页暂停和卸载清理；13图钻取、对比/聚合/图形切换、真实空指标、统计/悬停/图例/全屏及键盘；指标AND筛选、分页/列控制、批量成功及409恢复；空间切换时忽略旧响应；跨空间矩阵展开；稳定性年度/月/ISO周/日、级别、未来空态和事件详情；大屏编辑/预览、真实下载、导入校验/重复冲突/回读及删除，均有验证。

发布版检查还包含业务和大屏深层路由直接刷新、CSS/JS资源与CSP。浏览器结果`pageErrors=[]`、`blockedExternalOrigins=[]`。测试自建业务/指标/大屏已清理，不把测试输入混作种子；使用专用acceptance.db，源码不含运行数据库。

## 截图

下面六张来自本项目最终发布版、合成数据，不是参考站截图：

1. [首页](../tests/evidence/screenshots/01-overview.png)
2. [业务双列13图](../tests/evidence/screenshots/02-business-dashboard.png)
3. [指标管理](../tests/evidence/screenshots/03-indicators.png)
4. [七日状态矩阵](../tests/evidence/screenshots/04-statuspage.png)
5. [稳定性与事件](../tests/evidence/screenshots/05-stability-events.png)
6. [大屏预览](../tests/evidence/screenshots/06-screen-preview.png)

另有[桌面首页](screenshots/frontend-home-desktop.png)、[双列详情](screenshots/frontend-business-desktop.png)、[390px手机首页](screenshots/frontend-home-mobile.png)。没有收到参考站内页截图，未执行像素对比。

## 已修复与边界

联调发现并修复：严格JSON绑定空400改为结构化错误；ID末尾换行校验；ES空桶警告传递及断线；真实/合成来源混淆；半开窗口及桶起点；非有限统计；过期请求竞态；补齐首页自动刷新/布局与详情双列。另有测试同步等待和HTTP替身mapping fixture适配，均记录在矩阵，没有将失败断言改成无条件通过。

尚未实现或验收：生产认证/SSO/RBAC/租户隔离、不可篡改审计、真实ES/Oracle集成、生产容量与高可用、备份恢复演练、容器运行/公开部署、参考站私有导入格式、像素级还原、AI/知识库/自动修复/通知。空间仅逻辑分组，不能宣传为身份访问控制。ES默认关闭，Oracle明确未接入；首页30天和稳定性自然窗口统计不同，界面及README已分别说明。

REA的工具安装/CLI成功与页面分析失败属于前置调查事实：本产品依据父线程合法浏览器只读观察提供的规格，不声称通过REA取得了页面证据。旧LogAtlas缺失，本次没有恢复或重建它。此阶段只完成aiopsctl工作区的Polaris范围，原Git历史和外部重置备份保留，提交仅在本地。
