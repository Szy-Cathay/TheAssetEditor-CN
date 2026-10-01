using System.Reflection;
using System.Threading;
using Editors.AnimatioReTarget.Editor;
using Editors.Shared.Core.Common;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.Rendering.Materials.Shaders;
using GameWorld.Core.Rendering.RenderItems;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.ToolCreation;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.AnimatioReTarget;

[NonParallelizable]
[Apartment(ApartmentState.STA)]
public class AnimationRetargetPreviewTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void UpdateAnimation_RiderAction_RendersEverySourceAndGeneratedBone(bool changeTargetAfterMeshLoad)
    {
        var runner = new AssetEditorTestRunner();
        runner.CreateCaContainer();
        var output = runner.LoadPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), true)!;
        var sourceSkeleton = PackFile.CreateFromFileSystem("humanoid01e.anim",
            PathHelper.GetDataFile("AnimationRetarget/animations/skeletons/humanoid01e.anim"));
        var targetSkeleton = PackFile.CreateFromFileSystem("humanoid01d.anim",
            PathHelper.GetDataFile("AnimationRetarget/animations/skeletons/humanoid01d.anim"));
        var animationPath = @"animations\battle\humanoid01e\rider\warhorse\sword_lord\locomotion\hu1e_hr1_sword_lord_canter_01.anim";
        runner.PackFileService.AddFilesToPack(output,
        [
            new NewPackFileEntry(@"animations\skeletons", sourceSkeleton),
            new NewPackFileEntry(@"animations\skeletons", targetSkeleton),
            new NewPackFileEntry(Path.GetDirectoryName(animationPath)!, PackFile.CreateFromFileSystem(Path.GetFileName(animationPath),
                PathHelper.GetDataFile($"AnimationRetarget/{animationPath}"))),
        ]);
        using var editor = (AnimationRetargetEditor)runner.CommandFactory.Create<OpenEditorCommand>()
            .Execute(EditorEnums.AnimationRetarget_Editor);
        var sceneObjectEditor = runner.GetRequiredServiceInCurrentEditorScope<SceneObjectEditor>();
        var source = editor.GetSceneObjectFromId(AnimationRetargetIds.Source)!.Data;
        var target = editor.GetSceneObjectFromId(AnimationRetargetIds.Target)!.Data;
        var generated = editor.GetSceneObjectFromId(AnimationRetargetIds.Generated)!.Data;
        sceneObjectEditor.SetSkeleton(source, sourceSkeleton);
        sceneObjectEditor.SetAnimation(source, animationPath);
        if (changeTargetAfterMeshLoad)
            sceneObjectEditor.SetMesh(target, runner.PackFileService.FindFile(
                @"variantmeshes\wh_variantmodels\hu1e\cth\cth_celestial_general\cth_celestial_general_body_02.wsmodel")!);
        sceneObjectEditor.SetSkeleton(target, targetSkeleton);
        editor.BoneManager.ApplyDefaultMapping();
        foreach (var bone in editor.BoneManager.FlatBoneList)
        {
            editor.BoneManager.SelectedBone = bone;
            editor.BoneManager.ResetSelectedBoneCommand.Execute(null);
        }
        editor.UpdateAnimation();
        Assert.Multiple(() =>
        {
            Assert.That(generated.Skeleton, Is.Not.Null);
            Assert.That(generated.Skeleton.SkeletonName, Is.EqualTo(target.Skeleton.SkeletonName));
            Assert.That(generated.Skeleton.BoneNames, Is.EqualTo(target.Skeleton.BoneNames));
            Assert.That(generated.Skeleton.AnimationPlayer, Is.SameAs(generated.Player));
            Assert.That(generated.SkeletonSceneNode.Skeleton, Is.SameAs(generated.Skeleton));
        });
        var renderEngine = runner.GetRequiredServiceInCurrentEditorScope<RenderEngineComponent>();
        var lines = (List<VertexPositionColor>)typeof(RenderEngineComponent)
            .GetField("_renderLines", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderEngine)!;
        var device = editor.GameWorld!.GraphicsDevice;
        using var renderTarget = new RenderTarget2D(device, 1000, 600, false, SurfaceFormat.Color, DepthFormat.Depth24);
        using var effect = new BasicEffect(device) { VertexColorEnabled = true };
        foreach (var frameIndex in new[] { 0, generated.AnimationClip.DynamicFrames.Count / 2, generated.AnimationClip.DynamicFrames.Count - 1 })
        {
            source.Player.IsEnabled = true;
            generated.Player.IsEnabled = true;
            source.Player.CurrentFrame = frameIndex;
            generated.Player.CurrentFrame = frameIndex;
            source.Player.Refresh();
            generated.Player.Refresh();
            foreach (var sideView in new[] { false, true })
            {
                lines.Clear();
                var offsetAxis = sideView ? Vector3.UnitZ : Vector3.UnitX;
                foreach (var (scene, offset) in new[] { (source, -1.6f), (generated, 1.6f) })
                {
                    scene.SkeletonSceneNode.IsVisible = true;
                    scene.SkeletonSceneNode.SelectedBoneIndex = scene.Skeleton.GetBoneIndexByName("spine_2");
                    scene.SkeletonSceneNode.NodeColour = scene == source ? Color.DarkGreen : Color.DarkBlue;
                    var start = lines.Count;
                    scene.SkeletonSceneNode.Render(renderEngine, Matrix.CreateTranslation(offsetAxis * offset));
                    var cursor = start;
                    for (var bone = 0; bone < scene.Skeleton.BoneCount; bone++)
                    {
                        var centre = Vector3.Zero;
                        for (var vertex = 0; vertex < 24; vertex++)
                            centre += lines[cursor + vertex].Position / 24;
                        var expected = scene.Skeleton.GetAnimatedWorldTranform(bone).Translation + offsetAxis * offset;
                        Assert.That(Vector3.Distance(centre, expected), Is.LessThan(0.0001f), scene.Skeleton.BoneNames[bone]);
                        cursor += scene.Skeleton.GetParentBoneIndex(bone) < 0 ? 24 : 26;
                    }
                    Assert.That(cursor, Is.EqualTo(lines.Count), "Every bone and parent link must be rendered.");
                }
                var bounds = BoundingBox.CreateFromPoints(lines.Select(line => line.Position));
                var centrePoint = (bounds.Min + bounds.Max) / 2;
                var size = bounds.Max - bounds.Min;
                var width = MathF.Max(sideView ? size.Z : size.X, 1) * 1.15f;
                var height = MathF.Max(size.Y, width * 0.6f) * 1.15f;
                var distance = MathF.Max(size.Length(), 1) * 2;
                effect.World = Matrix.Identity;
                effect.View = Matrix.CreateLookAt(centrePoint + (sideView ? Vector3.UnitX : Vector3.UnitZ) * distance,
                    centrePoint, Vector3.Up);
                effect.Projection = Matrix.CreateOrthographic(width, height, 0.01f, distance * 4);
                device.SetRenderTarget(renderTarget);
                device.Clear(Color.LightGray);
                device.BlendState = BlendState.Opaque;
                device.DepthStencilState = DepthStencilState.Default;
                device.RasterizerState = RasterizerState.CullNone;
                foreach (var pass in effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    device.DrawUserPrimitives(PrimitiveType.LineList, lines.ToArray(), 0, lines.Count / 2);
                }
                device.SetRenderTarget(null);
                var pixels = new Color[renderTarget.Width * renderTarget.Height];
                renderTarget.GetData(pixels);
                Assert.That(pixels.Count(pixel => pixel.R > pixel.G * 1.5f && pixel.R > pixel.B * 1.5f), Is.GreaterThan(10),
                    "Both selected spine bones must produce visible pixels.");
                var path = Path.Combine(TestContext.CurrentContext.TestDirectory, $"retarget-rider-all-bones-{frameIndex}-{sideView}.png");
                using (var image = File.Create(path))
                    renderTarget.SaveAsPng(image, renderTarget.Width, renderTarget.Height);
                TestContext.AddTestAttachment(path);
            }
        }
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void UpdateAnimation_VariantMeshWeapon_FollowsGeneratedAnimation(bool reloadTarget, bool useDifferentHumanoid)
    {
        var runner = new AssetEditorTestRunner();
        runner.CreateCaContainer();
        var output = runner.LoadPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), true)!;
        var variantMesh = PackFile.CreateFromASCII("retarget_weapon_preview.variantmeshdefinition", """
            <VARIANT_MESH model="variantmeshes/wh_variantmodels/hu1e/cth/cth_celestial_general/cth_celestial_general_body_02.wsmodel">
              <SLOT name="weapon" attach_point="weapon_1">
                <VARIANT_MESH model="variantmeshes/wh_variantmodels/hu1e/cth/cth_props/cth_celestial_general_hammer_2h_01.wsmodel" />
              </SLOT>
            </VARIANT_MESH>
            """);
        runner.PackFileService.AddFilesToPack(output, [new NewPackFileEntry("variantmeshes", variantMesh)]);
        using var editor = (AnimationRetargetEditor)runner.CommandFactory.Create<OpenEditorCommand>()
            .Execute(EditorEnums.AnimationRetarget_Editor);
        var sceneObjectEditor = runner.GetRequiredServiceInCurrentEditorScope<SceneObjectEditor>();
        var source = editor.GetSceneObjectFromId(AnimationRetargetIds.Source)!.Data;
        var target = editor.GetSceneObjectFromId(AnimationRetargetIds.Target)!.Data;
        var generated = editor.GetSceneObjectFromId(AnimationRetargetIds.Generated)!.Data;
        if (useDifferentHumanoid)
        {
            var skeleton = PackFile.CreateFromFileSystem("humanoid01c.anim",
                PathHelper.GetDataFile("AnimationRetarget/animations/skeletons/humanoid01c.anim"));
            var animationPath = @"animations\battle\humanoid01c\2handed_axe\attacks\hu1c_2ha_attack_01.anim";
            var animation = PackFile.CreateFromFileSystem(Path.GetFileName(animationPath),
                PathHelper.GetDataFile($"AnimationRetarget/{animationPath}"));
            runner.PackFileService.AddFilesToPack(output,
            [
                new NewPackFileEntry(@"animations\skeletons", skeleton),
                new NewPackFileEntry(Path.GetDirectoryName(animationPath)!, animation),
            ]);
            sceneObjectEditor.SetSkeleton(source, skeleton);
            sceneObjectEditor.SetAnimation(source, animationPath);
        }
        else
        {
            sceneObjectEditor.SetMesh(source, runner.PackFileService.FindFile(
                @"variantmeshes\wh_variantmodels\hu1\emp\emp_karl_franz\emp_karl_franz.wsmodel")!);
            sceneObjectEditor.SetAnimation(source,
                @"animations\battle\humanoid01\2handed_hammer\stand\hu1_2hh_stand_idle_01.anim");
        }
        sceneObjectEditor.SetMesh(target, variantMesh);
        editor.BoneManager.ApplyDefaultMapping();
        var weaponBone = editor.BoneManager.FlatBoneList.Single(bone => bone.BoneName == "weapon_1");
        weaponBone.HasMapping = true;
        weaponBone.MappedIndex = source.Skeleton.GetBoneIndexByName("be_prop_0");
        Assert.That(weaponBone.MappedIndex, Is.GreaterThanOrEqualTo(0));
        foreach (var bone in editor.BoneManager.FlatBoneList)
        {
            editor.BoneManager.SelectedBone = bone;
            editor.BoneManager.ResetSelectedBoneCommand.Execute(null);
            bone.ApplyTranslation = true;
            bone.ApplyRotation = true;
        }
        editor.UpdateAnimation();
        if (reloadTarget)
        {
            sceneObjectEditor.SetMesh(target, variantMesh);
            editor.UpdateAnimation();
        }

        var targetWeapon = GetWeapon(target);
        var generatedWeapon = GetWeapon(generated);
        Assert.Multiple(() =>
        {
            Assert.That(generatedWeapon.AttachmentPointName, Is.EqualTo("weapon_1"));
            Assert.That(generatedWeapon.AnimationPlayer, Is.SameAs(generated.Player));
            Assert.That(generatedWeapon.AttachmentBoneResolver, Is.Not.Null);
            Assert.That(generatedWeapon.AttachmentBoneResolver, Is.Not.SameAs(targetWeapon.AttachmentBoneResolver));
        });
        var boneIndex = generated.Skeleton.GetBoneIndexByName("weapon_1");
        Assert.That(boneIndex, Is.GreaterThanOrEqualTo(0));
        generated.Player.IsEnabled = true;
        foreach (var frameIndex in new[] { 0, generated.AnimationClip.DynamicFrames.Count / 2, generated.AnimationClip.DynamicFrames.Count - 1 })
        {
            generated.Player.CurrentFrame = frameIndex;
            generated.Player.Refresh();
            generated.Skeleton.Update();
            var expected = generated.Skeleton.GetAnimatedWorldTranform(boneIndex);
            var actual = generatedWeapon.AttachmentBoneResolver!.GetWorldTransformIfAnimating();
            Assert.That(Vector3.Distance(actual.Translation, expected.Translation), Is.LessThan(0.0001f));
            Assert.That(MatrixDifference(actual, expected), Is.LessThan(0.0001f));
            AssertWeaponRender(runner, editor, generatedWeapon, targetWeapon, frameIndex, reloadTarget);
            if (useDifferentHumanoid && !reloadTarget)
                AssertBodyRender(runner, editor, generated, frameIndex);
        }
    }

    private static void AssertBodyRender(AssetEditorTestRunner runner, AnimationRetargetEditor editor, SceneObject generated, int frameIndex)
    {
        var renderEngine = runner.GetRequiredServiceInCurrentEditorScope<RenderEngineComponent>();
        var renderItems = (IReadOnlyDictionary<RenderBuckedId, List<IRenderItem>>)typeof(RenderEngineComponent)
            .GetField("_renderItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderEngine)!;
        renderItems[RenderBuckedId.Normal].Clear();
        foreach (var mesh in SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(generated.ModelNode))
            mesh.Render(renderEngine, Matrix.Identity);
        var body = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(generated.ModelNode)
            .First(node => string.IsNullOrEmpty(node.AttachmentPointName));
        var bounds = BoundingSphere.CreateFromBoundingBox(body.Geometry.BoundingBox);
        var centre = bounds.Center;
        var radius = MathF.Max(bounds.Radius, 1);
        var device = editor.GameWorld!.GraphicsDevice;
        using var renderTarget = new RenderTarget2D(device, 512, 512, false, SurfaceFormat.Color, DepthFormat.Depth24);
        foreach (var side in new[] { -1, 1 })
        {
            var camera = centre + new Vector3(0, 0, side * radius * 4);
            var parameters = new CommonShaderParameters(
                Matrix.CreateLookAt(camera, centre, Vector3.Up),
                Matrix.CreateOrthographic(radius * 2.5f, radius * 2.5f, 0.01f, radius * 10),
                camera, centre, 0, 0, 0, 1, Vector3.One, [Vector3.One, Vector3.One, Vector3.One]);
            device.SetRenderTarget(renderTarget);
            device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Transparent, 1, 0);
            device.BlendState = BlendState.Opaque;
            device.DepthStencilState = DepthStencilState.Default;
            device.RasterizerState = RasterizerState.CullNone;
            try
            {
                foreach (var item in renderItems[RenderBuckedId.Normal])
                    item.Draw(device, parameters, RenderingTechnique.Normal);
            }
            finally
            {
                device.SetRenderTarget(null);
            }
            var pixels = new Color[renderTarget.Width * renderTarget.Height];
            renderTarget.GetData(pixels);
            Assert.That(pixels.Count(pixel => pixel.A > 0), Is.GreaterThan(1000), "The generated body must produce visible pixels.");
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, $"retarget-body-{frameIndex}-{side}.png");
            using (var image = File.Create(path))
                renderTarget.SaveAsPng(image, renderTarget.Width, renderTarget.Height);
            TestContext.AddTestAttachment(path);
        }
    }

    private static void AssertWeaponRender(
        AssetEditorTestRunner runner,
        AnimationRetargetEditor editor,
        Rmv2MeshNode weapon,
        Rmv2MeshNode targetWeapon,
        int frameIndex,
        bool reloadTarget)
    {
        var renderEngine = runner.GetRequiredServiceInCurrentEditorScope<RenderEngineComponent>();
        var renderItems = (IReadOnlyDictionary<RenderBuckedId, List<IRenderItem>>)typeof(RenderEngineComponent)
            .GetField("_renderItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderEngine)!;
        renderItems[RenderBuckedId.Normal].Clear();
        weapon.Render(renderEngine, Matrix.Identity);
        var item = renderItems[RenderBuckedId.Normal].Single();
        var world = weapon.GetRenderWorldMatrix();
        var sphere = BoundingSphere.CreateFromBoundingBox(weapon.Geometry.BoundingBox);
        var centre = Vector3.Transform(sphere.Center, world);
        var radius = MathF.Max(sphere.Radius, 0.1f);
        var camera = centre + new Vector3(0, radius, radius * 4);
        var parameters = new CommonShaderParameters(
            Matrix.CreateLookAt(camera, centre, Vector3.Up),
            Matrix.CreateOrthographic(radius * 3, radius * 3, 0.01f, radius * 10),
            camera, centre, 0, 0, 0, 1, Vector3.One,
            [Vector3.One, Vector3.One, Vector3.One]);
        var device = editor.GameWorld!.GraphicsDevice;
        using var renderTarget = new RenderTarget2D(device, 256, 256, false, SurfaceFormat.Color, DepthFormat.Depth24);
        var pixels = DrawWeapon(device, renderTarget, item, parameters);
        Assert.That(pixels.Count(pixel => pixel.A > 0), Is.GreaterThan(50), "The generated weapon must produce visible pixels.");
        Assert.That(weapon.Material.GetCapability<CommonShaderParametersCapability>().ModelMatrix, Is.EqualTo(world));
        var effect = (Effect)typeof(CapabilityMaterial)
            .GetMethod("GetEffect", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(weapon.Material, null)!;
        Assert.That(effect.Parameters["World"].GetValueMatrix(), Is.EqualTo(world), "The shader must receive the generated weapon transform.");
        var imagePath = Path.Combine(TestContext.CurrentContext.TestDirectory, $"retarget-weapon-{reloadTarget}-{frameIndex}.png");
        using (var image = File.Create(imagePath))
            renderTarget.SaveAsPng(image, renderTarget.Width, renderTarget.Height);
        TestContext.AddTestAttachment(imagePath);

        var bindItem = new GeometryRenderItem(weapon.Geometry, weapon.Material, targetWeapon.GetRenderWorldMatrix());
        var bindPixels = DrawWeapon(device, renderTarget, bindItem, parameters);
        Assert.That(pixels.Where((pixel, index) => pixel != bindPixels[index]).Count(), Is.GreaterThan(50),
            "The generated weapon render must differ from the target bind pose.");
    }

    private static Color[] DrawWeapon(
        GraphicsDevice device,
        RenderTarget2D renderTarget,
        IRenderItem item,
        CommonShaderParameters parameters)
    {
        device.SetRenderTarget(renderTarget);
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Transparent, 1, 0);
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        device.RasterizerState = RasterizerState.CullNone;
        try
        {
            item.Draw(device, parameters, RenderingTechnique.Normal);
        }
        finally
        {
            device.SetRenderTarget(null);
        }
        var pixels = new Color[renderTarget.Width * renderTarget.Height];
        renderTarget.GetData(pixels);
        return pixels;
    }

    private static Rmv2MeshNode GetWeapon(SceneObject sceneObject)
    {
        var slot = SceneNodeHelper.GetChildrenOfType<SlotNode>(sceneObject.ModelNode)
            .Single(node => node.AttachmentBoneName == "weapon_1");
        return SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(slot).First();
    }

    private static float MatrixDifference(Matrix actual, Matrix expected) =>
        MathF.Abs(actual.M11 - expected.M11) + MathF.Abs(actual.M12 - expected.M12) + MathF.Abs(actual.M13 - expected.M13) +
        MathF.Abs(actual.M21 - expected.M21) + MathF.Abs(actual.M22 - expected.M22) + MathF.Abs(actual.M23 - expected.M23) +
        MathF.Abs(actual.M31 - expected.M31) + MathF.Abs(actual.M32 - expected.M32) + MathF.Abs(actual.M33 - expected.M33);
}
