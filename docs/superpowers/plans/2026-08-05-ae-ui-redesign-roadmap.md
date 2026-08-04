# AE UI Redesign Roadmap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 AE 国区版全部用户界面渐进迁移到已确认的石墨蓝设计系统，并在最终用户验收后收口为唯一长期 UI 规范。

**Architecture:** 整体工作拆成可独立验收的批次，前一批的代码、测试和视觉验收结果是后一批详细计划的事实来源。设计变量和公共控件先建立稳定契约，随后迁移主窗口、公共流程、Pack/Git 与编辑器模块；一次性设计和计划文档只保留到最终规范完成。

**Tech Stack:** .NET 10、WPF、XAML、C#、CommunityToolkit.Mvvm、NUnit、现有 `Shared.Ui` 与主题资源系统。

## Global Constraints

- 产品和程序集身份保持 `AssetEditor.CN`，主程序保持 `AssetEditor.CN.exe`。
- 保留 WPF、XAML、MVVM、Binding、Command、DI、快捷键和自动化标识。
- 不全局启用 Panuon 或引入新的 UI 框架。
- 用户可见文本只使用中文并接入 `AssetEditor/Language_Cn.json`。
- `Shared.Ui` 不得反向依赖 `AssetEditor`、`Editors` 或具体业务编辑器。
- 深色石墨基线、Windows UI 字体、中等偏紧凑密度和 70–160 ms 弱动效以已批准设计规格为准。
- 不动画布局属性，不伪造进度、成功、错误或搜索功能。
- 内存未保存编辑、磁盘工作树、Git 暂存区和提交历史保持严格分离。
- 每个实施批次使用独立 `codex/` 分支；本地提交、推送、PR 和合并分别取得授权。
- 自动化测试不能替代实际窗口视觉验收；未经明确授权不控制用户桌面。

---

## Source of Truth

- Approved design: `docs/superpowers/specs/2026-08-05-ae-ui-design-system-design.md`
- Current detailed plan: `docs/superpowers/plans/2026-08-05-ae-ui-foundation.md`
- Final canonical document after all implementation and user acceptance: `docs/ui-design-system.md`

## Plan Generation Rule

后续详细计划不提前根据当前代码臆测。每一批完成自动验证并由用户通过视觉验收后，重新读取当时 HEAD、分支、工作区、XAML 覆盖清单和剩余旧样式消费者，再生成下一份详细计划。

每份详细计划必须：

- 列出精确文件、接口、测试和命令；
- 包含失败测试、最小实现、通过测试和差异检查；
- 说明该批不覆盖的界面和剩余风险；
- 在进入下一批前由用户复核；
- 不把历史测试数量、旧提交或旧截图当作当前成功证据。

## Phase Sequence

### Phase 1: Design-system foundation

**Detailed plan:** `docs/superpowers/plans/2026-08-05-ae-ui-foundation.md`

**Deliverable:** 所有主题可解析完整语义 Brush；字体、间距、几何、尺寸和动效变量进入应用与 WPF 测试宿主；建立文字和基础表面样式，不迁移业务页面。

**Exit gate:** 资源契约测试、字体设置测试、主题切换测试、Release 构建和完整测试通过；用户确认默认字体及基础表面没有不可接受的全局回归。

### Phase 2: Core common controls

**Detailed plan path:** `docs/superpowers/plans/YYYY-MM-DD-ae-ui-common-controls.md`

**Generation trigger:** Phase 1 通过用户验收后，以实际 `AeBrush.*`、`AeSize.*`、`AeRadius.*`、`AeMotion.*` 和 `AeText.*` 资源签名生成。

**Deliverable:** 按钮、输入、选择、复选、开关、标签、树、列表、表格、菜单、滚动条和焦点状态形成可选择使用的键控样式；旧隐式样式继续存在。

**Exit gate:** 公共控件完整状态测试和深浅主题实例化通过；展开箭头和通知图标按批准样板垂直居中；用户批准实际 WPF 控件画面。

### Phase 3: Main shell pilot

**Detailed plan path:** `docs/superpowers/plans/YYYY-MM-DD-ae-ui-main-shell.md`

**Generation trigger:** Phase 2 通过用户验收后，重新核对 `MainWindow.xaml`、`MenuBarView.xaml`、`FileTreeView.xaml`、CachedTabControl 和相关测试。

**Deliverable:** 34 DIP 活动栏、可调内容侧栏、编辑器标签和状态栏落地；只连接现有入口，不添加假搜索框。

**Exit gate:** 编辑器创建、切换、拖动、关闭和未保存状态测试通过；正常与窄窗口、高 DPI、键盘焦点和用户实际窗口验收通过。

### Phase 4: Common workflows

**Detailed plan path:** `docs/superpowers/plans/YYYY-MM-DD-ae-ui-common-workflows.md`

**Generation trigger:** Phase 3 通过用户验收后，重新核对设置、标准对话框、错误提示和 `OperationProgressView` 的实际入口。

**Deliverable:** 设置、确认、错误、加载和真实进度统一到公共控件系统；设置预览取消恢复和进度真实性保持不变。

**Exit gate:** 设置主题与字体事务、Owner/模态性、加载/失败/重试及真实进度测试通过；用户视觉验收通过。

### Phase 5: Pack and Git workspace

**Detailed plan path:** `docs/superpowers/plans/YYYY-MM-DD-ae-ui-pack-git-workspace.md`

**Generation trigger:** Phase 4 通过用户验收后，读取 `docs/folder-project-version-control.md` 和当时 Git 工作区实现。

**Deliverable:** 文件树、工作树、Git 管理、提交历史、分支、上下文菜单和长操作反馈迁移；四层状态语义不变。

**Exit gate:** Folder Project/Git 门禁测试、真实大工程阶段计时和用户实际 Git 工作流验收通过。

### Phase 6: Editor families

每个编辑器家族拥有独立计划和用户验收，不共享一次笼统的“编辑器已迁移”结论。

| Order | Detailed plan path | Coverage |
|---:|---|---|
| 6.1 | `docs/superpowers/plans/YYYY-MM-DD-ae-ui-editor-audio.md` | Audio Explorer、Audio Editor、转换、合并及音频设置 |
| 6.2 | `docs/superpowers/plans/YYYY-MM-DD-ae-ui-editor-model-kitbash.md` | Kitbash、模型、材质、3D 工具与子窗口 |
| 6.3 | `docs/superpowers/plans/YYYY-MM-DD-ae-ui-editor-animation-metadata.md` | 动画、重定向、Animation Meta 与属性编辑 |
| 6.4 | `docs/superpowers/plans/YYYY-MM-DD-ae-ui-editor-remaining.md` | Texture、TWUI、CSC、导入导出、Reports 及剩余窗口 |

**Generation trigger:** 前一个家族通过用户验收后重新生成下一个家族计划，并更新 XAML 全覆盖清单。

**Exit gate:** 每个家族的行为测试、Release 构建、相关真实文件操作和用户视觉验收分别通过。

### Phase 7: Theme completion and legacy cleanup

**Detailed plan path:** `docs/superpowers/plans/YYYY-MM-DD-ae-ui-theme-legacy-cleanup.md`

**Generation trigger:** XAML 全覆盖清单显示所有用户可见界面已迁移后，重新扫描隐式样式、资源键、XAML、代码创建、反射和测试消费者。

**Deliverable:** 浅色、高对比度及其他色板完成精调；只删除有完整无消费者证据的旧样式和资源。

**Exit gate:** 所有主题资源、完整 Release 构建和完整测试通过；用户对深色、浅色和高对比度最终画面确认没有问题。

### Phase 8: Canonical UI standard

**Detailed plan path:** `docs/superpowers/plans/YYYY-MM-DD-ae-ui-final-standard.md`

**Generation trigger:** Phase 1–7 全部完成，XAML 覆盖清单无遗漏，用户明确确认实际应用没有问题。

**Deliverable:**

- 依据最终代码和验收结果创建 `docs/ui-design-system.md`；
- 更新 `docs/README.md`，把 UI 设计、实现和审查路由到该规范；
- 更新根 `AGENTS.md`，要求未来 Codex 在新增或修改 UI 前读取规范；
- 建立本次 UI 重构生成文档清单，确认稳定内容已进入长期规范；
- 删除本次一次性 UI 设计规格、实施计划、迁移清单及其索引；
- 保留架构、Git 语义、IPC、领域和来源文档。

**Exit gate:** 用户审核长期规范及删除清单，内容检查和 `git diff --check` 通过，仓库只保留一个长期 UI 规范入口。

## Initiative-wide Coverage Ledger

Phase 1 执行时建立临时迁移清单 `docs/superpowers/plans/ae-ui-migration-ledger.md`。清单逐项列出四个产品源码根目录中的每个 `Window`、`UserControl`、资源字典和纯 C# WPF 控件，并使用以下唯一状态：

- `Unreviewed`
- `Legacy-compatible`
- `Migrated`
- `Not-user-visible`
- `Deferred-with-user-approval`

每项记录所属家族、实际运行入口、使用的公共控件、自动验证、用户视觉验收和剩余风险。任何阶段不得用抽样代替该清单；最终长期规范完成后，该临时清单与一次性计划一起删除。

## Git and Authorization Gates

- 写计划不授权实现。
- 用户选择执行方式并明确授权实施后，才创建对应批次分支并修改代码。
- 计划中的本地提交步骤只有在执行授权包含本地提交时才执行。
- 推送、PR 和合并不由本路线图自动授权。
