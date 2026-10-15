# 动画重定向布局与播放改进验收记录

本文是用户要求保留的阶段归档，不替代当前实现或后续修改的验证。稳定契约见 [UI 规范](../ui-design-system.md)、[总体架构](../architecture.md)、[KitbashEditor](../kitbash-editor.md)及根 [AGENTS.md](../../AGENTS.md)。

## 人工验收与完整交付范围

2026-10-15，用户确认：“OK 我测试过没问题了，动画重定向任务可以收尾结束了”。此确认覆盖本轮动画重定向任务的人工验收；用户未逐项声明所有其他编辑器、主题、游戏资源和第三方工具的兼容性，不据此补写未执行的测试。

本轮位于 `D:\Project\Szy-Cathay\TheAssetEditor-CN`，分支为 `codex/animation-retarget-layout`。建立归档时，HEAD、本地 `master`、`origin/master` 及远端查询到的 `master` 均为 `684439a2bc480a7bb3d8397fdeb7f40d62c791c5`，本轮成果尚未提交。预期目标为 `Szy-Cathay/TheAssetEditor-CN` 的 `master`；本文不是提交、推送、PR、合并或发布成功的证明。

本轮包含以下全部成果，不只包含最后一次工具栏调整：

- 动画重定向布局：保留左侧全局活动栏和资源树；源、生成结果、目标共享场景，播放器居于场景下方；右侧按“来源、目标、重定向调整”组织真实功能，保留独立滚动、可拖分隔条、模型与动画输入、骨骼映射、参数配置、应用和保存。
- 中文名称与操作含义：区分提供动作的来源与接收动作的目标，明确骨骼参数、动画集、关联文件、预览间距及保存选项。参数仍须手动应用；单条保存使用已生成结果，未改变重定向算法或另建保存流程。
- 播放修复：由有效主动画提供进度，阻止滑块范围同步反向重置播放；来源变更使旧结果失效，目标动画不会抢占已有来源或结果；定位保留播放/暂停，循环和末尾暂停由共享控制协调。
- 模型交互隔离：非 Kitbash 的模型点选、框选、轮廓和模型 Gizmo 默认关闭；保留相机操作、关键帧骨骼、超级视图元数据标记、CSC 非模型通道及骑乘附着点操作。骑乘顶点选择直接限定到当前坐骑，不依赖整模型选择。
- 工具栏数字框：预览间距输入框适配紧凑工具栏行高，文字居中并留出上下间距，保留窄窗口换行。
- 对应本地化、定向测试、离屏图库、项目规则、长期文档和本归档。

## 已有验证证据

以下结果来自不同阶段，存在重叠和复用，**不能相加宣称为一次新的全量测试**。本次收尾只修改规则和文档，不重建产品、不重跑这些测试。证据位于 `artifacts/animation-retarget-layout/`。

| 阶段与记录 | 实际结果及复用关系 |
| --- | --- |
| 布局初期：`targeted-checks.trx`、`save-scroll-final.trx` | 分别 30 通过、1 通过；均 0 失败、0 跳过。后续布局检查有重叠 |
| 中文名称：`naming-check/naming-check.trx` | 12 通过、0 失败、0 跳过 |
| 播放修复：`playback-before.trx`、`retarget-final.trx` | 修复前 7 项失败；修复后包含真实动画数据的 9 项播放回归全部通过。该次整个筛选实际为 38 通过、1 失败、0 跳过，失败项是仍要求隐藏目标动画控件的旧静态期望，不能把此日志写成 39 项全绿 |
| 修正布局期望：`layout-final.trx` | 重建测试项目后 21 通过、0 失败、0 跳过，包含上项旧期望的修正检查；其余未变化的播放结果复用 |
| 共享播放器：`shared-player.trx` | 8 通过、0 失败、0 跳过；单独保留，不与其他阶段合计为全量结果 |
| 交互隔离与输入框修复前：`viewport-before.trx` | 13 项失败，覆盖默认选择器初始化及四主题、三种缩放下的输入框高度；修复后的对应检查见下一行 |
| 最后定向验证：`viewport-regression.trx` | 63 通过、0 失败、0 跳过，覆盖模型交互能力、组件隔离、Gizmo 订阅、骑乘工具、重定向布局、中文资源及 XAML 结构 |
| 最后 CSC 验证：`viewport-csc.trx` | 13 通过、0 失败、0 跳过 |
| 最后元数据选择验证：`viewport-metadata.trx` | 9 通过、0 失败、0 跳过 |
| 最后相机验证：`viewport-camera.trx` | 5 通过、0 失败、0 跳过 |

最后四条测试命令共 90 项定向检查，均退出 0；本次收尾复用这些结果。`layout-first.trx` 因测试宿主连接超时而中止，执行 0 项，不能记为通过；`layout-second.trx` 的早期布局失败及中间构建失败日志同样保留。最终状态依据后续对应检查，不删除失败记录来营造全程通过。

本地构建使用 Debug。最后的 `viewport-targeted-build.log` 记录 GameWorld、动画工具、重定向、主程序及四个受影响测试项目成功构建，八条命令均退出 0；构建命令为 `dotnet build <项目> --configuration Debug --no-restore -p:BuildProjectReferences=false -m:1 /nr:false`。最终测试使用 `dotnet test <项目> --configuration Debug --no-build --no-restore --filter <对应筛选> --logger <TRX 日志> --results-directory artifacts/animation-retarget-layout`，具体范围由上述 TRX 中的用例清单确定。已有警告未扩展为本轮清理范围。

离屏证据保留在 `final-check/`、`naming-check/`、`viewport-check/` 和 `mount-check/`。最终输入框检查覆盖四种主题及 96、120、144 DPI，骑乘视图亦保留四主题图像。它们是实际 WPF 控件的离屏布局渲染，3D 区域使用占位场景，不证明 GPU 场景外观或所有真实鼠标操作；未将其描述为桌面自动验收。用户人工确认单独见上文。

本次没有运行完整解决方案测试、前台输入测试或发布验证，也没有产生新的线上 CI 结果。现有 `Run-Unit-Test` 工作流在 PR 和 `master` 推送时执行完整 Release 还原、构建和测试；仅推送功能分支不会触发此工作流。后续授权交付时仍须等待实际检查，不能用本地 Debug 结果代替。

## 收尾清理与保护结果

遵循用户本次明确要求，只将下列过期目录送入回收站；不永久删除、不清空回收站：

| 原位置（相对仓库） | 已核验内容 |
| --- | --- |
| `artifacts/animation-retarget-layout/library-upload-91bdab4acb6045af9c2588291ce6af3b` | 3 个已使用完毕的上传辅助脚本，共 68,536 字节；图片及 Library 交付物保留 |
| `artifacts/animation-retarget-layout/Deploy_热心市民石先生 2026-10-15 17_46_05` | 0 个文件的空测试部署目录 |

清理前检查绝对路径、仓库包含关系、Git 跟踪状态、重解析点和相关进程；未停止任何程序。清理后，两处原路径均不存在，回收站项目的原目录、原名称、目录结构和文件 SHA-256 均核验一致，确认可恢复。`artifacts/animation-retarget-closeout-20261015/` 中保留 `recycle-before.json`、`process-check.json`、`recycle-receipts.json`、`protected-before.json` 与 `cleanup-result.json`，记录具体回收位置及验证结果。

回收前后校验了 452 个保护文件，其中包括主程序 Debug 目录全部 275 个文件、清理前的本轮修改和其余任务证据。随后规则和归档修改属于本次收尾的预期变化。保留源码、`.git`、`.vs`、所有构建输出、备份、交付图片、测试和构建日志，以及其他任务或归属不明的内容；保留可运行的 `AssetEditor/bin/Debug/net10.0-windows/AssetEditor.CN.exe`。未执行整类 `bin`、`obj` 或 `artifacts` 清理。

## 独立 VFX 工作与后续 Git 边界

独立工作区 `D:\Project\Szy-Cathay\TheAssetEditor-CN-vfx-native`、分支 `codex/vfx-native-investigation` 保持原状。收尾只读核查时，其未提交修改为 `Testing/AssetEditorTests/VfxEditorTests.cs`，与本轮文件没有重叠；这只是当时的快照，不替代未来合并时的检查。

本轮共享接口变化是按编辑器作用域注入 `ViewportInteractionCapabilities`，模型编辑默认关闭，Kitbash 单独启用。后续 VFX 如接入共享 3D 预览，应沿用此默认值并保留其特效专用交互；不能通过开启整模型编辑来解决预览交互需求。未在 VFX 工作区修改、构建或清理文件。

收尾后的完整文件清单、状态和哈希保存在 `artifacts/animation-retarget-closeout-20261015/delivery-scope.json`。提交、推送、创建 PR、合并及发布均尚未执行，也未由本次收尾授权；后续由用户集中确认所需阶段，不自动归档任务。
