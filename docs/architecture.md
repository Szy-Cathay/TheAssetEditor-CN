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

### VFX 可视化编辑

- `Editors/VfxEditor` 编辑 `vfx/` 下 revision 1/2 的 XML 片段，组合根通过该模块的 `DependencyInjectionContainer` 注册。原有文本编辑器仍可选用；XML 格式错误显示文件名和行列位置，不自动修补源文件。
- `Shared/GameFiles/Vfx` 保留文件编码、顶层节点、未知数据和未修改的曲线元数据；未编辑时写回原始字节。曲线按时间显示，写回保留原始关键点顺序及同时间点；只调整已有关键点的时间和值，非线性段显示关键点连线示意，不模拟游戏插值或特效渲染。
- 编辑器默认显示常用调整，按颜色、大小、疏密、时间等目的分类；搜索覆盖当前层所有分类，仍遵守画质筛选。属性用途、调整方法和中文模式名称来自 `Language_Cn.json`，模式选择写回原始游戏值；未确认语义的属性明确标识，不提供猜测性调法。曲线横轴保留源文件刻度，不统一换算时间单位。
- 发射器、同名行为链和渲染栈合并展示为效果层；引用与引用层覆盖单独编辑，画质档位独立。新建组合使用原生 `vfx_references`，依赖源 VFX 及其资源；位置、旋转、大小和开始时间成组编辑。复制或移除层同步处理关联行为链、渲染栈和编辑器元数据，结构撤销保留节点身份，兼容已有字段撤销；不增删曲线关键点。
- 保存走 `IFileSaveService`：当前工程内的文件按所属容器定位，参考资源或其他工程资源另存；文件已被外部修改、输入无效、保存失败或取消时保留未保存状态。
- “预览特效”使用当前页内存快照，依赖按源工程、当前工程、已加载参考和原版资源解析；不保存文档或改变撤销状态。`TerryPreviewBuilder` 递归收集 VFX 引用、模型和材质资源，特效使用独立名称；`ExportSnapshot` 通过现有 Pack 序列化导出脱离工作区的资源包，不发送工程保存事件。
- Terry 预览仅支持战锤 3：临时 MOVIE 包放入配置的游戏数据目录，导出场景位于 `assembly_kit/working_data/AssetEditor.CN/TerryPreview`，缓存和归属记录隔离在 `%USERPROFILE%\AssetEditor.CN\TerryPreview`。按钮会正常关闭旧 Terry、保留其未保存提示，再以相对场景路径启动以避开 Terry 的中文路径限制；只自动确认本次生成场景的组件加载窗口，并在该进程内打开 VFX 工具、用临时名称筛选只显示当前特效，不改 Terry 的资源配置或删除原版依赖。工具激活和筛选通过控件自动化及限定到该进程窗口的字符消息完成，不依赖 Terry 保持前台，也不发送全局鼠标或键盘输入；确认筛选后的列表仅含当前特效后才报告就绪。不同导出保留独立场景，覆盖或停用临时包前校验归属和内容散列，不覆盖用户修改过的预览文件。预览验证外观，不模拟技能逻辑。
- 验证入口为 `VfxEditorTests`，真实本地样本扫描通过 `ASSETEDITOR_VFX_SAMPLES` 指定目录；渲染输出通过 `ASSETEDITOR_VFX_RENDER_OUTPUT` 指定，不把样本路径写死在测试中。

### 动画批量导出

- `Editors/AnimationFragmentEditor/Editor.AnimationFragmentEditor/AnimationBatchExporter` 将已加载的只读来源中的独立 `.anim` 文件转换为版本 5、6、7 或 8，写入当前可编辑工程；当前可编辑工程不出现在来源列表中，也不展开 `.animpack` 内部条目。v8 使用 `AnimationFile` 的 v8 写入器并重新读取校验，保留静态/缺省轨道与多段数据；旧版本不支持多段输出。
- 来源和动画默认均不勾选；筛选只影响显示，“全选筛选结果”只增加当前可见项，“取消全部选择”清除包括隐藏项在内的全部选择。Shift 按当前可见顺序选择锚点到目标的范围；左键拖过的范围按起始项统一勾选或取消，不反复翻转。松开、移出列表、失去捕获、卸载或切换上下文结束拖选；筛选变化重置范围锚点，但保留已有勾选。锚点不产生常驻行高亮，键盘焦点提示仍保留。导出只处理勾选项，同路径的多来源动画必须先消除歧义。
- 输出固定为当前可编辑工程，不提供独立输出路径设置；保留资源相对路径，仅附带来源中与成功转换动画同骨架名称的逆矩阵。文件夹工程通过现有文件写入服务直接落盘，不保存其他编辑器的内存内容、不生成 Pack、不创建还原点。执行前确认输出目标和覆盖影响；转换失败逐项报告，来源或当前工程变化时停止写入。
- 版本提示只引用已核实样本，不将格式版本视为完整游戏兼容表：`Data/Rome_Man_And_Shield_Pack` 的骨架为 v5，`Data/Pharaoh_Pack` 的骨架为 v7，`AnimationFileV8WriterTests` 核验战锤 3 的 v8 动画；v6 的游戏对应关系尚未核实。同一游戏可能使用多个版本，骨架和动画配置仍须匹配。

### Shared UI

- `Shared.Ui` 只能引用 `Shared/` 内项目，不能反向依赖 `AssetEditor`、`Editors` 或具体编辑器。
- 进入 `Shared.Ui` 的组件必须跨模块、无业务含义、职责稳定且可独立验证；名称相同不足以证明应共享。
- 通用对话框优先使用 `IStandardDialogs` 等现有抽象。
- 自动门禁：`Testing/AssetEditorTests/SharedUiArchitectureTests.cs`。

### Pack 与编辑器文件身份

- `Shared/SharedCore/PackFiles/PackFileService.cs` 管理已加载容器，并通过角色明确的入口区分当前文件夹工程与参考 Pack；产品调用方不能用布尔参数决定普通 Pack 是否可编辑。
- 文件夹工程是唯一面向用户的可编辑工作区，资源修改直接落盘。参考 Pack 与 CA Pack 只读；普通 `PackFileContainer` 的解析、序列化和内部短生命周期写入能力只服务格式、生成、测试与开发装配。
- 主工作流只公开新建 Mod 工程、从 Pack 导入工程、打开工程、打开参考 Pack 和生成 Pack。最近工程与最近参考 Pack 分别恢复原角色；再次打开已加载工程时直接将该文件夹工程切回当前工作区，参考 Pack 不能替换当前工程或获得直接保存入口。
- 文件关联等价启动参数、IPC `packPathOnDisk` 和其他外部 `.pack` 输入统一进入应用级用途选择：用户只能明确选择“作为参考打开”或“导入为工程”。外部入口不能静默创建普通可编辑 Pack；取消、设置取消或失败时不得改变当前工作区和已加载容器。
- 外部 Pack 路径在进入用途选择前统一规范化；同一路径同角色复用已有容器或重新激活已有工程，不重复加载。若同一路径已按另一角色加载，应用以中文说明冲突并停止，不自动转换角色。
- 生成 Pack 从文件夹工程当前磁盘快照写出工程外的输出 Pack，不保存编辑器内存、不创建 Git 历史；编辑器保存只负责把内存修改写入工程磁盘。
- 文件夹工程对应用层只公开 `IFolderProjectHistoryService` 的工程历史能力；分支、暂存区、储藏、合并和提交改写只保留在 `Shared.Core` 内部兼容/恢复实现中，不能注册为用户命令、窗口或编辑器。
- `PackFile` 的身份必须结合所属容器和资源路径解析。跨容器查找、保存或错误报告不能只按文件名猜测；先检查 `GetPackFileContainer` 与 `GetFullPath`。
- 文件夹工程的内存、磁盘、Git 与输出 Pack 语义见 [`folder-project-version-control.md`](folder-project-version-control.md)。

### 中文资源与 WPF

- 用户可见文本统一进入 `AssetEditor/Language_Cn.json`，由 `Shared/SharedCore/Services/LocalizationManager.cs` 加载。
- 主题资源位于 `AssetEditor/Themes/`。独立视图和测试不能假设 `App.xaml` 已自动加载全部资源。
- 涉及 `Application`、`ResourceDictionary` 或 `Freezable` 的测试使用 `Testing/AssetEditorTests/WpfTestApplicationHost.cs` 的单一 STA/Dispatcher 宿主。

### 共享动画播放

- 共享播放器以选定的主动画提供时长、帧数、当前位置和播放状态；未加载动画的参考对象不能使界面误报为正在播放。动画重定向的来源、生成结果与目标优先级见 [UI 规范](ui-design-system.md)。
- 刷新主动画或滑块范围时，WPF 可能把范围约束后的滑块值写回绑定；此类界面同步不能再次触发动画定位，否则会重置实际播放进度。
- 按时间定位和拖动进度保留原有播放/暂停状态，逐帧按钮仍沿用暂停定位行为。主动画自然播到末尾才触发统一循环或暂停，并同步控制同场景的参考动画与元数据播放器。

### 3D 交互能力

- `ViewportInteractionCapabilities` 按编辑器作用域隔离，默认关闭模型点选、框选、选中轮廓和模型 Gizmo；只有 `KitbashSceneComponentSet` 启用模型编辑能力。共享层不判断具体编辑器类型，也不把开关放入全局设置。
- 非 Kitbash 的相机操作、动画预览和业务专用操作独立保留：关键帧工具编辑骨骼，超级视图选择和编辑元数据标记，CSC 编辑相机、灯光等场景通道；CSC 模型及模型容器不能激活视口 Gizmo。
- 骑乘工具通过 Tab 开启附着点选择，直接在当前坐骑的可见、有权重网格上选择顶点；不经过整模型选择，也不能选择骑手或生成结果。退出或更换坐骑后清除临时顶点选择。

### 3D 初始观察方向

- 各 3D 宿主通过共享 `ArcBallCamera` 从目标上方俯视 24°、水平转角 26° 开始，与批准的空场景参考方向保持一致：红 X 轴从左下向右上缓斜，蓝 Z 轴从左上向右下较陡。保留焦点和视距；只设置初值，不限制后续自由绕转，不翻转模型。
- Kitbash、共享动画/骨架/超级视图宿主与 CSC 复用此初值。照片工作室调参、读取已存相机设置和 CSC 的“透过相机查看”等显式操作仍按自身设置执行，不能被默认角度覆盖。

### 更新器

- 主程序只启动发布包内的独立更新器；国区版运行时身份以根 `AGENTS.md` 为准。
- 主程序和更新器优先读取 Gitee `szy-cathay/AssetEditor-CN-Downloads` 仓库公开的 `raw/master/update-feed.json`，不可用时回退到该仓库的发行版 API。更新器读取发行版资产中的清单，按清单顺序下载小于 Gitee 单附件限制的 ZIP 分片，合并后校验每个分片和完整 ZIP 的 SHA-256。
- 更新安装必须验证安装目录与事务目录不重叠，拒绝重解析点、路径别名重叠和非自有事务工作区。
- updater payload 复制后逐文件复核 SHA-256；安装在事务目录完成暂存、备份和替换，失败时尝试回滚并保留原始错误。
- 二次更新进程从事务目录中的更新器副本启动，但工作目录必须保持为安装目录；不能用进程当前目录猜测安装位置或事务所有权。
- 更新器顶层失败必须输出原始异常并返回非零退出码。交互式控制台在正常结束和可捕获失败后都等待用户确认；标准输入或输出被重定向时不得阻塞等待。
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

构建模式和完整验证命令以根 `AGENTS.md` 为准：本地开发、调试和人工验收使用 Debug；线上 CI 与正式发布使用 Release，CI 的编译配置不代表发布或发布授权。单元测试通过不能替代 WPF 视觉验收、真实 Pack 行为或大型工程性能验收。
