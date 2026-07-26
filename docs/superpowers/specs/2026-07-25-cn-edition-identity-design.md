# Asset Editor 国区版身份隔离设计

## 目标

将当前下游仓库从仅在目录名后添加 `-Szy` 的状态，调整为可与原版 Asset Editor 并存的中文专用版本。完成后，开发者、Windows、用户数据、IPC 和更新系统都能明确区分两个版本，同时避免对现有 C# 命名空间进行高风险的全量改名。

## 已确认的命名

| 层级 | 国区版名称 |
| --- | --- |
| 用户可见名称 | `Asset Editor 国区版` |
| 内部产品标识 | `AssetEditor.CN` |
| 首个版本 | `1.1.0` |
| 本地仓库目录 | `D:\TheAssetEditor-CN` |
| GitHub 仓库 | `Szy-Cathay/TheAssetEditor-CN` |
| 解决方案 | `AssetEditor.CN.sln` |
| 主项目显示名 | `AssetEditor.CN` |
| 主程序 | `AssetEditor.CN.exe` |
| 更新器 | `AssetEditor.CN.Updater.exe` |
| 用户数据目录 | `%USERPROFILE%\AssetEditor.CN` |
| IPC 命名管道 | `AssetEditor.CN.Ipc` |

## 仓库和源码边界

- GitHub 仓库从 `Szy-Cathay/TheAssetEditor-Szy` 改名为 `Szy-Cathay/TheAssetEditor-CN`。
- 本地 `origin` 只指向国区版仓库。
- 删除名为 `upstream` 的远程地址及其远程跟踪引用。
- 最后将本地工作目录从 `D:\TheAssetEditor-Szy` 改为 `D:\TheAssetEditor-CN`。
- 保留现有 `AssetEditor.*` C# 命名空间、大部分源码目录和功能子项目名。
- 只更新因程序集改名而必须变化的项目引用、XAML 程序集资源路径和测试。
- 不修改、移动或删除 `D:\TheAssetEditor`。
- README 保留原项目地址和原作者来源说明，不抹除上游贡献历史。

这样可以让用户和操作系统看到的产品身份完全独立，同时避免全量命名空间替换引发 XAML、程序集引用、序列化或外部工具兼容问题。

## 运行时隔离

国区版固定使用 `%USERPROFILE%\AssetEditor.CN` 作为应用数据根目录。其下的配置、日志、报告、临时文件、Schema、应用组件、动画映射和更新下载目录都不再与原版共享。

首次启动时：

- 创建全新的 `ApplicationSettings.json`。
- 不读取、不复制也不迁移 `%USERPROFILE%\AssetEditor` 中的原版设置。
- 不修改原版的日志、缓存或更新文件。

IPC 命名管道从 `TheAssetEditor.Ipc` 改为 `AssetEditor.CN.Ipc`。国区版不监听旧管道，也不提供旧管道兼容层，因此原版和国区版可以同时运行。IPC 文档和相关测试同步更新。

## 中文专用发行

- 发布产物只包含 `Language_Cn.json`。
- 删除英文和法文本地化文件。
- 启动流程固定加载中文。
- 设置页面移除语言选择控件。
- 设置模型和设置视图模型移除语言偏好、语言枚举及切换逻辑。
- 删除不再使用的语言选择转换代码。
- 保留代码标识符和命名空间的现有英文命名，因为它们不属于用户可见语言。
- 验证所有实际使用的本地化键都存在于 `Language_Cn.json`，避免界面显示原始键名。

窗口标题固定为 `Asset Editor 国区版 v1.1.0`。

## 更新和支持链接

- 主程序的版本检查改为读取 `Szy-Cathay/TheAssetEditor-CN` 的 Releases。
- 更新器从同一仓库下载国区版发布包。
- 更新器查找并重启 `AssetEditor.CN.exe`，其自身文件名为 `AssetEditor.CN.Updater.exe`。
- 更新器只操作自身安装目录和国区版更新临时目录。
- 项目的 `Product`、`PackageId`、仓库 URL 和项目 URL 使用国区版身份。
- 异常窗口中的问题反馈链接指向国区版仓库 Issues。
- 与原作者贡献或赞助有关的既有链接不冒充国区版支持渠道；上游来源在 README 中明确标注。

## 图标

国区版图标保留原 AE 图标主体，在角落加入清晰的红色 `CN` 角标。生成独立的 `AssetEditor.CN.png` 和 `AssetEditor.CN.ico`，并更新 WPF 资源和程序图标引用。

图标需要在窗口、任务栏和快捷方式常见尺寸下保持可辨认。图标改动不改变应用布局或主题。

## 实施顺序

1. 在独立分支 `codex/cn-edition-identity` 上完成所有代码和资源修改。
2. 更新解决方案、程序集、程序、更新器和产品元数据。
3. 隔离用户数据目录和 IPC，并更新对应测试与文档。
4. 移除多语言切换，只保留中文资源和中文启动路径。
5. 更新国区版图标、README、更新源和支持链接。
6. 完成构建、测试、发布和运行时检查。
7. 验证通过后，将 GitHub 仓库改名为 `TheAssetEditor-CN`。
8. 更新本地 `origin`，删除 `upstream`。
9. 确认目标路径不存在后，将本地目录改为 `D:\TheAssetEditor-CN`。

## 验证和成功标准

- `AssetEditor.CN.sln` 可以完整还原和构建。
- 现有测试及本次新增或调整的身份隔离测试全部通过。
- `win-x64` 发布成功。
- 发布目录包含 `AssetEditor.CN.exe` 和 `AssetEditor.CN.Updater.exe`，不包含旧名称的两个可执行文件。
- 发布目录只包含 `Language_Cn.json`，不包含英文或法文本地化文件。
- 中文 JSON 可以解析，程序实际使用的本地化键没有缺失。
- 国区版仅在 `%USERPROFILE%\AssetEditor.CN` 下创建应用数据。
- 国区版使用 `AssetEditor.CN.Ipc`，代码和 IPC 文档中不再使用旧管道名。
- 版本检查、更新器、项目元数据和问题反馈不再把原仓库作为国区版服务地址。
- 新图标的 PNG 和 ICO 可以正常加载，`CN` 角标在小尺寸下可辨认。
- `git remote -v` 最终只显示指向 `Szy-Cathay/TheAssetEditor-CN` 的 `origin`。
- `D:\TheAssetEditor` 和 `%USERPROFILE%\AssetEditor` 保持不变。

## 失败保护

- 如果 `D:\TheAssetEditor-CN` 已存在，不覆盖也不合并该目录，停止本地目录改名。
- 如果 GitHub 仓库改名失败，不把本地 `origin` 改到一个尚不存在的地址。
- 如果构建、测试、发布或运行时检查失败，不执行仓库和本地目录的最终改名。
- 不通过全局字符串替换改写命名空间；所有修改都限制在已确认的产品身份和隔离边界内。
