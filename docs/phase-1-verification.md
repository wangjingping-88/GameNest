# GameNest Phase 1 验收记录

> 阶段：Phase 1｜最小可用游戏库  
> 日期：2026-08-04  
> 产品约束：`docs/product-plan.md`  
> 视觉基线：`docs/GameNest_Fluent_Air_UI.png`

## 交付范围

- 手工选择 EXE 或快捷方式添加游戏；
- 游戏卡片、详情、搜索、收藏、最近游玩和本地编辑；
- 本地图标异步提取、占位封面、启动和直接 PID 跟踪；
- SQLite 持久化及应用重启后的用户编辑保留；
- B｜Fluent Air 页面在用户确认的 1680×1000 原生窗口下限内适配。

明确未实现：目录或磁盘扫描、平台清单导入、在线元数据、启动前后进程快照、派生进程接管、停止/强杀、性能遥测与覆盖层。

## 固定版本

| 组件 | 版本 |
| --- | --- |
| .NET SDK | 10.0.302 |
| C# | 14.0 |
| 目标框架 | `net10.0-windows10.0.19041.0` |
| Windows App SDK / WinUI 3 | 2.3.1 Stable |
| CommunityToolkit.Mvvm | 8.4.2 |
| Microsoft.Data.Sqlite | 10.0.10 |

完整 NuGet 固定版本见 `Directory.Packages.props`，恢复解析结果见各项目 `packages.lock.json`。Phase 1 未新增第三方二进制或 NuGet 依赖。

## 自动化验收

| 验收点 | 证据 | 结果 |
| --- | --- | --- |
| 添加、编辑、收藏、搜索、移除 | `GameLibraryServiceTests.AddEditFavoriteSearchRemoveRoundTripUsesApplicationService` | 通过 |
| 阻止重复可执行路径 | `AddAsyncRejectsDuplicateResolvedExecutable` | 通过 |
| 20 个不同路径 | `AddAsyncSupportsTwentyDistinctUnicodeAndSpecialCharacterPaths` | 通过 |
| 中文、空格、特殊字符 EXE | `InspectAsyncAcceptsExecutableInUnicodeSpaceAndSpecialCharacterPath` | 通过 |
| LNK 目标、参数、工作目录 | `InspectAsyncResolvesShortcutTargetArgumentsAndWorkingDirectory` | 通过 |
| 20 条记录和用户编辑重启保留 | `TwentyGamesAndUserEditsPersistAcrossRepositoryInstances` | 通过 |
| 图标失败降级占位封面 | `ExtractIconAsyncReturnsNullWhenSourceDisappears` | 通过 |
| 添加不等待图标提取 | `AddAsyncReturnsBeforeIconExtractionAndRefreshPersistsIconLater` | 通过 |
| 冷启动添加交互预算 | `ColdAddReturnsBeforeIconExtractionWithinInteractiveBudget`（预算 1500 ms） | 通过 |
| 直接 PID、退出和重新启动 | `LaunchAsyncTracksDirectPidAndAllowsRelaunchAfterExit` | 通过 |
| 移除级联清理数据库关系 | `RemoveAsyncCascadesProfileAndAsset` | 通过 |
| Phase 0 架构与遥测留白不回归 | Domain / Telemetry 架构测试 | 通过 |

测试总数：29；失败：0；跳过：0。

## Definition of Done

| 条目 | 结果 | 说明 |
| --- | --- | --- |
| Release 构建成功且无新增警告 | 通过 | 使用固定 SDK 构建，0 警告、0 错误 |
| 全部测试通过 | 通过 | Domain 3、Application 12、Infrastructure 13、Telemetry 1 |
| 新增公共服务具有测试 | 通过 | 应用服务、SQLite 仓储、文件检查、图标服务和启动服务均有覆盖 |
| 耗时 I/O 不同步阻塞 UI | 通过 | 文件/COM 检查在后台；SQLite、图标流和日志使用异步路径 |
| CancellationToken 传递 | 通过 | ViewModel → Application → Infrastructure 全链路传递 |
| 错误可理解且可诊断 | 通过 | UI 状态消息 + 后台滚动日志；图标失败可降级 |
| 不提交本机真实数据 | 通过 | 测试使用临时目录；仓库不保存真实游戏清单和绝对数据路径 |
| README 可复现 | 通过 | 已补充 Phase 1 使用方式、数据目录和边界 |
| 无临时假数据冒充功能 | 通过 | 游戏库来自 SQLite；空状态使用真实零记录 |
| 人工验收清单 | 进行中 | 功能和视觉由用户在实机预览中复核，最终截图确认后归档 |

## 实机人工检查清单

- [x] 首页、游戏库、收藏、最近游玩、正在运行、扫描占位页、设置页可导航；
- [x] 搜索框边界与画布可区分，焦点时不出现默认蓝色下划线；
- [x] 搜索文字在当前 DPI 下视觉垂直居中；
- [ ] 非最大化窗口中首页游戏库区域完整显示且无滚动条；
- [ ] 添加真实 EXE 后记录应立即出现，体感不再约 2 秒；
- [ ] 图标随后异步补齐，失败时保持占位封面；
- [ ] 添加、编辑、收藏、移除、启动和退出后重启均符合预期；
- [ ] 关闭并重启 GameNest 后用户编辑仍保留。

未勾选项不是用自动化结果替代视觉或体感确认，而是保留给当前实机预览的最终用户验收。

## 已知风险

- Phase 1 只跟踪 `Process.Start` 返回的直接 PID。启动器派生真正游戏进程后立即退出时，状态会按直接进程结束；后续阶段才实现快照与进程接管。
- 本地图标依赖 Windows Shell 缩略图能力，个别可执行文件可能无可用图标；此时按设计显示占位封面。
- 用户指定的 1680×1000 原生最小窗口大于产品文档的 1000×680 DIP 建议值。在高 DPI、任务栏占用或工作区更小的设备上仍需继续验证。
- 缓存图标文件当前不会在移除游戏时同步删除，只删除数据库关系；后续缓存维护任务再统一回收。
