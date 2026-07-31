using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Editors.CscEditor.Data;
using Editors.CscEditor.ViewModels;
using Shared.Core.Services;

namespace Test.CscEditor;

[TestFixture]
public class CscPresentationLocalizationTests
{
    [Test]
    public void DisplayName_LocalizesFallbackWithoutChangingGroupLabel()
    {
        var element = new CscElement
        {
            Id = 12,
            Kind = CscElementKind.PointLight,
            GroupLabel = "point_light"
        };
        var viewModel = new CscElementViewModel(
            element,
            (_, _) => { },
            CreateLocalization());

        Assert.Multiple(() =>
        {
            Assert.That(
                viewModel.DisplayName,
                Is.EqualTo("[12] 点光源"));
            Assert.That(
                element.GroupLabel,
                Is.EqualTo("point_light"));
        });
    }

    [Test]
    public void DisplayName_PreservesCustomGroupLabel()
    {
        var element = new CscElement
        {
            Id = 12,
            Kind = CscElementKind.PointLight,
            GroupLabel = "custom_marker"
        };
        var viewModel = new CscElementViewModel(
            element,
            (_, _) => { },
            CreateLocalization());

        Assert.That(
            viewModel.DisplayName,
            Is.EqualTo("[12] custom_marker"));
    }

    [Test]
    public void SpliceDisplay_LocalizesBooleansWithoutChangingRawValue()
    {
        var element = new CscElement
        {
            SpliceRemainingFieldsDisplay =
                "Bool=True, Bool=False, U8=255"
        };
        var viewModel = new CscElementViewModel(
            element,
            (_, _) => { },
            CreateLocalization());

        Assert.Multiple(() =>
        {
            Assert.That(
                viewModel.SpliceRemainingFieldsDisplay,
                Is.EqualTo(
                    "布尔值=是，布尔值=否，U8=255"));
            Assert.That(
                element.SpliceRemainingFieldsDisplay,
                Is.EqualTo(
                    "Bool=True, Bool=False, U8=255"));
        });
    }

    [Test]
    public void Undo_redo_toolbar_uses_existing_icons_and_chinese_localization_keys()
    {
        var projectDirectory =
            Directory.GetParent(
                Path.GetDirectoryName(GetSourcePath())!)!;
        var viewPath = Path.Combine(
            projectDirectory.FullName,
            "Editors.CscEditor",
            "Views",
            "CscEditorView.xaml");
        var repositoryRoot =
            projectDirectory.Parent!.Parent!;
        var languagePath = Path.Combine(
            repositoryRoot.FullName,
            "AssetEditor",
            "Language_Cn.json");

        var view = File.ReadAllText(viewPath);
        using var language = JsonDocument.Parse(
            File.ReadAllText(languagePath));

        Assert.Multiple(() =>
        {
            Assert.That(
                view,
                Does.Contain(
                    "IconLibrary.UndoIcon"));
            Assert.That(
                view,
                Does.Contain(
                    "IconLibrary.RedoIcon"));
            Assert.That(
                language.RootElement
                    .GetProperty("Csc.ToolTip.Undo")
                    .GetString(),
                Is.EqualTo("撤销上一步编辑"));
            Assert.That(
                language.RootElement
                    .GetProperty("Csc.ToolTip.Redo")
                    .GetString(),
                Is.EqualTo("重做上一步编辑"));
        });
    }

    private static string GetSourcePath(
        [CallerFilePath] string path = "") =>
        path;

    private static LocalizationManager CreateLocalization()
    {
        var localization = new LocalizationManager();
        var stringsField = typeof(LocalizationManager).GetField(
            "_strings",
            BindingFlags.Instance |
                BindingFlags.NonPublic);
        Assert.That(stringsField, Is.Not.Null);
        stringsField!.SetValue(
            localization,
            new Dictionary<string, string>
            {
                ["Csc.Kind.PointLight"] = "点光源",
                ["Csc.Raw.Bool"] = "布尔值",
                ["Csc.Raw.True"] = "是",
                ["Csc.Raw.False"] = "否"
            });
        return localization;
    }
}
