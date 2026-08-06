# GameNest：Windows 单机游戏启动器产品与开发方案

> 文档版本：1.2  
> 编写日期：2026-08-04  
> 项目代号：GameNest（暂定，可在开发前统一改名）  
> 已确认视觉方向：B｜Fluent Air（轻透 Fluent，默认浅色主题）  
> 目标读者：产品设计者、Codex Coding、后续维护者

---

## 1. 一句话定义

GameNest 是一款本地优先的 Windows 单机游戏启动器：自动发现散落在不同磁盘和目录中的游戏，把普通快捷方式变成带封面、简介、状态和游玩记录的可视化游戏库，并提供可靠的启动、运行识别、停止和游戏内性能覆盖层。

## 2. 要解决的核心痛点

1. 游戏分散在 C、D、E 等不同盘符和多层目录中，查找成本高。
2. 桌面快捷方式只有名称和小图标，无法直观看出游戏内容。
3. 重装系统、移动目录或盘符改变后，快捷方式容易失效。
4. 游戏启动后缺少统一的运行状态、停止入口和游玩时长记录。
5. 手工整理封面、简介、启动参数和游戏目录重复且麻烦。
6. 进入游戏后想查看帧率、CPU、GPU 和内存占用时，通常还要额外安装并配置其他监控工具。

## 3. 产品目标与边界

### 3.1 首版目标

- 首次打开后，一次选择要扫描的磁盘，自动发现大部分独立 EXE 游戏。
- 自动识别 Steam、Epic、GOG 和现有桌面快捷方式中的可用游戏信息。
- 以封面卡片展示游戏，支持搜索、筛选、收藏和最近游玩。
- 支持一键启动，并准确展示“启动中、运行中、停止中、未运行”状态。
- 停止游戏时优先正常关闭；只有正常关闭失败后才允许用户确认强制结束。
- 游戏运行时提供可开关的轻量覆盖层，显示 FPS、游戏进程 CPU、GPU 和内存占用。
- 自动提取本地图标、文件版本信息和安装位置；简介和封面支持自动补全、手工修改。
- 无网络、无账号、无第三方 API Key 时仍能完整使用本地启动功能。

### 3.2 首版明确不做

- 不负责游戏下载、购买、更新和破解。
- 不做侵入式覆盖层技术，包括 DLL 注入、图形 API Hook、截图注入或修改游戏进程内存；覆盖层仅采用外部透明窗口与非注入式采样。
- 不做云存档同步、Mod 管理、串流和多人联机管理。
- 不尝试绕过反作弊、管理员权限或受保护进程。
- 不把 Microsoft Store / Xbox Game Pass 应用作为首版重点；后续以独立适配器支持。
- 不默认上传用户的游戏目录、游戏列表或游玩记录。

## 4. 用户使用流程

### 4.1 首次使用

1. 启动应用，自动列出当前可用固定磁盘。
2. 默认勾选非系统盘；系统盘只扫描常见游戏平台目录和用户主动选择的目录。
3. 用户选择“快速扫描”或“深度扫描”：
   - 快速扫描：游戏平台清单、桌面快捷方式、常见游戏目录。
   - 深度扫描：在所选磁盘中递归寻找候选 EXE，并用评分模型过滤。
4. 扫描过程持续显示当前磁盘、已检查目录数、发现候选数、耗时和取消按钮。
5. 高置信度游戏自动加入；中置信度候选进入确认页；低置信度候选默认忽略。
6. 进入游戏库，用户可立即启动游戏，也可继续补充封面和简介。

### 4.2 日常使用

- 打开应用后默认进入“游戏库”。
- 双击卡片或点击主按钮启动。
- Ctrl+K 打开快速搜索，输入游戏名后回车启动。
- 正在运行的游戏卡片显示绿色状态点和本次运行时长。
- 详情页的“启动”按钮在运行时变为“停止”。
- 默认随游戏显示性能覆盖层，可用 Ctrl+Shift+F12 随时显示或隐藏。
- 新接入或重新上线的磁盘自动恢复对应游戏，不把离线磁盘上的游戏误删。

## 5. 功能范围

| 模块 | MVP 必须具备 | 后续增强 |
| --- | --- | --- |
| 游戏库 | 卡片视图、列表视图、搜索、收藏、最近游玩、隐藏 | 自定义合集、智能标签 |
| 自动扫描 | 固定磁盘、指定目录、平台清单、EXE 评分、增量扫描 | MFT/USN Journal 高速索引 |
| 启动 | EXE、快捷方式、URI、自定义参数、工作目录 | 启动前后脚本、模拟器配置 |
| 停止 | 正常关闭、超时提示、确认后结束进程树 | 每游戏自定义优雅退出策略 |
| 性能覆盖层 | FPS、游戏 CPU、GPU、内存；位置、透明度、快捷键 | 帧时间曲线、温度、功耗、1% Low |
| 元数据 | 本地图标、文件信息、手工简介与图片 | 可插拔在线元数据提供者 |
| 状态 | 启动中、运行中、停止中、离线、路径失效 | 崩溃识别和健康提示 |
| 记录 | 最近启动、累计时长、单次会话 | 成就、统计图、年度回顾 |
| 设置 | 主题、扫描根目录、排除项、缓存、开机启动 | 多设备同步 |

## 6. 界面与交互设计

### 6.1 已确认视觉方向：B｜Fluent Air

B 方案作为首版正式视觉基线，默认使用轻透、明亮、信息直观的 Windows Fluent 风格。

![GameNest B 方案：Fluent Air 浅色主题预览](GameNest_Fluent_Air_UI.png)

设计落地原则：

- 默认浅色主题，画布使用带轻微暖度的浅灰，而不是纯白铺满。
- 左侧固定导航，中央为 Hero 和游戏库，右侧在有运行游戏时展示会话状态。
- 顶部搜索框保持突出，搜索与快速启动是最高频入口。
- 封面卡片使用 2:3 比例，卡片信息保持克制，只显示标题、最近状态、盘符和更多菜单。
- 使用细边框、轻阴影和少量半透明层级，不大面积使用高模糊玻璃效果。
- 性能覆盖层使用深色悬浮条，与浅色主界面形成稳定对比。
- 蓝色是唯一主强调色；绿色只表达运行正常，橙色只用于性能警示，红色只用于危险操作。
- 预览图是布局与视觉层级基准，不把图中的虚构游戏名、封面或人物素材作为正式资源。

首版基础色建议：

| 用途 | 色值 |
| --- | --- |
| 应用背景 | #F5F7FA |
| 主内容表面 | #FFFFFF |
| 次级表面 | #F8FAFC |
| 边框 | #E4E9F0 |
| 主文字 | #18212F |
| 次级文字 | #667085 |
| 主强调蓝 | #2688F4 |
| 悬停浅蓝 | #EAF3FF |
| 运行绿色 | #22A06B |
| 警示橙色 | #F59E0B |
| 危险红色 | #E5484D |
| 覆盖层背景 | #171A1F，约 88% 不透明度 |

### 6.2 信息架构

左侧导航保持精简：

- 首页
- 游戏库
- 收藏
- 最近游玩
- 正在运行
- 扫描与导入
- 设置

顶部区域：

- 全局搜索框
- 当前扫描状态
- 主题切换
- 窗口控制

### 6.3 首页

- 顶部为最近一次游玩的横向 Hero 区，展示背景图、游戏名、简短介绍和主启动按钮。
- 下方依次展示“继续游玩”“最近添加”“收藏”。
- 没有背景图时，使用封面主色生成柔和渐变，不显示低质量拉伸图。

### 6.4 游戏库

- 默认使用 2:3 比例封面卡片。
- 卡片内容仅保留：封面、标题、运行状态、收藏标记。
- 鼠标悬停后显示启动按钮和更多菜单。
- 支持按名称、最近游玩、累计时长、加入日期排序。
- 支持按盘符、来源、可用状态和自定义标签筛选。
- 使用虚拟化布局，数百至上千个游戏时不一次性创建全部卡片控件。

### 6.5 游戏详情页

详情页包含：

- 横向背景图和纵向封面。
- 游戏名称、一句话简介、标签、来源。
- 大号“启动”或“停止”按钮。
- 最近游玩、累计时长、安装位置。
- 性能覆盖层开关，以及该游戏是否覆盖全局设置。
- 可折叠的高级启动配置：EXE、参数、工作目录、管理员模式、匹配进程名。
- 编辑元数据、打开游戏目录、重新匹配运行进程、从库中移除。

### 6.6 扫描结果确认页

- 以“确定是游戏”“可能是游戏”“已忽略”三组展示。
- 每条显示候选名称、EXE 路径、图标、置信度和命中依据。
- 同一目录出现多个 EXE 时合并为一个游戏，并允许用户选择主程序。
- 支持批量确认、排除整个目录和撤销操作。

### 6.7 视觉规范

- 整体风格：Windows 11 Fluent Air，明亮、轻透、清晰，不堆叠过多玻璃效果。
- 默认主题：浅色，同时提供深色和跟随系统；首次启动默认浅色。
- 深色主题作为完整可用的次主题，不能只是简单反色。
- 浅色主题使用原始横向 Logo；深色主题使用透明底冷灰字标版本，保留蓝青色品牌图形且不增加发光效果；跟随系统时随当前应用模式即时切换。
- 运行状态：#44D19D；警告：#F6B94A；危险操作：#FF5F6D。
- 圆角：窗口内容 12px，卡片 10px，按钮 8px。
- 间距采用 4px 基础网格，常用间距 8、12、16、24、32px。
- 动画时长控制在 120 至 200ms；扫描和启动不得被装饰动画阻塞。
- 默认及最小窗口为 1680×1000；支持 100% 至 300% DPI。系统工作区或高 DPI 强制压缩时，局部内容允许滚动作为可访问性降级。
- 所有状态不能只依赖颜色表达，需同时提供图标或文字。
- 设置页使用“二级分类目录 + 单一详情面板”，内容最大宽度约 1460 DIP 并保持左上对齐；全屏时不等比放大卡片、字体、按钮或内边距，不使用比例行高制造空白。

### 6.8 覆盖层视觉与交互

覆盖层默认是一条紧凑的横向深色悬浮条：

    FPS 118  |  CPU 34%  |  GPU 87%  |  RAM 6.8 GB

- 默认放在游戏窗口右上角，支持左上、左下、右下。
- 默认仅一行；小分辨率或竖屏游戏可切换为两行。
- 数字使用等宽字体，标签使用普通 UI 字体。
- FPS 为绿色；CPU 为蓝色；GPU 为橙色；RAM 为紫色。颜色可关闭。
- 支持 75%、100%、125%、150% 四档缩放。
- 支持 50% 至 95% 背景不透明度。
- 默认完全鼠标穿透，不抢焦点，不出现在 Alt+Tab 和任务栏。
- Ctrl+Shift+F12 显示或隐藏；快捷键可修改并检测冲突。
- 设置页提供实时预览，即使没有启动游戏也能调整位置、字号和透明度。

## 7. 自动扫描设计

### 7.1 扫描优先级

按成本从低到高依次执行：

1. 读取游戏平台安装清单。
2. 导入桌面和开始菜单中的有效 .lnk 快捷方式。
3. 扫描用户配置的常见游戏目录。
4. 对用户选择的磁盘执行通用深度扫描。

游戏平台适配器和通用扫描器必须实现同一接口，扫描结果统一进入候选归并与评分流程。

### 7.2 首版平台适配器

- Steam：读取 libraryfolders.vdf 和 appmanifest_*.acf，获取安装目录、AppId 和名称。
- Epic：读取本机安装清单，解析安装位置和启动信息。
- GOG：读取本机可用的安装信息和卸载注册表项。
- Windows 快捷方式：解析目标、参数、工作目录和图标位置。
- 独立游戏：通用目录扫描与特征评分。

适配器读取失败必须降级，不得阻断其他来源的扫描。

### 7.3 通用 EXE 候选评分

建议初始评分如下，后续根据真实样本调整：

| 特征 | 分值 |
| --- | ---: |
| 来自已识别的平台清单 | +100 |
| 同目录存在 steam_api.dll 或 steam_api64.dll | +35 |
| 存在 UnityPlayer.dll、Unreal 引擎目录、pak/data 等游戏特征 | +25 |
| EXE 文件版本包含有效 ProductName 或 FileDescription | +15 |
| 同目录存在明显封面、背景图或游戏数据目录 | +10 |
| 路径或目录名包含 Games、Game、游戏等用户游戏目录特征 | +10 |
| 文件名包含 uninstall、unins、setup、crash、report、updater | -80 |
| 位于 Windows、驱动、系统恢复、回收站等系统目录 | -100 |
| 文件名为常见运行库、配置器或辅助工具 | -40 |
| 文件小于 256KB 且无其他游戏特征 | -20 |

评分区间：

- 70 分及以上：高置信度，默认加入。
- 40 至 69 分：中置信度，进入确认页。
- 40 分以下：默认忽略，但保留在本次扫描日志中供排查。

注意：分值不是安全判断。任何程序在真正启动前都必须再次验证文件存在，且只能启动用户确认进入游戏库的配置。

### 7.4 多 EXE 归并

同一游戏目录可能包含主程序、启动器、配置器、崩溃上报器和卸载器。归并规则：

1. 平台清单指定的 EXE 优先。
2. 其次选择产品名与目录名最接近、评分最高的 EXE。
3. launcher.exe 不能简单排除；如果它是唯一有效入口，则作为默认启动配置。
4. 其余候选保存为备用启动配置，例如“DX11”“DX12”“安全模式”。
5. 置信度接近时不自动决定，交给用户选择。

### 7.5 性能与可靠性

- 扫描在后台线程运行，所有磁盘访问支持取消。
- 使用有界并发，默认同时处理 2 至 4 个目录，避免机械硬盘被随机读取拖慢。
- 只读取目录项、文件大小、修改时间和必要的 PE 版本信息，不对大型游戏文件做全量哈希。
- 用“路径 + 文件大小 + 修改时间”生成轻量指纹，实现增量扫描。
- 跳过重解析点或记录已访问目录，防止符号链接和 Junction 形成循环。
- AccessDenied、路径过长、磁盘断开等错误按目录记录并继续。
- 排除目录可由用户配置，默认排除 Windows、$Recycle.Bin、System Volume Information、开发依赖和常见缓存目录。
- 应用关闭后保留扫描检查点；下次启动只重扫发生变化的根目录。

### 7.6 磁盘身份

不要只保存盘符。为每个扫描根目录同时保存卷标识、当前盘符和相对路径：

- 盘符变化时尝试按卷标识重新绑定。
- 移动硬盘离线时把游戏标记为“磁盘未连接”，不要删除记录。
- 磁盘重新接入时恢复状态并执行增量扫描。

## 8. 启动、运行识别与停止

### 8.1 启动配置

每个游戏至少有一个 LaunchProfile，字段包括：

- ExecutablePath
- Arguments
- WorkingDirectory
- LaunchKind：Executable、Shortcut、Uri
- RunAsAdministrator
- ExpectedProcessNames
- ProcessMatchMode
- GracefulStopTimeoutSeconds

启动前依次检查：

1. 游戏当前是否已运行，防止重复启动。
2. 文件、工作目录或 URI 是否有效。
3. 参数是否来自本地受信配置，正确处理引号和空格。
4. 是否需要管理员权限；默认不提权，只有用户启用后才请求。
5. 写入“启动中”会话，再调用系统启动能力。

### 8.2 运行进程识别

仅记录最初返回的 PID 不够，因为很多 launcher 会启动真正游戏后自行退出。首版采用分层识别：

1. 直接跟踪 Process.Start 返回的 PID。
2. 启动前保存进程快照，启动后在短时间窗口内观察新进程。
3. 优先匹配可执行文件路径位于游戏目录中的新进程。
4. 其次匹配用户确认过的 ExpectedProcessNames。
5. 无法确定时显示“可能正在运行”，不允许直接强制停止；提示用户选择正确进程并保存映射。
6. 应用重启后，按已保存的进程签名重新发现仍在运行的游戏。

访问其他进程路径可能因权限失败；单个进程读取失败应忽略并记录调试日志。

### 8.3 停止策略

停止按钮的语义必须安全、透明：

1. 用户点击“停止”，弹出提示：强制结束可能导致未保存进度丢失。
2. 默认先调用正常窗口关闭，等待 10 秒，可由游戏配置调整。
3. 游戏退出后结束会话并累计游玩时长。
4. 超时后显示“游戏未响应”，提供“继续等待”和“强制结束”。
5. 只有用户再次确认后，才结束已确认的进程树。
6. 如果权限不足，说明原因；不得静默失败或无限重试提权。

.NET 的 Process.Kill(entireProcessTree: true) 可作为最终回退，但其等待状态并不代表所有后代进程都已经完成退出，因此实现后仍需重新扫描相关进程并更新 UI。

首版不默认使用 Windows Job Object 约束所有游戏。部分游戏启动器、反作弊和已处于 Job 中的进程可能不兼容；后续只能作为经过验证的可选策略。

### 8.4 运行状态机

    NotRunning
        -> Launching
        -> Running
        -> StopRequested
        -> Exited

异常分支：

- Launching 超时：FailedToStart
- 主进程消失但发现匹配子进程：保持 Running 并切换跟踪目标
- 进程非正常退出：CrashedOrUnknown
- 磁盘离线：Unavailable

## 9. 游戏内性能覆盖层技术设计

### 9.1 首版范围

首版覆盖层显示以下四项，并明确数据口径：

| 指标 | 首版定义 | 刷新频率 |
| --- | --- | ---: |
| FPS | 目标游戏实际呈现帧的 1 秒滚动值；不可用时显示 -- | 500ms |
| CPU | 已确认的游戏进程组 CPU 占用，按逻辑处理器数量归一化到 0 至 100% | 1000ms |
| GPU | 目标进程对应 GPU Engine 的占用近似值，限制在 0 至 100% | 1000ms |
| RAM | 已确认游戏进程组的私有内存总量，以 MB 或 GB 显示 | 1000ms |

数据不可获取时只把对应指标显示为 --，其他指标继续工作。不得用系统总占用冒充游戏进程占用。

### 9.2 显示能力与兼容边界

首版采用外部覆盖层窗口：

- 正式支持窗口化和无边框全屏游戏。
- 传统独占全屏模式下不保证覆盖层可见；检测到时提示切换为无边框全屏。
- 不注入 DLL，不 Hook DirectX、Vulkan 或 OpenGL，不修改游戏进程内存。
- 覆盖层被游戏遮挡、目标窗口最小化或失去前台焦点时自动隐藏。
- 游戏切换显示器、分辨率或 DPI 后，覆盖层在 1 秒内重新定位。
- HDR、受保护内容和反作弊环境只做兼容性检测，不尝试绕过限制。

### 9.3 FPS 采集

首选使用固定版本的 PresentMon，通过 ETW 非注入式采集目标渲染进程：

1. 只针对 IProcessTracker 已确认的主渲染 PID。
2. 以隐藏子进程运行，输出重定向到标准输出，不生成持续增长的 CSV 文件。
3. 解析逐帧数据，用 1 秒滑动窗口计算 FPS。
4. 进程切换后停止旧会话并绑定新的主渲染 PID。
5. PresentMon 缺失、启动失败、权限不足或目标 API 不支持时返回明确能力状态。
6. 固定依赖版本、校验文件哈希，并在第三方许可证清单中保留其 MIT License。

PresentMon 的 ETW 会话可能需要管理员权限或 Performance Log Users 组权限。主程序默认不提权；设置页提供“覆盖层兼容性检测”，说明 FPS 是否可用以及具体原因。不得静默以管理员身份重启整个启动器。

### 9.4 CPU、GPU 与内存采集

- CPU：以两次 Process.TotalProcessorTime 差值除以墙钟时间和逻辑处理器数，聚合已确认的游戏进程组。
- 内存：优先使用每个进程的私有内存口径，避免把共享 DLL 页面重复当成游戏独占内存；界面统一显示 RAM。
- GPU：优先检测可用的 GPU Process 性能计数器；不可用时枚举带目标 PID 的 GPU Engine 实例，按引擎归类后取最繁忙引擎值。
- 多 GPU 电脑记录 LUID 或适配器标识；首版显示当前主渲染 GPU 的一个百分比。
- 性能计数器实例随进程创建和退出变化，采集器必须定期重新枚举，不能永久缓存实例名。
- 对权限不足、驱动不暴露计数器、硬件加速 GPU 调度造成的偏差，显示“数据不可用”或诊断提示，不伪造数值。

### 9.5 覆盖层窗口

覆盖层由独立的 GameNest.Overlay 进程承载，游戏和主启动器不受覆盖层崩溃影响。

建议使用无边框 Win32 layered window，并应用以下行为：

- Topmost：保持在普通窗口上方。
- ToolWindow：不出现在任务栏和 Alt+Tab。
- NoActivate：显示时不抢游戏焦点。
- 鼠标命中测试返回透明，保证所有鼠标操作穿透到游戏。
- 使用 DWM 扩展边界或客户端区域定位，不把窗口阴影算入游戏内容区域。
- 使用命名管道接收主程序下发的目标窗口、指标值和 OverlayProfile。
- 管道消息有版本号、长度限制和断线重连，拒绝任意命令执行。

覆盖层只按指标刷新频率重绘，不能跟随游戏帧率每帧重绘。

### 9.6 生命周期

1. 游戏进入 Running 且覆盖层启用。
2. 主程序确认主渲染 PID 和游戏窗口。
3. 启动或复用 GameNest.Overlay。
4. 遥测提供者发布 PerformanceSnapshot。
5. 覆盖层定位到目标窗口并显示。
6. 游戏失去前台、最小化或切走时隐藏，但继续低频采样。
7. 游戏退出后停止采集、清空敏感句柄并关闭覆盖层会话。
8. 启动器退出时覆盖层退出，但不得结束游戏。

### 9.7 性能预算

- 覆盖层与遥测总 CPU 额外占用目标低于 2%，以常见 6 核桌面处理器为基准。
- GameNest.Overlay 工作集目标低于 80MB。
- 指标采集不得阻塞 UI 线程或游戏启动流程。
- 标准输出解析使用有界缓冲，消费者落后时丢弃旧帧数据，不无限积压。
- 覆盖层关闭时不启动 FPS ETW 会话，并停止 GPU 性能计数器采样。

## 10. 游戏简介与封面

### 10.1 离线基础能力

无需联网即可生成最小可用信息：

- 标题：平台清单名称，其次 PE ProductName，再次目录名或 EXE 名。
- 图标：提取 EXE 或快捷方式关联图标。
- 简介：允许用户编辑；未获取到内容时显示“尚未添加简介”，不生成虚假描述。
- 封面：优先读取用户指定图片或游戏目录中的明显图片；否则使用图标、标题和主色生成统一占位封面。
- 所有编辑结果保存在本地，重新扫描不得覆盖用户手工修改。

### 10.2 在线增强

通过 IMetadataProvider 接口实现，不把某个第三方服务写死在核心业务中。可选适配器可考虑 IGDB、RAWG 或合法授权的封面服务，但必须遵守各自条款，并由用户自行配置凭据。

匹配流程：

1. 用规范化标题和平台 ID 查询。
2. 计算名称、发行年份、开发商等匹配分。
3. 高置信度自动填充；中置信度让用户选择；低置信度不写入。
4. 展示元数据来源和最后更新时间。
5. 本地手工字段优先级高于在线字段。
6. API Key 使用 Windows Credential Locker 或 DPAPI 保存，不写入 SQLite 明文或日志。

## 11. 推荐技术方案

### 11.1 技术栈

| 层次 | 选择 | 原因 |
| --- | --- | --- |
| 语言与运行时 | C# + .NET 10 | Windows 进程、文件系统和异步任务能力成熟，维护成本低 |
| UI | WinUI 3 + Windows App SDK 2.3.1 Stable | 原生 Windows 11 风格，支持 Fluent、Mica、高 DPI 和现代控件 |
| 架构 | MVVM + 分层服务 | UI 与扫描、进程、存储逻辑可独立测试 |
| MVVM | CommunityToolkit.Mvvm | 轻量、官方生态、减少样板代码 |
| 本地存储 | SQLite + Microsoft.Data.Sqlite | 单文件、无需服务、适合游戏库和会话记录 |
| 依赖注入 | Microsoft.Extensions.DependencyInjection | 便于替换扫描器、元数据源和测试替身 |
| FPS 采集 | PresentMon，固定并审核版本 | 跨 DirectX、OpenGL、Vulkan 的非注入式帧呈现采集 |
| 系统指标 | .NET Process API + Windows PDH | 按游戏进程采集 CPU、GPU 和内存 |
| 覆盖层 | 独立 Win32 透明窗口进程 | 鼠标穿透、不抢焦点、与主界面崩溃隔离 |
| 日志 | Microsoft.Extensions.Logging 抽象 + 本地滚动文件 | 支持排查扫描和启动问题，禁止记录密钥 |
| 测试 | xUnit | 单元测试和 Windows 集成测试 |

版本策略：

- 只使用 Windows App SDK Stable 通道，不使用 Preview 或 Experimental。
- 首次建仓固定 NuGet 版本并提交锁定文件，不在每次构建时自动漂移到最新版。
- 开发阶段使用 unpackaged、self-contained x64 配置，减少目标机器依赖。
- 发布阶段同时评估安装包与便携版；不要为了单文件 EXE 牺牲启动速度和可调试性。

### 11.2 为什么不优先选择 Electron

本项目大量依赖 Windows 文件系统、注册表、快捷方式、进程追踪、窗口关闭和高 DPI 桌面行为。C# + WinUI 3 的调用链更直接，内存占用和原生观感更适合常驻型启动器。若未来需要跨平台，再评估独立前端层，而不是让首版承担跨平台成本。

## 12. 代码架构

建议解决方案结构：

    GameNest.sln
    src/
      GameNest.App/                 WinUI 3 页面、控件、ViewModel、主题
      GameNest.Domain/              Game、LaunchProfile、PlaySession 等纯模型
      GameNest.Application/         用例、接口、状态协调、验证规则
      GameNest.Infrastructure/      SQLite、扫描器、平台适配器、Win32、元数据
      GameNest.Telemetry/           FPS、CPU、GPU、内存采集与能力检测
      GameNest.Overlay/             独立透明覆盖层进程、窗口跟踪和渲染
    tests/
      GameNest.Domain.Tests/
      GameNest.Application.Tests/
      GameNest.Infrastructure.Tests/
      GameNest.Telemetry.Tests/
    docs/
      product-plan.md
      architecture-decisions.md

核心接口建议：

    IGameSourceAdapter
      ScanAsync(ScanContext, CancellationToken)

    IGameCandidateScorer
      Score(GameCandidate)

    IGameLibraryRepository
      GetAllAsync()
      UpsertAsync()
      SetAvailabilityAsync()

    IGameLaunchService
      LaunchAsync(GameId, LaunchProfileId)
      RequestStopAsync(GameId)
      ForceStopAsync(GameId)

    IProcessTracker
      ObserveLaunchAsync(LaunchObservation)
      ReconcileRunningGamesAsync()

    IPerformanceTelemetry
      StartAsync(TelemetryTarget, CancellationToken)
      StopAsync()
      Snapshots

    IFpsTelemetryProvider
      CheckCapabilityAsync()
      CaptureAsync(ProcessId, CancellationToken)

    IGameWindowLocator
      FindPrimaryWindowAsync(ProcessGroup)
      GetContentBoundsAsync(WindowHandle)

    IOverlayController
      ShowAsync(OverlaySession)
      UpdateAsync(PerformanceSnapshot)
      HideAsync()

    IMetadataProvider
      SearchAsync(MetadataQuery)
      GetDetailsAsync(ProviderGameId)

    IAssetCache
      StoreCoverAsync()
      GetOrCreatePlaceholderAsync()

依赖方向必须保持：

- App 依赖 Application 和 Domain。
- Infrastructure 实现 Application 中定义的接口。
- Domain 不依赖 WinUI、SQLite、文件系统或网络。
- ViewModel 不直接访问数据库、注册表和 Process API。
- Overlay 不引用主程序 UI，不读取数据库，只接收经过验证的会话消息。

## 13. 本地数据模型

### 13.1 Game

- Id
- Title
- SortTitle
- Description
- InstallRoot
- SourceType
- SourceGameId
- VolumeIdentity
- IsFavorite
- IsHidden
- Availability
- DetectionConfidence
- UserEditedFields
- DateAddedUtc
- LastPlayedUtc
- TotalPlaySeconds

### 13.2 LaunchProfile

- Id
- GameId
- Name
- LaunchKind
- ExecutablePath
- Arguments
- WorkingDirectory
- RunAsAdministrator
- ExpectedProcessNames
- IsDefault
- GracefulStopTimeoutSeconds

### 13.3 GameAsset

- Id
- GameId
- AssetType：Icon、Cover、Hero、Screenshot
- LocalPath
- Source
- Width
- Height
- ContentHash
- UpdatedAtUtc

### 13.4 ScanRoot

- Id
- VolumeIdentity
- CurrentPath
- RelativePath
- ScanMode
- IsEnabled
- LastScanUtc
- LastCheckpoint

### 13.5 PlaySession

- Id
- GameId
- StartedAtUtc
- EndedAtUtc
- DurationSeconds
- ExitKind
- TrackedProcessIds

### 13.6 OverlayProfile

- Id
- GameId，可为空表示全局默认
- IsEnabled
- Position：TopLeft、TopRight、BottomLeft、BottomRight
- ScalePercent
- BackgroundOpacityPercent
- ShowFps
- ShowCpu
- ShowGpu
- ShowRam
- ToggleHotkey
- HideWhenGameNotForeground
- UpdatedAtUtc

SQLite 启用 WAL 模式。所有结构变更通过显式迁移完成，数据库初始化和迁移必须可重复执行。

## 14. 性能目标

以下指标作为工程验收目标，不作为绝对承诺：

- 200 个游戏的本地库，热启动到可交互不超过 1.5 秒。
- 游戏库滚动以 60 FPS 为目标，不因封面解码阻塞 UI 线程。
- 启动命令发出后 300ms 内更新为“启动中”。
- 进程状态变化在正常情况下 2 秒内反映到 UI。
- 覆盖层在游戏进入 Running 后 3 秒内出现；快捷键切换响应目标低于 150ms。
- FPS 每 500ms 更新，CPU、GPU、RAM 每 1000ms 更新。
- 覆盖层和遥测额外 CPU 占用目标低于 2%，Overlay 工作集目标低于 80MB。
- 深度扫描可随时取消，点击取消后 1 秒内停止派发新目录任务。
- 1000 个游戏时内存长期稳定，目标低于 250MB。
- 图片只按显示尺寸生成缩略图并缓存，不把原图完整常驻内存。
- 数据库、图片缓存和日志均设置可查看、可清理的大小策略。

## 15. 安全、隐私与容错

- 默认普通用户权限运行，不安装自启动后台系统服务。
- 启动和强制停止前验证目标来自本地已确认的 LaunchProfile。
- 不从在线简介或图片元数据中执行命令。
- 所有 SQL 使用参数化查询。
- 扫描器捕获单目录异常，不能因一个受限目录导致整个任务失败。
- 强制停止必须二次确认，并明确提示可能丢失未保存进度。
- 不在日志中写 API Key、完整认证头或用户敏感环境变量。
- 性能遥测只在本机采集当前游戏进程，不是用户行为遥测，不上传任何指标。
- 覆盖层可全局关闭或按游戏关闭；关闭后停止对应采集会话。
- 不向游戏注入 DLL，不 Hook 图形 API，不绕过反作弊或受保护内容。
- PresentMon 等第三方二进制必须固定版本、校验哈希并保留许可证。
- 需要额外权限时必须说明用途并由用户主动选择，不能静默提权。
- 游戏目录失效、盘符改变或磁盘离线时保留用户编辑过的元数据。
- 数据库损坏时先备份原文件，再尝试恢复；不得直接覆盖。

## 16. 开发阶段与交付顺序

### Phase 0：工程骨架

交付：

- 建立分层解决方案、Telemetry、Overlay 和测试项目。
- 按 B｜Fluent Air 预览建立 WinUI 3 主窗口、导航、主题令牌和依赖注入。
- SQLite 初始化、迁移和日志。
- CI 至少执行 restore、build、test。

验收：

- 新环境按 README 可构建运行。
- 默认浅色，并可切换深色、跟随系统；浅色界面与 B 方案的布局层级一致。
- 无业务数据时有完整空状态，不出现临时占位文本。

### Phase 1：最小可用游戏库

交付：

- 手工添加 EXE 或快捷方式。
- 游戏卡片、详情页、搜索、收藏和本地编辑。
- 图标提取、占位封面、启动和直接 PID 跟踪。

验收：

- 能添加、编辑、移除和重新启动至少 20 个不同路径的测试游戏。
- 路径包含中文、空格和特殊字符时仍可启动。
- 用户编辑内容重启应用后保留。

### Phase 2：自动扫描

交付：

- ScanRoot 管理、快速扫描和深度扫描。
- Steam、快捷方式和通用 EXE 适配器。
- 候选评分、目录归并、确认页、排除规则和增量指纹。
- 磁盘离线与重新绑定。

验收：

- 测试样本中高置信度候选无卸载器、崩溃上报器等明显误报。
- 扫描可暂停或取消，UI 始终可操作。
- 无权限目录、符号链接循环和磁盘中途拔出不会使应用崩溃。

### Phase 3：可靠的运行与停止

交付：

- 启动前后进程快照。
- launcher 退出后的真实游戏进程接管。
- 正常关闭、等待、二次确认强制结束。
- 会话和累计时长。

验收：

- 直接 EXE、父进程派生子进程、启动器立即退出三类测试均能正确更新状态。
- 未确认正确进程时，不显示可直接强制停止的危险操作。
- 启动器自身退出不会结束正在运行的游戏。

### Phase 4：性能遥测与游戏内覆盖层

交付：

- GameNest.Telemetry 与 GameNest.Overlay 独立项目。
- FPS、游戏进程 CPU、GPU、RAM 四项采集。
- 覆盖层透明窗口、鼠标穿透、目标窗口定位和全局快捷键。
- 全局 OverlayProfile、按游戏覆盖配置、设置页实时预览。
- 权限与兼容性检测，以及指标不可用时的降级状态。

验收：

- DirectX 11、DirectX 12 以及至少一种 Vulkan 或 OpenGL 测试程序能获得 FPS。
- 窗口化和无边框全屏下覆盖层可见、位置正确且不抢焦点。
- 独占全屏无法显示时给出明确说明，不尝试 DLL 注入。
- FPS、CPU、GPU、RAM 任一采集失败时，其他指标仍正常显示。
- 目标游戏切换分辨率、DPI 或显示器后 1 秒内重新定位。
- 覆盖层与采集总 CPU 和内存达到性能预算，游戏退出后无残留会话。

### Phase 5：元数据与视觉完善

交付：

- 统一图片缓存和缩略图。
- 可插拔元数据接口。
- 至少一个用户可选的在线提供者，或完整的手工导入流程。
- 按 B｜Fluent Air 完善 Hero 区、右侧运行面板、骨架屏、错误状态和可访问性。

验收：

- 无网和 API 失败时不影响游戏库及启动。
- 在线匹配错误可以撤销，手工字段不会被覆盖。
- 1000 条模拟数据的滚动和搜索性能达到目标。

### Phase 6：发布

交付：

- x64 便携版或安装包。
- 自动备份、缓存清理、诊断信息导出。
- 用户 README、覆盖层兼容性说明、隐私说明、第三方许可证清单。

验收：

- 在干净的 Windows 测试环境可安装、启动、扫描、卸载。
- 卸载前明确是否保留数据库和封面缓存。
- Release 构建无调试密钥、测试 API Key 和绝对开发机路径。

### Phase 7：GitHub Release 在线升级

交付：

- 固定从公开仓库 `wangjingping-88/GameNest` 的最新正式 GitHub Release 检查 GameNest 自身更新；不负责游戏下载或游戏更新。
- 启动后后台检查、每 24 小时最多一次，设置页支持手动检查和关闭自动检查；离线、超时、404 或限流不阻断本地游戏库。
- Release 使用 `vMAJOR.MINOR.PATCH` 标签和固定命名的 ZIP、SHA-256、更新清单、ECDSA P-256 签名四项资产。
- 客户端先验证内置受信公钥对原始清单字节的签名，再验证包大小、SHA-256、下载域名和 ZIP 路径；没有生产公钥时只允许检查和打开下载页。
- 更新前创建数据库备份，在同一磁盘暂存候选目录；旧主程序和 Overlay 正常退出后交换目录，新版隐藏初始化并确认健康，失败时恢复旧目录和数据库。
- 普通权限无法写入便携目录时不提权、不强制结束进程，改为打开 GitHub 下载页。
- tag 触发的发布工作流固定 .NET 10.0.302 和 PresentMon 2.5.1，缺少签名 Secret 时直接失败。

验收：

- 版本比较、稳定通道、24 小时缓存、ETag、离线、限流、资产缺失、错误签名、错误哈希、超大包和恶意 ZIP 均有自动化测试。
- 临时便携目录覆盖成功交换、旧进程未退出、健康检查失败和数据库回滚；不使用进程强杀或默认提权。
- `0.2.1` 是更新能力的 bootstrap 版本，`0.1.0` 必须手动安装；`v0.2.0` 因 SQLite 依赖安全告警被 CI 门禁拒绝，未创建 Release；配置受信生产公钥后，第一个真实在线升级路径为 `0.2.1 → 0.2.2`。
- 独立更新签名不等于 Authenticode；未采购代码签名证书前仍明确保留 SmartScreen 风险。

## 17. 测试计划

### 17.1 单元测试

- 候选评分的正向和负向样本。
- 多 EXE 归并与主程序选择。
- 游戏名规范化和元数据匹配。
- 盘符变化后的路径重新绑定。
- 状态机的合法和非法转换。
- 会话时长计算与异常退出。
- FPS 滑动窗口、CPU 归一化、GPU 聚合和内存单位转换。
- OverlayProfile 合并规则、快捷键冲突和指标不可用状态。

### 17.2 集成测试

- 使用测试用小型 EXE 模拟正常退出、无响应和派生子进程。
- 临时目录模拟深层路径、中文路径、拒绝访问和重解析点。
- SQLite 首次初始化、版本迁移、并发读取和异常恢复。
- Steam/快捷方式适配器使用固定样本文件，不依赖开发机真实游戏库。
- 使用可控渲染测试程序验证 FPS 管道启动、输出解析、取消和异常退出。
- 使用父子进程组验证 CPU 与 RAM 聚合，验证进程切换后遥测重新绑定。
- 覆盖层进程崩溃或命名管道断开时，游戏和主启动器保持运行。

### 17.3 UI 验收

- 100%、150%、200%、300% 缩放。
- 1920×1080、2560×1440、3840×2160。
- 键盘完整操作、焦点可见、屏幕阅读器标签。
- 长标题、无封面、无简介、磁盘离线、扫描失败等边界状态。
- 对照 B｜Fluent Air 预览检查导航、Hero、运行面板、卡片网格和浅色层级。
- 覆盖层四角位置、四档缩放、透明度、隐藏快捷键和多显示器定位。

## 18. Definition of Done

每个阶段完成前必须同时满足：

1. dotnet build Release 成功且无新增警告。
2. dotnet test 全部通过。
3. 新增公共服务具有单元测试或说明无法测试的原因。
4. 所有耗时 I/O 均不在 UI 线程同步执行。
5. CancellationToken 能从页面命令传递到扫描或网络调用。
6. 错误对用户可理解，对日志可诊断。
7. 不把本机绝对路径、密钥和真实游戏清单提交到仓库。
8. README 更新到可以让下一位开发者复现。
9. 不用临时假数据冒充已实现功能。
10. 完成对应阶段的人工验收清单并记录结果。
11. 覆盖层相关变更不得引入 DLL 注入、图形 API Hook 或默认提权。
12. 所有第三方二进制记录版本、来源、哈希和许可证。

## 19. 交给 Codex Coding 的开工指令

把本文件放到仓库 docs/product-plan.md，把 GameNest_Fluent_Air_UI.png 放到 docs/GameNest_Fluent_Air_UI.png，然后向 Codex Coding 发送：

    阅读 docs/product-plan.md，并将它视为当前项目的产品和技术约束。

    先只实现 Phase 0，不要提前实现扫描、在线元数据、进程强杀或性能覆盖层。
    在编码前：
    1. 检查当前仓库结构和 AGENTS.md。
    2. 给出本阶段将创建或修改的文件清单。
    3. 明确 Windows App SDK、.NET 和 NuGet 的固定版本。
    4. 如果文档与现有代码冲突，先说明冲突及建议，不要静默重写。

    实现要求：
    - 使用 C#、.NET 10、WinUI 3、MVVM 和分层架构。
    - 以 docs/GameNest_Fluent_Air_UI.png 作为 B｜Fluent Air 视觉基线。
    - 核心业务不能直接写在 XAML code-behind。
    - 所有本地 I/O 必须异步或放在后台执行。
    - 保留现有用户修改，不使用破坏性 Git 命令。
    - 完成后运行 Release build 和全部测试。

    最终回复必须包含：
    - 已完成内容；
    - 关键设计决定；
    - 构建与测试结果；
    - 尚未完成或有风险的内容；
    - 下一阶段建议，但不要自动开始下一阶段。

完成 Phase 0 后，再逐阶段发送：

    继续实现 docs/product-plan.md 中的 Phase N。
    只在当前阶段范围内修改；先检查上一阶段的实现和测试，再给出计划。
    完成后按 Definition of Done 验证并报告结果。

## 20. 关键风险与应对

| 风险 | 影响 | 应对 |
| --- | --- | --- |
| 通用 EXE 扫描误报 | 游戏库混入卸载器和工具 | 评分、目录归并、确认页、可解释命中依据 |
| launcher 与真实游戏进程分离 | 状态错误、停止失败 | 启动前后快照、路径匹配、用户确认的进程签名 |
| 强制结束导致存档损坏 | 严重用户体验问题 | 正常关闭优先、等待、二次确认、明确警告 |
| 管理员或反作弊进程不可访问 | 无法读路径或停止 | 普通权限优先、透明提示、不绕过保护 |
| 在线数据匹配错误 | 封面和简介张冠李戴 | 置信度阈值、来源显示、撤销、手工字段优先 |
| 深度扫描拖慢磁盘 | 应用卡顿、用户取消 | 有界并发、增量指纹、排除项、可取消 |
| 移动硬盘盘符改变 | 路径失效 | 保存卷标识与相对路径，自动重新绑定 |
| WinUI 版本漂移 | 构建不稳定 | Stable 通道、固定包版本、提交锁定文件 |
| 独占全屏遮挡外部覆盖层 | 用户看不到指标 | 首版明确支持窗口化和无边框；检测并提示，不转向注入 |
| FPS ETW 权限不足 | FPS 显示 -- | 启动前能力检测、明确诊断、主程序不静默提权 |
| GPU 驱动不暴露计数器 | GPU 数值缺失或偏差 | 运行时探测多种计数器，注明近似口径并允许单项降级 |
| 遥测影响游戏性能 | 失去轻量启动器定位 | 独立进程、有界缓冲、低频重绘、性能预算和基准测试 |
| 覆盖层抢焦点或拦截输入 | 严重影响游戏操作 | NoActivate、鼠标穿透、自动化与人工输入测试 |

## 21. 后续可扩展方向

- Xbox 手柄全屏“大屏模式”。
- 模拟器与 ROM 库。
- 自定义合集、标签和智能筛选。
- 启动前切换电源计划、显示器、音频设备，退出后恢复。
- 游戏存档本地备份。
- SteamGrid 风格封面批量匹配。
- 游戏时长和最近游玩统计。
- 帧时间曲线、1% Low、0.1% Low、GPU 温度、功耗和显存占用。
- 局域网内多台电脑游戏库汇总。
- 插件式游戏平台适配器。

## 22. 官方技术参考

- [Windows App SDK Stable release channels](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels)
- [Windows App SDK deployment overview](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/deploy-overview)
- [Unpackaged WinUI 3 deployment](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app)
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)
- [Microsoft.Data.Sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/)
- [ItemsRepeater virtualization](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/items-repeater)
- [System.Diagnostics.Process.Kill](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill?view=net-10.0)
- [Windows Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)
- [PresentMon official repository and license](https://github.com/GameTechDev/PresentMon)
- [PresentMon console output and process targeting](https://github.com/GameTechDev/PresentMon/blob/main/README-ConsoleApplication.md)
- [Windows extended window styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)
- [DWM window attributes](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)
- [Collecting performance counter data](https://learn.microsoft.com/en-us/windows/win32/perfctrs/collecting-performance-data)

---

## 最终产品判断

GameNest 的首版视觉方向已确定为 B｜Fluent Air：以默认浅色、清晰层级和低学习成本建立产品辨识度。工程上最难的部分仍是减少扫描误报、在 launcher 派生真实游戏进程后保持正确状态，以及用非注入方式稳定呈现性能覆盖层。开发顺序必须先完成游戏库和进程跟踪，再接入遥测与覆盖层，最后补充在线元数据。这样即使第三方数据服务或部分性能计数器不可用，核心启动体验仍然可靠。
