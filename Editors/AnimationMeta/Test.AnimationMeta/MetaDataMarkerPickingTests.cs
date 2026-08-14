using Editors.AnimationMeta.SuperView.Inspection;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Test.AnimationMeta;

[TestFixture]
public sealed class MetaDataMarkerPickingTests
{
    [Test]
    public void Pick_PrefersDistanceFromRayThenStableIndexOrder()
    {
        var fartherFromRay = CreateCandidate(
            new Vector3(0.1f, 0, 3),
            stableOrder: 0);
        var nearerToRay = CreateCandidate(
            new Vector3(0.05f, 0, 8),
            stableOrder: 1);
        var ray = new Ray(Vector3.Zero, Vector3.UnitZ);

        var nearest = MetaDataMarkerHitTester.Pick(
            ray,
            [fartherFromRay, nearerToRay],
            useAngularTolerance: false);

        Assert.That(nearest?.Candidate, Is.SameAs(nearerToRay));

        var firstInIndex = CreateCandidate(
            new Vector3(0, 0, 5),
            stableOrder: 2);
        var secondInIndex = CreateCandidate(
            new Vector3(0, 0, 5),
            stableOrder: 3);

        var tied = MetaDataMarkerHitTester.Pick(
            ray,
            [secondInIndex, firstInIndex],
            useAngularTolerance: false);

        Assert.That(tied?.Candidate, Is.SameAs(firstInIndex));
    }

    [Test]
    public void Pick_IgnoresHiddenOrRemovedMarkersAndKeepsFarMarkersUsable()
    {
        var hidden = CreateCandidate(
            new Vector3(0, 0, 2),
            stableOrder: 0,
            isVisible: false);
        var removed = CreateCandidate(
            new Vector3(0, 0, 3),
            stableOrder: 1);
        var current = CreateCandidate(
            new Vector3(0.5f, 0, 100),
            stableOrder: 2);
        var ray = new Ray(Vector3.Zero, Vector3.UnitZ);

        var hit = MetaDataMarkerHitTester.Pick(
            ray,
            [hidden, current],
            useAngularTolerance: true);

        Assert.Multiple(() =>
        {
            Assert.That(hit?.Candidate, Is.SameAs(current));
            Assert.That(hit?.Candidate, Is.Not.SameAs(removed));
        });
    }

    [Test]
    public void Pick_SplashEndpoint_ReturnsTheSpecificEditPoint()
    {
        var candidate = CreateCandidate(
            new Vector3(1, 0, 3),
            stableOrder: 0,
            hitTargets:
            [
                new MetaDataMarkerHitTarget(
                    new Vector3(0, 0, 3),
                    MetaDataMarkerPoint.SplashStart),
                new MetaDataMarkerHitTarget(
                    new Vector3(2, 0, 3),
                    MetaDataMarkerPoint.SplashEnd),
            ]);

        var hit = MetaDataMarkerHitTester.Pick(
            new Ray(Vector3.Zero, Vector3.UnitZ),
            [candidate],
            useAngularTolerance: false);

        Assert.That(
            hit?.Target.Point,
            Is.EqualTo(MetaDataMarkerPoint.SplashStart));
    }

    [Test]
    public void PointerInteraction_SingleThenDoubleClick_SelectsOnceAndFocusesOnce()
    {
        var candidate = CreateCandidate(
            new Vector3(0, 0, 3),
            stableOrder: 0);
        var interaction = new MetaDataMarkerPointerInteraction();

        Assert.That(
            interaction.Update(
                TimeSpan.Zero,
                new Vector2(10, 10),
                isPressed: true,
                isReleased: false,
                isBlocked: false,
                CreateHit(candidate)).Kind,
            Is.EqualTo(MetaDataMarkerPointerActionKind.None));
        var firstClick = interaction.Update(
            TimeSpan.FromMilliseconds(20),
            new Vector2(10, 10),
            isPressed: false,
            isReleased: true,
            isBlocked: false,
            CreateHit(candidate));

        interaction.Update(
            TimeSpan.FromMilliseconds(100),
            new Vector2(10, 10),
            isPressed: true,
            isReleased: false,
            isBlocked: false,
            CreateHit(candidate));
        var secondClick = interaction.Update(
            TimeSpan.FromMilliseconds(120),
            new Vector2(10, 10),
            isPressed: false,
            isReleased: true,
            isBlocked: false,
            CreateHit(candidate));

        Assert.Multiple(() =>
        {
            Assert.That(
                firstClick.Kind,
                Is.EqualTo(MetaDataMarkerPointerActionKind.Select));
            Assert.That(firstClick.Hit?.Candidate, Is.SameAs(candidate));
            Assert.That(
                secondClick.Kind,
                Is.EqualTo(MetaDataMarkerPointerActionKind.Focus));
            Assert.That(secondClick.Hit?.Candidate, Is.SameAs(candidate));
        });
    }

    [Test]
    public void PointerInteraction_ClickWithoutMarker_ClearsSelection()
    {
        var interaction = new MetaDataMarkerPointerInteraction();

        interaction.Update(
            TimeSpan.Zero,
            Vector2.Zero,
            isPressed: true,
            isReleased: false,
            isBlocked: false,
            hit: null);
        var action = interaction.Update(
            TimeSpan.FromMilliseconds(20),
            Vector2.Zero,
            isPressed: false,
            isReleased: true,
            isBlocked: false,
            hit: null);

        Assert.That(
            action.Kind,
            Is.EqualTo(MetaDataMarkerPointerActionKind.ClearSelection));
    }

    [Test]
    public void PointerInteraction_DifferentSplashEndpoints_DoNotDoubleClick()
    {
        var candidate = CreateCandidate(
            new Vector3(0, 0, 3),
            stableOrder: 0);
        var interaction = new MetaDataMarkerPointerInteraction();
        var startHit = CreateHit(
            candidate,
            MetaDataMarkerPoint.SplashStart);
        var endHit = CreateHit(
            candidate,
            MetaDataMarkerPoint.SplashEnd);

        interaction.Update(
            TimeSpan.Zero,
            Vector2.Zero,
            isPressed: true,
            isReleased: false,
            isBlocked: false,
            startHit);
        interaction.Update(
            TimeSpan.FromMilliseconds(20),
            Vector2.Zero,
            isPressed: false,
            isReleased: true,
            isBlocked: false,
            startHit);
        interaction.Update(
            TimeSpan.FromMilliseconds(100),
            Vector2.Zero,
            isPressed: true,
            isReleased: false,
            isBlocked: false,
            endHit);
        var secondClick = interaction.Update(
            TimeSpan.FromMilliseconds(120),
            Vector2.Zero,
            isPressed: false,
            isReleased: true,
            isBlocked: false,
            endHit);

        Assert.That(
            secondClick.Kind,
            Is.EqualTo(MetaDataMarkerPointerActionKind.Select));
    }

    [Test]
    public void PointerInteraction_GizmoOrCameraOwnershipCancelsPendingClick()
    {
        var candidate = CreateCandidate(
            new Vector3(0, 0, 3),
            stableOrder: 0);
        var interaction = new MetaDataMarkerPointerInteraction();

        interaction.Update(
            TimeSpan.Zero,
            Vector2.Zero,
            isPressed: true,
            isReleased: false,
            isBlocked: false,
            CreateHit(candidate));
        var blocked = interaction.Update(
            TimeSpan.FromMilliseconds(10),
            Vector2.Zero,
            isPressed: false,
            isReleased: false,
            isBlocked: true,
            CreateHit(candidate));
        var released = interaction.Update(
            TimeSpan.FromMilliseconds(20),
            Vector2.Zero,
            isPressed: false,
            isReleased: true,
            isBlocked: false,
            CreateHit(candidate));

        Assert.Multiple(() =>
        {
            Assert.That(
                blocked.Kind,
                Is.EqualTo(MetaDataMarkerPointerActionKind.None));
            Assert.That(
                released.Kind,
                Is.EqualTo(MetaDataMarkerPointerActionKind.None));
        });
    }

    [Test]
    public void MarkerPreview_HoverUsesExistingOutlineAndVisibilityControlsHitTest()
    {
        var source = new ImpactPosition_v10
        {
            Name = "IMPACT_POS",
            Version = 10,
        };
        var root = new GroupNode("root");
        var node = new SimpleDrawableNode("marker");
        root.AddObject(node);
        var visualStates = new List<bool>();
        var preview = new DrawableMetaInstance(
            null,
            node,
            source,
            false,
            visualStates.Add,
            () => Vector3.Zero,
            () => Quaternion.Identity);

        preview.IsHovered = true;
        preview.IsHovered = false;
        preview.IsEnabled = false;

        Assert.Multiple(() =>
        {
            Assert.That(visualStates, Is.EqualTo(new[]
            {
                false,
                true,
                false,
            }));
            Assert.That(preview.IsHitTestVisible, Is.False);
        });

        preview.CleanUp();
        preview.IsEnabled = true;
        Assert.That(preview.IsHitTestVisible, Is.False);
    }

    [Test]
    public void MarkerContract_ExcludesPropAndSharedSelectionTypes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                typeof(IMetaDataMarkerPreview).IsAssignableFrom(
                    typeof(DrawableMetaInstance)),
                Is.True);
            Assert.That(
                typeof(IMetaDataMarkerPreview).IsAssignableFrom(
                    typeof(CombatMetaDataInstance)),
                Is.True);
            Assert.That(
                typeof(IMetaDataMarkerPreview).IsAssignableFrom(
                    typeof(PropInstance)),
                Is.False);
            Assert.That(
                typeof(IMetaDataMarkerPreview).IsAssignableFrom(
                    typeof(AnimatedPropInstance)),
                Is.False);
            Assert.That(
                typeof(MetaDataMarkerPickerComponent)
                    .GetConstructors()
                    .SelectMany(constructor => constructor.GetParameters())
                    .Select(parameter => parameter.ParameterType.FullName)
                    .Where(name => name != null),
                Has.None.Contains("SelectionManager"));
        });
    }

    private static MetaDataMarkerPickCandidate CreateCandidate(
        Vector3 position,
        int stableOrder,
        bool isVisible = true,
        IReadOnlyList<MetaDataMarkerHitTarget>? hitTargets = null)
    {
        var source = new ImpactPosition_v10
        {
            Name = "IMPACT_POS",
            Version = 10,
            Position = position,
        };
        var item = new MetaDataInspectionItem(
            source,
            MetaDataDocumentOwner.Animation,
            true,
            null,
            MetaDataAuthoredTimeStatus.NotApplicable,
            null,
            MetaDataPreviewCapability.Available,
            CombatMetaDataPreviewCategory.Impact,
            position,
            []);
        return new MetaDataMarkerPickCandidate(
            item,
            new TestMarkerPreview(
                source,
                position,
                isVisible,
                hitTargets),
            stableOrder);
    }

    private static MetaDataMarkerHit CreateHit(
        MetaDataMarkerPickCandidate candidate,
        MetaDataMarkerPoint point = MetaDataMarkerPoint.Default) =>
        new(
            candidate,
            new MetaDataMarkerHitTarget(
                candidate.Preview.FocusPosition,
                point));

    private sealed class TestMarkerPreview : IMetaDataMarkerPreview
    {
        private readonly Vector3 _position;

        public ParsedMetadataAttribute Source { get; }
        public bool IsEnabled { get; set; } = true;
        public bool IsSelected { get; set; }
        public bool ShowForEntireAnimation { get; set; }
        public Vector3 FocusPosition => _position;
        public Matrix ReferenceWorldTransform => Matrix.Identity;
        public Matrix WorldTransform => Matrix.CreateTranslation(_position);
        public int? HighlightedBoneIndex => null;
        public bool IsHitTestVisible { get; }
        public bool IsHovered { get; set; }
        public float HitTestRadius => 0.3f;
        public IReadOnlyList<MetaDataMarkerHitTarget> HitTargets { get; }

        public TestMarkerPreview(
            ParsedMetadataAttribute source,
            Vector3 position,
            bool isVisible,
            IReadOnlyList<MetaDataMarkerHitTarget>? hitTargets)
        {
            Source = source;
            _position = position;
            IsHitTestVisible = isVisible;
            HitTargets = hitTargets ??
            [
                new MetaDataMarkerHitTarget(
                    position,
                    MetaDataMarkerPoint.Default),
            ];
        }
    }
}
