using Editors.AnimationMeta.SuperView.Visualisation.Rules;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Moq;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.RigidModel.Transforms;

namespace Test.AnimationMeta;

[TestFixture]
internal sealed class ThreeKingdomsPropWindTests
{
    [TestCaseSource(nameof(NativeAngles))]
    public void AngleKernel_MatchesOfficialNativeStateAndReturnBits(int factorBits, int correction,
        int heading, int enabled, int windX, int windZ, int forwardX, int forwardZ,
        int expectedCorrection, int expectedHeading, int expectedReturnBits)
    {
        var state = new ThreeKingdomsWindAngleState((ushort)heading, (ushort)correction);
        var angle = state.Update(enabled != 0, new(windX, 0, windZ), new(forwardX, 0, forwardZ),
            BitConverter.Int32BitsToSingle(factorBits));
        Assert.Multiple(() =>
        {
            Assert.That(state.Correction, Is.EqualTo(expectedCorrection));
            Assert.That(state.Heading, Is.EqualTo(expectedHeading));
            Assert.That(BitConverter.SingleToInt32Bits(angle), Is.EqualTo(expectedReturnBits));
        });
    }

    [Test]
    public void ModelSpaceCorrection_MatchesNativeWithRotatedParent()
    {
        var parent = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.3f);
        var bone = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f);
        var result = ThreeKingdomsPropWindRule.ApplyModelSpaceRotation(bone, parent, 0.5f);
        // Official 0x7394e0 probe; distinguishes model-space delta from local or world-Y rotation.
        var expected = new Quaternion(-0.2316426932811737f, 0.26414865255355835f,
            -0.04695628210902214f, 0.9350734353065491f);
        Assert.That(Math.Abs(Quaternion.Dot(result, expected)), Is.EqualTo(1).Within(0.000001f));
    }

    [Test]
    public void WindGate_UsesAlternateAndPausedRefreshDoesNotAdvance()
    {
        var skeleton = CreateSkeleton();
        var primary = Clip(new(2, 0, 0));
        var alternate = Clip(new(0, 3, 0));
        var wind = Vector2.Zero;
        var clock = new AnimationPlaybackTime(0.25f, 0.25, 1, 0, false, false);
        var rule = new ThreeKingdomsPropWindRule(skeleton, primary, alternate, 1, true,
            () => wind, () => Matrix.Identity, () => clock);
        var first = AnimationSampler.Sample(0.25f, skeleton, primary, [rule]);
        Assert.That(rule.UsesAlternate, Is.True);
        Assert.That(first.BoneTransforms[1].Translation, Is.EqualTo(new Vector3(0, 3, 0)));

        for (var index = 0; index < 20; index++)
        {
            var paused = AnimationSampler.Sample(0.25f, skeleton, primary, [rule]);
            Assert.That(paused.BoneTransforms.Select(bone => bone.WorldTransform),
                Is.EqualTo(first.BoneTransforms.Select(bone => bone.WorldTransform)));
        }
        Assert.That(rule.WindHeading, Is.Zero);

        wind = new(1, 1);
        var switchedWhilePaused = AnimationSampler.Sample(0.25f, skeleton, primary, [rule]);
        Assert.That(rule.UsesAlternate, Is.False);
        Assert.That(switchedWhilePaused.BoneTransforms[1].Translation,
            Is.EqualTo(first.BoneTransforms[1].Translation));
        Assert.That(rule.WindHeading, Is.Zero);

        clock = clock with { ElapsedSeconds = 0.35, ClipTimeSeconds = 0.35f, IsRunning = true };
        var advancing = AnimationSampler.Sample(0.35f, skeleton, primary, [rule]);
        Assert.That(advancing.BoneTransforms[1].Translation.X, Is.InRange(0.00001f, 1.99999f));
        Assert.That(advancing.BoneTransforms[1].Translation.Y, Is.InRange(0.00001f, 2.99999f));
        Assert.That(rule.WindHeading, Is.GreaterThan(0));
    }

    [Test]
    public void BodyLoop_UsesContinuousPropTimeAndSeekReplaysDeterministically()
    {
        var skeleton = CreateSkeleton();
        var primary = Clip(Vector3.Zero);
        primary.DynamicFrames[1].Position[1] = new(10, 0, 0);
        var clock = new AnimationPlaybackTime(0.1f, 1.1, 1, 1, true, false);
        var rule = new ThreeKingdomsPropWindRule(skeleton, primary, null, -1, false,
            () => Vector2.Zero, () => Matrix.Identity, () => clock);
        var actual = AnimationSampler.Sample(0.1f, skeleton, primary, [rule]);
        var expected = AnimationSampler.Sample(0.55f, skeleton, primary);
        Assert.That(actual.BoneTransforms[1].Translation, Is.EqualTo(expected.BoneTransforms[1].Translation));
        Assert.That(actual.BoneTransforms[1].Translation.X, Is.GreaterThan(1));

        clock = new(0.4f, 0.4, 2, 0, false, false);
        var replayed = AnimationSampler.Sample(0.2f, skeleton, primary, [rule]);
        var freshRule = new ThreeKingdomsPropWindRule(skeleton, primary, null, -1, false,
            () => Vector2.Zero, () => Matrix.Identity, () => clock);
        var fresh = AnimationSampler.Sample(0.2f, skeleton, primary, [freshRule]);
        Assert.That(replayed.BoneTransforms.Select(bone => bone.WorldTransform),
            Is.EqualTo(fresh.BoneTransforms.Select(bone => bone.WorldTransform)));
    }

    [Test]
    public void ActionMeta_UsesStartRelativeClampedTime()
    {
        var skeleton = CreateSkeleton();
        var primary = Clip(Vector3.Zero);
        primary.DynamicFrames[1].Position[1] = new(10, 0, 0);
        var clock = new AnimationPlaybackTime(2.9f, 10.9, 1, 2, false, false);
        var rule = new ThreeKingdomsPropWindRule(skeleton, primary, null, -1, false,
            () => Vector2.Zero, () => Matrix.Identity, () => clock, loop: false, startTime: 0.5f);
        var actual = AnimationSampler.Sample(0, skeleton, primary, [rule]);
        Assert.That(actual.BoneTransforms[1].Translation, Is.EqualTo(new Vector3(10, 0, 0)));
        clock = new(0.2f, 0.2, 2, 0, false, false);
        actual = AnimationSampler.Sample(0, skeleton, primary, [rule]);
        Assert.That(actual.BoneTransforms[1].Translation, Is.EqualTo(Vector3.Zero));
    }

    [TestCase(-1, 1f, true)]
    [TestCase(1, 0.1f, true)]
    [TestCase(1, 0.2f, false)]
    public void WindGate_RequiresExactBoneAndNativeMinimumForce(int bone, float strength, bool alternateExpected)
    {
        var skeleton = CreateSkeleton();
        var primary = Clip(new(2, 0, 0));
        var alternate = Clip(new(0, 3, 0));
        var rule = new ThreeKingdomsPropWindRule(skeleton, primary, alternate, bone, false,
            () => new(strength, 0), () => Matrix.Identity,
            () => new(0.25f, 0.25, 1, 0, false, false));
        var actual = AnimationSampler.Sample(0.25f, skeleton, primary, [rule]);
        Assert.That(rule.UsesAlternate, Is.EqualTo(alternateExpected));
        Assert.That(actual.BoneTransforms[1].Translation,
            Is.EqualTo(alternateExpected ? new Vector3(0, 3, 0) : new Vector3(2, 0, 0)));
        Assert.That(actual.BoneTransforms[0].Rotation, Is.EqualTo(Quaternion.Identity));
    }

    [Test]
    public void OfficialCompleteMeta_PreservesWindBoneAndAlternateAnimation()
    {
        // data.pack, inf_flag_default_persistent_metadata_alive_0.anm.meta, SHA256 e858e14a...
        var original = Convert.FromBase64String(OfficialMeta);
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        var document = parser.ParseFile(original);
        var prop = document.Attributes.OfType<AnimatedProp_v13_3K>().Single();
        Assert.That(prop.MetaData, Is.EqualTo("flag_base_0"));
        Assert.That(prop.PathToAnotherAnim,
            Is.EqualTo("animations/battle/character/male01/infantry/inf_flag/default/flag/flag_persistent_collapse01.anim"));
        Assert.That(prop, Is.InstanceOf<IThreeKingdomsAnimatedPropWindMeta>());
        Assert.That(parser.GenerateBytes(document.Version, document), Is.EqualTo(original));
    }

    [Test]
    public void AttachmentModeTwo_UsesNativeIndependentTranslationAndQuaternionNormalization()
    {
        var skeleton = CreateSkeleton(new(0.5f, 0.5f, 0.5f, 0.5f), new(0.25f, -0.5f, 1));
        var host = Matrix.CreateRotationZ(0.3f) * Matrix.CreateTranslation(10, 20, 30);
        var actual = ThreeKingdomsPropWindRule.CreateAttachmentTransform(skeleton, new(2, 3, 4),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathHelper.PiOver2), host);
        // Native 0x2670e0f..0x2671097: inverse-root rotates (position-bindPosition),
        // while orientation * inverse(root) becomes -90 degrees around Y for these inputs.
        var expected = Matrix.CreateRotationY(-MathHelper.PiOver2) *
            Matrix.CreateTranslation(3.5f, 3, 1.75f) * host;
        AssertMatrix(actual, expected);
    }

    [TestCase(0)]
    [TestCase(1)]
    public void ProductionAttachmentAndSampler_PreserveOfficialBindRootAndNativeFinalWorld(int windBone)
    {
        // Both official flag clips have root quaternion shorts (16384,16384,16384,16384).
        var bindRoot = new Quaternion(0.5f, 0.5f, 0.5f, 0.5f);
        var player = new AnimationPlayer { IsEnabled = true };
        var skeleton = CreateSkeleton(bindRoot, player: player);
        var primary = Clip(new(2, 0, 0));
        player.SetAnimation(primary, skeleton);
        var hostSkeleton = CreateSkeleton();
        var host = Mock.Of<ISkeletonProvider>(value => value.Skeleton == hostSkeleton);
        var sceneWorld = Matrix.CreateTranslation(5, 6, 7);
        Matrix Attachment() => ThreeKingdomsPropWindRule.CreateAttachmentTransform(
            skeleton, Vector3.Zero, Quaternion.Identity, Matrix.Identity);
        var clock = new AnimationPlaybackTime(0, 0, 1, 0, true, false);
        var rule = new ThreeKingdomsPropWindRule(skeleton, primary, null, windBone, true,
            () => Vector2.One, () => Attachment() * sceneWorld, () => clock);
        player.AnimationRules.Add(rule);
        player.Pause();
        var node = new GroupNode("native animated prop");
        var instance = new AnimatedPropInstance(node, player, host, 0, () => Vector3.Zero,
            () => Quaternion.Identity, 0, 0, new AnimatedProp_v13_3K(), false, _ => { }, Attachment);
        instance.Update(0);
        clock = clock with { ElapsedSeconds = 3, ClipTimeSeconds = 1, CycleIndex = 1 };
        instance.Update(1);

        var actual = player.GetCurrentAnimationFrame();
        var angle = MathHelper.PiOver4;
        var delta = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -angle);
        // Independent native pose-writer result for parent (.5,.5,.5,.5), local identity.
        var expectedChild = new Quaternion(0, 0, -MathF.Sin(angle / 2), MathF.Cos(angle / 2));
        var expectedRoot = windBone == 0 ? delta * bindRoot : bindRoot;
        var expectedBone = windBone == 0 ? Quaternion.Identity : expectedChild;
        var expectedModelBone = Matrix.CreateFromQuaternion(expectedBone) *
            Matrix.CreateTranslation(2, 0, 0) * Matrix.CreateFromQuaternion(expectedRoot);
        // Caller writes Rx(-angle) into render World and passes Rx(+angle) to matrix generation.
        var nativeRenderWorld = Matrix.CreateRotationX(-angle) * Attachment() * sceneWorld;
        var nativeBoneWorld = expectedModelBone * Matrix.CreateRotationX(angle);
        AssertMatrix(actual.GetSkeletonAnimatedWorld(skeleton, 1) * node.ModelMatrix * sceneWorld,
            nativeBoneWorld * nativeRenderWorld);
        AssertMatrix(node.ModelMatrix, Matrix.CreateFromQuaternion(Quaternion.Inverse(bindRoot)));
        Assert.That(rule.WindHeading, Is.EqualTo(8192));
        Assert.That(Vector3.TransformNormal(Vector3.UnitX, nativeRenderWorld),
            Is.EqualTo(Vector3.TransformNormal(Vector3.UnitX, node.ModelMatrix * sceneWorld)));
        Assert.That(actual.BoneTransforms[0].Rotation, Is.Not.EqualTo(Quaternion.Identity));
    }

    [Test]
    public void PausedAttachmentUpdate_ChangesOnlyOwnNodeWithoutAdvancingWindClock()
    {
        var player = new AnimationPlayer { IsEnabled = true };
        var skeleton = CreateSkeleton(new(0.5f, 0.5f, 0.5f, 0.5f), player: player);
        var primary = Clip(new(2, 0, 0));
        player.SetAnimation(primary, skeleton);
        var hostSkeleton = CreateSkeleton();
        var host = Mock.Of<ISkeletonProvider>(value => value.Skeleton == hostSkeleton);
        Matrix Attachment() => ThreeKingdomsPropWindRule.CreateAttachmentTransform(skeleton,
            Vector3.Zero, Quaternion.Identity, hostSkeleton.GetAnimatedWorldTranform(0));
        var clock = new AnimationPlaybackTime(0, 0, 1, 0, false, false);
        var rule = new ThreeKingdomsPropWindRule(skeleton, primary, null, 1, true,
            () => Vector2.One, Attachment, () => clock);
        player.AnimationRules.Add(rule);
        player.Pause();
        var node = new GroupNode("native prop");
        var unrelated = new GroupNode("ordinary prop") { ModelMatrix = Matrix.CreateTranslation(9, 8, 7) };
        var unrelatedBefore = unrelated.ModelMatrix;
        var instance = new AnimatedPropInstance(node, player, host, 0, () => Vector3.Zero,
            () => Quaternion.Identity, 0, 0, new AnimatedProp_v13_3K(), false, _ => { }, Attachment);
        instance.Update(0);
        var poseBefore = player.GetCurrentAnimationFrame().BoneTransforms.Select(bone => bone.WorldTransform).ToArray();
        hostSkeleton.Translation[0] = new(3, 4, 5);
        hostSkeleton.RebuildSkeletonMatrix();
        instance.Update(0);
        AssertMatrix(node.ModelMatrix, Matrix.CreateFromQuaternion(Quaternion.Inverse(skeleton.Rotation[0])) *
            Matrix.CreateTranslation(3, 4, 5));
        Assert.That(player.GetCurrentAnimationFrame().BoneTransforms.Select(bone => bone.WorldTransform),
            Is.EqualTo(poseBefore));
        Assert.That(rule.WindHeading, Is.Zero);
        Assert.That(clock.ElapsedSeconds, Is.Zero);
        Assert.That(unrelated.ModelMatrix, Is.EqualTo(unrelatedBefore));
    }

    [Test]
    public void AttachmentModeTwo_WithoutWindPreservesExistingZeroOffsetPropWorld()
    {
        var bindRoot = new Quaternion(0.5f, 0.5f, 0.5f, 0.5f);
        var skeleton = CreateSkeleton(bindRoot);
        var hostSkeleton = CreateSkeleton(Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f), new(3, 4, 5));
        var host = Mock.Of<ISkeletonProvider>(value => value.Skeleton == hostSkeleton);
        var clip = Clip(new(2, 0, 0));
        foreach (var frame in clip.DynamicFrames)
            frame.Rotation[0] = bindRoot;
        var attachment = ThreeKingdomsPropWindRule.CreateAttachmentTransform(skeleton, Vector3.Zero,
            Quaternion.Identity, hostSkeleton.GetAnimatedWorldTranform(0));
        var rule = new ThreeKingdomsPropWindRule(skeleton, clip, null, -1, false,
            () => Vector2.Zero, () => attachment, () => new(0, 0, 1, 0, false, false));
        var native = AnimationSampler.Sample(0f, skeleton, clip, [rule]);
        var ordinary = AnimationSampler.Sample(0f, skeleton, clip,
            [new CopyRootTransform(host, 0, Vector3.Zero, Quaternion.Identity)]);
        for (var index = 0; index < skeleton.BoneCount; index++)
            AssertMatrix(native.GetSkeletonAnimatedWorld(skeleton, index) * attachment,
                ordinary.GetSkeletonAnimatedWorld(skeleton, index));
        Assert.That(new AnimatedProp_v14(), Is.Not.InstanceOf<IThreeKingdomsAnimatedPropWindMeta>());
    }

    private static void AssertMatrix(Matrix actual, Matrix expected)
    {
        foreach (var point in new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
            Assert.That(Vector3.Distance(Vector3.Transform(point, actual), Vector3.Transform(point, expected)),
                Is.LessThan(0.00001f));
    }

    private static AnimationClip Clip(Vector3 position)
    {
        var clip = new AnimationClip { Duration = TimeSpan.FromSeconds(2) };
        for (var index = 0; index < 2; index++)
            clip.DynamicFrames.Add(new()
            {
                Position = [Vector3.Zero, position, Vector3.UnitY],
                Rotation = [Quaternion.Identity, Quaternion.Identity, Quaternion.Identity],
                Scale = [Vector3.One, Vector3.One, Vector3.One],
            });
        return clip;
    }

    private static GameSkeleton CreateSkeleton(Quaternion? rootRotation = null, Vector3? rootPosition = null,
        AnimationPlayer? player = null)
    {
        var file = new AnimationFile
        {
            Header = new() { SkeletonName = "prop_wind_test" },
            Bones = [new() { Id = 0, Name = "root", ParentId = -1 },
                new() { Id = 1, Name = "wind_pivot", ParentId = 0 },
                new() { Id = 2, Name = "cloth", ParentId = 1 }],
        };
        var part = new AnimationFile.AnimationPart();
        var frame = new AnimationFile.Frame();
        for (var index = 0; index < 3; index++)
        {
            part.TranslationMappings.Add(new(index));
            part.RotationMappings.Add(new(index));
            frame.Transforms.Add(new RmvVector3());
            frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
        }
        var rotation = rootRotation ?? Quaternion.Identity;
        frame.Quaternion[0] = new RmvVector4(rotation.X, rotation.Y, rotation.Z, rotation.W);
        var position = rootPosition ?? Vector3.Zero;
        frame.Transforms[0] = new RmvVector3(position.X, position.Y, position.Z);
        part.DynamicFrames.Add(frame);
        file.AnimationParts.Add(part);
        return GameSkeleton.CreateFromAnimationFile(file, player ?? new AnimationPlayer());
    }

    private static IEnumerable<TestCaseData> NativeAngles() =>
        NativeAngleRows.Select(row => new TestCaseData(row.Cast<object>().ToArray()));

    // Official EXE SHA1 5fec65f1f095faf4f528488f66da6fbbce93aef8, RVA 0x2668a50.
    // factor bits, correction, heading, gate, wind X/Z, forward X/Z, result correction/heading/float bits.
    private static readonly int[][] NativeAngleRows =
    [
        [0, 0, 0, 1, 1, 1, 1, 1, 57344, 0, -1085730853],
        [0, 0, 0, 1, -1, 1, 1, 1, 57344, 0, -1085730853],
        [0, 0, 0, 1, 1, -1, 1, 1, 57344, 0, -1085730853],
        [0, 0, 0, 1, -1, -1, 1, 1, 57344, 0, -1085730853],
        [0, 0, 8192, 1, 1, 1, 1, 1, 0, 8192, 0],
        [0, 0, 8192, 1, -1, 1, 1, 1, 0, 8192, 0],
        [0, 0, 8192, 1, 1, -1, 1, 1, 0, 8192, 0],
        [0, 0, 8192, 1, -1, -1, 1, 1, 0, 8192, 0],
        [0, 0, 32768, 1, 1, 1, 1, 1, 24576, 32768, 1075235812],
        [0, 0, 32768, 1, -1, 1, 1, 1, 24576, 32768, 1075235812],
        [0, 0, 32768, 1, 1, -1, 1, 1, 24576, 32768, 1075235812],
        [0, 0, 32768, 1, -1, -1, 1, 1, 24576, 32768, 1075235812],
        [0, 0, 40960, 1, 1, 1, 1, 1, 32768, 40960, 1078530011],
        [0, 0, 40960, 1, -1, 1, 1, 1, 32768, 40960, 1078530011],
        [0, 0, 40960, 1, 1, -1, 1, 1, 32768, 40960, 1078530011],
        [0, 0, 40960, 1, -1, -1, 1, 1, 32768, 40960, 1078530011],
        [0, 0, 65535, 1, 1, 1, 1, 1, 57343, 65535, -1085729245],
        [0, 0, 65535, 1, -1, 1, 1, 1, 57343, 65535, -1085729245],
        [0, 0, 65535, 1, 1, -1, 1, 1, 57343, 65535, -1085729245],
        [0, 0, 65535, 1, -1, -1, 1, 1, 57343, 65535, -1085729245],
        [1036831949, 0, 0, 1, 1, 1, 1, 1, 58163, 819, -1087048211],
        [1036831949, 0, 0, 1, -1, 1, 1, 1, 56525, 64717, -1084413495],
        [1036831949, 0, 0, 1, 1, -1, 1, 1, 59802, 2458, -1089684535],
        [1036831949, 0, 0, 1, -1, -1, 1, 1, 54886, 63078, -1081953802],
        [1036831949, 0, 8192, 1, 1, 1, 1, 1, 0, 8192, 0],
        [1036831949, 0, 8192, 1, -1, 1, 1, 1, 63898, 6554, -1105146002],
        [1036831949, 0, 8192, 1, 1, -1, 1, 1, 1638, 9830, 1042337646],
        [1036831949, 0, 8192, 1, -1, -1, 1, 1, 3277, 11469, 1050729471],
        [1036831949, 0, 32768, 1, 1, 1, 1, 1, 22118, 30310, 1074247392],
        [1036831949, 0, 32768, 1, -1, 1, 1, 1, 27034, 35226, 1076224233],
        [1036831949, 0, 32768, 1, 1, -1, 1, 1, 23757, 31949, 1074906473],
        [1036831949, 0, 32768, 1, -1, -1, 1, 1, 25395, 33587, 1075565152],
        [1036831949, 0, 40960, 1, 1, 1, 1, 1, 36045, 44237, -1070271397],
        [1036831949, 0, 40960, 1, -1, 1, 1, 1, 34406, 42598, -1069612316],
        [1036831949, 0, 40960, 1, 1, -1, 1, 1, 31130, 39322, 1077871332],
        [1036831949, 0, 40960, 1, -1, -1, 1, 1, 32768, 40960, 1078530011],
        [1036831949, 0, 65535, 1, 1, 1, 1, 1, 58162, 818, -1087046602],
        [1036831949, 0, 65535, 1, -1, 1, 1, 1, 56524, 64716, -1084411887],
        [1036831949, 0, 65535, 1, 1, -1, 1, 1, 59801, 2457, -1089682926],
        [1036831949, 0, 65535, 1, -1, -1, 1, 1, 54885, 63077, -1081952997],
        [1056964608, 0, 0, 1, 1, 1, 1, 1, 61440, 4096, -1094119461],
        [1056964608, 0, 0, 1, -1, 1, 1, 1, 53248, 61440, -1080636444],
        [1056964608, 0, 0, 1, 1, -1, 1, 1, 4096, 12288, 1053364187],
        [1056964608, 0, 0, 1, -1, -1, 1, 1, 45056, 53248, -1074048046],
        [1056964608, 0, 8192, 1, 1, 1, 1, 1, 0, 8192, 0],
        [1056964608, 0, 8192, 1, -1, 1, 1, 1, 57344, 0, -1085730853],
        [1056964608, 0, 8192, 1, 1, -1, 1, 1, 8192, 16384, 1061752795],
        [1056964608, 0, 8192, 1, -1, -1, 1, 1, 16384, 24576, 1070141403],
        [1056964608, 0, 32768, 1, 1, 1, 1, 1, 12288, 20480, 1066847204],
        [1056964608, 0, 32768, 1, -1, 1, 1, 1, 36864, 45056, -1070600736],
        [1056964608, 0, 32768, 1, 1, -1, 1, 1, 20480, 28672, 1073435602],
        [1056964608, 0, 32768, 1, -1, -1, 1, 1, 28672, 36864, 1076882912],
        [1056964608, 0, 40960, 1, 1, 1, 1, 1, 49152, 57344, -1077342245],
        [1056964608, 0, 40960, 1, -1, 1, 1, 1, 40960, 49152, -1072247836],
        [1056964608, 0, 40960, 1, 1, -1, 1, 1, 24576, 32768, 1075235812],
        [1056964608, 0, 40960, 1, -1, -1, 1, 1, 32768, 40960, 1078530011],
        [1056964608, 0, 65535, 1, 1, 1, 1, 1, 61440, 4096, -1094119461],
        [1056964608, 0, 65535, 1, -1, 1, 1, 1, 53247, 61439, -1080635640],
        [1056964608, 0, 65535, 1, 1, -1, 1, 1, 4096, 12288, 1053364187],
        [1056964608, 0, 65535, 1, -1, -1, 1, 1, 45055, 53247, -1074047242],
        [1065353216, 0, 0, 1, 1, 1, 1, 1, 0, 8192, 0],
        [1065353216, 0, 0, 1, -1, 1, 1, 1, 49152, 57344, -1077342245],
        [1065353216, 0, 0, 1, 1, -1, 1, 1, 16384, 24576, 1070141403],
        [1065353216, 0, 0, 1, -1, -1, 1, 1, 32768, 40960, 1078530011],
        [1065353216, 0, 8192, 1, 1, 1, 1, 1, 0, 8192, 0],
        [1065353216, 0, 8192, 1, -1, 1, 1, 1, 49152, 57344, -1077342245],
        [1065353216, 0, 8192, 1, 1, -1, 1, 1, 16384, 24576, 1070141403],
        [1065353216, 0, 8192, 1, -1, -1, 1, 1, 32768, 40960, 1078530011],
        [1065353216, 0, 32768, 1, 1, 1, 1, 1, 0, 8192, 0],
        [1065353216, 0, 32768, 1, -1, 1, 1, 1, 49152, 57344, -1077342245],
        [1065353216, 0, 32768, 1, 1, -1, 1, 1, 16384, 24576, 1070141403],
        [1065353216, 0, 32768, 1, -1, -1, 1, 1, 32768, 40960, 1078530011],
        [1065353216, 0, 40960, 1, 1, 1, 1, 1, 0, 8192, 0],
        [1065353216, 0, 40960, 1, -1, 1, 1, 1, 49152, 57344, -1077342245],
        [1065353216, 0, 40960, 1, 1, -1, 1, 1, 16384, 24576, 1070141403],
        [1065353216, 0, 40960, 1, -1, -1, 1, 1, 32768, 40960, 1078530011],
        [1065353216, 0, 65535, 1, 1, 1, 1, 1, 0, 8192, 0],
        [1065353216, 0, 65535, 1, -1, 1, 1, 1, 49152, 57344, -1077342245],
        [1065353216, 0, 65535, 1, 1, -1, 1, 1, 16384, 24576, 1070141403],
        [1065353216, 0, 65535, 1, -1, -1, 1, 1, 32768, 40960, 1078530011],
        [1036831949, 0, 8191, 1, 1, 1, -1, -1, 32767, 8191, 1078529609],
        [1036831949, 0, 8193, 1, 1, 1, -1, -1, 32769, 8193, -1068954039],
        [1036831949, 0, 65535, 1, 1, 1, -1, -1, 25394, 818, 1075564750],
        [1056964608, 0, 8191, 1, 1, 1, -1, -1, 32768, 8192, 1078530011],
        [1056964608, 0, 8193, 1, 1, 1, -1, -1, 32768, 8192, 1078530011],
        [1056964608, 0, 65535, 1, 1, 1, -1, -1, 28672, 4096, 1076882912],
        [1065353216, 0, 8191, 1, 1, 1, -1, -1, 32768, 8192, 1078530011],
        [1065353216, 0, 8193, 1, 1, 1, -1, -1, 32768, 8192, 1078530011],
        [1065353216, 0, 65535, 1, 1, 1, -1, -1, 32768, 8192, 1078530011],
        [1056964608, 0, 8192, 0, 1, 1, 1, 1, 0, 8192, 0],
        [1056964608, 1, 8192, 0, 1, 1, 1, 1, 1, 8192, 952700891],
        [1056964608, 32767, 8192, 0, 1, 1, 1, 1, 32767, 8192, 1078529609],
        [1056964608, 32768, 8192, 0, 1, 1, 1, 1, 32768, 8192, 1078530011],
        [1056964608, 32769, 8192, 0, 1, 1, 1, 1, 32769, 8192, -1068954039],
        [1056964608, 65535, 8192, 0, 1, 1, 1, 1, 65535, 8192, -1194782757],
    ];

    private const string OfficialMeta = "AgAAAAUAAAANAEFOSU1BVEVEX1BST1ANAAAAAAAAAAAAAAAAAA4AAAAAXABWYXJpYW50TWVzaGVzL19WYXJpYW50TW9kZWxzL3Byb3BzL2NoaW5lc2Uvd2VpZ2h0ZWQvcF9mbGFnMDFfbWFsZTAxL2hfc3F1YXJlX2ZsYWdfMDEud3Ntb2RlbFkAYW5pbWF0aW9ucy9iYXR0bGUvY2hhcmFjdGVyL21hbGUwMS9pbmZhbnRyeS9pbmZfZmxhZy9kZWZhdWx0L2ZsYWcvZmxhZ19wZXJzaXN0ZW50XzAxLmFuaW0AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIA/AgAAAAIAAAABAAAAAAAAAAAAAAAAAIA/CwBmbGFnX2Jhc2VfMGEAYW5pbWF0aW9ucy9iYXR0bGUvY2hhcmFjdGVyL21hbGUwMS9pbmZhbnRyeS9pbmZfZmxhZy9kZWZhdWx0L2ZsYWcvZmxhZ19wZXJzaXN0ZW50X2NvbGxhcHNlMDEuYW5pbQoATEhBTkRfUE9TRQoAAAAAAAAAAAAAAAAAAAAAAAMAAAAAAIA/CgBSSEFORF9QT1NFCgAAAAAAAAAAAAAAAAAAAAAAAwAAAAAAgD8PAERPQ0tfRVFQVF9SSEFORAoAAAAAAAAAAAAAAAAACgAAAAEAAAAAAAAAAAAAAA4ARE9DS19FUVBUX0JBQ0sKAAAAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAA=";
}
