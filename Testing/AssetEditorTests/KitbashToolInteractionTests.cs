using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommonControls.Editors.BoneMapping.View;
using Editors.KitbasherEditor.ChildEditors.MeshFitter;
using KitbasherEditor.Views;
using Microsoft.Xaml.Behaviors;
using NUnit.Framework;
using Shared.Core.Misc;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs.MathViews;
using Shared.Ui.Common.Behaviors;
using Shared.Ui.Editors.BoneMapping;
using static AssetEditorTests.PlaybackAndViewportRefinementTests;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class KitbashToolInteractionTests
{
    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void PhotoStudio_NumericFieldsRenderWithExistingBindings(ThemeType theme)
    {
        WithTheme(theme, () =>
        {
            var window = new Editors.KitbasherEditor.ChildEditors.PhotoStudio.PhotoStudioWindow(null!);
            try
            {
                var content = (FrameworkElement)window.Content;
                window.Content = null;
                content.Resources.MergedDictionaries.Add(window.Resources);
                content.DataContext = new PhotoProbe
                {
                    CameraPosition = new Vector3ViewModel(0, 1, 4), CameraLookAt = new Vector3ViewModel(0, 1, 0),
                    CameraYawDegrees = 20d, CameraPitchDegrees = -24d, CameraZoom = 4d,
                    LightIntensity = 1d, EnvLightRotationY = 0d, DirectLightRotationX = 20d, DirectLightRotationY = 35d,
                    DoubleImageResolution = true
                };
                Layout(content, 560);
                Assert.That(Descendants<TextBox>(content).Count(x => Interaction.GetBehaviors(x).OfType<TextBoxInputBehavior>().Any(b => b.IsDragEnabled)), Is.EqualTo(13));
                Render(content, $"photo-studio-numeric-{theme}", 1);
            }
            finally { window.Close(); }
        });
    }

    [TestCaseSource(typeof(PlaybackAndViewportRefinementTests), nameof(ThemeScales))]
    public void ApprovedLayouts_RenderAllControlsOffscreen(ThemeType theme, double scale)
    {
        WithTheme(theme, () =>
        {
            var model = new ToolProbe();
            var dummy = new MeshFitterViewModel(null!, null!, null!);
            typeof(MeshFitterViewModel).GetField("_isDisposed", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(dummy, true);
            foreach (var fitter in new[] { true, false })
            {
                Window window = fitter ? new MeshFitterWindow(dummy) : new ReRiggingWindow(null!);
                try
                {
                    var content = (FrameworkElement)window.Content;
                    window.Content = null;
                    content.Resources.MergedDictionaries.Add(window.Resources);
                    content.DataContext = model;
                    var host = new Border { Background = (Brush)Application.Current.FindResource("AeBrush.Canvas"), Child = content };
                    VisualTreeHelper.SetRootDpi(host, new DpiScale(scale, scale));
                    foreach (var size in new[] { new Size(fitter ? 1160 : 1040, 650), new Size(fitter ? 1000 : 800, 500) })
                    {
                        Layout(host, size.Width, size.Height);
                        Assert.That(Descendants<TreeView>(host).Count(), Is.EqualTo(2));
                        var buttons = Descendants<Button>(host).Where(x => x.Visibility == Visibility.Visible && x.Content is string).ToArray();
                        Assert.That(buttons.Length, Is.EqualTo(fitter ? 11 : 9));
                        foreach (var button in buttons)
                        {
                            Assert.That(button.ActualWidth, Is.GreaterThan(20), button.Content.ToString());
                            var point = button.TranslatePoint(new Point(), host);
                            Assert.That(point.X + button.ActualWidth, Is.LessThanOrEqualTo(size.Width + 1));
                            Assert.That(point.Y + button.ActualHeight, Is.LessThanOrEqualTo(size.Height + 1));
                        }
                        if (fitter)
                        {
                            var numericFields = Descendants<TextBox>(host).Where(x => Interaction.GetBehaviors(x).OfType<TextBoxInputBehavior>().Any(b => b.IsDragEnabled)).ToArray();
                            Assert.That(numericFields.Length, Is.EqualTo(11));
                            Assert.That(numericFields.All(x => x.ActualWidth >= 45), Is.True);
                            Assert.That(numericFields.All(x => x.HorizontalContentAlignment == HorizontalAlignment.Left), Is.True);
                        }
                        Render(host, $"{(fitter ? "mesh-fitter-A" : "rerigging-final")}-{theme}-{scale:0.00}-{size.Width}", scale);
                    }
                }
                finally { window.Close(); }
            }
            // The same mapping view is also hosted by the animation retargeting editor.
            var shared = new BoneMappingView { DataContext = model };
            Layout(shared, 760, 390);
            Assert.That(Descendants<TreeView>(shared).All(x => x.ActualHeight > 180), Is.True);
            Render(shared, $"shared-mapping-{theme}-{scale:0.00}", scale);
        });
    }

    [TestCaseSource(typeof(PlaybackAndViewportRefinementTests), nameof(ThemeScales))]
    public void TransformPanel_RendersRoundedThemeSurfaceAndBoundNumericFields(ThemeType theme, double scale)
    {
        WithTheme(theme, () =>
        {
            var view = new KitbasherView
            {
                DataContext = new WorkspaceProbe
                {
                    LeftColumnWidth = new GridLength(1, GridUnitType.Star), RightColumnWidth = new GridLength(400),
                    Scene = new Border { Background = Brushes.DarkSlateGray }, Animation = new PlayerProbe(),
                    MenuBar = new
                    {
                        MenuItems = Array.Empty<object>(), CustomButtons = Array.Empty<object>(), SidebarButtons = Array.Empty<object>(),
                        TransformTool = new
                        {
                            IsVisible = true, CanEditValues = true, ButtonEnabled = true, DragStep = 0.25,
                            Text = "旋转", Description = "本次旋转", CoordinateDescription = "本次变换量 · 世界坐标",
                            Vector3 = new Vector3ViewModel(12, 37, -41), ShowVertexFalloff = new NotifyAttr<bool>(false)
                        }
                    }
                }
            };
            VisualTreeHelper.SetRootDpi(view, new DpiScale(scale, scale));
            Layout(view, 1160, 680);
            var panel = (Border)view.FindName("TransformPanel");
            Assert.That(panel.CornerRadius.TopLeft, Is.GreaterThan(0));
            var fields = Descendants<TextBox>((StackPanel)view.FindName("TransformFields")).ToArray();
            Assert.That(fields.Select(x => x.Text), Is.EqualTo(new[] { "12", "37", "-41" }));
            Assert.That(fields.SelectMany(x => Interaction.GetBehaviors(x).OfType<TextBoxInputBehavior>()).All(x => x.IsDragEnabled && x.DragStep == 0.25), Is.True);
            foreach (var control in fields.Cast<FrameworkElement>().Concat(Descendants<Button>(panel)))
            {
                var point = control.TranslatePoint(new Point(), panel);
                Assert.That(point.X, Is.GreaterThanOrEqualTo(0));
                Assert.That(point.X + control.ActualWidth, Is.LessThanOrEqualTo(panel.ActualWidth));
            }
            Render(view, $"transform-panel-{theme}-{scale:0.00}", scale);
        });
    }

    public class PhotoProbe
    {
        public Vector3ViewModel CameraPosition { get; set; } = new(0, 1, 4);
        public Vector3ViewModel CameraLookAt { get; set; } = new(0, 1, 0);
        public double CameraYawDegrees { get; set; }
        public double CameraPitchDegrees { get; set; }
        public double CameraZoom { get; set; }
        public double LightIntensity { get; set; }
        public double EnvLightRotationY { get; set; }
        public double DirectLightRotationX { get; set; }
        public double DirectLightRotationY { get; set; }
        public bool DoubleImageResolution { get; set; }
    }
    public class ToolProbe : BoneMappingViewModel
    {
        public NotifyAttr<bool> RelativeScale { get; } = new(false);
        public NotifyAttr<bool> IsBoneSelected { get; } = new(true);
        public DoubleViewModel ScaleFactor { get; } = new(1);
        public DoubleViewModel BoneScaleFactor { get; } = new(1);
        public Vector3ViewModel SkeletonDisplayOffset { get; } = new(0, 0, 0);
        public Vector3ViewModel BonePositionOffset { get; } = new(0, 0, 0);
        public Vector3ViewModel BoneRotationOffset { get; } = new(0, 0, 0);
        public bool HasValidTransform => true;
        public ToolProbe()
        {
            var source = Tree();
            Initialize(new RemappedAnimatedBoneConfiguration
            {
                MeshSkeletonName = "来源骨架 humanoid01", ParnetModelSkeletonName = "目标骨架 humanoid02",
                MeshBones = [source], ParentModelBones = [Tree()]
            });
            OnlyShowUsedBones.Value = false;
            ShowTransformSection.Value = true;
        }
        private static AnimatedBone Tree()
        {
            var root = new AnimatedBone(0, "root");
            root.IsUsedByCurrentModel.Value = true;
            root.Children.Add(new AnimatedBone(1, "bip_spine"));
            root.Children.Add(new AnimatedBone(2, "bip_head"));
            return root;
        }
    }
}
