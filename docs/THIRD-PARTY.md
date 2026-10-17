# 第三方依赖与许可记录

核对日期：2026-10-17。依据实际 lock 文件、当前已安装的 npm `package.json`/许可文件及 NuGet `.nuspec`，不是仅按框架名称推测。完整逐包版本、完整性哈希、安装情况、声明来源及许可文件 SHA-256 见 [dependency-licenses.json](dependency-licenses.json)；已安装包提供的许可与 NOTICE 原文见 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)。相同原文按 SHA-256 去重，并保留各包归属。

本文件**不是本项目的 LICENSE**，不为衡川观测的原创代码自动授予任何开源许可。第三方包继续适用各自许可；以下是已取得声明与证据的清单，不替代各许可原文。

## 原创代码与未复用项目

业务后端、中文页面、SVG 图表、测试与合成数据为本次原创实现。未复制 Flashcat 的源代码、商标或素材；页面规格仅来自父线程合法只读观察后提供的文字。REA 页面分析没有成功，不把它描述为已取得页面分析结果。

REA 安装在项目外，属于开发工具，不是应用运行依赖，也没有复制其实现到仓库。验收脚本使用的 `playwright-core` 单独列入测试依赖，不能因此把整个 REA 工具列为产品组件。LogMind、HolmesGPT、Aurora 未实际加入本项目或复制代码。Elasticsearch 适配器使用 .NET `HttpClient` 原生实现；Oracle 尚未接入，无 Oracle 驱动或采集组件。

## npm 依赖

`frontend/package-lock.json` 锁定 72 个包；当前 Linux x64 安装 49 个，其余 23 个主要是其他平台的可选原生绑定，清单保留其 lock 声明并标记为本平台未安装，没有把未读取的文件称为已核验。前端直接依赖如下，所有版本均为 lock 中的实际版本。

| 包 | 版本 | 用途 | 许可声明 |
| --- | --- | --- | --- |
| vue | 3.5.44 | 页面运行 | MIT |
| vue-router | 4.6.4 | 页面路由 | MIT |
| @vitejs/plugin-vue | 6.0.10 | 构建 | MIT |
| vite | 8.3.4 | 开发与构建 | MIT |
| typescript | 5.9.3 | 类型检查与构建 | Apache-2.0 |
| vue-tsc | 3.3.12 | Vue 类型检查 | MIT |

72 个锁定包的声明分布：MIT 55、MPL-2.0 12、Apache-2.0 2、BSD-2-Clause 1、BSD-3-Clause 1、ISC 1。这里统计的是包顶层声明，不能用来替代包内包含的其他第三方 NOTICE。

需要单独注意：`lightningcss 1.33.0` 及其 11 个平台绑定声明 **MPL-2.0**，当前安装的是主包、`lightningcss-linux-x64-gnu 1.33.0` 与 `lightningcss-linux-x64-musl 1.33.0`；安装不等于该绑定已被当前进程加载。已保留其 LICENSE，未修改其上游源码。`detect-libc 2.1.2` 为 Apache-2.0，`entities 7.0.1` 为 BSD-2-Clause，`source-map-js 1.2.2` 为 BSD-3-Clause，`picocolors 1.1.1` 为 ISC。Vite、Rolldown、TypeScript 附带的其他第三方声明也已保留在合并 NOTICE 中。

`tests/package-lock.json` 锁定 `playwright-core 1.58.2`，许可 Apache-2.0，只用于浏览器验收。此次实际执行使用项目外 `/workspace/rea-tool/node_modules/playwright-core` 中**相同版本**的包；核对了该包的 `package.json`、LICENSE、NOTICE 与 ThirdPartyNotices.txt。仓库声明允许在其他环境通过 `npm ci --prefix tests` 安装同版本，不要求复制 REA。系统 Chromium 是测试工具，不是本项目打包的产品依赖；本记录未完成操作系统浏览器及其完整依赖的许可审计。

## NuGet 依赖

依据 `backend/Hengchuan.Api/packages.lock.json`、`backend/Hengchuan.Tests/packages.lock.json` 合并后为 19 个唯一 NuGet 包；Project 类型的原创 `Hengchuan.Api` 引用不计入第三方。API 运行依赖闭包为 6 个包，其余为测试依赖。实际安装目录为本次专用 `/workspace/.tools/nuget`，未读取旧项目配置。

| 包或同版本包组 | 锁定版本 | `.nuspec` 声明 |
| --- | --- | --- |
| Microsoft.Data.Sqlite、Microsoft.Data.Sqlite.Core | 10.0.13 | MIT |
| SQLitePCLRaw.bundle_e_sqlite3、SQLitePCLRaw.core、SQLitePCLRaw.lib.e_sqlite3、SQLitePCLRaw.provider.e_sqlite3 | 2.1.13 | Apache-2.0 |
| Microsoft.NET.Test.Sdk、Microsoft.CodeCoverage、Microsoft.TestPlatform.ObjectModel、Microsoft.TestPlatform.TestHost | 18.0.1 | MIT |
| Newtonsoft.Json | 13.0.3 | MIT |
| xunit、xunit.assert、xunit.core、xunit.extensibility.core、xunit.extensibility.execution | 2.9.3 | Apache-2.0 |
| xunit.analyzers | 1.18.0 | Apache-2.0 |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 |
| xunit.abstractions | 2.0.3 | **确切版本许可未核实**；仅有旧式 licenseUrl |

18 个包具有本地 `.nuspec` 的 SPDX expression；此次没有 `license type="file"` 的包。生成器同时支持 file 声明，并只读取包目录内部文件。部分 NuGet 包没有随包提供 LICENSE 文件，记录使用其 `.nuspec` 声明，并保留来源、作者及 copyright；不把缺失的文件编造成已经读取。

`xunit.abstractions 2.0.3` 的本地 `.nuspec` 没有 SPDX/license 节点或 LICENSE 文件，仅指向 [xunit 官方仓库 master/license.txt](https://raw.githubusercontent.com/xunit/xunit/master/license.txt)。该可变分支当前包含 Apache-2.0 及所列导入代码的 MIT 说明，但本次未取得可绑定到 **2.0.3** 的 tag/commit 许可证据，因此仍保留 `unverified-legacy-license-url`，不按同系列其他包推断。官方仓库 tag 查询未成功；后续补齐确切版本证据再更新此项。

### SQLite 原生引擎与封装包分开记录

`SQLitePCLRaw.* 2.1.13` 的包声明为 Apache-2.0，这不等于原生 SQLite 引擎被重新归类为 Apache-2.0。[SQLite 官方版权说明](https://www.sqlite.org/copyright.html)将原始 SQLite 可交付代码与文档声明为公有领域；本清单以 `LicenseRef-SQLite-Public-Domain` 单独记录该上游声明。

本环境对已安装 `SQLitePCLRaw.lib.e_sqlite3/2.1.13/runtimes/linux-x64/native/libe_sqlite3.so` **只调用了 `sqlite3_libversion()`**，读到原生版本 **3.53.3**，并记录二进制 SHA-256。没有打开数据库或读取业务数据；其他平台的原生二进制未执行验证。wrapper/package 版本 2.1.13 与 native engine 版本 3.53.3 是两个不同的版本号。

## 实际漏洞检查

2026-10-17 使用本次安装的 .NET SDK **10.0.402**，针对两个项目分别执行：

```bash
dotnet list backend/Hengchuan.Api/Hengchuan.Api.csproj package --vulnerable --include-transitive --no-restore --format json
dotnet list backend/Hengchuan.Tests/Hengchuan.Tests.csproj package --vulnerable --include-transitive --no-restore --format json
```

两次均退出码 **0**，查询源为 `https://api.nuget.org/v3/index.json`，结果均未报告存在漏洞的 NuGet 包。原始 JSON 及命令、时间、退出码已保存在 `dependency-licenses.json` 的 `vulnerabilityAudit` 中。没有自动修改任何版本。

该结果仅表示此次 NuGet 公告查询没有报告匹配项；**没有覆盖 npm、原生 SQLite、系统 Chromium、操作系统镜像、.NET shared runtime 或业务代码安全**，也不替代后续发布前复查。许可未核实项与漏洞检查是不同事项。

## 复现与更新

安装 lock 中的依赖后，运行仅本地读取的生成器：

```bash
python3 scripts/dependency-licenses.py \
  --nuget-root /workspace/.tools/nuget \
  --playwright-root /workspace/rea-tool/node_modules/playwright-core
```

它重新生成 JSON 与合并 NOTICE，不访问网络、不改依赖、不打开应用数据，也不授予项目许可。若不提供 `--audit-record`，会保留原 JSON 中已有漏洞检查的时间和结果，**不会声称重新执行过漏洞检查**。`--audit-record path.json` 可导入单独运行的检查记录；更新依赖后应重新执行上面的两个漏洞查询，再更新记录及本说明。

本云端测试环境的命令另设置了 `DOTNET_CLI_HOME=/workspace/.tools/dotnet-home`、`NUGET_PACKAGES=/workspace/.tools/nuget`、`XDG_DATA_HOME=/workspace/.tools/xdg-data`、`TMPDIR=/workspace/.tools/tmp`，以使用隔离且可写的工具目录。生成器记录的绝对路径只是本次证据位置，不是应用运行时对这些云端路径的依赖。
