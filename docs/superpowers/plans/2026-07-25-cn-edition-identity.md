# Asset Editor 国区版身份隔离实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将当前下游仓库改造成可以与原版并存、只提供中文、拥有独立程序名、数据目录、IPC、更新源和仓库身份的 Asset Editor 国区版。

**Architecture:** 保留现有 `AssetEditor.*` C# 命名空间和功能项目结构，只替换会被用户、Windows、更新器或外部进程识别的产品身份。先在本地完成代码、资源和测试，再执行 GitHub 仓库、本地远程地址及工作目录的最终切换。

**Tech Stack:** .NET 10、WPF、MSBuild、MSTest、NUnit、Octokit、PowerShell、Pillow、GitHub CLI。

## Global Constraints

- 用户可见名称必须是 `Asset Editor 国区版`。
- 内部产品标识必须是 `AssetEditor.CN`。
- 首个版本必须是 `1.1.0`。
- 主程序必须是 `AssetEditor.CN.exe`。
- 更新器必须是 `AssetEditor.CN.Updater.exe`。
- 用户数据根目录必须是 `%USERPROFILE%\AssetEditor.CN`。
- IPC 命名管道必须是 `AssetEditor.CN.Ipc`，不兼容旧管道。
- 发行包只能包含 `Language_Cn.json`，不能包含英文或法文本地化文件。
- 不迁移、不读取、不修改 `%USERPROFILE%\AssetEditor` 中的原版数据。
- 不修改、移动或删除 `D:\TheAssetEditor`。
- 不批量改写现有 `AssetEditor.*` C# 命名空间。
- GitHub 仓库和本地工作目录只能在全部本地验证通过后改名。
- README 必须保留上游项目和原作者来源说明。

## File Map

- `AssetEditor.CN.sln`: 国区版解决方案名称和项目显示名。
- `AssetEditor/AssetEditor.csproj`: 主程序集、产品元数据、版本、发布语言和图标。
- `AssetEditorUpdater/AssetEditorUpdater.csproj`: 更新器程序集和版本。
- `Shared/SharedCore/Misc/DirectoryHelper.cs`: 国区版用户数据根目录。
- `Editors/Ipc/IpcEditor/AssetEditorIpcServer.cs`: 独立 IPC 管道。
- `AssetEditor/App.xaml.cs`: 固定加载中文。
- `Shared/SharedCore/Services/LocalizationManager.cs`: 单一中文资源加载。
- `Shared/SharedCore/Settings/ApplicationSettingsService.cs`: 删除语言偏好。
- `AssetEditor/ViewModels/SettingsViewModel.cs`: 删除语言选择状态和保存逻辑。
- `AssetEditor/Views/Settings/SettingsView.xaml`: 删除语言选择控件并收紧行号。
- `AssetEditor/Views/Settings/SettingsEnumConverter.cs`: 删除语言代码转换分支。
- `Shared/SharedCore/Services/VersionChecker.cs`: 国区版 Releases 地址。
- `AssetEditor/ViewModels/UpdaterViewModel.cs`: 国区版更新器文件名。
- `AssetEditorUpdater/AssetEditorUpdater.cs`: 国区版更新源、文件名、数据目录和中文控制台信息。
- `AssetEditor/Language_Cn.json`: 唯一用户界面语言、标题及反馈链接。
- `AssetEditor/AssetEditor.CN.png`、`AssetEditor/AssetEditor.CN.ico`: 独立图标资源。
- `AssetEditor/Themes/Controls.xaml`: 新程序集和新图标的 Pack URI。
- `Testing/Shared.Core.Test/Misc/DirectoryHelperTests.cs`: 数据目录隔离测试。
- `Editors/Ipc/Test.Ipc/AssetEditorIpcServerTests.cs`: IPC 名称测试。
- `Testing/AssetEditorTests/ChineseLocalizationTests.cs`: 单语言发布和标题测试。
- `Testing/Shared/TestUtility/PathHelper.cs`: 不再依赖仓库目录名称查找测试数据。
- `.github/workflows/pr-test.yml`: 新解决方案名称。
- `.editorconfig`: 删除上游开发者机器上的绝对路径。
- `docs/asseteditor-ipc.md`: 国区版 IPC 文档。
- `README.md`: 国区版说明和上游来源。

---

### Task 1: 隔离用户数据目录和 IPC

**Files:**
- Create: `Testing/Shared.Core.Test/Misc/DirectoryHelperTests.cs`
- Create: `Editors/Ipc/Test.Ipc/AssetEditorIpcServerTests.cs`
- Modify: `Shared/SharedCore/Misc/DirectoryHelper.cs`
- Modify: `Editors/Ipc/IpcEditor/AssetEditorIpcServer.cs`
- Modify: `docs/asseteditor-ipc.md`

**Interfaces:**
- Produces: `DirectoryHelper.ApplicationDirectory == Path.Combine(DirectoryHelper.UserDirectory, "AssetEditor.CN")`
- Produces: `DirectoryHelper.UpdateDirectory == Path.Combine(DirectoryHelper.UserDirectory, "AssetEditor.CN", "Temp", "Update")`
- Produces: `AssetEditorIpcServer.PipeName == "AssetEditor.CN.Ipc"`

- [ ] **Step 1: 写入会复现目录冲突的测试**

Create `Testing/Shared.Core.Test/Misc/DirectoryHelperTests.cs`:

```csharp
using Shared.Core.Misc;

namespace Test.Shared.Core.Misc
{
    [TestFixture]
    public class DirectoryHelperTests
    {
        [Test]
        public void ApplicationDirectory_UsesCnEditionRoot()
        {
            var expected = Path.Combine(DirectoryHelper.UserDirectory, "AssetEditor.CN");

            Assert.That(DirectoryHelper.ApplicationDirectory, Is.EqualTo(expected));
        }

        [Test]
        public void UpdateDirectory_IsUnderCnEditionRoot()
        {
            var expected = Path.Combine(
                DirectoryHelper.UserDirectory,
                "AssetEditor.CN",
                "Temp",
                "Update");

            Assert.That(DirectoryHelper.UpdateDirectory, Is.EqualTo(expected));
        }
    }
}
```

Create `Editors/Ipc/Test.Ipc/AssetEditorIpcServerTests.cs`:

```csharp
using Editors.Ipc;

namespace Test.Ipc
{
    public class AssetEditorIpcServerTests
    {
        [Test]
        public void PipeName_UsesCnEditionIdentity()
        {
            Assert.That(AssetEditorIpcServer.PipeName, Is.EqualTo("AssetEditor.CN.Ipc"));
        }
    }
}
```

- [ ] **Step 2: 运行测试并确认当前实现失败**

Run:

```powershell
dotnet test Testing\Shared.Core.Test\Test.Shared.Core.csproj --filter DirectoryHelperTests
dotnet test Editors\Ipc\Test.Ipc\Test.Ipc.csproj --filter AssetEditorIpcServerTests
```

Expected: 目录测试显示当前值仍是 `%USERPROFILE%\AssetEditor`，IPC 测试显示当前值仍是 `TheAssetEditor.Ipc`。

- [ ] **Step 3: 最小化修改运行时身份**

Change the root property in `Shared/SharedCore/Misc/DirectoryHelper.cs`:

```csharp
public static string ApplicationDirectory { get { return Path.Combine(UserDirectory, "AssetEditor.CN"); } }
```

Change the public constant in `Editors/Ipc/IpcEditor/AssetEditorIpcServer.cs`:

```csharp
public const string PipeName = "AssetEditor.CN.Ipc";
```

Update `docs/asseteditor-ipc.md` so the title is `Asset Editor 国区版 IPC`, the pipe name is `AssetEditor.CN.Ipc`, and the Windows path is:

```text
\\.\pipe\AssetEditor.CN.Ipc
```

- [ ] **Step 4: 运行目标测试**

Run:

```powershell
dotnet test Testing\Shared.Core.Test\Test.Shared.Core.csproj --filter DirectoryHelperTests
dotnet test Editors\Ipc\Test.Ipc\Test.Ipc.csproj --filter AssetEditorIpcServerTests
```

Expected: both commands pass.

- [ ] **Step 5: 提交运行时隔离**

```powershell
git add Testing\Shared.Core.Test\Misc\DirectoryHelperTests.cs
git add Editors\Ipc\Test.Ipc\AssetEditorIpcServerTests.cs
git add Shared\SharedCore\Misc\DirectoryHelper.cs
git add Editors\Ipc\IpcEditor\AssetEditorIpcServer.cs
git add docs\asseteditor-ipc.md
git commit -m "feat: isolate CN edition runtime identity"
```

---

### Task 2: 移除多语言并固定中文

**Files:**
- Create: `Testing/AssetEditorTests/ChineseLocalizationTests.cs`
- Modify: `AssetEditor/AssetEditor.csproj`
- Modify: `AssetEditor/App.xaml.cs`
- Modify: `Shared/SharedCore/Services/LocalizationManager.cs`
- Modify: `Shared/SharedCore/Settings/ApplicationSettingsService.cs`
- Modify: `AssetEditor/ViewModels/SettingsViewModel.cs`
- Modify: `AssetEditor/Views/Settings/SettingsView.xaml`
- Modify: `AssetEditor/Views/Settings/SettingsEnumConverter.cs`
- Modify: `AssetEditor/Language_Cn.json`
- Delete: `AssetEditor/Language_En.json`
- Delete: `AssetEditor/Language_Fr.json`

**Interfaces:**
- Produces: `LocalizationManager.LoadLanguage()` with no language parameter.
- Produces: the build output contains exactly `Language_Cn.json`.
- Removes: persisted language preference and runtime language switching.

- [ ] **Step 1: 在删除基线文件前验证中文键覆盖**

Run:

```powershell
$enKeys = (Get-Content -Raw AssetEditor\Language_En.json | ConvertFrom-Json).PSObject.Properties.Name
$cnKeys = (Get-Content -Raw AssetEditor\Language_Cn.json | ConvertFrom-Json).PSObject.Properties.Name
$missing = @(Compare-Object $enKeys $cnKeys | Where-Object SideIndicator -eq '<=')
if ($missing.Count -ne 0) { throw "Language_Cn.json is missing keys: $($missing.InputObject -join ', ')" }
```

Expected: no missing Chinese keys.

- [ ] **Step 2: 写入单语言发布测试**

Create `Testing/AssetEditorTests/ChineseLocalizationTests.cs`:

```csharp
using System.Text.Json;

namespace AssetEditorTests
{
    [TestClass]
    public class ChineseLocalizationTests
    {
        [TestMethod]
        public void BuildOutput_ContainsOnlyChineseLanguageFile()
        {
            var languageFiles = Directory
                .GetFiles(AppContext.BaseDirectory, "Language_*.json")
                .Select(Path.GetFileName)
                .OrderBy(name => name)
                .ToArray();

            CollectionAssert.AreEqual(new[] { "Language_Cn.json" }, languageFiles);
        }

        [TestMethod]
        public void ChineseLanguageFile_ContainsCnEditionTitle()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Language_Cn.json");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var title = document.RootElement.GetProperty("Title.AppTitle").GetString();

            Assert.AreEqual("Asset Editor 国区版 v{0}", title);
        }
    }
}
```

- [ ] **Step 3: 清理测试输出并确认测试失败**

Run:

```powershell
dotnet clean Testing\AssetEditorTests\AssetEditorTests.csproj
dotnet test Testing\AssetEditorTests\AssetEditorTests.csproj --filter ChineseLocalizationTests
```

Expected: the output contains three language files and the title is still `AssetEditor 国区特供 v{0}`.

- [ ] **Step 4: 将本地化管理器收窄为单一中文文件**

In `Shared/SharedCore/Services/LocalizationManager.cs`:

- Replace `_selectedLangauge` with:

```csharp
private const string LanguageFile = "Language_Cn.json";
```

- Remove `SelectedLangauge` and `GetPossibleLanguages()`.
- Change `LoadLanguage(string languageCode)` to `LoadLanguage()`.
- Read only `Language_Cn.json`.
- Use these user-visible error messages:

```csharp
MessageBox.Show($"找不到中文语言文件“{LanguageFile}”。");
MessageBox.Show($"中文语言文件解析失败：{LanguageFile}");
MessageBox.Show($"中文语言文件加载失败：{ex.Message}");
```

- Keep `Get` and `GetFormat`; their logging should identify the fixed Chinese file rather than a selected language code.

In `AssetEditor/App.xaml.cs`, replace the two language discovery/loading calls with:

```csharp
var localizationManager = _serviceProvider.GetRequiredService<LocalizationManager>();
localizationManager.LoadLanguage();
```

- [ ] **Step 5: 删除设置中的语言状态和控件**

In `Shared/SharedCore/Settings/ApplicationSettingsService.cs`, remove:

```csharp
public string SelectedLangauge { get; set; } = "en";
```

In `AssetEditor/ViewModels/SettingsViewModel.cs`:

- Remove `_localizationManager`.
- Remove `AvailableLangauges` and `SelectedLanguage`.
- Remove `LocalizationManager` from the constructor parameters.
- Remove language initialization, language persistence and `LoadLanguage` from `Save()`.

In `AssetEditor/Views/Settings/SettingsView.xaml`:

- Delete the `SettingsWindow.SelectedLanguage` label and the language `ComboBox`.
- Shift the remaining application settings rows from `3..11` to `2..10`.
- Remove one now-unused `<RowDefinition Height="Auto"/>`.

In `AssetEditor/Views/Settings/SettingsEnumConverter.cs`, remove only the `value is string langCode` branch; preserve game, theme, background and font conversion.

- [ ] **Step 6: 删除其他语言资源并更新中文身份**

In `AssetEditor/AssetEditor.csproj`, keep only:

```xml
<None Update="Language_Cn.json">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```

Delete:

```text
AssetEditor/Language_En.json
AssetEditor/Language_Fr.json
```

In `AssetEditor/Language_Cn.json`:

- Set `Title.AppTitle` to `Asset Editor 国区版 v{0}`.
- Change `UpdaterWindow.UpdateInfo` to start with `Asset Editor 国区版有新版本可用！`.
- Point `Shared.CustomException.InfoText` to `https://github.com/Szy-Cathay/TheAssetEditor-CN/issues`.
- Remove `SettingsWindow.SelectedLanguage` and the `Language.CN`, `Language.EN`, `Language.FR` entries.
- Update the top comment to identify `Asset Editor 国区版中文本地化`.

- [ ] **Step 7: 清理并运行本地化测试**

Run:

```powershell
dotnet clean Testing\AssetEditorTests\AssetEditorTests.csproj
dotnet test Testing\AssetEditorTests\AssetEditorTests.csproj --filter ChineseLocalizationTests
rg -n "Language_En|Language_Fr|SelectedLangauge|SelectedLanguage|AvailableLangauges|GetPossibleLanguages" AssetEditor Shared
```

Expected: tests pass and `rg` returns no active source matches.

- [ ] **Step 8: 提交中文专用发行改动**

```powershell
git add AssetEditor Shared Testing\AssetEditorTests
git commit -m "feat: make CN edition Chinese-only"
```

---

### Task 3: 设置解决方案、程序集和版本身份

**Files:**
- Move: `AssetEditor.sln` to `AssetEditor.CN.sln`
- Modify: `AssetEditor.CN.sln`
- Modify: `AssetEditor/AssetEditor.csproj`
- Modify: `AssetEditorUpdater/AssetEditorUpdater.csproj`
- Modify: `AssetEditor/Themes/Controls.xaml`
- Modify: `Testing/Shared/TestUtility/PathHelper.cs`
- Modify: `.github/workflows/pr-test.yml`
- Modify: `.editorconfig`

**Interfaces:**
- Produces: main assembly name `AssetEditor.CN`.
- Produces: updater assembly name `AssetEditor.CN.Updater`.
- Preserves: root namespace `AssetEditor` and updater namespace `AssetEditorUpdater`.
- Produces: version `1.1.0`.

- [ ] **Step 1: 运行当前程序集身份检查并确认失败**

Run:

```powershell
$mainAssembly = dotnet msbuild AssetEditor\AssetEditor.csproj -getProperty:AssemblyName
$updaterAssembly = dotnet msbuild AssetEditorUpdater\AssetEditorUpdater.csproj -getProperty:AssemblyName
if ($mainAssembly -ne "AssetEditor.CN") { Write-Error "Main assembly is $mainAssembly" }
if ($updaterAssembly -ne "AssetEditor.CN.Updater") { Write-Error "Updater assembly is $updaterAssembly" }
```

Expected: both checks report the old assembly names.

- [ ] **Step 2: 设置主程序元数据**

Add or update these properties in `AssetEditor/AssetEditor.csproj`:

```xml
<AssemblyName>AssetEditor.CN</AssemblyName>
<RootNamespace>AssetEditor</RootNamespace>
<Authors>Szy-Cathay;AssetEdCommunity</Authors>
<Company>Szy-Cathay</Company>
<Product>AssetEditor.CN</Product>
<PackageProjectUrl>https://github.com/Szy-Cathay/TheAssetEditor-CN</PackageProjectUrl>
<RepositoryUrl>https://github.com/Szy-Cathay/TheAssetEditor-CN</RepositoryUrl>
<PackageId>AssetEditor.CN</PackageId>
<Version>1.1.0</Version>
<AssemblyVersion>$(Version)</AssemblyVersion>
```

Keep the physical project file and source folder names unchanged.

- [ ] **Step 3: 设置更新器程序集身份**

Add these properties to `AssetEditorUpdater/AssetEditorUpdater.csproj`:

```xml
<AssemblyName>AssetEditor.CN.Updater</AssemblyName>
<RootNamespace>AssetEditorUpdater</RootNamespace>
<Product>AssetEditor.CN.Updater</Product>
<Version>1.1.0</Version>
```

- [ ] **Step 4: 改名解决方案并更新构建入口**

Move `AssetEditor.sln` to `AssetEditor.CN.sln`.

In `AssetEditor.CN.sln`:

```text
"AssetEditor"        -> "AssetEditor.CN"
"AssetEditorUpdater" -> "AssetEditor.CN.Updater"
```

Do not change project GUIDs or physical `.csproj` paths.

In `.github/workflows/pr-test.yml`, change the test command to:

```yaml
run: dotnet test AssetEditor.CN.sln --configuration Release --no-restore --verbosity normal
```

- [ ] **Step 5: 让测试数据查找不依赖仓库文件夹名**

Replace name-based root discovery in `Testing/Shared/TestUtility/PathHelper.cs` with:

```csharp
private static string FindRepositoryRoot()
{
    DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
    while (current != null)
    {
        if (File.Exists(Path.Combine(current.FullName, "AssetEditor.CN.sln")))
            return current.FullName;

        current = current.Parent;
    }

    throw new DirectoryNotFoundException("Unable to locate AssetEditor.CN.sln.");
}
```

Make `GetDataFolder` and `GetDataFile` combine paths from `FindRepositoryRoot()` and remove their `rootDir` parameter. Preserve existing lowercase behavior in `GetDataFolder` only if existing callers depend on it.

Change `.editorconfig` from the old developer-specific absolute path to:

```ini
spelling_exclusion_path = spellingexclusions.dic
```

- [ ] **Step 6: 更新程序集 Pack URI**

In `AssetEditor/Themes/Controls.xaml`, change the assembly portion only:

```xml
<Setter Property="Icon" Value="pack://application:,,,/AssetEditor.CN;component/AssetEditorIcon.png"/>
```

The icon filename changes in Task 5; keeping the old filename temporarily makes this task independently buildable.

- [ ] **Step 7: 验证程序集和解决方案**

Run:

```powershell
$mainAssembly = dotnet msbuild AssetEditor\AssetEditor.csproj -getProperty:AssemblyName
$updaterAssembly = dotnet msbuild AssetEditorUpdater\AssetEditorUpdater.csproj -getProperty:AssemblyName
if ($mainAssembly -ne "AssetEditor.CN") { throw "Unexpected main assembly: $mainAssembly" }
if ($updaterAssembly -ne "AssetEditor.CN.Updater") { throw "Unexpected updater assembly: $updaterAssembly" }
dotnet build AssetEditor.CN.sln --configuration Debug
dotnet test Testing\Shared\Test.TestingUtility.csproj --configuration Debug --no-build
```

Expected: identity checks and build pass. If the test project has no discoverable tests, `dotnet test` must still exit successfully.

- [ ] **Step 8: 提交构建身份**

```powershell
git add AssetEditor.CN.sln AssetEditor AssetEditorUpdater Testing\Shared .github\workflows\pr-test.yml .editorconfig
git add -u AssetEditor.sln
git commit -m "build: rename CN edition outputs"
```

---

### Task 4: 切换国区版更新源和更新器文件名

**Files:**
- Modify: `Shared/SharedCore/Services/VersionChecker.cs`
- Modify: `AssetEditor/ViewModels/UpdaterViewModel.cs`
- Modify: `AssetEditorUpdater/AssetEditorUpdater.cs`

**Interfaces:**
- Consumes: main executable `AssetEditor.CN.exe`.
- Consumes: updater executable `AssetEditor.CN.Updater.exe`.
- Produces: release source `Szy-Cathay/TheAssetEditor-CN`.
- Produces: updater work directory `%USERPROFILE%\AssetEditor.CN\Temp\Update`.

- [ ] **Step 1: 运行旧身份扫描并确认失败**

Run:

```powershell
$matches = rg -n "donkeyProgramming|GitHubRepository = `"TheAssetEditor`"|AssetEditorUpdater\.exe|AssetEditor\.exe|Path\.Combine\(userDirectory, `"AssetEditor`"" Shared\SharedCore\Services\VersionChecker.cs AssetEditor\ViewModels\UpdaterViewModel.cs AssetEditorUpdater\AssetEditorUpdater.cs
if ($LASTEXITCODE -eq 0) { $matches; throw "Old updater identity is still active." }
```

Expected: the command reports the current upstream owner, repository, executable names and user directory.

- [ ] **Step 2: 更新主程序的版本检查**

In `Shared/SharedCore/Services/VersionChecker.cs`:

```csharp
private const string GitHubOwner = "Szy-Cathay";
private const string GitHubRepository = "TheAssetEditor-CN";
```

Also change the Octokit product header to:

```csharp
new ProductHeaderValue("AssetEditor.CN")
```

- [ ] **Step 3: 更新主程序启动的更新器文件名**

In `AssetEditor/ViewModels/UpdaterViewModel.cs`:

```csharp
private const string AssetEditorUpdaterExe = "AssetEditor.CN.Updater.exe";
```

Change its update log message to name `Asset Editor 国区版`.

- [ ] **Step 4: 更新独立更新器身份**

In `AssetEditorUpdater/AssetEditorUpdater.cs`, use:

```csharp
private const string GitHubOwner = "Szy-Cathay";
private const string GitHubRepository = "TheAssetEditor-CN";
private const string AssetEditorExe = "AssetEditor.CN.exe";
private const string AssetEditorUpdaterExe = "AssetEditor.CN.Updater.exe";
private const string UpdateFilesDirectoryName = "AssetEditor.CN";
```

Build the update directory with:

```csharp
var updateDirectory = Path.Combine(userDirectory, "AssetEditor.CN", "Temp", "Update");
```

Use `AssetEditor.CN` as the Octokit and HTTP user-agent product identity.

Translate the updater's user-visible console messages with these meanings:

```text
国区版更新器运行目录：{currentDirectory}
正在将更新器复制到：{updateDirectory}
当前已是最新版本。
正在将 Asset Editor 国区版从 {installedVersion} 更新到 {latestVersion}。
无法从 GitHub 获取最新版本：{exception.Message}
正在下载最新版本……
无法从 GitHub 下载最新版本。
正在备份 {installationDirectory} 到 {updateBackupDirectory}……
正在解压更新文件到 {installationDirectory}……
更新完成。
正在重新启动 Asset Editor 国区版……
更新失败：未找到 AssetEditor.CN.exe。
按任意键关闭。
```

- [ ] **Step 5: 验证更新身份**

Run:

```powershell
$matches = rg -n "donkeyProgramming|GitHubRepository = `"TheAssetEditor`"|AssetEditorUpdater\.exe|AssetEditor\.exe|Path\.Combine\(userDirectory, `"AssetEditor`"" Shared\SharedCore\Services\VersionChecker.cs AssetEditor\ViewModels\UpdaterViewModel.cs AssetEditorUpdater\AssetEditorUpdater.cs
if ($LASTEXITCODE -eq 0) { $matches; throw "Old updater identity is still active." }
dotnet build AssetEditor\AssetEditor.csproj --configuration Debug
dotnet build AssetEditorUpdater\AssetEditorUpdater.csproj --configuration Debug
```

Expected: no old active identity matches and both builds pass.

- [ ] **Step 6: 提交更新系统改动**

```powershell
git add Shared\SharedCore\Services\VersionChecker.cs
git add AssetEditor\ViewModels\UpdaterViewModel.cs
git add AssetEditorUpdater\AssetEditorUpdater.cs
git commit -m "feat: point updates to CN edition releases"
```

---

### Task 5: 制作独立图标并改写 README

**Files:**
- Create: `AssetEditor/AssetEditor.CN.png`
- Create: `AssetEditor/AssetEditor.CN.ico`
- Modify: `AssetEditor/AssetEditor.csproj`
- Modify: `AssetEditor/Themes/Controls.xaml`
- Modify: `README.md`
- Delete: `AssetEditor/AssetEditorIcon.png`
- Delete: `AssetEditor/AssetEditorIcon.ico`

**Interfaces:**
- Consumes: existing `AssetEditor/AssetEditorIcon.png` as the visual source.
- Produces: WPF resource `AssetEditor.CN.png`.
- Produces: Windows application icon `AssetEditor.CN.ico`.

- [ ] **Step 1: 检查原图标**

Use `view_image` on the absolute path:

```text
D:\TheAssetEditor-Szy\AssetEditor\AssetEditorIcon.png
```

Record its composition and confirm the source is readable before editing.

- [ ] **Step 2: 使用 imagegen 生成带 CN 角标的透明 PNG**

Load the `imagegen` skill, then edit the existing PNG with this prompt:

```text
Preserve the existing Asset Editor logo, silhouette, colors, transparency, and centered composition. Add a compact high-contrast red corner badge at the bottom-right containing the uppercase white letters "CN". The badge must remain clearly readable at 16, 24, 32, 48, and 64 pixel icon sizes. Do not add any other text, background, shadow, glow, border, or decorative element. Return a square transparent PNG suitable for a Windows application icon.
```

Save the approved result as:

```text
AssetEditor/AssetEditor.CN.png
```

- [ ] **Step 3: 目视检查小尺寸可读性**

Use `view_image` on `AssetEditor/AssetEditor.CN.png`. Confirm:

- the original logo is still recognizable;
- the canvas is square and transparent;
- `CN` is not clipped;
- the badge remains legible when the image is viewed at taskbar scale.

Regenerate once with a larger badge if the letters disappear at small size.

- [ ] **Step 4: 生成多尺寸 ICO**

Run:

```powershell
@'
from PIL import Image

source = r"AssetEditor\AssetEditor.CN.png"
target = r"AssetEditor\AssetEditor.CN.ico"
sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]

with Image.open(source) as image:
    image.convert("RGBA").save(target, format="ICO", sizes=sizes)
'@ | python -
```

Expected: `AssetEditor/AssetEditor.CN.ico` exists and is non-empty.

- [ ] **Step 5: 切换项目图标引用**

In `AssetEditor/AssetEditor.csproj`:

```xml
<ApplicationIcon>AssetEditor.CN.ico</ApplicationIcon>
<None Remove="AssetEditor.CN.png" />
<Resource Include="AssetEditor.CN.png" />
```

In `AssetEditor/Themes/Controls.xaml`:

```xml
<Setter Property="Icon" Value="pack://application:,,,/AssetEditor.CN;component/AssetEditor.CN.png"/>
```

Delete the two unreferenced old icon files only after the new references are in place.

- [ ] **Step 6: 改写 README**

Replace `README.md` with:

````markdown
# Asset Editor 国区版

Asset Editor 国区版是面向中国大陆用户维护的 Total War 资产编辑工具下游版本，仅提供中文界面。

## 版本身份

- 程序：`AssetEditor.CN.exe`
- 用户数据：`%USERPROFILE%\AssetEditor.CN`
- 更新仓库：`Szy-Cathay/TheAssetEditor-CN`
- 当前版本：`1.1.0`

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
````

- [ ] **Step 7: 构建并检查图标资源**

Run:

```powershell
dotnet clean AssetEditor\AssetEditor.csproj
dotnet build AssetEditor\AssetEditor.csproj --configuration Debug
$png = Get-Item AssetEditor\AssetEditor.CN.png
$ico = Get-Item AssetEditor\AssetEditor.CN.ico
if ($png.Length -eq 0 -or $ico.Length -eq 0) { throw "CN icon asset is empty." }
```

Expected: build succeeds and both assets are non-empty.

- [ ] **Step 8: 提交品牌资源**

```powershell
git add AssetEditor\AssetEditor.csproj
git add AssetEditor\Themes\Controls.xaml
git add AssetEditor\AssetEditor.CN.png
git add AssetEditor\AssetEditor.CN.ico
git add README.md
git add -u AssetEditor\AssetEditorIcon.png AssetEditor\AssetEditorIcon.ico
git commit -m "feat: apply CN edition branding"
```

---

### Task 6: 执行完整本地验收

**Files:**
- Verify only; do not change repository identity in this task.

**Interfaces:**
- Consumes: all local implementation commits.
- Produces: a pass/fail gate for GitHub and folder rename.

- [ ] **Step 1: 检查工作区和原版保护目标**

Run:

```powershell
git status --short --branch
if (-not (Test-Path -LiteralPath 'D:\TheAssetEditor')) { throw 'Original repository is missing.' }
if (Test-Path -LiteralPath 'D:\TheAssetEditor-CN') { throw 'Target directory already exists.' }
```

Expected: worktree is clean, the original repository exists, and the new target does not exist.

- [ ] **Step 2: 完整还原、构建和测试**

Run:

```powershell
dotnet restore AssetEditor.CN.sln
dotnet build AssetEditor.CN.sln --configuration Release --no-restore
dotnet test AssetEditor.CN.sln --configuration Release --no-build --verbosity normal
```

Expected: all commands exit successfully. Any failure blocks Tasks 7.

- [ ] **Step 3: 发布到唯一临时目录**

Run:

```powershell
$publishDir = Join-Path $env:TEMP "AssetEditor.CN-publish-$([Guid]::NewGuid().ToString('N'))"
dotnet publish AssetEditor\AssetEditor.csproj --configuration Release --runtime win-x64 --self-contained false --output $publishDir
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
```

Expected: publish succeeds without touching either repository directory.

- [ ] **Step 4: 验证发布产物身份**

Run:

```powershell
$required = @('AssetEditor.CN.exe', 'AssetEditor.CN.Updater.exe', 'Language_Cn.json')
foreach ($name in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDir $name))) { throw "Missing $name" }
}

$forbidden = @('AssetEditor.exe', 'AssetEditorUpdater.exe', 'Language_En.json', 'Language_Fr.json')
foreach ($name in $forbidden) {
    if (Test-Path -LiteralPath (Join-Path $publishDir $name)) { throw "Forbidden output: $name" }
}

$languageFiles = @(Get-ChildItem -LiteralPath $publishDir -Filter 'Language_*.json' | Select-Object -ExpandProperty Name)
if ($languageFiles.Count -ne 1 -or $languageFiles[0] -ne 'Language_Cn.json') {
    throw "Unexpected language files: $($languageFiles -join ', ')"
}

$versionInfo = (Get-Item -LiteralPath (Join-Path $publishDir 'AssetEditor.CN.exe')).VersionInfo
if ($versionInfo.ProductName -ne 'AssetEditor.CN') { throw "Unexpected product: $($versionInfo.ProductName)" }
if (-not $versionInfo.FileVersion.StartsWith('1.1.0')) { throw "Unexpected version: $($versionInfo.FileVersion)" }
```

- [ ] **Step 5: 扫描活动代码中的旧身份**

Run:

```powershell
$oldIdentity = rg -n "TheAssetEditor\.Ipc|donkeyProgramming|Language_En|Language_Fr|SelectedLangauge|AssetEditorUpdater\.exe|AssetEditor\.exe" AssetEditor AssetEditorUpdater Shared Editors\Ipc .github
if ($LASTEXITCODE -eq 0) { $oldIdentity; throw "Active old identity remains." }
```

Expected: no matches. README and historical notes are deliberately excluded because README preserves upstream attribution.

- [ ] **Step 6: 启动发布程序并验证新目录和 IPC**

Before launch, record the original settings hash:

```powershell
$originalSettings = Join-Path $env:USERPROFILE 'AssetEditor\ApplicationSettings.json'
$originalHashBefore = if (Test-Path -LiteralPath $originalSettings) {
    (Get-FileHash -LiteralPath $originalSettings -Algorithm SHA256).Hash
} else {
    $null
}

$cnRoot = Join-Path $env:USERPROFILE 'AssetEditor.CN'
if (Test-Path -LiteralPath $cnRoot) { throw "CN data directory already exists before first-run verification." }

$app = Start-Process -FilePath (Join-Path $publishDir 'AssetEditor.CN.exe') -WorkingDirectory $publishDir -PassThru
Start-Sleep -Seconds 10

if ($app.HasExited) { throw "AssetEditor.CN.exe exited during startup." }
if (-not (Test-Path -LiteralPath (Join-Path $cnRoot 'ApplicationSettings.json'))) {
    throw "CN settings file was not created."
}
```

The first launch intentionally opens the settings window before starting IPC. Load the `computer-use` skill, focus the `Asset Editor 国区版` settings window, and click its bottom-right `保存` button. Then wait for normal startup:

```powershell
Start-Sleep -Seconds 10

$pipes = Get-ChildItem -LiteralPath '\\.\pipe\' | Select-Object -ExpandProperty Name
if ($pipes -notcontains 'AssetEditor.CN.Ipc') { throw "CN IPC pipe is not running." }

Stop-Process -Id $app.Id
$app.WaitForExit()
```

Then verify the original setting file is unchanged:

```powershell
$originalHashAfter = if (Test-Path -LiteralPath $originalSettings) {
    (Get-FileHash -LiteralPath $originalSettings -Algorithm SHA256).Hash
} else {
    $null
}

if ($originalHashBefore -ne $originalHashAfter) {
    throw "Original AssetEditor settings changed."
}
```

- [ ] **Step 7: 检查图标并清理临时发布目录**

Use `view_image` on:

```text
D:\TheAssetEditor-Szy\AssetEditor\AssetEditor.CN.png
```

After inspection, verify the recursive delete target is the unique temporary directory:

```powershell
$resolvedPublishDir = [IO.Path]::GetFullPath($publishDir)
$resolvedTemp = [IO.Path]::GetFullPath($env:TEMP)
if (-not $resolvedPublishDir.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove non-temp path: $resolvedPublishDir"
}
Remove-Item -LiteralPath $resolvedPublishDir -Recurse -Force
```

Expected: the icon is readable and only the unique publish directory is removed.

- [ ] **Step 8: Record the verification gate**

Run:

```powershell
git status --short --branch
git log --oneline --decorate -6
```

Expected: worktree remains clean. Do not continue if any required verification failed.

---

### Task 7: 切换 GitHub、Git 远程和本地目录

**Files:**
- External: GitHub repository name.
- Local Git config: `origin` and removal of `upstream`.
- Local filesystem: `D:\TheAssetEditor-Szy` to `D:\TheAssetEditor-CN`.

**Interfaces:**
- Consumes: a fully verified and clean branch.
- Produces: one GitHub repository `Szy-Cathay/TheAssetEditor-CN`.
- Produces: one local remote named `origin`.
- Produces: local path `D:\TheAssetEditor-CN`.

- [ ] **Step 1: Reconfirm authentication, branch and clean state**

Run:

```powershell
gh auth status
git branch --show-current
git status --short
```

Expected: authenticated as `Szy-Cathay`, branch is `codex/cn-edition-identity`, and status is empty.

- [ ] **Step 2: Push the verified branch before renaming**

Run:

```powershell
git push -u origin codex/cn-edition-identity
```

Expected: the branch exists on `Szy-Cathay/TheAssetEditor-Szy`.

- [ ] **Step 3: Rename the GitHub repository**

Run:

```powershell
gh repo rename -R Szy-Cathay/TheAssetEditor-Szy TheAssetEditor-CN --yes
gh repo view Szy-Cathay/TheAssetEditor-CN --json nameWithOwner,url
```

Expected: `nameWithOwner` is `Szy-Cathay/TheAssetEditor-CN`.

- [ ] **Step 4: Update the local remote and remove upstream**

Run each write separately:

```powershell
git remote set-url origin https://github.com/Szy-Cathay/TheAssetEditor-CN.git
```

```powershell
git remote remove upstream
```

Then verify:

```powershell
git fetch --prune origin
git remote -v
```

Expected: only `origin` remains and both fetch/push URLs target `Szy-Cathay/TheAssetEditor-CN.git`.

- [ ] **Step 5: Verify absolute source and target paths before moving**

Run:

```powershell
$source = [IO.Path]::GetFullPath('D:\TheAssetEditor-Szy')
$target = [IO.Path]::GetFullPath('D:\TheAssetEditor-CN')

if ($source -ne 'D:\TheAssetEditor-Szy') { throw "Unexpected source: $source" }
if ($target -ne 'D:\TheAssetEditor-CN') { throw "Unexpected target: $target" }
if (-not (Test-Path -LiteralPath $source)) { throw "Source repository is missing." }
if (Test-Path -LiteralPath $target) { throw "Target repository already exists." }
if (-not (Test-Path -LiteralPath 'D:\TheAssetEditor')) { throw "Original repository is missing." }
```

- [ ] **Step 6: Rename the local repository directory as the final filesystem write**

Run from `D:\`, not from inside the repository:

```powershell
Move-Item -LiteralPath 'D:\TheAssetEditor-Szy' -Destination 'D:\TheAssetEditor-CN'
```

Do not use a recursive copy, delete, or cross-shell path handoff.

- [ ] **Step 7: Final verification from the new path**

Run:

```powershell
git -C 'D:\TheAssetEditor-CN' status --short --branch
git -C 'D:\TheAssetEditor-CN' remote -v
git -C 'D:\TheAssetEditor-CN' branch --show-current
Test-Path -LiteralPath 'D:\TheAssetEditor'
Test-Path -LiteralPath 'D:\TheAssetEditor-Szy'
Test-Path -LiteralPath 'D:\TheAssetEditor-CN'
```

Expected:

```text
branch: codex/cn-edition-identity
remote: only origin -> https://github.com/Szy-Cathay/TheAssetEditor-CN.git
D:\TheAssetEditor exists: True
D:\TheAssetEditor-Szy exists: False
D:\TheAssetEditor-CN exists: True
```
