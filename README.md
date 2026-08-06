# GameNest

GameNest 是一个本地优先的 Windows 单机游戏启动器。本仓库当前实现到 **Phase 7：GitHub Release 在线升级**；真实 FPS 在当前开发机因普通用户 ETW 权限受阻，仍按用户决定延期到家中环境补测。产品与技术约束以 [`docs/product-plan.md`](docs/product-plan.md) 为准。

## 当前范围

Phase 0 至 Phase 3 的工程骨架、游戏库、扫描和可靠运行之外，Phase 4 已包含：

- 手工选择 `.exe` 或 `.lnk` 添加游戏，快捷方式会解析目标、参数、工作目录和图标来源；
- SQLite 持久化的游戏卡片、详情、搜索、收藏、最近游玩和本地编辑；
- 本地图标异步提取与占位封面，添加记录后立即显示，图标在后台补齐；
- 启动前后进程快照、直接 PID 与父子进程链跟踪，并可在 launcher 退出后接管实际游戏进程；
- 已确认、可能和未确认三档进程身份；只有带启动时间身份校验的已确认进程才允许停止；
- 正常关闭、按启动配置等待、超时后二次确认强制结束，PID 复用时拒绝操作；
- PlaySession 持久化、本次时长、累计时长、退出类型，以及应用异常退出后的遗留会话恢复；
- 中文、空格和特殊字符路径测试，以及 20 条不同路径记录的持久化验收；
- B｜Fluent Air 浅色/深色/跟随系统主题和非最大化窗口响应式布局。
- ScanRoot 添加、启用/停用、移除，以及稳定卷标识、相对路径和盘符变化后的重新绑定；
- 快速扫描与深度扫描，扫描可暂停、继续或取消，目录 I/O 在后台以 3 个工作线程有界执行；
- Steam `libraryfolders.vdf` / `appmanifest_*.acf`、桌面/开始菜单快捷方式和通用 EXE 三类统一适配器；
- 可解释候选评分、同目录主程序归并、“确定是游戏 / 可能是游戏 / 已忽略”确认页；
- 批量确认导入、整目录排除、撤销最近排除和“路径 + 大小 + 修改时间”增量指纹；
- 无权限目录、重解析点循环、路径过长和扫描中磁盘断开时按目录降级，不使应用崩溃。
- PresentMon 2.5.1 的 1 秒滚动 FPS、已确认进程组 CPU/私有内存和 GPU Engine 近似占用；
- 四项指标独立能力状态，任一失败时只把该项降级为 `--`；
- 独立 `GameNest.Overlay` 透明置顶窗口、鼠标穿透、不抢焦点、四角定位和全局快捷键；
- 窗口化/无边框全屏目标窗口跟踪，前台/最小化隐藏及 250 ms 重定位；
- 全局和按游戏覆盖配置、设置页实时示例预览、普通权限兼容性检测；
- DX11、DX12、OpenGL 可控渲染程序和不写用户游戏库的隔离验收工具。

Phase 5 已提供可插拔元数据接口、来源归因和撤销机制，当前默认使用完整手工流程且不访问网络。Phase 6 增加每日自动备份、引用感知缓存清理、脱敏诊断导出、x64 自包含便携包和带数据保留选择的卸载脚本。Phase 7 增加公开 GitHub Release 检查、ETag/24 小时缓存、ECDSA 清单信任链、异步下载与安全解压、同卷便携目录交换、健康确认和数据库回滚。0.2.4 已内置受信生产公钥，只会安装同时通过签名和哈希校验的更新资产；0.1.0 与未形成 Release 的历史标签仍须手动安装。以下功能尚未实现：Epic/GOG 平台适配、正式在线元数据提供者、帧时间曲线、温度/功耗、1% Low、安装器和 Authenticode 代码签名。图形 API Hook、DLL 注入、默认提权和绕过反作弊明确不做。

## 固定工具链

| 组件 | 固定版本 |
| --- | --- |
| .NET SDK | 10.0.302 |
| 目标框架 | `net10.0-windows10.0.19041.0` |
| Windows App SDK / WinUI 3 | 2.3.1 Stable |
| CommunityToolkit.Mvvm | 8.4.2 |
| Microsoft.Data.Sqlite | 10.0.10 |
| Microsoft.Extensions.* | 10.0.10 |
| Microsoft.NET.Test.Sdk | 18.8.1 |
| xUnit v3 | 3.2.2 |
| PresentMon | 2.5.1 standalone x64 |

完整 NuGet 版本见 [`Directory.Packages.props`](Directory.Packages.props)，传递依赖见各项目的 `packages.lock.json`。

## 环境要求

- Windows 10 2004（内部版本 19041）或更高版本；
- x64 系统；
- .NET SDK 10.0.302，团队环境统一安装到 `D:\Program Files\dotnet`；
- PresentMon 2.5.1 安装到 `D:\Program Files\GameNest\PresentMon\2.5.1`；
- 可访问 NuGet.org，首次还原依赖时使用。

详细安装步骤见 [`docs/environment-setup.md`](docs/environment-setup.md)。

## 构建与测试

在 PowerShell 中从仓库根目录执行：

```powershell
& 'D:\Program Files\dotnet\dotnet.exe' restore GameNest.sln --locked-mode
& 'D:\Program Files\dotnet\dotnet.exe' build GameNest.sln -c Release --no-restore
& '.\scripts\Test-Release.ps1' -NoBuild
```

构建完成后启动主程序：

```powershell
& '.\src\GameNest.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\GameNest.App.exe'
```

首次启动会在 `%LOCALAPPDATA%\GameNest` 下创建：

- `data\gamenest.db`：本地 SQLite 数据库；
- `assets\cache`：按内容哈希复用的图标和封面缩略图缓存；
- `logs\gamenest-yyyyMMdd.log`：滚动日志。
- `backups\gamenest-auto-*.db`：每天最多一次、最近 7 份的自动数据库备份。

生成便携发布包：

```powershell
& '.\scripts\Publish-Portable.ps1'
& '.\scripts\Test-UpdateRelease.ps1'
```

用户操作与卸载说明见 [`docs/user-guide.md`](docs/user-guide.md)，发布流程与干净环境清单见 [`docs/release-guide.md`](docs/release-guide.md)。

## 使用方式

1. 在首页或“游戏库”页面选择“添加游戏”。
2. 选择本地 `.exe` 或 `.lnk`。记录会先写入本地库并立即显示，图标随后在后台加载。
3. 在游戏库中选择卡片，可编辑名称、简介、参数和工作目录，也可收藏、启动或移除记录。
4. 顶部搜索框按游戏名称即时过滤当前页面。

封面与元数据：

1. 在游戏详情中选择“选择封面”，从本机选择 PNG、JPG、JPEG、BMP 或 WebP；GameNest 只保存受控尺寸的本地缩略图。
2. 安装目录顶层存在 `cover`、`poster`、`folder`、`background` 或 `banner` 命名图片时，后台可在未手工指定封面的前提下发现它。
3. 名称、简介、启动参数、工作目录和手工封面均带保护标记，后续元数据提供者不得覆盖。
4. 当前不注册在线提供者，不访问网络；可插拔接口的离线失败隔离、来源记录和错误匹配撤销已由自动化测试覆盖。

运行与停止流程：

1. 点击“启动游戏”后，GameNest 先记录启动前进程快照，再跟踪入口进程、父子进程链、可执行路径和预期进程名。
2. launcher 退出但确认的派生进程仍运行时，会话保持运行，并把详情中的 PID 切换到实际游戏进程。
3. 详情显示本次会话、累计时长和进程身份。未完全确认的进程只显示状态，不提供停止入口。
4. 点击“停止”后先向已确认进程请求正常关闭，并按启动配置等待；仍未退出时才显示带存档风险说明的二次强制确认。
5. 应用关闭不会结束游戏，只把未完成会话记为应用已关闭；异常中断留下的活动会话在下次启动时以“跟踪中断”结算。

自动扫描流程：

1. 打开“扫描与导入”，选择“添加目录”；默认不会主动遍历系统盘。
2. “快速扫描”优先读取 Steam 清单、桌面/开始菜单快捷方式和已配置目录；“深度扫描”递归检查已启用根目录。
3. 扫描期间可暂停、继续或取消；状态区显示阶段、当前路径、已检查目录、候选数和耗时。
4. 在“确定是游戏 / 可能是游戏 / 已忽略”中查看分数和命中依据。同目录只默认勾选主程序，备选入口保留。
5. 勾选候选并确认导入；误报可“排除目录”，也可撤销最近一次排除。

扫描只读取本机文件元数据并写入 `%LOCALAPPDATA%\GameNest\data\gamenest.db`，不会上传路径或游戏列表。增量扫描使用路径、文件大小和修改时间生成轻量指纹，不读取文件内容哈希。

性能覆盖层流程：

1. 在“设置”中启用覆盖层，选择位置、缩放、透明度、指标和全局快捷键；默认快捷键为 `Ctrl+Shift+F12`。
2. 先运行“兼容性检测”。PresentMon ETW 权限不足时 FPS 显示 `--`，CPU/GPU/RAM 仍继续工作，GameNest 不会自动提权。
3. 游戏进入 Running 且找到已确认 PID 的窗口后，独立覆盖层显示在游戏客户区；最小化或切到后台时自动隐藏。
4. 传统独占全屏不保证外部窗口可见，请切换为无边框全屏。GameNest 不使用 DLL 注入或图形 API Hook。
5. 游戏退出或覆盖层关闭后停止 PresentMon/PDH 会话并关闭覆盖层进程。

没有本机游戏时，可按 [`tests/render-probes/README.md`](tests/render-probes/README.md) 构建 DX11、DX12、OpenGL 测试窗口，再用 [`tests/GameNest.Phase4Harness/README.md`](tests/GameNest.Phase4Harness/README.md) 运行隔离验收；该工具不读取或修改用户游戏库。

## 解决方案结构

```text
src/
  GameNest.App/             WinUI 3、ViewModel、主题与组合根
  GameNest.Domain/          纯领域层
  GameNest.Application/     用例接口与应用规则
  GameNest.Infrastructure/  SQLite、图片缓存、扫描适配器、Windows 卷/进程识别与文件日志
  GameNest.Telemetry/       PresentMon/Process/PDH 遥测、窗口定位与覆盖层控制
  GameNest.Overlay/         独立 Win32 透明覆盖层进程
tests/
  GameNest.Domain.Tests/
  GameNest.Application.Tests/
  GameNest.Infrastructure.Tests/
  GameNest.Telemetry.Tests/
  GameNest.Phase4Harness/   不写用户库的 Phase 4 隔离验收工具
  render-probes/            DX11、DX12、OpenGL 最小渲染程序
```

依赖方向和关键取舍见 [`docs/architecture-decisions.md`](docs/architecture-decisions.md)。Phase 7 验收见 [`docs/phase-7-verification.md`](docs/phase-7-verification.md)，兼容性见 [`docs/overlay-compatibility.md`](docs/overlay-compatibility.md)，本地数据说明见 [`docs/privacy.md`](docs/privacy.md)，第三方声明见 [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)。
