using Editors.AnimationMeta.SuperView.Editing;
using Editors.AnimationMeta.SuperView.Visualisation.Rules;
using Microsoft.Xna.Framework;
using Shared.Core.Events;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Test.AnimationMeta;

[TestFixture]
internal class CombatMetaDataEditSessionTests
{
    [TestCase(0, "be_prop_0")]
    [TestCase(1, "be_prop_1")]
    public void DockEquipment_MapsPropBoneIdWithoutOffset(
        int propBoneId,
        string expectedBoneName)
    {
        Assert.That(
            DockEquipmentRule.GetEquipmentBoneName(propBoneId),
            Is.EqualTo(expectedBoneName));
    }

    [Test]
    public void TrackedEffect_UsesBoneFrameForWorldTransformAndLocalStorage()
    {
        var effect = new Effect_v11
        {
            Tracking = true,
            NodeIndex = 4,
            Position = new Vector3(1, 2, 3),
            Orientation = Quaternion.CreateFromAxisAngle(
                Vector3.UnitX,
                MathHelper.PiOver4).ToVector4(),
        };
        var boneFrame = Matrix.CreateRotationY(MathHelper.PiOver2) *
            Matrix.CreateTranslation(10, 20, 30);
        var session = new CombatMetaDataEditSession(new TestEventHub());
        var owner = new object();

        Assert.That(
            session.SetTarget(
                owner,
                effect,
                CombatMetaDataPoint.Position,
                () => boneFrame),
            Is.True);

        var expectedWorldPosition = Vector3.Transform(
            effect.Position,
            boneFrame);
        AssertVector(
            session.CurrentWorldPosition!.Value,
            expectedWorldPosition);

        var originalLocalPosition = effect.Position;
        var worldDelta = new Vector3(2, 0, 0);
        var expectedLocalDelta = Vector3.TransformNormal(
            worldDelta,
            Matrix.Invert(boneFrame));
        Assert.That(session.BeginGesture(), Is.True);
        Assert.That(session.Translate(worldDelta), Is.True);
        AssertVector(
            effect.Position,
            originalLocalPosition + expectedLocalDelta);
        session.CancelGesture();

        var originalWorldOrientation = session.CurrentWorldOrientation!.Value;
        var worldRotationDelta = Matrix.CreateRotationZ(MathHelper.PiOver4);
        Assert.That(
            session.BeginGesture(CombatMetaDataTransformMode.Rotate),
            Is.True);
        Assert.That(session.Rotate(worldRotationDelta), Is.True);

        var expectedWorldRotation = Matrix.CreateFromQuaternion(
            originalWorldOrientation) * worldRotationDelta;
        AssertDirection(
            session.CurrentWorldOrientation!.Value,
            expectedWorldRotation);
    }

    [Test]
    public void EffectTransform_CanMoveRotateUndoAndRedo()
    {
        var effect = new Effect_v11
        {
            Position = new Vector3(1, 2, 3),
            Orientation = new Vector4(0, 0, 0, 1),
        };
        var session = new CombatMetaDataEditSession(new TestEventHub());
        var owner = new object();

        Assert.That(
            session.SetTarget(owner, effect, CombatMetaDataPoint.Position),
            Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(session.CurrentPosition, Is.EqualTo(effect.Position));
            Assert.That(session.CurrentOrientation, Is.Not.Null);
            Assert.That(session.CanRotate, Is.True);
        });

        Assert.That(session.BeginGesture(), Is.True);
        Assert.That(session.Translate(new Vector3(4, 0, 0)), Is.True);
        Assert.That(session.EndGesture(), Is.True);

        var rotation = Matrix.CreateRotationY(MathHelper.PiOver2);
        Assert.That(
            session.BeginGesture(CombatMetaDataTransformMode.Rotate),
            Is.True);
        Assert.That(session.Rotate(rotation), Is.True);
        Assert.That(session.EndGesture(), Is.True);

        var rotatedOrientation = new Quaternion(effect.Orientation);
        var rotatedXAxis = Vector3.Transform(
            Vector3.UnitX,
            rotatedOrientation);
        var expectedXAxis = Vector3.Transform(Vector3.UnitX, rotation);
        Assert.Multiple(() =>
        {
            Assert.That(rotatedXAxis.X, Is.EqualTo(expectedXAxis.X).Within(0.0001f));
            Assert.That(rotatedXAxis.Y, Is.EqualTo(expectedXAxis.Y).Within(0.0001f));
            Assert.That(rotatedXAxis.Z, Is.EqualTo(expectedXAxis.Z).Within(0.0001f));
        });

        Assert.That(session.Undo(), Is.True);
        Assert.That(
            new Quaternion(effect.Orientation),
            Is.EqualTo(Quaternion.Identity));
        Assert.That(session.Undo(), Is.True);
        Assert.That(effect.Position, Is.EqualTo(new Vector3(1, 2, 3)));

        Assert.That(session.Redo(), Is.True);
        Assert.That(session.Redo(), Is.True);
        Assert.That(effect.Position, Is.EqualTo(new Vector3(5, 2, 3)));
    }

    [TestCaseSource(nameof(SinglePointTargets))]
    public void SinglePointDrag_CanCancelCommitUndoAndRedo(
        ParsedMetadataAttribute source,
        Func<ParsedMetadataAttribute, Vector3> readPosition)
    {
        var session = new CombatMetaDataEditSession(new TestEventHub());
        var owner = new object();
        var start = readPosition(source);
        var delta = new Vector3(1, 2, 3);

        Assert.That(
            session.SetTarget(owner, source, CombatMetaDataPoint.Position),
            Is.True);

        Assert.That(session.BeginGesture(), Is.True);
        Assert.That(session.Translate(delta), Is.True);
        Assert.That(readPosition(source), Is.EqualTo(start + delta));
        session.CancelGesture();

        Assert.Multiple(() =>
        {
            Assert.That(readPosition(source), Is.EqualTo(start));
            Assert.That(session.CanUndo, Is.False);
            Assert.That(session.HasUnsavedChanges, Is.False);
        });

        Assert.That(session.BeginGesture(), Is.True);
        Assert.That(session.Translate(delta), Is.True);
        Assert.That(session.EndGesture(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(readPosition(source), Is.EqualTo(start + delta));
            Assert.That(session.CanUndo, Is.True);
            Assert.That(session.HasUnsavedChanges, Is.True);
        });

        Assert.That(session.Undo(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(readPosition(source), Is.EqualTo(start));
            Assert.That(session.HasUnsavedChanges, Is.False);
            Assert.That(session.CanRedo, Is.True);
        });

        Assert.That(session.Redo(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(readPosition(source), Is.EqualTo(start + delta));
            Assert.That(session.HasUnsavedChanges, Is.True);
        });
    }

    [TestCaseSource(nameof(SplashTargets))]
    public void SplashDrag_EditsStartAndEndIndependently(
        ParsedMetadataAttribute source,
        Func<ParsedMetadataAttribute, Vector3> readStart,
        Func<ParsedMetadataAttribute, Vector3> readEnd)
    {
        var session = new CombatMetaDataEditSession(new TestEventHub());
        var owner = new object();
        var originalStart = readStart(source);
        var originalEnd = readEnd(source);
        var startDelta = new Vector3(1, 0, 0);
        var endDelta = new Vector3(0, 2, 0);

        Assert.That(
            session.SetTarget(owner, source, CombatMetaDataPoint.SplashStart),
            Is.True);
        Assert.That(session.BeginGesture(), Is.True);
        Assert.That(session.Translate(startDelta), Is.True);
        Assert.That(session.EndGesture(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(readStart(source), Is.EqualTo(originalStart + startDelta));
            Assert.That(readEnd(source), Is.EqualTo(originalEnd));
        });

        Assert.That(
            session.SetTarget(owner, source, CombatMetaDataPoint.SplashEnd),
            Is.True);
        Assert.That(session.BeginGesture(), Is.True);
        Assert.That(session.Translate(endDelta), Is.True);
        Assert.That(session.EndGesture(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(readStart(source), Is.EqualTo(originalStart + startDelta));
            Assert.That(readEnd(source), Is.EqualTo(originalEnd + endDelta));
        });

        Assert.That(session.Undo(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(readStart(source), Is.EqualTo(originalStart + startDelta));
            Assert.That(readEnd(source), Is.EqualTo(originalEnd));
        });

        Assert.That(session.Undo(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(readStart(source), Is.EqualTo(originalStart));
            Assert.That(readEnd(source), Is.EqualTo(originalEnd));
            Assert.That(session.HasUnsavedChanges, Is.False);
        });
    }

    static IEnumerable<TestCaseData> SinglePointTargets()
    {
        yield return Case(
            new ImpactPosition_v2 { Position = new Vector3(1, 1, 1) },
            value => ((ImpactPosition_v2)value).Position,
            "Impact v2");
        yield return Case(
            new ImpactPosition_v10 { Position = new Vector3(2, 2, 2) },
            value => ((ImpactPosition_v10)value).Position,
            "Impact v10");
        yield return Case(
            new ImpactDirectionPosition_v10 { Position = new Vector3(3, 3, 3) },
            value => ((ImpactDirectionPosition_v10)value).Position,
            "Impact direction v10");
        yield return Case(
            new TargetPos_0 { Position = new Vector3(4, 4, 4) },
            value => ((TargetPos_0)value).Position,
            "Target v0");
        yield return Case(
            new TargetPos_10 { Position = new Vector3(5, 5, 5) },
            value => ((TargetPos_10)value).Position,
            "Target v10");
        yield return Case(
            new FirePos_v0 { Position = new Vector3(6, 6, 6) },
            value => ((FirePos_v0)value).Position,
            "Fire v0");
        yield return Case(
            new FirePos_v2 { Position = new Vector3(7, 7, 7) },
            value => ((FirePos_v2)value).Position,
            "Fire v2");
        yield return Case(
            new FirePos_v10 { Position = new Vector3(8, 8, 8) },
            value => ((FirePos_v10)value).Position,
            "Fire v10");
    }

    static IEnumerable<TestCaseData> SplashTargets()
    {
        yield return SplashCase(
            new SplashAttack_v3
            {
                StartPosition = new Vector3(1, 2, 3),
                EndPosition = new Vector3(4, 5, 6)
            },
            value => ((SplashAttack_v3)value).StartPosition,
            value => ((SplashAttack_v3)value).EndPosition,
            "Splash v3");
        yield return SplashCase(
            new SplashAttack_v10
            {
                StartPosition = new Vector3(2, 3, 4),
                EndPosition = new Vector3(5, 6, 7)
            },
            value => ((SplashAttack_v10)value).StartPosition,
            value => ((SplashAttack_v10)value).EndPosition,
            "Splash v10");
        yield return SplashCase(
            new SplashAttack_v11
            {
                StartPosition = new Vector3(3, 4, 5),
                EndPosition = new Vector3(6, 7, 8)
            },
            value => ((SplashAttack_v11)value).StartPosition,
            value => ((SplashAttack_v11)value).EndPosition,
            "Splash v11");
    }

    static TestCaseData Case(
        ParsedMetadataAttribute source,
        Func<ParsedMetadataAttribute, Vector3> readPosition,
        string name) =>
        new(source, readPosition) { TestName = name };

    static TestCaseData SplashCase(
        ParsedMetadataAttribute source,
        Func<ParsedMetadataAttribute, Vector3> readStart,
        Func<ParsedMetadataAttribute, Vector3> readEnd,
        string name) =>
        new(source, readStart, readEnd) { TestName = name };

    private static void AssertVector(Vector3 actual, Vector3 expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.0001f));
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.0001f));
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(0.0001f));
        });
    }

    private static void AssertDirection(
        Quaternion actual,
        Matrix expected)
    {
        var actualDirection = Vector3.Transform(Vector3.UnitX, actual);
        var expectedDirection = Vector3.TransformNormal(
            Vector3.UnitX,
            expected);
        AssertVector(actualDirection, expectedDirection);
    }

    private sealed class TestEventHub : IEventHub
    {
        private readonly Dictionary<Type, List<Delegate>> _subscribers = new();

        public void PublishGlobalEvent<T>(T value) => Publish(value);

        public void Publish<T>(T value)
        {
            if (!_subscribers.TryGetValue(typeof(T), out var subscribers))
                return;

            foreach (var subscriber in subscribers)
                ((Action<T>)subscriber)(value);
        }

        public void Register<T>(object owner, Action<T> action)
        {
            if (!_subscribers.TryGetValue(typeof(T), out var subscribers))
            {
                subscribers = [];
                _subscribers.Add(typeof(T), subscribers);
            }

            subscribers.Add(action);
        }

        public void UnRegister(object owner)
        {
        }
    }
}
