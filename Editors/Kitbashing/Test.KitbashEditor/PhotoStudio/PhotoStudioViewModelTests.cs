using System.Reflection;
using System.Text.Json;
using System.Windows.Input;
using System.IO;
using Editors.KitbasherEditor;
using GameWorld.Core.Components.Rendering;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.Misc;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.MathViews;

namespace Test.KitbashEditor.PhotoStudio;

[TestFixture]
public class PhotoStudioViewModelTests
{
    private const string ViewModelTypeName =
        "Editors.KitbasherEditor.ChildEditors.PhotoStudio.PhotoStudioViewModel";

    [Test]
    public void PositionEdit_RecalculatesOrbitWithoutCameraPositionSetter()
    {
        var context = CreateViewModel();
        var desiredPosition = new Vector3(8, 4, 11);

        GetProperty<Vector3ViewModel>(
                context.ViewModel,
                "CameraPosition")
            .Set(desiredPosition);

        Assert.Multiple(() =>
        {
            Assert.That(
                Vector3.Distance(
                    context.Camera.Position,
                    desiredPosition),
                Is.LessThan(0.001f));
            Assert.That(
                GetProperty<float>(
                    context.ViewModel,
                    "CameraZoom"),
                Is.EqualTo(context.Camera.Zoom));
            Assert.That(
                GetProperty<float>(
                    context.ViewModel,
                    "CameraYaw"),
                Is.EqualTo(context.Camera.Yaw));
            Assert.That(
                GetProperty<float>(
                    context.ViewModel,
                    "CameraPitch"),
                Is.EqualTo(context.Camera.Pitch));
        });
    }

    [Test]
    public void TargetEdit_PreservesOrbitAndTranslatesCamera()
    {
        var context = CreateViewModel();
        var oldLookAt = context.Camera.LookAt;
        var oldPosition = context.Camera.Position;
        var oldYaw = context.Camera.Yaw;
        var oldPitch = context.Camera.Pitch;
        var oldZoom = context.Camera.Zoom;
        var newLookAt = new Vector3(3, -2, 5);

        GetProperty<Vector3ViewModel>(
                context.ViewModel,
                "CameraLookAt")
            .Set(newLookAt);

        Assert.Multiple(() =>
        {
            Assert.That(context.Camera.Yaw, Is.EqualTo(oldYaw));
            Assert.That(context.Camera.Pitch, Is.EqualTo(oldPitch));
            Assert.That(context.Camera.Zoom, Is.EqualTo(oldZoom));
            Assert.That(
                context.Camera.Position,
                Is.EqualTo(oldPosition + newLookAt - oldLookAt));
        });
    }

    [Test]
    public void ScalarAndLightEditsSynchronizeCameraAndRenderStore()
    {
        var context = CreateViewModel();

        SetProperty(context.ViewModel, "CameraYaw", -1.2f);
        SetProperty(context.ViewModel, "CameraPitch", 0.4f);
        SetProperty(context.ViewModel, "CameraZoom", 17.0f);
        SetProperty(context.ViewModel, "LightIntensity", 2.5f);
        SetProperty(context.ViewModel, "EnvLightRotationY", 35.0f);
        SetProperty(context.ViewModel, "DirectLightRotationX", 70.0f);
        SetProperty(context.ViewModel, "DirectLightRotationY", 125.0f);

        var displayedPosition =
            GetProperty<Vector3ViewModel>(
                    context.ViewModel,
                    "CameraPosition")
                .GetAsVector3();

        Assert.Multiple(() =>
        {
            Assert.That(context.Camera.Yaw, Is.EqualTo(-1.2f));
            Assert.That(context.Camera.Pitch, Is.EqualTo(0.4f));
            Assert.That(context.Camera.Zoom, Is.EqualTo(17.0f));
            Assert.That(
                Vector3.Distance(
                    displayedPosition,
                    context.Camera.Position),
                Is.LessThan(0.001f));
            Assert.That(
                context.RenderParameters.LightIntensityMult,
                Is.EqualTo(2.5f));
            Assert.That(
                context.RenderParameters.EnvLightRotationDegrees_Y,
                Is.EqualTo(35.0f));
            Assert.That(
                context.RenderParameters.DirLightRotationDegrees_X,
                Is.EqualTo(70.0f));
            Assert.That(
                context.RenderParameters.DirLightRotationDegrees_Y,
                Is.EqualTo(125.0f));
        });
    }

    [TestCase(false, 1.0f)]
    [TestCase(true, 2.0f)]
    public void ScreenshotCommand_UsesChineseCnOutputNameAndDirectory(
        bool doubleResolution,
        float expectedScale)
    {
        var context = CreateViewModel();
        SetProperty(
            context.ViewModel,
            "DoubleImageResolution",
            doubleResolution);

        var command = GetProperty<ICommand>(
            context.ViewModel,
            "TakeScreenshotCommand");
        command.Execute(null);

        var settings = context.CaptureRequest.Settings;
        Assert.That(settings, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(
                settings!.ImageUpScaleFactor,
                Is.EqualTo(expectedScale));
            Assert.That(
                settings.Name,
                Is.EqualTo("截图"));
            Assert.That(
                settings.OutputFolder,
                Is.EqualTo(
                    Path.Combine(
                        DirectoryHelper.ApplicationDirectory,
                        "照片工作室",
                        "截图")));
        });
    }

    [Test]
    public void SettingsSaveDialog_UsesChineseDefaultFileName()
    {
        var type = typeof(DependencyInjectionContainer).Assembly
            .GetType(ViewModelTypeName);
        Assert.That(type, Is.Not.Null);

        var method = type!.GetMethod(
            "CreateSettingsSaveDialog",
            BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);

        var dialog = method!.Invoke(null, null);
        Assert.That(dialog, Is.Not.Null);
        var fileName = dialog!.GetType()
            .GetProperty("FileName")
            ?.GetValue(dialog);

        Assert.That(fileName, Is.EqualTo("照片工作室设置.json"));
    }

    [Test]
    public void InvalidSettingsJson_DoesNotPartiallyChangeLiveState()
    {
        var context = CreateViewModel();
        var oldPosition = context.Camera.Position;
        var oldLookAt = context.Camera.LookAt;
        var oldLightIntensity =
            context.RenderParameters.LightIntensityMult;

        var method = context.ViewModel.GetType().GetMethod(
            "TryImportSettings",
            BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);

        var imported = (bool)method!.Invoke(
            context.ViewModel,
            ["{\"CameraPositionX\": 999, broken json"])!;

        Assert.Multiple(() =>
        {
            Assert.That(imported, Is.False);
            Assert.That(context.Camera.Position, Is.EqualTo(oldPosition));
            Assert.That(context.Camera.LookAt, Is.EqualTo(oldLookAt));
            Assert.That(
                context.RenderParameters.LightIntensityMult,
                Is.EqualTo(oldLightIntensity));
        });
        context.Dialogs.Verify(
            dialogs => dialogs.ShowExceptionWindow(
                It.IsAny<Exception>(),
                It.IsAny<string>()),
            Times.Once);
    }

    [Test]
    public void SettingsRoundTrip_PreservesDoubleImageResolution()
    {
        var context = CreateViewModel();
        SetProperty(
            context.ViewModel,
            "DoubleImageResolution",
            false);
        var createSettings = context.ViewModel.GetType().GetMethod(
            "CreateSettings",
            BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
        var importSettings = context.ViewModel.GetType().GetMethod(
            "TryImportSettings",
            BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
        Assert.That(createSettings, Is.Not.Null);
        Assert.That(importSettings, Is.Not.Null);

        var json = JsonSerializer.Serialize(
            createSettings!.Invoke(context.ViewModel, null));
        SetProperty(
            context.ViewModel,
            "DoubleImageResolution",
            true);
        var imported = (bool)importSettings!.Invoke(
            context.ViewModel,
            [json])!;

        Assert.Multiple(() =>
        {
            Assert.That(imported, Is.True);
            Assert.That(
                GetProperty<bool>(
                    context.ViewModel,
                    "DoubleImageResolution"),
                Is.False);
        });
    }

    [TestCase("CameraYaw")]
    [TestCase("CameraPitch")]
    [TestCase("CameraZoom")]
    [TestCase("LightIntensity")]
    [TestCase("EnvLightRotationY")]
    [TestCase("DirectLightRotationX")]
    [TestCase("DirectLightRotationY")]
    public void NonFiniteScalarEdit_IsRejectedAndDisplayIsRestored(
        string propertyName)
    {
        var context = CreateViewModel();
        var oldValue = GetProperty<float>(
            context.ViewModel,
            propertyName);

        SetProperty(
            context.ViewModel,
            propertyName,
            float.NaN);

        Assert.Multiple(() =>
        {
            Assert.That(
                GetProperty<float>(
                    context.ViewModel,
                    propertyName),
                Is.EqualTo(oldValue));
            Assert.That(float.IsFinite(context.Camera.Yaw), Is.True);
            Assert.That(float.IsFinite(context.Camera.Pitch), Is.True);
            Assert.That(float.IsFinite(context.Camera.Zoom), Is.True);
            Assert.That(
                float.IsFinite(
                    context.RenderParameters.LightIntensityMult),
                Is.True);
            Assert.That(
                float.IsFinite(
                    context.RenderParameters.EnvLightRotationDegrees_Y),
                Is.True);
            Assert.That(
                float.IsFinite(
                    context.RenderParameters.DirLightRotationDegrees_X),
                Is.True);
            Assert.That(
                float.IsFinite(
                    context.RenderParameters.DirLightRotationDegrees_Y),
                Is.True);
        });
    }

    [Test]
    public void InvalidVectorAndZoomEdits_AreRejectedAndDisplaysAreRestored()
    {
        var context = CreateViewModel();
        var oldPosition = context.Camera.Position;
        var oldLookAt = context.Camera.LookAt;
        var oldZoom = context.Camera.Zoom;

        var position = GetProperty<Vector3ViewModel>(
                context.ViewModel,
                "CameraPosition");
        position.X.Value = double.NaN;
        var lookAt = GetProperty<Vector3ViewModel>(
                context.ViewModel,
                "CameraLookAt");
        lookAt.Y.Value = double.NaN;
        SetProperty(
            context.ViewModel,
            "CameraZoom",
            ArcBallCamera.MinZoom / 2);

        Assert.Multiple(() =>
        {
            Assert.That(
                Vector3.Distance(
                    context.Camera.Position,
                    oldPosition),
                Is.LessThan(0.0001f));
            Assert.That(context.Camera.LookAt, Is.EqualTo(oldLookAt));
            Assert.That(context.Camera.Zoom, Is.EqualTo(oldZoom));
            Assert.That(
                Vector3.Distance(
                    GetProperty<Vector3ViewModel>(
                            context.ViewModel,
                            "CameraPosition")
                        .GetAsVector3(),
                    oldPosition),
                Is.LessThan(0.0001f));
            Assert.That(
                GetProperty<Vector3ViewModel>(
                        context.ViewModel,
                        "CameraLookAt")
                    .GetAsVector3(),
                Is.EqualTo(oldLookAt));
            Assert.That(
                GetProperty<float>(
                    context.ViewModel,
                    "CameraZoom"),
                Is.EqualTo(oldZoom));
        });
    }

    [Test]
    public void ScreenshotFailure_IsShownToUser()
    {
        var context = CreateViewModel();
        var command = GetProperty<ICommand>(
            context.ViewModel,
            "TakeScreenshotCommand");
        command.Execute(null);
        var settings = context.CaptureRequest.Settings;
        Assert.That(settings, Is.Not.Null);
        Assert.That(settings!.FailureHandler, Is.Not.Null);
        var exception = new InvalidOperationException(
            "Simulated capture failure");

        settings.FailureHandler!.Invoke(exception);

        context.Dialogs.Verify(
            dialogs => dialogs.ShowExceptionWindow(
                exception,
                It.Is<string>(message =>
                    message.Contains("照片工作室"))),
            Times.Once);
    }

    private static ViewModelContext CreateViewModel()
    {
        var assembly = typeof(DependencyInjectionContainer).Assembly;
        var type = assembly.GetType(ViewModelTypeName);
        Assert.That(
            type,
            Is.Not.Null,
            $"Missing production type {ViewModelTypeName}");

        var camera = new ArcBallCamera(null!, null!, null!)
        {
            LookAt = new Vector3(1, 2, 3),
            Yaw = 0.8f,
            Pitch = 0.32f,
            Zoom = 10
        };
        var renderParameters = new SceneRenderParametersStore();
        var dialogs = new Mock<IStandardDialogs>();
        var captureRequest = new CaptureRequest();
        Action<SaveRenderImageSettings> requestCapture =
            settings => captureRequest.Settings = settings;

        var constructor = type!.GetConstructor(
            BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic,
            binder: null,
            [
                typeof(Action<SaveRenderImageSettings>),
                typeof(SceneRenderParametersStore),
                typeof(ArcBallCamera),
                typeof(IStandardDialogs)
            ],
            modifiers: null);
        Assert.That(
            constructor,
            Is.Not.Null,
            "The test seam constructor was not found.");

        var viewModel = constructor!.Invoke(
            [
                requestCapture,
                renderParameters,
                camera,
                dialogs.Object
            ]);

        return new ViewModelContext(
            viewModel,
            camera,
            renderParameters,
            dialogs,
            captureRequest);
    }

    private static T GetProperty<T>(
        object instance,
        string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName);
        Assert.That(property, Is.Not.Null);
        return (T)property!.GetValue(instance)!;
    }

    private static void SetProperty<T>(
        object instance,
        string propertyName,
        T value)
    {
        var property = instance.GetType().GetProperty(propertyName);
        Assert.That(property, Is.Not.Null);
        property!.SetValue(instance, value);
    }

    private sealed class CaptureRequest
    {
        public SaveRenderImageSettings? Settings { get; set; }
    }

    private sealed record ViewModelContext(
        object ViewModel,
        ArcBallCamera Camera,
        SceneRenderParametersStore RenderParameters,
        Mock<IStandardDialogs> Dialogs,
        CaptureRequest CaptureRequest);
}
