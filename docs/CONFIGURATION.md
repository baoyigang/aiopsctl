# 配置说明（无秘密示例）

所有连接配置仅在服务端文件或环境变量中设置，前端不接收ES地址、凭据、任意DSL或索引扩展。默认不建立外部连接；首版不需要任何账号凭据。

```json
{
  "Urls": "http://127.0.0.1:5080",
  "Storage": { "Path": "data/hengchuan.db" },
  "Elasticsearch": {
    "Enabled": false,
    "Endpoint": "https://es.example.invalid:9200",
    "AllowedIndices": ["synthetic-observation-2026-10"],
    "AllowedServices": ["checkout"],
    "AllowedEnvironments": ["demo"],
    "MetricFields": { "latency": "latency_ms" },
    "TimestampField": "@timestamp",
    "ServiceField": "service.name.keyword",
    "EnvironmentField": "environment.keyword",
    "TimeoutSeconds": 10
  }
}
```

上面域名/索引/服务/字段均为虚构占位，Enabled保持false；不要将此示例说成真实可用连接。没有填入或请求企业凭据。若以后有明确授权，连接秘密只能通过部署环境的受控秘密机制注入，不能写进仓库、聊天或前端。ApiKey属性为可选服务端项，此处故意不提供值。

指标`sourceId=demo`始终生成标识明确的合成数据；`sourceId=es-main`使用上述服务端配置、所属业务的service/environment及指标metricName白名单键。ES失败显示独立图表错误，不回落演示成功。Meta里的configured仅表示启用配置，不等于接通。

适配器强制允许列表、最多7天、固定date_histogram与avg/sum、数值mapping以及service/environment精确keyword映射，timestamp须为date/date_nanos。每次field_caps重新核验，还核验返回indices仅含请求的具体索引。管理员应配置实际具体索引；未提供独立_resolve/index身份解析，别名/数据流未经真实集群验收，不作为支持能力。禁止wildcard、客户端查询脚本、运行时字段、TLS忽略和重定向；HTTP仅允许literal loopback测试端点，真实连接用HTTPS。

响应最多1MiB及1000桶；部分失败、超时或错误mapping明确失败。空桶保留缺口警告、原值不补零；图表按intervalSeconds断线。真实ES不支持raw原值模式。该适配器仅以HTTP替身验证，尚无真实ES版本、权限、字段映射或数据质量验收；启用前由管理员在获准环境核验。

Oracle仅保留来源扩展状态；不存在默认连接、驱动、SQL、alert/trace/listener采集或AWR/ASH功能。

SQLite首次启动只创建一次合成种子，后续启动读取原配置。建议将Storage路径设到构建目录之外，并在停写后做一致备份。源目录`.git`重建前完整备份位于本次云端外部目录`/workspace/project-reset-backups/20261017T054109Z-aiopsctl`；本项目不会清理或覆盖该目录。不要把该本地备份当作已经建立的跨环境持久备份。
