# AE UI Phase 6.3: Animation, Retargeting, and Metadata Editor Family

**Goal:** 将动画编辑、动画重定向、Animation Meta 与共享属性编辑界面迁移到已批准的石墨蓝设计系统，同时保持命令、绑定、快捷键、拖放、预览与保存行为不变。

**Scope:** `Editors/AnimationEditor` 的 9 个 XAML、`Editors/AnimationFragmentEditor` 的 3 个 XAML、`Editors/AnimationReTarget` 的 7 个 XAML、`Editors/MetaDataEditor/AnimationMeta` 的 5 个 XAML，以及 `Editors/Shared/Editors.Shared.Core` 的 7 个用户可见 XAML，共 31 个界面。

## Success criteria

- [x] 新增显式选择使用的共享编辑器工作区样式字典，为常用控件映射现有 `Ae*` 键控样式，并使用 Phase 1 的 `AppFontFamily` / `AppFontWeight`，不改变全局隐式样式。
- [x] 31 个界面使用语义 Brush、统一字体与中等偏紧凑密度，不保留固定主题色或旧主题资源。
- [x] 动画播放控件改用清晰、垂直居中的矢量播放图标，不依赖字体字符或 Emoji。
- [x] 骨骼映射树的展开箭头、选择状态和密集属性编辑器在四主题下保持清晰一致。
- [x] 所有现有命令、Binding、点击处理、快捷键、拖放、预览和保存入口保持不变。
- [x] 结构回归测试通过，并在 Dark、Light、HighContrastDark、HighContrastLight 下实例化全部 31 个用户可见界面。
- [x] 100%、125%、150% Windows 缩放下完成 372 张真实 WPF 截图和逐图检查，系统缩放最终恢复到 150%。
- [x] 相关定向测试与完整 Release restore/build/test 全部通过，`git diff --check` 无错误。

## Implementation

### 1. Establish family contracts

- [x] 新增 `Testing/AssetEditorTests/UiAnimationMetadataFamilyTests.cs`，固定 31 个产品 XAML、语义主题资源、公共交互样式和关键行为契约。
- [x] 新增 `Shared/SharedUI/Common/Styles/EditorWorkspaceStyles.xaml`，只提供编辑器家族显式合并的隐式到键控样式映射。
- [x] 在各家族根工作区和独立窗口合并样式；画廊直接渲染子视图时也显式加载同一字典。

### 2. Migrate animation and retargeting workflows

- [x] 迁移动画关键帧、战役动画创建、坐骑动画创建、批处理、保存预览和可视化辅助界面。
- [x] 迁移动画包、AnimSet 表格编辑与批量导出，保留表格编辑事件和键盘输入绑定。
- [x] 迁移重定向工作区、骨骼选择/设置、映射窗口、保存窗口和设置页，修正仅影响布局的错误列索引。

### 3. Migrate metadata and shared property editing

- [x] 迁移 Animation Meta 主界面、条目、属性、新建窗口与 SuperView。
- [x] 迁移共享 EditorHost、动画播放、引用模型、骨骼映射和文本编辑器。
- [x] 用几何图标替换动画播放字符图标；保留命令参数和 ToggleButton 语义。

### 4. Visual verification

- [x] 新增 `Testing/AssetEditorTests/UiAnimationMetadataFamilyGallery.cs`，使用真实视图/窗口及安全示例 DataContext 渲染全部 31 个界面。
- [x] 每个缩放档生成四主题截图与接触表；逐图检查裁切、滚动、对齐、对比度、焦点、禁用、空状态和密集中文内容。
- [x] 发现问题后只修改本家族相关代码，重新生成受影响截图直至通过。

### 5. Gates and handoff

- [x] 运行结构/画廊测试和 Animation Meta、Retargeting、Editors.Shared.Core 定向测试。
- [x] 更新 `ae-ui-migration-ledger.md` 与总路线图，31 个 XAML 与新增样式字典全部进入清单。
- [x] 运行完整 Release restore/build/test 与差异检查。
- [x] 本地提交本批次；不推送、不创建 PR、不发布，然后直接进入 Phase 6.4。
