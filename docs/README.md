# AI 上下文路由

不要默认通读 `docs/`。先遵循根 `AGENTS.md`，再按当前任务加载下表所需文件的边界与相关章节；跨模块或整体验收任务按各文件的阅读要求扩展。普通实现细节直接查代码。

| 任务触发条件 | 读取 | 只用于 |
| --- | --- | --- |
| 产品身份、Pack、文件夹工程术语可能混淆 | [`../CONTEXT.md`](../CONTEXT.md) | 统一词义 |
| 跨模块定位、依赖注入、Pack 生命周期、共享 UI、更新器或测试路由 | [`architecture.md`](architecture.md) | 入口、边界、验证定位 |
| KitbashEditor、模型选择/变换/覆盖层、场景树、保存或 Kitbash 子工具 | [`kitbash-editor.md`](kitbash-editor.md) | 模型编辑架构、状态所有权、实现边界和验证门禁 |
| 超级视图、SuperView、Animation META 预览、元数据时间/空间编辑或双文档保存 | [`superview.md`](superview.md) | 元数据预览/编辑数据流、双文档所有权和模型编辑隔离 |
| VFX 编辑器、Effekseer 场内预览、游戏特效语义转换或共享特效节点 | [`vfx-editor.md`](vfx-editor.md) 的硬边界、修改路由、验证门禁及相关语义；跨模块另读 [`architecture.md`](architecture.md)，涉及 Effect 时加读 [`superview.md`](superview.md) | 原始游戏数据权威、编辑/预览状态、连续 trail、材质/模拟适配范围与分层验证 |
| 动画重定向布局、来源/目标切换或共享播放进度 | [`ui-design-system.md`](ui-design-system.md)、[`architecture.md`](architecture.md) | 实际重定向流程、主动画进度与模型交互隔离 |
| 新增、修改、审查或验收任何 WPF 界面、窗口、控件、主题、图标、动画或加载进度 | [`ui-design-system.md`](ui-design-system.md) | 唯一长期 UI 规范与实施门禁 |
| 文件夹工程、本地 Git、状态语义、路径安全或大型工程性能 | [`folder-project-version-control.md`](folder-project-version-control.md) | 非显然状态模型和高风险契约 |
| 外部进程通过命名管道打开资源 | [`asseteditor-ipc.md`](asseteditor-ipc.md) | IPC 协议和限制 |
| 指定版本发布、更新源资产或恢复发布流程 | [`release.md`](release.md) | 发布阶段、产物契约和远端核验 |

## 事实优先级

1. 当前代码、`.csproj`、配置和测试；
2. 本目录中的稳定契约；
3. Git 历史、PR、旧对话和记忆，仅用于定位待核验内容。

仅当稳定契约、术语、关键入口或已知限制改变时更新文档。不要记录测试数量、性能快照、临时分支、提交 SHA 或可由代码搜索直接重建的类/方法清单。

实施计划、一次性研究报告和手工测试清单不作为长期 AI 上下文；只提炼其中仍有效、代码无法表达的设计原因、故障分层或人工验收门禁。

## 指令维护

本路由遵循 OpenAI 的 [AGENTS.md 指南](https://learn.chatgpt.com/docs/agent-configuration/agents-md)与 [Codex 工作建议](https://learn.chatgpt.com/docs/learn/best-practices)：根文件保留简短、准确、可执行的约束，领域细节通过链接按任务读取，较深目录规则按其作用域覆盖。VFX 细节集中在 `vfx-editor.md`，总体架构保留共享边界；一次性证据和待办留在根 `HANDOFF-VFX-LOCAL.md`，不作为默认全量加载内容。官方来源指导文档组织，游戏语义仍由当前代码、测试及原生对照核实。

为便于跨设备恢复，本项目把发布关键步骤保存在 `release.md`；授权和验收边界仍以根 `AGENTS.md` 为准。文档盘点不自动授权清理、提交或发布。

## 验收归档

以下文件仅用于追溯用户要求保留的阶段验收，不作为默认加载的 AI 上下文，也不能代替当前代码和新改动的验证：

- [KitbashEditor 界面与工具改进验收记录](maintenance/kitbash-editor-acceptance-20261015.md)
- [动画重定向布局与播放改进验收记录](maintenance/animation-retarget-acceptance-20261015.md)
