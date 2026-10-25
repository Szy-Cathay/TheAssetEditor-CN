# AI 上下文路由

不要默认通读 `docs/`。先遵循根 `AGENTS.md`，再按当前任务加载下表所需文件的边界与相关章节；跨模块或整体验收任务按各文件的阅读要求扩展。普通实现细节直接查代码。

| 任务触发条件 | 读取 | 只用于 |
| --- | --- | --- |
| 产品身份、Pack、文件夹工程术语可能混淆 | [`../CONTEXT.md`](../CONTEXT.md) | 统一词义 |
| 跨模块定位、依赖注入、Pack 生命周期、共享 UI、更新器或测试路由 | [`architecture.md`](architecture.md) | 入口、边界、验证定位 |
| KitbashEditor、模型选择/变换/覆盖层、场景树、保存或 Kitbash 子工具 | [`kitbash-editor.md`](kitbash-editor.md) | 模型编辑架构、状态所有权、实现边界和验证门禁 |
| 超级视图、SuperView、Animation META 预览、元数据时间/空间编辑或双文档保存 | [`superview.md`](superview.md) | 元数据预览/编辑数据流、双文档所有权和模型编辑隔离 |
| VFX 编辑器、Effekseer 场内预览、游戏特效语义转换或共享特效节点 | [`architecture.md`](architecture.md) 的“VFX 可视化编辑”；涉及 Effect 时加读 [`superview.md`](superview.md) | 原始游戏数据权威、适配范围、未支持诊断和静态/离屏/原生验证边界 |
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

本路由参考 OpenAI 的 [GPT-6 Astra 指令与 Skill 建议](https://developers.openai.com/blog/rethinking-skills-and-prompts-for-gpt-6-astra)：仓库规则保留稳定边界，具体上下文按任务加载。为便于跨设备恢复，本项目把发布关键步骤保存在 `release.md`；授权和验收边界仍以根 `AGENTS.md` 为准。

维护规则时同时遵循 OpenAI 的 [AGENTS.md 指南](https://learn.chatgpt.com/docs/agent-configuration/agents-md)和 [Codex 工作建议](https://learn.chatgpt.com/guides/best-practices)：目录越深的指令越具体，根文件保持简洁、可执行，将详细上下文链接到相关文档。规则来自已确认的项目约束，不把一次性操作记录扩充成通用要求。

## 验收归档

以下文件仅用于追溯用户要求保留的阶段验收，不作为默认加载的 AI 上下文，也不能代替当前代码和新改动的验证：

- [KitbashEditor 界面与工具改进验收记录](maintenance/kitbash-editor-acceptance-20261015.md)
- [动画重定向布局与播放改进验收记录](maintenance/animation-retarget-acceptance-20261015.md)
