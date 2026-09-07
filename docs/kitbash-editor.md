# KitbashEditor 技术上下文

> 面向修改、审查和验收 KitbashEditor 的 AI。本文记录稳定架构、状态所有权、关键调用链、实现边界和验证路由，不替代当前代码。涉及界面时还必须阅读 [`ui-design-system.md`](ui-design-system.md)。

## 1. 阅读触发与事实优先级

出现下列任一范围时，开始工作前完整阅读本文：

- Kitbash、模型编辑、`.rigid_model_v2`、`.variantmeshdefinition`、`.wsmodel`；
- 对象/面/边/顶点/骨骼选择、Gizmo、比例编辑、橙色轮廓、编辑覆盖层；
- Scene Explorer、节点属性、材质、LOD、动画或骨架；
- Kitbash 保存、子工具、离屏渲染、截图或 Photo Studio；
- 计划修改 `GameWorld/View3D` 中会被 Kitbash 使用的共享输入、选择、变换或渲染代码。

事实发生冲突时按以下顺序处理：

1. 当前代码、项目引用和测试；
2. 本文记录的稳定契约与边界；
3. 其他仓库文档；
4. 历史提交、PR、对话和上游实现。

本文不记录当前测试总数、分支名或提交 SHA。新增类型后不要机械地把每个类抄进本文；只有所有权、入口、状态含义、跨模块契约或高风险边界变化时才更新。

## 2. 编辑器身份与硬边界

KitbashEditor 是本仓库唯一承担模型编辑的编辑器。其职责包括：

- 从 Pack 资源加载可编辑模型、骨架、动画和参考模型；
- 编辑对象层级、网格几何、材质、骨骼和附着点；
- 提供对象、面、边、顶点和骨骼五种选择语义；
- 预览并提交平移、旋转、缩放和比例编辑；
- 通过统一命令历史提供撤销、重做和未保存状态；
- 按游戏和输出格式策略保存几何、材质、LOD 与 `.wsmodel`。

硬边界：

- 其他编辑器中的模型只用于参考、预览或辅助展示，不因此获得模型编辑能力。
- Kitbash 专属的选择输入、选择覆盖层和 Gizmo 编排必须留在 `Editors/Kitbashing/KitbasherEditor/`。
- 不得把 Kitbash 专属组件注册为全局或所有编辑器作用域都能解析的 `IGameComponent`。
- 不得在共享 View3D 组件中通过 `IsKitbash`、编辑器类型判断、模式开关或 Kitbash 默认值实现专属行为。
- 只有对所有真实调用者都成立的行为才能下沉到 `GameWorld/View3D`；仅服务 Kitbash 的策略必须由 Kitbash 组合层持有。

## 3. 模块、注册与组成

### 3.1 主要位置

| 范围 | 位置 | 所有权 |
| --- | --- | --- |
| Kitbash 主模块 | `Editors/Kitbashing/KitbasherEditor/` | 编辑器入口、界面、专属交互、子工具 |
| Kitbash 测试 | `Editors/Kitbashing/Test.KitbashEditor/` | 模块级行为、保存、渲染、子工具 |
| 共享 View3D | `GameWorld/View3D/` | 中性场景、输入原语、选择状态、Gizmo 原语、渲染和保存策略 |
| 共享模型/格式 | `Shared/`、`GameWorld/` 其他项目 | 文件格式、场景节点、动画、数学和 GPU 原语 |
| 应用组合根 | `AssetEditor/` | 模块发现、编辑器宿主、窗口和 Pack 生命周期 |

项目入口是 `Editors/Kitbashing/KitbasherEditor/DependencyInjectionContainer.cs`。它注册 scoped 的主视图/VM、场景创建、保存策略、菜单、节点编辑器和子工具，并在 `RegisterTools` 中声明 Kitbash 编辑器及扩展名优先级。不要绕过该入口在 View 或命令内部创建另一套服务容器。

### 3.2 View3D 组件组成

Kitbash 的 View3D 由两层组成：

1. `View3DCoreComponentSet`：所有 View3D 宿主可复用的核心组件，例如键鼠、相机、场景管理、渲染、网格、动画和灯光。
2. `KitbashSceneComponentSet`：只属于 Kitbash 的 `KitbashSelectionInputComponent`、`KitbashModelGizmoComponent` 和 `KitbashSelectionOverlayComponent`。

`ComponentInserter` 先确保 `IWpfGame` 已创建，再按顺序插入核心组件和显式给出的编辑器组件。`KitbasherViewModel` 是 Kitbash 专属组件的实际插入入口。

`KitbashSceneComponentSet` 有意手工构造选择覆盖层、选择输入、模型 Gizmo 和选择形态快捷键组件，而不是让它们成为可全局解析的独立服务。选择形态快捷键在共享输入之后、相机之前处理，但不预占鼠标；圈选与点选、框选一样，在相机和 Gizmo 处理输入之后才申请左键手势的所有权。这是隔离契约，不是待清理的重复代码。`Testing/AssetEditorTests/View3DComponentIsolationTests.cs` 对以下事实设有门禁：

- 组合器只添加核心组件和调用方显式提供的组件；
- 参考预览组件不会夹带 Kitbash 编辑组件；
- 应用容器不全局注册 Kitbash 专属组件或任意 `IGameComponent`。

## 4. 主对象图与状态所有权

| 对象 | 主要职责 | 不应承担的职责 |
| --- | --- | --- |
| `KitbasherViewModel` | 编辑器生命周期、加载/保存入口、主 VM 组合、dirty 汇总 | 逐帧拾取、GPU 覆盖层绘制、格式解析细节 |
| `KitbasherRootScene` | Kitbash 根节点、主 `AnimationPlayer`、骨架查找/变更 | WPF 菜单和保存对话框 |
| `KitbashSceneCreator` | 从 Pack 文件建立规范场景树和初始保存设置 | 处理用户手势或直接保存输出 |
| `SelectionManager` | 当前选择模式与选择状态的共享状态机 | 解释 Kitbash 快捷键、绘制 Kitbash 覆盖层 |
| `KitbashSelectionInputComponent` | Kitbash 鼠标/快捷键、拾取和选择命令 | 变换预览、持久化文件 |
| `KitbashModelGizmoComponent` | Kitbash Gizmo/模态变换手势编排 | 通用变换数学的第二份实现 |
| `TransformGizmoWrapper` | 把各类选择包装为可预览、提交、取消的变换目标 | 判断当前编辑器是谁 |
| `KitbashSelectionOverlayComponent` | Kitbash 各选择模式的可视化 | 修改选择状态或保存数据 |
| `CommandExecutor` | 撤销/重做、文档状态 ID、命令事件 | 直接决定 UI 是否显示按钮 |
| `SceneExplorerViewModel` | 场景树与对象选择同步 | 面/边/顶点选择 |
| `SceneNodeEditorFactory` | 为单个受支持节点创建右侧属性编辑器 | 兜底编辑任意未知节点 |
| `SaveService` | 验证并按策略写出几何、材质和 LOD | 弹出 WPF 对话框或改动选择 |

必须区分三种状态：

- 场景/模型状态：`KitbasherRootScene` 及其节点、几何、材质、骨架和动画；
- 交互状态：`SelectionManager`、活动 Gizmo、鼠标所有权、变换预览和覆盖层缓存；
- 文档状态：`CommandExecutor.CurrentDocumentStateId` 与 `KitbasherViewModel` 保存时记录的状态 ID。

不要用 UI 选中、GPU 预览或树展开状态代替真实模型变更，也不要让纯选择操作把文档错误地标记为已修改。

## 5. 加载与规范场景树

加载入口位于 `KitbasherViewModel`，场景构建由 `KitbashSceneCreator` 完成。核心流程：

1. 根据 Pack 文件类型读取 `.rigid_model_v2`、`.variantmeshdefinition` 或 `.wsmodel`。
2. `.wsmodel` 先解析其几何引用；`.variantmeshdefinition` 作为参考资产处理，并把默认输出指向 `.rigid_model_v2`。
3. 建立固定根结构：骨架节点、`MainEditableNode` 和参考模型组。
4. 将 LOD/网格装入主可编辑节点，初始化主动画播放器。
5. 依据模型声明解析 `animations\skeletons\<name>.anim` 骨架资源。
6. 从首个适用的 weighted material 复制附着点，并建立初始输出路径和 LOD 设置。
7. 加载完成后更新当前文件、显示名、相机焦点和已保存文档状态。

场景树不变量：

- `MainEditableNode` 是可保存模型的权威根；没有可编辑内容时不能伪造成功保存。
- 参考模型进入独立参考组，默认不可编辑、不可选择，不能混入主输出。
- 骨架是网格蒙皮、骨骼选择、附着点和动画的共同参考；替换骨架必须通过既有事件和刷新链路传播。
- 加载器建立的特殊根节点是结构契约，删除/重分组命令必须继续保护它们。

修改加载支持时，同时检查场景树、保存设置初始化、参考/可编辑标记、骨架与动画连接以及对应 round-trip 测试。不能只让文件“能打开”。

## 6. 选择系统

### 6.1 模式与状态

`SelectionManager` 是共享状态机，当前模式为：

- `Object`：场景节点选择；
- `Face`：网格三角面选择；
- `Edge`：拓扑边选择；
- `Vertex`：顶点选择，可带比例编辑权重；
- `Bone`：骨骼选择。

它发布状态和选择变更事件，并能复制/恢复选择状态。共享状态机不应知道 Kitbash 快捷键或覆盖层样式。

选择/模式命令通常需要撤销，但不属于文档内容变更：它们可以 `IsMutation=true` 进入交互历史，同时必须保持 `AffectsDocument=false`。否则仅点击对象或切换模式就会触发“未保存”。

### 6.2 Kitbash 输入解释

`KitbashSelectionInputComponent` 拥有 Kitbash 语义：

- 左键点选和框选；Shift 点选切换该元素，Shift 框选添加，Ctrl 移除；
- 穿透选择关闭时，点/边/面框选按当前显示姿态检查自身遮挡，不能穿透选中背面；面按框内可见表面命中，边优先完整包含、无完整边时再选相交边。近裁剪面截断的几何仍参与可见性判断；
- 根据当前模式选择对象、面、边、顶点或骨骼；
- 编辑模式的拾取不得回落成对象拾取；
- 对象、点、边、面模式均支持 `A` 全选、`Alt+A` 取消全选、`Ctrl+I` 反选；重复全选不增加历史，对象全选遵循可见性与锁定层级；`Ctrl+L` 沿拓扑扩展相连元素；
- 边切换为面模式时，只有三条边全部选中的三角面才进入面选择；
- `F9` 进入骨骼模式；其他模式切换由 Gizmo 组件配合完成；
- 拖框期间申请共享鼠标所有权，结束或取消时必须释放；
- 组件释放时清理临时纹理、事件和鼠标状态。

动画或蒙皮模型的面/边/顶点拾取必须以 `MeshPoseSnapshot` 的当前变形后世界坐标为准；只在静态路径使用 `RenderMatrix`。若拾取仍使用原始顶点，动画播放时视觉位置与命中位置会分离。

### 6.3 场景树选择

`SceneExplorerViewModel` 和 `MultiSelectTreeView` 只同步对象级选择，保留展开、可见性等树状态，并通过 `SceneNodeSelectedEvent` 驱动右侧编辑器。它不承载面/边/顶点选择。

未知节点、多选或空选不应猜测属性编辑器：`SceneNodeEditorFactory` 只为明确支持的 `MainEditableNode`、`Rmv2MeshNode`、`SkeletonNode`、`GroupNode` 建立编辑器；切换目标时释放旧编辑器，重复选择同一节点时复用当前编辑器。

## 7. 变换、撤销与 dirty

### 7.1 变换链

变换链为：

`KitbashModelGizmoComponent` / 数值工具 → `TransformGizmoWrapper` → 预览备份 → 命令提交 → `CommandExecutor`。

`KitbashModelGizmoComponent` 负责：

- 将共享 `Gizmo` 配置为当前选择的 Kitbash 交互；
- `G/R/S` 模态平移、旋转、缩放；
- `Tab` 在对象与上次编辑子模式之间切换；数字 `1/2/3` 进入顶点/边/面；
- `G` 使用视口投影跟随鼠标；`R` 累积鼠标绕冻结中心的角度；`S` 使用到中心的距离比例，越过中心可镜像。鼠标坐标按 WPF 视口尺寸换算，不使用固定像素灵敏度；
- `G/R/S` 从按键按下时的鼠标位置开始；按键到首帧之间的位移不得丢失，长按按键不得重复启动手势；
- Shift 精调时移动/缩放使用 1/10 增量、旋转使用 1/30 角度增量；按下或松开 Shift 不得跳变。移动/缩放支持边缘回绕，旋转保持中心参考；
- 模态 Ctrl 吸附步长为平移 1 单位、旋转 5°、缩放 0.1；Ctrl+Shift 为 0.1 单位、1°、0.01；
- 数字输入实时预览且不受吸附影响；左键或 Enter 确认同一预览，右键或 Escape 取消，结束时清空数字状态；
- 同轴连按在当前空间、另一空间、解除约束之间循环（默认世界→局部→解除）；局部方向固定在手势开始时，平移、旋转、缩放均按该方向执行；
- 模态和工具栏拖动期间，选择快捷键不得中断当前变换；
- 工具栏轴柄在按下时重新命中测试，使用同一模态预览数学，松开左键提交；不能重新使用逐帧截断位移或逐帧舍入吸附的旧拖动路径；
- 移动/缩放 Gizmo 的轴线、轴端和平面区域均参与命中，坐标须与实际绘制矩阵一致；平面悬停高亮对应边界。选择被中断后的旧鼠标松开不能重新启动手势；
- 网格的“选区中心”旋转/缩放使用 Gizmo 显示的中心，只有“世界原点”使用零点；点、边、面的预览、确认和撤销须保持同一轴心；
- Toolbar Gizmo 的显示开关与模态变换分离；隐藏 Toolbar Gizmo 不应禁用 `G/R/S`；
- 选择变化时取消旧手势、释放旧 wrapper、从新选择重建；
- 开始时取得鼠标所有权，所有成功、取消和异常路径都释放所有权。

### 7.2 预览事务

`TransformGizmoWrapper` 位于共享 View3D，负责对象、顶点、面、边、骨骼的通用变换事务：

1. `Begin` 固定本次目标、选择和比例权重，并捕获 CPU/GPU/动画基线；
2. Preview 只更新显示状态，不创建多条历史；
3. Commit 生成一个可撤销命令并释放备份；
4. Cancel 恢复几何、骨架帧、包围盒和 GPU 显示；
5. 失败时优先保留首个异常，同时尽量完成恢复和资源释放。

手势中途改变选择、模式、比例权重、宿主生命周期或鼠标所有权，都不能留下半应用状态。不能把“每一帧 preview”当作独立命令，也不能在提交前丢弃基线。

### 7.3 文档状态

`CommandExecutor` 用 `CurrentDocumentStateId` 表示可保存文档内容的历史位置。`KitbasherViewModel` 保存成功后记录该 ID，之后以是否相等判断 dirty。

- 属性、几何、材质、层级、骨骼等真实变更必须通过命令并影响文档状态。
- 选择、选择模式、相机、焦点、视口着色和纯预览不能影响文档状态。
- 失败的 Execute/Undo/Redo 不能破坏 undo/redo 栈或伪造新状态。
- 直接修改模型对象而不经过既有命令体系，会破坏 dirty、撤销和保存一致性；除明确的加载/初始化/预览路径外禁止这样做。

## 8. 渲染与选择覆盖层

`KitbashSelectionOverlayComponent` 是 Kitbash 专属可视化层。当前视觉语义：

| 模式 | 主要覆盖层 | 整体对象橙色轮廓 |
| --- | --- | --- |
| Object | RenderEngine 选择轮廓 | 有 |
| Face | 选中面填色与橙色边、活动面白色边框、线框 | 有 |
| Edge | 全线框、选中/活动边、完整选中面的填色 | 有 |
| Vertex | 顶点标记、屏幕空间边/动画线框、完整选中面的填色 | **没有** |
| Bone | 骨架/骨骼选择显示 | 不由模型整体轮廓替代 |

“顶点模式不显示整个对象橙色轮廓”是明确模式边界，不能为了复用 Face/Edge 分支而恢复。Face 和 Edge 当前仍有整体轮廓，不能把顶点修复扩大到它们。

点、边组成完整三角面时显示与面模式相同的选中填色；不完整的三角面不填色，显示推导不得修改选择状态或交互历史。顶点标记在面填色之后绘制，活动点保持可辨认。Kitbash 的选中点、边、面不会因缩远或网格密度升高而整体淡出，普通未选网格细节仍可降低透明度；穿透状态继续区分可见与被遮挡元素。线框模式的面中心标记独立于穿透开关，实体和材质预览仍在穿透时显示面中心。

动画网格的覆盖层必须使用当前 `MeshPoseSnapshot`，保证线框、顶点和实际变形模型一致。静态密集顶点路径使用屏幕空间边实例并设容量上限，优先保留选中顶点；修改此路径要关注：

- 拓扑/位置/矩阵缓存何时失效；
- 选中与活动元素的颜色、透明度、深度偏移和遮挡关系；
- GPU buffer、render item、事件订阅和临时实例是否释放；
- 离屏像素测试与真实 WPF/GPU 视觉验收是否都覆盖。

共享 `RenderEngineComponent`、选择 mask、Effect 参数和 render item 是高扩散面。若问题只出现在 Kitbash 某模式，优先在专属覆盖层修复；只有证明共享渲染契约本身错误，才修改共享层并验证其他 View3D 使用者。

## 9. 界面、菜单与输入焦点

主界面位于 `Core/KitbasherView.xaml`：左侧为菜单、Blender 风格工具栏、View3D、动画条和数值变换浮层；右侧为 Scene Explorer 与节点属性编辑器。布局尺寸、分隔条和样式复用 `KitbashUiStyles.xaml` 及共享 `Ae*` 资源。

输入分两层：

- WPF 菜单/快捷键：`MenuBarView` 挂接宿主窗口，在加载时订阅、卸载时解除；隐藏或非活动标签页不得响应；文本输入控件中的按键不得触发编辑命令。
- View3D 键鼠：共享输入组件只在渲染焦点有效时读取状态，并记录两帧之间的短按释放（包括 WPF 已处理的 Tab）；失去键盘焦点或应用激活变化要清除陈旧按键跃迁；鼠标使用单一所有者协议。
- 相机拖动和变换显式申请共享连续鼠标输入：手势期间必须同时捕获鼠标并用原生裁剪将系统光标限制在 View3D 内，消息处理前也不能越出视窗；触边后从对边继续。提示文字使用实际光标坐标。结束、失焦、失去捕获、视口布局变化或卸载必须释放捕获并恢复此前的裁剪；Kitbash 同时取消未提交预览。旋转保留屏幕中心参考，不回绕角度输入。
- `WM_MOUSEMOVE` 只触发采样：输入层在宿主窗口的 DPI 环境下累计尚未消费的系统光标位移，回绕后立即读取实际落点并重置物理采样基准；回绕本身不能改变连续坐标，不累计整幅视窗偏移。旧边缘坐标、旧内部坐标及回绕回声都不能使静止光标产生位移。渲染帧及 WPF 合成事件不得轮询、重设光标或推进拖动。原生裁剪被其他所有者替换时取消预览，不能覆盖对方限制。验证须覆盖连续相对输入、原生捕获范围、回绕前后重放旧消息、正反微移和渲染 DPI 环境变化。
- 键鼠坐标统一使用 WPF 视口的逻辑尺寸，绘制坐标才换算为渲染像素；拾取射线、点/边命中、正交框选及导航轴点击必须遵守同一约定。渲染焦点禁用输入法组字，不影响文本框的中文输入。
- 平移按初始轴心深度把连续鼠标位移换算为视图平面向量，再应用轴／平面约束，不得把控件抓取位置作为透视约束的投影起点。视线与约束轴夹角小于 5° 时，沿用 Blender 的纵向深度控制，水平换边不能把模型推入远处。回归验证必须连接原生鼠标输入、Gizmo、模型顶点预览及撤销，不能仅以鼠标坐标连续判定变换正确。
- 鼠标按下、松开和按下位置跨帧保留，快速框选或点击不能只依赖帧末按钮状态；回绕、确认后的状态清理不能把光标重置为零坐标或制造滚轮增量。结束变换只忽略该次手势的鼠标松开，不得吞掉随后新按下的点击或框选。

视图导航沿用游戏的 Y 向上坐标，不改变模型格式轴向。六个轴视图必须完全垂直于对应轴；正交/透视切换保持焦点处的显示比例，Shift＋中键按屏幕平面平移，Ctrl＋中键缩放。中键绕转可连续越过顶部、底部及整圈边界；从倒置视角开始拖动时，横向方向按该次手势开始时的画面朝向固定。视图切换动画必须接受后续方向键请求，手动导航、聚焦和重置优先于旧动画，不能被下一帧覆盖。

右上角导航坐标与当前视线重合时，正负轴端点重叠；命中判断必须排除朝向观察者的端点，使点击切换到反向视图，并允许再次点击返回。其他轴端点和数字小键盘仍切换到各自指定方向。

中键导航用 Escape 或右键取消时恢复手势开始的焦点、角度、距离和投影类型，并释放捕获；中键仍按住时不得重启旧手势。失焦或捕获中断后停止导航，等待新的按下。鼠标遮挡判断使用视口实际所属窗口，不依赖主窗口类型名称。

聚焦对象、点、边、面时使用与当前渲染一致的世界坐标（包括对象层级、旋转、缩放和动画姿态）；按选中范围中心及视窗宽高比调整透视距离或正交显示范围。空选不移动相机，单点只居中并保留缩放。聚焦和相机重置不修改几何或文档状态。

第四组编辑交互的稳定约定：

- 穿透选择仅通过右上角图标切换，无快捷键，使用 Kitbash 局部状态。编辑模式开启后，点线面选择可穿过遮挡；对应覆盖层也穿透深度，面中心标记用于穿透状态下的点击拾取。正常选择继续使用可见性判断。该状态不修改模型或材质，也不进入照片导出。
- 编辑模式通过左侧鼠标图标下方的圈选按钮或 W 切换普通鼠标与圈选鼠标，按钮高亮与快捷键共享状态。圈选只改变左键选择形状：左键涂选，Ctrl+左键移除，Shift 不擦除；连续划过的路径参与选择，不能只检测每帧端点。中键、Shift+中键、Ctrl+中键及滚轮继续正常导航，G/R/S 和工具栏 Gizmo 的变换、确认、取消、撤销不改变选择形态。小键盘 +/- 调整圈选半径。每笔松开鼠标后提交一步选择撤销并释放捕获，不标记模型未保存；Esc、右键或失焦结束当前涂选，保留选择结果与圈选形态。点击普通鼠标图标或 W 可返回点选/框选；离开点、边、面编辑模式后停用圈选。
- 圈选时，无 Ctrl/Shift 的空白单击在松开时清空当前点、边、面选区，保留圈选和编辑模式，可一步撤销；拖刷经过空白、按住 Ctrl/Shift、圈内命中已有元素或中断未完成的点击，都不触发整组选区清空。
- 连按 R 切换屏幕平面旋转与视图双轴自由旋转。RR 使用开始时的视图基、连续鼠标位移与轴角合成；Shift 为 1/30 精调，Ctrl 为 5° 吸附，Ctrl+Shift 为 1°，确认／取消复用同一变换备份。
- G/R/S 数值输入按文本事件顺序处理。`=` 进入表达式输入，支持括号、`+ - * / // % ** ^`、科学记数法、`pi/tau/e` 和常用数学函数（如 `sin`、`sqrt`、`abs`、`min/max`、`radians/degrees`），不执行任意脚本。简单数值输入保留减号切换正负的习惯，表达式中的减法使用 `=` 模式。Tab／Shift+Tab 切换分量，RR 有两个角度分量；箭头、Home/End、Backspace/Delete 与 Ctrl+A 可编辑，Ctrl+V 可粘贴表达式。未完成或无效表达式保留上次有效预览且不可确认。

Blender 风格表示上述交互约定，不代表完整建模功能等价：目前仍没有变换方向／轴心饼菜单、四边面建模拓扑、任意 Python 数值求值或场景单位换算；缩放还受可逆变换安全范围约束。

不要为同一快捷键在 View、VM 和 GameComponent 各写一份处理。先确定它属于 WPF 命令还是逐帧 View3D 手势，并保持焦点、文本输入和生命周期门禁。

菜单和工具栏由 `MenuBarViewModel`、`ToolbarBuilder` 及命令类组合。可见性/可用性应来自当前选择模式和选择内容，不要在 XAML 中复制一套业务判断。数值变换仍必须走 `TransformGizmoWrapper` 的 begin/commit/cancel；比例编辑禁用时传递零权重范围，启用时半径不得低于现有最小值；视口着色只改变渲染设置，不得污染文档 dirty。

视口着色的稳定约定：

- 线框、实体、材质预览仍通过右上角三个按钮互斥切换；相邻下拉面板只显示当前模式适用的设置。模式切换保留各自设置，“恢复默认”只重置当前模式，不清空选区或改变鼠标工具。
- 线框先填充模型深度，再绘制可见边；穿透关闭时不显示被表面挡住的背面线框，保持与可见性选择一致。穿透开启后，背面点线面降低不透明度，选中元素保留更强的高亮；实体和材质预览的表面不透明度可单独调整。穿透不隐式切换快捷键或选择模式。
- 实体提供灰色工作室和陶土、金属 MatCap（按视图法线采样的观察材质），不读取模型贴图。凹槽增强和接触阴影使用当前姿态的屏幕空间法线、深度，支持透视与正交；这是视窗观察效果，不等同于离线渲染阴影。
- 材质预览可沿用游戏光照，或使用柔光工作室、阴天、暖阳环境，并调整强度、环境旋转。局部环境使用自己的光照，避免被固定正面灯盖住。设置和生成的光照纹理由当前视窗拥有，不写入模型材质、不改变全局光照或照片导出；共用 Effect 每次绘制都必须重置这些局部参数。

## 10. 节点属性与材料编辑

右侧节点编辑器是场景树对象属性的编辑入口。稳定约束：

- 属性修改通过 `SceneNodePropertyEditor` 包装共享命令，确保 dirty、撤销和 UI 回读一致；
- 初始化 ViewModel 时读取值不能产生变更命令；
- 相同值写回不创建无意义历史；
- 预览属性（例如阵营颜色）与持久属性要分开；
- 材质子视图按真实材质类型路由，不建立“万能材质”兜底写入；
- 骨架、附着点、网格名称、可见性和动画等变化要通知对应显示/保存链路。

新增节点编辑器前先判断该节点是否真能独立编辑。参考组、根节点或未知节点不应因为存在公共属性就自动获得编辑器。

## 11. 保存管线

保存入口由 `SaveCommand`/Save As 命令发起，核心服务位于 `GameWorld/View3D/Services/SceneSaving/`。流程：

1. 首次保存或 Save As 显示 `SaveDialog`；取消必须返回取消结果，不写文件、不清 dirty。
2. 从 `MainEditableNode` 取得可编辑模型；缺失时保存失败。
3. 将当前附着点和用户确认后的 LOD 草稿写入 `GeometrySaveSettings`。
4. `SaveService` 依次执行验证、LOD 策略、几何策略和材质/`.wsmodel` 策略。
5. 全部成功后发布 `ScopedFileSavedEvent`。
6. `KitbasherViewModel` 仅在成功事件后更新文件身份和已保存文档状态。

`SaveDialogViewModel` 对 LOD 设置使用草稿：浏览和输入不应立即改动真实场景；只有 Apply/确认才持久化。文件选择使用 `IStandardDialogs`，不要在模块里新增另一套原生对话框调用。

保存相关修改至少要核对：

- 游戏版本与几何格式策略选择；
- LOD 生成、可见距离、空 LOD 和网格过滤；
- 材质、贴图、附着点、骨架和 `.wsmodel` 引用；
- Save/Save As/取消/失败时的文件身份与 dirty；
- load-save round trip 是否保持格式语义，而不只是成功生成字节。

## 12. 动画、骨架与附着点

`KitbasherRootScene` 持有主 `AnimationPlayer`，其暂停时不做冗余刷新。骨架变化通过 `KitbasherSkeletonChangedEvent` 和 handler 传播到网格、附着点、动画及相关 UI。

修改骨架或动画时必须同时考虑：

- 当前动画 pose 与静态 bind pose；
- 蒙皮网格拾取、覆盖层和 Gizmo 预览；
- 骨骼选择/变换的世界空间与父子层级；
- 附着点的骨骼索引、保存和重新加载；
- 暂停状态下是否主动刷新显示；
- 替换骨架后旧订阅、旧 resolver 和旧选择是否失效。

骨骼变换和网格顶点变换共享部分数学/事务设施，但数据恢复路径不同。不要用网格备份替代骨架帧备份，也不要把骨骼命令降为纯视觉预览。

## 13. 子工具

子工具属于 Kitbash 编辑工作流，由 Kitbash DI 作用域创建；它们不是独立的全局模型编辑器。

| 子工具 | 职责 | 关键边界 |
| --- | --- | --- |
| BMI Editor | 查看/筛选/修改 bone matrix influence | 必须保持骨骼索引和权重合法 |
| Mesh Fitter | 骨骼映射、相对缩放和偏移 | 修改必须可撤销并刷新网格显示 |
| Re-Rigging | 重映射骨骼索引 | 保留 undo，不能静默丢失未映射权重 |
| Pin Tool | 将网格固定到顶点或执行 skin wrap | 使用现有加速/权重转移算法，不在 UI 线程写第二份算法 |
| Photo Studio | 相机设置、离屏捕获、导入导出 | 复用 RenderEngine；捕获完成不等于主模型已保存 |
| Vertex Debugger | 观察选中顶点与诊断数据 | 诊断显示不得修改模型 |
| Save Dialog | 输出路径、格式、LOD 策略草稿 | 只有确认后改真实保存设置 |

子窗口关闭、编辑器关闭或作用域释放时必须解除事件、释放 GameComponent/GPU 资源并停止持有主场景。不要把子工具注册成跨编辑器单例。

## 14. 修改边界与路由

### 14.1 默认允许在 Kitbash 模块内修改

- Kitbash 专属界面、菜单、快捷键解释和场景树行为；
- Kitbash 专属选择模式编排、覆盖层和 Gizmo 交互；
- Kitbash 的加载组合、节点属性编辑和子工具；
- Kitbash DI 注册及模块测试。

仍需遵守最小修改、命令历史、生命周期和 UI 规范。

### 14.2 修改共享层前必须证明

下列位置可以修改，但必须先证明问题是通用契约而非 Kitbash 特例，并扩大验证：

- `GameWorld/View3D/Components/Selection/SelectionManager.cs`；
- `GameWorld/View3D/Components/Gizmo/TransformGizmoWrapper.cs`；
- `GameWorld/View3D/Components/Rendering/` 与 RenderEngine；
- `GameWorld/View3D/Components/Input/`；
- `GameWorld/View3D/Services/CommandExecutor.cs`；
- `GameWorld/View3D/Services/SceneSaving/`；
- 公共场景节点、动画、数学和 GPU 资源类型。

证明至少回答：其他调用者是谁；行为是否对所有调用者成立；能否在 Kitbash 组合层完成；现有共享测试覆盖什么；需要补哪些隔离/回归测试。

### 14.3 禁止的实现方式

- 在共享组件里判断编辑器类型或新增 Kitbash 模式开关；
- 全局注册 Kitbash 专属 GameComponent；
- 让参考/预览编辑器获得对象、网格或骨骼编辑能力；
- 直接修改模型而绕过命令、dirty 和撤销；
- 把 GPU preview 当作权威数据或把选择状态当作文件内容；
- 为修复某一种选择模式而无证据改变其他模式；
- 在未验证其他 View3D 使用者时改变共享选择、输入、Gizmo 或渲染默认值；
- 只凭自动渲染测试宣称视觉已经验收。

### 14.4 常见任务路由

| 任务 | 首查入口 | 同时检查 |
| --- | --- | --- |
| 选不中/选错元素 | `KitbashSelectionInputComponent` | `SelectionManager`、`MeshPoseSnapshot`、输入焦点 |
| 变换无法确认/取消 | `KitbashModelGizmoComponent` | `TransformGizmoWrapper`、鼠标所有权、命令历史 |
| 轮廓/线框/顶点显示错误 | `KitbashSelectionOverlayComponent` | RenderEngine mask、动画 pose、离屏像素测试 |
| 点击后误报未保存 | 命令的 `AffectsDocument` | `CommandExecutor`、`KitbasherViewModel` 保存状态 ID |
| 场景树与视口选择不同步 | `SceneExplorerViewModel` | `SelectionManager`、事件发布和多选语义 |
| 属性无法撤销或不变 dirty | `SceneNodePropertyEditor` | 对应节点 VM、命令事件 |
| 打开文件内容不完整 | `KitbashSceneCreator` | 文件解析、骨架/动画、参考标记、初始保存设置 |
| 保存后重开丢数据 | `SaveService` 和策略 | Load/Save round trip、附件/材质/LOD/路径 |
| 子工具修改不生效 | 对应 `ChildEditors/` | 命令、场景刷新、作用域和关闭清理 |
| 仅 Kitbash 出现共享 View3D 问题 | 先查 Kitbash 组合层 | 证明后才进入共享层 |

## 15. 验证门禁

验证必须按实际改动选择，不能用一个测试集冒充全部覆盖。

| 改动范围 | 最低自动验证 | 仍需人工/集成验证 |
| --- | --- | --- |
| 组件隔离/DI | `View3DComponentIsolationTests`、受影响项目构建 | 打开非 Kitbash View3D，确认无编辑组件泄漏 |
| 选择/快捷键 | Kitbash 选择与菜单回归测试 | 点选、框选、组合键、文本输入、切换标签 |
| Gizmo/变换 | `TransformGestureTests`、`TransformUploadTests` | 对象/点/边/面/骨骼的确认、取消、连续手势 |
| 覆盖层/渲染 | `RenderEngineSelectionMaskOffscreenTests` 及相关 Effect 测试 | 实际 GPU/WPF 视觉检查；逐模式检查 |
| 节点属性/树 | `SceneNodeEditor*Tests` | 多选、空选、右键菜单、属性面板生命周期 |
| 加载/保存 | `LoadAndSave/`、SaveDialog 与 round-trip 测试 | 用真实 Pack 文件重开并核对游戏内语义 |
| 子工具 | 对应 MeshFitter/Pin/ReRigging/PhotoStudio 测试 | 子窗口关闭、主编辑器切换、实际输出 |
| UI/XAML | 布局/本地化测试、Release 构建 | 实际中文界面、缩放、主题和焦点 |

如果修改了共享 View3D，再运行所有受影响调用者的测试和架构门禁。UI、轮廓、材质或离屏渲染的自动测试只能说明已覆盖的契约；最终外观必须经过本地视觉验收。

## 16. 完成前自检

- 改动是否仍只让 Kitbash 拥有模型编辑能力？
- 专属行为是否留在 `Editors/Kitbashing/KitbasherEditor/`？
- 是否保持核心组件与显式编辑器组件的组成方式？
- 选择、preview、文档 dirty、磁盘输出是否仍是不同状态？
- 所有成功、取消、异常、关闭路径是否释放鼠标、事件和 GPU 资源？
- 模型变更是否可撤销，纯选择/相机/视口变化是否不 dirty？
- 动画 pose 下的拾取、覆盖层和变换是否使用同一显示几何？
- 保存失败或取消是否保持文件身份与 dirty？
- 如果改了共享层，是否列出了全部调用者并提供跨编辑器证据？
- 如果改了 UI/渲染，是否完成实际视觉验证并说明未覆盖风险？
