---
title: GameNest PresentMon 普通权限 ETW 拒绝
date: 2026-08-05
tags:
  - 日常记录/踩坑记录
  - GameNest
  - PresentMon
  - ETW
status: 已定位
---

# GameNest PresentMon 普通权限 ETW 拒绝

## 现象

PresentMon 2.5.1 的文件、SHA-256 与 Intel Authenticode 签名均通过，但普通用户运行时退出码为 6，错误包含 `failed to start trace session: access denied`，没有产生呈现事件。

## 根因

PresentMon 需要创建系统 ETW 会话。部分 Windows 策略只允许管理员或 `Performance Log Users` 组成员创建该会话。二进制能启动不代表 ETW 权限可用，因此不能只用“文件存在 + 哈希正确”判定 FPS 能力。

## 修复与设计

- 设置页兼容性检测实际启动 1 秒、无 CSV 的 PresentMon 会话；
- 超时限制为 5 秒，并区分 `PermissionDenied`、`NotSupported` 和一般 `Unavailable`；
- FPS 单项显示 `--`，CPU/GPU/RAM 继续运行；
- GameNest 不自动提权、不修改用户组、不以假值冒充 FPS；
- 若用户主动需要 FPS，可由管理员把账号加入 `Performance Log Users`，重新登录后复测。

## 调试经验

验证 ETW 工具时至少分三层：文件完整性、进程能否启动、最小真实会话能否创建。只检查前两层会把权限问题推迟到游戏运行期，导致 UI 误报“可用”。
