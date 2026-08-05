using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Editors.CscEditor.Services;
using Editors.CscEditor.Views;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.Ui.Common;

namespace Test.CscEditor;

[TestFixture]
[NonParallelizable]
[Apartment(ApartmentState.STA)]
public class CscViewBindingTests
{
    private static readonly string[] EditableFloatPaths =
    [
        "Begin",
        "End",
        "PeriodSpeedMultiplier",
        "PeriodTimeOffset",
        "PositionX",
        "PositionY",
        "PositionZ",
        "RotationXDegrees",
        "RotationYDegrees",
        "RotationZDegrees",
        "ScaleValue",
        "WeightValue",
        "BasePositionX",
        "BasePositionY",
        "BasePositionZ",
        "BaseRotationXDegrees",
        "BaseRotationYDegrees",
        "BaseRotationZDegrees",
        "LightColourR",
        "LightColourG",
        "LightColourB",
        "LightIntensity",
        "LightRange",
        "SpotInnerAngleDegrees",
        "SpotOuterAngleDegrees",
        "CameraFov",
        "CameraRollDegrees",
        "CameraNear",
        "CameraFar",
        "SceneDuration",
        "FocusPointX",
        "FocusPointY",
        "FocusPointZ",
        "Radius",
    ];

    private static readonly string[] ReadOnlyFloatPaths =
    [
        "SubSceneDuration",
        "SubSceneFocusPointX",
        "SubSceneFocusPointY",
        "SubSceneFocusPointZ",
        "SubSceneRadius",
    ];

    [TestCase("6.25", "en-US")]
    [TestCase("6,25", "zh-CN")]
    [TestCase("6\uFF0E25", "zh-CN")]
    [TestCase("6\u300225", "zh-CN")]
    [TestCase("6.25", "fr-FR")]
    public void Editable_float_converter_accepts_common_decimal_separators(
        string input,
        string cultureName)
    {
        var view = new CscEditorView();
        var window = CreateWindow(view);

        try
        {
            window.Show();
            view.UpdateLayout();
            var binding = BindingOperations.GetBinding(
                FindTextBox(view, "SceneDuration"),
                TextBox.TextProperty);

            Assert.That(binding, Is.Not.Null);
            Assert.That(binding!.Converter, Is.Not.Null);

            var converted = binding.Converter!.ConvertBack(
                input,
                typeof(float),
                null!,
                CultureInfo.GetCultureInfo(cultureName));

            Assert.That(converted, Is.TypeOf<float>());
            Assert.That((float)converted, Is.EqualTo(6.25f));
        }
        finally
        {
            window.Close();
        }
    }

    [TestCase("NaN")]
    [TestCase("Infinity")]
    [TestCase("-Infinity")]
    public void Editable_float_converter_rejects_non_finite_values(
        string input)
    {
        var converter = new CscFloatConverter();

        var converted = converter.ConvertBack(
            input,
            typeof(float),
            null!,
            CultureInfo.InvariantCulture);

        Assert.That(
            converted,
            Is.SameAs(DependencyProperty.UnsetValue));
    }

    [Test]
    public void Scene_duration_binding_rejects_negative_values()
    {
        var view = new CscEditorView();
        var window = CreateWindow(view);

        try
        {
            window.Show();
            view.UpdateLayout();
            var binding = BindingOperations.GetBinding(
                FindTextBox(view, "SceneDuration"),
                TextBox.TextProperty);

            var converted = binding!.Converter!.ConvertBack(
                "-1",
                typeof(float),
                binding.ConverterParameter!,
                CultureInfo.InvariantCulture);

            Assert.That(
                converted,
                Is.SameAs(DependencyProperty.UnsetValue));
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Every_editable_float_binding_uses_converter_but_read_only_fields_do_not()
    {
        var view = new CscEditorView();
        var window = CreateWindow(view);

        try
        {
            window.Show();
            view.UpdateLayout();
            var bindings = Descendants<TextBox>(view)
                .Select(textBox => BindingOperations.GetBinding(
                    textBox,
                    TextBox.TextProperty))
                .Where(binding => binding?.Path?.Path != null)
                .ToDictionary(binding => binding!.Path.Path);

            var converter = bindings["SceneDuration"].Converter;
            Assert.That(converter, Is.Not.Null);

            Assert.Multiple(() =>
            {
                foreach (var path in EditableFloatPaths)
                    Assert.That(
                        bindings[path].Converter,
                        Is.SameAs(converter),
                        path);

                foreach (var path in ReadOnlyFloatPaths)
                    Assert.That(
                        bindings[path].Converter,
                        Is.Null,
                        path);
            });
        }
        finally
        {
            window.Close();
        }
    }

    [TestCase("6.25", "fr-FR")]
    [TestCase("6,25", "zh-CN")]
    [TestCase("6\uFF0E25", "zh-CN")]
    [TestCase("6\u300225", "zh-CN")]
    public void Save_after_button_takes_focus_commits_localized_scene_duration(
        string input,
        string language)
    {
        var view = new CscEditorView();
        var source = new EditorSource();
        view.DataContext = source;
        var window = CreateWindow(view);

        try
        {
            window.Show();
            view.UpdateLayout();
            var durationTextBox =
                FindTextBox(view, "SceneDuration");
            durationTextBox.Language =
                XmlLanguage.GetLanguage(language);
            Assert.That(
                Keyboard.Focus(durationTextBox),
                Is.SameAs(durationTextBox));
            ReplaceTextCharacterByCharacter(
                durationTextBox,
                input);

            var saveButton =
                view.FindName("SaveButton") as Button;
            Assert.That(saveButton, Is.Not.Null);
            Assert.That(saveButton!.Focus(), Is.True);
            saveButton.RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));

            Assert.Multiple(() =>
            {
                Assert.That(
                    Validation.GetHasError(durationTextBox),
                    Is.False);
                Assert.That(source.SavedDuration, Is.EqualTo(6.25f));
                Assert.That(source.SaveCount, Is.EqualTo(1));
                Assert.That(source.HasUnsavedChanges, Is.False);
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Save_after_invalid_field_loses_focus_keeps_validation_error_and_does_not_save()
    {
        var view = new CscEditorView();
        var source = new EditorSource();
        view.DataContext = source;
        var window = CreateWindow(view);

        try
        {
            window.Show();
            view.UpdateLayout();
            var durationTextBox =
                FindTextBox(view, "SceneDuration");
            Assert.That(
                Keyboard.Focus(durationTextBox),
                Is.SameAs(durationTextBox));
            ReplaceTextCharacterByCharacter(
                durationTextBox,
                "not-a-number");

            var saveButton =
                view.FindName("SaveButton") as Button;
            Assert.That(saveButton, Is.Not.Null);
            Assert.That(saveButton!.Focus(), Is.True);
            saveButton.RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));

            Assert.Multiple(() =>
            {
                Assert.That(
                    Validation.GetHasError(durationTextBox),
                    Is.True);
                Assert.That(source.SaveCount, Is.Zero);
                Assert.That(source.HasUnsavedChanges, Is.False);
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Scene_root_numeric_fields_preserve_decimal_typing_and_save_commits_focused_source()
    {
        var view = new CscEditorView();
        var source = new EditorSource();
        view.DataContext = source;
        var window = new Window
        {
            Content = view,
            Width = 1200,
            Height = 800,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
        };

        try
        {
            window.Show();
            view.UpdateLayout();
            var durationTextBox = Descendants<TextBox>(view)
                .Single(textBox =>
                    BindingOperations.GetBinding(
                        textBox,
                        TextBox.TextProperty)?.Path?.Path ==
                    "SceneDuration");
            Assert.That(
                Keyboard.Focus(durationTextBox),
                Is.SameAs(durationTextBox));

            durationTextBox.SelectAll();
            foreach (var character in "6.25")
            {
                durationTextBox.SelectedText =
                    character.ToString();
                durationTextBox.SelectionStart =
                    durationTextBox.Text.Length;
            }

            Assert.Multiple(() =>
            {
                Assert.That(
                    durationTextBox.Text,
                    Is.EqualTo("6.25"));
                Assert.That(
                    source.SelectedSceneRoot.SceneDuration,
                    Is.EqualTo(6f));
            });

            var saveButton =
                view.FindName("SaveButton") as Button;
            Assert.That(saveButton, Is.Not.Null);
            saveButton.RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));

            Assert.Multiple(() =>
            {
                Assert.That(
                    source.SavedDuration,
                    Is.EqualTo(6.25f));
                Assert.That(
                    source.HasUnsavedChanges,
                    Is.False);
            });

            var focusPointXTextBox =
                Descendants<TextBox>(view)
                    .Single(textBox =>
                        BindingOperations.GetBinding(
                            textBox,
                            TextBox.TextProperty)?.Path?.Path ==
                        "FocusPointX");
            Assert.That(
                Keyboard.Focus(focusPointXTextBox),
                Is.SameAs(focusPointXTextBox));
            focusPointXTextBox.SelectAll();
            foreach (var character in "2.5")
            {
                focusPointXTextBox.SelectedText =
                    character.ToString();
                focusPointXTextBox.SelectionStart =
                    focusPointXTextBox.Text.Length;
            }

            Assert.Multiple(() =>
            {
                Assert.That(
                    focusPointXTextBox.Text,
                    Is.EqualTo("2.5"));
                Assert.That(
                    source.SelectedSceneRoot.FocusPointX,
                    Is.EqualTo(1f));
            });

            saveButton.RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));

            Assert.Multiple(() =>
            {
                Assert.That(
                    source.SavedFocusPointX,
                    Is.EqualTo(2.5f));
                Assert.That(
                    source.HasUnsavedChanges,
                    Is.False);
            });

            focusPointXTextBox.SelectAll();
            focusPointXTextBox.SelectedText =
                "not-a-number";
            saveButton.RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));

            Assert.Multiple(() =>
            {
                Assert.That(
                    Validation.GetHasError(
                        focusPointXTextBox),
                    Is.True);
                Assert.That(source.SaveCount, Is.EqualTo(2));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Undo_button_commits_valid_focused_text_before_undo()
    {
        var view = new CscEditorView();
        var source = new EditorSource();
        view.DataContext = source;
        var window = CreateWindow(view);

        try
        {
            window.Show();
            view.UpdateLayout();
            var durationTextBox =
                FindTextBox(view, "SceneDuration");
            Keyboard.Focus(durationTextBox);
            ReplaceTextCharacterByCharacter(
                durationTextBox,
                "6.25");

            var undoButton =
                view.FindName("UndoButton") as Button;
            Assert.That(undoButton, Is.Not.Null);
            undoButton!.RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));

            Assert.Multiple(() =>
            {
                Assert.That(
                    source.DurationSeenByUndo,
                    Is.EqualTo(6.25f));
                Assert.That(source.UndoCount, Is.EqualTo(1));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Invalid_focused_text_blocks_undo_of_older_history()
    {
        var view = new CscEditorView();
        var source = new EditorSource();
        view.DataContext = source;
        var window = CreateWindow(view);

        try
        {
            window.Show();
            view.UpdateLayout();
            var durationTextBox =
                FindTextBox(view, "SceneDuration");
            Keyboard.Focus(durationTextBox);
            ReplaceTextCharacterByCharacter(
                durationTextBox,
                "not-a-number");

            var undoButton =
                view.FindName("UndoButton") as Button;
            Assert.That(undoButton, Is.Not.Null);
            undoButton!.RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));

            Assert.Multiple(() =>
            {
                Assert.That(
                    Validation.GetHasError(durationTextBox),
                    Is.True);
                Assert.That(source.UndoCount, Is.Zero);
            });
        }
        finally
        {
            window.Close();
        }
    }

    [TestCase(Key.Z, ModifierKeys.Control, CscHistoryShortcut.Undo)]
    [TestCase(Key.Y, ModifierKeys.Control, CscHistoryShortcut.Redo)]
    [TestCase(
        Key.Z,
        ModifierKeys.Control | ModifierKeys.Shift,
        CscHistoryShortcut.Redo)]
    [TestCase(Key.Z, ModifierKeys.None, CscHistoryShortcut.None)]
    public void Keyboard_shortcuts_map_to_expected_history_action(
        Key key,
        ModifierKeys modifiers,
        CscHistoryShortcut expected)
    {
        Assert.That(
            CscEditorView.GetHistoryShortcut(key, modifiers),
            Is.EqualTo(expected));
    }

    [OneTimeSetUp]
    public void CreateApplication()
    {
        var application = Application.Current ??
            new TestApplication(new LocalizationManager());
        application.ShutdownMode =
            ShutdownMode.OnExplicitShutdown;
        EnsureThemeResources(application);
    }

    private static void EnsureThemeResources(Application application)
    {
        if (application.Resources.Contains("AeButton.Primary"))
            return;

        foreach (var path in new[]
                 {
                     "Themes/ColourDictionaries/DarkTheme.xaml",
                     "Themes/ControlColours.xaml",
                     "Themes/DesignSystem/DesignTokens.xaml",
                     "Themes/DesignSystem/Typography.xaml",
                     "Themes/DesignSystem/SurfaceStyles.xaml",
                     "Themes/Controls.xaml",
                     "Themes/DesignSystem/Controls/Buttons.xaml",
                     "Themes/DesignSystem/Controls/Inputs.xaml",
                     "Themes/DesignSystem/Controls/Collections.xaml",
                     "Themes/DesignSystem/Controls/MenusAndFeedback.xaml",
                     "Themes/DesignSystem/Shell.xaml",
                     "Themes/DesignSystem/Workflows.xaml",
                 })
        {
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        $"pack://application:,,,/AssetEditor.CN;component/{path}"),
                });
        }
    }

    private static Window CreateWindow(CscEditorView view) =>
        new()
        {
            Content = view,
            Width = 1200,
            Height = 800,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
        };

    private static TextBox FindTextBox(
        CscEditorView view,
        string bindingPath) =>
        Descendants<TextBox>(view)
            .Single(textBox =>
                BindingOperations.GetBinding(
                    textBox,
                    TextBox.TextProperty)?.Path?.Path ==
                bindingPath);

    private static void ReplaceTextCharacterByCharacter(
        TextBox textBox,
        string value)
    {
        textBox.SelectAll();
        foreach (var character in value)
        {
            textBox.SelectedText = character.ToString();
            textBox.SelectionStart = textBox.Text.Length;
        }
    }

    private static IEnumerable<T> Descendants<T>(
        DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0;
             index < VisualTreeHelper.GetChildrenCount(parent);
             index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                yield return match;

            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    private sealed class TestApplication(
        LocalizationManager localization) :
        Application,
        IAssetEditorMain
    {
        public IServiceProvider ServiceProvider { get; } =
            new TestServiceProvider(localization);
    }

    private sealed class TestServiceProvider(
        LocalizationManager localization) :
        IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(LocalizationManager)
                ? localization
                : null;
    }

    public sealed class EditorSource :
        ISaveableEditor,
        ICscUndoRedoEditor
    {
        public DurationSource SelectedSceneRoot { get; }
        public bool HasUnsavedChanges { get; set; }
        public float? SavedDuration { get; private set; }
        public float? SavedFocusPointX { get; private set; }
        public int SaveCount { get; private set; }
        public int UndoCount { get; private set; }
        public float? DurationSeenByUndo { get; private set; }
        public bool IsSceneRootSelected => true;
        public bool CanUndo => true;
        public bool CanRedo => true;

        public EditorSource()
        {
            SelectedSceneRoot = new DurationSource(
                () => HasUnsavedChanges = true);
        }

        public bool Save()
        {
            SavedDuration =
                SelectedSceneRoot.SceneDuration;
            SavedFocusPointX =
                SelectedSceneRoot.FocusPointX;
            SaveCount++;
            HasUnsavedChanges = false;
            return true;
        }

        public bool Undo()
        {
            DurationSeenByUndo =
                SelectedSceneRoot.SceneDuration;
            UndoCount++;
            return true;
        }

        public bool Redo() => true;
    }

    public sealed class DurationSource(
        Action modified) :
        INotifyPropertyChanged
    {
        private float _sceneDuration = 6f;
        private float _focusPointX = 1f;

        public float SceneDuration
        {
            get => _sceneDuration;
            set
            {
                _sceneDuration = value;
                modified();
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(
                        nameof(SceneDuration)));
            }
        }

        public float FocusPointX
        {
            get => _focusPointX;
            set
            {
                _focusPointX = value;
                modified();
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(
                        nameof(FocusPointX)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
