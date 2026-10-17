# 衡川观测

一个可运行的原创业务可观测工作台：.NET 10 + Vue 3 / TypeScript + SQLite。产品名、组件、图标、SVG图表和业务代码为本次原创实现；默认使用隔离合成数据，没有接入真实 Elasticsearch 或 Oracle。

规格来自父线程在合法登录浏览器中的**只读文字观察**。REA fork 虽已另行安装并验证 CLI/CDP，但其环境访问演示站被代理403拒绝，没有得到成功的页面分析证据。本项目没有复制 Flashcat 受保护源码、商标或素材；没有参考站截图，视觉设计依据文字规格独立完成，不声称像素级还原。所有编辑、导入导出及持久化行为是独立设计，没有在参考站执行写操作。

## 启动

需要 .NET SDK **10.0.402**（仓库global.json）及 Node.js 22.12+/24、npm。开发与测试实际使用 Node24.19.0/npm11.9.0。已有NuGet/npm锁文件；不需启动REA、ES、Oracle或其他平台。

```sh
dotnet restore backend/Hengchuan.Tests/Hengchuan.Tests.csproj --locked-mode
cd frontend
npm ci
cd ..
bash scripts/dev.sh
```

在运行这两个进程的同一台机器访问 `http://127.0.0.1:5173/polaris`，后端为 `http://127.0.0.1:5080`。**云端容器的localhost不是用户电脑的localhost。** 本次没有公开部署、端口转发、推送或修改用户电脑。

本云端SDK仅安装在 `/workspace/.tools/dotnet`。执行器的首次NuGet命名mutex存在环境限制，本轮已在全新的工具缓存目录完成初始化；本环境命令可使用：

```sh
export DOTNET_BIN=/workspace/.tools/dotnet/dotnet
export DOTNET_CLI_HOME=/workspace/.tools/dotnet-home
export NUGET_PACKAGES=/workspace/.tools/nuget
export XDG_DATA_HOME=/workspace/.tools/xdg-data
export TMPDIR=/workspace/.tools/tmp
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false
bash scripts/dev.sh
```

这些设置只涉及工作区内工具缓存；未修改系统权限或安装系统服务。其他正常安装SDK的环境通常不需要云端专用路径。首次依赖还原需要网络；离线包缓存未提供。

## 单进程发布

```sh
bash scripts/publish-local.sh
cd .local/publish
dotnet Hengchuan.Api.dll
# 同一台机器打开 http://127.0.0.1:5080/polaris
```

脚本构建前端并复制到ASP.NET静态目录；深层路由刷新可用。SQLite 默认保存在运行内容目录的 `data/hengchuan.db`。可通过 `Storage__Path=/absolute/path/to/hengchuan.db` 指定持久化文件。第一次启动种子只创建一次，重启不覆盖编辑。发布目录可重建，**不要把唯一业务数据库放在将被替换/清理的构建输出中**。验证记录见 [验收记录](docs/ACCEPTANCE.md)。未提供或声称验证Docker、公开托管、生产部署。

## 已实现

- `/polaris`：空间选择、新空间及业务编辑，双列业务卡、名称/负责人/服务搜索、可用性/目标/负责人/图表数/预算条、趋势与业务钻取；手动刷新、自动刷新和布局控制。
- `/polaris/n/:business`：`biz-checkout`参考规模13图，其中`ind-empty`为真实空态；最近12小时等时间窗口、昨天/上周比较、求和/平均/原值，折线/柱状/漏斗、统计、图例、键盘与全屏；采样缺口断线，来源与ES警告可见。
- `/polaris/indicator`：数据源/启用/业务/源名称/指标名称AND筛选、未引用、列显隐、批量启停、分页、创建编辑删除。被业务或大屏引用时不允许直接删除。
- `/polaris/statuspage`：跨空间最近7日健康矩阵，空间可展开，最近状态与逐日状态分开。
- `/polaris/stability`：空间/年度/等级筛选、自然年/月/ISO周/日统计、业务目标/可用性/预算/事件/MTTR/MTBF及事件详情；未来窗口空态。API支持人工事件创建/修改，UI当前提供详情查看。
- `/polaris/screen`：搜索分页选择、大屏新建编辑删除、严格UTF8/JSON格式校验导入导出、独立面板聚合/图形设置与预览。导入仅接受本项目格式`hengchuan-screen` version1，不导入参考站私有格式。
- 所有配置与事件存SQLite；revision乐观并发、原子批量及引用验证、失败回滚、输入边界与JSON错误、取消支持。演示操作者固定显示`demo-operator`，不是账号或认证。

## 统计口径

全部UTC，窗口采用半开区间`[from,to)`。首页最近30天滚动窗口，卡片微趋势是最近12小时合成时序；稳定性采用选定自然年/月/ISO周/日，与所选年度相交并截至当前。两者窗口不同，不应强行统一数值。

事件停机区间在窗口内截断并取并集，重叠不重复计；进行中事件截止窗口末。可用性=`(1−停机分钟/已观测分钟)×100`；预算=`已观测分钟×(1−目标/100)`；剩余额度下限0，消耗比例允许超过100%，只将进度条宽度限制在100%。事件数按相交原始事件计；MTTR为相交已恢复事件截断时长平均；MTBF为窗口内非停机分钟/事件数；无相应事件时返回null。等级筛选影响所有事件派生值；汇总可用性为业务算术平均，不是请求加权SLA。

合成时序每5分钟采样；avg/sum按30分钟桶（超过24小时为6小时桶）且桶从所选from开始；raw保留原始采样。累计/最大/最小/平均基于实际返回点，不是事件停机统计。比较对齐前1天/7天，只用匹配时间点计算变化率；缺测不补零、不假设连续、不计算无匹配变化率。合成时序不是某实际业务的真实SLA证据。

## 数据源与安全边界

`backend/Hengchuan.Api/appsettings.json` 默认ES关闭。无秘密示例见 [配置说明](docs/CONFIGURATION.md)。真实ES只读适配仅接受服务端concrete索引、服务、环境及数值字段白名单，校验mapping，限制窗口7天、1000桶、1MiB响应，有超时/取消且拒绝重定向/TLS绕过；只支持avg/sum，raw明确报不支持，失败不回落合成成功。此适配器仅经HTTP替身测试，**未经用户真实ES版本/映射实测**。

Oracle仅显示未接入的来源扩展状态，没有访问数据库、日志、AWR/ASH。没有生产账号、企业配置、日志、凭据或历史数据复制。没有自动修复、通知或外部模型调用。

首版限定本机回环：校验Host/远端地址、写入专用header和浏览器Origin；这不是生产身份认证。空间是逻辑数据分组，引用一致性校验**不是RBAC/租户访问隔离**。任意本机进程仍可调用API；没有SSO、角色授权、加密审计或多租户边界，不可直接公开部署。界面将文本作为数据，不执行导入代码，拒绝HTML/控制字符和错误格式。

## 维护与测试

```sh
bash scripts/check.sh
node tests/api-acceptance.mjs
# 仅浏览器验收需要此测试依赖，应用运行不需要
npm ci --prefix tests
# Chromium需安装，默认读取CHROMIUM_PATH或/usr/bin/chromium
node tests/browser-acceptance.mjs
```

API/浏览器测试需先启动本地应用；URL及测试环境详见 [tests/README.md](tests/README.md)。本云端浏览器需经执行器自动审核允许正常用户命名空间，测试显式保留Chromium sandbox，不使用`--no-sandbox`，不修改SUID helper/内核权限；新建测试profile，不读取已有会话。证据适用范围见 [测试矩阵](tests/MATRIX.md)、[独立复核](docs/INDEPENDENT-REVIEW.md)。

SQLite快照schema1保存统一领域模型，revision在事务中递增；配置规模上限由校验控制。当前适合单实例本地首版；未来迁移PostgreSQL可将空间/业务/指标/事件/大屏规范化，保留ID、版本及事务边界，迁移前停写、用SQLite备份API或停机一致快照，不能只复制活跃`.db`而丢WAL。尚未实现生产备份调度、schema2迁移、负载基准、高可用、多副本部署或恢复演练。

第三方依赖与归属见 [THIRD-PARTY.md](docs/THIRD-PARTY.md)。仓库路径沿用用户的aiopsctl；原`.git`历史与外部重置备份保留，新实现不会自动推送远程。旧LogAtlas不在本环境，未将本项目冒称为旧成果恢复。
