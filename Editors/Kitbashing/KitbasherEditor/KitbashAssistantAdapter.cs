using System.Text.Json.Nodes;
using Editors.KitbasherEditor.Core;
using Editors.KitbasherEditor.ChildEditors.PinTool.Commands;
using Editors.KitbasherEditor.ChildEditors.ReRiggingTool;
using Editors.KitbasherEditor.ChildEditors.MeshFitter;
using Shared.Ui.Editors.BoneMapping;
using GameWorld.Core.Commands;
using Editors.KitbasherEditor.Core.MenuBarViews;
using Editors.KitbasherEditor.UiCommands;
using Editors.KitbasherEditor.ViewModels;
using Editors.KitbasherEditor.ViewModels.SceneNodeEditor;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.Rendering.Materials.Capabilities.Utility;
using GameWorld.Core.Rendering.Materials.Shaders;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using KitbasherEditor.ViewModels.MenuBarViews;
using Microsoft.Xna.Framework;
using Shared.Core.DependencyInjection;
using Shared.Core.ModAssistant;
using Shared.Core.PackFiles;
using Shared.Core.ToolCreation;
using static Shared.Core.ModAssistant.AssistantJson;

namespace Editors.KitbasherEditor;

public sealed class KitbashAssistantAdapter(IScopeRepository scopes, IPackFileService files) : IAssistantEditorAdapter
{
    public IEnumerable<AssistantOperation> GetOperations(IEditorInterface editor, AssistantIds ids)
    {
        if (editor is not KitbasherViewModel vm) yield break;
        var selection = vm.SceneExplorer.SelectionManager;
        var scene = vm.SceneExplorer.SceneManager;
        var history = scopes.GetRequiredService<CommandExecutor>(editor);
        var player = scopes.GetRequiredService<KitbasherRootScene>(editor).Player;
        ISceneNode[] Nodes() => scene.GetEnumeratorConditional(_ => true).ToArray();
        JsonNode State() => Node(new
        {
            document_revision = history.CurrentDocumentStateId,
            vm.HasUnsavedChanges,
            nodes = Nodes().Select(n => new
            {
                id = ids.Get(n),
                parent = n.Parent == null ? null : ids.Get(n.Parent),
                n.Name,
                type = n.GetType().Name,
                n.IsEditable,
                n.IsVisible,
                transform = n is Rmv2MeshNode mesh ? new
                {
                    position = Vector(mesh.Position),
                    scale = Vector(mesh.Scale),
                    rotation = new[] { mesh.Orientation.X, mesh.Orientation.Y, mesh.Orientation.Z, mesh.Orientation.W },
                    vertex_revision = mesh.Geometry.VertexDataVersion,
                    vertices = mesh.Geometry.VertexCount(),
                    bounds_min = Vector(mesh.Geometry.BoundingBox.Min),
                    bounds_max = Vector(mesh.Geometry.BoundingBox.Max)
                } : null
            }).ToArray(),
            selection = selection.GetState().SelectedObjects().Select(n => ids.Get(n)).ToArray(),
            selected_vertices = (selection.GetState() as VertexSelectionState)?.SelectedVertices,
            selected_faces = (selection.GetState() as FaceSelectionState)?.SelectedFaces,
            mode = selection.GetState().Mode.ToString(),
            skeleton = vm.Animation.SelectedSkeleton,
            animation = vm.Animation.SelectedAnimation?.ToString(),
        });
        AssistantOperation Op(string name, string label, JsonObject schema, AssistantEffect effect, Func<JsonObject, JsonNode> run, Func<string?>? unavailable = null) =>
            AssistantOperation.Sync(name, ids.Get(editor), Text(label), schema, effect,
                Text(effect == AssistantEffect.Edit ? "EditorUndo" : "NoDataChange"), State, run, unavailable);
        yield return Op("kitbash.select", "SelectObjects", Schema(("objects", Array(Str(), 100), true)), AssistantEffect.Preview, args =>
        {
            var nodes = Nodes();
            var targets = ((JsonArray)args["objects"]!).Select(x => nodes.SingleOrDefault(n => ids.Get(n) == x!.GetValue<string>())
                as ISelectable ?? throw new ArgumentException(Text("TargetGone"))).ToArray();
            if (targets.Any(n => !n.IsSelectable)) throw new ArgumentException(Text("ReadOnly"));
            var state = new ObjectSelectionState();
            state.ModifySelection(targets, false);
            selection.SetState(state);
            return State();
        });
        yield return Op("kitbash.visibility", "Visibility", Schema(("objects", Array(Str(), 100), true), ("visible", Bool(), true)), AssistantEffect.Preview, args =>
        {
            var targets = ((JsonArray)args["objects"]!).Select(x => Nodes().SingleOrDefault(n => ids.Get(n) == x!.GetValue<string>())
                ?? throw new ArgumentException(Text("TargetGone"))).ToArray();
            foreach (var target in targets) target.IsVisible = args.Boolean("visible");
            return State();
        });
        yield return Op("kitbash.transform", "Transform", Schema(("mode", Str("", "Translate", "Rotate", "Scale"), true),
            ("x", Num(-10000, 10000, "Translate: game units; Rotate: degrees; Scale: multiplier"), true),
            ("y", Num(-10000, 10000), true), ("z", Num(-10000, 10000), true), ("space", Str("", "world", "local"), true)), AssistantEffect.Edit, args =>
        {
            var mode = Enum.Parse<TransformToolViewModel.TransformMode>(args.String("mode"));
            if (mode == TransformToolViewModel.TransformMode.Scale && new[] { "x", "y", "z" }.Any(k => args.Number(k) <= 0))
                throw new ArgumentException(Text("InvalidParameters"));
            var tool = vm.MenuBar.TransformTool;
            tool.SelectedTransformSpaceIndex = args.String("space") == "local" ? 1 : 0;
            tool.SetMode(mode);
            tool.Vector3.Set(new Vector3((float)args.Number("x"), (float)args.Number("y"), (float)args.Number("z")));
            tool.ApplyCommand.Execute(null);
            return State();
        }, () => !vm.MenuBar.TransformTool.CanEditValues || !vm.MenuBar.TransformTool.ButtonEnabled ||
            selection.GetState().SelectedObjects().Any(n => !n.IsEditable) ? Text("Unavailable") : null);

        AssistantOperation? Command<T>(string name, AssistantEffect effect = AssistantEffect.Edit) where T : IKitbasherUiCommand
        {
            var action = vm.MenuBar.AssistantAction<T>();
            if (action == null) return null;
            return AssistantOperation.Sync(name, ids.Get(editor), action.ToolTipAttribute.Value, Schema(), effect,
                Text(effect == AssistantEffect.Edit ? "EditorUndo" : "NoDataChange"), State,
                _ => { action.TriggerInternal(); return State(); }, () => action.IsActionEnabled.Value ? null : Text("Unavailable"));
        }
        AssistantOperation?[] menuOperations =
        [
            Command<UndoCommand>("kitbash.undo"),
            Command<RedoCommand>("kitbash.redo"),
            Command<DuplicateObjectCommand>("kitbash.duplicate"),
            Command<DeleteObjectCommand>("kitbash.delete"),
            Command<GroupItemsCommand>("kitbash.group"),
            Command<MergeObjectsCommand>("kitbash.merge"),
            Command<DivideSubMeshCommand>("kitbash.split"),
            Command<SortMeshesCommand>("kitbash.sort_meshes"),
            Command<CreateStaticMeshCommand>("kitbash.static_mesh"),
            Command<ReduceMeshCommand>("kitbash.reduce_mesh"),
            Command<ConstructBoxUiCommand>("kitbash.create_box"),
            Command<ConstructPlaneUiCommand>("kitbash.create_plane"),
            Command<ConstructSphereUiCommand>("kitbash.create_sphere"),
            Command<FocusCameraCommand>("kitbash.focus", AssistantEffect.Preview),
            Command<ResetCameraCommand>("kitbash.reset_camera", AssistantEffect.Preview),
            Command<ToggleViewSelectedCommand>("kitbash.isolate", AssistantEffect.Preview),
            Command<BrowseForReferenceCommand>("kitbash.import_reference", AssistantEffect.Preview),
            Command<DeleteLodsCommand>("kitbash.delete_lower_lods"),
            Command<ConvertFaceToVertexCommand>("kitbash.faces_to_vertices", AssistantEffect.Preview),
            Command<ExpandFaceSelectionCommand>("kitbash.expand_face_selection", AssistantEffect.Preview),
        ];
        foreach (var operation in menuOperations)
            if (operation != null) yield return operation;
        yield return Op("kitbash.geometry", "GeometryInspect", Schema(("object", Str(), true), ("offset", Num(0, 10000000, integer: true), false)), AssistantEffect.Query, args =>
        {
            var mesh = Nodes().OfType<Rmv2MeshNode>().Single(m => ids.Get(m) == args.String("object"));
            return Node(new
            {
                mesh.Name,
                mesh.Geometry.SkeletonName,
                mesh.Geometry.WeightCount,
                vertex_count = mesh.Geometry.VertexCount(),
                vertices = mesh.Geometry.VertexArray.Select((v, i) => new { index = i, position = Vector(v.Position3()), v.BlendIndices, v.BlendWeights }).Skip(args.Integer("offset")).Take(100)
            });
        });
        yield return Op("kitbash.select_geometry", "GeometrySelect", Schema(("object", Str(), true), ("mode", Str("", "vertices", "faces"), true),
            ("indices", Array(Num(0, 10000000, integer: true), 10000), true)), AssistantEffect.Preview, args =>
        {
            var mesh = Nodes().OfType<Rmv2MeshNode>().Single(m => ids.Get(m) == args.String("object"));
            if (!mesh.IsEditable) throw new ArgumentException(Text("ReadOnly"));
            var indices = args["indices"]!.AsArray().Select(i => i!.GetValue<int>()).Distinct().ToArray();
            var vertices = args.String("mode") == "vertices";
            if (indices.Any(i => i >= (vertices ? mesh.Geometry.VertexCount() : mesh.Geometry.IndexArray.Length / 3))) throw new ArgumentException(Text("InvalidParameters"));
            if (vertices) { var state = new VertexSelectionState(mesh, 0); state.SetSelection(indices); selection.SetState(state); }
            else { var state = new FaceSelectionState { RenderObject = mesh }; state.ModifySelection(indices, false); selection.SetState(state); }
            return State();
        });
        yield return Op("kitbash.transfer_weights", "TransferWeights", Schema(("source", Str(), true), ("targets", Array(Str(), 100), true),
            ("mode", Str("", "nearest_surface", "pin_vertex"), true), ("vertex", Num(0, 10000000, integer: true), false)), AssistantEffect.Edit, args =>
        {
            var source = Nodes().OfType<Rmv2MeshNode>().Single(m => ids.Get(m) == args.String("source"));
            var targets = args["targets"]!.AsArray().Select(id => Nodes().OfType<Rmv2MeshNode>().Single(m => ids.Get(m) == id!.GetValue<string>())).ToArray();
            if (source.Geometry.WeightCount == 0 || targets.Contains(source) || targets.Distinct().Count() != targets.Length || targets.Any(m => !m.IsEditable))
                throw new ArgumentException(Text("InvalidParameters"));
            var commands = scopes.GetRequiredService<CommandFactory>(editor);
            if (args.String("mode") == "pin_vertex")
            {
                if (args["vertex"] == null || args.Integer("vertex") >= source.Geometry.VertexCount()) throw new ArgumentException(Text("InvalidParameters"));
                commands.Create<PinMeshToVertexCommand>().Configure(c => c.Configure(targets, source, args.Integer("vertex"))).BuildAndExecute();
            }
            else commands.Create<SkinWrapRiggingCommand>().Configure(c => c.Configure(targets, source)).BuildAndExecute();
            return State();
        });
        yield return Op("kitbash.bone_mappings", "ModelBoneMappings", Schema(("object", Str(), true)), AssistantEffect.Query, args =>
        {
            var mesh = Nodes().OfType<Rmv2MeshNode>().Single(m => ids.Get(m) == args.String("object"));
            var lookup = scopes.GetRequiredService<ISkeletonAnimationLookUpHelper>(editor);
            var source = lookup.GetSkeletonFileFromName(mesh.Geometry.SkeletonName);
            var target = scopes.GetRequiredService<KitbasherRootScene>(editor).Skeleton;
            return Node(new
            {
                source = mesh.Geometry.SkeletonName,
                target = target?.SkeletonName,
                source_bones = source?.Bones.Select(b => new { b.Id, b.Name }),
                target_bones = target?.BoneNames,
                used_bones = mesh.Geometry.GetUniqeBlendIndices()
            });
        });
        yield return Op("kitbash.rerig", "Rerig", Schema(("objects", Array(Str(), 100), true), ("mappings", Array(Schema(
            ("source_bone", Str(), true), ("target_bone", Str(), true)), 512), true), ("fit_mesh", Bool(), false), ("fit_scale", Num(.001, 1000), false),
            ("relative_scale", Bool(), false)), AssistantEffect.Edit, args =>
        {
            var meshes = args["objects"]!.AsArray().Select(id => Nodes().OfType<Rmv2MeshNode>().Single(m => ids.Get(m) == id!.GetValue<string>())).ToList();
            if (meshes.Distinct().Count() != meshes.Count || meshes.Any(m => !m.IsEditable || m.Geometry.WeightCount == 0) || meshes.Select(m => m.Geometry.SkeletonName).Distinct().Count() != 1)
                throw new ArgumentException(Text("InvalidParameters"));
            var lookup = scopes.GetRequiredService<ISkeletonAnimationLookUpHelper>(editor);
            var source = lookup.GetSkeletonFileFromName(meshes[0].Geometry.SkeletonName) ?? throw new ArgumentException(Text("MissingReference"));
            var target = scopes.GetRequiredService<KitbasherRootScene>(editor).Skeleton ?? throw new InvalidOperationException(Text("PreviewNotReady"));
            var targetFile = lookup.GetSkeletonFileFromName(target.SkeletonName) ?? throw new ArgumentException(Text("MissingReference"));
            var used = meshes.SelectMany(m => m.Geometry.GetUniqeBlendIndices()).Select(i => (int)i).Distinct().ToList();
            var mapping = args["mappings"]!.AsArray().Cast<JsonObject>().Select(m => new IndexRemapping(source.Bones.Single(b => b.Name == m.String("source_bone")).Id,
                targetFile.Bones.Single(b => b.Name == m.String("target_bone")).Id, true)).ToList();
            if (mapping.Select(m => m.OriginalValue).Distinct().Count() != mapping.Count || used.Any(i => !mapping.Any(m => m.OriginalValue == i)) || mapping.Any(m => m.NewValue is < 0 or > 255))
                throw new ArgumentException(Text("InvalidParameters"));
            if (args.Boolean("fit_mesh"))
            {
                var configuration = new RemappedAnimatedBoneConfiguration
                {
                    MeshSkeletonName = meshes[0].Geometry.SkeletonName,
                    ParnetModelSkeletonName = target.SkeletonName,
                    MeshBones = AnimatedBoneHelper.CreateFromSkeleton(source, used),
                    ParentModelBones = AnimatedBoneHelper.CreateFromSkeleton(targetFile)
                };
                foreach (var map in mapping)
                {
                    var bone = configuration.MeshBones.Select(b => b.GetFromBoneId(map.OriginalValue)).First(b => b != null);
                    bone.MappedBoneIndex.Value = map.NewValue; bone.MappedBoneName.Value = targetFile.Bones.Single(b => b.Id == map.NewValue).Name;
                }
                using var fitter = scopes.GetRequiredService<MeshFitterViewModel>(editor);
                fitter.Initialize(configuration, meshes, target, source);
                fitter.ScaleFactor.Value = args.Number("fit_scale", 1); fitter.RelativeScale.Value = args.Boolean("relative_scale");
                if (!fitter.ApplyAssistantFitting()) throw new InvalidOperationException(Text("NotCompleted"));
            }
            else scopes.GetRequiredService<CommandFactory>(editor).Create<RemapBoneIndexesCommand>().Configure(c => c.Configure(meshes, mapping, target.SkeletonName)).BuildAndExecute();
            return Node(new { state = State(), fitted_mesh = args.Boolean("fit_mesh"), remapped_weights = !args.Boolean("fit_mesh"), meshes = meshes.Select(m => new { target = ids.Get(m), m.Geometry.SkeletonName }) });
        });

        void ApplyMaterials((Rmv2MeshNode Mesh, CapabilityMaterial Material)[] values)
        {
            foreach (var (mesh, material) in values) mesh.Material = material;
            var current = selection.GetStateCopy();
            selection.CreateSelectionSate(GeometrySelectionMode.Object, null!);
            selection.SetState(current);
        }
        JsonObject ChangesSchema() => Array(Schema(("field", Str(), true),
            ("value_json", Str("JSON encoded value; schema is published by material.inspect"), true)), 100);
        CapabilityMaterial PrepareMaterial(Rmv2MeshNode mesh, JsonArray changes)
        {
            if (!mesh.IsEditable) throw new InvalidOperationException(Text("ReadOnly"));
            var clone = mesh.Material.Clone();
            var fields = MaterialFields(clone).ToDictionary(f => f.Name);
            var seen = new HashSet<string>();
            foreach (var change in changes.Cast<JsonObject>())
            {
                var name = change.String("field");
                if (!seen.Add(name) || !fields.TryGetValue(name, out var field)) throw new ArgumentException(Text("InvalidParameters"));
                var value = JsonNode.Parse(change.String("value_json"));
                Validate(value, field.Schema);
                field.Write(value!);
            }
            foreach (var texture in Textures(clone).Where(t => t.UseTexture && (seen.Contains("texture." + t.Type + ".path") || seen.Contains("texture." + t.Type + ".enabled"))))
                if (TexturePathResolver.FindTextureFile(files, texture.TexturePath, out _) == null) throw new ArgumentException(Text("MissingReference") + ": " + texture.TexturePath);
            return clone;
        }
        yield return Op("kitbash.materials.set", "MaterialBatch", Schema(("objects", Array(Str(), 100), true), ("changes", ChangesSchema(), true)), AssistantEffect.Edit, args =>
        {
            var meshes = args["objects"]!.AsArray().Select(id => Nodes().OfType<Rmv2MeshNode>().Single(x => ids.Get(x) == id!.GetValue<string>())).ToArray();
            if (meshes.Distinct().Count() != meshes.Length) throw new ArgumentException(Text("InvalidParameters"));
            var before = meshes.Select(m => (m, m.Material)).ToArray();
            var after = meshes.Select(m => (m, PrepareMaterial(m, args["changes"]!.AsArray()))).ToArray();
            scopes.GetRequiredService<SceneNodePropertyEditor>(editor).Update(before, after, ApplyMaterials);
            return Node(new { state = State(), materials = meshes.Select(m => new { target = ids.Get(m), fields = MaterialFields(m.Material).Select(f => new { f.Name, value = f.Read() }) }) });
        });

        yield return Op("kitbash.animation_choices", "AnimationChoices", Schema(("query", Str(), false), ("offset", Num(0, 1000000, integer: true), false)), AssistantEffect.Query,
            args => Node(new
            {
                skeletons = vm.Animation.SkeletonList.Where(x => x.Contains(args["query"]?.GetValue<string>() ?? "", StringComparison.OrdinalIgnoreCase)).Skip(args.Integer("offset")).Take(100),
                animations = vm.Animation.AnimationsForCurrentSkeleton.Where(x => x.ToString().Contains(args["query"]?.GetValue<string>() ?? "", StringComparison.OrdinalIgnoreCase))
                    .Skip(args.Integer("offset")).Take(100).Select(x => new { id = ids.Get(x), name = x.ToString() })
            }));
        yield return Op("kitbash.animation_select", "SelectAnimation", Schema(("skeleton", Str(), false), ("animation", Str("stable animation id"), false)), AssistantEffect.Preview, args =>
        {
            if (args["skeleton"] != null && args["animation"] != null) throw new ArgumentException(Text("InvalidParameters"));
            if (args["skeleton"] != null) vm.Animation.SelectedSkeleton = vm.Animation.SkeletonList.Single(x => x == args.String("skeleton"));
            if (args["animation"] != null) vm.Animation.SelectedAnimation = vm.Animation.AnimationsForCurrentSkeleton.Single(x => ids.Get(x) == args.String("animation"));
            return State();
        });
        yield return Op("kitbash.playback", "Playback", Schema(("action", Str("", "play", "pause", "seek_frame", "seek_seconds"), true),
            ("frame", Num(1, 1000000, "one-based displayed frame", true), false), ("seconds", Num(0, 86400, "seconds"), false)), AssistantEffect.Preview, args =>
        {
            var action = args.String("action");
            if (action == "seek_frame" && (args["frame"] == null || args.Integer("frame") > player.FrameCount)) throw new ArgumentException(Text("InvalidParameters"));
            if (action == "seek_seconds" && (args["seconds"] == null || args.Number("seconds") > player.Duration.TotalSeconds)) throw new ArgumentException(Text("InvalidParameters"));
            vm.Animation.IsEnabled = true;
            if (action == "play") player.Play(); else player.Pause();
            if (action == "seek_frame") player.CurrentFrame = args.Integer("frame") - 1;
            if (action == "seek_seconds") player.SeekToTimeSeconds((float)args.Number("seconds"));
            return Node(new { frame = player.CurrentFrame + 1, frames = player.FrameCount, seconds = player.CurrentTime.TotalSeconds, playing = player.IsPlaying });
        }, () => player.AnimationClip == null ? Text("PreviewNotReady") : null);

        foreach (var mesh in Nodes().OfType<Rmv2MeshNode>())
        {
            JsonNode MaterialState() => Node(new
            {
                owner = ids.Get(editor),
                mesh = ids.Get(mesh),
                mesh.Name,
                mesh.IsEditable,
                type = mesh.Material.Type.ToString(),
                document_revision = history.CurrentDocumentStateId,
                fields = MaterialFields(mesh.Material).Select(f => new { f.Name, schema = f.Schema, value = f.Read() }),
                preview_fields = PreviewFields(mesh.Material).Select(f => new { f.Name, schema = f.Schema, value = f.Read() })
            });
            yield return AssistantOperation.Sync("material.inspect", ids.Get(mesh), Text("MaterialInspect"), Schema(), AssistantEffect.Query, Text("NoDataChange"), MaterialState,
                _ => Node(new
                {
                    state = MaterialState(),
                    textures = Textures(mesh.Material).Select(t => new
                    {
                        slot = t.Type.ToString(),
                        path = t.TexturePath,
                        t.UseTexture,
                        found = string.IsNullOrEmpty(t.TexturePath) || TexturePathResolver.FindTextureFile(files, t.TexturePath, out var resolved) != null
                    })
                }));
            yield return AssistantOperation.Sync("material.set", ids.Get(mesh), Text("MaterialSet"), Schema(("changes", ChangesSchema(), true)), AssistantEffect.Edit,
                Text("EditorUndo"), MaterialState, args =>
                {
                    var clone = PrepareMaterial(mesh, args["changes"]!.AsArray());
                    scopes.GetRequiredService<SceneNodePropertyEditor>(editor).Update(mesh.Material, clone, value => ApplyMaterials([(mesh, value)]));
                    if (!ReferenceEquals(mesh.Material, clone)) throw new InvalidOperationException(Text("ExecutionFailed"));
                    return MaterialState();
                }, () => mesh.IsEditable ? null : Text("ReadOnly"));
            yield return AssistantOperation.Sync("material.preview_tint", ids.Get(mesh), Text("MaterialPreviewTint"), Schema(("changes", ChangesSchema(), true)),
                AssistantEffect.Preview, Text("NoDataChange"), MaterialState, args =>
                {
                    var clone = mesh.Material.Clone();
                    var fields = PreviewFields(clone).ToDictionary(f => f.Name);
                    var seen = new HashSet<string>();
                    foreach (var change in args["changes"]!.AsArray().Cast<JsonObject>())
                    {
                        if (!seen.Add(change.String("field")) || !fields.TryGetValue(change.String("field"), out var field)) throw new ArgumentException(Text("InvalidParameters"));
                        var value = JsonNode.Parse(change.String("value_json")); Validate(value, field.Schema); field.Write(value!);
                    }
                    ApplyMaterials([(mesh, clone)]);
                    return MaterialState();
                }, () => PreviewFields(mesh.Material).Any() ? null : Text("Unavailable"));
        }
    }

    private static float[] Vector(Vector3 v) => [v.X, v.Y, v.Z];
    private sealed record Field(string Name, JsonObject Schema, Func<JsonNode> Read, Action<JsonNode> Write);
    private static IEnumerable<TextureInput> Textures(CapabilityMaterial material)
    {
        foreach (var capability in material.Capabilities)
        {
            TextureInput[] textures = capability switch
            {
                MetalRoughCapability c => [c.BaseColour, c.MaterialMap, c.NormalMap, c.Mask],
                SpecGlossCapability c => [c.DiffuseMap, c.SpecularMap, c.GlossMap, c.NormalMap, c.Mask],
                EmissiveCapability c => [c.Emissive, c.EmissiveDistortion],
                AdvancedMaterialCapability c => [c.DirtMap, c.DirtMask, c.DecalMask, c.SkinMask],
                BloodCapability c => [c.BloodMask],
                _ => [],
            };
            foreach (var texture in textures) yield return texture;
        }
    }
    private static IEnumerable<Field> MaterialFields(CapabilityMaterial material)
    {
        foreach (var t in Textures(material))
        {
            yield return new("texture." + t.Type + ".path", Str(), () => Node(t.TexturePath), v => t.TexturePath = v.GetValue<string>());
            yield return new("texture." + t.Type + ".enabled", Bool(), () => Node(t.UseTexture), v => t.UseTexture = v.GetValue<bool>());
        }
        Field BooleanField(string name, Func<bool> get, Action<bool> set) => new(name, Bool(), () => Node(get()), v => set(v.GetValue<bool>()));
        Field Scalar(string name, Func<float> get, Action<float> set, double min = 0, double max = 100) => new(name, Num(min, max), () => Node(get()), v => set(v.GetValue<float>()));
        Field Colour(string name, Func<Vector3> get, Action<Vector3> set) => new(name,
            Schema(("r", Num(0, 1), true), ("g", Num(0, 1), true), ("b", Num(0, 1), true)),
            () => Node(new { r = get().X, g = get().Y, b = get().Z }), v => set(new Vector3((float)v["r"]!.GetValue<double>(), (float)v["g"]!.GetValue<double>(), (float)v["b"]!.GetValue<double>())));
        foreach (var c in material.Capabilities.OfType<MaterialBaseCapability>())
            yield return BooleanField("use_alpha", () => c.UseAlpha, v => c.UseAlpha = v);
        if (material.TryGetCapability<EmissiveCapability>() is { } e)
        {
            yield return Scalar("emissive.strength", () => e.EmissiveStrength, v => e.EmissiveStrength = v);
            yield return Scalar("emissive.speed", () => e.EmissiveSpeed, v => e.EmissiveSpeed = v, -100);
            yield return Scalar("emissive.pulse_speed", () => e.EmissivePulseSpeed, v => e.EmissivePulseSpeed = v);
            yield return Scalar("emissive.pulse_strength", () => e.EmissivePulseStrength, v => e.EmissivePulseStrength = v);
            yield return Scalar("emissive.distort_strength", () => e.EmissiveDistortStrength, v => e.EmissiveDistortStrength = v);
            yield return Scalar("emissive.fresnel_strength", () => e.EmissiveFresnelStrength, v => e.EmissiveFresnelStrength = v);
            yield return Colour("emissive.tint", () => e.EmissiveTint, v => e.EmissiveTint = v);
        }
        if (material.TryGetCapability<AdvancedMaterialCapability>() is { } a)
        {
            yield return BooleanField("use_dirt", () => a.UseDirt, v => a.UseDirt = v);
            yield return BooleanField("use_decal", () => a.UseDecal, v => a.UseDecal = v);
            yield return BooleanField("use_skin", () => a.UseSkin, v => a.UseSkin = v);
            yield return Scalar("uv_scale.x", () => a.UvScale.X, v => a.UvScale = new Vector2(v, a.UvScale.Y), -10000, 10000);
            yield return Scalar("uv_scale.y", () => a.UvScale.Y, v => a.UvScale = new Vector2(a.UvScale.X, v), -10000, 10000);
            yield return Scalar("texture_transform.x", () => a.TextureTransform.X, v => a.TextureTransform = new Vector4(v, a.TextureTransform.Y, a.TextureTransform.Z, a.TextureTransform.W), -10000, 10000);
            yield return Scalar("texture_transform.y", () => a.TextureTransform.Y, v => a.TextureTransform = new Vector4(a.TextureTransform.X, v, a.TextureTransform.Z, a.TextureTransform.W), -10000, 10000);
            yield return Scalar("texture_transform.z", () => a.TextureTransform.Z, v => a.TextureTransform = new Vector4(a.TextureTransform.X, a.TextureTransform.Y, v, a.TextureTransform.W), -10000, 10000);
            yield return Scalar("texture_transform.w", () => a.TextureTransform.W, v => a.TextureTransform = new Vector4(a.TextureTransform.X, a.TextureTransform.Y, a.TextureTransform.Z, v), -10000, 10000);
        }
        if (material.TryGetCapability<BloodCapability>() is { } blood)
            yield return BooleanField("use_blood", () => blood.UseBlood, v => blood.UseBlood = v);
    }
    private static IEnumerable<Field> PreviewFields(CapabilityMaterial material)
    {
        if (material.TryGetCapability<TintCapability>() is not { } tint) yield break;
        yield return new("use_tinting", Bool(), () => Node(tint.UseTinting), v => tint.UseTinting = v.GetValue<bool>());
        for (var i = 0; i < 3; i++)
        {
            var index = i;
            var schema = Schema(("r", Num(0, 1), true), ("g", Num(0, 1), true), ("b", Num(0, 1), true));
            JsonNode Colour(Vector3 c) => Node(new { r = c.X, g = c.Y, b = c.Z });
            Vector3 Read(JsonNode v) => new((float)v.AsObject().Number("r"), (float)v.AsObject().Number("g"), (float)v.AsObject().Number("b"));
            yield return new("tint_colour_" + (i + 1), schema, () => Colour(tint.TintColours[index]), v => tint.TintColours[index] = Read(v));
        }
    }
}
