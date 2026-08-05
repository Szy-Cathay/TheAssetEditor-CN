# AE UI Pack and Git Workspace Implementation Plan

**Goal:** 将 Pack 文件树、文件夹工程创建、工作树、暂存区、分支、提交历史、合并和长操作反馈迁移到已批准的语义主题与公共控件系统，同时保持四层状态和本地 Git 行为不变。

**Authorization:** 用户已授权 Phase 1–7 连续本地实施、自动化视觉验证和本地提交；本批不推送、不创建 PR、不发布。

## Scope

### Create

- `Testing/AssetEditorTests/UiPackGitWorkspaceTests.cs`
- `Testing/AssetEditorTests/UiPackGitWorkspaceGallery.cs`

### Modify

- `AssetEditor/Views/FileTreeView.xaml`
- `AssetEditor/Views/FolderProject/FolderProjectSetupWindow.xaml`
- `AssetEditor/Views/FolderProject/FolderProjectSetupWindow.xaml.cs`
- `AssetEditor/UiCommands/FolderProjectImportDialogs.cs`
- `AssetEditor/UiCommands/CreateFolderProjectCommand.cs`
- `AssetEditor/UiCommands/ImportPackAsFolderProjectCommand.cs`
- `AssetEditor/Views/FolderProjectVersionControl/FolderProjectGitStyles.xaml`
- `AssetEditor/Views/FolderProjectVersionControl/FolderProjectGitPanelView.xaml`
- `AssetEditor/Views/FolderProjectVersionControl/FolderProjectGitRepositoryView.xaml`
- `AssetEditor/Views/FolderProjectVersionControl/FolderProjectVersionControlWindow.xaml`
- `Shared/SharedUI/BaseDialogs/PackFileTree/PackFileBrowserView.xaml`
- affected Pack tree, folder-project, Git workspace, progress, and migration-ledger tests
- `docs/superpowers/plans/ae-ui-migration-ledger.md`

## Behavior Contract

- 编辑器内存、工程磁盘工作树、Git index、提交历史和输出 Pack 继续严格分离。
- 同一路径的 staged 与 unstaged 差异继续分别显示；Stage 不清理工作树，Unstage 不丢弃磁盘内容。
- 分支切换的携带、暂存和丢弃三种选择及危险确认继续由现有命令决定。
- 提交只使用现有 `Commit staged` / `Commit all` 语义；不增加远程 Git 能力。
- 历史文件操作继续区分最新提交编辑、任意提交重置和反向提交。
- 状态扫描、提交详情、分支和合并进度继续来自真实服务；大列表保留虚拟化。
- 工程创建和路径校验行为不变；提示改用已有标准对话框，并保持当前主窗口 Owner。

## Execution

1. 添加语义资源、四层状态、虚拟化和标准对话框结构测试，先确认旧界面不满足新契约。
2. 将 Git 专用样式改为基于公共按钮、输入、树、列表、表格、菜单与语义颜色的键控样式。
3. 迁移工作树、暂存区、分支选择、提交区、历史详情、stash 与合并窗口。
4. 迁移 Pack 文件树、搜索、上下文菜单、未保存标记、刷新遮罩和工程创建窗口。
5. 用真实临时本地仓库建立 staged+unstaged、分支、历史、stash、合并和加载状态图库。
6. 在 100%、125%、150% 及 Dark、Light、HighContrastDark、HighContrastLight 下捕获并逐张检查。
7. 对真实大目录分别计时初始化、状态扫描、提交差异和 UI 数据投影；运行全部 Folder Project/Git 门禁、完整 Release 构建、完整测试和 `git diff --check`，更新迁移清单并本地提交。

## Exit Gate

- 四层状态语义、危险操作确认、分支和历史行为测试全部通过。
- Git 和 Pack 界面不再依赖旧主题画刷或硬编码主题颜色，公共控件状态与键盘焦点清晰。
- 三档缩放、四主题及窄侧栏无裁切、重叠、低对比或意外滚动条。
- 真实临时 Git 仓库完成 Stage、Unstage、Commit、Branch、History、Stash 和状态刷新验证。
- 大工程阶段计时分别记录，未用界面响应代替 Git 与磁盘性能结论。
- 推送、PR、发布和 Phase 8 长期规范均未执行。
