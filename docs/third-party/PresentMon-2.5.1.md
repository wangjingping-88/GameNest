# PresentMon 2.5.1 第三方依赖记录

| 项目 | 固定值 |
| --- | --- |
| 名称 | Intel PresentMon |
| 版本 | 2.5.1 |
| 架构 | standalone x64 |
| 上游来源 | `https://github.com/GameTechDev/PresentMon/releases/tag/v2.5.1` |
| 便携版运行路径 | `Tools\PresentMon\PresentMon-2.5.1-x64.exe` |
| SHA-256 | `9BEC3083069F58F911E6A512F4806DB51A27BD096103087BC1D05EF54C80A191` |
| Authenticode | 有效；签名者 Intel Corporation |
| 许可证 | MIT；见 `docs/licenses/PresentMon-2.5.1-LICENSE.txt` |

GameNest 每次启动 FPS 会话前都校验固定哈希。文件缺失或哈希不符时只把 FPS 降级为 `--`，不会尝试下载替换文件，也不会影响 CPU、GPU、RAM 或游戏启动。

PresentMon 只通过标准输出向 GameNest 传递目标 PID 的呈现事件；GameNest 不创建永久 CSV，不注入 DLL，不 Hook 图形 API，也不修改游戏进程内存。
