# Asset Editor 国区版 UI 规范

## 1. 适用范围与事实来源

本规范适用于 `AssetEditor/`、`Shared/SharedUI/`、`Editors/` 和 `GameWorld/` 中所有新增或修改的 WPF 用户界面。它是 UI 设计、实现、审查和验收的唯一长期规范，取代 2026-08-05 UI 重构产生的一次性设计规格、阶段计划和迁移台账。

执行 UI 任务前必须先读取本文件，再核对当前代码、资源字典和测试。事实优先级为：

1. 当前代码、XAML、项目引用和自动化测试；
2. 本规范中代码无法稳定表达的视觉与交互约束；
3. 历史截图、计划、提交和对话，只用于定位，不证明当前行为。

UI 修改只改变呈现或用户已明确要求的交互，不得顺带改变 Binding、Command、快捷键、拖放、保存、关闭、Git、Pack 或编辑器业务语义。

## 2. 视觉方向

- 风格为 Visual Studio / Codex 类的专业桌面工作台：深色石墨基线、克制、清晰、中等偏紧凑。
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
7. `Themes/DesignSystem/Controls/Buttons.xaml`
8. `Themes/DesignSystem/Controls/Inputs.xaml`
9. `Themes/DesignSystem/Controls/Collections.xaml`
10. `Themes/DesignSystem/Controls/MenusAndFeedback.xaml`
11. `Themes/DesignSystem/Shell.xaml`
12. `Themes/DesignSystem/Workflows.xaml`

业务 XAML 使用 `DynamicResource` 消费主题颜色与字体；不得复制主题色值到业务页面。只有不会随主题变化的尺寸、圆角和时长可以使用 `StaticResource`。

### 3.2 语义颜色

业务界面只使用以下 `AeBrush.*` 角色，不直接引用 `AColour.*` 或写固定主题色：

| 资源 | 深色基线 | 用途 |
| --- | --- | --- |
| `AeBrush.Canvas` | `#151719` | 应用和编辑器底层背景 |
| `AeBrush.Surface1` | `#1B1E21` | 侧栏、工具区、一级内容面 |
| `AeBrush.Surface2` | `#212529` | 输入、弹层和次级表面 |
| `AeBrush.Surface3` | `#282D32` | 按钮和选中前景表面 |
| `AeBrush.SurfaceHover` | `#30363C` | 普通悬停 |
| `AeBrush.Border` | `#343A40` | 默认边框和分隔 |
| `AeBrush.BorderStrong` | `#464E56` | 输入、弹层等强边界 |
| `AeBrush.TextPrimary` | `#E4E7E9` | 主要文字 |
| `AeBrush.TextSecondary` | `#B0B6BC` | 普通说明和控件文字 |
| `AeBrush.TextMuted` | `#858D95` | 次要信息 |
| `AeBrush.Accent` | `#64A9E2` | 选中和主操作 |
| `AeBrush.AccentHover` | `#75B5E8` | 强调悬停 |
| `AeBrush.AccentSoft` | `#263A4B` | 低强度选中背景 |
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
- `AeSize.ActivityRailWidth`：30 DIP。
- `AeSize.TabHeight`：24 DIP。
- `AeSize.CompactRowHeight`：24 DIP。
- `AeSize.ControlHeight`：26 DIP；普通输入框、按钮和选择器优先使用此高度。
- `AeSize.ProminentControlHeight`：30 DIP；只用于需要更强层级的主要操作。
- `AeRadius.Compact/Control/Surface/Overlay`：3、4、6、7 DIP。

控件高度由密度角色决定，不随字号任意放大。文字、图标和下拉箭头必须在固定高度中视觉居中，不能出现“大输入框配小字”和过量上下留白。

### 3.5 表面

- `AeSurface.Canvas`：无边框底层画布。
- `AeSurface.Panel`：`Surface1`、1 DIP `Border`、6 DIP 圆角。
- `AeSurface.Control`：`Surface2`、1 DIP `BorderStrong`、4 DIP 圆角。
- `AeSurface.Overlay`：`Surface2`、1 DIP `BorderStrong`、7 DIP 圆角。

不得在同一层级重复套用面板、卡片和边框。独立窗口已经提供窗口边界时，内部只保留业务所需的一层内容结构。

## 4. 公共控件契约

### 4.1 必须复用的样式家族

- 按钮：`AeButton.Primary`、`Secondary`、`Quiet`、`Danger`、`Icon`、`DropdownArrow`、`VisibilityToggle`。
- 输入：`AeInput.TextBox`、`ComboBox`、`CheckBox`、`RadioButton`、`Switch`、`ExpandableField`。
- 表单：`AeForm.Label`、`AeForm.TextLabel`。
- 集合：`AeTree.*`、`AeList.*`、`AeTable.*`、`AeTab.*`、`AeTag.*`、`AeScrollBar.Compact`。
- 菜单与反馈：`AeMenu.*`、`AeToolTip`、`AeFeedback.*`、`AeValidation.Message`、`AeEmptyState.*`、`AeProgress.Bar`。
- 主工作区：`AeShell.*`。
- 标准工作流：`AeWorkflow.*`。

新增无业务含义且被多个模块真实复用的控件才进入 `Shared.Ui`。单个编辑器专用样式留在该编辑器中，但必须基于上述语义资源和公共样式。

### 4.2 按钮交互

这是用户已验收的固定行为，所有普通按钮、图标按钮、ToggleButton、播放器按钮和编辑器专用按钮都必须一致：

按下和回弹统一复用 `AeMotion.ButtonPressStoryboard` 与 `AeMotion.ButtonReleaseStoryboard`，不得在业务页面复制另一套 Storyboard。

- 鼠标悬停：只高亮背景、边框或图标颜色；不得放大按钮。
- 鼠标按下：立即缩放到 `0.985`，70 ms，`EaseOut`。
- 鼠标松开：立即回弹到 `1.0`，120 ms，`EaseOut`。
- 不等待长按，不在松开后才开始完整按压动画。
- 不创建半透明复制层、残影、光晕或持续状态覆盖层。
- 所有按钮 `FocusVisualStyle` 为 `null`，不得显示聚焦框、蓝色描边或按下后残留边框；键盘命令、访问键和自动化名称仍必须保留。
- 禁用状态降低层级但保持文字可读；危险按钮使用 `Danger`，不能只靠红色表达风险。

播放器播放期间，播放按钮背景持续保持选中高亮，播放图标使用 `AeBrush.Danger`；停止或暂停后恢复普通状态。播放器复用 `AeEditor.PlaybackToggle` 等现有实现，不单独制造另一套按钮。

### 4.3 输入、选择和下拉箭头

- 点击输入框主体必须能编辑；不得用透明元素、装饰层或错误的命中区域挡住输入。
- ComboBox 点击整个控件都能展开，不能只让箭头区域响应。
- 下拉、展开和折叠箭头使用固定尺寸矢量图标；不得使用 Unicode 字符、Emoji 或依赖字体基线的符号。
- 所有编辑器中的下拉箭头都不使用圆形卡片或独立背景。悬停只高亮箭头本身，不高亮圆形/方形背景。
- 输入焦点、校验错误、禁用和只读状态必须可区分；输入控件可以使用语义焦点边框，但按钮不得出现聚焦框。

### 4.4 图标

- 优先复用 `Material.Icons.WPF` 或项目现有矢量 `Path`；禁止用 `×`、`→`、`▾` 等字体字符充当生产图标。
- 图标先放入明确尺寸的图标盒，再在盒内水平、垂直居中；不能依靠文字基线调整位置。
- 图标颜色使用语义 Brush，必须随十套主题切换。
- 类型图标需要在 16–20 DIP 下仍可辨识。Kitbash 场景树沿用已确认的 C 组类型语义：复选框、类型图标、名称紧凑排列；锁图标紧贴名称右侧，不放到行的最远端。
- 可见性使用小眼睛图标，不使用含义不明确的蓝色圆点；启用/禁用使用复选框或明确的状态图标。

## 5. 布局规则

### 5.1 主窗口与设置

- 主窗口保持：顶部标题/菜单、30 DIP 左活动栏、可调宽度内容侧栏、多标签编辑区、紧凑底部状态栏。
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
- 已知总量必须显示来自业务阶段的真实 `current/total`；未知总量使用不确定进度，禁止估算、伪造百分比或用静态文本冒充进度。
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
| 按钮 | `AssetEditor/Themes/DesignSystem/Controls/Buttons.xaml` | `AeButton.Primary`、`AeButton.Secondary`、`AeButton.Quiet`、`AeButton.Danger`、`AeButton.Icon`、`AeButton.DropdownArrow`、`AeButton.VisibilityToggle` |
| 输入与表单 | `AssetEditor/Themes/DesignSystem/Controls/Inputs.xaml` | `AeInput.TextBox`、`AeInput.ComboBox`、`AeInput.CheckBox`、`AeInput.RadioButton`、`AeInput.Switch`、`AeInput.ExpandableField`、`AeForm.Label`、`AeForm.TextLabel`、`AeValidation.Message` |
| 集合控件 | `AssetEditor/Themes/DesignSystem/Controls/Collections.xaml` | `AeTab.Item`、`AeTag.Container`、`AeTag.Text`、`AeTree.View`、`AeTree.Item`、`AeList.View`、`AeList.Item`、`AeTable.Grid`、`AeTable.Header`、`AeTable.Row`、`AeTable.Cell` |
| 菜单与反馈 | `AssetEditor/Themes/DesignSystem/Controls/MenusAndFeedback.xaml` | `AeMenu.Bar`、`AeMenu.Item`、`AeMenu.Context`、`AeToolTip`、`AeFeedback.Notice`、`AeFeedback.Icon`、`AeFeedback.SuccessIcon`、`AeFeedback.WarningIcon`、`AeFeedback.DangerIcon`、`AeEmptyState.Panel`、`AeEmptyState.Title`、`AeEmptyState.Description`、`AeProgress.Bar`、`AeScrollBar.Compact` |
| 变量、文字与表面 | `AssetEditor/Themes/DesignSystem/DesignTokens.xaml`、`Typography.xaml`、`SurfaceStyles.xaml` | `AeSpace.*`、`AeSize.*`、`AeRadius.*`、`AeMotion.*`、`AeText.*`、`AeSurface.*` |
| 主窗口工作区 | `AssetEditor/Themes/DesignSystem/Shell.xaml` | `AeShell.*` |
| 设置与标准工作流 | `AssetEditor/Themes/DesignSystem/Workflows.xaml` | `AeWorkflow.*` |
| 编辑器按钮与播放器 | `Shared/SharedUI/Common/Styles/EditorWorkspaceStyles.xaml` | `AeEditor.ToggleIcon`、`AeEditor.PlaybackToggle` |
| 分隔条 | `Shared/SharedUI/Common/Styles/GridSplitterStyles.xaml` | `AeVerticalGridSplitterStyle`、`AeHorizontalGridSplitterStyle` |
| 统一窗口 | `Shared/SharedUI/Common/AssetEditorWindow.cs` | `AssetEditorWindow` 与 `CustomWindowStyle` |
| 标准消息 | `Shared/SharedCore/Services/IStandardDialogs.cs`、`Shared/SharedUI/BaseDialogs/StandardDialog/` | `IStandardDialogs`、`UnifiedMessageBox`、`MessageDialogWindow` |
| 长操作进度 | `Shared/SharedUI/Common/OperationProgress/` | `OperationProgressWindowHost`、`OperationProgressWindow` |

`AssetEditor/Themes/Controls.xaml` 是旧模板和第三方控件的兼容层，不是新增公共 UI 的首选入口。只有目录中没有对应能力且现有兼容模板确实无法组合时，才考虑扩展设计系统。

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
7. 运行受影响项目 Release 构建、相关测试和 `git diff --check`；跨模块 UI 变更执行完整 Release 构建与完整测试。
8. 交给用户在真实应用中完成主题、DPI、字体、窗口尺寸和交互验收。

## 12. 禁止项

- 禁止新增固定主题色、固定字体族、任意字号、任意行高和未登记的间距尺度。
- 禁止用字体字符或 Emoji 代替图标。
- 禁止下拉箭头圆卡片、按钮聚焦框、按钮悬停放大、松开后才播放按压动画、残影和半透明复制层。
- 禁止控件过高而文字过小，禁止依靠冒号对齐表单。
- 禁止子分组伪装成父分组，禁止无意义横线和层层嵌套卡片。
- 禁止嵌入式加载界面、重复加载状态、假进度、假详情和无效取消按钮。
- 禁止为了视觉重构改变业务数据、Git/Pack 状态、保存语义或编辑器生命周期。
- 禁止只在深色主题、100% 缩放或静态截图下宣称 UI 完成。
