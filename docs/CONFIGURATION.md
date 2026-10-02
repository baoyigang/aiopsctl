# 运行与集成配置

## 本地安全边界

.NET 强制 `127.0.0.1` 监听，不采纳 ASPNETCORE_URLS 的公网监听设置。仅接受 localhost/127.0.0.1 Host；同源 Origin 校验；写 API 要求 `X-Aiops-Client: workbench`，webhook 独立 Bearer 校验。不支持 CORS。无认证生产网关前禁止暴露公网/公司网络；当前 webhook 可由同机 Alertmanager 或受控隧道访问。

这不是生产认证：同一操作系统用户能读配置与数据，CSRF header 也不是认证凭据。审计不防篡改，日志样本未加密；部署前需 TLS/SSO/RBAC/最小授权、加密、保留策略与审计归档。

## 设置步骤

1. 运维人员在服务器的安全环境配置批准的目标 origin（HTTP/HTTPS、IPv4、端口），例如 `AIOPS_ALLOWED_ORIGINS='http://10.20.0.10:9090;http://10.20.0.20:8000'`。默认不允许任何出站端点，包含私网。
2. 通过服务器密钥管理/环境注入 `AIOPS_MODEL_KEY`（如果服务需要认证）与 `AIOPS_WEBHOOK_TOKEN`。不要把真实值写到仓库、命令历史、前端或日志。
3. 重启服务。页面中的“模型和数据源”填写 URL 和模型名。Prometheus URL 如 `http://10.20.0.10:9090`；模型 URL 如 `http://10.20.0.20:8000/v1`，服务拼接 `/chat/completions`。
4. 公网模型必须标记为外网；显式选中 `ruleCategory` 才会发起模型请求。当前只支持一个活动模型配置/数据空间，不支持负载均衡或多模型自动路由。
5. Prometheus 点“只读查询 up”，模型在日志诊断中勾选模型后分析。成功/未配置/失败会明确显示。

所有出站连接：禁用代理、禁用重定向、8 秒总超时、响应最多 1 MiB、正常证书校验；限制 IPv4 精确 origin，拒绝 DNS 主机名、IPv6、链路本地、已知云元数据地址。若机构 DNS 模型网关必须接入，后续应实现带固定解析/连接 IP 验证的 DNS 适配器，不能删除当前校验。HTTP 仅适用于受信任内网测试；生产需 TLS 和可信网络策略。

## Alertmanager 真实 webhook

使用 Alertmanager 官方的 webhook receiver，URL 为同机 `http://127.0.0.1:5080/api/webhooks/alertmanager`。设置 `http_config.authorization.type: Bearer` 与由服务器安全文件提供的 `credentials_file`，对应 AIOPS_WEBHOOK_TOKEN。以下示例不包含真实密钥：

```yaml
receivers:
  - name: aiops-local
    webhook_configs:
      - url: http://127.0.0.1:5080/api/webhooks/alertmanager
        send_resolved: true
        http_config:
          authorization:
            type: Bearer
            credentials_file: /run/secrets/aiops-webhook-token
```

只有 `alerts` 内 `status`、`labels` 用于入库。最多 100 条/请求，总 body 256 KiB。相同标签集合聚合，接收次数并非唯一发生次数；MVP 未实现乱序 startsAt/endsAt 去重与通知路由。生产接入前须补充重放策略和告警保留策略。

## Kubernetes

在已有授权的集群终端执行 `kubectl get pods -A -o json`，把输出粘贴到导入框。此服务不索要 kubeconfig，不控制集群，仅识别 `Pod`、metadata.uid/name、spec.nodeName。单次 ≤500 Pods，要求已调度；暂不支持多集群节点命名隔离、删除同步和定时拉取。人工关系即使改变了终点也不会被后续导入静默覆盖。导入来源为 kubernetes；手工编辑后来源 manual 始终优先。

## 日志与模型数据治理

样本日志保存前做常见凭据键/Bearer 脱敏；最多 10,000 字符。模型输入视为不可信数据、固定 system 指令不允许提升日志内指令权限。模型没有 tool 调用、数据库或 Shell 能力；输出作为普通文本渲染，不执行 HTML/Markdown 脚本。不能保证任意模型一定忽略全部提示注入，因此通过“没有执行能力 + 外网不传原文”限制影响面。

外网输入只发送固定 `ruleCategory=timeout` 或 `ruleCategory=unknown`。显式字段授权不是允许任意输入外传；白名单拒绝 rawLogs。内网可发送脱敏后的输入，仍需要组织内的模型服务权限与数据保留管理。

## 查询限制

- Prometheus 只支持 `up`、`process_cpu_seconds_total` 的 instant query，固定 `timeout=5s&limit=100`；不执行自由 PromQL，不写入数据源，不冒充历史趋势。
- 图表数据集：assets(kind/source)、incidents(severity/status)、logs(level/system)，count 聚合；1–168 小时、1–50 组、bar/pie。没有任意 SQL/JS 字段。
- 日志查询 1–168 小时、≤200 返回项、关键词 ≤200 字符，字面量包含匹配。
- 每类最多读取最近 10,000 条 SQLite 工作集记录；无分页和总量治理，不能用于大规模生产检索。
