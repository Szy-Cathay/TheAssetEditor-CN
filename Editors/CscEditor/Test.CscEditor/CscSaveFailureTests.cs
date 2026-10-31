using System.Reflection;
using System.Runtime.CompilerServices;
using Editors.CscEditor.Data;
using Editors.CscEditor.Services;
using Editors.CscEditor.ViewModels;
using Serilog;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;

namespace Test.CscEditor;

public class CscSaveFailureTests
{
    [Test]
    public void Failed_replacement_load_keeps_previous_document_and_save_destination()
    {
        var scene = CscScene.Load(CscSceneRoundTripTests.BuildSceneWithManifestBytes());
        var originalFile = PackFile.CreateFromBytes("original.csc", CscSceneWriter.Write(scene));
        var invalidFile = PackFile.CreateFromBytes("invalid.csc", [0, 1, 2, 3]);
        var exceptions = 0;
        string? savedPath = null;
        byte[]? savedBytes = null;

        // Exercise the file transaction without constructing a GPU scene or a desktop window.
        var editor = (CscEditorViewModel)RuntimeHelpers.GetUninitializedObject(typeof(CscEditorViewModel));
        SetField(editor, "_logger", new LoggerConfiguration().CreateLogger());
        SetField(editor, "_localization", new LocalizationManager());
        SetField(editor, "_dialogs", Proxy<IStandardDialogs>((method, _) =>
        {
            Assert.That(method.Name, Is.EqualTo(nameof(IStandardDialogs.ShowExceptionWindow)));
            exceptions++;
            return null;
        }));
        SetField(editor, "_packFileService", Proxy<IPackFileService>((method, arguments) =>
        {
            Assert.That(method.Name, Is.EqualTo(nameof(IPackFileService.GetFullPath)));
            return @"composite_scene\" + ((PackFile)arguments![0]!).Name;
        }));
        SetField(editor, "_fileSaveService", Proxy<IFileSaveService>((method, arguments) =>
        {
            Assert.That(method.Name, Is.EqualTo(nameof(IFileSaveService.Save)));
            savedPath = (string)arguments![0]!;
            savedBytes = (byte[])arguments[1]!;
            return PackFile.CreateFromBytes("original.csc", savedBytes);
        }));
        SetProperty(editor, nameof(CscEditorViewModel.SceneData), scene);
        SetProperty(editor, nameof(CscEditorViewModel.CurrentFile), originalFile);
        editor.DisplayName = originalFile.Name;
        var history = new CscEditHistory(
            Proxy<IEventHub>((_, _) => null),
            () => CscSceneWriter.Write(scene),
            () => CscSelectionSnapshot.Root,
            (_, _) => { });
        SetField(editor, "_editHistory", history);
        history.InitializeLoadedDocument();
        scene.Duration += 2;
        history.RecordChange("Edited original scene");

        editor.LoadFile(invalidFile);
        var fileAfterLoad = editor.CurrentFile;
        var nameAfterLoad = editor.DisplayName;
        var dirtyAfterLoad = editor.HasUnsavedChanges;
        var saved = editor.Save();
        Assert.Multiple(() =>
        {
            Assert.That(exceptions, Is.EqualTo(1));
            Assert.That(fileAfterLoad, Is.SameAs(originalFile));
            Assert.That(nameAfterLoad, Is.EqualTo(originalFile.Name));
            Assert.That(editor.SceneData, Is.SameAs(scene));
            Assert.That(dirtyAfterLoad, Is.True);
            Assert.That(saved, Is.True);
            Assert.That(savedPath, Is.EqualTo(@"composite_scene\original.csc"));
            Assert.That(CscScene.Load(savedBytes!).Duration, Is.EqualTo(scene.Duration));
            Assert.That(editor.HasUnsavedChanges, Is.False);
        });
    }

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static void SetProperty(object target, string name, object value) =>
        target.GetType().GetProperty(name)!.SetValue(target, value);

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> callback) where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).Callback = callback;
        return proxy;
    }

    public class TestProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Callback { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Callback(targetMethod!, args);
    }
}
