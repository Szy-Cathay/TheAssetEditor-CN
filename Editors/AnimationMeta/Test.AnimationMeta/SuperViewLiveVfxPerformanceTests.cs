using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.SuperView;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
using Editors.Shared.Core.Common;
using GameWorld.Core.Vfx;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public sealed class SuperViewLiveVfxPerformanceTests
{
    [TestCase(1)]
    [TestCase(24)]
    public void LiveVfxPublication_PreservesAllEffectInstancesAndDoesNotRebuildTheInspectionIndex(int count)
    {
        // DIAG: empty XML isolates host publication work, not game rendering semantics.
        const string vfxPath = @"vfx\diagnostic_live_edit.xml";
        const string metaPath = @"animations\battle\codex\diagnostic_live_edit.anm.meta";
        var runner = new AssetEditorTestRunner();
        runner.CreateOutputPack();
        var superView = (SuperViewViewModel)runner.ServiceProvider
            .GetRequiredService<IEditorCreator>().Create(EditorEnums.SuperView_Editor);
        try
        {
            var saves = runner.GetRequiredServiceInCurrentEditorScope<IFileSaveService>();
            var parser = runner.GetRequiredServiceInCurrentEditorScope<MetaDataFileParser>();
            var sceneEditor = runner.GetRequiredServiceInCurrentEditorScope<SceneObjectEditor>();
            var documents = runner.ServiceProvider.GetRequiredService<VfxPreviewDocumentStore>();
            var xml = Encoding.UTF8.GetBytes("<vfx revision=\"2\"><emitters/></vfx>");
            var file = saves.Save(vfxPath, xml, false)!;
            var metadata = new ParsedMetadataFile
            {
                Version = 2,
                Attributes = Enumerable.Range(0, count).Select(index => (ParsedMetadataAttribute)new Effect_v11
                {
                    Name = "EFFECT", Version = 11, VfxName = "diagnostic_live_edit", EndTime = 3,
                    Position = new Vector3(index, 0, 0), Orientation = new Vector4(0, 0, 0, 1), NodeIndex = -1,
                }).ToList(),
            };
            var metaFile = saves.Save(metaPath, parser.GenerateBytes(metadata.Version, metadata), false)!;
            var scene = superView.SceneObjects.Single();
            scene.FragAndSlotSelection.MetaDataName = metaPath;
            sceneEditor.SetMetaFile(scene.Data, metaFile, null);
            var instances = scene.Data.MetaDataItems.OfType<EffectMetaInstance>().ToArray();
            Assert.That(instances, Has.Length.EqualTo(count));
            var inspection = superView.MetaDataInspectionIndex;
            var inspectionChanges = 0;
            superView.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName == nameof(SuperViewViewModel.MetaDataInspectionIndex)) inspectionChanges++;
            };
            var publisher = new object();
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            for (var revision = 0; revision < 20; revision++)
                documents.Publish(publisher, file, new(xml, vfxPath, _ => null));
            stopwatch.Stop();
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            if (Environment.GetEnvironmentVariable("ASSETEDITOR_SUPERVIEW_EDIT_PERF_OUTPUT") is { Length: > 0 } output)
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, $"publication-{count}.json"), JsonSerializer.Serialize(new
                {
                    Diagnostic = "Empty XML isolates host publication, not game rendering or desktop FPS.",
                    Effects = count, Revisions = 20, ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                    AllocatedBytes = allocated, InspectionChanges = inspectionChanges,
                    InstancesReplaced = instances.Where((instance, index) => !ReferenceEquals(instance, scene.Data.MetaDataItems[index])).Count(),
                }, new JsonSerializerOptions { WriteIndented = true }));
            }

            Assert.Multiple(() =>
            {
                Assert.That(scene.Data.MetaDataItems.Count, Is.EqualTo(count));
                for (var index = 0; index < count; index++)
                    Assert.That(scene.Data.MetaDataItems[index], Is.SameAs(instances[index]),
                        "The scene node already observes source changes; the host must not discard its resources and playback state.");
                Assert.That(superView.MetaDataInspectionIndex, Is.SameAs(inspection));
                Assert.That(inspectionChanges, Is.Zero, "XML publication is not a META structure change.");
                Assert.That(superView.HasUnsavedChanges, Is.False);
                Assert.That(file.DataSource.ReadData(), Is.EqualTo(xml));
            });
            documents.Remove(publisher);
            for (var index = 0; index < count; index++)
                Assert.That(scene.Data.MetaDataItems[index], Is.SameAs(instances[index]));

            superView.SelectedTabControllerIndex = 1;
            var tag = superView.MetaEditor.Tags.First();
            superView.MetaEditor.SelectedTag = tag;
            var position = tag.Variables.OfType<VectorAttributeViewModel>().Single();
            for (var revision = 1; revision <= 20; revision++) position.Value.X.TextValue = revision.ToString();
            tag.Variables.Single(variable => variable.PropertyName == "Scale").ValueAsString = "2.5";
            Assert.Multiple(() =>
            {
                for (var index = 0; index < count; index++)
                    Assert.That(scene.Data.MetaDataItems[index], Is.SameAs(instances[index]),
                        "META transform editing must not discard the Effect or recreate its GPU resources.");
                Assert.That(instances[0].FocusPosition.X, Is.EqualTo(20));
                Assert.That(instances[0].IsSelected, Is.True);
                Assert.That(superView.HasUnsavedChanges, Is.True);
            });
        }
        finally { superView.Close(); }
    }
}
