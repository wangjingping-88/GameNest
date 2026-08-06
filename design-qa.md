# GameNest Phase 0 视觉验收

## 验收范围

- 产品与技术约束：`docs/product-plan.md`
- 视觉基线：`docs/GameNest_Fluent_Air_UI.png`
- 当前阶段：Phase 0
- 明确不在本轮实现：磁盘扫描、在线元数据、进程强杀、性能覆盖层
- 用户视觉上下文：未发现已保存的 product-design 用户偏好，本轮以仓库基线图和用户提供的实际窗口截图为准。

## 对照证据

### 视觉基线

- 文件：`docs/GameNest_Fluent_Air_UI.png`
- 尺寸：1680 × 945 px
- 状态：已有游戏、存在运行中会话的完整产品态

### 实现截图

- `artifacts/visual-audit/2026-08-04/03-home-pass2.png`
  - 尺寸：2048 × 1120 px
  - 状态：Phase 0 首页空状态
- `artifacts/visual-audit/2026-08-04/04-library-generic-pass2.png` 至 `09-settings-pass2.png`
  - 尺寸：2048 × 1120 px
  - 状态：用户提供的第二轮全页面截图；用于定位通用页窄柱和设置页过度拉伸问题
- `artifacts/visual-audit/2026-08-04/13-library-pass4-final.png`
  - 尺寸：2578 × 1408 px（Windows 物理像素）
  - 状态：最终游戏库空状态
- `artifacts/visual-audit/2026-08-04/14-settings-pass4-final.png`
  - 尺寸：2578 × 1408 px（Windows 物理像素）
  - 状态：最终设置页、跟随系统主题
- `artifacts/visual-audit/2026-08-04/15-window-minimum-reference.png`
  - 尺寸：1688 × 997 px
  - 状态：用户指定的窗口缩小下限与搜索框对齐参考
- `artifacts/visual-audit/2026-08-04/16-generic-centering-annotation.png`
  - 尺寸：1688 × 997 px
  - 状态：用户标注的通用空状态居中目标
- `artifacts/visual-audit/2026-08-04/17-home-window-minimum-final.png`
  - 尺寸：1680 × 1000 px（Windows 物理像素）
  - 状态：最终默认/最小尺寸下的 Phase 0 首页
- `artifacts/visual-audit/2026-08-04/18-library-centered-final.png`
  - 尺寸：1680 × 1000 px（Windows 物理像素）
  - 状态：最终默认/最小尺寸下的游戏库空状态
- `artifacts/visual-audit/2026-08-04/19-library-vertical-position-audit.png`
  - 尺寸：1680 × 1000 px（Windows 物理像素）
  - 状态：通用空状态仅完成水平居中时的垂直位置审查
- `artifacts/visual-audit/2026-08-04/20-library-horizontal-vertical-centered-final.png`
  - 尺寸：1680 × 1000 px（Windows 物理像素）
  - 状态：通用空状态在标题下方剩余内容区双向居中，并向上补偿 16 DIP
- `artifacts/visual-audit/2026-08-04/21-settings-regression-final.png`
  - 尺寸：1680 × 1000 px（Windows 物理像素）
  - 状态：设置页布局回归检查

基线图与实现图的数据状态不同，因此不进行像素级内容一致性判断；本轮重点比较导航、留白、层级、圆角、边界、主题色和内容密度。Phase 0 主动使用空状态，属于阶段约束下的预期差异。Pass 5 的用户参考与实现均为完整窗口物理像素截图，分别为 1688 × 997 和取整后的 1680 × 1000；未做密度重采样，以窗口比例和可见布局为比较依据。

## 全屏与重点区域比较

### 全屏结构

- 左侧导航宽度稳定，品牌、导航和本地优先说明形成三段结构。
- 顶部搜索框保留基线中的全局入口位置，但在 Phase 0 明确标注“添加后可用”。
- 首页使用宽幅浅蓝英雄区和大面积游戏库空状态，延续 Fluent Air 的低噪声、轻边界和宽松留白。
- 通用页面内容卡片限制在 960 DIP 内，避免早期版本的窄长白柱，也避免空状态无意义铺满整屏。
- 通用页面的 960 × 390 DIP 空状态卡片在内容区水平居中，收藏、最近游玩、运行中和扫描页复用相同布局。
- 通用空状态卡片在页面标题下方的剩余内容区垂直居中，并向上补偿 16 DIP；标题区仍保持顶部稳定。
- 设置页内容限制在 1180 DIP 内，采用 3:2 双栏；两栏高度由内容决定，不再拉伸到窗口底部。
- 默认窗口和原生缩小下限统一为 1680 × 1000；在该尺寸下首页、通用页和设置页均不显示滚动条。

### 重点区域

- 导航区：最终截图中选中项的图标与文字均使用强调色，未选中项保持中性灰，符合基线的状态表达。
- 通用空状态区：卡片约 390 DIP 高，图标、标题和说明在视觉中心；收藏、最近游玩、运行中和扫描页使用各自语义图标。
- 搜索区：Phase 0 使用边框、图标和文本组成的静态搜索外观，图标与文字均由同一网格垂直居中，避免只读 TextBox 模板导致的占位文字偏移。
- 设置区：主题选项、隐私说明和准备状态在首屏形成紧凑信息组；不存在大块卡片内空白。
- 颜色与边界：浅灰画布、白色表面、低对比边框和蓝色强调色与 B｜Fluent Air 基线一致。

重点区域无需额外裁剪：1680 × 1000 原始截图中搜索文字、图标、卡片边界和空状态文本均可清晰辨认；已在同一视觉输入中同时打开用户参考图与实现图进行比较。

## 发现与修复记录

| 轮次 | 严重级别 | 发现 | 处理结果 |
| --- | --- | --- | --- |
| Pass 1 | P1 | 首页标题、搜索入口和空状态层级不稳定 | 重构首页英雄区、搜索框和空状态信息层级 |
| Pass 2 | P1 | 五个通用页面根容器按内容收缩，形成约 310 px 的窄长白柱 | 页面根容器改为全宽参与测量，内容卡片独立限宽并固定合理空状态高度 |
| Pass 2 | P1 | 设置页左卡片被星号行拉伸到整屏高度 | 设置页内容行改为自动高度，双栏容器独立限宽 |
| Pass 3 | P2 | 所有通用空状态均使用游戏手柄图标 | 由 ViewModel 提供页面语义图标并绑定到空状态 |
| Pass 3 | P2 | 选中导航文字变蓝而图标仍为灰色 | 移除图标的显式灰色覆盖，使其继承 ListView 选中前景色 |
| Pass 4 | — | 游戏库与设置页最终全屏复核 | 未发现阻断 Phase 0 的视觉问题 |
| Pass 5 | P1 | 窗口可以缩到布局出现滚动条，且默认尺寸没有作为缩小下限 | 默认尺寸取整为 1680 × 1000，并通过 `OverlappedPresenter` 设置相同原生最小尺寸；向 1200 × 700 发起缩小请求后窗口仍保持 1680 × 1000 |
| Pass 5 | P2 | 搜索占位文字受 TextBox 模板影响，垂直位置不稳定 | Phase 0 搜索外观改为静态边框网格，图标和文字统一垂直居中 |
| Pass 5 | P2 | 游戏库至扫描页面的空状态卡片仍偏左 | 保持 960 × 390 DIP 尺寸并改为内容区水平居中；最终证据见 `18-library-centered-final.png` |
| Pass 6 | P2 | 卡片完成水平居中后仍紧贴标题区，顶部与底部留白明显失衡 | 通用页主体行改为占用剩余空间，卡片在其中垂直居中并向上补偿 16 DIP；前后证据见 `19-library-vertical-position-audit.png` 与 `20-library-horizontal-vertical-centered-final.png` |
| Pass 6 | — | 设置页可能被相似行定义误改 | 设置页恢复自动内容高度并完成实机回归，证据见 `21-settings-regression-final.png` |

## 剩余风险

- 1680 × 1000 的原生最小尺寸会使应用无法完整显示在可用工作区小于该尺寸的显示器上；这是本轮按用户指定视觉下限形成的明确取舍。
- 高对比度主题尚未形成截图矩阵。
- 基线中的游戏封面、运行中会话、性能信息和扫描交互属于后续阶段，本轮没有用占位业务逻辑提前实现。

## 最终结果

passed

## Phase 1 增量视觉验收

### 实机发现

| 轮次 | 严重级别 | 发现 | 处理结果 |
| --- | --- | --- | --- |
| Pass 7 | P1 | Phase 1 把静态搜索外观替换为真实 `TextBox` 后，系统模板重新引入偏上的文字基线和焦点蓝色下划线 | 使用独立搜索描边令牌，透明化输入控件内部背景/边界，固定 38 DIP 高度并校正不对称垂直 Padding |
| Pass 7 | P2 | 搜索框白色表面与浅灰画布边界不够明确 | 浅色/深色/高对比度主题分别定义 `GameNestSearchBorderBrush`，外层圆角边界始终可辨识 |
| Pass 7 | P1 | 添加游戏约等待 2 秒才显示 | 游戏记录先入库并立即返回 UI；本地图标提取改为后台补齐，窄更新 `GameAssets` 防止覆盖并发用户编辑 |
| Pass 8 | P1 | 非最大化窗口下首页游戏库空状态底部被裁切，全屏正常 | Hero 从 320 压缩为 280 DIP，收紧页面 Padding、间距与空状态组件，底部内容区使用剩余高度且不启用滚动条 |
| Pass 8 | P2 | 快捷退出进程可能在启动返回前释放 `Process` | 注册退出回调前缓存 PID，并容忍退出回调已经完成释放；该项为功能回归，不改变视觉 |

### 当前证据与状态

- 参考：`docs/GameNest_Fluent_Air_UI.png`；
- 用户实机证据：2026-08-04 的 Phase 1 首页截图，包含搜索框、添加按钮和非最大化窗口裁切标注；
- 修正版预览：`artifacts/phase1-responsive-validation/GameNest.App.exe`；
- 编译验证：修正版 XAML Release 构建 0 警告、0 错误；
- 待确认：用户在相同 DPI 与非最大化窗口下复核搜索文字垂直居中、首页底部完整显示和添加体感。

### Pass 9：搜索输入模板回归

- 源视觉真值：`C:\Users\JPWANG~1.UCC\AppData\Local\Temp\codex-clipboard-b1e66ef4-d58d-4c0a-bb76-e31d74d4b026.png`；
- 实现截图：`artifacts/visual-audit/2026-08-04/22-phase1-search-template.png`；
- 聚焦对比：`artifacts/visual-audit/2026-08-04/23-phase1-search-comparison.png`；
- 源图：1662×990 px；实现图：1344×800 px；系统窗口截图，未做密度重采样；
- 对比区域：两图均裁取搜索框所在的 `x=330, y=0, width=820, height=80` 区域，并按原始像素上下排列；
- 状态：浅色主题、首页空状态、搜索框未输入且已获得焦点；
- 完整视图：实现仍保持 B｜Fluent Air 的导航、Hero 和游戏库层级，本轮没有改动其他区域；
- 聚焦视图：实现中的搜索图标、占位文字和 40 DIP 圆角轮廓上下留白对称，文字视觉中心与搜索框中心一致；默认模板的焦点下划线不再出现；
- 字体与排版：Segoe UI Variable Text、14 DIP，字重和字距未漂移；
- 间距与布局：内容与占位文字共用 `40,0,16,0`，模板内分别垂直居中；
- 颜色与令牌：普通状态使用 `GameNestSearchBorderBrush`，悬停/焦点使用 `GameNestAccentBrush`；
- 图像与图标：继续使用系统 `FontIcon`，无新增或替换资产；
- 文案：保持“搜索游戏”，未发生内容变化。

比较历史：Pass 7/8 依赖默认 `TextBox` 模板和不对称 Padding，实机仍显示文字偏上；Pass 9 改为专用 `SearchTextBoxStyle`，让 `ContentElement` 与 `PlaceholderTextContentPresenter` 独立、明确地垂直居中。修正版聚焦对比未发现剩余 P0/P1/P2 问题。

final result: passed

## Phase 2 增量视觉验收

### Pass 11：扫描与导入页面

- 阶段：Phase 2 自动扫描；
- 视觉基线：`docs/GameNest_Fluent_Air_UI.png`；
- 实现证据：`artifacts/visual-audit/2026-08-05/phase2-scan-page.png`；
- 窗口：1680×1000 DIP，125% DPI 下完整物理捕获为 2100×1250 px；
- 状态：真实零 ScanRoot、零候选，未注入示例游戏或伪扫描结果；
- 布局：左侧 360 DIP 扫描范围，右侧弹性确认区，顶部操作与底部确认入口在默认窗口内完整可见；
- 分组：使用“确定是游戏 / 可能是游戏 / 已忽略”三档 Pivot，延续 Fluent Air 的白色表面、细边框和单一蓝色强调；
- 控制：添加目录、快速/深度扫描、暂停/取消、撤销排除和确认导入形成清晰动作层级；
- 状态反馈：根目录空状态和候选 0 项来自真实 SQLite 数据；禁用按钮与当前可执行状态一致；
- 回归：顶部搜索框、左导航和 1680×1000 无滚动条约束保持不变。

未发现阻断 Phase 2 的 P0/P1/P2 视觉问题。

final result: passed

### Pass 10：搜索图标光学中心修正

- 实机反馈：几何居中后，Segoe MDL2 Assets 的放大镜字形仍因自身留白而比文字视觉中心高约 2 px；
- 修复：保持文字模板不动，仅把搜索 `FontIcon` 的顶部 Margin 增加 2 DIP；
- 实现截图：`artifacts/visual-audit/2026-08-04/24-phase1-search-icon-optical-center.png`；
- 结果：放大镜主体与“搜索游戏”文字的视觉中心线一致，搜索框文字本身仍保持上下居中；未发现新增 P0/P1/P2 问题。

final result: passed
## Phase 3 增量视觉验收

### Pass 12：运行页与停止安全状态

- 阶段：Phase 3 可靠的运行与停止；
- 视觉基线：`docs/GameNest_Fluent_Air_UI.png`；
- 窗口：1680×1000；
- 状态：真实零运行游戏，未向用户库注入示例进程或伪会话；
- UI Automation：运行页标题、说明、空状态和搜索框均在窗口可见范围内；可见滚动条为 0；
- 交互：已确认运行态才显示停止入口，超时后由原生 ContentDialog 二次确认；
- 密度：详情新增本次会话、累计时长和身份提示，仍保持 B｜Fluent Air 的轻量信息层级。

本机 WinUI 合成器对 `PrintWindow` 返回空白帧，未将无效图片作为视觉证据。未发现阻断 Phase 3 的 P0/P1/P2 布局问题。

final result: passed
