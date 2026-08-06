# Phase 4 隔离验收工具

该工具把 `tests/render-probes` 中的可控渲染窗口直接接入正式的窗口定位器、遥测和独立覆盖层，不读取或修改用户游戏库。

构建：

```powershell
& 'D:\Program Files\dotnet\dotnet.exe' build '.\tests\GameNest.Phase4Harness\GameNest.Phase4Harness.csproj' -c Release
```

窗口化验证：

```powershell
& 'D:\Program Files\dotnet\dotnet.exe' `
  '.\tests\GameNest.Phase4Harness\bin\Release\net10.0-windows10.0.19041.0\win-x64\GameNest.Phase4Harness.dll' `
  '.\tests\render-probes\bin\GameNest.D3D11Probe.exe'
```

无边框全屏验证：

```powershell
& 'D:\Program Files\dotnet\dotnet.exe' `
  '.\tests\GameNest.Phase4Harness\bin\Release\net10.0-windows10.0.19041.0\win-x64\GameNest.Phase4Harness.dll' `
  '.\tests\render-probes\bin\GameNest.D3D11Probe.exe' --borderless
```

工具只结束自己创建的测试程序、PresentMon 会话和覆盖层进程；不会结束用户游戏。
