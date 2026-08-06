# GameNest Phase 2 验收记录

> 阶段：Phase 2｜自动扫描  
> 日期：2026-08-05  
> 产品约束：`docs/product-plan.md`  
> 视觉基线：`docs/GameNest_Fluent_Air_UI.png`

## 交付范围

- ScanRoot 添加、启用/停用、移除、扫描检查点和在线状态；
- 快速扫描、深度扫描、暂停、继续与取消；
- Steam 清单、Windows 快捷方式和通用 EXE 统一适配器；
- 候选评分、可解释证据、同目录主程序归并和三档确认页；
- 批量确认导入、整目录排除、撤销最近排除和增量指纹；
- 稳定卷标识、磁盘离线保留和盘符变化后的路径重新绑定；
- 受限目录、I/O、重解析点和磁盘中途断开时按目录降级。

明确未实现：Epic/GOG、在线元数据、启动前后进程快照、launcher 子进程接管、停止或强杀、性能遥测与覆盖层。

## 固定版本

| 组件 | 版本 |
| --- | --- |
| .NET SDK | 10.0.302 |
| C# | 14.0 |
| 目标框架 | `net10.0-windows10.0.19041.0` |
| Windows App SDK / WinUI 3 | 2.3.1 Stable |
| CommunityToolkit.Mvvm | 8.4.2 |
| Microsoft.Data.Sqlite | 10.0.10 |

Phase 2 未新增 NuGet 包或第三方二进制；现有锁定版本和 `packages.lock.json` 未漂移。

## 自动化验收

| 验收点 | 证据 | 结果 |
| --- | --- | --- |
| Steam 卸载器不进入高置信 | `SteamUninstallerIsNotHighConfidence` | 通过 |
| 多信号通用 EXE 达到高置信 | `GenericExecutableWithMultipleGameSignalsIsHighConfidence` | 通过 |
| 同目录优先平台清单并保留备选 | `SteamManifestWinsPrimarySelectionAndAlternativesRemain` | 通过 |
| 指纹稳定且元数据变化后更新 | `FingerprintChangesWhenFileMetadataChanges` | 通过 |
| 暂停等待、继续和取消 | `ScanPauseControllerTests`、`GameScanServiceTests` | 通过 |
| Steam ACF 解析并跳过卸载器 | `SteamManifestSelectsGameExecutableAndSkipsUninstaller` | 通过 |
| 快捷方式保留参数和工作目录 | `ShortcutAdapterPreservesArgumentsAndWorkingDirectory` | 通过 |
| 通用扫描信号、磁盘消失容错 | `GenericScanUsesSignalsAndMissingDiskDoesNotCrash` | 通过 |
| ScanRoot、候选和排除项重启保留 | `RootCandidateAndExclusionPersistAcrossRepositoryInstances` | 通过 |
| 来源元数据、磁盘离线状态与盘符重绑定持久化 | `DiscoveryMetadataAndOfflineStatusPersist` | 通过 |
| `001` 到 `002` 显式迁移且可重复 | `SqliteDatabaseInitializerTests` | 通过 |
| Phase 0/1 行为不回归 | 原有 Domain / Application / Infrastructure / Telemetry 测试 | 通过 |

测试总数：42；失败：0；跳过：0。

## Definition of Done

| 条目 | 结果 | 说明 |
| --- | --- | --- |
| Release 构建成功且无新增警告 | 通过 | 固定 SDK，0 警告、0 错误 |
| 全部测试通过 | 通过 | Domain 3、Application 20、Infrastructure 18、Telemetry 1 |
| 新增公共服务具有测试 | 通过 | 评分、归并、扫描协调、暂停、三类适配器和 SQLite 扫描仓储有覆盖 |
| 耗时 I/O 不同步阻塞 UI | 通过 | SQLite 异步；卷识别/快捷方式/目录遍历在后台；通用扫描有界并发为 3 |
| CancellationToken 全链路传递 | 通过 | 页面命令 → `GameScanService` → 适配器/仓储/暂停等待 |
| 错误可理解且可诊断 | 通过 | 页面状态消息、扫描运行错误和后台滚动日志；单目录失败降级 |
| 不提交真实路径或游戏清单 | 通过 | 测试使用临时目录，扫描数据库留在 `%LOCALAPPDATA%` |
| README 可复现 | 通过 | 已补充 Phase 2 使用流程、边界和数据行为 |
| 无假数据冒充功能 | 通过 | 扫描页读取 SQLite 真实根目录与候选；空状态为真实零记录 |
| 人工验收记录 | 通过 | Release 实机启动、旧库迁移、125% DPI 默认窗口与扫描页检查 |
| 覆盖层安全边界 | 通过 | 未改动 Overlay，不含 DLL 注入、图形 Hook 或提权 |
| 第三方二进制记录 | 不适用 | 本阶段未新增第三方二进制 |

## 实机人工验收

- [x] Phase 1 用户数据库启动后从 `001_initial` 升级至 `002_phase2_scan`，日志显示共 2 个迁移；
- [x] Release 进程启动并保持响应，主窗口标题为 GameNest；
- [x] “扫描与导入”导航可通过 UI Automation 选择；
- [x] 用户确认的默认/最小窗口 1680×1000 下扫描页无滚动条；
- [x] 125% DPI 物理捕获为 2100×1250，页面左右边界、操作按钮和底部确认入口完整；
- [x] 根目录为空时显示真实空状态，快速/深度扫描按钮禁用；
- [x] 扫描范围、进度控制、三档候选、排除撤销和批量确认均有可见入口；
- [x] 自动化覆盖暂停、取消、磁盘消失、卸载器过滤和 SQLite 重启持久化。

视觉证据：`artifacts/visual-audit/2026-08-05/phase2-scan-page.png`。

## 已知风险

- 快速扫描和深度扫描都可递归用户明确添加的根目录；两者主要区别是调用意图和根目录模式。若未来增加“整盘深扫”向导，需要进一步限制快速扫描的目录深度。
- Windows 卷 GUID 和盘符重新绑定依赖卷处于可枚举且就绪状态；网络映射盘、云占位文件和特殊挂载点仍需增加设备矩阵。
- 实际无权限 ACL 目录和真实热拔插的验证主要依赖明确的异常捕获路径及磁盘消失自动化样本；后续应在可控 Windows 虚拟机中补充真实设备矩阵。
- 扫描候选当前使用通用本地图标；确认导入后的图标继续由 Phase 1 图标服务按需提取。
