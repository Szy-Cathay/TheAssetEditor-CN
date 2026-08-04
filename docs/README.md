# AI 上下文路由

不要默认通读 `docs/`。先读根 `AGENTS.md`，再按当前任务只加载下表所需文件；普通实现细节直接查代码。

| 任务触发条件 | 读取 | 只用于 |
| --- | --- | --- |
| 产品身份、Pack、文件夹工程术语可能混淆 | [`../CONTEXT.md`](../CONTEXT.md) | 统一词义 |
| 跨模块定位、依赖注入、Pack 生命周期、共享 UI、更新器或测试路由 | [`architecture.md`](architecture.md) | 入口、边界、验证定位 |
| 文件夹工程、本地 Git、状态语义、路径安全或大型工程性能 | [`folder-project-version-control.md`](folder-project-version-control.md) | 非显然状态模型和高风险契约 |
| 外部进程通过命名管道打开资源 | [`asseteditor-ipc.md`](asseteditor-ipc.md) | IPC 协议和限制 |
| Issue、标签或领域文档维护流程 | [`agents/`](agents/) | Agent 工作流，不是运行时架构 |

## 事实优先级

1. 当前代码、`.csproj`、配置和测试；
2. 本目录中的稳定契约；
3. Git 历史、PR、旧对话和记忆，仅用于定位待核验内容。

仅当稳定契约、术语、关键入口或已知限制改变时更新文档。不要记录测试数量、性能快照、临时分支、提交 SHA 或可由代码搜索直接重建的类/方法清单。

实施计划和一次性手工测试清单不作为长期 AI 上下文；只提炼其中仍有效、代码无法表达的设计原因、故障分层或人工验收门禁。
