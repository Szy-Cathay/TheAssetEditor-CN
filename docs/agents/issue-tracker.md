# Issue tracker: GitHub

本仓库的 Issue 和 PRD 使用 GitHub Issues 管理。所有操作均使用 `gh` CLI。

## 操作约定

- **创建 Issue**：`gh issue create --title "..." --body "..."`。多行正文使用 PowerShell here-string，或通过 `--body-file` 传入。
- **读取 Issue**：`gh issue view <number> --comments`，同时获取标签；需要筛选时使用 `--json` 和 `--jq`。
- **列出 Issue**：`gh issue list --state open --json number,title,body,labels,comments --jq '[.[] | {number, title, body, labels: [.labels[].name], comments: [.comments[].body]}]'`，并按需添加 `--label` 和 `--state` 过滤条件。
- **评论 Issue**：`gh issue comment <number> --body "..."`
- **添加或移除标签**：`gh issue edit <number> --add-label "..."` / `--remove-label "..."`
- **关闭 Issue**：`gh issue close <number> --comment "..."`

通过 `git remote -v` 判断仓库；在克隆仓库内运行时，`gh` 会自动识别对应仓库。

## 将 Pull Request 作为分流入口

**PRs as a request surface: no.**（如果本仓库以后将外部 Pull Request 视为功能请求，可将 `no` 改为 `yes`；`/triage` 会读取此开关。）

设置为 `yes` 后，Pull Request 使用与 Issue 相同的标签和状态，并改用对应的 `gh pr` 命令：

- **读取 Pull Request**：使用 `gh pr view <number> --comments`；查看差异时使用 `gh pr diff <number>`。
- **列出待分流的外部 Pull Request**：使用 `gh pr list --state open --json number,title,body,labels,author,authorAssociation,comments`，只保留 `authorAssociation` 为 `CONTRIBUTOR`、`FIRST_TIME_CONTRIBUTOR` 或 `NONE` 的项目，排除 `OWNER`、`MEMBER` 和 `COLLABORATOR`。
- **评论、标记或关闭**：使用 `gh pr comment`、`gh pr edit --add-label` / `--remove-label` 和 `gh pr close`。

GitHub 的 Issue 和 Pull Request 共用编号空间，因此 `#42` 可能是其中任意一种。先运行 `gh pr view 42`，失败后再运行 `gh issue view 42`。

## 当 Skill 要求“发布到 Issue tracker”

创建一个 GitHub Issue。

## 当 Skill 要求“获取相关 ticket”

运行 `gh issue view <number> --comments`。

## Wayfinding 操作

供 `/wayfinder` 使用。**Map** 是一个总 Issue，**child** 是作为 ticket 的子 Issue。

- **Map**：使用一个带有 `wayfinder:map` 标签的 Issue，正文保存 Notes、Decisions-so-far 和 Fog。创建命令为 `gh issue create --label wayfinder:map`。
- **Child ticket**：通过 GitHub sub-issue 将子 Issue 关联到 Map。若仓库未启用 sub-issue，则在 Map 正文中添加任务列表，并在 child 正文顶部写入 `Part of #<map>`。标签使用 `wayfinder:<type>`，其中类型为 `research`、`prototype`、`grilling` 或 `task`。认领后，将 ticket 分配给当前开发者。
- **Blocking**：优先使用 GitHub 原生 Issue dependency。添加依赖边的命令为 `gh api --method POST repos/<owner>/<repo>/issues/<child>/dependencies/blocked_by -F issue_id=<blocker-db-id>`。`<blocker-db-id>` 必须是阻塞 Issue 的数字数据库 ID，可通过 `gh api repos/<owner>/<repo>/issues/<n> --jq .id` 获取，不能使用 `#number` 或 `node_id`。GitHub 的 `issue_dependencies_summary.blocked_by` 表示仍未关闭的阻塞项。若原生依赖不可用，则在 child 正文顶部添加 `Blocked by: #<n>, #<n>`。所有阻塞 Issue 关闭后，ticket 才算解除阻塞。
- **Frontier query**：列出 Map 下仍处于打开状态的 child，排除存在未关闭阻塞项或已有 assignee 的 ticket，然后选择 Map 顺序中的第一项。
- **Claim**：运行 `gh issue edit <n> --add-assignee @me`。这是会话中的第一次写操作。
- **Resolve**：运行 `gh issue comment <n> --body "<answer>"`，然后运行 `gh issue close <n>`，最后把上下文指针和链接追加到 Map 的 Decisions-so-far。
