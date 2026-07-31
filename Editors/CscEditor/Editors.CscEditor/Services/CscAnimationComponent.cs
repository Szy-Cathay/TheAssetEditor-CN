using System;
using System.Collections.Generic;
using System.Linq;
using Editors.CscEditor.Data;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;

namespace Editors.CscEditor.Services
{
    /// <summary>
    /// Drives scene time and applies each element's evaluated transform/visibility every frame:
    /// world matrices are composed through the attach tree (including the parent model's animated
    /// bone when the element is attached to one) and pushed into the element node plus every
    /// mesh/dummy content node, and each model's skeletal AnimationPlayer is scrubbed to the
    /// timeline. Runs even while paused so gizmo/detail edits show up immediately. Also applies
    /// the look-through-camera view when one is active.
    /// </summary>
    public class CscAnimationComponent : BaseComponent
    {
        readonly CscPlaybackContext _context;
        readonly CscSceneGraphBuilder _sceneBuilder;
        readonly ArcBallCamera _camera;
        readonly RenderEngineComponent _renderEngine;
        readonly GridComponent _gridComponent;
        CscScene? _scene;

        double _captureAccumulator;
        const double CaptureInterval = 0.1; // throttle CPU readback to ~10fps - GetData() stalls the GPU pipeline
        System.Windows.Media.Imaging.WriteableBitmap? _portholeBitmap;

        /// <summary>Cropped-to-square, alpha-preserving snapshot of the current look-through render
        /// (sourced from RenderEngineComponent's pre-composite alpha target, not the final opaque
        /// viewport surface - see CapturePortholeFrame). Null when not looking through a camera.</summary>
        public System.Windows.Media.ImageSource? PortholeLiveFrame { get; private set; }

        /// <summary>Raised whenever <see cref="PortholeLiveFrame"/> is refreshed.</summary>
        public event Action? PortholeFrameUpdated;

        public CscAnimationComponent(CscPlaybackContext context, CscSceneGraphBuilder sceneBuilder,
            ArcBallCamera camera, RenderEngineComponent renderEngine, GridComponent gridComponent)
        {
            _context = context;
            _sceneBuilder = sceneBuilder;
            _camera = camera;
            _renderEngine = renderEngine;
            _gridComponent = gridComponent;
            UpdateOrder = (int)ComponentUpdateOrderEnum.Default;
        }

        public void SetScene(CscScene? scene)
        {
            _scene = scene;
            ClearLookThrough();
        }

        /// <summary>Switches the viewport to look through the given camera element.</summary>
        public void SetLookThrough(int cameraElementId)
        {
            _context.LookThroughElementId = cameraElementId;
            ApplyFrame();
        }

        /// <summary>Returns the viewport to the normal arc-ball view.</summary>
        public void ClearLookThrough()
        {
            _context.LookThroughElementId = -1;
            _camera.ViewMatrixOverride = null;
            _camera.ProjectionMatrixOverride = null;
            _gridComponent.ShowGrid = true;
            _renderEngine.SquareViewport = false;
            PortholeLiveFrame = null;
            PortholeFrameUpdated?.Invoke();
        }

        public override void Update(GameTime gameTime)
        {
            if (_scene == null)
                return;

            if (_context.IsPlaying)
            {
                var time = _context.CurrentTime + (float)gameTime.ElapsedGameTime.TotalSeconds;
                if (time >= _context.Duration)
                {
                    if (_context.Loop)
                    {
                        time = _context.Duration > 0 ? time % _context.Duration : 0;
                    }
                    else
                    {
                        time = _context.Duration;
                        _context.IsPlaying = false;
                    }
                }
                _context.CurrentTime = time;
            }

            ApplyFrame();

            if (_context.LookThroughElementId >= 0)
            {
                _captureAccumulator += gameTime.ElapsedGameTime.TotalSeconds;
                if (_captureAccumulator >= CaptureInterval)
                {
                    _captureAccumulator = 0;
                    CapturePortholeFrame();
                }
            }
        }

        /// <summary>Reads back a centred square crop of RenderEngineComponent's pre-composite
        /// render target (SurfaceFormat.Color - has real alpha, unlike the final viewport surface
        /// D3D11Host shares with WPF, which is SurfaceFormat.Bgr32/opaque) into a WPF bitmap, so the
        /// porthole preview can show real transparency (composited with porthole_back.png in the
        /// view) instead of the 3D view's own opaque grey background. Throttled - GetData() is a
        /// GPU/CPU sync point, too costly to do every frame.</summary>
        void CapturePortholeFrame()
        {
            var frame = _renderEngine.LastFrame;
            if (frame == null || frame.Width <= 0 || frame.Height <= 0)
                return;

            var size = Math.Min(frame.Width, frame.Height);
            var offsetX = (frame.Width - size) / 2;
            var offsetY = (frame.Height - size) / 2;

            var colors = new Color[size * size];
            frame.GetData(0, new Rectangle(offsetX, offsetY, size, size), colors, 0, colors.Length);

            if (_portholeBitmap == null || _portholeBitmap.PixelWidth != size)
                _portholeBitmap = new System.Windows.Media.Imaging.WriteableBitmap(size, size, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);

            var bytes = new byte[size * size * 4];
            for (var i = 0; i < colors.Length; i++)
            {
                var c = colors[i];
                bytes[i * 4 + 0] = c.B;
                bytes[i * 4 + 1] = c.G;
                bytes[i * 4 + 2] = c.R;
                bytes[i * 4 + 3] = c.A;
            }

            _portholeBitmap.WritePixels(new System.Windows.Int32Rect(0, 0, size, size), bytes, size * 4, 0);
            PortholeLiveFrame = _portholeBitmap;
            PortholeFrameUpdated?.Invoke();
        }

        public void ApplyFrame()
        {
            if (_scene == null)
                return;

            var localTimes = BuildTimeContext(_context.CurrentTime);
            DriveSkeletalPlayers(localTimes);

            // GameSkeleton caches the frame it hands out to bone-attach resolvers (attach_point
            // props, RMV2 rigid-bone pieces) in its own field instead of reading the player live.
            // Refresh it once DriveSkeletalPlayers has set each player's frame for this tick, or
            // every such attachment stays frozen at the bind pose while the skinned mesh (which
            // reads the player directly) animates.
            foreach (var content in _sceneBuilder.ModelContents.Values)
                content.Skeleton?.Update();

            var worlds = new Dictionary<int, Matrix>();
            foreach (var element in _scene.DfsOrder(includeNested: true))
                ApplyElementFrame(element, Matrix.Identity, localTimes[element.Id], worlds);

            // Root-ref sub-scenes render under their host element's transform, at their own
            // (possibly looped) local time - see BuildTimeContext. Unlike a normal element's own
            // children (whose visibility never inherits from a parent - see ApplyElementFrame's own
            // remarks), an embedded Composite Scene's entire contents are gated by the ROOT_REF
            // host's own aliveness: the referenced scene isn't a set of independently-timed nodes
            // the user placed here, it's a single embedded unit that's either "there" (host inside
            // its own Begin/End window) or not - so every element inside it is forced hidden
            // whenever the host itself isn't alive, regardless of the sub-element's own window.
            // Cascades through nested root-refs too, via subSceneContentVisible.
            var subSceneContentVisible = new Dictionary<int, bool>();
            foreach (var sub in _sceneBuilder.SubScenes)
            {
                if (!worlds.TryGetValue(sub.Host.Id, out var hostWorld))
                    continue;

                var hostTime = localTimes.GetValueOrDefault(sub.Host.Id, _context.CurrentTime);
                var ancestorVisible = !subSceneContentVisible.TryGetValue(sub.Host.Id, out var parentVisible) || parentVisible;
                var contentVisible = sub.Host.IsAliveAt(hostTime) && ancestorVisible;

                foreach (var element in sub.Scene.DfsOrder(includeNested: true))
                {
                    subSceneContentVisible[element.Id] = contentVisible;
                    ApplyElementFrame(element, hostWorld, localTimes.GetValueOrDefault(element.Id, _context.CurrentTime), worlds, contentVisible);
                }
            }

            if (_context.LookThroughElementId >= 0 &&
                worlds.TryGetValue(_context.LookThroughElementId, out var camWorld) &&
                FindElementIncludingSubScenes(_context.LookThroughElementId) is { Kind: CscElementKind.Camera } camElement)
            {
                var camTime = localTimes.GetValueOrDefault(camElement.Id, _context.CurrentTime);
                ApplyLookThroughCamera(camElement, camWorld, camTime);
            }
        }

        /// <summary>
        /// Resolves, for every element (main scene and every root-ref sub-scene, arbitrarily
        /// nested), the local scene time it should animate at. Aliveness is each element's own
        /// affair (see <see cref="CscElement.IsAliveAt"/>) - a parent outside its own Begin/End
        /// window does NOT hide its children; a child with its own wider window is meant to keep
        /// showing regardless of what its parent is doing. Root-ref sub-scenes get their own
        /// looped local clock: elapsed host time since the ROOT_REF element's Begin (scaled by its
        /// ELEMENT_PERIOD speed multiplier - see <see cref="ComputeSubSceneTime"/>), wrapped by the
        /// referenced scene's own duration - so e.g. a ROOT_REF active for 15s referencing a 1s
        /// scene plays it 15 times instead of once before going inactive. Processes sub-scenes in
        /// <see cref="CscSceneGraphBuilder.SubScenes"/>'s load order, which is guaranteed
        /// outer-before-inner (a sub-scene is appended to the list while its host's own outer
        /// LoadSubScene call is still running), so a host's local time is always already resolved
        /// by the time its own sub-scene is processed.
        /// </summary>
        Dictionary<int, float> BuildTimeContext(float globalTime)
        {
            var localTimes = new Dictionary<int, float>();

            foreach (var element in _scene!.DfsOrder(includeNested: true))
                localTimes[element.Id] = globalTime;

            foreach (var sub in _sceneBuilder.SubScenes)
            {
                if (!localTimes.TryGetValue(sub.Host.Id, out var hostTime))
                    continue;

                var subTime = ComputeSubSceneTime(sub, hostTime);
                foreach (var element in sub.Scene.DfsOrder(includeNested: true))
                    localTimes[element.Id] = subTime;
            }

            return localTimes;
        }

        /// <summary>Referenced scene's own local clock: elapsed time since the host's Begin, scaled
        /// by the host ROOT_REF_ELEMENT's own ELEMENT_PERIOD speed multiplier - the same (flag,
        /// speed, offset) record used for SFX/animation bundles elsewhere (see
        /// <see cref="CscElement.PeriodSpeedMultiplier"/>), not ROOT_REF_ELEMENT's own
        /// game_time_multiplier/scene_time_multiplier type-channels: verified against real files
        /// (extracted .csc corpus) that those two channels sit at their neutral 1.0 in every
        /// authored case, while ELEMENT_PERIOD's speed field carries the real value -
        /// chd_hellshard_drill_01_movedown.csc's RootRef to chd_drill_mainchain_down.csc has
        /// ELEMENT_PERIOD speed 0.024 (matching the observed 7.2s clip stretched to a 300s host
        /// window) with both type-channels at 1.0, and chd_hellshard_drill_01_idle.csc's RootRef to
        /// chd_drill_head_01.csc has ELEMENT_PERIOD speed 0 (freeze) with both type-channels at 1.0
        /// too - so ELEMENT_PERIOD is the actual mechanism, not the named channels. Wrapped by the
        /// referenced scene's own Duration so it loops for as long as the host stays alive.</summary>
        internal static float ComputeSubSceneTime(CscSubScene sub, float hostTime)
        {
            var host = sub.Host;
            var speed = host.PeriodSpeedMultiplier;

            // speed == 0 is a legitimate authored value (freeze the sub-scene on its first frame,
            // e.g. a static prop reusing a .csc that's normally animated) - it must NOT be treated
            // as "unset" and defaulted back to 1 (unlike SampleBindingTime's use of this same field
            // for ANIMATION_ELEMENT clip speed, where 0 does mean "unset"). A negative speed plays
            // the referenced scene in reverse - elapsed then counts down, so this needs floored
            // modulo (not C#'s remainder operator, which returns negative for a negative dividend)
            // to wrap that into a continuous reverse loop through [0, duration) instead of clamping
            // to a permanent freeze at 0.
            var elapsed = (hostTime - host.NormalizedBegin) * speed;

            var duration = sub.Scene.Duration;
            if (duration <= 0)
                return 0;

            return elapsed - duration * MathF.Floor(elapsed / duration);
        }

        /// <summary>Composes one element's world matrix at its own local time <paramref name="t"/>
        /// (attach parent chain, bone attachment) and pushes it into its scene nodes.
        /// <paramref name="rootWorld"/> is the transform parentless elements compose against -
        /// identity for the main scene, the host element's world for root-ref sub-scenes.
        /// A model's apparent ground displacement (walk cycles carrying the character away from
        /// its element origin) never appears here: it lives entirely in the skeletal pose (see
        /// <see cref="ComputePlacedPose"/>), which the skinned mesh and every bone resolver read
        /// directly - so every node, marker and functional origin stays at its authored
        /// transform no matter how far an animation carries the character.
        /// <para/>
        /// Visibility is each element's own <see cref="CscElement.IsAliveAt"/>, not inherited from
        /// its attach-tree parent - a parent outside its own window does not hide children that
        /// are still inside theirs. The one exception is <paramref name="hostVisible"/>, used only
        /// for embedded Composite Scene contents (see the sub-scene loop in <see cref="ApplyFrame"/>),
        /// which forces the whole embedded tree hidden while its ROOT_REF host isn't alive.</summary>
        void ApplyElementFrame(CscElement element, Matrix rootWorld, float t, Dictionary<int, Matrix> worlds, bool hostVisible = true)
        {
            var parentWorld = rootWorld;
            if (element.Parent != null && worlds.TryGetValue(element.Parent.Id, out var pw))
                parentWorld = GetAttachBoneMatrix(element) * pw;

            // This is the element's functional origin - what children (other animation bindings on
            // the same model, bone-attached elements, ...) resolve their own position against.
            var world = element.LocalTransform(t) * parentWorld;
            worlds[element.Id] = world;

            if (!_sceneBuilder.ElementNodes.TryGetValue(element.Id, out var node))
                return;

            node.WorldMatrix = world;
            node.IsVisible = (hostVisible && element.IsAliveAt(t)) || element.Id == _context.SelectedElementId;

            PushWorldToContent(node, world);
        }

        CscElement? FindElementIncludingSubScenes(int elementId)
        {
            if (_scene?.FindElement(elementId) is { } found)
                return found;
            foreach (var sub in _sceneBuilder.SubScenes)
                if (sub.Scene.FindElement(elementId) is { } subFound)
                    return subFound;
            return null;
        }

        /// <summary>Points the viewport camera through a scene camera element - position/forward/up
        /// from its world matrix (local +Z is forward, matching CscCameraFrustumNode) and fov/
        /// near/far from its live channels. Roll spins the up vector around the camera's own local
        /// forward axis before it's carried into world space, so it's independent of world orientation.</summary>
        void ApplyLookThroughCamera(CscElement camElement, Matrix world, float t)
        {
            var position = world.Translation;
            var forward = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, world));
            var roll = camElement.CameraRoll?.Evaluate(t) ?? 0f;
            var localUp = Vector3.TransformNormal(Vector3.Up, Matrix.CreateRotationZ(roll));
            var up = Vector3.Normalize(Vector3.TransformNormal(localUp, world));
            _camera.ViewMatrixOverride = Matrix.CreateLookAt(position, position + forward, up);

            var fovDegrees = CscCameraFrustumNode.EffectiveFovDegrees(camElement, t);
            var near = Math.Max(0.01f, camElement.CameraNear?.Evaluate(t) ?? 0.1f);
            var far = Math.Max(near + 0.1f, camElement.CameraFar?.Evaluate(t) ?? 10000f);

            // Projection uses a fixed 1:1 aspect, so the rasterized viewport must be square too, else the render stretches.
            _renderEngine.SquareViewport = true;
            _camera.ProjectionMatrixOverride =
                Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(fovDegrees), 1f, near, far) * Matrix.CreateScale(-1, 1, 1);

            // The editing grid shouldn't show through the transparent background (see CscEditorView.xaml's overlay).
            _gridComponent.ShowGrid = false;
        }

        /// <summary>World transform of the parent's attach bone (in the parent's local space) when
        /// this element is bone-attached to an animated model; identity otherwise.</summary>
        Matrix GetAttachBoneMatrix(CscElement element)
        {
            if (element.AttachBoneIndex < 0 || element.Parent == null)
                return Matrix.Identity;

            if (!_sceneBuilder.ModelContents.TryGetValue(element.Parent.Id, out var content) || content.Skeleton == null)
                return Matrix.Identity;

            var boneIndex = element.AttachBoneIndex;
            if (boneIndex >= content.Skeleton.BoneCount)
                return Matrix.Identity;

            var frame = content.Player.GetCurrentAnimationFrame();
            return frame != null
                ? frame.GetSkeletonAnimatedWorld(content.Skeleton, boneIndex)
                : content.Skeleton.GetWorldTransform(boneIndex);
        }

        /// <summary>Writes the world matrix into every content node under the element node - the
        /// engine's picking and selection-highlight code read each node's own ModelMatrix and do
        /// not compose parent transforms, so world transforms must live on the leaves. Meshes
        /// resolved to a bone (variantmesh attach_point props, or RMV2's own rigid-bone id for
        /// "building" pieces) get the element world through <see cref="Rmv2MeshNode.AttachmentOuterWorld"/>
        /// instead - one step further out than the bone, not in ModelMatrix, which Render composes
        /// on the near side of the bone transform (see Rmv2MeshNode.Render for why the split
        /// exists: the bone's own transform is local to this model and must land inside the
        /// element's world, not have the element's world land inside it).</summary>
        static void PushWorldToContent(CscElementSceneNode node, Matrix world)
        {
            node.ForeachNodeRecursive(child =>
            {
                if (child is Rmv2MeshNode { AttachmentBoneResolver: not null } attachedMesh)
                    attachedMesh.AttachmentOuterWorld = world;
                else if (child is Rmv2MeshNode or ICscContentDummy)
                    child.ModelMatrix = world;
            });
        }

        /// <summary>
        /// Scrubs each model's skeletal player to its own local scene time, honoring each
        /// animation binding's own Begin/End window and ELEMENT_PERIOD (speed multiplier, time
        /// offset). A model can carry several full-body ANIMATION_ELEMENT children: each one's
        /// pose plays at its own element's authored transform (see <see cref="ComputePlacedPose"/>)
        /// and overlapping windows cross-blend through a nested fold in Begin order (see
        /// <see cref="DriveBaseBindings"/>) instead of one clip abruptly replacing another.
        /// Splice bindings (hand_pose/face_pose etc.) are handled separately afterward as a
        /// per-bone override on top of whatever this produced - see <see cref="ApplySplices"/>.
        /// </summary>
        void DriveSkeletalPlayers(Dictionary<int, float> localTimes)
        {
            foreach (var (elementId, content) in _sceneBuilder.ModelContents)
            {
                if (content.Bindings.Count == 0)
                    continue;

                var sceneTime = localTimes.GetValueOrDefault(elementId, 0f);
                var modelElement = FindElementIncludingSubScenes(elementId);

                var baseBindings = content.Bindings.Where(b => b.Element.SpliceRecord == null).ToList();
                var spliceBindings = content.Bindings.Where(b => b.Element.SpliceRecord != null).ToList();

                DriveBaseBindings(content, baseBindings, modelElement, sceneTime);
                ApplySplices(content, spliceBindings, modelElement, sceneTime);
            }
        }

        /// <summary>Sequences/loops/cross-blends the model's non-splice bindings, driving
        /// content.Player's pose. Each binding's sampled pose is first PLACED - rigidly moved to
        /// its own ANIMATION_ELEMENT's authored transform, advanced by its own completed-loop
        /// root motion (see <see cref="ComputePlacedPose"/>) - and the placed poses are then
        /// folded through a nested crossfade in Begin order, so any depth of authored overlap
        /// (Boris's walk/step/idle chain runs three deep) stays continuous.</summary>
        void DriveBaseBindings(CscModelContent content, List<CscAnimationBinding> bindings, CscElement? modelElement, float sceneTime)
        {
            var ordered = bindings.OrderBy(b => b.Element.NormalizedBegin).ToList();
            var started = new List<(CscAnimationBinding Binding, CscElement AnimElement, AnimationFrame Placed)>();

            foreach (var binding in ordered)
            {
                var animElement = binding.Element;
                if (binding.FrameCount <= 0 || binding.ClipLengthSeconds <= 0)
                    continue;
                if (sceneTime < animElement.NormalizedBegin)
                    continue;

                // The owning model's own (begin, end) window is authoritative over a child
                // animation's window - an animation that outlives its model gets cut off instead
                // of continuing to play after the model itself would already be gone. A binding
                // past its own window stays in the list frozen at its End pose, so late scene
                // times (and scrubbing back into them) deterministically resolve to the final
                // clip's last placed pose instead of whatever the player happened to show last.
                var effectiveEnd = EffectiveEnd(animElement, modelElement);
                var isPast = animElement.TimingMode != "infinite" && effectiveEnd >= animElement.NormalizedBegin && sceneTime > effectiveEnd;
                var sampleTime = isPast ? effectiveEnd : sceneTime;

                var placed = ComputePlacedPose(binding, animElement, modelElement, effectiveEnd, sampleTime);
                if (placed != null)
                    started.Add((binding, animElement, placed));
            }

            if (started.Count == 0)
                return;

            // Nested crossfade: each binding fades in over the window between its own Begin and
            // the previous binding's effective End, on top of the composite of everything before
            // it - blend(blend(walk, step, w1), idle, w2) for a three-deep overlap. Every weight
            // is continuous in scene time and saturates exactly when the previous clip's window
            // closes, so clips joining, blending and expiring never step the pose: a two-clip
            // overlap reduces to the plain pairwise crossfade, a clip with no overlap (span <= 0:
            // the previous clip ended before this one began) takes over instantly, and an
            // already-past prefix stops contributing exactly when the weights above it saturate.
            var composite = started[0].Placed;
            var newest = started[0].Binding;
            for (var i = 1; i < started.Count; i++)
            {
                var (binding, animElement, placed) = started[i];
                if (placed.BoneTransforms.Count != composite.BoneTransforms.Count)
                    continue;

                var span = EffectiveEnd(started[i - 1].AnimElement, modelElement) - animElement.NormalizedBegin;
                var weight = span > 0 ? Math.Clamp((sceneTime - animElement.NormalizedBegin) / span, 0f, 1f) : 1f;
                composite = weight >= 1f ? placed : BlendFrames(composite, placed, weight, binding.Skeleton);
                newest = binding;
            }

            if (content.Player.AnimationClip != newest.Clip)
                content.Player.SetAnimation(newest.Clip, newest.Skeleton, allowAnimationsFromDifferentSkeletons: true);
            content.Player.SetManualFrame(composite);
        }

        /// <summary>Samples a binding's pose and rigidly places it where the scene data says that
        /// clip plays: at its own ANIMATION_ELEMENT's authored transform (relative to the model),
        /// advanced by the clip's own completed-loop root motion since its Begin.
        /// <para/>
        /// Scene authors PLACE each animation element where its clip is meant to play - verified
        /// against boris_scene's real data: walk's element sits at the model origin, and
        /// step_forward (z=6.51, slightly rotated) / stand_idle (z=7.01) are authored exactly
        /// where the walk cycle leaves Boris, so with placement applied, walk's animroot reaches
        /// z=7.7 as it fades out, step's ends at z=7.06 and idle stands at z=7.01 - continuity
        /// across a transition chain comes from the FILE, not from anything derived at runtime.
        /// Synthesizing positions instead (e.g. one shared per-model render-world nudge carrying
        /// completed loops) cannot place two overlapping clips at once and produces a visible
        /// jump somewhere in every transition chain.
        /// <para/>
        /// Loop motion composes INSIDE the placement (<c>pose * loopDelta^k * elementTransform</c>):
        /// at every loop wrap the raw pose's animroot reset is exactly cancelled by the
        /// incremented power (loopDelta is <c>Invert(root(first)) * root(last)</c> - see
        /// <see cref="CscSceneGraphBuilder"/>'s ComputeLoopRootMotion), keeping each binding's
        /// placed pose continuous over its whole life with no separate ground-position channel to
        /// fall out of sync with. The loop count is normalized to the binding's own Begin (a
        /// nonzero ELEMENT_PERIOD offset makes it nonzero, often negative, the instant the clip
        /// starts - see <see cref="MatrixPower"/>), so each clip starts exactly at its authored
        /// mark and only advances by what it has genuinely played through since.
        /// <para/>
        /// A self-bound carrier (a standalone ANIMATION_ELEMENT that is itself the content
        /// element - building-destruct anims whose pieces bone-attach straight to it) skips the
        /// element-transform factor: everything consuming its pose already composes against the
        /// carrier's own world matrix, so folding the same transform into the pose would apply it
        /// twice. Loop advancement still applies.
        /// <para/>
        /// The rigid placement is applied to the stored bind-relative WorldTransforms directly -
        /// see <see cref="ApplyRigidOffset"/> for why that is algebraically exact.</summary>
        static AnimationFrame? ComputePlacedPose(CscAnimationBinding binding, CscElement animElement, CscElement? modelElement, float effectiveEnd, float sampleTime)
        {
            var (frame, frameInterpolation, loops) = SampleBindingTime(binding, animElement, effectiveEnd, sampleTime);
            var pose = AnimationSampler.Sample(frame, frameInterpolation, binding.Skeleton, binding.Clip);
            if (pose == null)
                return null;

            var (_, _, loopsAtBegin) = SampleBindingTime(binding, animElement, effectiveEnd, animElement.NormalizedBegin);
            var placement = MatrixPower(binding.LoopRootMotion, loops - loopsAtBegin);
            var selfBound = modelElement != null && animElement.Id == modelElement.Id;
            if (!selfBound)
                placement *= animElement.LocalTransform(sampleTime);
            return ApplyRigidOffset(pose, placement);
        }

        /// <summary>Applies each active splice binding (an ANIMATION_ELEMENT with an
        /// ANIMATION_SPLICE_ELEMENT sibling record - hand_pose/face_pose etc.) as a bone-mask
        /// override on top of whatever <see cref="DriveBaseBindings"/> already set as the player's
        /// current frame: the splice's own SpliceBoneId bone and every descendant of it (by the
        /// model's own skeleton hierarchy) has its pose entirely REPLACED by the splice clip's own
        /// sampled pose, not blended - hand/face poses are meant to override specific bones
        /// outright, unlike the cross-fade used for two overlapping full-body clips. Assumes a
        /// splice's own clip shares the same skeleton/bone ordering as the model's base skeleton
        /// (bone index N means the same bone in both) - the only sensible reading of "given by the
        /// bone id" since there's nothing else to map SpliceBoneId against. Several splices can be
        /// active at once (a hand pose and a face pose target disjoint bones); when two target the
        /// same bone the later one (by Begin) wins.</summary>
        void ApplySplices(CscModelContent content, List<CscAnimationBinding> spliceBindings, CscElement? modelElement, float sceneTime)
        {
            if (spliceBindings.Count == 0 || content.Skeleton == null)
                return;

            var baseFrame = content.Player.GetCurrentAnimationFrame();
            if (baseFrame == null)
                return;

            AnimationFrame? merged = null;

            foreach (var binding in spliceBindings.OrderBy(b => b.Element.NormalizedBegin))
            {
                var animElement = binding.Element;
                if (binding.FrameCount <= 0 || binding.ClipLengthSeconds <= 0)
                    continue;
                if (sceneTime < animElement.NormalizedBegin)
                    continue;

                var effectiveEnd = EffectiveEnd(animElement, modelElement);
                if (animElement.TimingMode != "infinite" && effectiveEnd >= animElement.NormalizedBegin && sceneTime > effectiveEnd)
                    continue;

                var (frame, frameInterpolation, _) = SampleBindingTime(binding, animElement, effectiveEnd, sceneTime);
                var splicePose = AnimationSampler.Sample(frame, frameInterpolation, binding.Skeleton, binding.Clip);
                if (splicePose == null || splicePose.BoneTransforms.Count != baseFrame.BoneTransforms.Count)
                    continue;

                merged ??= CloneFrame(baseFrame);
                ApplySpliceOverride(merged, splicePose, content.Skeleton, animElement.SpliceBoneId);
            }

            if (merged != null)
                content.Player.SetManualFrame(merged);
        }

        /// <summary>Overrides SpliceBoneId and its descendants in <paramref name="merged"/> with the
        /// splice clip's own pose WITHOUT discarding the base animation's actual current arm/head
        /// chain. Each bone's <see cref="AnimationFrame.BoneKeyFrame.WorldTransform"/> (as produced
        /// by <see cref="AnimationSampler"/>) is already bind-relative but chain-composed against
        /// whichever clip it was sampled from - so naively copying the splice's own WorldTransform
        /// for the target bone would replace it with the splice clip's own idea of where its parent
        /// chain is (typically close to bind pose, since a hand/face-only clip doesn't meaningfully
        /// animate the shoulder/neck above it), leaving the hand/face behind while the real arm/head
        /// keeps moving under the base animation. Instead, each spliced bone's LOCAL
        /// rotation/translation/scale (still available on the sampled <see cref="AnimationFrame.BoneKeyFrame"/> -
        /// <see cref="AnimationSampler"/> never clears them, only adds the composed WorldTransform)
        /// is recomposed against the BASE animation's own current chain - top-down, so descendants
        /// chain onto their own already-overridden parent - and the hand/face rigidly follows
        /// wherever the real arm/head actually is right now, with only its own internal pose
        /// replaced.</summary>
        static void ApplySpliceOverride(AnimationFrame merged, AnimationFrame splicePose, GameSkeleton skeleton, int spliceBoneId)
        {
            if (spliceBoneId < 0 || spliceBoneId >= skeleton.BoneCount)
                return;

            var overriddenWorlds = new Dictionary<int, Matrix>();
            foreach (var boneIndex in BoneAndDescendantsTopDown(skeleton, spliceBoneId))
            {
                if (boneIndex >= merged.BoneTransforms.Count || boneIndex >= splicePose.BoneTransforms.Count)
                    continue;

                var parentIndex = skeleton.GetParentBoneIndex(boneIndex);
                var parentWorld = parentIndex < 0
                    ? Matrix.Identity
                    : overriddenWorlds.TryGetValue(parentIndex, out var overriddenParent)
                        ? overriddenParent
                        : merged.GetSkeletonAnimatedWorld(skeleton, parentIndex);

                var spliceKey = splicePose.BoneTransforms[boneIndex];
                var localSplice = Matrix.CreateScale(spliceKey.Scale) * Matrix.CreateFromQuaternion(spliceKey.Rotation) * Matrix.CreateTranslation(spliceKey.Translation);
                var animatedWorld = localSplice * parentWorld;
                overriddenWorlds[boneIndex] = animatedWorld;

                var bindInverse = Matrix.Invert(skeleton.GetWorldTransform(boneIndex));
                merged.BoneTransforms[boneIndex] = new AnimationFrame.BoneKeyFrame
                {
                    BoneIndex = spliceKey.BoneIndex,
                    ParentBoneIndex = spliceKey.ParentBoneIndex,
                    Rotation = spliceKey.Rotation,
                    Translation = spliceKey.Translation,
                    Scale = spliceKey.Scale,
                    WorldTransform = bindInverse * animatedWorld,
                };
            }
        }

        static AnimationFrame CloneFrame(AnimationFrame source)
        {
            var clone = new AnimationFrame();
            clone.BoneTransforms.AddRange(source.BoneTransforms);
            return clone;
        }

        /// <summary>A bone and every bone beneath it in the skeleton hierarchy, parent before child
        /// so each descendant's newly-overridden parent world is already available when it's
        /// processed - "certain bones and their children" for splice overrides.</summary>
        static List<int> BoneAndDescendantsTopDown(GameSkeleton skeleton, int boneId)
        {
            var result = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(boneId);
            while (queue.Count > 0)
            {
                var bone = queue.Dequeue();
                result.Add(bone);
                foreach (var child in skeleton.GetDirectChildBones(bone))
                    queue.Enqueue(child);
            }
            return result;
        }

        static float EffectiveEnd(CscElement animElement, CscElement? modelElement)
        {
            var effectiveEnd = animElement.NormalizedEnd;
            if (modelElement != null && modelElement.TimingMode != "infinite" && modelElement.NormalizedEnd >= modelElement.NormalizedBegin)
                effectiveEnd = MathF.Min(effectiveEnd, modelElement.NormalizedEnd);
            return effectiveEnd;
        }

        /// <summary>Frame index + fractional inter-frame position + completed-loop count for a
        /// binding at a given sample time (already clamped by the caller to whichever window
        /// applies), honoring the animation's own (normalized) Begin and ELEMENT_PERIOD speed/
        /// offset. Floored modulo (not truncation) so a negative time offset wraps into "plays the
        /// tail end of the clip first" instead of freezing on frame 0 until elapsed time catches up
        /// to |offset|.
        /// <para/>
        /// <see cref="Frame"/>/<see cref="FrameInterpolation"/> are deliberately computed the same
        /// way <see cref="AnimationSampler.Sample(float, GameSkeleton, AnimationClip, List{IAnimationChangeRule}, bool)"/>
        /// does (round to the nearest frame, keep the leftover as the blend factor toward its
        /// neighbour) rather than truncating <paramref name="normalized"/> straight to an integer
        /// frame index with no fractional remainder - the previous version discarded that remainder
        /// entirely (passed a hardcoded interpolation of 0 into <see cref="AnimationSampler.Sample(int, float, GameSkeleton, AnimationClip, List{IAnimationChangeRule}, bool)"/>),
        /// so playback only ever showed whichever whole frame the current time landed on with no
        /// blend toward the next - correct, but visibly stepped ("slideshow-y"), worst at a heavily
        /// slowed ELEMENT_PERIOD (e.g. 0.025x) where each frame then holds for tens of real seconds
        /// before jumping to the next.</summary>
        static (int Frame, float FrameInterpolation, int CompletedLoops) SampleBindingTime(CscAnimationBinding binding, CscElement animElement, float effectiveEnd, float sampleTime)
        {
            var begin = animElement.NormalizedBegin;
            var clampedTime = animElement.TimingMode == "infinite" || effectiveEnd < begin
                ? MathF.Max(sampleTime, begin)
                : Math.Clamp(sampleTime, begin, effectiveEnd);

            var speed = animElement.PeriodSpeedMultiplier != 0 ? animElement.PeriodSpeedMultiplier : 1;
            var animTime = (clampedTime - begin) * speed + animElement.PeriodTimeOffset;

            var passes = animTime / binding.ClipLengthSeconds;
            var completedLoops = (int)MathF.Floor(passes);
            var normalized = passes - completedLoops; // always in [0,1), even for negative animTime

            var maxFrameIndex = MathF.Max(binding.FrameCount - 1, 0);
            var frameWithLeftover = normalized * maxFrameIndex;
            var frame = (int)MathF.Round(frameWithLeftover);
            var frameInterpolation = frameWithLeftover - frame;
            frame = Math.Clamp(frame, 0, binding.FrameCount - 1);

            return (frame, frameInterpolation, completedLoops);
        }

        /// <summary>Per-bone crossfade of two already-sampled frames, blended in LOCAL bone space
        /// (each bone's parent-relative rotation/translation/scale, Slerp/Lerp-ed) and then
        /// recomposed down the hierarchy exactly the way <see cref="AnimationSampler"/> builds a
        /// frame (<c>animatedWorld = local * parentAnimatedWorld</c>, parent-before-child, then
        /// bind-normalized for the shader).
        /// <para/>
        /// Blending each bone's MODEL-SPACE transform independently instead keeps limb chains
        /// intact only while the two poses roughly agree. Where they differ (e.g. a dual-swords
        /// walk against an idle holding the arms completely differently), every bone takes its own
        /// straight-line path between its two model-space positions, detaching hands and forearms
        /// from the arm chain that should be carrying them. Blending locals and
        /// recomposing makes every bone ride its own blended parent by construction, so limbs
        /// stay connected at every weight; parentless bones' locals ARE their model-space
        /// placement (see <see cref="ApplyRigidOffset"/>), so the verified positional continuity
        /// of the placed animroot is unchanged by blending this way.
        /// <para/>
        /// Callers are expected to pass frames already placed by <see cref="ComputePlacedPose"/>,
        /// so both sides express their pose in the same (model-space) reference frame - blending
        /// raw clip-local poses would average two unrelated authoring origins through the
        /// animroot bone.</summary>
        static AnimationFrame BlendFrames(AnimationFrame a, AnimationFrame b, float weightB, GameSkeleton skeleton)
        {
            var blended = new AnimationFrame();
            var count = Math.Min(Math.Min(a.BoneTransforms.Count, b.BoneTransforms.Count), skeleton.BoneCount);
            var animatedWorlds = new Matrix[count];
            for (var i = 0; i < count; i++)
            {
                var boneA = a.BoneTransforms[i];
                var boneB = b.BoneTransforms[i];

                var scale = Vector3.Lerp(boneA.Scale, boneB.Scale, weightB);
                var rotation = Quaternion.Slerp(boneA.Rotation, boneB.Rotation, weightB);
                var translation = Vector3.Lerp(boneA.Translation, boneB.Translation, weightB);

                var local = Matrix.CreateScale(scale) * Matrix.CreateFromQuaternion(rotation) * Matrix.CreateTranslation(translation);
                var parent = boneA.ParentBoneIndex;
                animatedWorlds[i] = parent >= 0 && parent < i ? local * animatedWorlds[parent] : local;

                blended.BoneTransforms.Add(new AnimationFrame.BoneKeyFrame
                {
                    BoneIndex = boneA.BoneIndex,
                    ParentBoneIndex = boneA.ParentBoneIndex,
                    Rotation = rotation,
                    Translation = translation,
                    Scale = scale,
                    WorldTransform = Matrix.Invert(skeleton.GetWorldTransform(i)) * animatedWorlds[i],
                });
            }
            return blended;
        }

        /// <summary>Rigidly repositions/reorients every bone of an already-sampled frame by a
        /// constant matrix (see <see cref="ComputePlacedPose"/>) - body-pose shape is
        /// untouched, only where it sits and faces changes. Each <see
        /// cref="AnimationFrame.BoneKeyFrame.WorldTransform"/> is bind-relative
        /// (<c>bindInverse * animatedWorld</c>, per <see cref="AnimationPlayer.GetSkeletonAnimatedWorld"/>'s
        /// <c>bindWorld * WorldTransform = animatedWorld</c>), so post-multiplying the offset onto
        /// the STORED WorldTransform is exactly equivalent to post-multiplying the true model-space
        /// animatedWorld and re-deriving - the per-bone bindWorld factor cancels out algebraically,
        /// no unbake/rebake needed.
        /// <para/>
        /// The per-bone LOCAL components are preserved (they're <see cref="BlendFrames"/>'s
        /// inputs, not derived data): a rigid whole-pose move leaves every parent-relative
        /// transform unchanged except for parentless bones (animroot), whose local IS their
        /// animated world - the offset folds into those directly, keeping locals and worlds
        /// mutually consistent so a chain recompose from locals reproduces exactly these worlds.
        /// Identity offset is a no-op fast path (skips reallocating a whole frame every tick for
        /// the common case of a binding placed at its model's origin with no completed
        /// loops).</summary>
        static AnimationFrame ApplyRigidOffset(AnimationFrame frame, Matrix offset)
        {
            if (offset == Matrix.Identity)
                return frame;

            var result = new AnimationFrame();
            foreach (var bone in frame.BoneTransforms)
            {
                var rotation = bone.Rotation;
                var translation = bone.Translation;
                var scale = bone.Scale;
                if (bone.ParentBoneIndex < 0)
                {
                    var local = Matrix.CreateScale(scale) * Matrix.CreateFromQuaternion(rotation) * Matrix.CreateTranslation(translation);
                    (local * offset).Decompose(out scale, out rotation, out translation);
                }

                result.BoneTransforms.Add(new AnimationFrame.BoneKeyFrame
                {
                    BoneIndex = bone.BoneIndex,
                    ParentBoneIndex = bone.ParentBoneIndex,
                    Rotation = rotation,
                    Translation = translation,
                    Scale = scale,
                    WorldTransform = bone.WorldTransform * offset,
                });
            }
            return result;
        }

        /// <summary>Supports negative exponents (inverse) as well as positive ones: a binding
        /// sampled with a negative ELEMENT_PERIOD time offset starts already partway through the
        /// clip counted BACKWARD from frame 0 (e.g. offset -0.7 loops means "0.7 loops before the
        /// clip's own start"), which floored-modulo division in <see cref="SampleBindingTime"/>
        /// correctly reports as a negative completed-loop count. Previously any exponent &lt;= 0 was
        /// treated as identity, which was correct for exactly 0 but wrong for negative counts -
        /// LoopRootMotion^-1 should undo one loop's worth of motion, not contribute nothing. That
        /// bug meant every binding's first loop-wrap after a negative-offset start (once the loop
        /// counter crossed from negative to zero, both of which the old code rendered identically)
        /// had no compensating jump to cancel the clip's own reset-to-frame-0 pose, producing a
        /// visible backward teleport right at the start before playback evened out (e.g. several
        /// characters each carrying a distinct negative ELEMENT_PERIOD offset so they don't all
        /// step in lockstep).</summary>
        static Matrix MatrixPower(Matrix m, int exponent)
        {
            if (exponent == 0 || m == Matrix.Identity)
                return Matrix.Identity;

            if (exponent < 0)
                return Matrix.Invert(MatrixPower(m, -exponent));

            var result = Matrix.Identity;
            var basePower = m;
            while (exponent > 0)
            {
                if ((exponent & 1) != 0)
                    result *= basePower;
                basePower *= basePower;
                exponent >>= 1;
            }
            return result;
        }
    }
}
