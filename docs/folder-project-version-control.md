# AI 上下文：文件夹工程与工程历史

## 何时读取

涉及文件夹工程、未保存/未记录状态、工程历史、底层本地仓库、路径安全、watcher、批量磁盘操作或大型工程性能时读取。

本文记录当前稳定语义和诊断边界，不是用户教程、LibGit2Sharp API 说明或历史实施计划。

## 硬约束

- 文件夹工程是磁盘支持的 Mod 工程，也是唯一面向用户的可编辑工作区；普通 Pack 不能成为用户工作区。
- 普通 Pack 只作为导入来源、只读参考或工程生成的输出。参考 Pack 的复制目标始终是当前文件夹工程，没有活动工程时写入类操作必须禁用并给出中文原因。
- 工程必须拥有普通目录形式的本地 `.git` 作为工程历史的当前存储实现；不支持 linked worktree、submodule、bare repository 或远端协作。普通用户界面不得要求用户理解或配置 Git。
- 当前配置文件是 `aeproject.cn.json`。可只读加载 `aeproject.json` 与 `project_ignore.json`；配置迁移必须是另一个明确获准的写入动作，且只能写回国区版配置。
- 输出 Pack 必须位于工程根目录之外。
- 工程资源只接受 `FolderProjectPathPolicy` 验证后的相对路径；控制文件、Git 元数据、重解析点逃逸、Windows 保留名和尾随点/空格不是资源。
- 底层仓库不跟踪空目录；空目录由 `aeproject.cn.json` 的 `EmptyDirectories` 保存和恢复，并随还原点记录配置文件。
- 面向普通流程的 `IFolderProjectHistoryService` 只提供状态、初始化、创建还原点、历史、还原点内容、异常恢复和放弃未记录修改；不提供身份、分支、暂存区、储藏、合并或远端能力。
- `FolderProjectVersionControlService` 与底层仓库模型只作为 `Shared.Core` 内部兼容/恢复实现；应用层和模块不能引用其高级接口，也不能重新接回普通工程历史界面。
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
| 恢复整个工程 | 有未记录修改时先创建安全还原点，再把所选还原点的完整内容作为新的当前还原点 | 不移动、不删除或重写旧还原点 |
| 恢复单个文件 | 把所选还原点中的文件版本写回工程磁盘并保留为未记录修改 | 不创建还原点、不隐式保存编辑器内存 |
| 放弃未记录修改 | 选定路径恢复到当前还原点；对应未记录新文件会被删除 | 不影响未选择路径、不创建还原点 |
| 恢复异常工程历史 | 保留当前磁盘文件，把未完成操作的内容转为普通未记录修改，并重新加载工程 | 不删除还原点、额外历史线、储藏、标签或其他引用；不访问网络 |

恢复整个工程、放弃全部未记录修改和恢复异常工程历史属于批量磁盘操作，必须通过 `FolderProjectGitOperationCoordinator` 关闭相关编辑器、卸载容器、执行核心事务并重新加载。单文件恢复不卸载整个工程，但写入后必须重新对账容器。这些操作都只在底层元数据目录保留本次操作所需的临时回滚状态；磁盘与界面刷新全部成功后立即清理。若写入、删除、历史更新或重新加载失败，必须先恢复操作前磁盘、还原点历史和底层 index，再重新加载原工程状态。

同一路径可以同时存在 staged 与 unstaged 底层差异；工程历史状态必须合并成一项“未记录修改”，创建还原点时必须记录两段合并后的最终磁盘内容，不能因隐藏 index 而漏记。

旧版 AE-CN 建立的普通本地仓库可以直接打开。打开、刷新、查看和退出只读取当前配置与历史状态，不规范化写回配置，不切换当前历史线，也不改写额外历史线、储藏、标签或其他引用；当前历史线名称不是 `master` 时仍按它原本的位置继续记录。未完成操作、冲突、游离历史、占用锁和不可读文件都必须阻止创建还原点、恢复和放弃修改。工程历史界面只显示中文原因和单一的“保留当前文件并恢复工程”入口，不暴露高级对象。

异常恢复支持保留外部合并或其他未完成操作留下的磁盘内容，并清理可安全退出的底层操作状态；游离历史会优先接回指向同一还原点的现有本地历史线，没有时创建一条内部恢复线。占用锁和不可读文件必须先由用户解除，linked worktree、submodule 和 bare repository 仍不支持。底层操作若不能被安全退出，恢复必须回滚 index、HEAD 和操作标记并继续保持门禁，不能假装恢复成功。

## 责任与入口

| 责任 | 当前入口 |
| --- | --- |
| 新建、导入、打开工程与打开参考 Pack | `AssetEditor/UiCommands/*FolderProjectCommand.cs`、`OpenReferencePackCommand.cs` |
| 当前工程、参考 Pack 角色与只读门禁 | `Shared/SharedCore/PackFiles/IPackFileService.cs`、`PackFileService.cs` |
| 最近工程与最近参考 Pack | `AssetEditor/Services/RecentFilesTracker.cs`、`AssetEditor/ViewModels/MenuBarViewModel.cs` |
| 磁盘资源树、空目录、watcher、指纹与对账 | `Shared/SharedCore/PackFiles/Models/FolderProjectContainer.cs` |
| 配置读取、显式迁移与规范化 | `Shared/SharedCore/PackFiles/Models/FolderProjectSettings.cs` |
| 路径、元数据、重解析点和输出位置安全 | `Shared/SharedCore/PackFiles/Utility/FolderProjectPathPolicy.cs` |
| 仓库初始化、二进制属性与临时文件忽略 | `Shared/SharedCore/PackFiles/Utility/FolderProjectGitRepository.cs` |
| 普通工程历史窄接口、状态与还原点映射 | `Shared/SharedCore/PackFiles/Utility/FolderProjectHistoryService.cs`、`FolderProjectHistoryModels.cs` |
| 底层仓库兼容与异常恢复实现（仅 `Shared.Core` 内部） | `Shared/SharedCore/PackFiles/Utility/FolderProjectVersionControlService*.cs` |
| 编辑器内存与磁盘/历史边界 | `AssetEditor/Services/FolderProjectUnsavedChangesService.cs` |
| 需要卸载/重载工程的 Git 与磁盘操作 | `AssetEditor/Services/FolderProjectGitOperationCoordinator.cs` |
| 主窗口工程历史投影和刷新 | `AssetEditor/ViewModels/FolderProjectHistoryViewModel.cs`、`FolderProjectHistoryWorkspaceViewModel.cs` |
| 普通工程历史中文界面 | `AssetEditor/Views/FolderProjectHistory/FolderProjectHistoryView.xaml` |
| 异常工程历史恢复窗口 | `AssetEditor/Services/FolderProjectHistoryWindowService.cs`、`FolderProjectHistoryWindow.xaml` |
| 退出前未记录状态保护 | `AssetEditor/Services/FolderProjectCloseGuard.cs` |

ViewModel 不应直接拼接批量文件系统操作或调用 `LibGit2Sharp.Commands`，否则会绕过路径策略、回滚、进度、容器重载和测试替身。

从最近工程再次打开一个已经加载的文件夹工程时，不重复创建容器，而是将原容器重新激活为当前工作区。最近参考 Pack 始终按只读参考角色重新加载，不能走这条激活路径。

从文件关联等价启动参数、IPC 或其他外部入口收到 `.pack` 时，应用先要求用户选择只读参考或导入工程。参考选择只增加只读容器，不替换当前工程；导入选择复用标准工程设置、路径策略、进度和工程历史初始化。导入来源的规范化绝对路径保存为 `aeproject.cn.json` 的 `SourcePackPath`，工程重新打开后仍可识别重复来源和角色冲突。用户取消用途、取消工程设置或导入失败时，当前工作区与容器列表保持原样。同一 Pack 已按另一角色加载时，不自动改变角色。

## watcher、刷新与批量操作

- `FolderProjectContainer` 递归监听工程目录。watcher 事件去抖后重新对账磁盘资源、空目录和文件指纹，再发布资源路径变化。
- 控制文件、`.git`、原子写入临时文件和损坏检测占位不进入资源树。
- 主窗口的工程历史面板打开时读取完整未记录状态；文件变化事件只把刷新排入当前工程，历史操作进行中时等待完成后再刷新。
- 恢复整个工程、放弃全部未记录修改和异常恢复通过 `FolderProjectGitOperationCoordinator`：记录 UI/空目录状态，关闭相关编辑器并卸载容器，执行操作，再重新打开和刷新。
- 合并中的阶段由 `FolderProjectMergeSession` 保存；任何未完成操作或游离历史必须先进入专用工程历史恢复流程，不能直接当作普通可编辑工程。

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
- 永久放弃未记录修改必须有明确中文确认；恢复和异常恢复失败时必须保持原工程状态或继续阻止打开。
- 输出 Pack、当前已加载 Pack、工程根和活动容器之间不能形成路径冲突。

## 验证路由

核心容器、watcher、路径和 Git 服务：

- `Testing/Shared.Core.Test/PackFiles/FolderProjectContainerTests.cs`
- `Testing/Shared.Core.Test/PackFiles/FolderProjectWatcherTests.cs`
- `Testing/Shared.Core.Test/PackFiles/FolderProjectPathPolicyTests.cs`
- `Testing/Shared.Core.Test/PackFiles/FolderProjectGitFoundationTests.cs`
- `Testing/Shared.Core.Test/PackFiles/FolderProjectLegacyRepositoryRecoveryTests.cs`
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
- `Testing/AssetEditorTests/FolderProjectHistoryWorkspaceViewModelTests.cs`
- `Testing/AssetEditorTests/FolderProjectHistoryArchitectureTests.cs`
- `Testing/AssetEditorTests/FolderProjectGitOperationCoordinatorTests.cs`
- `Testing/AssetEditorTests/FolderProjectCloseGuardTests.cs`
- `Testing/AssetEditorTests/FolderProjectTreeStateTests.cs`

状态语义变化先建立行为测试。真实窗口、watcher 时序、长操作或大型目录还需要 Release 应用实测；单元测试不能替代视觉、并发和性能验收。
