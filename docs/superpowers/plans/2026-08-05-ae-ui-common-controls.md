# AE UI Common Controls Implementation Plan

**Goal:** 在不影响未迁移页面的前提下，建立可选择使用的石墨蓝公共控件家族，并以真实 WPF 渲染验证完整状态、主题、DPI 与已批准对齐规则。

**Authorization:** 用户已授权 Phase 1–7 连续本地实施、自动化视觉验证和本地提交；本批不推送、不创建 PR、不发布。

## Scope

### Create

- `AssetEditor/Themes/DesignSystem/Controls/Buttons.xaml`
- `AssetEditor/Themes/DesignSystem/Controls/Inputs.xaml`
- `AssetEditor/Themes/DesignSystem/Controls/Collections.xaml`
- `AssetEditor/Themes/DesignSystem/Controls/MenusAndFeedback.xaml`
- `Testing/AssetEditorTests/UiCommonControlResourceTests.cs`
- `Testing/AssetEditorTests/UiCommonControlGallery.cs`

### Modify

- `AssetEditor/App.xaml`
- `Testing/AssetEditorTests/WpfTestApplicationHost.cs`
- `Testing/AssetEditorTests/UiDesignSystemResourceTests.cs`
- `docs/superpowers/plans/ae-ui-migration-ledger.md`

## Resource Contract

- Buttons: `AeButton.Primary`, `AeButton.Secondary`, `AeButton.Quiet`, `AeButton.Danger`, `AeButton.Icon`
- Inputs: `AeInput.TextBox`, `AeInput.ComboBox`, `AeInput.CheckBox`, `AeInput.RadioButton`, `AeInput.Switch`, `AeValidation.Message`
- Navigation and collections: `AeTab.Item`, `AeTag.Container`, `AeTag.Text`, `AeTree.View`, `AeTree.Item`, `AeList.View`, `AeList.Item`, `AeTable.Grid`, `AeTable.Header`, `AeTable.Row`, `AeTable.Cell`
- Menus and feedback: `AeMenu.Bar`, `AeMenu.Item`, `AeMenu.Context`, `AeToolTip`, `AeFeedback.Notice`, `AeFeedback.Icon`, `AeFeedback.SuccessIcon`, `AeFeedback.WarningIcon`, `AeFeedback.DangerIcon`, `AeEmptyState.Panel`, `AeEmptyState.Title`, `AeEmptyState.Description`, `AeProgress.Bar`, `AeScrollBar.Compact`

所有样式必须有明确键，不得替换旧隐式样式。颜色只使用 `AeBrush.*`，尺寸只使用批准的 `AeSize.*`、`AeSpace.*` 和 `AeRadius.*`。

## Execution

1. 添加失败资源契约测试，证明样式和资源加载顺序尚未存在。
2. 最小实现四个控件资源字典，并接入应用和 WPF 测试宿主。
3. 实例化每个样式，验证默认、悬停、按下、键盘焦点、禁用、选中、校验错误及空集合状态所需触发器。
4. 验证树展开箭头使用固定矢量图标和独立居中盒，反馈图标相对整条通知垂直居中。
5. 用测试画廊在 Dark、Light、HighContrastDark、HighContrastLight 下渲染公共控件；分别以 100%、125%、150% 捕获并人工检查截图。
6. 运行 `AssetEditorTests`、Release 构建、`git diff --check`，更新迁移清单并本地提交。

## Exit Gate

- 新样式全部键控且四主题可实例化。
- 箭头和通知图标在三档缩放下垂直居中。
- 控件无裁切、错位、低对比度或伪造状态。
- 未迁移业务页面继续使用旧隐式样式。
- 推送、PR、发布和 Phase 8 长期规范均未执行。
