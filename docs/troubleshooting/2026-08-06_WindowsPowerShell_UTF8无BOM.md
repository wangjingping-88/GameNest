---
title: Windows PowerShell 5.1 执行 UTF-8 无 BOM 脚本的编码陷阱
date: 2026-08-06
tags:
  - GameNest
  - PowerShell
  - 发布
  - 踩坑记录
aliases:
  - PowerShell 中文脚本解析失败
---

# Windows PowerShell 5.1 执行 UTF-8 无 BOM 脚本的编码陷阱

## 现象

同一份 `.ps1` 在现代 PowerShell 可读取，但 Windows PowerShell 5.1 把 UTF-8 无 BOM 的中文字符串按本地 ANSI 编码解释，可能出现乱码，甚至因误解码字符破坏引号而直接报 `ParserError`。本阶段测试脚本和发布审计脚本先后遇到同一问题。

## 处理策略

- 开发、CI 和发布审计脚本只使用 ASCII 提示，保证 Windows PowerShell 5.1 与 PowerShell 7 都能解析。
- 面向最终用户的卸载脚本保留中文，但发布时使用 `UTF8Encoding(true)` 写入带 BOM 的副本。
- `.cmd` 包装器通过 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File` 调用发布包内带 BOM 的脚本。

```powershell
$source = Get-Content -LiteralPath $sourcePath -Raw -Encoding UTF8
[IO.File]::WriteAllText($targetPath, $source, [Text.UTF8Encoding]::new($true))
```

## 验证

使用系统自带 Windows PowerShell 执行卸载脚本的安全预演：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\Uninstall-GameNest.ps1 -DataAction Keep -WhatIf
```

预演必须正确显示中文数据保留说明，只输出将执行的删除目标，不实际删除程序或本地数据。
