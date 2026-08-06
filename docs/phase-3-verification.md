# GameNest Phase 3 验收记录

> 阶段：Phase 3｜可靠的运行与停止  
> 日期：2026-08-05  
> 产品约束：`docs/product-plan.md`  
> 视觉基线：`docs/GameNest_Fluent_Air_UI.png`

## 交付范围

- 启动前后系统进程快照；
- 直接进程、父子进程链和 launcher 立即退出后的实际游戏进程接管；
- 已确认、可能、未确认三档身份及停止安全门；
- 正常关闭、配置化等待、二次确认强制结束和 PID 启动时间复核；
- PlaySession、退出类型、跟踪 PID、本次时长、累计时长及遗留会话恢复；
- 运行页和游戏详情中的真实运行状态、会话时长与停止入口。

明确未实现：性能指标采集、ETW/PresentMon、Telemetry 管道、覆盖层窗口、全局快捷键、图形 API Hook、DLL 注入、默认提权、在线元数据或 Epic/GOG 适配器。

## 固定版本

| 组件 | 版本 |
| --- | --- |
| .NET SDK | 10.0.302 |
| C# | 14.0 |
| 目标框架 | `net10.0-windows10.0.19041.0` |
| Windows App SDK / WinUI 3 | 2.3.1 Stable |
| CommunityToolkit.Mvvm | 8.4.2 |
| Microsoft.Data.Sqlite | 10.0.10 |

Phase 3 未新增 NuGet 包或第三方二进制，锁定文件未漂移。

## 自动化验收

| 验收点 | 证据 | 结果 |
| --- | --- | --- |
| 直接 EXE 退出后状态回到未运行 | `DirectExecutableExitsAndCompletesNaturalSession` | 通过 |
| 父进程派生子进程后接管子 PID | `ParentProcessChildIsAdoptedAndRemainsPrimaryAfterParentExit` | 通过 |
| launcher 在首次后快照前退出仍保持会话 | `LauncherMayExitBeforeFirstPostLaunchSnapshotWithoutEndingGame` | 通过 |
| 未确认进程不允许停止 | `ProbableProcessCanBeTrackedButCannotBeStopped` | 通过 |
| 正常关闭不触发强杀 | `GracefulCloseWaitsForExitAndDoesNotKill` | 通过 |
| 强制结束必须经过确认结果 | `ForceStopRequiresPriorConfirmationResultAndPersistsForcedExit` | 通过 |
| PID 启动时间不符时拒绝强杀 | `StartAndKillValidateProcessStartTimeToPreventPidReuse` | 通过 |
| 会话结束和累计时长只结算一次 | `CompletingSessionPersistsExitAndUpdatesGameTotalsExactlyOnce` | 通过 |
| 每个游戏只允许一个活动会话 | `OnlyOneActiveSessionPerGameIsAllowed` | 通过 |
| 遗留活动会话可恢复 | `RecoverInterruptedSessionsCompletesStaleActiveSession` | 通过 |
| `001` 到 `003` 迁移可重复 | `SqliteDatabaseInitializerTests` | 通过 |

测试总数：53；失败：0；跳过：0。

## Definition of Done

| 条目 | 结果 | 说明 |
| --- | --- | --- |
| Release 构建成功且无新增警告 | 通过 | 固定 SDK，0 警告、0 错误 |
| 全部测试通过 | 通过 | Domain 5、Application 21、Infrastructure 26、Telemetry 1 |
| 新增公共服务具有测试 | 通过 | 运行协调、进程控制、会话仓储和恢复流程均有覆盖 |
| 耗时 I/O 不同步阻塞 UI | 通过 | 进程枚举、启动、状态检查和控制均在后台；SQLite 使用异步 API |
| CancellationToken 全链路传递 | 通过 | 页面/对话框 → 应用服务 → 运行协调器/控制器/仓储 |
| 错误可理解且可诊断 | 通过 | 停止结果含安全原因；启动、接管、监视和结算均写日志 |
| 不提交真实路径或游戏清单 | 通过 | 快照不持久化系统进程清单；测试使用脚本快照和系统 ComSpec |
| README 可复现 | 通过 | 已补运行、接管、停止和会话行为 |
| 无假数据冒充功能 | 通过 | 实机页读取用户 SQLite；验收未注入游戏或会话 |
| 人工验收记录 | 通过 | Release 实机启动、003 迁移、运行页 UI Automation 边界检查 |
| 覆盖层安全边界 | 通过 | Telemetry/Overlay 未修改，无注入、Hook 或提权 |
| 第三方二进制记录 | 不适用 | 本阶段未新增第三方二进制 |

## 实机人工验收

- [x] Phase 2 用户数据库从 2 个迁移升级到 `003_phase3_runtime`，日志显示共 3 个迁移；
- [x] Release 进程启动并保持响应，窗口标题为 GameNest；
- [x] “正在运行”导航可通过 UI Automation 选择；
- [x] 1680×1000 窗口下标题、说明、搜索框和真实空状态均在可见边界内；
- [x] UI Automation 检测到 0 个可见滚动条；
- [x] 未向用户库写入示例游戏、伪进程或伪会话；
- [x] 真实 Windows 进程控制集成测试验证 PID 启动时间保护与清理。

本轮未保留截图：当前 WinUI 合成器对 `PrintWindow` 返回空白帧，屏幕复制只得到桌面背景；无效图片已删除，控件边界和可见性由 UI Automation 记录验证。

## 已知风险

- 受保护、提权或反作弊进程可能无法读取路径或启动时间；此时降级为仅跟踪，不提供停止，不尝试提权。
- 只命中安装目录但无法证明父子关系的进程标记为“可能”，launcher 极端重启链可能需要用户配置预期进程名。
- GameNest 异常退出后不会尝试接管仍在运行的旧进程；遗留会话按恢复时刻以 `TrackingLost` 结算，时长可能包含少量无法确定的尾段。
- `CloseMainWindow` 只适用于具有可关闭顶层窗口的进程；无窗口或忽略关闭请求的游戏会进入二次确认流程。
- 父子与 launcher 场景使用确定性脚本快照覆盖；仍建议在可控 Windows 虚拟机中增加 Steam 等真实 launcher 设备矩阵。
