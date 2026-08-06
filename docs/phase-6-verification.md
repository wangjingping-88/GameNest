# Phase 6：发布验收记录

日期：2026-08-06

## 阶段范围

本阶段实现 `docs/product-plan.md` 的 Phase 6：x64 自包含便携版、启动自动备份、孤立图片缓存清理、脱敏诊断导出、卸载数据保留选择、用户文档及 Release 内容审计。

未新增在线服务、扫描适配器、覆盖层指标、进程控制策略或安装器；Phase 4 的真实游戏 FPS 补测仍按用户决定延期。

## 固定版本

- .NET SDK：10.0.302
- C#：14
- 目标框架：`net10.0-windows10.0.19041.0`
- Windows App SDK / WinUI 3：2.3.1 Stable
- CommunityToolkit.Mvvm：8.4.2
- Microsoft.Data.Sqlite / Microsoft.Extensions.*：10.0.10
- Microsoft.NET.Test.Sdk：18.8.1
- xUnit：3.2.2；runner：3.1.5；coverlet：10.0.1
- PresentMon：2.5.1 standalone x64，固定 SHA-256

本阶段未新增 NuGet 包。

## 实现摘要

- `IApplicationMaintenanceService` 把维护能力留在 Application 边界，SQLite、文件系统、压缩和脱敏实现位于 Infrastructure。
- 启动后在后台检查自动备份，24 小时最多一次，保留最近 7 份；手工备份不参与自动轮换。
- 缓存清理只枚举 `assets/cache` 和旧版 `assets/icons` 的直接子文件，并保留数据库仍引用的路径。
- 诊断 ZIP 仅包含版本、系统概况、数量/容量统计和最近 3 份脱敏日志；明确排除数据库、游戏标题、游戏路径和凭据。
- 设置页新增“数据与维护”，文件夹选择仍是薄 code-behind，核心操作由 ViewModel 和应用端口协调。
- 便携版包含 .NET 10、Windows App SDK、独立 Overlay、PresentMon、用户/隐私/兼容性/许可证文档及交互式卸载脚本。
- 应用 EXE、窗口/任务栏和自定义标题栏统一使用正式品牌图标与横版 Logo；ICO 包含 16 至 256 像素多尺寸帧。
- Release 审计拒绝 PDB、PresentMon 哈希漂移、测试 API Key、私钥和开发机绝对路径。

## 自动化结果

最终命令：

```powershell
& '.\scripts\Test-Release.ps1'
& '.\scripts\Publish-Portable.ps1'
& '.\scripts\Verify-ReleasePackage.ps1' `
  -PackageDirectory '.\artifacts\release\GameNest-0.1.0-win-x64-portable'
```

- Release build：通过，0 警告，0 错误。
- Domain：18/18。
- Application：25/25。
- Infrastructure：33/33，其中新增维护服务测试 3 项。
- Telemetry：17/17。
- 合计：93/93，无跳过。
- 基线中性能计时和 SQLite 全局连接池的并行抖动，已通过按测试项目顺序运行和测试程序集内隔离稳定，不修改业务预算或业务实现。
- 便携目录内容审计：通过；PresentMon SHA-256、必需 XBF/PRI、用户文档、许可证和卸载脚本均存在。
- Release 内容审计：未发现 PDB/ILK、测试 API Key、私钥或开发机绝对路径。
- ZIP：`GameNest-0.1.0-win-x64-portable.zip`，134,397,400 字节。
- ZIP SHA-256：`67FA2DB475C298F7555D772B20C78705E8B764199DFFA23D6316CBDE9B6864E8`；与 `.sha256` 记录一致。
- 隔离启动烟雾测试：通过；便携 EXE 初始化临时 SQLite、创建自动备份并通过正常窗口关闭退出，未读取真实用户库。
- 卸载安全预演：Windows PowerShell 5.1 下 `-DataAction Keep -WhatIf` 通过，正确显示保留数据库/备份/封面缓存的决定，未执行删除。

## Definition of Done 对照

- Release 构建和全部自动化测试通过。
- 新增维护服务有备份一致性/节流、引用感知缓存清理、诊断脱敏与排除数据库测试。
- 数据库备份、文件枚举、删除、日志读取和 ZIP 创建均在后台执行；文件选择器仍为薄 code-behind。
- 自动备份失败不会阻断游戏库；缓存清理限制在受控缓存目录；诊断导出由用户主动选择目标。
- 发布包使用相对 PresentMon 路径，不包含调试符号、测试密钥或开发机路径。
- 用户 README、覆盖层兼容性、隐私、第三方许可证、发布指南和本验收记录已更新。
- 未自动提交代码，未执行真实卸载或破坏性 Git 操作。

## 人工验收

- [x] 用户已确认 Phase 5 的视觉与封面流程无明显问题。
- [ ] 在干净 Windows x64 环境解压、启动并扫描测试目录。
- [ ] 卸载时分别验证“保留数据库/封面缓存”和“删除全部本地数据”。
- [ ] 在干净环境确认自包含启动不要求另装 .NET 或 Windows App SDK Runtime。
- [x] 当前机器使用隔离数据目录验证便携 EXE 启动、数据库初始化、自动备份和正常关闭。
- [x] 从 Release EXE 提取品牌图标，并验证最终便携包可加载 Logo/ICO 资源后正常启动和关闭。
- [x] 使用 `-WhatIf` 验证卸载脚本的保留数据分支和目标提示。

## 已知风险

- 便携版 0.1.0 暂无代码签名，下载分发可能触发 SmartScreen 提示。
- 当前开发机可以验证自包含包结构和启动产物，但不能替代干净 Windows 虚拟机的完整安装/扫描/卸载验收。
- PresentMon 普通权限 ETW 能力与机器策略相关；失败只降级 FPS，不影响发布包启动和游戏库。
