# URL 空间选择

空间是合成演示数据的逻辑分组，不是 RBAC 或租户身份授权。状态页仍明确展示全部演示空间；其他页面按当前空间筛选。

`/polaris?space=核心交易` 按 `/api/meta` 中的空间名称精确匹配。名称保留大小写、空白和原始字符，由 Vue Router 编码一次；不对已解码的值再次解码。选择器、主导航、业务钻取、管理关联指标及状态页的跨空间业务链接都写入相应空间。

- 有效且唯一的 `spaceId` 优先于 `space`。正常名称唯一时规范为 `space=名称`；元数据存在重名时同时保留选中 ID，避免刷新后选错空间。
- 未知名称/ID、空值、无值和重复参数明确报错，清空选中空间，并隐藏页面内容。不会默默回退显示其他空间。可通过顶部选择器恢复；没有可用空间时需刷新并检查元数据。
- URL 没有空间参数时，沿用当前有效选择；全新页面优先使用原有 `space-default`，不存在时用元数据首项，并以 replace 写回 URL，不增加历史条目。旧的不带空间业务详情链接先读取业务所属空间再规范 URL。显式空间与业务不符则报错，不展示该业务图表，也不反向覆盖 URL 空间。
- 用户切空间和跨页导航创建正常历史条目；刷新、后退和前进均由 URL 恢复选择。切空间时清除属于旧空间的 `businessId` 筛选，保留其他查询参数与 hash。
- 空间元数据与已确认路由共同门控页面加载。元数据失败后重试也不会提前挂载旧路由；新导航会使旧空间解析失效，防止迟到的旧业务响应重新跳转。切空间重建页面并取消旧读取，避免旧结果覆盖新空间。

这是一项独立实现。公开脚本静态检查仅支持名称精确匹配、参数优先级等候选行为；本实现严格验证 ID 和重复参数，没有照搬参考脚本的存储回退。参考站已登录运行时、实际空间 ID、服务端授权和统计口径尚未验证，当前环境仍缺少用户安全交互登录入口。

验证方式：

```sh
npm --prefix frontend test
npm --prefix frontend run typecheck
npm --prefix frontend run build
# 先启动本地合成 API 与前端，保留 Chromium sandbox
npm --prefix tests run browser:space
npm --prefix tests run browser
```

专项覆盖六页命名深链接和刷新、选择器/导航/历史、特殊字符、未知/歧义参数、元数据失败/延迟、详情空间一致性、旧解析与旧响应竞态。编码和重名空间仅通过本地 GET 响应替身提供，不创建持久空间，不访问参考站或读取其他浏览器会话。机器结果分别写 `tests/evidence/url-space-results.json` 与 `tests/evidence/browser-results.json`；设置 `HENGCHUAN_EVIDENCE_DIR` 可保存到独立目录。旧验收与本轮结果分开记录。

2026-10-19 实跑：解析单元 **17/17**、[URL 专项 **20/20**](../tests/evidence/url-space-results.json)、[原有六页交互 **11/11**](../tests/evidence/url-space-regression.json)、后端 **157/157**；前端 typecheck/build 通过。[验证摘要](../tests/evidence/url-space-validation.json)记录来源哈希与边界。浏览器使用全新 profile、保留 sandbox、仅回环应用；本轮页面回归针对 Vite5173/API5080，没有重跑原 12 组 API 或单 Host Release 浏览器验收。
