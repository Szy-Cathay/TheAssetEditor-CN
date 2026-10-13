# Asset Editor 国区版 UI 规范

## 1. 适用范围与事实来源

本规范适用于 `AssetEditor/`、`Shared/SharedUI/`、`Editors/` 和 `GameWorld/` 中所有新增或修改的 WPF 用户界面。它是 UI 设计、实现、审查和验收的唯一长期规范，取代 2026-08-05 UI 重构产生的一次性设计规格、阶段计划和迁移台账。

执行 UI 任务前先读第 1、9、10、12 节及任务相关章节；跨模块 UI 或整体验收时通读全文，再核对当前代码、资源字典和测试。事实优先级为：

1. 当前代码、XAML、项目引用和自动化测试；
2. 本规范中代码无法稳定表达的视觉与交互约束；
3. 历史截图、计划、提交和对话，只用于定位，不证明当前行为。

UI 修改只改变呈现或用户已明确要求的交互，不得顺带改变 Binding、Command、快捷键、拖放、保存、关闭、Git、Pack 或编辑器业务语义。

## 2. 视觉方向

- 风格为 Visual Studio / Codex 类的专业桌面工作台：Codex 参考图的中性灰阶基线、克制、清晰、中等偏紧凑。
- 主要层级依靠对齐、文字层级、表面明度和 1 DIP 边框，不依靠大卡片、阴影或装饰。
- 不使用玻璃、霓虹、渐变堆叠、满屏卡片、过度胶囊化、大面积阴影或高饱和装饰色。
- 大面积工作区保持平整；圆角只用于控件、面板和弹层，不把每个业务分组都做成独立卡片。
- 状态不能只靠颜色表达；成功、警告、错误、锁定、可见性和播放状态同时使用图标、文字或形状。

## 3. 资源与主题

### 3.1 强制资源顺序

`AssetEditor/App.xaml` 和 `Testing/AssetEditorTests/WpfTestApplicationHost.cs` 必须保持以下合并顺序：

1. `Themes/ColourDictionaries/{Theme}.xaml`
2. `Themes/ControlColours.xaml`
3. `Themes/DesignSystem/DesignTokens.xaml`
4. `Themes/DesignSystem/Typography.xaml`
5. `Themes/DesignSystem/SurfaceStyles.xaml`
6. `Themes/Controls.xaml`（兼容层）
7. `Themes/DesignSystem/Controls/Interaction.xaml`
8. `Themes/DesignSystem/Controls/Buttons.xaml`
9. `Themes/DesignSystem/Controls/Inputs.xaml`
10. `Themes/DesignSystem/Controls/Collections.xaml`
11. `Themes/DesignSystem/Controls/MenusAndFeedback.xaml`
12. `Themes/DesignSystem/Shell.xaml`
13. `Themes/DesignSystem/Workflows.xaml`

业务 XAML 使用 `DynamicResource` 消费主题颜色与字体；不得复制主题色值到业务页面。只有不会随主题变化的尺寸、圆角和时长可以使用 `StaticResource`。

### 3.2 语义颜色

业务界面只使用以下 `AeBrush.*` 角色，不直接引用 `AColour.*` 或写固定主题色：

| 资源 | 深色基线 | 用途 |
| --- | --- | --- |
| `AeBrush.Canvas` | `#111111` | 应用和编辑器底层背景 |
| `AeBrush.Surface1` | `#181818` | 侧栏、工具区、一级内容面 |
| `AeBrush.Surface2` | `#212121` | 输入、弹层和次级表面 |
| `AeBrush.Surface3` | `#292929` | 按钮和选中前景表面 |
| `AeBrush.SurfaceHover` | `#333333` | 普通悬停 |
| `AeBrush.Border` | `#303030` | 默认边框和分隔 |
| `AeBrush.BorderStrong` | `#4A4A4A` | 输入、弹层等强边界 |
| `AeBrush.TextPrimary` | `#EDEDED` | 主要文字 |
| `AeBrush.TextSecondary` | `#B5B5B5` | 普通说明和控件文字 |
| `AeBrush.TextMuted` | `#929292` | 次要信息 |
| `AeBrush.Accent` | `#64A9E2` | 键盘焦点和强调状态 |
| `AeBrush.OnAccent` | `#111111` | 强调背景上的文字与图标，随主题保持可读对比度 |
| `AeBrush.AccentHover` | `#75B5E8` | 强调悬停 |
| `AeBrush.AccentSoft` | `#303030` | 低强度选中背景 |
| `AeBrush.EditorTabAccent` | `#4A4A4A` | 兼容标签强调资源；主页以圆角表面表达选中，主题保留各自的对比度 |
| `AeBrush.Success` | `#72BC91` | 成功 |
| `AeBrush.Warning` | `#E2B45F` | 警告和未保存 |
| `AeBrush.Danger` | `#E17979` | 错误、危险和播放中图标 |

深色值只用于说明基线；其他主题必须在自己的颜色字典中提供同名角色。当前必须兼容：`DarkTheme`、`LightTheme`、`HighContrastDark`、`HighContrastLight`、`CoolDark`、`WarmDark`、`VSCodeDark`、`VSCodeLight`、`ChromeDark`、`ChromeLight`。

### 3.3 字体角色

| 样式 | 字号 | 字重 | 用途 |
| --- | ---: | --- | --- |
| `AeText.PageTitle` | 20 | SemiBold | 页面标题 |
| `AeText.SectionTitle` | 13 | SemiBold | 一级分区标题 |
| `AeText.Body` | 12 | Normal | 正文和普通控件 |
| `AeText.Label` | 11 | SemiBold | 字段标签 |
| `AeText.Caption` | 11 | Normal | 时间、说明和辅助信息 |
| `AeText.Technical` | 11 | Normal | 路径、资源 ID、数值和日志 |

普通界面字体使用 `AppFontFamily` 和 `AppFontWeight`。默认家族为 `Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI`；技术文本使用 `Cascadia Mono, Consolas`。Alibaba PuHuiTi 与 HarmonyOS 字体预览只能替换字体家族和可用字重，不改变字号、行高或布局；取消设置必须完整恢复，保存后才持久化。

### 3.4 尺寸、间距与圆角

- 间距只从 `AeSpace.1/2/3/4/6/8` 取值：4、8、12、16、24、32 DIP。
- `AeSize.ActivityRailWidth`：44 DIP。
- `AeSize.TabHeight`：28 DIP。
- `AeSize.CompactRowHeight`：24 DIP。
- `AeSize.ControlHeight`：28 DIP；普通输入框、按钮和选择器优先使用此高度。
- `AeSize.ProminentControlHeight`：32 DIP；只用于需要更强层级的主要操作。
- `AeSize.ScrollBarThumb/ScrollBarThumbActive`：滚动条可见滑块为 4／6 DIP，圆角收尾；正常、悬停、拖动分别使用 `TextMuted`、`TextSecondary`、`TextPrimary`，禁用复用公共透明度。横纵滚动条实际布局槽与命中区均使用 `AeSize.ScrollBarThickness`（6 DIP），不保留系统宽轨道、不显示箭头，也不覆盖内容边缘拦截点击。普通、紧凑及编辑器局部样式共用同一模板，保留原生 Track、翻页命令、滚轮、键盘和辅助功能语义。
- `AeRadius.Compact/Control/Surface/Overlay`：4、7、10、12 DIP。
- `AeRadius.EditorTab`：7 DIP 圆角，用于主工作区编辑器标签。

控件高度由密度角色决定，不随字号任意放大。文字、图标和下拉箭头必须在固定高度中视觉居中，不能出现“大输入框配小字”和过量上下留白。

### 3.5 表面

- `AeSurface.Canvas`：无边框底层画布。
- `AeSurface.Panel`：`Surface1`、1 DIP `Border`、10 DIP 圆角。
- `AeSurface.Control`：`Surface2`、1 DIP `BorderStrong`、7 DIP 圆角。
- `AeSurface.Overlay`：`Surface2`、1 DIP `BorderStrong`、12 DIP 圆角。

`AeSurface.Panel` 只用于需要明确边界的独立工作区、预览区或数据区，不用于普通标题分区、表单分组或对话框内容外壳。普通分区优先使用留白和必要的单条分隔线。不得在同一层级重复套用面板、卡片和边框；独立窗口已经提供窗口边界时，内部只保留业务所需的一层内容结构。

## 4. 公共控件契约

### 4.1 必须复用的样式家族

- 集合交互基类：`AeInteraction.Selectable`，统一悬停、选中、禁用与键盘焦点，不持有控件模板。
- 按钮：`AeButton.Primary`、`Secondary`、`Quiet`、`Danger`、`Icon`、`DropdownArrow`、`VisibilityToggle`。
- 输入：`AeInput.TextBox`、`ComboBox`、`CheckBox`、`RadioButton`、`Switch`、`ExpandableField`。
- 表单：`AeForm.Label`、`AeForm.TextLabel`。
- 集合：`AeTree.*`、`AeList.*`、`AeTable.*`、`AeTab.*`、`AeTag.*`、`AeScrollBar.Compact`。
- 菜单与反馈：`AeMenu.*`、`AeToolTip`、`AeFeedback.*`、`AeValidation.Message`、`AeEmptyState.*`、`AeProgress.Bar`。
- 主工作区：`AeShell.*`。
- 标准工作流：`AeWorkflow.*`。

新增无业务含义且被多个模块真实复用的控件才进入 `Shared.Ui`。单个编辑器专用样式留在该编辑器中，但必须基于上述语义资源和公共样式。

左活动栏是普通交互态的视觉基准：悬停使用 `AeBrush.SurfaceHover`，选中使用 `AeBrush.AccentSoft` 且优先于悬停，前景使用 `AeBrush.TextPrimary`，控件圆角使用 `AeRadius.Control`。集合项共用 `AeInteraction.Selectable`，禁用透明度共用 `AeInteraction.DisabledOpacity`。颜色直接切换，按压沿用既有缩放时序；不为普通悬停另加延迟或遮罩。树只对当前行命中区域响应悬停，不能因鼠标位于子项而点亮祖先；自定义多选树保留自己的选择状态来源。普通工具按钮与单选工具使用相同底色层级，主操作、危险操作、勾选图形、输入错误和键盘焦点保留各自语义。

### 4.2 按钮交互

除下述左活动栏特例外，这是用户已验收的固定行为，普通按钮、图标按钮、ToggleButton、播放器按钮和编辑器专用按钮都必须一致：

左活动栏的资源、工程历史、编辑器快捷入口及底部设置图标不使用按压缩放或回弹。按住期间持续显示悬停或活动底色，活动状态优先于悬停；命令按钮仍在松开时执行，不能因点击而永久选中。此特例不影响最近工程或其他区域的按钮动画。

按下和回弹统一复用 `AeMotion.ButtonPressStoryboard` 与 `AeMotion.ButtonReleaseStoryboard`，不得在业务页面复制另一套 Storyboard。

- 鼠标悬停：只高亮背景、边框或图标颜色；不得放大按钮。
- 鼠标按下：立即缩放到 `0.985`，70 ms，`EaseOut`。
- 鼠标松开：立即回弹到 `1.0`，120 ms，`EaseOut`。
- 不等待长按，不在松开后才开始完整按压动画。
- 不创建半透明复制层、残影、光晕或持续状态覆盖层。
- 所有可聚焦控件复用 `AeFocus.Keyboard`。该视觉只能由 WPF 键盘导航触发；鼠标点击、鼠标选中和按下回弹不得显示或残留蓝紫色聚焦边框。
- 键盘焦点使用 1 DIP 语义强调描边，不使用光晕、双层描边或持续状态覆盖层；键盘命令、访问键和自动化名称必须保留。
- 禁用状态降低层级但保持文字可读；危险按钮使用 `Danger`，不能只靠红色表达风险。

播放器播放期间，播放按钮背景持续保持选中高亮，播放图标使用 `AeBrush.Danger`；停止或暂停后恢复普通状态。播放器复用 `AeEditor.PlaybackToggle` 和可拖动的 `AeEditor.PlaybackSlider`，不单独制造另一套按钮或时间轴。

### 4.3 输入、选择和下拉箭头

- 点击输入框主体必须能编辑；不得用透明元素、装饰层或错误的命中区域挡住输入。
- ComboBox 点击整个控件都能展开，不能只让箭头区域响应。
- 下拉、展开和折叠箭头使用固定尺寸矢量图标；不得使用 Unicode 字符、Emoji 或依赖字体基线的符号。
- 所有编辑器中的下拉箭头都不使用圆形卡片或独立背景。悬停只高亮箭头本身，不高亮圆形/方形背景。
- 工具栏仅在确有隐藏工具时显示展开箭头；空间足够时隐藏空的展开入口。
- 输入焦点、校验错误、禁用和只读状态必须可区分；焦点描边同样只在键盘导航时出现，鼠标点击后不保留。

### 4.4 图标

- 优先复用 `Material.Icons.WPF` 或项目现有矢量 `Path`；禁止用 `×`、`→`、`▾` 等字体字符充当生产图标。
- 图标先放入明确尺寸的图标盒，再在盒内水平、垂直居中；不能依靠文字基线调整位置。
- 图标颜色使用语义 Brush，必须随十套主题切换。
- 类型图标需要在 16–20 DIP 下仍可辨识。Kitbash 场景树沿用已确认的 C 组类型语义：复选框、类型图标、名称紧凑排列；锁图标紧贴名称右侧，不放到行的最远端。
- 可见性使用小眼睛图标，不使用含义不明确的蓝色圆点；启用/禁用使用复选框或明确的状态图标。

### 4.5 菜单与可拖动分隔条

- 顶部菜单、子菜单和上下文菜单复用 `AeMenu.Bar`、`AeMenu.Item` 与 `AeMenu.Context`。悬停或键盘高亮直接使用 `AeBrush.SurfaceHover`，已展开菜单使用 `AeBrush.AccentSoft`；不得叠加第二层状态遮罩，也不使用 Popup 淡入动画，避免残影和悬停状态不同步。
- 所有可拖动面板分隔条复用 `Shared/SharedUI/Common/Styles/GridSplitterStyles.xaml` 中的 `AeVerticalGridSplitterStyle` 或 `AeHorizontalGridSplitterStyle`。两种样式统一使用 1 DIP 可见细线与 `AeSize.SplitterHitTarget`（8 DIP）透明命中区域，提供正确方向的调整光标、`PreviousAndNext` 调整语义以及悬停/拖动反馈。分隔条所在行列使用 `Auto`，实际仅占 1 DIP；公共样式通过两侧负边距将透明命中区域覆盖到相邻面板边缘，并提升绘制顺序，不能额外撑出宽缝。相邻面板保留最小尺寸约束。
- 业务 XAML 可以设置布局所需的行列与跨度，但不得内联覆盖分隔条的 Margin、背景、边框、方向、调整行为或模板；同时检查宿主行列、相邻面板的 Margin/Padding，避免额外制造空槽。新增 GridSplitter 时必须同步加入 `SharedUiArchitectureTests` 的消费清单，并通过 `SharedGridSplitterStyleTests`。

## 5. 布局规则

### 5.1 主窗口与设置

- 主窗口保持：单行顶部菜单与窗口按钮、44 DIP 左活动栏、可调宽度内容侧栏、多标签编辑区、紧凑底部状态栏。主窗口基于公共窗口样式，使用只有内容表面的局部模板容纳集成菜单，不增加独立标题行；保留 `SingleBorderWindow` 和 `WindowChrome` 的原生拖动、双击、系统菜单及当前显示器工作区计算。不得用 `WindowStyle=None`、主显示器尺寸上限或固定底部留白代替工作区边界。窗口按钮走系统命令与既有关闭检查。版本与游戏信息只在底部显示，状态文字垂直居中并保留上下留白，异常提示可自然增高。
- 相邻菜单、侧栏、标签页、编辑区与状态栏直接衔接，用单条边界分隔；宿主不重复叠加外边距，圆角用于可点击控件而非连成一体的工作区接缝。内容内部保留 4／8 DIP 等既有间距尺度，不以压缩文字或缩小点击目标换取紧凑。
- 左活动栏、内容侧栏与编辑区共享工作区外框的顶边、底边及总高度。资源／工程历史不显示常驻标题行，只保留对应活动栏图标的中文悬停提示与无障碍名称；内容侧栏直接显示原有工具条和内容。编辑标签仅在有文档时出现，每行 28 DIP，分隔线叠绘在行内，不额外增加高度；空编辑区不保留空白标题行。不同区域内部的必要工具条和标签带不强行撑成等高，侧栏收起后保留活动栏及底部设置入口。
- “文件”之前的图标只控制资源树／历史内容侧栏与其分隔线，保留最左侧活动栏及编辑器入口；展开时恢复收起前的宽度。顶部不再保留独立“视图”菜单。内容侧栏直接显示资源树或历史；新建、打开和最近工程放在空编辑区，复用顶部菜单的真实工程集合与打开命令。重复打开已有工程只激活该工程，正在打开时忽略重入点击。
- 左活动栏在资源/历史导航之后，按现有 `AddToolbarButton` 登记列出独立工具，并复用 `EditorShortcutViewModel.OpenEditorCommand`。图标使用矢量资源并提供中文提示及无障碍名称；禁用与游戏限制沿用工具登记，活动状态跟随当前编辑标签。依赖已打开文件且未登记独立入口的编辑器不额外添加按钮。
- 标签页红点只表达真实的未保存文档状态；未提供该状态的浏览、生成和导出工具默认不显示，不能将绑定失败当作已修改。骨架工具加载和选择骨骼不产生红点，骨骼内容、结构或输出名称改变后才显示；骨架与逆矩阵都成功写入才清除，取消或失败保留关闭保护。
- 不为不存在的功能放假搜索框、假按钮或占位操作。
- 全局设置的导航和内容均从左上角开始，使用紧凑行距；不得把整页内容垂直或水平居中。
- 设置页说明优先放入 ToolTip，不用长期占空间的大段解释文字。

### 5.2 编辑器属性表单

- 字段行不显示装饰性的冒号。
- 标签位于控件前方，标签列固定宽度并右对齐；不同长度的中文标签向控件侧对齐。
- 控件区域从同一条竖线开始，向右侧工作区对齐或拉伸；同组控件的左边界、右边界和行高保持一致。
- 复选框、眼睛、浏览、清除等紧凑操作必须与对应字段在同一行并紧贴控件，不单独占用一条宽列。
- 三轴数值、双值缩放和同类复合字段使用等宽子列。
- 业务信息区优先左上对齐；只有空状态说明可以在空白工作区居中。

### 5.3 分组层级

- 一级分组使用 `AeText.SectionTitle`、清晰的展开箭头和一级边界。
- 子分组相对父项缩进，标题字号可以与父项相同，但使用 Normal 字重。
- 子分组不得与父分组占同样的整行背景和边框，否则会误导为同级。
- Kitbash 的“基础贴图”“血液”“阵营颜色预览”等子项保持透明背景，不添加标题下横线；内容通过缩进和留白表达层级。
- 分隔线只用于真正的区域边界，不在每个字段或子标题后重复绘制。

### 5.4 树、列表和密集编辑器

- 树节点展开箭头放在固定盒中，按完整行高垂直居中。
- 选中子节点只高亮自身，不给所有父节点叠加边框或选中背景。
- 场景树的复选框、类型图标、文字保持紧凑，不用过大的缩进、行高或图标。
- 列表、树和表格必须保留虚拟化、多选、拖放、上下文菜单、键盘和展开状态等现有行为。
- 主窗口资源树不再用加粗表示当前编辑目标。只在工程 / Pack 根节点右侧显示访问标识：当前可写文件夹工程显示小铅笔，ToolTip 为“当前编辑工程”；其他容器（包括 CA Pack、参考 Pack 和未激活工程）显示灰色斜体 `[只读]`。只读表示当前不能作为编辑写入目标，不代表磁盘权限；切换当前工程后同步更新两边标识。标识与选中高亮、未保存星号和忽略状态各自独立，文件 / 文件夹子节点不重复标注。
- 骨骼设置保持左右分栏：左侧骨骼树，右侧选中骨骼属性；没有选中骨骼时也不得把右侧属性区移到整体中央。

## 6. 窗口、对话框与消息

- 所有主窗口、编辑器子窗口、照片工作室、工具窗口和标准对话框使用公共 `AssetEditorWindow` / `CustomWindowStyle`，统一标题栏、背景、边框和主题切换。
- 不允许再出现原生白色标题栏、主题不一致的顶部栏、右侧/底部黑边或额外宿主边框。
- 模态窗口设置正确 Owner，保持原有默认按钮、取消按钮、返回值和关闭语义。
- 标准消息通过 `IStandardDialogs`、`UnifiedMessageBox` 或现有公共抽象显示，不在业务 ViewModel 中直接创建窗口。
- 错误、警告、询问和信息消息保留对应语义图标；图标无卡片背景并与消息内容顶部/视觉中心对齐。
- 对话框内部不再套一层“窗口式卡片”；窗口表面、正文和底部操作区最多形成一套清晰层级。

## 7. 加载与真实进度

- 加载只使用公共 `OperationProgressWindowHost` / `OperationProgressWindow`；不得在工作区、资源树或编辑器内部再嵌入第二套加载条或状态卡。
- 音频浏览器、音频编辑器及其他长操作同样使用独立进度窗口。
- 操作在 500 ms 内完成时不显示窗口；超过 500 ms 才显示。窗口显示后至少保持 300 ms，避免闪烁。
- 应用启动画面在首帧后开始真实资源准备，不适用普通短任务的延迟出现规则。普通启动按“启动画面 → 首次路径设置（需要时）→ 后台读取、合并 CA Pack → 登记一次 → 后台整理资源树 → 主窗口首帧 → 启动画面淡出”交接；主窗口就绪后才处理外部 Pack 参数、启动 IPC 和检查更新。空目录或没有可读取 Pack 不能视为成功；重试工作区准备时复用已登记资源，取消时等待当前读取退出，不能发布半成品资源树。
- 启动失败提示区分资源读取与工作区打开阶段；资源已登记后不再提示修改游戏路径。窗口交接保留普通/最大化启动设置，主窗口首帧就绪前保持输入禁用，并遵守 WPF 对最大化窗口激活状态的约束。
- 应用图标沿用批准的透明 `AssetEditor.CN.png`，`AssetEditor.CN.ico` 提供 16／24／32／48／64／128／256 像素帧。启动专用画面等比例显示同一图标，外部像素保持透明，不铺壁纸或矩形底板；`AssetEditorCN` 是原生文字，加载指示、阶段和文件信息由真实任务驱动，不能烘焙到图片里。文字复用主题的纯色前景：深色主题使用浅字，浅色主题使用深字；不叠加阴影、描边或发光效果。透明窗口内文字使用灰阶抗锯齿、显示模式字距和像素对齐，避免整块文字先栅格化再缩放。启动画面不显示“查看详情”和“退出 AE”入口；失败时保留重试、适用阶段的路径修复及 Esc／Alt+F4 关闭提示。关闭请求即使与读取异常同时发生也必须完成，不能停留在失败页。运行中重新加载游戏资源仍使用普通加载窗口。启动专用模板保留 `WindowSurface` 供共同过渡控制器使用；小工作区通过外层滚动容纳错误说明和操作。
- 加载窗口先完成首帧绘制，从下一实际渲染帧开始按 `AeMotion.WindowEnter`（280 ms）淡入；结束时按 `AeMotion.WindowExit`（200 ms）淡出并等待窗口关闭，再继续后续对话框。退场期间保留内容，不能先折叠进度视图导致窗口缩小。内容表面以中心为原点，在 `AeMotion.WindowScale`（0.96）与原尺寸之间轻微缩放，使用无弹跳、无过冲的柔和缓动；不改变顶层窗口尺寸。复用原有标题栏、主题，不将动画当作业务进度。系统关闭客户端或窗口动画时直接切换状态。
- 三类加载入口（通用进度宿主、启动 Pack 加载、文件夹工程加载）共用 `OperationProgressWindowTransition`。透明度过渡必须使用 WPF 支持的整窗透明表面，不能只把不透明系统窗口中的内容设为透明；不叠加玻璃边缘或系统原生出入场动画。Owner 关闭时终止过渡并释放窗口，不能等退场动画阻塞退出。
- 已知总量必须显示来自业务阶段的真实 `current/total`；未知总量使用 `OperationProgressView` 的圆形动态指示，禁止估算、伪造百分比或用静态文本冒充进度。圆形指示仅在控件可见、操作进行中且无确定总量时运行，周期复用 `AeMotion.Indeterminate`；操作结束、隐藏、卸载或系统禁用客户端动画时停止。启动画面使用 `UseLeadingIndicator` 将指示放在阶段文字上方，其他加载窗口保持原有信息顺序。
- 操作开始即更新原界面的简短状态文字，独立窗口继续遵守短任务防闪延迟。音频浏览器首次打开与手动刷新共用加载流程，读取名称、发现音频库、整理索引和生成列表分别报告阶段；音频库读取和配音角色层级生成报告实际完成数。列表整理和层级构建在后台执行，取消、切换选择或关闭后不得回写旧结果。通用非模态进度窗口显示时不主动抢夺焦点。
- 当前详情显示正在处理的真实文件、对象或阶段；详情历史来自真实事件，不能生成演示数据。
- 取消按钮只在真实支持取消且绑定真实 `ICommand` 时出现；文本必须本地化。
- 同一操作只能有一个主要加载界面。操作完成、失败或取消后必须关闭窗口并恢复原界面可操作状态。

## 8. 中文、主题、DPI 与可访问性

- 所有用户可见文字只保留中文，并通过 `AssetEditor/Language_Cn.json` 的资源键提供；不得在 XAML、ViewModel 或公共控件中新增硬编码可见文本。
- 路径、资源 ID、格式名和游戏内部枚举可以保留技术原文，但其字段标签和说明必须中文化。
- 图标按钮提供中文 ToolTip 和 `AutomationProperties.Name`。
- 主题资源、控件模板和业务窗口必须在十套主题下无缺键；至少人工覆盖 Dark、Light、HighContrastDark、HighContrastLight。
- 人工视觉验收覆盖 Windows 100%、125%、150% 缩放，检查裁切、基线、图标居中、滚动条、弹层位置和中文截断。
- 自动化测试不能证明视觉正确；最终视觉与交互由用户在真实窗口中验收。
- 无交互实例化和离屏渲染优先用于自动验证。需要真实鼠标、键盘、系统焦点或实机预览窗口时，先按根目录 `AGENTS.md` 约定时段并取得用户明确同意；关闭 opt-in 开关不等于完整套件不会抢占桌面，仍须核对实际用例。焦点冲突不是自动重跑的理由，未获同意的用例跳过并明确报告。

## 9. 架构边界

- `Shared.Core` 不引用 WPF；核心层消息、状态和进度使用 UI 无关模型或接口。
- `Shared.Ui` 只能引用 `Shared/` 内项目，不反向依赖 `AssetEditor`、`Editors` 或具体业务编辑器。
- `AssetEditor` 是组合根；公共 UI 注册沿用现有 `DependencyInjectionContainer`、`IStandardDialogs` 和进度抽象。
- 业务 ViewModel 不直接创建标准窗口，不从代码后置层伪造业务状态。
- 公共控件消费应用提供的 `AeBrush.*`、字体和设计变量；WPF 测试统一使用 `WpfTestApplicationHost`。
- 不全局启用会替换所有 WPF 模板的第三方 StyleDictionary；第三方库只用于明确、局部、可验证的能力。

## 10. Agent 公共组件复用协议

本章是所有新增功能、修改控件和 UI 调整任务的强制入口。Agent 不得仅凭记忆判断“项目里没有现成控件”，必须先完成搜索、复用判断和证据记录。

### 10.1 公共资源目录

| 能力 | 文件 | 公开入口 |
| --- | --- | --- |
| 集合交互态 | `AssetEditor/Themes/DesignSystem/Controls/Interaction.xaml` | `AeInteraction.Selectable`；尺寸变量中的 `AeInteraction.DisabledOpacity` |
| 按钮 | `AssetEditor/Themes/DesignSystem/Controls/Buttons.xaml` | `AeButton.Primary`、`AeButton.Secondary`、`AeButton.Quiet`、`AeButton.Danger`、`AeButton.Icon`、`AeButton.DropdownArrow`、`AeButton.VisibilityToggle` |
| 输入与表单 | `AssetEditor/Themes/DesignSystem/Controls/Inputs.xaml` | `AeInput.TextBox`、`AeInput.ComboBox`、`AeInput.ComboBoxItem`、`AeInput.CheckBox`、`AeInput.RadioButton`、`AeInput.Switch`、`AeInput.ExpandableField`、`AeForm.Label`、`AeForm.TextLabel`、`AeValidation.Message` |
| 集合控件 | `AssetEditor/Themes/DesignSystem/Controls/Collections.xaml` | `AeTab.Item`、`AeTag.Container`、`AeTag.Text`、`AeTree.View`、`AeTree.Item`、`AeList.View`、`AeList.Item`、`AeTable.Grid`、`AeTable.Header`、`AeTable.Row`、`AeTable.Cell` |
| 菜单与反馈 | `AssetEditor/Themes/DesignSystem/Controls/MenusAndFeedback.xaml` | `AeMenu.Bar`、`AeMenu.Item`、`AeMenu.Context`、`AeToolTip`、`AeFeedback.Notice`、`AeFeedback.Icon`、`AeFeedback.SuccessIcon`、`AeFeedback.WarningIcon`、`AeFeedback.DangerIcon`、`AeEmptyState.Panel`、`AeEmptyState.Title`、`AeEmptyState.Description`、`AeProgress.Bar`、`AeScrollBar.Compact` |
| 变量、文字、表面与焦点 | `AssetEditor/Themes/DesignSystem/DesignTokens.xaml`、`Typography.xaml`、`SurfaceStyles.xaml` | `AeSpace.*`、`AeSize.*`、`AeRadius.*`、`AeMotion.*`、`AeText.*`、`AeSurface.*`、`AeFocus.Keyboard` |
| 主窗口工作区 | `AssetEditor/Themes/DesignSystem/Shell.xaml` | `AeShell.*` |
| 设置与标准工作流 | `AssetEditor/Themes/DesignSystem/Workflows.xaml` | `AeWorkflow.*` |
| 编辑器按钮与播放器 | `Shared/SharedUI/Common/Styles/EditorWorkspaceStyles.xaml` | `AeEditor.ToggleIcon`、`AeEditor.PlaybackToggle`、`AeEditor.PlaybackSlider` |
| 分隔条 | `Shared/SharedUI/Common/Styles/GridSplitterStyles.xaml` | `AeVerticalGridSplitterStyle`、`AeHorizontalGridSplitterStyle` |
| 统一窗口 | `Shared/SharedUI/Common/AssetEditorWindow.cs` | `AssetEditorWindow` 与 `CustomWindowStyle` |
| 标准消息 | `Shared/SharedCore/Services/IStandardDialogs.cs`、`Shared/SharedUI/BaseDialogs/StandardDialog/` | `IStandardDialogs`、`UnifiedMessageBox`、`MessageDialogWindow` |
| 长操作进度 | `Shared/SharedUI/Common/OperationProgress/` | `OperationProgressWindowHost`、`OperationProgressWindow`、`OperationProgressWindowTransition` |

`AssetEditor/Themes/Controls.xaml` 是旧模板和第三方控件的兼容层，不是新增公共 UI 的首选入口。只有目录中没有对应能力且现有兼容模板确实无法组合时，才考虑扩展设计系统。

普通 Button、TextBox、ComboBox、ListBoxItem、TabItem、Menu、MenuItem 和 ContextMenu 的默认样式直接复用以上公共样式。输入框、下拉框和菜单保留兼容层的命名基础模板，默认入口与显式公共样式入口共用同一编辑、展开和勾选行为；默认样式保留密集编辑器原有的尺寸约束。专用视口、时间轴、表格与第三方控件的独立模板继续由原模块持有，不以默认样式替换其交互契约。

### 10.2 强制搜索

开始编辑前，Agent 至少执行以下搜索，并查看两个最接近当前需求的真实用例：

```powershell
rg -n 'x:Key="Ae|AssetEditorWindow|OperationProgressWindowHost|IStandardDialogs' AssetEditor Shared Editors GameWorld
rg -n 'AeButton\.|AeInput\.|AeForm\.|AeTree\.|AeList\.|AeTable\.|AeWorkflow\.|AeEditor\.' AssetEditor Shared Editors GameWorld
rg -n '<(Button|ToggleButton|TextBox|ComboBox|TreeView|DataGrid|ProgressBar)\b' AssetEditor Shared Editors GameWorld --glob '*.xaml'
```

搜索目标不是找到相同名称，而是确认：已有语义样式、相同状态组合、相同窗口类型、相同业务交互以及相邻模块的成熟实现。未执行搜索不能声称“需要新控件”。

### 10.3 复用决策

严格按以下顺序决策，命中后停止，不继续扩大实现：

1. **直接复用**：已有公开样式或公共组件能够表达需求，直接引用并保留原 Binding、Command 和状态。
2. **组合复用**：单个样式不够时，使用现有布局容器、语义样式和公共行为组合，不复制 ControlTemplate。
3. **业务局部样式**：只有单个编辑器需要特殊排列时，在编辑器目录新增基于公开 `Ae*` 样式的局部样式；不得反向放入 `Shared.Ui`。
4. **扩展公共样式**：至少两个独立模块存在同一无业务含义需求，且组合现有能力仍会造成实质重复时，才允许扩展设计系统或 `Shared.Ui`。

不得为了改一个 Margin、宽度、文案、图标或业务状态创建新的公共控件。不得给现有公开样式换一个近义名称后复制模板。

### 10.4 新公共组件准入

新增或扩展公共组件必须同时满足并完成以下事项：

- 提供搜索证据，说明现有组件为什么无法直接复用或组合。
- 无业务名称、无具体编辑器依赖，并确认至少两个真实调用方。
- 使用现有 `AeBrush.*`、`AeText.*`、`AeSize.*`、`AeRadius.*` 和 `AeMotion.*`，不另建平行变量体系。
- 在对应设计系统资源文件中实现，并把公开键加入本章目录。
- 更新 `UiCommonControlResourceTests` 的公开样式契约，并覆盖默认、悬停、按下、禁用、选中等实际状态。
- 在 Dark、Light、HighContrastDark、HighContrastLight 中实例化或渲染验证。
- 保持 `Shared.Ui` 依赖边界，通过 `SharedUiArchitectureTests`。

只增加业务页面、不增加公共能力时，不得修改公共组件目录或创建新的全局资源字典。

### 10.5 交付证据

每个 UI 任务的最终回复必须简短说明：

- 复用了哪些现有资源、组件和参考用例；
- 是否新增公共组件；如果新增，列出两个真实调用方和无法组合复用的原因；
- 修改了哪些业务 UI，哪些 Binding、Command 和业务语义保持不变；
- 实际执行的主题、架构、构建、测试和人工验收范围。

没有新增公共组件时，也必须明确写出“未新增公共组件”，防止复制模板或局部控件被误报为复用。

## 11. 新功能实施清单

新增或修改 UI 时按以下顺序执行：

1. 读取本规范，定位已有公共样式、相似窗口和业务状态来源。
2. 明确哪些是视觉变化，哪些 Binding、Command、快捷键、Owner、关闭或保存行为必须保持。
3. 优先组合现有 `Ae*` 资源；只有确有多模块复用时才新增公共组件。
4. 新增所有中文资源键、ToolTip 和自动化名称。
5. 为默认、悬停、按下、禁用、选中、错误、空、加载等实际状态补测试。
6. UI 资源或主题变更使用 `WpfTestApplicationHost`；公共边界变更运行 `SharedUiArchitectureTests`。
7. 运行受影响项目 Release 构建、相关测试和 `git diff --check`；跨模块 UI 变更执行完整 Release 构建与完整测试。桌面输入用例受第 8 节的单独授权边界约束；用户要求停止后复用已有结果，保留失败、跳过和中止记录，不将局部通过写成整套通过。
8. 交给用户在真实应用中完成主题、DPI、字体、窗口尺寸和交互验收。

## 12. 禁止项

- 禁止新增固定主题色、固定字体族、任意字号、任意行高和未登记的间距尺度。
- 禁止用字体字符或 Emoji 代替图标。
- 禁止下拉箭头圆卡片、鼠标点击后出现或残留聚焦框、按钮悬停放大、松开后才播放按压动画、残影和半透明复制层；键盘导航必须保留 `AeFocus.Keyboard`。
- 禁止控件过高而文字过小，禁止依靠冒号对齐表单。
- 禁止子分组伪装成父分组，禁止无意义横线和层层嵌套卡片。
- 禁止嵌入式加载界面、重复加载状态、假进度、假详情和无效取消按钮。
- 禁止为了视觉重构改变业务数据、Git/Pack 状态、保存语义或编辑器生命周期。
- 禁止只在深色主题、100% 缩放或静态截图下宣称 UI 完成。
