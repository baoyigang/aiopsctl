# 本地核心交互验收

仅对已启动的本地合成演示应用执行；目标必须是 `127.0.0.1` 或 `localhost`。不访问 Flashcat、真实 ES/Oracle，不连接既有浏览器或导入会话。全部变更使用独立命名的合成数据，结束时清理本脚本创建的业务、指标和大屏。

## 运行

先按项目 README 启动 API `127.0.0.1:5080` 和前端 `127.0.0.1:5173`，然后：

```bash
node tests/api-acceptance.mjs
node tests/browser-acceptance.mjs
node tests/url-space-acceptance.mjs
```

本云端已有 Playwright Core，可直接从 `/workspace/rea-tool/node_modules/playwright-core` 使用，不运行 REA 或其 setup/MCP。其他开发环境可先 `npm --prefix tests ci`；版本锁定为 `1.58.2`。`PLAYWRIGHT_CORE_DIR` 可指定已安装模块目录，`CHROMIUM_PATH` 默认 `/usr/bin/chromium`。

浏览器脚本显式启用 Chromium sandbox，只禁用 SUID helper，使用新的临时上下文、独立 XDG 目录和最小环境变量；不连接 CDP 9222 或任何其他会话。本云端默认执行器禁止 namespace sandbox，已用平台批准的普通用户上下文实测可启动，正式浏览器验收也必须保持该上下文，不使用 `--no-sandbox`。

可用 `HENGCHUAN_API`、`HENGCHUAN_WEB` 覆盖回环端点。合并发布模式可将两者均指向 `http://127.0.0.1:5080`。API 与浏览器测试按顺序运行；两者均验证 revision，避免相互并发写入干扰。

URL 空间专项只读本地合成 API，特殊字符/重名空间通过 GET 响应替身提供；结果为 `tests/evidence/url-space-results.json`。两套浏览器脚本均支持 `HENGCHUAN_EVIDENCE_DIR=/absolute/evidence/path`，用于保留新一轮证据而不覆盖旧记录。URL 参数、未知空间处理和回归范围见 [URL 空间选择](../docs/URL-SPACE.md)。纯函数测试使用 `npm --prefix frontend test`，不需运行服务或浏览器。

## 证据与覆盖

矩阵见 [MATRIX.md](MATRIX.md)。机器可读结果保存在 `tests/evidence/api-results.json` 和 `tests/evidence/browser-results.json`；截图保存在 `tests/evidence/screenshots/`。非零退出码表示检查失败。证据只含合成数据，不存环境凭据、cookies 或浏览器 profile。

API 通过真实 HTTP 请求检查 CRUD、版本冲突、跨空间引用、批量原子性、筛选分页、图表采样统计、事件窗口、状态矩阵及大屏导入导出。浏览器操作真实页面并临时延迟本地响应验证竞态，使用独立合成指标验证批量冲突和恢复，拦截所有非本应用来源的请求。

脚本准备与语法检查不等于应用验收通过；实际结果以执行后的证据为准。
