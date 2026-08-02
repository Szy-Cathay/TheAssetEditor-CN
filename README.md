# Asset Editor 国区版

Asset Editor 国区版是面向中国大陆用户维护的 Total War 资产编辑工具下游版本，仅提供中文界面。

## 版本身份

- 程序：`AssetEditor.CN.exe`
- 用户数据：`%USERPROFILE%\AssetEditor.CN`
- 更新仓库：`Szy-Cathay/TheAssetEditor-CN`
- 当前版本：`2.1.0`

国区版可以与原版 Asset Editor 并存，两者不共用配置、日志、缓存、更新目录或 IPC 接口。

## 构建

```powershell
dotnet restore AssetEditor.CN.sln
dotnet build AssetEditor.CN.sln --configuration Release --no-restore
dotnet test AssetEditor.CN.sln --configuration Release --no-build
```

## 上游来源

本项目基于 Asset Editor 开发，并保留原项目及社区贡献者的历史。

- 上游项目：<https://github.com/donkeyProgramming/TheAssetEditor>
- 国区版问题反馈：<https://github.com/Szy-Cathay/TheAssetEditor-CN/issues>
