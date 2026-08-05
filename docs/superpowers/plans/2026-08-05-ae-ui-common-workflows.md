# AE UI Common Workflows Implementation Plan

**Goal:** 将设置、标准确认/错误对话框、启动加载和真实进度迁移到已批准的公共控件与语义主题系统，同时保持现有事务和模态行为。

**Authorization:** 用户已授权 Phase 1–7 连续本地实施、自动化视觉验证和本地提交；本批不推送、不创建 PR、不发布。

## Scope

### Create

- `AssetEditor/Themes/DesignSystem/Workflows.xaml`
- `Testing/AssetEditorTests/UiCommonWorkflowTests.cs`
- `Testing/AssetEditorTests/UiCommonWorkflowGallery.cs`

### Modify

- `AssetEditor/App.xaml`
- `AssetEditor/Language_Cn.json`
- `AssetEditor/Views/Settings/SettingsWindow.xaml`
- `AssetEditor/Views/Settings/SettingsView.xaml`
- `AssetEditor/Views/Startup/StartupPackLoadingWindow.xaml`
- `AssetEditor/Views/FolderProject/FolderProjectProgressWindow.xaml`
- `Shared/SharedUI/Common/OperationProgress/OperationProgressView.xaml`
- `Shared/SharedUI/BaseDialogs/StandardDialog/MessageDialogWindow.xaml`
- `Shared/SharedUI/BaseDialogs/StandardDialog/Text/TextInputWindow.xaml`
- `Shared/SharedUI/BaseDialogs/StandardDialog/Text/TitleDescriptionInputWindow.xaml`
- `Shared/SharedUI/BaseDialogs/StandardDialog/PackFile/PackFileBrowserWindow.xaml`
- `Shared/SharedUI/BaseDialogs/StandardDialog/PackFile/SavePackFileWindow.xaml`
- `Shared/SharedUI/BaseDialogs/StandardDialog/ErrorDialog/ErrorListWindow.xaml`
- `Shared/SharedUI/BaseDialogs/StandardDialog/ErrorDialog/ErrorListView.xaml`
- `Shared/SharedUI/BaseDialogs/StandardDialog/ExceptionHandling/CustomExceptionWindow.xaml`
- `Shared/SharedUI/BaseDialogs/StandardDialog/ExceptionHandling/CustomExceptionWindow.xaml.cs`
- `Shared/SharedUI/BaseDialogs/StandardDialog/StandardDialogs.cs`
- `Testing/AssetEditorTests/WpfTestApplicationHost.cs`
- `Testing/AssetEditorTests/UiDesignSystemResourceTests.cs`
- affected existing settings, dialog, loading, and progress tests
- `docs/superpowers/plans/ae-ui-migration-ledger.md`

## Behavior Contract

- 设置主题、字体和视口选项继续即时预览；取消、关闭和 Escape 均完整恢复原状态。
- 保存继续只持久化现有设置；需要重启时仍显示明确提示。
- 标准对话框继续使用现有返回值、默认按钮和取消按钮，并统一以当前主窗口为 Owner。
- 错误窗口继续支持复制、关闭和强制关闭；错误详情保持可读且默认不抢占主要操作。
- 启动加载和文件夹工程进度继续显示真实阶段、当前详情、完成数和详情历史。
- 加载失败继续提供重试、检查游戏路径和退出；失败期间主窗口保持不可交互。

## Execution

1. 添加工作流资源、结构、Owner 和事务契约测试。
2. 添加键控设置导航、对话框和进度样式，并按顺序接入应用与 WPF 测试宿主。
3. 迁移设置五个分类和底部操作区到公共控件。
4. 迁移标准消息、文本输入、Pack 浏览、错误详情和异常窗口；保持现有事件与返回值。
5. 迁移启动加载、文件夹工程进度和 `OperationProgressView`；验证失败/重试和真实阶段。
6. 在三档缩放和四主题下捕获设置、确认、错误、加载与失败状态并逐张检查。
7. 运行设置事务、Owner/模态、加载/失败/重试、完整 Release 构建、完整测试和 `git diff --check`，更新迁移清单并本地提交。

## Exit Gate

- 设置即时预览和取消恢复测试通过，字体与主题资源完整恢复。
- 所有标准工作流无硬编码主题背景，按钮层级、焦点、默认/取消行为清晰。
- Owner/模态性、加载失败/重试及真实进度测试通过。
- 三档缩放与四主题无裁切、重叠、低对比或意外滚动条。
- 推送、PR、发布和 Phase 8 长期规范均未执行。
