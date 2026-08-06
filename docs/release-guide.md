# GameNest Phase 7 发布指南

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

输出（版本从 `Directory.Build.props` 读取）：

- `artifacts\release\GameNest-0.2.2-win-x64-portable\`
- `artifacts\release\GameNest-0.2.2-win-x64-portable.zip`
- `artifacts\release\GameNest-0.2.2-win-x64-portable.sha256`

发布脚本会重新执行 Release publish、复制覆盖层和固定 PresentMon、移除 PDB，并调用 `Verify-ReleasePackage.ps1` 检查必需文件、PresentMon 哈希、调试产物、测试密钥、私钥和绝对开发机路径。

## 更新签名与 GitHub Release

正式发布前在仓库外生成 ECDSA P-256 密钥：

```powershell
& '.\scripts\New-UpdateSigningKey.ps1' `
  -PrivateKeyOutput 'D:\安全位置\GameNest-update-private.base64' `
  -PublicKeyOutput 'D:\安全位置\GameNest-update-public.base64'
```

- 私钥内容保存到 GitHub Secret `GAMENEST_UPDATE_PRIVATE_KEY`，不得提交、粘贴到 issue 或出现在日志。
- 公钥及 `keyId` 应分别配置为 GitHub Variables `GAMENEST_UPDATE_PUBLIC_KEY`、`GAMENEST_UPDATE_KEY_ID`；0.2.2 已内置同一把 `GAMENESTPUBLIC` 受信公钥。私钥仍只能保存在 GitHub Secret。

推送严格的 `vMAJOR.MINOR.PATCH` tag 后，`.github/workflows/release.yml` 会固定使用 .NET 10.0.302，下载并校验 PresentMon 2.5.1，运行全部测试、生成便携包、签名清单、执行资产审计并创建 Release。缺少任一签名配置时工作流在发布前失败。

固定资产：

- `GameNest-{version}-win-x64-portable.zip`
- `GameNest-{version}-win-x64-portable.sha256`
- `GameNest-{version}-win-x64-portable.update.json`
- `GameNest-{version}-win-x64-portable.update.sig`

本地仅验证签名脚本和资产契约时，可在生成便携包后运行：

```powershell
& '.\scripts\Test-UpdateRelease.ps1'
```

该脚本只使用进程内临时密钥，产物不得发布。实际 `0.2.2` Release 仍以 GitHub Secret、Variables 和 tag 工作流的验收结果为准。

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
