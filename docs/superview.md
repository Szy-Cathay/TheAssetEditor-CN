# 超级视图（SuperView）技术上下文

> 面向修改、审查和验收超级视图的 AI。本文记录稳定架构、数据流、元数据预览/编辑原理、所有权边界和验证路由，不替代当前代码。涉及界面时还必须阅读 [`ui-design-system.md`](ui-design-system.md)。

## 1. 阅读触发与事实优先级

出现下列任一范围时，开始工作前完整阅读本文：

- 超级视图、SuperView、Animation META、动画元数据预览；
- persistent/animation metadata 双页签、Fragment/Slot、预览实例；
- Impact、Target、Fire、Splash、Effect、Prop、Blood 等空间元数据；
- 元数据 3D Gizmo、时间范围、骨骼附着、批量显示、聚焦；
- 计划修改超级视图使用的 `SceneObject`、动画播放器、View3D、Gizmo 或元数据解析共享代码。

事实发生冲突时按以下顺序处理：

1. 当前代码、项目引用和测试；
2. 本文记录的稳定契约与边界；
3. 其他仓库文档；
4. 历史提交、PR、对话和上游实现。

本文只保存不能靠单个类快速重建的所有权、调用链和边界，不记录测试总数、提交 SHA 或机械式方法清单。稳定契约发生变化时同步更新本文。

## 2. 编辑器身份与硬边界

超级视图是动画及其元数据的组合预览、检查和元数据编辑宿主。它负责：

- 选择动画 Fragment/Slot 并加载参考角色模型、骨架和动画；
- 同时加载 persistent metadata 与当前 animation metadata；
- 把元数据转换为可随动画更新的模型、标记、线段、形状和动画规则；
- 提供元数据列表/属性编辑、时间跳转、分类显隐和选中聚焦；
- 对已验证的空间元数据提供 3D 平移/旋转、撤销/重做和保存；
- 在引用文件缺失且 Fragment 提供合法目标路径时创建元数据文件。

超级视图不是模型编辑器。硬边界：

- 参考模型和元数据生成的 Prop/标记默认不可被共享模型选择系统编辑。
- 不得接入 Kitbash 的对象/面/边/顶点/骨骼选择输入、Kitbash 覆盖层或模型变换 wrapper。
- 超级视图的 3D Gizmo 只写回受支持元数据字段，不修改网格、骨架资源或参考模型层级。
- 元数据二进制定义、解析和序列化属于共享格式层；不能为超级视图方便而在 UI 层猜测字段布局。
- 预览失败不应破坏原始元数据，也不应伪造不存在的资源；可降级为明确的通用空间标记或跳过单条规则。

## 3. 模块、注册与宿主组成

### 3.1 主要位置

| 范围 | 位置 | 所有权 |
| --- | --- | --- |
| 超级视图 | `Editors/MetaDataEditor/AnimationMeta/SuperView/` | 宿主 VM、预览构建、空间编辑、专属界面 |
| 元数据编辑器 | `Editors/MetaDataEditor/AnimationMeta/MetaEditor/` | 单份 metadata 文档的列表、字段、解析与保存 UI |
| AnimationMeta 模块入口 | `Editors/MetaDataEditor/AnimationMeta/DependencyInjectionContainer.cs` | 编辑器和 scoped 服务注册 |
| 共享编辑器宿主 | `Editors/Shared/Editors.Shared.Core/Common/` | 参考模型、动画选择、播放同步、View3D 宿主 |
| 共享 View3D | `GameWorld/View3D/` | 场景、相机、渲染、Gizmo 原语和动画容器 |
| 元数据格式 | `Shared/GameFiles/` 下相关 AnimationMeta 实现 | 定义、解析、未知 payload 保留和序列化 |
| 模块测试 | `Editors/AnimationMeta/Test.AnimationMeta/` | 元数据编辑、预览、时间、空间 Gizmo 与保存 |

### 3.2 工具注册

`DependencyInjectionContainer.RegisterTools` 注册两类不同入口：

- 超级视图：工具栏入口，没有直接文件扩展名所有权，用户先选择参考动画上下文；
- Meta Editor：直接打开 `.anm.meta`、`.meta`、`.snd.meta` 等元数据文件。

不要因为两者共用 `MetaDataEditorViewModel` 就把入口合并。直接 Meta Editor 是单文档编辑器；超级视图同时组合两份文档、动画、模型、预览和空间编辑。

### 3.3 View3D 组成

`SuperViewViewModel` 继承 `EditorHostBase`。基类建立共享 `IWpfGame`、核心 View3D 组件、参考模型 VM、动画播放器和场景对象集合；超级视图构造时再显式加入 scoped 的 `CombatMetaDataGizmoComponent` 与 `MetaDataMarkerPickerComponent`。

超级视图不插入 `KitbashSceneComponentSet`。其专属 Gizmo 使用共享 `Gizmo` 原语，但编辑目标是 `ParsedMetadataAttribute`，不是 `SelectionManager` 中的模型选择。专属 marker picker 只消费当前 `MetaDataInspectionIndex` 与当前可见 marker 预览，同样不进入共享模型选择。

## 4. 对象与状态所有权

| 对象 | 主要职责 | 状态所有权 |
| --- | --- | --- |
| `SuperViewViewModel` | 双文档组合、场景/动画事件、预览重建、保存与 UI 命令 | 当前 Fragment/Slot、活动页签、选中预览、分类显示设置 |
| `PersistentMetaEditor` | persistent metadata 列表、字段、结构和保存 | 一份独立解析文档及 dirty |
| `MetaEditor` | animation metadata 列表、字段、结构和保存 | 另一份独立解析文档及 dirty |
| `SceneObjectViewModel` | 单个参考资产的 UI 状态 | 模型路径、动画选择、阵营/相机等共享预览状态 |
| `SceneObjectEditor` | 加载模型、骨架、动画和 meta，并发布更新事件 | 当前运行时 `SceneObject` |
| `SceneObject` | 场景根、主播放器、metadata instances | 每帧调用实例 `Update` |
| `MetaDataBuilder` | 组合预览实例、动画规则和结构化诊断 | 一次构建结果，不持有文档 dirty |
| `MetaDataPreviewBuilder` | 分派并建立已支持的预览实例族 | 单条预览的失败隔离与 marker fallback |
| `MetaDataRuleBuilder` | 建立 Dock Equipment 和 Transform 动画规则 | 规则列表，不直接写主播放器 |
| `MetaDataResourceResolver` | 查找模型、动画和 Effect XML | 统一资源缺失诊断，不伪造资源 |
| `MetaDataInspectionIndex` | 汇总双文档 META 的检查身份、字段/时间状态、预览能力、空间焦点和诊断 | 以 owner 与源对象引用组成身份，不持有编辑状态 |
| `MetaDataTimelineViewModel` | 把索引中的有效时间范围投影成播放器标记并执行导航 | 只属于 SuperView，不解释或修改 META 字段 |
| `CombatMetaDataEditSession` | 空间元数据预览事务、撤销/重做、每文档历史 | 以文档 owner 区分的编辑历史 |
| `CombatMetaDataGizmoComponent` | 3D 手势与 Gizmo/鼠标生命周期 | 当前空间目标和活动 gesture |
| `MetaDataMarkerPickerComponent` | 当前 marker 的射线命中、悬停、单击选择和双击聚焦 | 只持有瞬时指针状态，不缓存文档或模型选择 |
| `SpatialMetaDataCatalog` | 已验证空间标签到可写字段的适配 | 类型能力定义，不持有运行时选择 |

必须区分：

- 原始/解析文档：两个 `MetaDataEditorViewModel` 各自持有；
- 预览实例：由 `MetaDataBuilder` 从当前文档快照重建，可随时清理；
- 空间编辑事务：`CombatMetaDataEditSession` 在解析对象上 preview/commit/cancel；
- 场景参考资源：模型、骨架、动画，只供显示和坐标参考；
- 文件 dirty：两个子编辑器 dirty 与两个 owner 的空间编辑历史共同汇总。

预览节点、分类显隐、当前播放时间和相机都不是文件内容。解析对象的字段值才是保存来源。

## 5. 双文档模型与 UI 耦合

超级视图在同一个编辑器作用域内手工创建两个独立 `MetaDataEditorViewModel`，而不是从 DI 解析同一个 scoped 实例：

- `PersistentMetaEditor` 对应存活期/持久元数据；
- `MetaEditor` 对应当前动画元数据。

两者必须保持独立的：

- 文件路径与 `FileOwner`；
- 解析根和列表选择；
- dirty、保存结果和结构变更事件；
- `CombatMetaDataEditSession` owner 历史。

`SuperView/EditorView.xaml` 提供 Persistent 与 Animation 页签，并在引用文件缺失时显示创建提示。

存在一个跨目录但属于超级视图表面的关键耦合：`MetaEditor/View/MetaDataAttributeView.xaml` 通过 `AncestorType=superview:EditorView` 绑定超级视图的批量预览、选中标记、时间跳转、聚焦、3D 编辑、撤销和重做。直接 Meta Editor 没有该祖先，因此超级视图专属控件必须折叠或停用。

修改这些控件时不能只搜索 `SuperView/`；必须同时检查 `MetaDataAttributeView.xaml`、其 code-behind、模板和 `MetaDataAttributeControlTests`。不要让超级视图专属按钮泄漏到直接 Meta Editor。

## 6. 加载与运行时数据流

### 6.1 参考动画加载

共享 `BinAnimationViewModel` 负责 Fragment/Slot 选择：

1. 选择 Fragment 和 Slot；
2. 确定参考模型、骨架和动画；
3. 解析当前 animation metadata 引用；
4. 查找 persistent metadata，优先 `PERSISTENT_METADATA_ALIVE`，必要时使用既有 fallback；
5. 调用 `SceneObjectEditor` 更新动画和两份 metadata；
6. 发布 `SceneObjectUpdateEvent`。

超级视图不应自己再写一套 Fragment/Slot 解析逻辑。若游戏数据库或引用语义变化，先修共享参考模型层并验证所有调用者。

### 6.2 更新事件

`SuperViewViewModel` 收到场景对象更新后：

1. 重置/切换两个 owner 的空间编辑历史；
2. 各自加载 persistent 与 animation metadata 编辑器；
3. 清理旧的预览实例和动画规则；
4. 清除已不属于当前源对象的用户预览覆盖设置；
5. 调用 `MetaDataBuilder.Build(...)`，一次取得实例、规则和诊断；
6. 把实例放入 `SceneObject.MetaDataItems`，把规则安装到主播放器，并替换当前诊断快照；
7. 刷新播放器和选择状态，并保持所有参考模型节点不可选择。

构建参数包含两份解析文档、当前选中属性、场景根、骨架、主播放器和 Fragment 上下文。任何缓存都必须以这些实际输入为准，不能只按 tag 名复用。

### 6.3 每帧更新

`SceneObject.Update` 以主播放器的当前秒数调用每个 `IMetaDataInstance.Update(currentTimeSeconds)`。动画 Prop 的附属播放器也必须与主时间同步；主播放器暂停时仍要在显式 seek 后刷新附属 pose。

时间单位是秒。不要在 UI、元数据或动画播放器之间隐式混用微秒、帧序号和秒；只有动画 clip 的 `MicrosecondsPerFrame` 在边界处换算。

## 7. 预览构建原理

### 7.1 合并顺序

`MetaDataBuilder.Build` 先应用 persistent metadata，再应用当前 animation metadata。若 animation metadata 包含格式定义中的 `DisablePersistant_v10`，则跳过 persistent 部分。保留代码中的格式拼写，不要为了文字正确而重命名二进制定义。

两层来源的预览仍保留各自 `ParsedMetadataAttribute` 引用。后续选中、增量替换、批量显隐和保存都依赖对象身份，不应按字段值或 tag 文本猜来源。

### 7.2 支持的输出

`MetaDataBuildResult` 一次返回三类运行时结果：

1. 场景实例：普通 Prop、Animated Prop、Effect、战斗定位器和通用空间标记；
2. 根播放器动画规则：Dock Equipment 和 Transform Bone；Animated Prop 的根变换复制仍由其附属播放器持有并随实例清理；
3. 结构化诊断：原始 `ParsedMetadataAttribute` 引用、`Persistent`/`Animation` owner、严重程度、本地化原因键，以及可用的时间范围、空间位置、资源路径或骨骼上下文。

构建器不直接把根规则写入 `AnimationPlayer`。调用者只消费完整结果，不参与 META 类型分派。`MetaDataBuildDiagnostic.ReasonKey` 必须对应中文本地化资源；诊断只描述预览问题，不改变字段校验、dirty 或保存门禁。

战斗预览分类为 `Impact`、`Target`、`Fire`、`Splash`。每个实例都有独立显隐，不应通过隐藏整个模型实现分类筛选。

### 7.3 资源失败隔离

每个 tag 预览通过独立的容错边界创建：

- 某个 Prop 模型或动画缺失时，记录警告，并在空间信息可用时降级为通用标记；
- 某条 Dock 动画规则缺失时，只跳过该规则；
- 某个 Splash 参数无效时，不能阻止其他合法定位器建立；
- 未知或不支持的 tag 保留在解析文档中，不因无法预览而删除或改写；
- 不能凭空生成一个“看起来合理”的资源或坐标掩盖数据缺失。

资源失败策略的目标是保留可编辑文档与其余预览，而不是把整个构建标记为成功无误。资源查找失败、资源服务异常和单项建立异常都只追加该源对象的诊断；若失败发生在节点挂载之后，必须移除该项产生的半成品节点。诊断快照由 `SuperViewViewModel.MetaDataDiagnostics` 提供给后续检查索引和问题列表。

### 7.4 增量刷新

字段编辑后，超级视图优先通过 `BuildPreview` 只重建受影响的一个预览：

- 构建器能为该源创建替代实例时，保留其他实例和它们的 UI 状态；
- 不支持增量创建、结构变更或影响动画规则时，回退到完整重建；
- 选择变化只更新 `IsSelected` 和高亮，不应无条件重建全部预览；
- 识别“同一预览”使用 `ParsedMetadataAttribute` 引用身份，不使用值相等。

替换实例时先完成新实例建立，再有序清理旧实例；失败不能让源属性从列表消失。

## 8. 预览实例生命周期

所有预览实例实现 `IMetaDataInstance`，至少提供 `Update` 和 `CleanUp`。主要类型：

| 类型 | 行为 | 清理责任 |
| --- | --- | --- |
| `DrawableMetaInstance` | 位置/方向标记，可跟随骨骼，可有时间范围 | 从父节点移除标记 |
| `CombatMetaDataInstance` | 战斗分类标记，使用实时位置与参考变换 | 从父节点移除节点 |
| `PropInstance` | 普通模型 Prop，按骨骼/根附着 | 移除节点并标记附属 player 删除 |
| `AnimatedPropInstance` | 自带动画的 Prop，与主时间同步/循环 | 移除节点并标记附属 player 删除 |

`ReferenceWorldTransform` 表示元数据局部值所依附的骨骼/父节点世界变换；`WorldTransform` 是局部位置/方向乘以该参考变换。3D 编辑和聚焦依赖这一分层，不能把二者合并成一个永久写回的世界矩阵。

实例可同时受三种可见性条件控制：

- `IsEnabled`：分类或用户开关；
- 当前时间是否落在 authored range；
- `ShowForEntireAnimation`：用户临时覆盖。

`IsSelected` 只控制视觉强调，例如 preview outline，不等于共享场景对象选择。清理时必须移除节点、附属 animation player、选择高亮和事件引用，避免每次 rebuild 叠加实例。

## 9. 时间语义

`MetaDataTimeRange` 集中处理时间范围：

- 从 v2/v10 的时间基类读取 start/end；
- 使用小容差比较边界；
- 普通范围在 start 到 end 内可见；
- Instant 类型可用至少一帧的显示窗口，避免 `(0,0)` 完全不可见；
- 只有明确列入 allowlist 的状态类 tag，`(0,0)` 才表示整段动画；
- Prop 等既有格式语义可显式使用 `WholeAnimation` 零范围行为。

不要建立“所有 `(0,0)` 都显示整段”的通用规则。这会让瞬时战斗事件错误地整段显示。新增 tag 时必须根据格式/游戏证据选择零范围语义并补测试。

跳转到开始/结束、选中时间范围显示和实例更新必须使用同一秒数定义。播放条 seek 后既要更新主 pose，也要更新附属 Prop pose和时间可见性。

### 9.1 统一检查索引与时间轴标记

`MetaDataInspectionIndex` 是 SuperView 内部的只读适配层。每项保留原始 `ParsedMetadataAttribute` 引用和 `Persistent`/`Animation` owner；即使两份文档中 tag 名称、版本和字段值完全相同，也必须是两个独立项。索引同时汇总字段有效性、原始 authored range、结构化时间状态、预览能力、可选战斗分类/空间焦点和该 owner 的构建诊断，不能反向成为保存数据源。

索引在文档加载、结构变化、字段有效性或值变化、单项/完整预览结果变化和动画 clip 时长变化后重建。活动页签或列表选择只更新时间轴的选择投影，不得因此完整重建预览。字段文本无效、时间为负数、start 大于 end、范围超出当前 clip 或没有可用 clip 时，索引保留原因状态，但不生成虚假标记或 seek 目标。

时间轴紧邻共享播放滑块，只投影三种只读标记：普通同一时刻为瞬时刻线，合法 start/end 为区间条，明确 allowlist 的 `(0,0)` 为全程条。Impact、Target、Fire、Splash 沿用三维预览分类颜色；其他类型使用主题语义色。所选项还必须通过轮廓、厚度或形状变化表达，不能只换颜色；tooltip 和 `AutomationProperties.Name` 需用中文说明类型、owner 和时间。

点击标记只做导航：切换到对应 owner 页签，按引用身份选择同一个源对象，并把共享播放器 seek 到 authored start。它不提供拖动、时间编辑、缩放、裁剪或区间调整。时间范围横向相交的标记分配到不同的紧凑轨道，不相交的标记复用轨道；轨道数量增加时注释区域按需增高，不能把多个标记完全覆盖成一条。共享 `AnimationPlayerView` 只暴露通用的可选注释内容槽；所有 META 索引、标记、颜色、轨道布局和点击语义仍留在 `AnimationMeta/SuperView`，不得向共享播放器加入 `IsSuperView`、META 类型判断或业务分发。

## 10. 空间元数据能力目录

`SpatialMetaDataCatalog` 是“哪些字段可由 3D Gizmo 安全编辑”的白名单适配层。当前覆盖 Effect、Prop、Blood、CameraShake、CrewLocation、SoundTrigger、SoundBuilding、Transform 等已验证类型，并为每类提供：

- 位置 getter/setter；
- 可选的方向 getter/setter；
- 骨骼或根附着信息；
- 是否使用通用空间标记；
- 是否允许旋转。

目录通过 `MetaDataTagAttribute` 识别可写的公开属性，但反射不是“任意字段都可编辑”的许可。只有显式适配并有格式证据的类型才能暴露 Gizmo。

新增空间类型时必须证明：

- 存储坐标是相对根、相对骨骼还是世界空间；
- 位置/方向字段是否可写，四元数约定是否一致；
- 是否需要可视标记、时间范围和骨骼高亮；
- 直接字段编辑、Gizmo preview、undo/redo、保存/重载是否一致；
- 未验证类型仍保持不可写，而不是猜测最相似的字段。

## 11. 3D 元数据编辑

### 11.1 与 Kitbash 变换的区别

`CombatMetaDataEditSession` 不使用 Kitbash 的共享模型选择和 `TransformGizmoWrapper`。它为每个文档 owner 维护自己的命令历史，直接对 `ParsedMetadataAttribute` 的已适配空间字段执行事务。

`CombatMetaDataGizmoComponent` 只提供 translate/rotate，默认以局部参考系显示；它不提供缩放，不选取网格，不改变模型节点。

### 11.2 Gesture 事务

空间编辑流程：

1. 根据活动页签和选中属性确定 owner 与目标；
2. 由预览取得 `ReferenceWorldTransform` 和当前世界 pose；
3. `BeginGesture` 记录字段基线；
4. 平移把世界 delta 转回元数据局部空间；
5. 旋转用参考旋转的共轭关系把世界旋转转回局部方向；
6. Preview 修改解析对象并发布预览刷新，不写入历史；
7. Commit 建立一条可 redo 的命令；Cancel 恢复原值；
8. 结束、禁用、选中变化或异常时释放鼠标所有权并清理目标。

Splash 的 start/end 是两个独立控制点；不能把它们压成一个中心点。Effect 和其他骨骼跟随类型必须基于实时骨骼 frame 转换，不能把当前世界位置直接永久写回局部字段。

### 11.3 历史与 dirty

Edit Session 以 owner 的引用身份隔离历史。Persistent 与 Animation 页签的 Undo/Redo、saved state 和 dirty 互不串联。

超级视图总 dirty 为：

- 任一 `MetaDataEditorViewModel` 有未保存结构/字段变化；或
- 任一 owner 的空间编辑历史偏离其 saved state。

保存成功后才标记对应历史为 saved。切换 Fragment/Slot、重建预览或选择属性不能误清另一份文档的 dirty，也不能把 preview 手势当作已保存。

## 12. 选择、显示和聚焦

超级视图存在的是“元数据属性选择”，不是“模型选择”：

- Meta 列表选中 `ParsedMetadataAttribute`；
- 通过引用身份找到对应 `IMetaDataPreview`；
- 设置其 `IsSelected`，更新高亮、时间信息和 3D Gizmo 目标；
- 聚焦使用预览的 `FocusPosition`；
- 需要时用 `HighlightedBoneIndex` 指示附着骨骼。

参考模型节点和 Prop 节点保持 `IsSelectable=false`。选中预览可使用专用 preview outline，但不得写入共享 `SelectionManager` 或触发 Kitbash 整体选择语义。

3D marker 选取只注册实现 `IMetaDataMarkerPreview`、仍属于当前检查索引且当前实际可见的预览。普通/Animated Prop、参考模型、网格、面、边、顶点、骨骼和无可视几何的骨骼占位预览不实现该命中契约。命中按到相机射线的距离、沿射线深度和检查索引顺序依次稳定排序；透视视图保留小角度容差，使远处可见 marker 仍可点选。Splash 保留半透明攻击范围面，线框只显示首尾端面圆环和位于同一平面的方向箭头；走廊端面圆环的显示与命中半径都必须使用 `WidthForCorridor / 2`，不能替换成固定尺寸的小定位环。首尾圆环分别作为 start/end 命中点，不能退化为只命中中心点。

单击 marker 通过检查索引中的 owner 与源对象引用切换页签并选择原始 `ParsedMetadataAttribute`，不跳播放时间、不改字段；点击未命中 marker 的空白位置清除当前 META 选择，但不能转而选中参考模型或 Prop。Splash 的首尾命中还必须同步切换对应 Gizmo 控制点。双击同一 marker 同一控制点的第二次点击只调用现有 META 聚焦，不重复选择；连续点击 Splash 的不同控制点仍是两次单击选择。没有有效焦点位置时保留单击选择且不移动相机。活动 Gizmo gesture 或相机等其他组件持有鼠标时 picker 必须清空本次点击并让路；它不得抢占鼠标所有权。

悬停复用 marker 已有的选中轮廓/加粗反馈，不新增共享 View3D 覆盖层。owner 切换立即清除旧悬停；预览替换、完整重建、Fragment/Slot 切换和关闭还必须清除待完成点击与双击状态。清理后的实例即使仍有旧对象引用也不得再次命中。

批量显示覆盖只应用于当前 animation metadata 来源的对应分类，不应修改 persistent metadata 的预览。用户覆盖按源对象身份跟踪；完整重建后应剔除已不存在的源，保留仍存在源的设置。

## 13. 保存与文件所有权

超级视图可以同时持有两份文件。保存流程必须分别处理：

1. 已加载的 Persistent 文档；
2. 已加载的 Animation 文档；
3. 对应 owner 的空间编辑历史。

若某份文件没有加载，不应因此让另一份保存失败。若引用文件缺失，只有 Fragment 提供明确目标路径时，创建按钮才可建立空的受支持 metadata 文件。

`ScopedFileSavedEvent.FileOwner` 用于判断是哪一个子编辑器完成保存，并更新 `SceneObject` 中正确的 metadata 引用。未知 owner 是编程错误，应暴露，而不是猜成当前页签。

只有两个需要保存的子操作都成功时，宿主才能把对应编辑历史标记为 saved。取消、解析失败、路径失败或部分保存不能清除仍未持久化的 dirty。

保存/重载验证必须确认：

- 位置与方向局部值没有被错误写成世界坐标；
- 未知 metadata payload 原样保留；
- tag 顺序、多选移动/删除和无效输入处理保持现有语义；
- 双文件路径和 owner 没有交换；
- 新建文件使用正确版本和引用路径。

## 14. 动画规则

预览构建器可把部分 metadata 转成运行时动画规则：

- `CopyRootTransform`：把指定骨骼的动画世界变换及 authored offset 应用到附属对象根；
- `DockEquipmentRule`：在活动时间内，把装备槽骨骼对齐到目标骨骼；
- `TransformBoneRule`：在活动时间内对指定骨骼应用 authored 位置/方向增量。

这些规则改变预览 pose，不直接改写原始动画文件。完整构建只返回根规则，由 `SuperViewViewModel` 在清除旧规则后统一安装。规则创建失败应只产生该源对象的诊断并隔离单条规则；旧规则在 rebuild 时必须从播放器移除，不能累计执行。

修改规则时同时检查：局部/世界空间接口、时间单位、骨骼索引、父子层级、循环/暂停刷新和错误后的停用行为。不能把预览规则的结果烘焙回模型或动画资源，除非另有明确功能授权。

## 15. 生命周期与清理

`SuperViewViewModel.Close` 和重新加载路径必须解除：

- Fragment/Slot 与 `SceneObjectUpdateEvent` 订阅；
- 主播放器和两个 Meta Editor 的属性/结构事件；
- `CombatMetaDataEditSession` 事件；
- EventHub 注册；
- 当前 Gizmo gesture 与鼠标所有权；
- marker picker 的悬停、待完成点击、双击序列和宿主回调；
- 所有 metadata instances、附属 players、场景节点和动画规则。

重新构建预览是高频生命周期，不等于关闭编辑器。每次 rebuild 都必须先有序清理旧结果，同时保留真正属于文档或用户覆盖的状态。不要依赖 GC 清理场景节点、GPU 资源或事件订阅。

## 16. 修改边界与路由

### 16.1 默认允许在 AnimationMeta/SuperView 内修改

- 超级视图宿主、双文档协调和专属界面；
- 元数据预览构建、实例、分类、时间和空间能力适配；
- 元数据 3D Edit Session/Gizmo；
- 超级视图专属测试和本地化。

若改 `MetaDataAttributeView.xaml`，必须保持直接 Meta Editor 与超级视图的条件化边界。

### 16.2 修改共享层前必须证明

下列位置可以修改，但影响面更大：

- `Editors/Shared/Editors.Shared.Core/Common/SceneObject*`；
- `BinAnimationViewModel`、`AnimationPlayerViewModel`、`EditorHostBase`；
- `GameWorld/View3D` 的 SceneManager、RenderEngine、Gizmo、动画容器和输入；
- `Shared/GameFiles` 中 AnimationMeta 定义、解析和序列化；
- 共享 Meta Editor 列表、字段控件和保存逻辑。

修改前必须列出其他调用者，证明行为是共享事实，说明能否在超级视图适配层解决，并运行相应跨编辑器/格式测试。

### 16.3 禁止的实现方式

- 给超级视图接入 Kitbash 专属组件或模型编辑命令；
- 让参考模型、Prop 或 marker 进入共享模型选择并可编辑；
- 在共享 View3D 中增加 `IsSuperView`、编辑器类型判断或超级视图默认值；
- 把 preview node/world matrix 当作持久数据源；
- 根据 tag 名或值相等代替 `ParsedMetadataAttribute` 引用身份；
- 把两份 Meta Editor 合并成同一个 scoped 文档实例或共用一条历史；
- 将所有 `(0,0)` 时间范围解释为全动画；
- 遇到缺失资源时删除 tag、修改源数据或虚构替代资源；
- 为支持未知空间类型而反射写入未验证字段；
- 保存部分失败后清除全部 dirty；
- 只凭预览测试宣称 WPF/GPU 界面已视觉验收。

### 16.4 常见任务路由

| 任务 | 首查入口 | 同时检查 |
| --- | --- | --- |
| 切动画后预览没更新 | `SuperViewViewModel` 的场景更新链 | `BinAnimationViewModel`、`SceneObjectEditor`、事件订阅 |
| persistent/animation 内容串线 | 两个 Meta Editor 的创建与 `FileOwner` | 活动页签、Edit Session owner、保存事件 |
| 某 tag 没有可视化 | `MetaDataBuilder` | 资源路径、类型定义、fallback、单条异常日志 |
| 选中 tag 后高亮/聚焦错误 | 预览引用身份和 `FocusPosition` | 活动页签、preview outline、骨骼参考变换 |
| 时间显示不对 | `MetaDataTimeRange` | 秒/微秒换算、零范围策略、播放器 seek |
| Gizmo 移动方向/保存坐标错误 | `CombatMetaDataEditSession` | `ReferenceWorldTransform`、局部/世界转换、骨骼 frame |
| Undo/dirty 串到另一页签 | owner 历史映射 | 子编辑器 dirty、saved state、引用身份 |
| 修改后全部预览闪烁/重建 | 增量 `BuildPreview` | 结构/规则 fallback、旧实例和该源诊断清理 |
| 缺失 Prop 导致整个视图失败 | 构建器资源隔离 | 通用 marker fallback、日志、其他实例保留 |
| 超级视图按钮出现在直接 Meta Editor | `MetaDataAttributeView.xaml` 祖先绑定 | Visibility 条件、控件测试 |
| 模型能被点选或编辑 | 场景节点 `IsSelectable` | 是否误插入共享/Kitbash 选择组件 |
| 关闭/切换后重复预览 | instances/rules cleanup | 附属 player、事件、Gizmo 和鼠标所有权 |

## 17. 验证门禁

| 改动范围 | 最低自动验证 | 仍需人工/集成验证 |
| --- | --- | --- |
| 双文档、保存、dirty | `MetaDataEditorViewModelTests`、`MetaDataEditorMultiSelectTests` | 两页签修改、部分缺失、保存/重开 |
| 空间 Gizmo | `CombatMetaDataEditSessionTests`、`SpatialMetaDataCatalogTests` | 根/骨骼附着、平移/旋转、取消/撤销/重做 |
| 预览构建/资源失败 | `MetaDataBuildResultTests`、`MetaDataBuilderMissingResourceTests` | 真实 Fragment、模型、动画、Effect 和诊断表现 |
| 时间与同步 | `MetaDataPreviewTimeTests` | 播放/暂停/seek/循环、瞬时 tag 与整段 tag |
| 检查索引/时间轴 | `MetaDataInspectionIndexTests`、`MetaDataEditorViewModelTests`、`UiAnimationMetadataFamilyGallery` | 双文档同值项、无效范围、点击导航、四主题/高对比度、空状态和缩放 |
| 字段 UI/批量控制 | `MetaDataAttributeControlTests` | 两页签、直接 Meta Editor、中文 UI 和焦点 |
| 格式解析 | `MetaDataFileParserTests` 及格式项目测试 | 真实文件 round trip、未知 payload |
| View3D 隔离与 marker 选取 | `MetaDataMarkerPickingTests`、`View3DComponentIsolationTests` | 单/双击、重叠/远距、播放可见性、Gizmo/相机让路，并确认模型、Prop、骨骼不可选择且无 Kitbash 编辑工具泄漏 |
| XAML/视觉 | 受影响项目构建和布局/本地化测试 | 实际 WPF 主题、缩放、marker、Gizmo 和遮挡 |

若修改共享动画播放器、SceneObject、Gizmo、RenderEngine 或格式层，必须补跑所有受影响调用者的测试。自动测试不能替代本地 WPF/GPU 视觉检查。

## 18. 完成前自检

- 超级视图是否仍只编辑 metadata，不编辑模型/动画资源？
- Persistent 与 Animation 是否仍是独立文档、独立 owner、独立历史？
- 参考模型和 Prop 是否保持不可由模型选择系统编辑？
- 预览是否由解析文档生成，而不是反向成为保存源？
- 局部值、骨骼参考变换与世界显示矩阵是否仍正确分层？
- `(0,0)` 是否按明确 tag 语义处理，而非全局猜测？
- 缺失/无效资源是否只隔离单项并保留原始 tag？
- 增量刷新是否保留无关实例，完整重建是否清理旧实例与规则？
- 所有 gesture、切换、异常和 Close 路径是否释放鼠标、事件、节点和 player？
- 保存失败或部分成功是否保留正确 dirty 与文件 owner？
- 超级视图专属 UI 是否仍只在超级视图祖先下出现？
- 如果改了共享层，是否提供了跨编辑器/格式证据？
- 如果改了 UI/预览，是否完成实际视觉验证并说明未覆盖风险？
