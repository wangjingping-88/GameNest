# GameNest Phase 7 验证报告

## 范围

Phase 7 只更新 GameNest 自身，不下载或更新游戏。实现包含公开 GitHub Release 检查、设置页与全局提示、ECDSA P-256 清单信任链、异步下载/哈希/解压、数据库备份、同卷便携目录交换、健康确认和失败回滚，以及 tag 发布工作流。

版本固定为：

- .NET SDK：10.0.302
- 目标框架：`net10.0-windows10.0.19041.0`
- Windows App SDK / WinUI 3：2.3.1 Stable
- NuGet：沿用 `Directory.Packages.props` 和锁定文件，无新增包
- GameNest：0.2.0

## 自动化覆盖

- 语义版本解析与数值比较；
- 最新正式 Release、预发布过滤、24 小时频率、ETag/304 缓存；
- 404、403/429、离线和资产缺失的非阻断降级；
- 临时 ECDSA P-256 正确签名、错误签名、错误哈希、超大包；
- 正常 ZIP、解压大小限制和 `../` 路径穿越；
- SQLite 自动检查偏好和缓存元数据持久化；
- 临时便携目录成功交换、旧进程未退出时拒绝强制结束、健康失败后的程序与数据库回滚；
- Release 资产命名、版本、SHA-256、签名以及开发机路径/私钥审计。

## 人工与外部验证边界

- 当前未生成或配置生产更新密钥，客户端受信公钥列表为空；这是安全门禁，不是测试公钥占位。应用只能检查更新并打开下载页。
- 0.1.0 没有更新客户端，必须手动安装 0.2.0；真实在线升级需在生产公钥嵌入且 0.2.0 正式发布后，以 0.2.1 验证。
- 本次不创建实际 GitHub Release，不设置生产 GitHub Secret，不提交或推送 Phase 7。
- 目录不可写、SmartScreen、断电中断和干净 Windows 实机仍需在正式发布前人工复核；独立更新签名不能替代 Authenticode。

## 验证结果

- `dotnet build GameNest.sln -c Release --no-restore`：通过，0 警告、0 错误。
- `scripts/Test-Release.ps1 -NoBuild`：通过，Domain 18、Application 34、Infrastructure 56、Telemetry 17，共 125 项，0 失败、0 跳过。
- `scripts/Publish-Portable.ps1`：通过，生成 0.2.0 x64 self-contained 便携目录和 ZIP；包内容审计通过。
- ZIP SHA-256：`1A4125875F3D94486EA4103B79489E4A01981F03DD0648E0E8A9CD44F90131BD`。
- ZIP 根布局：`.gamenest-portable-root`、`GameNest.App.exe`、`VERSION.txt` 均位于根目录，满足升级器安全解压契约。
- `scripts/Test-UpdateRelease.ps1`：通过，使用进程内临时 P-256 密钥生成并验证 `.update.json` / `.update.sig`，四项资产命名、版本、大小、SHA-256、签名及敏感内容审计通过；临时密钥已删除。
- Phase 7 PowerShell 脚本语法审计：通过；Windows PowerShell 5.1 兼容处理见 `docs/troubleshooting/2026-08-06_WindowsPowerShell5更新签名脚本兼容性.md`。
