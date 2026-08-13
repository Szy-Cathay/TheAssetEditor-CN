# AI 上下文：文件夹工程与本地 Git

## 何时读取

涉及文件夹工程、未保存/未提交状态、本地 Git、路径安全、watcher、批量磁盘操作或大型工程性能时读取。

本文记录当前稳定语义和诊断边界，不是用户教程、LibGit2Sharp API 说明或历史实施计划。

## 硬约束

- 文件夹工程是磁盘支持的 Mod 工程，也是唯一面向用户的可编辑工作区；普通 Pack 不能成为用户工作区。
- 普通 Pack 只作为导入来源、只读参考或工程生成的输出。参考 Pack 的复制目标始终是当前文件夹工程，没有活动工程时写入类操作必须禁用并给出中文原因。
- 工程必须拥有普通目录形式的本地 `.git`；不支持 linked worktree、submodule、bare repository 或远端协作。
- 当前配置文件是 `aeproject.cn.json`。可读取 `aeproject.json` 与 `project_ignore.json` 以迁移，但只写回国区版配置。
- 输出 Pack 必须位于工程根目录之外。
- 工程资源只接受 `FolderProjectPathPolicy` 验证后的相对路径；控制文件、Git 元数据、重解析点逃逸、Windows 保留名和尾随点/空格不是资源。
- Git 不跟踪空目录；空目录由 `aeproject.cn.json` 的 `EmptyDirectories` 保存和恢复。
- 本地 Git 公共服务不提供 push、pull、fetch 或远端分支能力。

## 状态分层

| 层 | 事实来源 | 由什么改变 | 不能假装成 |
| --- | --- | --- | --- |
| 编辑器内存 | 打开的 `ISaveableEditor` | 用户编辑、保存或放弃 | Git 工作树或提交 |
| 工程磁盘 / Git 工作树 | 工程根目录文件 | 编辑器保存、外部工具、Git 恢复/合并 | 未保存编辑 |
| Git index | 工程自己的 `.git` | Stage / Unstage | 干净工作树或提交 |
| 本地历史 / stash | 当前仓库对象与引用 | Commit、Stash、分支、合并、重置 | 输出 Pack 或云备份 |
| 输出 Pack | 工程外 `.pack` | 从当前磁盘内容生成 | 保存编辑器、Stage 或 Commit |

`FolderProjectUnsavedChangesService` 在 Stage 或全部提交前检查编辑器内存。用户选择“不保存”时，Git 操作只能处理磁盘已有内容。

## 操作语义

| 操作 | 改变 | 明确不做 |
| --- | --- | --- |
| 保存编辑器 | 内存 → 工程磁盘 | 不 Stage、不 Commit、不生成 Pack |
| 生成 Pack | 工程磁盘快照 → 工程外 `.pack` | 不保存内存、不改变 Git |
| Stage | 工作树差异 → index | 不清理磁盘、不创建提交 |
| Unstage | 移除 index 中的选择 | 不丢弃磁盘内容 |
| Commit staged | index → 当前分支历史 | 不自动包含未暂存或未保存内容 |
| Commit all | 先确认内存状态，再提交磁盘全部变化 | 不强制保存用户拒绝保存的编辑 |
| Discard | 选定路径恢复到 HEAD，并清理对应 index/工作树差异 | 不保留被丢弃的新文件；失败必须回滚 |
| Stash | 工作树与 index → stash | 不创建普通提交 |
| Apply / Pop stash | stash → 干净工作树 | Apply 不删除 stash；Pop 仅在成功后删除 |
| Revert commit | 创建反向提交 | 不重写旧历史 |
| Reset keep changes | 移动分支并保留后续内容为未暂存修改 | 会改写当前分支可见历史 |

同一路径可以同时存在 staged 与 unstaged 差异；UI 必须同时表达两段真实差异，不能为“去重”丢掉一段。

## 责任与入口

| 责任 | 当前入口 |
| --- | --- |
| 新建、导入、打开工程与打开参考 Pack | `AssetEditor/UiCommands/*FolderProjectCommand.cs`、`OpenReferencePackCommand.cs` |
| 当前工程、参考 Pack 角色与只读门禁 | `Shared/SharedCore/PackFiles/IPackFileService.cs`、`PackFileService.cs` |
| 最近工程与最近参考 Pack | `AssetEditor/Services/RecentFilesTracker.cs`、`AssetEditor/ViewModels/MenuBarViewModel.cs` |
| 磁盘资源树、空目录、watcher、指纹与对账 | `Shared/SharedCore/PackFiles/Models/FolderProjectContainer.cs` |
| 配置读取、旧配置迁移与规范化 | `Shared/SharedCore/PackFiles/Models/FolderProjectSettings.cs` |
| 路径、元数据、重解析点和输出位置安全 | `Shared/SharedCore/PackFiles/Utility/FolderProjectPathPolicy.cs` |
| 仓库初始化、二进制属性与临时文件忽略 | `Shared/SharedCore/PackFiles/Utility/FolderProjectGitRepository.cs` |
| 状态、提交、恢复、stash、分支与合并 | `Shared/SharedCore/PackFiles/Utility/FolderProjectVersionControlService*.cs` |
| 编辑器内存与磁盘/Git 边界 | `AssetEditor/Services/FolderProjectUnsavedChangesService.cs` |
| 需要卸载/重载工程的 Git 与磁盘操作 | `AssetEditor/Services/FolderProjectGitOperationCoordinator.cs` |
| 主窗口文件树/Git 投影和刷新 | `AssetEditor/ViewModels/FolderProjectGitWorkspaceViewModel.cs` |
| 版本控制操作、选择和中文反馈 | `AssetEditor/ViewModels/FolderProjectVersionControlViewModel.cs` |
| 退出前未提交状态保护 | `AssetEditor/Services/FolderProjectCloseGuard.cs` |

ViewModel 不应直接拼接批量文件系统操作或调用 `LibGit2Sharp.Commands`，否则会绕过路径策略、回滚、进度、容器重载和测试替身。

从最近工程再次打开一个已经加载的文件夹工程时，不重复创建容器，而是将原容器重新激活为当前工作区。最近参考 Pack 始终按只读参考角色重新加载，不能走这条激活路径。

## watcher、刷新与批量操作

- `FolderProjectContainer` 递归监听工程目录。watcher 事件去抖后重新对账磁盘资源、空目录和文件指纹，再发布资源路径变化。
- 控制文件、`.git`、原子写入临时文件和损坏检测占位不进入资源树。
- 主窗口优先按累计路径局部刷新 Git 状态；路径超过 `MaxIncrementalStatusPaths` 或事件要求重载时，退化为完整刷新，不能忽略多余路径。具体阈值读取当前代码。
- 分支切换、重置、恢复、合并等会批量改写磁盘的操作通过 `FolderProjectGitOperationCoordinator`：记录 UI/空目录状态，关闭相关编辑器并卸载容器，执行操作，再重新打开和刷新。
- 合并中的阶段由 `FolderProjectMergeSession` 保存；未完成或可恢复的合并必须先进入合并处理，不能直接当作普通可编辑工程。

## 诊断台账

复现不一致时，分别记录下列状态，并指出最先发生分叉的一层：

| 层 | 至少记录 |
| --- | --- |
| 编辑器 | 是否脏、是否保存、打开的资源与容器 |
| 磁盘 | 文件存在性、内容、路径、空目录、配置文件 |
| Git | HEAD/分支、工作树、index、stash、merge state |
| Pack 树 | 容器、资源路径、节点是否重复或缺失 |
| Git UI | staged/unstaged/history/branch 投影及刷新时间 |

不要用“界面看起来正确”替代磁盘和 Git 证据。大型工程还应分别计时：状态扫描、提交差异、index/分支/合并、磁盘写删移、容器重新对账、树与 Git UI 刷新。

## 数据与路径安全

- 路径按 Windows 大小写不敏感规则比较，但必须先规范化并验证仍位于工程根下。
- `.gitattributes` 将资源视为二进制，避免换行转换破坏游戏文件。
- 写入和丢弃优先使用同目录临时文件、原子移动和失败回滚；不要先直接覆盖再补救。
- 永久丢弃、重置、删除 stash、删除分支和历史改写需要明确中文确认；主分支不能删除或重命名。
- 输出 Pack、当前已加载 Pack、工程根和活动容器之间不能形成路径冲突。

## 验证路由

核心容器、watcher、路径和 Git 服务：

- `Testing/Shared.Core.Test/PackFiles/FolderProjectContainerTests.cs`
- `Testing/Shared.Core.Test/PackFiles/FolderProjectWatcherTests.cs`
- `Testing/Shared.Core.Test/PackFiles/FolderProjectPathPolicyTests.cs`
- `Testing/Shared.Core.Test/PackFiles/FolderProjectVersionControl*Tests.cs`

应用协调、内存边界和 UI 状态：

- `Testing/AssetEditorTests/CreateFolderProjectCommandTests.cs`
- `Testing/AssetEditorTests/ImportPackAsFolderProjectCommandTests.cs`
- `Testing/AssetEditorTests/OpenFolderProjectCommandTests.cs`
- `Testing/AssetEditorTests/OpenReferencePackCommandTests.cs`
- `Testing/AssetEditorTests/MenuBarFolderProjectRecentTests.cs`
- `Testing/AssetEditorTests/RecentFilesTrackerTests.cs`
- `Testing/AssetEditorTests/FolderProjectUnsavedChangesServiceTests.cs`
- `Testing/AssetEditorTests/FolderProjectGitOperationCoordinatorTests.cs`
- `Testing/AssetEditorTests/FolderProjectGitWorkspaceViewModelTests.cs`
- `Testing/AssetEditorTests/FolderProjectVersionControlViewModelTests.cs`
- `Testing/AssetEditorTests/FolderProjectCloseGuardTests.cs`
- `Testing/AssetEditorTests/FolderProjectTreeStateTests.cs`

状态语义变化先建立行为测试。真实窗口、watcher 时序、长操作或大型目录还需要 Release 应用实测；单元测试不能替代视觉、并发和性能验收。
