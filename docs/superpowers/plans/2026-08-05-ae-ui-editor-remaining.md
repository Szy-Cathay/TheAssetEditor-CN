# AE UI Phase 6.4: Remaining Editors and Shared Dialogs

**Goal:** 将剩余用户可见的 Updater、CSC、导入导出、Texture、TWUI 与共享对话/属性控件迁移到已批准的石墨蓝设计系统，同时保持业务行为、绑定、命令、快捷键和窗口语义不变。

**Scope:** 27 个用户可见 XAML；CSC 的纯 C# `CurveEditorControl` 由 `CscEditorView` 的真实渲染覆盖。旧主题字典、兼容样式字典和其他纯 C# 控件留到 Phase 7 基于消费者证据处理。

## Exact product paths

- `AssetEditor/Views/Updater/UpdaterWindow.xaml`
- `Editors/CscEditor/Editors.CscEditor/Views/CscEditorView.xaml`
- `Editors/ImportExportEditor/Editors.ImportExport/Exporting/Presentation/DdsToMaterialPng/DdsToMaterialPngView.xaml`
- `Editors/ImportExportEditor/Editors.ImportExport/Exporting/Presentation/DdsToNormalPng/DdsToNormalPngView.xaml`
- `Editors/ImportExportEditor/Editors.ImportExport/Exporting/Presentation/DdsToPng/DdsToPngView.xaml`
- `Editors/ImportExportEditor/Editors.ImportExport/Exporting/Presentation/ExportWindow.xaml`
- `Editors/ImportExportEditor/Editors.ImportExport/Exporting/Presentation/RmvToGltf/RmvToGltfExporterView.xaml`
- `Editors/ImportExportEditor/Editors.ImportExport/Importing/Presentation/GltfToRmv/RmvToGltfImporterView.xaml`
- `Editors/ImportExportEditor/Editors.ImportExport/Importing/Presentation/ImportWindow.xaml`
- `Editors/TextureEditor/Views/TextureInformationView.xaml`
- `Editors/TextureEditor/Views/TexturePreviewView.xaml`
- `Editors/TwuiEditor/Editor.Twui/Editor/ComponentEditor/ComponentView.xaml`
- `Editors/TwuiEditor/Editor.Twui/Editor/Presentation/HierarchyView.xaml`
- `Editors/TwuiEditor/Editor.Twui/Editor/Presentation/TwuiMainView.xaml`
- `Shared/SharedUI/BaseDialogs/AeAttribute.xaml`
- `Shared/SharedUI/BaseDialogs/AeAttribute2.xaml`
- `Shared/SharedUI/BaseDialogs/ColourPickerButton/ColourPickerButtonView.xaml`
- `Shared/SharedUI/BaseDialogs/ControllerHostWindow.xaml`
- `Shared/SharedUI/BaseDialogs/FilterDialog/CollapsableFilterControl.xaml`
- `Shared/SharedUI/BaseDialogs/FilterDialog/FilterUserControl.xaml`
- `Shared/SharedUI/BaseDialogs/MathViews/Matrix3x4View.xaml`
- `Shared/SharedUI/BaseDialogs/MathViews/Vector2View.xaml`
- `Shared/SharedUI/BaseDialogs/MathViews/Vector3View.xaml`
- `Shared/SharedUI/BaseDialogs/MathViews/Vector4View.xaml`
- `Shared/SharedUI/BaseDialogs/SelectionListDialog/SelectionListView.xaml`
- `Shared/SharedUI/BaseDialogs/SelectionListDialog/SelectionListWindow.xaml`
- `Shared/SharedUI/BaseDialogs/ToolSelector/ToolSelectorWindow.xaml`

## Success criteria

- [x] 27 个根界面使用 `AppFontFamily`、`AppFontWeight` 和语义 Brush，并显式选择公共编辑器控件样式。
- [x] 固定主题颜色、`SystemColors`、`ABrush.*` 以及错误的 `AeFont.*` 引用在本批范围内清零。
- [x] Updater、导入/导出、选择、筛选、工具选择和窗口按钮保留原命令、默认/取消、Owner 与关闭行为。
- [x] CSC 时间线、属性表、曲线画布以及 Texture/TWUI 的树、表、预览与分隔布局在四主题下清晰可用。
- [x] 结构测试固定 27 个路径、语义资源、公共控件和关键行为契约。
- [x] 27 个真实视图/窗口在四主题下实例化并通过布局断言。
- [x] 100%、125%、150% Windows 缩放下生成并逐图检查 324 张真实 WPF 截图，最终恢复到 150%。
- [x] 相关定向测试、完整 Release restore/build/test、迁移清单覆盖测试和 `git diff --check` 全部通过。

## Implementation

### 1. Establish contracts

- [x] 新增 `Testing/AssetEditorTests/UiRemainingEditorFamilyTests.cs`，以失败测试固定精确路径、语义主题、字体、共享样式和事件/绑定契约。
- [x] 新增 `Testing/AssetEditorTests/UiRemainingEditorFamilyGallery.cs`，以安全测试数据构造全部 27 个真实界面，并通过 CSC 宿主覆盖 `CurveEditorControl`。

### 2. Migrate product families

- [x] 迁移 Updater、CSC、Texture 与 TWUI 根工作区；保留下载/安装、时间线、曲线、树选择、预览和分隔器行为。
- [x] 迁移导出/导入窗口与五个格式页面；保留格式切换、路径、校验、执行和关闭行为。
- [x] 迁移共享属性、颜色、筛选、数学、选择列表、ControllerHost 与 ToolSelector；不改变复用 API 或窗口生命周期。

### 3. Visual verification

- [x] 在 Dark、Light、HighContrastDark、HighContrastLight 下渲染 27 个变体。
- [x] 在 100%、125%、150% 真实 Windows 缩放分别生成 108 张截图和四张接触表。
- [x] 逐图检查裁切、滚动、箭头/图标居中、对比度、焦点、禁用、空状态、密集中文和窗口操作区；只修复本批相关问题并重拍。

### 4. Gates and handoff

- [x] 运行结构/画廊、CSC、ImportExport 与 Shared.Ui 相关定向测试。
- [x] 更新迁移清单，27 个 XAML 与 CSC 纯代码画布进入已迁移状态。
- [x] 运行完整 Release restore/build/test、迁移覆盖测试和差异检查。
- [x] 本地提交本批次；不推送、不创建 PR、不发布，然后直接进入 Phase 7。
