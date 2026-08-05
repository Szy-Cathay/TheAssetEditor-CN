# AE UI Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立不依赖业务页面的 AE UI 设计变量、深色石墨语义 Brush、字体角色和基础表面样式，并让真实应用与 WPF 测试宿主使用同一资源顺序。

**Architecture:** 现有主题字典继续提供旧 `AColour.*`/`ABrush.*` 资源，同时新增稳定的 `AeBrush.*` 语义契约；未迁移页面不改样式引用。非颜色变量和键控文字/表面样式放入独立 ResourceDictionary，通过 `App.xaml` 与 `WpfTestApplicationHost` 以相同顺序加载。

**Tech Stack:** .NET 10、WPF ResourceDictionary、XAML、C#、NUnit、`ThemesController`、`WpfTestApplicationHost`。

## Global Constraints

- 以 `docs/superpowers/specs/2026-08-05-ae-ui-design-system-design.md` 为批准设计。
- 本批只建立基础资源和测试，不迁移主窗口或业务页面。
- 新样式必须有明确 `x:Key`，不得替换现有隐式控件样式。
- 主题 Brush 使用 `DynamicResource`；间距、几何、尺寸和时长使用不可变资源。
- 默认字体目标为 `Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI`；阿里普惠体和 HarmonyOS 设置仍可预览、取消恢复和保存。
- `Shared.Ui` 依赖方向保持不变。
- 实际代码执行前创建独立 `codex/ae-ui-foundation` 分支；计划中的提交步骤需要执行阶段明确授权本地提交。
- 不推送、不创建 PR、不合并。

---

## File Structure

### Files created by this phase

- `AssetEditor/Themes/DesignSystem/DesignTokens.xaml` — 数值尺寸、间距、圆角和动效时长。
- `AssetEditor/Themes/DesignSystem/Typography.xaml` — 键控文字角色。
- `AssetEditor/Themes/DesignSystem/SurfaceStyles.xaml` — 键控基础表面样式。
- `Testing/AssetEditorTests/UiDesignSystemResourceTests.cs` — 所有主题、变量、样式和动态切换契约。
- `docs/superpowers/plans/ae-ui-migration-ledger.md` — 临时全量 UI 迁移清单。

### Files modified by this phase

- `AssetEditor/App.xaml` — 合并新设计系统字典。
- `AssetEditor/Themes/ControlColours.xaml` — 默认 Windows UI 字体栈。
- `AssetEditor/Themes/ColourDictionaries/DarkTheme.xaml` — 精确石墨蓝语义 Brush。
- `AssetEditor/Themes/ColourDictionaries/ChromeDark.xaml`
- `AssetEditor/Themes/ColourDictionaries/VSCodeDark.xaml`
- `AssetEditor/Themes/ColourDictionaries/HighContrastDark.xaml`
- `AssetEditor/Themes/ColourDictionaries/WarmDark.xaml`
- `AssetEditor/Themes/ColourDictionaries/CoolDark.xaml`
- `AssetEditor/Themes/ColourDictionaries/LightTheme.xaml`
- `AssetEditor/Themes/ColourDictionaries/ChromeLight.xaml`
- `AssetEditor/Themes/ColourDictionaries/VSCodeLight.xaml`
- `AssetEditor/Themes/ColourDictionaries/HighContrastLight.xaml`
- `Testing/AssetEditorTests/WpfTestApplicationHost.cs` — 与应用相同的资源加载顺序。
- `Testing/AssetEditorTests/SettingsViewModelTests.cs` — 默认系统字体和自定义字体回归。

## Interfaces

### Semantic brush contract produced

```text
AeBrush.Canvas
AeBrush.Surface1
AeBrush.Surface2
AeBrush.Surface3
AeBrush.SurfaceHover
AeBrush.Border
AeBrush.BorderStrong
AeBrush.TextPrimary
AeBrush.TextSecondary
AeBrush.TextMuted
AeBrush.Accent
AeBrush.AccentHover
AeBrush.AccentSoft
AeBrush.Success
AeBrush.Warning
AeBrush.Danger
```

### Metric contract produced

```text
AeSpace.1 = 4
AeSpace.2 = 8
AeSpace.3 = 12
AeSpace.4 = 16
AeSpace.6 = 24
AeSpace.8 = 32
AeSize.ActivityRailWidth = 34
AeSize.TabHeight = 24
AeSize.CompactRowHeight = 28
AeSize.ControlHeight = 30
AeSize.ProminentControlHeight = 34
AeRadius.Compact = 3
AeRadius.Control = 4
AeRadius.Surface = 6
AeRadius.Overlay = 7
AeMotion.Pressed = 70 ms
AeMotion.Hover = 120 ms
AeMotion.Selection = 140 ms
AeMotion.Overlay = 160 ms
AeMotion.OverlayOffset = 2
```

### Keyed style contract produced

```text
AeText.PageTitle
AeText.SectionTitle
AeText.Body
AeText.Label
AeText.Caption
AeText.Technical
AeSurface.Canvas
AeSurface.Panel
AeSurface.Control
AeSurface.Overlay
```

Later plans consume these names exactly. Renaming a key requires updating this plan, its tests, and every completed consumer in the same change.

---

### Task 1: Establish the semantic brush contract across every theme

**Files:**

- Create: `Testing/AssetEditorTests/UiDesignSystemResourceTests.cs`
- Modify: all ten files under `AssetEditor/Themes/ColourDictionaries/*.xaml`

**Interfaces:**

- Consumes: existing `AColour.*` values inside each theme dictionary.
- Produces: the sixteen `AeBrush.*` resources listed above.

- [ ] **Step 1: Write the failing all-theme resource test**

Create `UiDesignSystemResourceTests.cs` with the following initial content:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NUnit.Framework;
using Shared.Core.Settings;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class UiDesignSystemResourceTests
{
    private static readonly string[] SemanticBrushKeys =
    [
        "AeBrush.Canvas",
        "AeBrush.Surface1",
        "AeBrush.Surface2",
        "AeBrush.Surface3",
        "AeBrush.SurfaceHover",
        "AeBrush.Border",
        "AeBrush.BorderStrong",
        "AeBrush.TextPrimary",
        "AeBrush.TextSecondary",
        "AeBrush.TextMuted",
        "AeBrush.Accent",
        "AeBrush.AccentHover",
        "AeBrush.AccentSoft",
        "AeBrush.Success",
        "AeBrush.Warning",
        "AeBrush.Danger",
    ];

    [TestCaseSource(nameof(ThemeNames))]
    public void EveryTheme_ExposesSemanticBrushContract(string themeName)
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            var dictionary = Load(
                $"Themes/ColourDictionaries/{themeName}.xaml");

            NUnitAssert.Multiple(() =>
            {
                foreach (var key in SemanticBrushKeys)
                {
                    NUnitAssert.That(
                        dictionary.Contains(key),
                        Is.True,
                        $"{themeName} is missing {key}.");
                    NUnitAssert.That(
                        dictionary[key],
                        Is.InstanceOf<SolidColorBrush>(),
                        $"{themeName} {key} is not a SolidColorBrush.");
                }
            });
        });
    }

    [Test]
    public void DarkTheme_UsesApprovedGraphitePalette()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            var dictionary = Load(
                "Themes/ColourDictionaries/DarkTheme.xaml");
            var expected = new Dictionary<string, string>
            {
                ["AeBrush.Canvas"] = "#FF151719",
                ["AeBrush.Surface1"] = "#FF1B1E21",
                ["AeBrush.Surface2"] = "#FF212529",
                ["AeBrush.Surface3"] = "#FF282D32",
                ["AeBrush.SurfaceHover"] = "#FF30363C",
                ["AeBrush.Border"] = "#FF343A40",
                ["AeBrush.BorderStrong"] = "#FF464E56",
                ["AeBrush.TextPrimary"] = "#FFE4E7E9",
                ["AeBrush.TextSecondary"] = "#FFB0B6BC",
                ["AeBrush.TextMuted"] = "#FF858D95",
                ["AeBrush.Accent"] = "#FF64A9E2",
                ["AeBrush.AccentHover"] = "#FF75B5E8",
                ["AeBrush.AccentSoft"] = "#FF263A4B",
                ["AeBrush.Success"] = "#FF72BC91",
                ["AeBrush.Warning"] = "#FFE2B45F",
                ["AeBrush.Danger"] = "#FFE17979",
            };

            NUnitAssert.Multiple(() =>
            {
                foreach (var pair in expected)
                {
                    var brush = (SolidColorBrush)dictionary[pair.Key];
                    NUnitAssert.That(
                        brush.Color.ToString(),
                        Is.EqualTo(pair.Value),
                        pair.Key);
                }
            });
        });
    }

    private static IEnumerable<string> ThemeNames() =>
        Enum.GetNames<ThemeType>();

    private static ResourceDictionary Load(string path) => new()
    {
        Source = new Uri(
            $"pack://application:,,,/AssetEditor.CN;component/{path}"),
    };
}
```

- [ ] **Step 2: Run the focused test and confirm the contract is absent**

Run:

```powershell
dotnet test Testing/AssetEditorTests/AssetEditorTests.csproj --configuration Release --filter "FullyQualifiedName~UiDesignSystemResourceTests" --verbosity normal
```

Expected: FAIL because every theme is missing `AeBrush.Canvas` and the remaining semantic keys.

- [ ] **Step 3: Add the exact DarkTheme brush block**

Append this block after the existing brushes in `DarkTheme.xaml`:

```xml
<!-- AE UI design-system semantic brushes -->
<SolidColorBrush x:Key="AeBrush.Canvas" Color="#151719" />
<SolidColorBrush x:Key="AeBrush.Surface1" Color="#1B1E21" />
<SolidColorBrush x:Key="AeBrush.Surface2" Color="#212529" />
<SolidColorBrush x:Key="AeBrush.Surface3" Color="#282D32" />
<SolidColorBrush x:Key="AeBrush.SurfaceHover" Color="#30363C" />
<SolidColorBrush x:Key="AeBrush.Border" Color="#343A40" />
<SolidColorBrush x:Key="AeBrush.BorderStrong" Color="#464E56" />
<SolidColorBrush x:Key="AeBrush.TextPrimary" Color="#E4E7E9" />
<SolidColorBrush x:Key="AeBrush.TextSecondary" Color="#B0B6BC" />
<SolidColorBrush x:Key="AeBrush.TextMuted" Color="#858D95" />
<SolidColorBrush x:Key="AeBrush.Accent" Color="#64A9E2" />
<SolidColorBrush x:Key="AeBrush.AccentHover" Color="#75B5E8" />
<SolidColorBrush x:Key="AeBrush.AccentSoft" Color="#263A4B" />
<SolidColorBrush x:Key="AeBrush.Success" Color="#72BC91" />
<SolidColorBrush x:Key="AeBrush.Warning" Color="#E2B45F" />
<SolidColorBrush x:Key="AeBrush.Danger" Color="#E17979" />
```

- [ ] **Step 4: Add the reusable mapping block to the other dark dictionaries**

Append the following exact block to `ChromeDark.xaml`, `VSCodeDark.xaml`, `HighContrastDark.xaml`, `WarmDark.xaml`, and `CoolDark.xaml`:

```xml
<!-- AE UI design-system semantic brushes -->
<SolidColorBrush x:Key="AeBrush.Canvas" Color="{StaticResource AColour.Tone1.Background.Static}" />
<SolidColorBrush x:Key="AeBrush.Surface1" Color="{StaticResource AColour.Tone2.Background.Static}" />
<SolidColorBrush x:Key="AeBrush.Surface2" Color="{StaticResource AColour.Tone3.Background.Static}" />
<SolidColorBrush x:Key="AeBrush.Surface3" Color="{StaticResource AColour.Tone4.Background.Static}" />
<SolidColorBrush x:Key="AeBrush.SurfaceHover" Color="{StaticResource AColour.Tone4.Background.MouseOver}" />
<SolidColorBrush x:Key="AeBrush.Border" Color="{StaticResource AColour.Tone3.Border.Static}" />
<SolidColorBrush x:Key="AeBrush.BorderStrong" Color="{StaticResource AColour.Tone5.Border.Static}" />
<SolidColorBrush x:Key="AeBrush.TextPrimary" Color="{StaticResource AColour.Foreground.Static}" />
<SolidColorBrush x:Key="AeBrush.TextSecondary" Color="{StaticResource AColour.Foreground.Deeper}" />
<SolidColorBrush x:Key="AeBrush.TextMuted" Color="{StaticResource AColour.Foreground.Disabled}" />
<SolidColorBrush x:Key="AeBrush.Accent" Color="{StaticResource AColour.ColourfulGlyph.Static}" />
<SolidColorBrush x:Key="AeBrush.AccentHover" Color="{StaticResource AColour.ColourfulGlyph.MouseOver}" />
<SolidColorBrush x:Key="AeBrush.AccentSoft" Color="{StaticResource AColour.Tone4.Background.Selected}" />
<SolidColorBrush x:Key="AeBrush.Success" Color="#72BC91" />
<SolidColorBrush x:Key="AeBrush.Warning" Color="#E2B45F" />
<SolidColorBrush x:Key="AeBrush.Danger" Color="#E17979" />
```

- [ ] **Step 5: Add the reusable mapping block to the light dictionaries**

Append the following exact block to `LightTheme.xaml`, `ChromeLight.xaml`, `VSCodeLight.xaml`, and `HighContrastLight.xaml`:

```xml
<!-- AE UI design-system semantic brushes -->
<SolidColorBrush x:Key="AeBrush.Canvas" Color="{StaticResource AColour.Tone1.Background.Static}" />
<SolidColorBrush x:Key="AeBrush.Surface1" Color="{StaticResource AColour.Tone2.Background.Static}" />
<SolidColorBrush x:Key="AeBrush.Surface2" Color="{StaticResource AColour.Tone3.Background.Static}" />
<SolidColorBrush x:Key="AeBrush.Surface3" Color="{StaticResource AColour.Tone4.Background.Static}" />
<SolidColorBrush x:Key="AeBrush.SurfaceHover" Color="{StaticResource AColour.Tone4.Background.MouseOver}" />
<SolidColorBrush x:Key="AeBrush.Border" Color="{StaticResource AColour.Tone3.Border.Static}" />
<SolidColorBrush x:Key="AeBrush.BorderStrong" Color="{StaticResource AColour.Tone5.Border.Static}" />
<SolidColorBrush x:Key="AeBrush.TextPrimary" Color="{StaticResource AColour.Foreground.Static}" />
<SolidColorBrush x:Key="AeBrush.TextSecondary" Color="{StaticResource AColour.Foreground.Deeper}" />
<SolidColorBrush x:Key="AeBrush.TextMuted" Color="{StaticResource AColour.Foreground.Disabled}" />
<SolidColorBrush x:Key="AeBrush.Accent" Color="{StaticResource AColour.ColourfulGlyph.Static}" />
<SolidColorBrush x:Key="AeBrush.AccentHover" Color="{StaticResource AColour.ColourfulGlyph.MouseOver}" />
<SolidColorBrush x:Key="AeBrush.AccentSoft" Color="{StaticResource AColour.Tone4.Background.Selected}" />
<SolidColorBrush x:Key="AeBrush.Success" Color="#267A42" />
<SolidColorBrush x:Key="AeBrush.Warning" Color="#8A5A00" />
<SolidColorBrush x:Key="AeBrush.Danger" Color="#B23B3B" />
```

- [ ] **Step 6: Run the semantic brush tests**

Run the command from Step 2.

Expected: PASS for all ten theme dictionaries and the exact DarkTheme palette.

- [ ] **Step 7: Commit the semantic brush contract**

Run only when local commits are authorized for implementation:

```powershell
git add Testing/AssetEditorTests/UiDesignSystemResourceTests.cs AssetEditor/Themes/ColourDictionaries
git commit -m "style: add semantic UI theme brushes"
```

---

### Task 2: Add metric and motion resources

**Files:**

- Create: `AssetEditor/Themes/DesignSystem/DesignTokens.xaml`
- Modify: `Testing/AssetEditorTests/UiDesignSystemResourceTests.cs`

**Interfaces:**

- Consumes: no earlier implementation types.
- Produces: the `AeSpace.*`, `AeSize.*`, `AeRadius.*`, `AeMotion.*` resources listed in the interface contract.

- [ ] **Step 1: Add a failing design-token test**

Add the following test and helper to `UiDesignSystemResourceTests`:

```csharp
[Test]
public void DesignTokens_ExposeApprovedMetricsAndDurations()
{
    WpfTestApplicationHost.Invoke(_ =>
    {
        var dictionary = Load(
            "Themes/DesignSystem/DesignTokens.xaml");

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(dictionary["AeSpace.1"], Is.EqualTo(4d));
            NUnitAssert.That(dictionary["AeSpace.2"], Is.EqualTo(8d));
            NUnitAssert.That(dictionary["AeSpace.3"], Is.EqualTo(12d));
            NUnitAssert.That(dictionary["AeSpace.4"], Is.EqualTo(16d));
            NUnitAssert.That(dictionary["AeSpace.6"], Is.EqualTo(24d));
            NUnitAssert.That(dictionary["AeSpace.8"], Is.EqualTo(32d));
            NUnitAssert.That(
                dictionary["AeSize.ActivityRailWidth"],
                Is.EqualTo(34d));
            NUnitAssert.That(dictionary["AeSize.TabHeight"], Is.EqualTo(24d));
            NUnitAssert.That(
                dictionary["AeSize.CompactRowHeight"],
                Is.EqualTo(28d));
            NUnitAssert.That(
                dictionary["AeSize.ControlHeight"],
                Is.EqualTo(30d));
            NUnitAssert.That(
                dictionary["AeSize.ProminentControlHeight"],
                Is.EqualTo(34d));
            NUnitAssert.That(
                dictionary["AeRadius.Compact"],
                Is.EqualTo(new CornerRadius(3)));
            NUnitAssert.That(
                dictionary["AeRadius.Control"],
                Is.EqualTo(new CornerRadius(4)));
            NUnitAssert.That(
                dictionary["AeRadius.Surface"],
                Is.EqualTo(new CornerRadius(6)));
            NUnitAssert.That(
                dictionary["AeRadius.Overlay"],
                Is.EqualTo(new CornerRadius(7)));
            NUnitAssert.That(
                ((Duration)dictionary["AeMotion.Pressed"]).TimeSpan,
                Is.EqualTo(TimeSpan.FromMilliseconds(70)));
            NUnitAssert.That(
                ((Duration)dictionary["AeMotion.Hover"]).TimeSpan,
                Is.EqualTo(TimeSpan.FromMilliseconds(120)));
            NUnitAssert.That(
                ((Duration)dictionary["AeMotion.Selection"]).TimeSpan,
                Is.EqualTo(TimeSpan.FromMilliseconds(140)));
            NUnitAssert.That(
                ((Duration)dictionary["AeMotion.Overlay"]).TimeSpan,
                Is.EqualTo(TimeSpan.FromMilliseconds(160)));
            NUnitAssert.That(
                dictionary["AeMotion.OverlayOffset"],
                Is.EqualTo(2d));
        });
    });
}
```

- [ ] **Step 2: Run the focused test and confirm the dictionary is absent**

Run:

```powershell
dotnet test Testing/AssetEditorTests/AssetEditorTests.csproj --configuration Release --filter "FullyQualifiedName~UiDesignSystemResourceTests.DesignTokens" --verbosity normal
```

Expected: FAIL because `DesignTokens.xaml` does not exist.

- [ ] **Step 3: Create the exact design-token dictionary**

Create `DesignTokens.xaml`:

```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:sys="clr-namespace:System;assembly=netstandard">
    <sys:Double x:Key="AeSpace.1">4</sys:Double>
    <sys:Double x:Key="AeSpace.2">8</sys:Double>
    <sys:Double x:Key="AeSpace.3">12</sys:Double>
    <sys:Double x:Key="AeSpace.4">16</sys:Double>
    <sys:Double x:Key="AeSpace.6">24</sys:Double>
    <sys:Double x:Key="AeSpace.8">32</sys:Double>

    <sys:Double x:Key="AeSize.ActivityRailWidth">34</sys:Double>
    <sys:Double x:Key="AeSize.TabHeight">24</sys:Double>
    <sys:Double x:Key="AeSize.CompactRowHeight">28</sys:Double>
    <sys:Double x:Key="AeSize.ControlHeight">30</sys:Double>
    <sys:Double x:Key="AeSize.ProminentControlHeight">34</sys:Double>

    <CornerRadius x:Key="AeRadius.Compact">3</CornerRadius>
    <CornerRadius x:Key="AeRadius.Control">4</CornerRadius>
    <CornerRadius x:Key="AeRadius.Surface">6</CornerRadius>
    <CornerRadius x:Key="AeRadius.Overlay">7</CornerRadius>

    <Duration x:Key="AeMotion.Pressed">0:0:0.070</Duration>
    <Duration x:Key="AeMotion.Hover">0:0:0.120</Duration>
    <Duration x:Key="AeMotion.Selection">0:0:0.140</Duration>
    <Duration x:Key="AeMotion.Overlay">0:0:0.160</Duration>
    <sys:Double x:Key="AeMotion.OverlayOffset">2</sys:Double>
</ResourceDictionary>
```

- [ ] **Step 4: Run the design-token test**

Run the command from Step 2.

Expected: PASS with all approved numeric values and durations.

- [ ] **Step 5: Commit the design tokens**

Run only when local commits are authorized for implementation:

```powershell
git add AssetEditor/Themes/DesignSystem/DesignTokens.xaml Testing/AssetEditorTests/UiDesignSystemResourceTests.cs
git commit -m "style: add UI design tokens"
```

---

### Task 3: Add typography roles and the approved default font

**Files:**

- Create: `AssetEditor/Themes/DesignSystem/Typography.xaml`
- Modify: `AssetEditor/Themes/ControlColours.xaml:295`
- Modify: `Testing/AssetEditorTests/UiDesignSystemResourceTests.cs`
- Modify: `Testing/AssetEditorTests/SettingsViewModelTests.cs:243-290`

**Interfaces:**

- Consumes: `AeBrush.TextPrimary`, `AeBrush.TextSecondary`, `AeBrush.TextMuted`, `AppFontFamily`, and `AppFontWeight`.
- Produces: the six `AeText.*` keyed TextBlock styles.

- [ ] **Step 1: Add failing typography contract tests**

Add this test to `UiDesignSystemResourceTests`:

```csharp
[Test]
public void Typography_ExposesApprovedTextRoles()
{
    WpfTestApplicationHost.Invoke(_ =>
    {
        var dictionary = Load(
            "Themes/DesignSystem/Typography.xaml");
        var expected = new Dictionary<string, double>
        {
            ["AeText.PageTitle"] = 20,
            ["AeText.SectionTitle"] = 13,
            ["AeText.Body"] = 12,
            ["AeText.Label"] = 11,
            ["AeText.Caption"] = 11,
            ["AeText.Technical"] = 11,
        };

        NUnitAssert.Multiple(() =>
        {
            foreach (var pair in expected)
            {
                var style = (Style)dictionary[pair.Key];
                NUnitAssert.That(
                    style.TargetType,
                    Is.EqualTo(typeof(TextBlock)),
                    pair.Key);
                NUnitAssert.That(
                    style.Setters.OfType<Setter>()
                        .Single(setter =>
                            setter.Property == TextBlock.FontSizeProperty)
                        .Value,
                    Is.EqualTo(pair.Value),
                    pair.Key);
            }
        });
    });
}
```

In `SettingsViewModelTests.EmbeddedFontFamiliesAndWeights_ResolveToRealTypefaces`, add:

```csharp
NUnitAssert.That(
    defaultFamily.Source,
    Does.Contain("Segoe UI"));
```

Change the failure message from “default embedded application font” to “default Windows UI application font”.

- [ ] **Step 2: Run typography and font tests and confirm failure**

Run:

```powershell
dotnet test Testing/AssetEditorTests/AssetEditorTests.csproj --configuration Release --filter "FullyQualifiedName~UiDesignSystemResourceTests.Typography|FullyQualifiedName~SettingsViewModelTests.EmbeddedFontFamiliesAndWeights" --verbosity normal
```

Expected: FAIL because `Typography.xaml` does not exist and the current default is Alibaba PuHuiTi.

- [ ] **Step 3: Create the typography dictionary**

Create `Typography.xaml`:

```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Style x:Key="AeText.PageTitle" TargetType="{x:Type TextBlock}">
        <Setter Property="FontFamily" Value="{DynamicResource AppFontFamily}" />
        <Setter Property="FontSize" Value="20" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Foreground" Value="{DynamicResource AeBrush.TextPrimary}" />
        <Setter Property="TextTrimming" Value="CharacterEllipsis" />
    </Style>
    <Style x:Key="AeText.SectionTitle" TargetType="{x:Type TextBlock}">
        <Setter Property="FontFamily" Value="{DynamicResource AppFontFamily}" />
        <Setter Property="FontSize" Value="13" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Foreground" Value="{DynamicResource AeBrush.TextPrimary}" />
    </Style>
    <Style x:Key="AeText.Body" TargetType="{x:Type TextBlock}">
        <Setter Property="FontFamily" Value="{DynamicResource AppFontFamily}" />
        <Setter Property="FontSize" Value="12" />
        <Setter Property="FontWeight" Value="Normal" />
        <Setter Property="Foreground" Value="{DynamicResource AeBrush.TextPrimary}" />
    </Style>
    <Style x:Key="AeText.Label" TargetType="{x:Type TextBlock}">
        <Setter Property="FontFamily" Value="{DynamicResource AppFontFamily}" />
        <Setter Property="FontSize" Value="11" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Foreground" Value="{DynamicResource AeBrush.TextSecondary}" />
    </Style>
    <Style x:Key="AeText.Caption" TargetType="{x:Type TextBlock}">
        <Setter Property="FontFamily" Value="{DynamicResource AppFontFamily}" />
        <Setter Property="FontSize" Value="11" />
        <Setter Property="FontWeight" Value="Normal" />
        <Setter Property="Foreground" Value="{DynamicResource AeBrush.TextMuted}" />
    </Style>
    <Style x:Key="AeText.Technical" TargetType="{x:Type TextBlock}">
        <Setter Property="FontFamily" Value="Cascadia Mono, Consolas" />
        <Setter Property="FontSize" Value="11" />
        <Setter Property="FontWeight" Value="Normal" />
        <Setter Property="Foreground" Value="{DynamicResource AeBrush.TextSecondary}" />
    </Style>
</ResourceDictionary>
```

- [ ] **Step 4: Change the default application font**

In `ControlColours.xaml`, replace the existing `AppFontFamily` value with:

```xml
<FontFamily x:Key="AppFontFamily">Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI</FontFamily>
```

Keep `AppFontWeight` as `Normal`. Do not change `FontSettingsHelper.GetFontFamily` for Alibaba PuHuiTi or HarmonyOS.

- [ ] **Step 5: Run typography and font tests**

Run the command from Step 2.

Expected: PASS; the default resolves to an installed Windows UI typeface and both embedded custom families still resolve every advertised weight.

- [ ] **Step 6: Run the complete settings font transaction tests**

Run:

```powershell
dotnet test Testing/AssetEditorTests/AssetEditorTests.csproj --configuration Release --filter "FullyQualifiedName~SettingsViewModelTests" --verbosity normal
```

Expected: PASS, including preview, cancel restore, save, and global Window font propagation.

- [ ] **Step 7: Commit typography resources**

Run only when local commits are authorized for implementation:

```powershell
git add AssetEditor/Themes/DesignSystem/Typography.xaml AssetEditor/Themes/ControlColours.xaml Testing/AssetEditorTests/UiDesignSystemResourceTests.cs Testing/AssetEditorTests/SettingsViewModelTests.cs
git commit -m "style: add UI typography roles"
```

---

### Task 4: Add keyed surface styles

**Files:**

- Create: `AssetEditor/Themes/DesignSystem/SurfaceStyles.xaml`
- Modify: `Testing/AssetEditorTests/UiDesignSystemResourceTests.cs`

**Interfaces:**

- Consumes: `AeBrush.Canvas`, `AeBrush.Surface1`, `AeBrush.Surface2`, `AeBrush.Border`, `AeBrush.BorderStrong`, `AeRadius.Surface`, and `AeRadius.Overlay`.
- Produces: four keyed Border styles used by common-control and shell plans.

- [ ] **Step 1: Add a failing surface-style test**

Add:

```csharp
[Test]
public void SurfaceStyles_AreKeyedAndDoNotReplaceImplicitBorderStyle()
{
    WpfTestApplicationHost.Invoke(_ =>
    {
        var tokens = Load("Themes/DesignSystem/DesignTokens.xaml");
        Application.Current.Resources.MergedDictionaries.Add(tokens);

        try
        {
            var dictionary = Load(
                "Themes/DesignSystem/SurfaceStyles.xaml");
            var keys = new[]
            {
                "AeSurface.Canvas",
                "AeSurface.Panel",
                "AeSurface.Control",
                "AeSurface.Overlay",
            };

            NUnitAssert.Multiple(() =>
            {
                foreach (var key in keys)
                {
                    var style = (Style)dictionary[key];
                    NUnitAssert.That(
                        style.TargetType,
                        Is.EqualTo(typeof(Border)));
                }

                NUnitAssert.That(
                    dictionary.Contains(typeof(Border)),
                    Is.False,
                    "Foundation styles must not replace the implicit Border style.");
            });
        }
        finally
        {
            Application.Current.Resources.MergedDictionaries.Remove(tokens);
        }
    });
}
```

- [ ] **Step 2: Run the focused test and confirm the dictionary is absent**

Run:

```powershell
dotnet test Testing/AssetEditorTests/AssetEditorTests.csproj --configuration Release --filter "FullyQualifiedName~UiDesignSystemResourceTests.SurfaceStyles" --verbosity normal
```

Expected: FAIL because `SurfaceStyles.xaml` does not exist.

- [ ] **Step 3: Create exact keyed surface styles**

Create:

```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Style x:Key="AeSurface.Canvas" TargetType="{x:Type Border}">
        <Setter Property="Background" Value="{DynamicResource AeBrush.Canvas}" />
        <Setter Property="BorderThickness" Value="0" />
    </Style>
    <Style x:Key="AeSurface.Panel" TargetType="{x:Type Border}">
        <Setter Property="Background" Value="{DynamicResource AeBrush.Surface1}" />
        <Setter Property="BorderBrush" Value="{DynamicResource AeBrush.Border}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="{StaticResource AeRadius.Surface}" />
    </Style>
    <Style x:Key="AeSurface.Control" TargetType="{x:Type Border}">
        <Setter Property="Background" Value="{DynamicResource AeBrush.Surface2}" />
        <Setter Property="BorderBrush" Value="{DynamicResource AeBrush.BorderStrong}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="{StaticResource AeRadius.Control}" />
    </Style>
    <Style x:Key="AeSurface.Overlay" TargetType="{x:Type Border}">
        <Setter Property="Background" Value="{DynamicResource AeBrush.Surface2}" />
        <Setter Property="BorderBrush" Value="{DynamicResource AeBrush.BorderStrong}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="{StaticResource AeRadius.Overlay}" />
    </Style>
</ResourceDictionary>
```

- [ ] **Step 4: Run the surface-style test**

Run the command from Step 2.

Expected: PASS; all styles are keyed and there is no implicit Border style.

- [ ] **Step 5: Commit surface styles**

Run only when local commits are authorized for implementation:

```powershell
git add AssetEditor/Themes/DesignSystem/SurfaceStyles.xaml Testing/AssetEditorTests/UiDesignSystemResourceTests.cs
git commit -m "style: add keyed UI surface styles"
```

---

### Task 5: Wire the design system into the app and WPF test host

**Files:**

- Modify: `AssetEditor/App.xaml:11-22`
- Modify: `Shared/SharedCore/Settings/ThemesController.cs:87-105`
- Modify: `Testing/AssetEditorTests/WpfTestApplicationHost.cs:62-75`
- Modify: `Testing/AssetEditorTests/UiDesignSystemResourceTests.cs`

**Interfaces:**

- Consumes: the three dictionaries created by Tasks 2–4.
- Produces: identical application/test resource order, assembly-independent theme resource URIs, and runtime theme-switch behavior for future plans.

- [ ] **Step 1: Add a failing merged-resource and theme-switch test**

Add:

```csharp
[Test]
public void ApplicationResources_LoadDesignSystemInRequiredOrder()
{
    WpfTestApplicationHost.InvokeWithThemeResources(
        WpfTestApplicationHost.EmptyServices,
        () =>
        {
            var sources = Application.Current.Resources.MergedDictionaries
                .Select(dictionary => dictionary.Source?.OriginalString)
                .Where(source => source != null)
                .ToList();
            var expected = new[]
            {
                "Themes/ColourDictionaries/DarkTheme.xaml",
                "Themes/ControlColours.xaml",
                "Themes/DesignSystem/DesignTokens.xaml",
                "Themes/DesignSystem/Typography.xaml",
                "Themes/DesignSystem/SurfaceStyles.xaml",
                "Themes/Controls.xaml",
            };

            NUnitAssert.Multiple(() =>
            {
                for (var index = 0; index < expected.Length; index++)
                {
                    NUnitAssert.That(
                        sources[index],
                        Does.EndWith(expected[index]),
                        $"Merged dictionary position {index}.");
                }
            });
        });
}

[Test]
public void ThemeSwitch_UpdatesSemanticBrushConsumers()
{
    WpfTestApplicationHost.InvokeWithThemeResources(
        WpfTestApplicationHost.EmptyServices,
        () =>
        {
            var previousTheme = ThemesController.CurrentTheme;

            try
            {
                ThemesController.SetTheme(ThemeType.DarkTheme);
                var border = new Border
                {
                    Style = (Style)Application.Current.FindResource(
                        "AeSurface.Panel"),
                };
                var dark = ((SolidColorBrush)border.Background).Color;

                ThemesController.SetTheme(ThemeType.LightTheme);
                var light = ((SolidColorBrush)border.Background).Color;

                NUnitAssert.That(light, Is.Not.EqualTo(dark));
            }
            finally
            {
                ThemesController.SetTheme(previousTheme);
            }
        });
}
```

Expose the existing nested empty provider to tests by changing `EmptyServiceProvider` from `private` to `internal`, or add this internal property to `WpfTestApplicationHost`:

```csharp
public static IServiceProvider EmptyServices { get; } =
    EmptyServiceProvider.Instance;
```

Use `WpfTestApplicationHost.EmptyServices` in the test rather than referencing the nested type directly.

- [ ] **Step 2: Run the focused tests and confirm resource lookup fails**

Run:

```powershell
dotnet test Testing/AssetEditorTests/AssetEditorTests.csproj --configuration Release --filter "FullyQualifiedName~UiDesignSystemResourceTests.ApplicationResources|FullyQualifiedName~UiDesignSystemResourceTests.ThemeSwitch" --verbosity normal
```

Expected: FAIL because the application test host has not merged the design-system dictionaries.

- [ ] **Step 3: Merge dictionaries into App.xaml**

After the existing `Themes/ControlColours.xaml` entry and before `Themes/Controls.xaml`, add in this exact order:

```xml
<ResourceDictionary Source="Themes/DesignSystem/DesignTokens.xaml" />
<ResourceDictionary Source="Themes/DesignSystem/Typography.xaml" />
<ResourceDictionary Source="Themes/DesignSystem/SurfaceStyles.xaml" />
```

Keep the existing theme before `ControlColours.xaml` and keep `Controls.xaml` after all five foundation dictionaries. This preserves the legacy relative order while ensuring every control dictionary loads after the variables it may consume.

- [ ] **Step 4: Merge the same dictionaries in WpfTestApplicationHost**

After the existing `Themes/ControlColours.xaml` dictionary and before `Themes/Controls.xaml` in `EnsureThemeResources`, add:

```csharp
Resources.MergedDictionaries.Add(CreateResourceDictionary(
    "Themes/DesignSystem/DesignTokens.xaml"));
Resources.MergedDictionaries.Add(CreateResourceDictionary(
    "Themes/DesignSystem/Typography.xaml"));
Resources.MergedDictionaries.Add(CreateResourceDictionary(
    "Themes/DesignSystem/SurfaceStyles.xaml"));
```

Add `EmptyServices` as described in Step 1 and update the test to use it.

- [ ] **Step 5: Make runtime theme resource URIs assembly-independent**

The focused theme-switch test runs inside `AssetEditorTests`, while production runs inside `AssetEditor.CN`. Relative `ResourceDictionary.Source` values therefore resolve against different calling assemblies. With the user's explicit approval, replace the two relative URIs created by `ThemesController.SetTheme` with these exact application-component Pack URIs:

```csharp
ThemeDictionary = new ResourceDictionary()
{
    Source = new Uri(
        $"pack://application:,,,/AssetEditor.CN;component/Themes/ColourDictionaries/{themeName}.xaml",
        UriKind.Absolute)
};
ControlColours = new ResourceDictionary()
{
    Source = new Uri(
        "pack://application:,,,/AssetEditor.CN;component/Themes/ControlColours.xaml",
        UriKind.Absolute)
};
```

Do not change the theme-selection logic, dictionary positions, font reapplication, external theme callback, or any other `ThemesController` behavior.

- [ ] **Step 6: Run the focused resource tests**

Run the command from Step 2.

Expected: PASS; a styled existing Border updates when `ThemesController` swaps the theme dictionary.

- [ ] **Step 7: Run all design-system and settings visual tests**

Run:

```powershell
dotnet test Testing/AssetEditorTests/AssetEditorTests.csproj --configuration Release --filter "FullyQualifiedName~UiDesignSystemResourceTests|FullyQualifiedName~SettingsViewModelTests|FullyQualifiedName~SettingsViewVisualTests" --verbosity normal
```

Expected: PASS with no resource-key, Dispatcher, font-preview, cancellation, or rendering failures.

- [ ] **Step 8: Commit resource wiring**

Run only when local commits are authorized for implementation:

```powershell
git add AssetEditor/App.xaml Shared/SharedCore/Settings/ThemesController.cs Testing/AssetEditorTests/WpfTestApplicationHost.cs Testing/AssetEditorTests/UiDesignSystemResourceTests.cs
git commit -m "style: load UI design-system resources"
```

---

### Task 6: Create the full UI migration ledger

**Files:**

- Create: `docs/superpowers/plans/ae-ui-migration-ledger.md`
- Test: `Testing/AssetEditorTests/UiDesignSystemResourceTests.cs`

**Interfaces:**

- Consumes: every product `*.xaml` source file and every code-only WPF control under `AssetEditor`, `Shared`, `Editors`, and `GameWorld`.
- Produces: the initiative-wide coverage source used by every remaining plan.

- [ ] **Step 1: Add a failing coverage test before the ledger exists**

Add this exact test and helpers. It reads the ledger from the repository root, extracts every backtick-wrapped `*.xaml` path in the first column, compares it with the same four-root source-tree enumeration implemented through `Directory.EnumerateFiles`, and reports missing or extra paths:

```csharp
[Test]
public void MigrationLedger_CoversEveryProductXamlSource()
{
    var solutionRoot = FindSolutionRoot();
    var ledgerPath = Path.Combine(
        solutionRoot,
        "docs",
        "superpowers",
        "plans",
        "ae-ui-migration-ledger.md");
    var ledgerPaths = File.ReadLines(ledgerPath)
        .Select(line => line.Split('|'))
        .Where(cells => cells.Length > 2)
        .Select(cells => cells[1].Trim())
        .Where(value =>
            value.StartsWith('`') &&
            value.EndsWith(".xaml`", StringComparison.OrdinalIgnoreCase))
        .Select(value => value[1..^1].Replace('\\', '/'))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var sourcePaths = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase);
    foreach (var productRoot in new[]
             {
                 "AssetEditor",
                 "Shared",
                 "Editors",
                 "GameWorld",
             })
    {
        var absoluteRoot = Path.Combine(solutionRoot, productRoot);
        foreach (var path in Directory.EnumerateFiles(
                     absoluteRoot,
                     "*.xaml",
                     SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(solutionRoot, path);
            if (!ContainsBuildOutputDirectory(relativePath))
                sourcePaths.Add(relativePath.Replace('\\', '/'));
        }
    }

    var missing = sourcePaths.Except(ledgerPaths)
        .OrderBy(path => path)
        .ToArray();
    var extra = ledgerPaths.Except(sourcePaths)
        .OrderBy(path => path)
        .ToArray();

    NUnitAssert.Multiple(() =>
    {
        NUnitAssert.That(
            missing,
            Is.Empty,
            $"Ledger is missing:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
        NUnitAssert.That(
            extra,
            Is.Empty,
            $"Ledger has extra paths:{Environment.NewLine}{string.Join(Environment.NewLine, extra)}");
    });
}

private static bool ContainsBuildOutputDirectory(string path) =>
    path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries)
        .Any(part =>
            part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("obj", StringComparison.OrdinalIgnoreCase));

private static string FindSolutionRoot()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(
                directory.FullName,
                "AssetEditor.CN.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException(
        "Could not locate AssetEditor.CN.sln.");
}
```

The test must ignore `bin` and `obj` directories and normalize separators to `/` before comparison. The four explicit product roots exclude `.git`, `.vs`, and `Testing` by construction.

- [ ] **Step 2: Run the coverage test and confirm the ledger is absent**

Run:

```powershell
dotnet test Testing/AssetEditorTests/AssetEditorTests.csproj --configuration Release --filter "FullyQualifiedName~UiDesignSystemResourceTests.MigrationLedger" --verbosity normal
```

Expected: FAIL because `docs/superpowers/plans/ae-ui-migration-ledger.md` does not exist. The failure must come from the missing ledger, not from a build or test setup error.

- [ ] **Step 3: Generate the exact product XAML inventory**

Run:

```powershell
$productRoots = @('AssetEditor', 'Shared', 'Editors', 'GameWorld')
Get-ChildItem $productRoots -Recurse -Filter '*.xaml' -File |
    Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
    } |
    ForEach-Object {
        (Resolve-Path -Relative $_.FullName).Substring(2) -replace '\\', '/'
    } |
    Sort-Object
```

For each returned path, add one ledger row with these columns:

```markdown
| Path | Kind | User-visible entry | Family | Status | Automated evidence | User visual acceptance | Residual risk |
```

The `Path` cell must wrap the exact repository-relative path in backticks. Initial `Status` is `Unreviewed`. Determine `Kind` from the root XAML element. Do not omit resource dictionaries or XAML files instantiated only from C#.

- [ ] **Step 4: Identify code-only WPF controls**

Run this candidate scan:

```powershell
rg -n --glob '*.cs' ":\s*(Window|UserControl|Control|FrameworkElement|Panel|Decorator)\b" AssetEditor Shared Editors GameWorld
```

Cross-check every result against XAML code-behind files and runtime registration or construction sites. Add each genuinely code-only user-facing WPF type as a row whose `Path` cell uses `` `relative/path.cs#TypeName` `` and whose `Kind` is `Code-only WPF`. Record false positives in the task report; do not add them to the ledger.

- [ ] **Step 5: Run the ledger coverage test**

Run:

```powershell
dotnet test Testing/AssetEditorTests/AssetEditorTests.csproj --configuration Release --filter "FullyQualifiedName~UiDesignSystemResourceTests.MigrationLedger" --verbosity normal
```

Expected: PASS with zero missing and zero extra product XAML paths.

- [ ] **Step 6: Commit the temporary coverage ledger**

Run only when local commits are authorized for implementation:

```powershell
git add docs/superpowers/plans/ae-ui-migration-ledger.md Testing/AssetEditorTests/UiDesignSystemResourceTests.cs
git commit -m "test: track complete UI migration coverage"
```

---

### Task 7: Verify the foundation batch

**Files:**

- Verify only; do not add unrelated fixes.

- [ ] **Step 1: Restore the solution**

Run:

```powershell
dotnet restore AssetEditor.CN.sln
```

Expected: exit code 0.

- [ ] **Step 2: Build Release**

Run:

```powershell
dotnet build AssetEditor.CN.sln --configuration Release --no-restore
```

Expected: exit code 0 with zero errors.

- [ ] **Step 3: Run the complete Release test suite**

Run:

```powershell
dotnet test AssetEditor.CN.sln --configuration Release --no-build --no-restore --verbosity normal
```

Expected: exit code 0; report exact passed, failed and skipped totals rather than copying an older snapshot.

- [ ] **Step 4: Check the diff**

Run:

```powershell
git diff --check
git status --short --branch
```

Expected: no whitespace errors and only the foundation files listed in this plan.

- [ ] **Step 5: Hand off manual visual verification**

Ask the user to verify the Release application at 100%, 125%, and 150% scaling with:

- default Windows UI font;
- Alibaba PuHuiTi preview and cancel;
- HarmonyOS preview and cancel;
- DarkTheme, LightTheme, HighContrastDark, and HighContrastLight;
- MainWindow, SettingsWindow, one standard dialog, and one editor containing dense Chinese labels.

Acceptance requires no clipping, baseline drift, missing resource, unreadable text, or font state left behind after cancel. Do not claim visual success until the user confirms.

- [ ] **Step 6: Record the Phase 1 gate**

Update the roadmap status in the task report, not in long-term architecture docs. Generate the common-controls detailed plan only after the user confirms the manual checklist.
