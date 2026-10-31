using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Navigation;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.WpfWindow;
using GameWorld.Core.WpfWindow.Internals;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Vfx;

// Compile measured cold rendering paths without invoking application or graphics code.
public static class VfxRuntimePreparation
{
    internal const int MaximumMethods = 8192;
    private static readonly PreparationQueue Queue = new(PrepareMethod, CoreMethods);
    public static Task PrepareAsync(params Type[] sceneTypes) => Queue.PrepareAsync(sceneTypes);
    internal static PreparationReport Report => Queue.Report;

    internal static void PrepareMethod(MethodBase method)
    {
        var arguments = (method.DeclaringType?.GetGenericArguments() ?? [])
            .Concat(method.IsGenericMethod ? method.GetGenericArguments() : []).Select(type => type.TypeHandle).ToArray();
        RuntimeHelpers.PrepareMethod(method.MethodHandle, arguments);
    }

    internal static IEnumerable<MethodBase> CoreMethods()
    {
        foreach (var method in ColdFrameDependencies()) yield return method;
        // These roots account for the measured first Update/Draw, projection and selection JIT spans.
        Type[] vfx = [typeof(EffekseerVfxPreview), typeof(TotalWarVfxSimulation), typeof(VfxPreviewComponent),
            typeof(VfxPreviewSceneNode), typeof(VfxFlatGroundDepth), typeof(VfxPreviewBounds), typeof(CommonShaderParameters),
            typeof(VfxInteractiveConversion), typeof(VfxInteractiveBounds), typeof(VfxInteractivePrewarm), typeof(VfxInteractiveFrameBudget),
            typeof(VfxPreviewSnapshot), typeof(VfxPreparedConversion), typeof(VfxConstantReloadPreparation), typeof(VfxInteractiveReloadFrame),
            typeof(VfxModelNativeCurve), typeof(VfxModelNativeMotion), typeof(VfxPreviewEmitter)];
        foreach (var type in vfx)
            foreach (var method in Methods(type, nested: true)) yield return method;
        Type[] scene = [typeof(WpfGame), typeof(D3D11Host), typeof(D3D11FramePresenter), typeof(D3D11Image),
            typeof(D3D11HostFrameDiagnostics), typeof(D3D11FrameTiming), typeof(IRenderItem), typeof(ViewportShadingPolicy), typeof(RenderTargetHelper), typeof(RenderEngineComponent), typeof(GridComponent),
            typeof(ArcBallCamera), typeof(FpsComponent), typeof(NavigationGizmoComponent), typeof(SceneManager),
            typeof(CommandStackRenderer), typeof(LightControllerComponent), typeof(AnimationsContainerComponent), typeof(LineHelper),
            typeof(CommonShaderParameterBuilder), typeof(SceneRenderParametersStore), typeof(SceneNodeHelper), typeof(SceneNode), typeof(GroupNode),
            typeof(Rmv2MeshNode), typeof(Rmv2ModelNode), typeof(SkeletonNode), typeof(VertexInstanceMesh),
            typeof(AnimationPlayer), typeof(AnimationSampler), typeof(GameSkeleton), typeof(AnimationFrame), typeof(AnimationClip), typeof(BoundingFrustum), typeof(GameTime)];
        foreach (var type in scene)
            foreach (var method in Methods(type, nested: true)) yield return method;
        foreach (var name in new[] { "GameWorld.Core.Components.Navigation.NavigationGizmo", "GameWorld.Core.Components.Navigation.CameraTransition" })
            if (typeof(WpfGame).Assembly.GetType(name) is { } type)
                foreach (var method in Methods(type, nested: true)) yield return method;

        var mathNames = new HashSet<string>(StringComparer.Ordinal)
        { "CreateLookAt", "CreatePerspectiveFieldOfView", "CreateOrthographicOffCenter", "CreateOrthographic", "CreateScale", "CreateTranslation",
            "CreateFromQuaternion", "CreateFromAxisAngle", "CreateRotationX", "CreateRotationY", "CreateRotationZ", "Multiply", "Invert", "Transpose", "Normalize", "Transform",
            "TransformNormal", "Cross", "Dot", "Length", "LengthSquared", "Project", "Unproject", "Lerp", "Slerp", "Clamp", "ToRadians", "ToDegrees" };
        Type[] math = [typeof(Matrix), typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Quaternion), typeof(BoundingBox), typeof(MathHelper), typeof(Viewport), typeof(Color)];
        foreach (var type in math)
            foreach (var method in Methods(type).Where(method => mathNames.Contains(method.Name) || method.Name.StartsWith("op_", StringComparison.Ordinal))) yield return method;
        Type[] graphics = [typeof(SpriteBatch), typeof(SpriteFont), typeof(GraphicsDevice), typeof(VertexBuffer), typeof(DynamicVertexBuffer),
            typeof(IndexBuffer), typeof(RenderTarget2D), typeof(RenderTargetBinding), typeof(VertexBufferBinding), typeof(VertexDeclaration), typeof(BasicEffect), typeof(Effect), typeof(EffectPass), typeof(EffectParameter),
            typeof(BlendState), typeof(DepthStencilState), typeof(RasterizerState), typeof(SamplerState), typeof(Texture2D)];
        var graphicsNames = new HashSet<string>(StringComparer.Ordinal)
        { "Begin", "End", "Draw", "DrawString", "OnApply", "Apply", "SetValue", "SetRenderTarget", "SetRenderTargets", "Clear", "Flush", "PlatformClear",
            "PlatformApplyState", "ApplyState", "SetVertexBuffer", "SetVertexBuffers", "SetUserVertexBuffer", "PlatformDrawUserPrimitives", "PlatformSetData",
            "DrawUserPrimitives", "DrawUserIndexedPrimitives", "DrawPrimitives", "DrawIndexedPrimitives", "SetDataInternal", "SetData", "GetState", "GetDxState", "GetFilter", "MeasureString", "FromType", "GetInputElements", "GetOrCreate" };
        foreach (var type in graphics)
            foreach (var method in Methods(type).Where(method => graphicsNames.Contains(method.Name) || method.IsConstructor || method.Name.StartsWith("Platform", StringComparison.Ordinal) || method.Name.StartsWith("GetMS", StringComparison.Ordinal) || method.Name is "CreateMSTexture" or "ResolveSubresource" || method.Name.StartsWith("get_", StringComparison.Ordinal) || method.Name.StartsWith("set_", StringComparison.Ordinal))) yield return method;
        foreach (var name in new[] { "SpriteBatcher", "SpriteBatchItem", "SpriteEffect", "InputLayoutCache", "VertexInputLayout", "VertexBufferBindings", "EffectHelpers",
            "TargetBlendState", "ConstantBuffer", "ConstantBufferCollection", "GraphicsCapabilities", "TextureCollection", "VertexElement", "SamplerStateCollection" })
            if (typeof(GraphicsDevice).Assembly.GetType("Microsoft.Xna.Framework.Graphics." + name) is { } type)
                foreach (var method in Methods(type, nested: true)) yield return method;

        var projectedOrder = typeof(TotalWarVfxSimulation).GetNestedType("ProjectedOrder", BindingFlags.NonPublic);
        Type[] elements = [typeof(VfxPreviewParticle), typeof(EffekseerParticle), typeof(VfxPreviewEmitter), typeof(VfxPreviewDiagnostic),
            typeof(VertexPositionColor), typeof(VertexPositionColorTexture), typeof(int), typeof(IRenderItem), typeof(GameComponent)];
        foreach (var element in elements)
        {
            foreach (var method in Methods(typeof(List<>).MakeGenericType(element), nested: true)) yield return method;
            foreach (var method in CloseMethods(typeof(Enumerable), [element], "Any", "TryGetSpan", "Where", "ToArray", "ToList", "Count", "FirstOrDefault")) yield return method;
            foreach (var method in CloseMethods(typeof(System.Runtime.InteropServices.CollectionsMarshal), [element], "AsSpan")) yield return method;
        }
        foreach (var type in new[] { typeof(Dictionary<object, float?>), typeof(Dictionary<int, int>), typeof(HashSet<int>) })
            foreach (var method in Methods(type, nested: true)) yield return method;
        foreach (var vertex in new[] { typeof(VertexPositionColor), typeof(VertexPositionColorTexture) })
            foreach (var type in new[] { typeof(GraphicsDevice), typeof(VertexBuffer), typeof(DynamicVertexBuffer) })
                foreach (var method in CloseMethods(type, [vertex], "DrawUserPrimitives", "PlatformDrawUserPrimitives", "SetUserVertexBuffer", "SetData", "SetDataInternal", "PlatformSetData")) yield return method;
        if (projectedOrder != null)
        {
            foreach (var method in CloseMethods(typeof(Array), [projectedOrder], "Sort")) yield return method;
            foreach (var name in new[] { "System.Collections.Generic.ArraySortHelper`1", "System.Collections.Generic.GenericArraySortHelper`1" })
                if (typeof(Array).Assembly.GetType(name) is { } helper)
                    foreach (var method in Methods(helper.MakeGenericType(projectedOrder), nested: true)) yield return method;
        }
    }

    private static IEnumerable<MethodBase> ColdFrameDependencies()
    {
        // Concrete value types are essential: their collection and vertex code is not shared with reference types.
        Type[] elements = [typeof(VfxPreviewParticle), typeof(EffekseerParticle), typeof(VertexPositionColor), typeof(VertexPositionColorTexture),
            typeof(VertexPositionTexture), typeof((float, Matrix)), typeof(IRenderItem), typeof(ISceneNode), typeof(Matrix), typeof(AnimationFrame.BoneKeyFrame),
            typeof(KeyValuePair<RenderBuckedId, List<IRenderItem>>), typeof(KeyValuePair<object, object>)];
        var axis = typeof(NavigationGizmoComponent).Assembly.GetType("GameWorld.Core.Components.Navigation.NavigationGizmo")?.GetNestedType("AxisDrawData", BindingFlags.NonPublic);
        var order = typeof(TotalWarVfxSimulation).GetNestedType("ProjectedOrder", BindingFlags.NonPublic);
        foreach (var element in elements.Concat(new[] { axis, order }.OfType<Type>()))
        {
            foreach (var method in Methods(typeof(List<>).MakeGenericType(element), true)) yield return method;
            foreach (var method in CloseMethods(typeof(Enumerable), [element], "Any", "TryGetSpan", "Where", "ToArray", "ToList", "Count", "FirstOrDefault")) yield return method;
            foreach (var method in CloseMethods(typeof(Array), [element], "Sort", "Resize", "Empty")) yield return method;
            foreach (var method in CloseMethods(typeof(System.Runtime.InteropServices.CollectionsMarshal), [element], "AsSpan")) yield return method;
            foreach (var name in new[] { "System.Collections.Generic.ArraySortHelper`1", "System.Collections.Generic.GenericArraySortHelper`1" })
                if (typeof(Array).Assembly.GetType(name) is { } helper)
                    foreach (var method in CloseTypeMethods(helper, [element])) yield return method;
            foreach (var name in new[] { "IEnumerableWhereIterator`1", "ArrayWhereIterator`1", "ListWhereIterator`1" })
                if (typeof(Enumerable).GetNestedType(name, BindingFlags.NonPublic) is { } iterator)
                    foreach (var method in CloseTypeMethods(iterator, [element])) yield return method;
        }
        foreach (var type in new[] { typeof(Dictionary<object, float?>), typeof(Dictionary<RenderBuckedId, List<IRenderItem>>),
            typeof(Dictionary<RasterizerStateEnum, RasterizerState>), typeof(HashSet<int>) })
            foreach (var method in Methods(type, true)) yield return method;
        foreach (var field in new[] { "_projectionEventTints", "_projectionEventAnchors", "_projectionEventScales", "_spriteCullScales" })
            if (typeof(TotalWarVfxSimulation).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic) is { } collection)
                foreach (var method in Methods(collection.FieldType, true)) yield return method;
        foreach (var node in new[] { typeof(Rmv2MeshNode), typeof(Rmv2ModelNode), typeof(SkeletonNode), typeof(ISceneNode) })
            foreach (var method in CloseMethods(typeof(SceneNodeHelper), [node], "GetChildrenOfType")) yield return method;
        foreach (var vertex in new[] { typeof(VertexPositionColor), typeof(VertexPositionColorTexture), typeof(VertexPositionTexture), typeof(short), typeof(int) })
        {
            foreach (var type in new[] { typeof(GraphicsDevice), typeof(VertexBuffer), typeof(DynamicVertexBuffer), typeof(IndexBuffer) })
                foreach (var method in CloseMethods(type, [vertex], "DrawUserPrimitives", "PlatformDrawUserPrimitives", "DrawUserIndexedPrimitives", "PlatformDrawUserIndexedPrimitives",
                    "SetUserVertexBuffer", "SetUserIndexBuffer", "SetData", "SetDataInternal", "PlatformSetData")) yield return method;
            foreach (var name in new[] { "Microsoft.Xna.Framework.Graphics.VertexDeclarationCache`1", "MonoGame.Framework.Utilities.ReflectionHelpers+SizeOf`1" })
                if (typeof(GraphicsDevice).Assembly.GetType(name) is { } helper)
                    foreach (var method in CloseTypeMethods(helper, [vertex])) yield return method;
        }
        Type[] interop = [typeof(SharpDX.Direct3D11.Device), typeof(SharpDX.Direct3D11.DeviceContext), typeof(SharpDX.Direct3D11.Texture2D),
            typeof(SharpDX.Direct3D11.InputLayout), typeof(SharpDX.Direct3D11.ShaderResourceView), typeof(SharpDX.Direct3D11.SamplerState),
            typeof(SharpDX.Direct3D11.RenderTargetView), typeof(SharpDX.Direct3D11.BlendState), typeof(SharpDX.Direct3D11.DepthStencilState),
            typeof(SharpDX.Direct3D11.RasterizerState), typeof(SharpDX.Direct3D11.VertexShaderStage), typeof(SharpDX.Direct3D11.PixelShaderStage),
            typeof(SharpDX.Direct3D11.InputAssemblerStage), typeof(SharpDX.Direct3D11.OutputMergerStage), typeof(SharpDX.Direct3D11.RasterizerStage), typeof(SharpDX.Direct3D11.CommonShaderStage)];
        foreach (var type in interop)
            foreach (var method in Methods(type))
                if (method.IsConstructor || method.Name.StartsWith("Create", StringComparison.Ordinal) || method.Name.StartsWith("Set", StringComparison.Ordinal)
                    || method.Name is "MapSubresource" or "UnmapSubresource" or "get_NativePointer" or "End" or "Flush" or "ResolveSubresource_" or "GetData") yield return method;
        foreach (var vertex in new[] { typeof(VertexPositionColor), typeof(VertexPositionColorTexture), typeof(VertexPositionTexture) })
        {
            foreach (var method in CloseMethods(typeof(SharpDX.Utilities), [vertex], "Write")) yield return method;
            if (typeof(SharpDX.Utilities).Assembly.GetType("SharpDX.Interop") is { } interopType)
                foreach (var method in CloseMethods(interopType, [vertex], "Write")) yield return method;
        }
        foreach (var type in new[] { typeof(Task<BoundingBox?>), typeof(AsyncTaskMethodBuilder<BoundingBox?>) })
            foreach (var method in Methods(type)) yield return method;
        foreach (var method in CloseMethods(typeof(Task), [typeof(BoundingBox?)], "Run")) yield return method;
    }
    private static IEnumerable<MethodBase> CloseTypeMethods(Type definition, Type[] arguments)
    {
        Type? closed = null;
        try { closed = definition.MakeGenericType(arguments); } catch (ArgumentException) { }
        return closed == null ? [] : Methods(closed, true);
    }
    private static IEnumerable<MethodBase> CloseMethods(Type type, Type[] arguments, params string[] names)
    {
        foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            if (method.IsGenericMethodDefinition && method.GetGenericArguments().Length == arguments.Length && names.Contains(method.Name, StringComparer.Ordinal))
            {
                MethodInfo? closed = null;
                try { closed = method.MakeGenericMethod(arguments); } catch (ArgumentException) { }
                if (closed != null) yield return closed;
            }
    }

    internal static IEnumerable<MethodBase> Methods(Type type, bool nested = false)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
            if (!method.IsAbstract && !method.ContainsGenericParameters && !type.ContainsGenericParameters) yield return method;
        if (!nested) yield break;
        foreach (var child in type.GetNestedTypes(flags))
        {
            var closed = child;
            if (child.ContainsGenericParameters && type.IsConstructedGenericType && child.GetGenericArguments().Length == type.GetGenericArguments().Length)
            {
                try { closed = child.MakeGenericType(type.GetGenericArguments()); } catch (ArgumentException) { continue; }
            }
            if (!closed.ContainsGenericParameters)
                foreach (var method in Methods(closed, true)) yield return method;
        }
    }

    internal sealed record PreparationReport(int Attempted, int Prepared, int Failed, int LimitedBatches, string? FirstFailure);

    internal sealed class PreparationQueue(Action<MethodBase> prepare, Func<IEnumerable<MethodBase>> coreMethods,
        int maximumMethods = MaximumMethods, TimeSpan? maximumTime = null)
    {
        private readonly object _gate = new();
        private readonly SemaphoreSlim _worker = new(1, 1);
        private readonly Dictionary<Type, Task> _sceneTasks = [];
        private readonly HashSet<MethodBase> _methods = [];
        private Task? _core;
        private int _attempted, _prepared, _failed, _limited;
        private string? _firstFailure;
        internal PreparationReport Report => new(_attempted, _prepared, _failed, _limited, _firstFailure);

        internal Task PrepareAsync(params Type[] types)
        {
            lock (_gate)
            {
                _core ??= Schedule(coreMethods, Task.CompletedTask);
                var tasks = new List<Task>();
                foreach (var type in types.Distinct())
                {
                    if (type == null) continue;
                    if (!_sceneTasks.TryGetValue(type, out var task))
                        _sceneTasks.Add(type, task = Schedule(() => Methods(type, nested: true), _core));
                    tasks.Add(task);
                }
                return tasks.Count == 0 ? _core : tasks.Count == 1 ? tasks[0] : Task.WhenAll(tasks);
            }
        }

        private Task Schedule(Func<IEnumerable<MethodBase>> methods, Task prerequisite) => Task.Run(async () =>
        {
            await prerequisite.ConfigureAwait(false);
            await _worker.WaitAsync().ConfigureAwait(false);
            try
            {
                var started = Stopwatch.GetTimestamp(); var count = 0;
                foreach (var method in methods())
                {
                    if (_methods.Contains(method)) continue;
                    if (count >= maximumMethods || Stopwatch.GetElapsedTime(started) >= (maximumTime ?? TimeSpan.FromMilliseconds(1500)))
                    { _limited++; break; }
                    _methods.Add(method); count++; _attempted++;
                    try { prepare(method); _prepared++; }
                    catch (Exception exception) { _failed++; _firstFailure ??= exception.GetType().FullName; }
                }
            }
            catch (Exception exception) { _failed++; _firstFailure ??= exception.GetType().FullName; }
            finally { _worker.Release(); }
        });
    }
}
