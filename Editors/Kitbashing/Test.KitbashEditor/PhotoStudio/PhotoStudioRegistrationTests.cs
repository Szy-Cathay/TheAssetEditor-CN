using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Editors.KitbasherEditor;
using Microsoft.Extensions.DependencyInjection;
using Shared.EmbeddedResources;
using Shared.Ui.Common.MenuSystem;
using KitbashDependencyInjectionContainer =
    Editors.KitbasherEditor.DependencyInjectionContainer;

namespace Test.KitbashEditor.PhotoStudio;

[TestFixture]
public class PhotoStudioRegistrationTests
{
    private const string Namespace =
        "Editors.KitbasherEditor.ChildEditors.PhotoStudio";

    [Test]
    public void DependencyContainer_RegistersPhotoStudioExactlyOnce()
    {
        var assembly =
            typeof(KitbashDependencyInjectionContainer).Assembly;
        var commandType = GetRequiredType(
            assembly,
            $"{Namespace}.OpenPhotoStudioCommand");
        var viewModelType = GetRequiredType(
            assembly,
            $"{Namespace}.PhotoStudioViewModel");
        var windowType = GetRequiredType(
            assembly,
            $"{Namespace}.PhotoStudioWindow");
        var services = new ServiceCollection();

        new KitbashDependencyInjectionContainer().Register(services);

        Assert.Multiple(() =>
        {
            Assert.That(
                services.Count(descriptor =>
                    descriptor.ServiceType == commandType),
                Is.EqualTo(1));
            Assert.That(
                services.Count(descriptor =>
                    descriptor.ServiceType == viewModelType),
                Is.EqualTo(1));
            Assert.That(
                services.Count(descriptor =>
                    descriptor.ServiceType == windowType),
                Is.EqualTo(1));
        });
    }

    [Test]
    public void Command_UsesUniqueCtrlPShortcut()
    {
        var assembly =
            typeof(KitbashDependencyInjectionContainer).Assembly;
        var commandType = GetRequiredType(
            assembly,
            $"{Namespace}.OpenPhotoStudioCommand");
        var command = RuntimeHelpers.GetUninitializedObject(commandType);
        var hotkey = (Hotkey?)commandType
            .GetProperty("HotKey")!
            .GetValue(command);

        Assert.Multiple(() =>
        {
            Assert.That(hotkey, Is.Not.Null);
            Assert.That(hotkey?.Key, Is.EqualTo(Key.P));
            Assert.That(
                hotkey?.ModifierKeys,
                Is.EqualTo(ModifierKeys.Control));
        });
    }

    [Test]
    public void Command_IsDisposedWithItsDependencyInjectionScope()
    {
        var assembly =
            typeof(KitbashDependencyInjectionContainer).Assembly;
        var commandType = GetRequiredType(
            assembly,
            $"{Namespace}.OpenPhotoStudioCommand");

        Assert.That(
            typeof(IDisposable).IsAssignableFrom(commandType),
            Is.True);
    }

    [Test]
    public void IconLibrary_LoadsPhotoStudioCameraIcon()
    {
        var property = typeof(IconLibrary).GetProperty(
            "CameraTool",
            BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic);
        Assert.That(property, Is.Not.Null);

        IconLibrary.Load();

        Assert.That(property!.GetValue(null), Is.Not.Null);
    }

    private static Type GetRequiredType(
        Assembly assembly,
        string name)
    {
        var type = assembly.GetType(name);
        Assert.That(type, Is.Not.Null, $"Missing production type {name}");
        return type!;
    }
}
