# GameNest 性能覆盖层兼容性说明

## 正式支持

- Windows 10 2004（19041）或更高版本，x64；
- 窗口化和无边框全屏游戏；
- DirectX 11、DirectX 12、OpenGL 和 Vulkan 的非注入式 PresentMon 呈现事件；
- 100% 至 300% DPI、四角定位、75%/100%/125%/150% 缩放；
- 普通用户权限运行。

Phase 6 便携版在 `Tools\PresentMon` 内携带固定的 PresentMon 2.5.1，并在启动采集前校验 SHA-256。运行时不再依赖开发机或系统级绝对安装路径；仍可通过 `GAMENEST_PRESENTMON_PATH` 显式覆盖用于受控测试。

## 权限边界

PresentMon 的 ETW 会话可能要求管理员权限或当前用户属于 `Performance Log Users`。GameNest 会在设置页执行兼容性检测，但不会自动提权、不会静默重启为管理员，也不会修改本地用户组。

若检测结果为“权限不足”：

- FPS 显示 `--` 并附带原因；
- CPU、GPU、RAM 继续按各自能力采样；
- 游戏启动、停止和库管理不受影响；
- 用户可自行让管理员把账号加入 `Performance Log Users`，重新登录后再检测。此操作不是 GameNest 的默认要求。

## 显示边界

- 传统独占全屏可能覆盖外部窗口。GameNest 会提示改用无边框全屏，不会转向 DLL 注入或图形 API Hook。
- 游戏最小化、被切到后台或窗口暂时不可定位时，覆盖层隐藏并保持低频采样。
- 受保护内容、反作弊进程、HDR 合成路径和驱动不暴露 GPU Engine 计数器时，相关单项可能显示 `--`；GameNest 不绕过保护。
- GPU 是目标进程相关 GPU Engine 中最繁忙引擎的近似占用，不能等同于显卡总占用。

## 排查顺序

1. 在“设置 → 兼容性检测”确认 PresentMon 文件、哈希和 ETW 权限。
2. 确认游戏处于窗口化或无边框全屏，并位于前台。
3. 确认全局覆盖层与该游戏的覆盖配置均已启用。
4. 检查全局快捷键是否被其他程序占用。
5. 查看 `%LOCALAPPDATA%\GameNest\logs` 中的本地日志。
