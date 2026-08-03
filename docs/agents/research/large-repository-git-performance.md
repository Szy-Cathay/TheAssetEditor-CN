# 大型仓库 Git 交互性能：官方机制与 AE 现状对照

## 研究边界

本文只整理 Git 与微软公开的一手资料，并与 AE 当前调用路径作事实对照；不构成实施方案。

## 成熟实现的共同机制

1. **将状态检查缩小到可能变化的范围。** Git 官方文档说明，大型工作树中的未跟踪文件枚举可能让 `git status` 变慢；`core.untrackedCache` 通过目录修改时间跳过未变化目录，`core.fsmonitor` 让 Git 只检查文件系统监视器报告的近期变化。两者都是在避免每次从零遍历全部目录。
2. **按明确路径执行局部操作。** Git 的暂存区是索引。用户执行 `git add <path>` 时，操作目标由 pathspec 限定；超大索引还可使用 split index，使写入集中于变化部分，而非每次重写完整索引。
3. **把仓库维护移出交互热路径。** Scalar 为大型仓库启用 commit-graph、后台维护、增量 repack、预取和索引优化。其目的不是取消正确性检查，而是避免前台交互承担提交图维护、垃圾回收和对象整理。
4. **让常用操作随改动量增长，而不是随仓库总量增长。** 微软公开描述 GVFS 的目标为 `O(modified)`：状态等操作主要随实际修改文件数量增长，而不是随仓库中全部文件数量增长。
5. **极端仓库仍不会所有操作零延迟。** 微软曾以约 350 万文件的 Windows 仓库说明传统 Git 的全局操作成本，并通过虚拟化、限制 Git 需要考虑的文件范围以及 Git 本身的算法优化，将分钟级操作压缩到秒级。冷启动、首次全局核对、影响大量文件的 checkout/reset/merge 仍有真实工作量。

## Visual Studio 可以由官方资料确认的部分

- Visual Studio 提供 commit graph 性能选项。
- Visual Studio 允许关闭自动多仓库激活；微软明确说明只使用一个仓库时，关闭该功能可提升性能。
- Visual Studio 将“Git 仓库管理”与“在解决方案资源管理器中打开仓库目录/自动加载解决方案”分开，避免仅查看 Git 就必然加载整套项目内容。

Visual Studio 没有公开当前版本 Git Changes 界面内部每一步的完整调度实现，因此不能把“它一定使用某个私有缓存或某种 watcher”当作已证实事实。能够确认的是：微软的大型仓库栈采用增量变更发现、提交图、后台维护、范围限制和 `O(modified)` 目标。

## AE 当前可直接确认的差异

- 进入 Git 侧栏会执行 Refresh。
- 暂存指定文件前，`UpdateStagingArea` 先调用 `RetrieveWorkingStatus` 获取整个工作区状态，再从结果中求受影响路径。
- `RetrieveWorkingStatus` 开启工作区和索引重命名检测、未跟踪文件枚举以及未跟踪目录递归。
- `CommitStaged` 在提交前也通过完整工作区状态检查是否存在已暂存内容。
- 默认 Git 操作完成后执行 Full Refresh；该刷新依次读取状态、身份、分支、储藏、最多 100 条历史、合并状态，并按当前页读取提交文件变化。

因此，在当前调用路径中，“只暂存一个已知文件”和“提交一个已知暂存集”仍被绑定到全仓状态读取与随后整套界面数据刷新。这里的问题不是 Git 无法处理大型仓库，而是单文件交互的成本仍随仓库总体规模和完整刷新内容增长。

## 一手资料

- Git `git status`: https://git-scm.com/docs/git-status
- Git `git update-index`: https://git-scm.com/docs/git-update-index
- Git Scalar: https://git-scm.com/docs/scalar
- Git sparse checkout: https://git-scm.com/docs/git-sparse-checkout
- Visual Studio Git 设置: https://learn.microsoft.com/visualstudio/version-control/git-settings?view=vs-2022
- Microsoft, Updates to GVFS: https://devblogs.microsoft.com/devops/updates-to-gvfs/
- Microsoft, Optimizing Git beyond GVFS: https://devblogs.microsoft.com/devops/optimizing-git-beyond-gvfs/
- Microsoft, Announcing GVFS: https://devblogs.microsoft.com/devops/announcing-gvfs-git-virtual-file-system/
