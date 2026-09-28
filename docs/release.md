# 国区版版本发布

本文件用于指定版本的发布与流程恢复。发布授权范围、Git 操作边界和完成标准以根 [`AGENTS.md`](../AGENTS.md) 为准；查阅或准备发布流程本身不触发发布。

## 事实来源与流程恢复

- 当前源码和配置决定版本、发布布局与更新协议：`AssetEditor/AssetEditor.csproj`、`AssetEditorUpdater/AssetEditorUpdater.csproj`、`AssetEditor/Properties/PublishProfiles/FolderProfile.pubxml`、`AssetEditorUpdater/GiteeUpdateSource.cs` 和 `.github/workflows/pr-test.yml`。
- 已有 Release 和远端资产只用于核对命名、结构及发布操作，不能替代当前源码和本次产物的验证。不要照抄旧版的文件大小、分片数或哈希值。
- `publish-asseteditor-cn` Skill 存在时可作为操作指引。若 Skill 不可用，依据本文件与当前配置恢复具体步骤；遇到无法核实的工具、权限、产物契约或远端状态，完成不依赖该信息的准备并在对应写入前停下说明。

## 发布顺序与门禁

1. 核对仓库、`master`、完整 Git 状态、远端最新提交、最近正式版本和目标标签。只使用用户指定的版本号；已有同名标签或不明工作区改动先处理清楚。
2. 同步检查主程序与更新器项目中的 `<Version>`，以及根 `README.md` 的“当前版本”；核对产品名、可执行文件名、用户目录和 Gitee 更新源未偏离国区版身份。根据上一版本到目标提交的实际差异编写发布说明。
3. 执行根 `AGENTS.md` 中的完整 Release 还原、构建和测试。随后在全新暂存目录按 `FolderProfile.pubxml` 发布；`AssetEditor.csproj` 的 `PublishUpdater` 目标应把独立更新器放入发布目录的 `Updater/`。核验主程序、更新器、运行时文件及目录结构，再打包完整安装 ZIP。
4. 核对更新协议后准备 Gitee 资产。更新器要求同一发行版恰好有一个 `.manifest.json` 清单，以及清单中每个分片各一个 ZIP 资产；清单包含版本、完整 ZIP 的名称/大小/SHA-256，和按顺序列出的分片名称/大小/SHA-256。分片大小与哈希、合并后的完整 ZIP 大小与哈希都必须由本次实际产物计算并验证；分片应符合 Gitee 当时的附件限制。不要固定分片数量。
5. 在根 `AGENTS.md` 的授权范围内执行必要提交、推送、标签、GitHub Release 和国区更新源发布。GitHub Release 提供完整安装 ZIP；Gitee 发行版提供清单与分片。任何阶段失败即停止，不跳过或声称已完成后续阶段。
6. 回读远端提交与标签、GitHub Actions 最终状态、GitHub Release 的版本/说明/资产，以及 Gitee 发行版的版本/清单/分片。验证远端资产与本次产物一致，且更新器能按清单解析并校验；全部通过后才报告发布完成。

本文件记录稳定门禁，不保存临时凭据、某次发布的提交号、文件大小、分片数量或哈希值。发布的实际命令、输出和未完成环节在当次对话中报告。
