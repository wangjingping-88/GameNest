@echo off
setlocal
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\Tools\VsDevCmd.bat" -arch=x64 -host_arch=x64 >nul
if errorlevel 1 exit /b 1

set "OUTPUT=%~dp0bin"
if not exist "%OUTPUT%" mkdir "%OUTPUT%"

cl /nologo /std:c++20 /EHsc /O2 /DUNICODE /D_UNICODE "%~dp0D3D11Probe.cpp" /Fe:"%OUTPUT%\GameNest.D3D11Probe.exe" /link /SUBSYSTEM:WINDOWS d3d11.lib dxgi.lib user32.lib
if errorlevel 1 exit /b 2

cl /nologo /std:c++20 /EHsc /O2 /DUNICODE /D_UNICODE "%~dp0D3D12Probe.cpp" /Fe:"%OUTPUT%\GameNest.D3D12Probe.exe" /link /SUBSYSTEM:WINDOWS d3d12.lib dxgi.lib user32.lib
if errorlevel 1 exit /b 3

cl /nologo /std:c++20 /EHsc /O2 /DUNICODE /D_UNICODE "%~dp0OpenGlProbe.cpp" /Fe:"%OUTPUT%\GameNest.OpenGlProbe.exe" /link /SUBSYSTEM:WINDOWS opengl32.lib gdi32.lib user32.lib
if errorlevel 1 exit /b 4

exit /b 0
