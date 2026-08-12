# AI 上下文：总体架构

## 何时读取

仅在任务涉及跨模块定位、依赖注入、编辑器注册、Pack 生命周期、共享 UI、更新器、本地化或测试范围时读取。

本文只记录稳定入口和非显然边界，不是完整项目引用图、类清单或启动教程。精确依赖读取当前 `.csproj`；精确行为读取当前代码和测试。

## 模块定位

| 区域 | 稳定职责 | 首要入口 |
| --- | --- | --- |
| `AssetEditor/` | WPF 主程序、组合根、主窗口、命令与应用级协调 | `App.xaml.cs`、`Services/DependencyInjectionConfig.cs` |
| `Shared/SharedCore/` | Pack 服务、文件夹工程、事件、设置、日志与工具注册 | `PackFiles/PackFileService.cs`、`DependencyInjectionContainer.cs` |
| `Shared/GameFiles/` | Total War 格式模型与读写 | `Shared.GameFormats.csproj` |
| `Shared/SharedUI/` | 跨模块且无业务含义的 WPF 组件 | `Shared.Ui.csproj`、`DependencyInjectionContainer.cs` |
| `Editors/` | 音频、Kitbash、动画、纹理、元数据、导入导出、报表等编辑器 | 各模块 `DependencyInjectionContainer.cs` |
| `GameWorld/View3D/` | 3D 场景、渲染、选择与变换 | `GameWorld.Core.csproj`、`DependencyInjectionContainer.cs` |
| `AssetEditorUpdater/` | 更新下载后的校验、事务安装、备份和回滚 | `AssetEditorUpdater.cs`、`UpdateInstaller.cs` |
| `Testing/` 与模块测试项目 | 架构、格式、WPF、更新器、渲染与 E2E 门禁 | `AssetEditor.CN.sln` |

不要根据上表推断直接项目引用；以当前 `.csproj` 为准。

## 必须保持的契约

### 组合根与编辑器

- `AssetEditor/Services/DependencyInjectionConfig.cs` 组合 Shared、GameWorld、Editors 和主程序服务。
- 各模块通过自己的 `DependencyInjectionContainer` 注册服务，通过 `RegisterTools` 向 `IEditorDatabase` 注册编辑器；不要在 View 或业务类中创建第二套容器。
- `Shared/SharedCore/ToolCreation/EditorDatabase.cs` 保存编辑器注册，`AssetEditor/Services/EditorManager.cs` 管理标签页、窗口、作用域和关闭流程。
- 保存失败或用户取消时必须保留编辑器脏状态。WPF `Closing` 回调中不要在同一调用栈再次同步 `Close()`。

### Shared UI

- `Shared.Ui` 只能引用 `Shared/` 内项目，不能反向依赖 `AssetEditor`、`Editors` 或具体编辑器。
- 进入 `Shared.Ui` 的组件必须跨模块、无业务含义、职责稳定且可独立验证；名称相同不足以证明应共享。
- 通用对话框优先使用 `IStandardDialogs` 等现有抽象。
- 自动门禁：`Testing/AssetEditorTests/SharedUiArchitectureTests.cs`。

### Pack 与编辑器文件身份

- `Shared/SharedCore/PackFiles/PackFileService.cs` 管理已加载容器和当前可编辑容器。
- CA Pack 只读；普通用户 Pack 在内存中修改并以临时文件替换方式保存；`FolderProjectContainer` 的资源修改直接落盘。
- `PackFile` 的身份必须结合所属容器和资源路径解析。跨容器查找、保存或错误报告不能只按文件名猜测；先检查 `GetPackFileContainer` 与 `GetFullPath`。
- 文件夹工程的内存、磁盘、Git 与输出 Pack 语义见 [`folder-project-version-control.md`](folder-project-version-control.md)。

### 中文资源与 WPF

- 用户可见文本统一进入 `AssetEditor/Language_Cn.json`，由 `Shared/SharedCore/Services/LocalizationManager.cs` 加载。
- 主题资源位于 `AssetEditor/Themes/`。独立视图和测试不能假设 `App.xaml` 已自动加载全部资源。
- 涉及 `Application`、`ResourceDictionary` 或 `Freezable` 的测试使用 `Testing/AssetEditorTests/WpfTestApplicationHost.cs` 的单一 STA/Dispatcher 宿主。

### 更新器

- 主程序只启动发布包内的独立更新器；国区版运行时身份以根 `AGENTS.md` 为准。
- 主程序从 Gitee `szy-cathay/AssetEditor-CN-Downloads` 检查版本；更新器读取同一发行版中的清单，按清单顺序下载小于 Gitee 单附件限制的 ZIP 分片，合并后校验每个分片和完整 ZIP 的 SHA-256。
- 更新安装必须验证安装目录与事务目录不重叠，拒绝重解析点、路径别名重叠和非自有事务工作区。
- updater payload 复制后逐文件复核 SHA-256；安装在事务目录完成暂存、备份和替换，失败时尝试回滚并保留原始错误。
- 发布布局同时由 `AssetEditor/AssetEditor.csproj` 的 `PublishUpdater` 目标和 `AssetEditor/Properties/PublishProfiles/FolderProfile.pubxml` 决定。

### IPC

`Editors/Ipc` 提供当前 Windows 用户可用的有界 JSON 行命名管道，只支持 `open` 动作。协议、限制和身份见 [`asseteditor-ipc.md`](asseteditor-ipc.md)。

## 按变化定位验证

| 变化 | 首先检查 | 最低验证边界 |
| --- | --- | --- |
| 新增或重注册编辑器 | 编辑器 `DependencyInjectionContainer`、`RegisterTools`、主项目引用、本地化 | 编辑器测试、组合根构建、中文资源 |
| Pack 读写 | `Shared.Core/PackFiles`、格式模型、容器身份 | 往返、失败回滚、多容器与保存测试 |
| 文件夹工程 | 容器、路径策略、Git 服务、应用协调器 | Shared.Core 与 AssetEditor 对应测试；大型目录分阶段实测 |
| 通用 WPF | `Shared/SharedUI`、资源引用、消费方 | `SharedUiArchitectureTests`、受影响视图测试、实际渲染 |
| 3D 或材质 | `GameWorld/View3D`、CPU 参数绑定、Effect | 源码契约、单元测试、离屏像素、实际 UI |
| 更新器或发布布局 | 主项目发布目标、更新器、发布配置 | 更新器测试、全新 publish 目录、产物核验 |
| IPC | `Editors/Ipc`、应用启动/停止生命周期 | IPC 测试、协议核对、真实管道用例 |

CI 和完整 Release 命令以根 `AGENTS.md` 为准。单元测试通过不能替代 WPF 视觉验收、真实 Pack 行为或大型工程性能验收。
