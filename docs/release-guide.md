# GameNest Phase 6 发布指南

## 固定输入

- .NET SDK：10.0.302，入口 `D:\Program Files\dotnet\dotnet.exe`
- Windows App SDK：2.3.1 Stable
- 目标：Windows x64、self-contained、非单文件
- PresentMon：2.5.1 standalone x64，SHA-256 `9BEC3083069F58F911E6A512F4806DB51A27BD096103087BC1D05EF54C80A191`

## 生成发布包

从仓库根目录执行：

```powershell
& '.\scripts\Test-Release.ps1'
& '.\scripts\Publish-Portable.ps1'
```

输出：

- `artifacts\release\GameNest-0.1.0-win-x64-portable\`
- `artifacts\release\GameNest-0.1.0-win-x64-portable.zip`
- 同名 `.sha256` 文件

发布脚本会重新执行 Release publish、复制覆盖层和固定 PresentMon、移除 PDB，并调用 `Verify-ReleasePackage.ps1` 检查必需文件、PresentMon 哈希、调试产物、测试密钥、私钥和绝对开发机路径。

## 干净 Windows 验收

建议使用未安装 .NET SDK/Runtime 和 Windows App SDK Runtime 的 Windows 11 x64 虚拟机：

1. 复制 ZIP 和 `.sha256`，在虚拟机内复核哈希。
2. 解压并启动 `GameNest.App.exe`，确认不提示安装运行时。
3. 添加测试 EXE；新增一个只含测试 EXE 的目录，完成快速扫描和确认导入。
4. 重启，确认游戏库、封面和设置保留，并检查 `backups` 已创建自动备份。
5. 在设置页执行缓存清理和诊断导出，解压诊断包确认没有数据库、游戏路径或凭据。
6. 运行 `Uninstall-GameNest.cmd`，先选择“保留数据”，确认程序目录删除而本地数据库/缓存仍在。
7. 重新解压启动，确认原数据可读取；再次卸载并选择“删除数据”，确认本地数据目录移除。

当前开发机验证不能替代这项干净环境人工验收。签名、下载页和 SmartScreen 信誉也应在正式对外分发前单独完成。
