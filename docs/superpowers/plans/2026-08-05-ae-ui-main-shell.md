# AE UI Main Shell Implementation Plan

**Goal:** 将主窗口迁移为已批准的石墨蓝工作台骨架，同时保留现有资源、Git、编辑器与状态行为。

**Authorization:** 用户已授权 Phase 1–7 连续本地实施、自动化视觉验证和本地提交；本批不推送、不创建 PR、不发布。

## Scope

### Create

- `AssetEditor/Themes/DesignSystem/Shell.xaml`
- `Testing/AssetEditorTests/UiMainShellTests.cs`
- `Testing/AssetEditorTests/UiMainShellGallery.cs`

### Modify

- `AssetEditor/App.xaml`
- `AssetEditor/Views/MainWindow.xaml`
- `AssetEditor/Views/MenuBarView.xaml`
- `AssetEditor/Language_Cn.json`
- `Testing/AssetEditorTests/WpfTestApplicationHost.cs`
- `Testing/AssetEditorTests/UiDesignSystemResourceTests.cs`
- `docs/superpowers/plans/ae-ui-migration-ledger.md`

## Behavior Contract

- 34 DIP 活动栏只映射现有“资源”和“本地 Git”工作域，共享 `GitWorkspace.SelectedSidebarTabIndex`。
- 内容侧栏继续由 `FileTreeColumnWidth` 调整，并继续由现有显示命令折叠为 0。
- 编辑器标签继续支持创建后的显示、选择、拖动排序、中键关闭、关闭按钮和上下文菜单。
- `CachedTabControl` 必须继续提供 `PART_ItemsHolder`，不破坏编辑器实例缓存。
- 未保存状态使用主题化危险色圆点，同时保留 `HasUnsavedChanges` 绑定，不再硬编码红色文字。
- 顶部只保留现有菜单；不显示没有真实命令实现的搜索框。
- 状态栏只显示现有应用、游戏、Pack 和真实加载进度。

## Execution

1. 添加失败的壳层资源、结构与绑定契约测试。
2. 添加键控壳层样式并接入应用和 WPF 测试宿主。
3. 重排主窗口为活动栏、可调内容侧栏、编辑器标签区和紧凑状态栏。
4. 迁移菜单、标签关闭按钮、空编辑器状态和未保存标记；保留原命令与事件。
5. 实例化真实 `MainWindow`，验证工作域切换、Git 禁用态、空状态和关键模板部件。
6. 在 Dark、Light、HighContrastDark、HighContrastLight 下渲染正常与窄窗口；分别以 100%、125%、150% 捕获并逐张检查。
7. 运行编辑器管理、主窗口、资源契约、完整 Release 构建、完整测试和 `git diff --check`，更新迁移清单并本地提交。

## Exit Gate

- 活动栏宽度、标签高度和状态栏高度符合已批准变量。
- 正常与窄窗口、三档缩放和四主题无裁切、重叠、低对比或意外滚动条。
- 键盘焦点、禁用 Git、空编辑器、未保存编辑器和加载状态均有自动或视觉证据。
- 编辑器缓存、切换、拖动和关闭绑定保持不变。
- 推送、PR、发布和 Phase 8 长期规范均未执行。
