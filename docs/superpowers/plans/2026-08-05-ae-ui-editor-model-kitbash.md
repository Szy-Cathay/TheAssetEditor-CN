# AE UI Phase 6.2: Model and Kitbash Editor Family

**Goal:** 将 Kitbash、模型、材质、骨骼和 3D 辅助工具迁移到已批准的石墨蓝设计系统，同时保持编辑命令、拖放、多选、3D 视口和保存行为不变。

**Scope:** `Editors/Kitbashing/KitbasherEditor` 的 28 个 XAML 源、`Editors/SkeletonEditor/Editor.VisualSkeletonEditor/SkeletonEditor/EditorView.xaml`、`GameWorld/View3D/Utility/UserInterface/ShaderTextureView.xaml`，以及两项纯 C# WPF 消费者。Dissolve 和 Iridescence 目录已从项目编译中排除，不纳入用户可见迁移。

## Success criteria

- [x] 家族级样式字典为常用控件映射 `Ae*` 键控样式，所有顶层窗口和工作区使用 `AeBrush.*` 与 `AeFont.*`。
- [x] Kitbash 主工作区、场景树、属性/材质页、骨骼编辑器和工具窗口形成一致的中等偏紧凑层级。
- [x] 不保留固定主题色或旧 `ABrush.*`、`ToolBarTrayBackground`、`GroupBox.Header.Static.Background` 消费者。
- [x] 保存对话框继续使用可编辑数值列；场景树继续支持多选、拖放、上下文菜单和展开状态；3D 宿主绑定和焦点事件不变。
- [x] 结构回归测试通过，并在 Dark、Light、HighContrastDark、HighContrastLight 下实例化全部 29 个用户可见界面。
- [x] 100%、125%、150% Windows 缩放下完成 348 张真实 WPF 截图和逐图检查，系统缩放最终恢复到 150%。
- [x] Kitbash、Skeleton、GameWorld 定向测试与完整 Release restore/build/test 全部通过，`git diff --check` 无错误。

## Implementation

### 1. Establish family contracts

- 新增 `Testing/AssetEditorTests/UiModelKitbashFamilyTests.cs`，固定 30 个产品 XAML 源、主题/字体资源、公共交互样式和关键行为契约。
- 新增 `Editors/Kitbashing/KitbasherEditor/KitbashUiStyles.xaml`，只做该家族的隐式到键控样式映射，不改变全局隐式样式。
- 将字典合并到 Kitbash 根工作区和独立子窗口；Skeleton 与 GameWorld 视图在自身资源中使用相同键控样式。

### 2. Migrate the workspace and dense property editors

- 主工作区建立清楚的菜单、垂直工具栏、3D 画布、时间轴、场景树和属性检查器层级。
- 场景树、空状态、上下文菜单和所选项使用语义色；展开图标保持垂直居中。
- 材质、网格、动画、骨骼和 BMI 属性页统一标题、分隔线、输入控件和滚动行为，不改业务绑定。

### 3. Migrate child windows and 3D utilities

- 迁移 Mesh Fitter、Photo Studio、Pin Tool、Re-rigging、Save、Vertex Debugger 和快捷键帮助窗口。
- 迁移 Visual Skeleton Editor 与 Shader Texture View；保留各自程序集边界，不让 Shared.Ui 反向依赖编辑器。
- 主要/次要/危险操作使用明确的 `AeButton.*` 角色，密集表格使用 `AeTable.*`。

### 4. Visual verification

- 新增 `Testing/AssetEditorTests/UiModelKitbashFamilyGallery.cs`，使用真实视图/窗口及安全的示例 DataContext 渲染代表性状态。
- 每个缩放档生成四主题截图与接触表；检查裁切、滚动、对齐、对比度、焦点、禁用和空状态。
- 发现问题后只修改本家族相关代码，重新生成受影响截图直至通过。

### 5. Gates and handoff

- 运行结构/画廊测试和 Kitbash、Skeleton、GameWorld 定向测试。
- 更新 `ae-ui-migration-ledger.md` 与总路线图，所有新增 XAML 必须进入清单。
- 运行完整 Release restore/build/test 与差异检查。
- 本地提交本批次；不推送、不创建 PR、不发布，然后直接进入 Phase 6.3。
