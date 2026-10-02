# 验收结果（2026-10-02，云端实际执行）

环境：`/workspace/aiopsctl`，基线提交 `a15c929fbf750da1230caeeac6a95e03dce8b356`，工作分支 `mvp`。SDK 10.0.401、Node v24.19.0、npm 11.9.0；系统 Chromium 151.0.7922.173。测试使用合成 fixture，不含企业凭据。

## 通过

| 检查 | 结果与证据 |
|---|---|
| .NET API build | 0 errors / 0 warnings |
| .NET tests | **14 passed, 0 failed, 0 skipped**；`backend/AiOps.Tests/SecurityTests.cs` |
| 前端 typecheck / build | 通过，生成由 .NET 提供的真实静态资源；构建仍有单个 JS chunk 超 500 kB 的体积提示，不影响功能 |
| 前端 Vitest | **2 passed, 0 failed**；API 空间/header、服务端错误传播 |
| Live HTTP 集成 | **19 checks passed, 0 failed**；`tests/integration.py`，实际启动 .NET/SQLite/本地上游 HTTP |
| Chromium UI | **1 场景 passed**，包括多项真实用户操作；`frontend/tests/ui/workbench.spec.ts` |
| npm audit | found **0 vulnerabilities** |
| NuGet 直接与传递依赖 | 当前官方源未报告易受攻击包 |
| 锁定还原 | `dotnet restore AiOps.slnx --locked-mode` 通过；NuGet packages.lock.json 与 npm package-lock.json 入库 |

### 19 项 HTTP 检查涵盖

1. Demo/real 持久化空间隔离。
2. 资产 CRUD、引用完整性。
3. 关系删除、资产删除。
4. 实际格式 Kubernetes Pod List 导入，人工资产名/关系终点覆盖不被自动发现替换。
5. 畸形 JSON 导入拒绝。
6. Alertmanager Bearer 拒绝/接受、重复聚合、恢复状态和 Worker 自动诊断。
7. 日志提示注入作为数据、敏感凭据脱敏、未配置模型明确规则回退。
8. 日志存储/查询和输入/时间/数量上限。
9. 图表保存与执行、SQL 式字段注入和超限拒绝。
10. SSRF 默认拒绝、私网精确许可、元数据端点/IPv6/URL 凭据、公共模型不可标记内网。
11. Prometheus 真实 HTTP 只读白名单查询、固定 timeout/limit，Demo 拒绝真实查询。
12. 上游 302 重定向和 >1 MiB 响应拒绝。
13. 模型不可达明确回退。
14. 外网未授权时不发出请求（检查上游实际收到的次数）。
15. 外网仅发送 ruleCategory，检查请求体无原始日志/注入语句/敏感值。
16. 内网模型收到脱敏请求。
17. 密钥不出现在设置读响应、审计，rawLogs 外发字段拒绝。
18. Origin/Host/缺少写请求 header 拒绝。
19. **停止进程、重新启动、重新读取**，资产/关系/日志/图表/设置一致，Demo 隔离保留。

### UI 交互证据

使用真实编译前端和 .NET 后端，独立临时 DB：新增两资产 → 建关系 → 粘贴包含 `<img onerror>` 和提示注入/秘密的日志 → 输出以文本显示且脱敏、DOM 无 img → 保存日志 → 生成并保存图表 → Demo seed/真实切换隔离 → 页面刷新后资产仍在。不是截图或 build 替代功能测试。

## 当前失败

最终回归无失败测试。

开发中遇到并已处理：默认 .NET home 只读（缓存改到允许目录）；前端 TypeScript 配置/模板错误；Playwright 模块路径；首次依赖扫描告警（更新精确锁定包后两端扫描通过）。这些不计为最终未修复失败。

## 未运行 / 剩余限制

- 企业真实 Prometheus、Alertmanager、Kubernetes 集群和真实模型服务端到端测试：无企业端点/凭据，本次使用本地 HTTP fixture 和真实 Kubernetes JSON 协议格式验证路径，不能声称已连通企业系统。
- 生产认证/RBAC/多租户、分布式并发、负载/压测、备份灾备演练：功能未实现或未验证。
- 全量日志平台、trace、CPU profiling、全链路根因排序：尚未实现。
- Playwright 自带 Chromium 下载被云端网络规则以 403 Domain forbidden 拒绝；未绕过限制，改用已经安装的系统 Chromium 并通过交互测试。因此不是当前 UI 验证阻塞。

## 复现

见 README。集成测试自己创建并清理临时数据库。UI 测试会向运行实例写入 `UI应用<timestamp>` 等测试对象，只应使用测试数据库。运行正式本地工作台时用独立 `AIOPS_DB`，避免把 UI 测试写入的数据当作真实资产。
