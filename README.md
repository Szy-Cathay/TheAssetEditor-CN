# Asset Editor 国区版

[下载最新版本](https://github.com/Szy-Cathay/TheAssetEditor-CN/releases/latest) · [查看上游项目](https://github.com/donkeyProgramming/TheAssetEditor) · [问题反馈](https://github.com/Szy-Cathay/TheAssetEditor-CN/issues)

> **仓库来源**
>
> 本仓库源自 [donkeyProgramming/TheAssetEditor](https://github.com/donkeyProgramming/TheAssetEditor)，是面向中国大陆用户独立维护的下游版本，并非上游官方中文版。
>
> 仓库保留上游项目的提交历史、版权信息以及原作者和社区贡献者的署名。

> **AI Agent 开发声明**
>
> 本仓库的下游开发全程由 AI Agent 完成。除直接继承或同步自上游的代码外，本仓库全部新增代码、修改、适配、自动测试和技术文档均由 AI Agent 编写。
>
> 人类维护者负责提出需求、确定方向、验收功能和批准发布，不直接编写代码。

## 1. 干什么用的

Asset Editor 国区版是一款运行在 Windows 上的《全面战争》系列游戏资产编辑工具，当前重点服务于《全面战争：战锤 3》的 Mod 制作流程。

它可以帮助 Mod 作者加载和浏览 Pack，编辑模型、动画、音频、纹理、动画 META 等游戏资源，并将修改保存为 Pack 或文件夹工程。

### 国区版身份

- 当前版本：`2.4.2`
- 主程序：`AssetEditor.CN.exe`
- 更新器：`AssetEditor.CN.Updater.exe`
- 用户数据目录：`%USERPROFILE%\AssetEditor.CN`
- IPC 接口：`AssetEditor.CN.Ipc`
- 更新来源：`Szy-Cathay/TheAssetEditor-CN`
- 用户界面：仅提供中文

国区版可以和原版 Asset Editor 同时安装。两者不共用配置、缓存、日志、更新目录或 IPC 接口。

> 功能说明以当前 `master` 源码为准。正式发布版包含的功能以对应版本的 [Release 说明](https://github.com/Szy-Cathay/TheAssetEditor-CN/releases)为准。

## 2. 有哪些功能

### 继承或后续同步自上游的功能

以下能力建立在原仓库及其社区贡献者的工作之上。

| 功能 | 用途 |
| --- | --- |
| Pack 与资源管理 | 加载游戏 Pack 和 Mod Pack，浏览、搜索、编辑并保存其中的资源 |
| 文件夹工程 | 从原仓库移植，可新建或打开文件夹工程、将 Pack 导入工程、直接编辑磁盘资源，再从工程生成 Pack |
| Kitbash 3D 模型编辑器 | 组合多个模型部件，编辑网格与材质，进行合并、拆分、复制、选择和实时三维预览 |
| 动画 META 编辑器 | 编辑动画标签、属性、触发时间和其他动画元数据 |
| 动画工具 | 编辑 AnimPack、战役动画和关键帧，批量导出动画，并在不同骨架之间重定向动画 |
| 音频编辑器与浏览器 | 浏览 Wwise 音频结构，试听波形，创建和编辑音频工程，转换及导出音频资源 |
| 骨架编辑器 | 查看和编辑骨骼结构、绑定关系及参考模型 |
| 纹理编辑器 | 预览游戏纹理和相关元数据 |
| 导入与导出 | 在 GLTF、FBX、RMV2、DDS、PNG 等外部格式与游戏格式之间进行转换 |
| 报表工具 | 生成材质、模型、动画 META、音频事件和资源搜索等分析报告 |
| 其他编辑工具 | 包括 TWUI、坐骑、战役动画、CSC 复合场景、照片工作室及 CAVP8 视频导出等工具 |

### 国区版新增或显著增强的功能

这里的“增强”表示在上游已有基础能力上，由国区版继续完成或重新设计，并不改变底层功能的上游来源。

| 功能 | 国区版变化 |
| --- | --- |
| 中文专用发行版 | 完成中文界面、国区版品牌、程序名称、用户目录、更新源和 IPC 的完整隔离 |
| 文件夹工程完善 | 在上游功能基础上完善工程配置、路径安全、空目录保存、大型工程性能、真实进度、未保存内容保护和 Pack 生成流程 |
| 本地 Git 管理 | 为文件夹工程补充完整的本地版本管理，包括暂存、提交、储藏、分支、合并、历史、恢复、还原和重置 |
| 本地历史保护 | 在提交、切换分支、关闭工程或退出前检查未保存内容，并明确区分编辑器内存、磁盘文件、Git 暂存区和提交历史 |
| 全新中文界面 | 统一重构主窗口、设置、Pack、Git、模型、动画、音频、骨架和各类对话框的视觉及交互 |
| 设置与真实进度 | 分类整理主题、字体、渲染、音频和保存设置；长时间操作显示真实阶段、当前内容和实际进度 |
| Kitbash 与 3D 视图强化 | 增加 Blender 风格视图操作、导航 Gizmo、选择和变换体验，并强化顶点、边、面、对象模式及选择覆盖层 |
| 三维渲染强化 | 增加材质预览、实体和线框模式，统一灯光、背景、网格、背面显示和阵营着色设置 |
| 超级视图强化 | 同时处理持久 META 和动画 META，显示战斗标记、Prop 和时间范围，并支持部分 META 的三维移动、旋转、撤销与重做 |
| 音频工作流强化 | 改进音频浏览、波形时间线、筛选、试听、编译、转换、合并、导出和错误反馈 |
| 保存与更新安全 | 强化 Pack 保存、自动备份、更新包校验、事务安装、备份与失败回滚，降低文件损坏和更新失败风险 |
| 外部工具联动 | 通过当前 Windows 用户专用的命名管道，让外部工具请求打开指定 Pack 资源或复用已有 Kitbash 页面 |
| 兼容性与稳定性 | 修复 Windows 10 启动、窗口生命周期、资源缺失、连续编辑和大型文件夹工程等场景中的问题 |

> 文件夹工程的 Git 功能只管理本地历史，不提供 `push`、`pull`、`fetch` 或远端分支协作。

## 3. 技术文档

本仓库的技术文档主要面向 AI Agent 和维护者，用于记录不能仅靠代码稳定表达的架构边界、领域术语和验证要求。

| 文档 | 内容 |
| --- | --- |
| [`docs/README.md`](docs/README.md) | 技术文档总入口与任务路由 |
| [`CONTEXT.md`](CONTEXT.md) | 产品身份、Pack、文件夹工程等核心术语 |
| [`docs/architecture.md`](docs/architecture.md) | 总体架构、模块职责、依赖注入和验证入口 |
| [`docs/kitbash-editor.md`](docs/kitbash-editor.md) | Kitbash 模型编辑、选择、变换、渲染和保存架构 |
| [`docs/superview.md`](docs/superview.md) | 超级视图、双 META 文档、预览和三维编辑架构 |
| [`docs/ui-design-system.md`](docs/ui-design-system.md) | 国区版 WPF 界面设计系统与公共组件规则 |
| [`docs/folder-project-version-control.md`](docs/folder-project-version-control.md) | 文件夹工程、本地 Git、路径安全和大型工程约束 |
| [`docs/asseteditor-ipc.md`](docs/asseteditor-ipc.md) | 外部工具打开资源时使用的 IPC 协议 |
| [`docs/agents/`](docs/agents/) | Issue、标签和领域文档维护规则 |

### 项目结构

- `AssetEditor/`：WPF 主程序、主窗口和应用级协调
- `Editors/`：模型、动画、音频、纹理、META、导入导出等编辑器
- `GameWorld/`：三维场景、渲染、选择和变换
- `Shared/`：Pack、游戏格式、公共服务与共享界面
- `AssetEditorUpdater/`：国区版更新器
- `Testing/`：自动测试、架构门禁和端到端验证

### 从源码构建

要求：

- Windows
- .NET 10 SDK

```powershell
git clone https://github.com/Szy-Cathay/TheAssetEditor-CN.git
cd TheAssetEditor-CN

dotnet restore AssetEditor.CN.sln
dotnet build AssetEditor.CN.sln --configuration Release --no-restore
dotnet test AssetEditor.CN.sln --configuration Release --no-build --no-restore --verbosity normal
```

## 4. 致谢

- 感谢 [The Asset Editor](https://github.com/donkeyProgramming/TheAssetEditor) 的原作者和所有社区贡献者。本仓库的编辑器基础、游戏格式支持和大量核心能力均建立在他们的工作之上。
- 感谢《全面战争》Mod 社区长期积累的格式研究、制作经验和问题反馈。
- 感谢所有参与功能测试、视觉检查、真实素材验证和问题反馈的用户与 Mod 作者。
- 感谢参与本仓库开发、测试、审查和文档维护的 AI Agent。

本项目是独立维护的非官方社区项目，与 Creative Assembly 没有隶属关系。
