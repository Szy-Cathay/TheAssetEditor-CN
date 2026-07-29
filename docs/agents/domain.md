# Domain Docs

本文件说明工程 Skills 在探索代码库时应如何使用本仓库的领域文档。

## 探索代码前

- 读取仓库根目录的 **`CONTEXT.md`**。
- 如果根目录存在 **`CONTEXT-MAP.md`**，它会指向每个上下文的 `CONTEXT.md`；只读取与当前任务相关的上下文。
- 检查 **`docs/adr/`**，读取涉及当前工作区域的 ADR。多上下文仓库还应检查 `src/<context>/docs/adr/`。

如果这些文件不存在，**直接继续，不要提示缺失，也不要预先建议创建**。`/domain-modeling` Skill（可由 `/grill-with-docs` 和 `/improve-codebase-architecture` 间接调用）会在术语或决策真正明确后按需创建这些文件。

## 文件结构

本仓库采用 single-context（单上下文）结构：

```text
/
|-- CONTEXT.md
|-- docs/adr/
|-- AssetEditor/
|-- Editors/
|-- GameWorld/
|-- Shared/
`-- Testing/
```

## 使用词汇表中的术语

当输出内容需要命名领域概念时，例如 Issue 标题、重构建议、假设或测试名称，应使用 `CONTEXT.md` 中定义的术语，不要改用词汇表明确排除的同义词。

如果词汇表中没有所需概念，应先判断是否正在发明项目并未使用的语言；如果确实存在领域缺口，则记录下来交给 `/domain-modeling`。

## 标明 ADR 冲突

如果输出内容与现有 ADR 冲突，应明确指出冲突，而不是静默覆盖原有决策。例如：

> 与 ADR-0007（事件溯源订单）冲突，但值得重新讨论，因为……
