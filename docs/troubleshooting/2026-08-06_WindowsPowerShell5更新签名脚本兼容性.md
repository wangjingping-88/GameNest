---
title: Windows PowerShell 5.1 更新签名脚本兼容性
date: 2026-08-06
tags:
  - GameNest
  - PowerShell
  - ECDSA
  - 踩坑记录
aliases:
  - GameNest 更新签名脚本兼容性
---

# Windows PowerShell 5.1 更新签名脚本兼容性

## 现象

Phase 7 本地发布审计最初直接在 PowerShell 中调用 ECDSA：

- `ECDsaCng` 没有 `ExportPkcs8PrivateKey` / `ImportPkcs8PrivateKey`；
- `string.Contains(string, StringComparison)` 找不到双参数重载。

同一脚本在 GitHub Actions 的 PowerShell 7 / 现代 .NET 中可用，但开发机默认 `powershell.exe` 是 Windows PowerShell 5.1，其运行时仍是 .NET Framework。

## 根因

PowerShell 语言版本不是唯一兼容性边界。Windows PowerShell 5.1 通过 .NET Framework 反射调用类型，无法看到现代 .NET 才增加的密码学和字符串 API。

## 解决

- 把 P-256 PKCS#8/SPKI 导入导出、P1363 签名和验签放进 .NET 10 文件型应用 `scripts/UpdateCryptoTool.cs`；
- PowerShell 只负责参数校验、文件路径、清单 JSON 和调用固定的 .NET 10.0.302；
- 字符串审计使用兼容 .NET Framework 的 `IndexOf(pattern, StringComparison) -ge 0`；
- 临时私钥只写入系统临时目录，使用后验证绝对路径仍位于临时根，再递归删除；私钥值不输出到日志。

> [!warning] 安全边界
> 临时测试密钥只能验证脚本和资产契约。生产私钥必须保存在 GitHub Secret，不能进入仓库、发布包或日志。

## 验证命令

```powershell
& '.\scripts\Publish-Portable.ps1'
& '.\scripts\Test-UpdateRelease.ps1'
```

通过标志：四项资产存在，ZIP/清单 SHA-256 一致，ECDSA P-256 签名验证成功，且审计未发现私钥或开发机绝对路径。

## 关联

- [[2026-08-06_WindowsPowerShell_UTF8无BOM]]
- [[GameNest Phase 7 验证报告]]
