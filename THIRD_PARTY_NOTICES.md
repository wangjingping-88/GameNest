# 第三方软件声明

## Intel PresentMon 2.5.1

GameNest 使用 Intel PresentMon 2.5.1 standalone x64 读取 Windows ETW 呈现事件。便携版将经固定 SHA-256 校验的原始可执行文件放在 `Tools\PresentMon`，未对其进行修改。

- 上游：`https://github.com/GameTechDev/PresentMon`
- 固定版本、哈希与签名：[`docs/third-party/PresentMon-2.5.1.md`](docs/third-party/PresentMon-2.5.1.md)
- MIT License：[`docs/licenses/PresentMon-2.5.1-LICENSE.txt`](docs/licenses/PresentMon-2.5.1-LICENSE.txt)
- 便携包内许可证副本：`LICENSES\PresentMon-2.5.1-LICENSE.txt`

NuGet 包及其传递依赖由各项目的 `packages.lock.json` 锁定。
