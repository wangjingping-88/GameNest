# GameNest Phase 4 验收记录

> 阶段：Phase 4｜性能遥测与游戏内覆盖层  
> 日期：2026-08-05  
> 产品约束：`docs/product-plan.md`  
> 视觉基线：`docs/GameNest_Fluent_Air_UI.png`

## 交付范围

- 独立 `GameNest.Telemetry` 与 `GameNest.Overlay`；
- PresentMon 2.5.1 FPS、进程组 CPU/私有内存、PDH GPU Engine 采集；
- 各指标独立能力状态与单项降级；
- 当前用户命名管道、透明置顶、鼠标穿透、不激活覆盖层窗口；
- 目标窗口定位、前台/最小化隐藏、250 ms 窗口跟踪和全局快捷键；
- 全局及按游戏 OverlayProfile、设置页实时预览和兼容性检测；
- DX11、DX12、OpenGL 可控渲染程序与隔离验收工具。

明确未实现：DLL 注入、图形 API Hook、默认提权、帧时间曲线、温度/功耗、1% Low、在线元数据和 Phase 5 视觉完善。

## 固定版本

| 组件 | 固定版本 |
| --- | --- |
| .NET SDK / C# | 10.0.302 / 14 |
| 目标框架 | `net10.0-windows10.0.19041.0` |
| Windows App SDK / WinUI 3 | 2.3.1 Stable |
| PresentMon | 2.5.1 standalone x64 |
| PresentMon SHA-256 | `9BEC3083069F58F911E6A512F4806DB51A27BD096103087BC1D05EF54C80A191` |

PresentMon 安装于 `D:\Program Files\GameNest\PresentMon\2.5.1`；MIT License 已保留。Phase 4 未引入未固定版本的 NuGet 包。

## 自动化验收

| 验收点 | 证据 | 结果 |
| --- | --- | --- |
| 1 秒 FPS 滚动窗、乱序重置、交换链过期 | `FpsRollingWindowTests` | 通过 |
| 标题驱动 CSV 与目标 PID 过滤 | `PresentMonCsvParserTests` | 通过 |
| 进程组 CPU/RAM 聚合 | `ProcessMetricSamplerTests` | 通过 |
| GPU 同引擎求和、最繁忙引擎选择 | `GpuMetricAggregatorTests` | 通过 |
| FPS 缺失时 CPU/RAM 继续采样 | `WindowsPerformanceTelemetryTests` | 通过 |
| 管道版本和 64 KB 长度限制 | `OverlayPipeProtocolTests` | 通过 |
| 独立覆盖层握手、穿透/不激活样式、DPI 重定位、退出 | `WindowsOverlayControllerTests` | 通过 |
| 游戏运行/退出启动并清理遥测与覆盖层 | `OverlayRuntimeCoordinatorTests` | 通过 |
| 全局与按游戏配置持久化 | `SqliteOverlayProfileRepositoryTests` | 通过 |
| 架构边界 | `ArchitectureBoundaryTests` | 通过 |

最终测试：Domain 15、Application 22、Infrastructure 27、Telemetry 17，共 81 项；失败 0，跳过 0。

## 可控渲染实机验收

使用 `tests/GameNest.Phase4Harness`，不向用户游戏库写入测试记录。

| API / 模式 | 窗口定位 | 不抢焦点与穿透 | 重定位 | 遥测宿主 + Overlay CPU | Overlay 工作集 |
| --- | --- | --- | ---: | ---: | ---: |
| DirectX 11 窗口化 | 通过 | 通过 | 37 ms | 1.35% | 36.0 MB |
| DirectX 12 窗口化 | 通过 | 通过 | 64 ms | 1.82% | 36.0 MB |
| OpenGL 窗口化 | 通过 | 通过 | 96 ms | 1.29% | 35.8 MB |
| DirectX 11 无边框全屏 | 通过，识别全屏覆盖 | 通过 | 不适用 | 0.95% | 35.8 MB |

四轮结束后没有残留 `GameNest.Overlay`、PresentMon 或渲染测试进程。CPU 低于 2%，Overlay 工作集低于 80 MB。

## 未通过/受阻项

本机普通用户无法创建 PresentMon ETW 会话：固定文件与哈希通过，但 PresentMon 返回退出码 6 / `access denied`。因此 DX11、DX12、OpenGL 的实际 FPS 数值验收均为**权限受阻，未通过**；运行时正确显示 `PermissionDenied` / `--`，CPU、GPU、RAM 仍有真实值。

这不是用假数据可以替代的验收项。若后续由管理员把测试账号加入 `Performance Log Users` 并重新登录，应再次运行三种 API 工具，记录 1 秒滚动 FPS 后才能把该项改为通过。

## UI 人工验收

- [x] 用户已检查 Phase 4 最新 UI，反馈“UI 没有大问题”；
- [x] 设置页可配置启用、位置、缩放、透明度、四项指标、快捷键和前台隐藏；
- [x] 预览明确标注“示例值”，不会冒充实时游戏指标；
- [x] 兼容性检测显示普通权限与 FPS 降级原因；
- [ ] 用户尚未使用真实游戏验证运行时覆盖层；本机没有游戏，已用可控渲染程序替代工程验收。

## Definition of Done 状态

| 条目 | 结果 | 说明 |
| --- | --- | --- |
| locked restore | 通过 | 11 个项目按锁定文件还原 |
| Release 构建、全部测试 | 通过 | 0 警告、0 错误；81/81 通过、0 跳过 |
| 公共服务测试 | 通过 | 遥测、覆盖层、配置、协调器均有覆盖 |
| 本地 I/O 与采样不阻塞 UI | 通过 | 后台任务、PeriodicTimer、有界消息与异步管道 |
| CancellationToken | 通过 | 启停、管道、能力检查和设置链路传递 |
| 可理解的错误与日志 | 通过 | 指标分级状态、权限/超时/断线说明 |
| README 与兼容性/隐私文档 | 通过 | 已更新，可离线复现 |
| 无假数据 | 通过 | 示例预览有明确标签；FPS 权限问题未伪造通过 |
| 无注入、Hook、默认提权 | 通过 | 外部窗口 + ETW/PDH，普通权限策略 |
| 第三方版本/哈希/许可证 | 通过 | PresentMon 2.5.1 完整记录 |
| Phase 4 全部验收 | 部分受阻 | 真实 FPS 值因本机 ETW 权限未通过 |
