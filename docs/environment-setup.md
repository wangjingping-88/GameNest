---
title: GameNest 开发环境搭建
date: 2026-08-04
tags:
  - 编程/环境搭建
  - GameNest
  - dotnet
  - WinUI
aliases:
  - GameNest 开发环境
status: 已验证
---

# GameNest 开发环境搭建

> 适用版本：GameNest Phase 0 至 Phase 4  
> 更新日期：2026-08-04

> [!important] 安装位置约束
> 所有开发环境依赖统一安装到 `D:\Program Files`，不要安装到 C 盘。

## 1. 固定版本

本项目不使用 Preview 或 Experimental 通道。

| 组件 | 版本 |
| --- | --- |
| .NET SDK | 10.0.302 |
| C# | 14 |
| Windows App SDK | 2.3.1 Stable |
| 目标框架 | `net10.0-windows10.0.19041.0` |
| 架构 | x64 |

NuGet 包由根目录 `Directory.Packages.props` 集中固定，各项目通过 `packages.lock.json` 锁定完整依赖图。

## 2. 安装 .NET SDK 到 D 盘

团队约定所有环境依赖安装到 `D:\Program Files`，不要安装到 C 盘。

在 PowerShell 中执行：

```powershell
$installer = Join-Path $env:TEMP 'dotnet-install-gamenest.ps1'
Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
& $installer -Version '10.0.302' -InstallDir 'D:\Program Files\dotnet' -Architecture 'x64'
```

验证安装：

```powershell
& 'D:\Program Files\dotnet\dotnet.exe' --list-sdks
& 'D:\Program Files\dotnet\dotnet.exe' --info
```

输出中必须包含：

```text
10.0.302 [D:\Program Files\dotnet\sdk]
```

注意：如果 `dotnet` 命令仍指向 C 盘旧版本，不要覆盖旧安装。构建 GameNest 时直接使用上面的 D 盘完整路径，或由管理员把 `D:\Program Files\dotnet` 调整到 PATH 前部。

## 3. 还原固定依赖

先在 PowerShell 中进入仓库根目录，然后执行：

```powershell
& 'D:\Program Files\dotnet\dotnet.exe' restore GameNest.sln --locked-mode
```

`--locked-mode` 会在依赖图与已提交锁定文件不一致时立即失败，避免版本静默漂移。

## 4. Release 构建与测试

```powershell
& 'D:\Program Files\dotnet\dotnet.exe' build GameNest.sln -c Release --no-restore
& 'D:\Program Files\dotnet\dotnet.exe' test GameNest.sln -c Release --no-build --no-restore
```

预期结果：

- 构建 0 警告、0 错误；
- 全部测试通过。

## 5. 启动应用

```powershell
& '.\src\GameNest.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\GameNest.App.exe'
```

应用是 unpackaged、self-contained x64 WinUI 3 程序，不要求目标电脑预装 Windows App SDK Runtime。

首次启动会异步初始化 `%LOCALAPPDATA%\GameNest\data\gamenest.db`，日志写入 `%LOCALAPPDATA%\GameNest\logs`。窗口先显示，数据库初始化不会阻塞 UI 线程。

## 6. 常见检查

### SDK 选择错误

确认仓库根目录存在 `global.json`，其中版本为 10.0.302，并用 D 盘的 `dotnet.exe` 执行命令。

### locked restore 失败

如果是有意升级包版本，先修改 `Directory.Packages.props`，再执行一次普通 `restore` 更新锁定文件，完整构建与测试通过后才能保留变更。不要手工编辑 `packages.lock.json`。

### 应用窗口未出现

先检查任务管理器中是否存在 `GameNest.App.exe`，再查看 `%LOCALAPPDATA%\GameNest\logs`。不要以管理员身份重启；Phase 0 不需要提升权限。

## 7. Phase 4：安装固定 PresentMon

> [!important] 普通权限策略
> GameNest 固定使用 PresentMon 2.5.1 standalone x64，但不会自动提权或修改本地用户组。FPS 权限不足时只降级 FPS，其他功能继续运行。

安装目录固定为：

```text
D:\Program Files\GameNest\PresentMon\2.5.1
```

从 Intel/GameTechDev 官方 GitHub Release `v2.5.1` 下载 `PresentMon-2.5.1-x64.exe` 和 `LICENSE.txt`，保存到上述目录。不要放在 C 盘，不要从第三方镜像获取。

验证文件：

```powershell
Get-FileHash `
  'D:\Program Files\GameNest\PresentMon\2.5.1\PresentMon-2.5.1-x64.exe' `
  -Algorithm SHA256

Get-AuthenticodeSignature `
  'D:\Program Files\GameNest\PresentMon\2.5.1\PresentMon-2.5.1-x64.exe'
```

固定结果：

```text
SHA-256: 9BEC3083069F58F911E6A512F4806DB51A27BD096103087BC1D05EF54C80A191
签名状态: Valid
签名者: Intel Corporation
```

运行 `tests\render-probes\build-render-probes.cmd` 可构建 DX11、DX12 和 OpenGL 可控测试程序；需要 Visual Studio 2022 Build Tools 的 C++ x64 工具链。本机已有的 Build Tools 位于系统默认目录，本项目没有为 Phase 4 额外安装或迁移它。

若设置页报告 `PermissionDenied`，说明文件校验成功但当前普通用户不能创建 PresentMon ETW 会话。可由管理员按组织策略把账号加入 `Performance Log Users` 并重新登录；GameNest 不替用户执行该系统变更。
