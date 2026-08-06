# Phase 0 验收记录

> 验收日期：2026-08-04  
> 产品基线：`docs/product-plan.md` 1.2 版

## 范围核对

已实现：

- 分层解决方案、Telemetry、Overlay 和四个测试项目；
- WinUI 3 主窗口、MVVM、依赖注入、主题令牌；
- SQLite 初始化、显式迁移、WAL；
- 后台滚动文件日志；
- NuGet 锁定文件与 GitHub Actions CI；
- 中文 README、架构决策、环境安装和验收记录。

明确未实现：

- 自动扫描或平台适配器；
- 在线元数据；
- 游戏启动、进程跟踪、停止或强杀；
- FPS/CPU/GPU/RAM 遥测；
- 性能覆盖层窗口或命名管道。

## 自动验证

最终命令：

```powershell
& 'D:\Program Files\dotnet\dotnet.exe' restore GameNest.sln --locked-mode
& 'D:\Program Files\dotnet\dotnet.exe' build GameNest.sln -c Release --no-restore
& 'D:\Program Files\dotnet\dotnet.exe' test GameNest.sln -c Release --no-build --no-restore
```

最终结果：

- locked restore：10 个项目全部成功；
- Release build：成功，0 警告、0 错误；
- 全部测试：14/14 通过，0 失败、0 跳过；
- Release 运行：`GameNest.App.exe` 主窗口在 1.238 秒内创建，标题为 `GameNest`，进程响应正常；
- 本地初始化日志：成功应用 `001_initial` 迁移。

## UI 验收

| 项目 | 结果 | 说明 |
| --- | --- | --- |
| 默认浅色 | 通过（源码与运行状态核对） | 初始 `ThemePreference.Light`，Light 令牌与参考图一致 |
| 深色主题 | 通过（实现与持久化测试） | 独立 Dark 令牌，通过异步命令保存 |
| 跟随系统 | 通过（实现与持久化测试） | 映射为 `ElementTheme.Default` |
| 三栏层级 | 通过（源码核对） | 左导航、中央 Hero/空状态、右会话与外观栏 |
| 空状态 | 通过（源码核对） | 0 款游戏，无虚构封面、游戏名和性能数据 |
| 最低尺寸 | 通过（实现核对） | 根布局 `1000 × 680`，初始窗口 `1360 × 820` |
| 键盘焦点 | 通过（控件能力核对） | 使用标准 Button/ListView，保留系统焦点视觉 |
| 100%–300% DPI | 有限验证 | manifest 为 PerMonitorV2；尚未逐档人工截图 |
| Release 窗口启动 | 通过 | 1.238 秒内获得主窗口句柄，`Responding=True` |

界面自动截图在验收过程中被用户主动停止，因此没有继续接管输入；该限制不影响构建和自动测试，但多 DPI 的逐档视觉对照仍列为风险。

## Definition of Done

| 条目 | 状态 |
| --- | --- |
| Release build 成功且无新增警告 | 通过：0 警告、0 错误 |
| 全部测试通过 | 通过：14/14 |
| 新增公共服务有测试 | 通过：迁移、设置存储、日志和边界测试 |
| 耗时 I/O 不同步阻塞 UI | 通过 |
| CancellationToken 可传递 | 通过：初始化与设置 API/命令 |
| 错误可理解、日志可诊断 | 通过 |
| 无密钥、真实游戏清单和个人绝对路径 | 通过 |
| README 可复现 | 通过 |
| 无临时假数据 | 通过 |
| Phase 0 人工验收已记录 | 通过，含有限验证说明 |
| 无注入、Hook 或默认提权 | 通过 |
| 第三方二进制版本/哈希/许可证 | 不适用：Phase 0 未引入第三方二进制；NuGet 由锁文件固定 |
