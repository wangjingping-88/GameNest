# GameNest Phase 4 可控渲染验收程序

该目录包含三个最小、离线、无第三方运行时依赖的窗口化渲染程序：DirectX 11、DirectX 12 和 OpenGL。它们只清屏并调用对应 API 的 Present/SwapBuffers，用于验证 PresentMon 管道，不访问 GameNest 数据库或用户游戏目录。

构建：

```powershell
& '.\tests\render-probes\build-render-probes.cmd'
```

输出位于 `tests/render-probes/bin`。这些二进制是本地构建产物，不纳入源代码交付。

默认以 960×540 窗口化运行；追加 `--borderless` 可切换为当前主显示器的无边框全屏，用于覆盖层定位验收：

```powershell
& '.\tests\render-probes\bin\GameNest.D3D11Probe.exe' --borderless
```
