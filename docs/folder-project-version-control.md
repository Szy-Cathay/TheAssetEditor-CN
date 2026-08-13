# AI 上下文：文件夹工程与工程历史

## 何时读取

涉及文件夹工程、未保存/未记录状态、工程历史、底层本地仓库、路径安全、watcher、批量磁盘操作或大型工程性能时读取。

本文记录当前稳定语义和诊断边界，不是用户教程、LibGit2Sharp API 说明或历史实施计划。

## 硬约束

- 文件夹工程是磁盘支持的 Mod 工程，也是唯一面向用户的可编辑工作区；普通 Pack 不能成为用户工作区。
- 普通 Pack 只作为导入来源、只读参考或工程生成的输出。参考 Pack 的复制目标始终是当前文件夹工程，没有活动工程时写入类操作必须禁用并给出中文原因。
- 工程必须拥有普通目录形式的本地 `.git` 作为工程历史的当前存储实现；不支持 linked worktree、submodule、bare repository 或远端协作。普通用户界面不得要求用户理解或配置 Git。
- 当前配置文件是 `aeproject.cn.json`。可读取 `aeproject.json` 与 `project_ignore.json` 以迁移，但只写回国区版配置。
- 输出 Pack 必须位于工程根目录之外。
- 工程资源只接受 `FolderProjectPathPolicy` 验证后的相对路径；控制文件、Git 元数据、重解析点逃逸、Windows 保留名和尾随点/空格不是资源。
- 底层仓库不跟踪空目录；空目录由 `aeproject.cn.json` 的 `EmptyDirectories` 保存和恢复，并随还原点记录配置文件。
- 面向普通流程的 `IFolderProjectHistoryService` 只提供状态、初始化、创建还原点、历史和还原点内容；不提供身份、分支、暂存区、储藏、合并或远端能力。
- `IFolderProjectVersionControlService` 的高级能力当前只为后续迁移和异常仓库恢复保留，不能重新接回普通工程历史界面。
- 工程历史和工程资源位于同一磁盘；它能处理本地误改，但不是云备份，不能抵御磁盘损坏或工程目录整体丢失。

## 状态分层

| 层 | 事实来源 | 由什么改变 | 不能假装成 |
| --- | --- | --- | --- |
| 编辑器内存 | 打开的 `ISaveableEditor` | 用户编辑、保存或放弃 | 未记录修改或还原点 |
| 工程磁盘 / 未记录修改 | 工程根目录文件 | 编辑器保存、外部工具、历史恢复 | 未保存编辑或还原点 |
| 还原点历史 | 当前工程历史对象 | 创建还原点 | 输出 Pack、编辑器撤销或云备份 |
| 底层 index | 工程自己的 `.git` | 仅底层实现和兼容操作 | 普通用户可选择的状态 |
| 输出 Pack | 工程外 `.pack` | 从当前磁盘内容生成 | 保存编辑器或创建还原点 |

`FolderProjectUnsavedChangesService` 在创建还原点前检查编辑器内存。用户可以“保存并创建还原点”“仅记录磁盘内容”或取消；提示必须明确工程历史不能保护仍停留在内存中的修改。

## 操作语义

| 操作 | 改变 | 明确不做 |
| --- | --- | --- |
| 保存编辑器 | 内存 → 工程磁盘 | 不创建还原点、不生成 Pack |
| 生成 Pack | 工程磁盘快照 → 工程外 `.pack` | 不保存内存、不创建还原点 |
| 刷新工程历史 | 读取磁盘全部未记录修改和最近还原点 | 不改写磁盘或创建还原点 |
| 创建还原点 | 先对账工程磁盘和空目录配置，再记录所有磁盘修改 | 不只记录底层 index 的局部选择；无修改时不创建重复还原点 |
| 查看还原点内容 | 按需读取该还原点相对上一个还原点的文件变化 | 不恢复或改写当前工程 |

以下操作只描述仍保留的高级兼容/恢复实现，不属于普通工程历史界面：

| Stage | 工作树差异 → index | 不清理磁盘、不创建提交 |
| Unstage | 移除 index 中的选择 | 不丢弃磁盘内容 |
| Commit staged | index → 当前分支历史 | 不自动包含未暂存或未保存内容 |
| Commit all | 先确认内存状态，再提交磁盘全部变化 | 不强制保存用户拒绝保存的编辑 |
| Discard | 选定路径恢复到 HEAD，并清理对应 index/工作树差异 | 不保留被丢弃的新文件；失败必须回滚 |
| Stash | 工作树与 index → stash | 不创建普通提交 |
| Apply / Pop stash | stash → 干净工作树 | Apply 不删除 stash；Pop 仅在成功后删除 |
| Revert commit | 创建反向提交 | 不重写旧历史 |
| Reset keep changes | 移动分支并保留后续内容为未暂存修改 | 会改写当前分支可见历史 |

同一路径可以同时存在 staged 与 unstaged 底层差异；工程历史状态必须合并成一项“未记录修改”，创建还原点时必须记录两段合并后的最终磁盘内容，不能因隐藏 index 而漏记。

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
| 普通工程历史窄接口、状态与还原点映射 | `Shared/SharedCore/PackFiles/Utility/FolderProjectHistoryService.cs`、`FolderProjectHistoryModels.cs` |
| 状态、提交、恢复、stash、分支与合并 | `Shared/SharedCore/PackFiles/Utility/FolderProjectVersionControlService*.cs` |
| 编辑器内存与磁盘/历史边界 | `AssetEditor/Services/FolderProjectUnsavedChangesService.cs` |
| 需要卸载/重载工程的 Git 与磁盘操作 | `AssetEditor/Services/FolderProjectGitOperationCoordinator.cs` |
| 主窗口工程历史投影和刷新 | `AssetEditor/ViewModels/FolderProjectHistoryViewModel.cs`、`FolderProjectGitWorkspaceViewModel.cs` |
| 普通工程历史中文界面 | `AssetEditor/Views/FolderProjectHistory/FolderProjectHistoryView.xaml` |
| 高级兼容/恢复操作 | `AssetEditor/ViewModels/FolderProjectVersionControlViewModel.cs` |
| 退出前未记录状态保护 | `AssetEditor/Services/FolderProjectCloseGuard.cs` |

ViewModel 不应直接拼接批量文件系统操作或调用 `LibGit2Sharp.Commands`，否则会绕过路径策略、回滚、进度、容器重载和测试替身。

从最近工程再次打开一个已经加载的文件夹工程时，不重复创建容器，而是将原容器重新激活为当前工作区。最近参考 Pack 始终按只读参考角色重新加载，不能走这条激活路径。

从文件关联等价启动参数、IPC 或其他外部入口收到 `.pack` 时，应用先要求用户选择只读参考或导入工程。参考选择只增加只读容器，不替换当前工程；导入选择复用标准工程设置、路径策略、进度和工程历史初始化。导入来源的规范化绝对路径保存为 `aeproject.cn.json` 的 `SourcePackPath`，工程重新打开后仍可识别重复来源和角色冲突。用户取消用途、取消工程设置或导入失败时，当前工作区与容器列表保持原样。同一 Pack 已按另一角色加载时，不自动改变角色。

## watcher、刷新与批量操作

- `FolderProjectContainer` 递归监听工程目录。watcher 事件去抖后重新对账磁盘资源、空目录和文件指纹，再发布资源路径变化。
- 控制文件、`.git`、原子写入临时文件和损坏检测占位不进入资源树。
- 主窗口的工程历史面板打开时读取完整未记录状态；文件变化事件只把刷新排入当前工程，历史操作进行中时等待完成后再刷新。
- 分支切换、重置、恢复、合并等会批量改写磁盘的操作通过 `FolderProjectGitOperationCoordinator`：记录 UI/空目录状态，关闭相关编辑器并卸载容器，执行操作，再重新打开和刷新。
- 合并中的阶段由 `FolderProjectMergeSession` 保存；未完成或可恢复的合并必须先进入合并处理，不能直接当作普通可编辑工程。

## 诊断台账

复现不一致时，分别记录下列状态，并指出最先发生分叉的一层：

| 层 | 至少记录 |
| --- | --- |
| 编辑器 | 是否脏、是否保存、打开的资源与容器 |
| 磁盘 | 文件存在性、内容、路径、空目录、配置文件 |
| 工程历史 | 当前还原点、未记录修改、还原点顺序和内容摘要 |
| 底层仓库 | HEAD/分支、工作树、index、stash、merge state；仅用于证明实现或诊断异常 |
| Pack 树 | 容器、资源路径、节点是否重复或缺失 |
| 工程历史 UI | 未记录修改、还原点、所选还原点内容及刷新时间 |

不要用“界面看起来正确”替代磁盘和底层仓库证据。大型工程还应分别计时：状态扫描、还原点差异、磁盘写删移、容器重新对账、资源树与工程历史 UI 刷新；高级恢复任务再额外记录 index、分支和合并阶段。

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
- `Testing/Shared.Core.Test/PackFiles/FolderProjectHistoryServiceTests.cs`

应用协调、内存边界和 UI 状态：

- `Testing/AssetEditorTests/CreateFolderProjectCommandTests.cs`
- `Testing/AssetEditorTests/ImportPackAsFolderProjectCommandTests.cs`
- `Testing/AssetEditorTests/OpenFolderProjectCommandTests.cs`
- `Testing/AssetEditorTests/OpenReferencePackCommandTests.cs`
- `Testing/AssetEditorTests/MenuBarFolderProjectRecentTests.cs`
- `Testing/AssetEditorTests/RecentFilesTrackerTests.cs`
- `Testing/AssetEditorTests/FolderProjectUnsavedChangesServiceTests.cs`
- `Testing/AssetEditorTests/FolderProjectHistoryViewModelTests.cs`
- `Testing/AssetEditorTests/FolderProjectGitOperationCoordinatorTests.cs`
- `Testing/AssetEditorTests/FolderProjectGitWorkspaceViewModelTests.cs`
- `Testing/AssetEditorTests/FolderProjectVersionControlViewModelTests.cs`
- `Testing/AssetEditorTests/FolderProjectCloseGuardTests.cs`
- `Testing/AssetEditorTests/FolderProjectTreeStateTests.cs`

状态语义变化先建立行为测试。真实窗口、watcher 时序、长操作或大型目录还需要 Release 应用实测；单元测试不能替代视觉、并发和性能验收。
