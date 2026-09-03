using AnimationEditor.Common.BaseControl;
using Editors.AnimationVisualEditors.AnimationKeyframeEditor;
using Editors.Shared.Core.Common.BaseControl;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shared.Core.ToolCreation;

namespace AssetEditorTests;

[TestClass]
public class AnimationKeyframeEditorRegistrationTests
{
    [TestMethod]
    public void RegisterTools_UsesExistingKeyframeEditor()
    {
        var database = new EditorDatabase(null!, null!);

        new Editors.AnimationVisualEditors.DependencyInjectionContainer()
            .RegisterTools(database);

        var editor = database.GetEditorInfos().Single(item =>
            item.EditorEnum == EditorEnums.AnimationKeyFrame_Editor);

        Assert.AreEqual(
            typeof(EditorHost<AnimationKeyframeEditorViewModel>),
            editor.ViewModel);
        Assert.AreEqual(typeof(EditorHostView), editor.View);
        Assert.AreEqual("DisplayName.KeyFrameTool", editor.ToolbarName);
    }
}
