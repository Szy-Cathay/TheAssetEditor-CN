using System.IO;
using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.Types;
using Shared.GameFormats.Vfx;
using Shared.GameFormats.WsModel;

namespace GameWorld.Core.Vfx;

internal sealed record VfxPreviewModel(EffekseerVertex[] Vertices, int[] Indices,
    EffekseerVertex[]? AnimationVertices = null, int AnimationFrameCount = 1, float AnimationCycles = 0, bool OpaqueMaterial = false, bool RigidAnimation = false,
    string? ApproximateMaterialShader = null);
internal enum VfxTextureUsage { Diffuse, DistortionNormal, ShadingNormal, MaterialValue, GradientValue, SolidWhite, OpaqueDiffuse }
internal sealed class VfxConversion
{
    public int TotalEmitterCount { get; set; }
    public int DisabledEmitterCount { get; set; }
    public List<VfxPreviewEmitter> Emitters { get; } = [];
    public List<byte[]> Textures { get; } = [];
    public List<string> TexturePaths { get; } = [];
    public List<VfxTextureUsage> TextureUsages { get; } = [];
    public List<VfxPreviewModel> Models { get; } = [];
    public List<VfxScrollingMeshBinding> MeshMaterials { get; } = [];
    public List<VfxPreviewDiagnostic> Diagnostics { get; } = [];
    public VfxPreviewEditorInfo EditorInfo { get; set; } = VfxPreviewEditorInfo.Empty;
}

// Only builds a preview projection. The source VfxDocument is never edited or used as a save result.
internal static class TotalWarVfxConverter
{
    private readonly record struct ModelCacheKey(string Path, int Part, string Animation, float Speed, float Life, Vector3 Pivot);
    private readonly record struct ConversionCheckpoint((long Vertices, long Indices) Budget, HashSet<ModelCacheKey> Models);

    private sealed class ConversionContext
    {
        private readonly Dictionary<(string Path, string Hash), VfxDocument> _documents = [];
        private readonly Dictionary<(string? Text, string Field, int Fallback, bool Planar), VfxPreviewCurve> _curves = [];
        public VfxModelAnimationBudget AnimationBudget { get; } = new();
        public VfxPreviewEditorInfoBuilder? Editor { get; set; }
        public bool CollectEditorInfo { get; init; } = true;
        public CancellationToken CancellationToken { get; init; }
        public VfxPreviewXmlCache? PreviewDocuments { get; init; }
        public Dictionary<ModelCacheKey, (VfxPreviewModel Model, string Texture, VfxScrollingMeshMaterial? Material, int PartCount)> Models { get; } = [];

        public VfxDocument ReadDocument(VfxPreviewSnapshot snapshot)
        {
            CancellationToken.ThrowIfCancellationRequested();
            var key = (snapshot.SourcePath.Replace('\\', '/').ToLowerInvariant(), System.Convert.ToHexStringLower(SHA256.HashData(snapshot.GameBytes)));
            if (_documents.TryGetValue(key, out var cached)) return cached;
            var document = PreviewDocuments?.Read(snapshot.SourcePath, key.Item2, snapshot.GameBytes, CancellationToken)
                ?? VfxDocument.Read(snapshot.GameBytes);
            _documents.Add(key, document);
            return document;
        }

        public VfxPreviewCurve ReadCurve(string? text, string field, float fallback, bool planarScale = false)
        {
            var key = (text, field, BitConverter.SingleToInt32Bits(fallback), planarScale);
            if (_curves.TryGetValue(key, out var existing)) return existing;
            // Numeric curves are immutable; particle randomness and diagnostics remain with each caller.
            var curve = new VfxPreviewCurve(text, field, fallback, planarScale);
            _curves.Add(key, curve);
            return curve;
        }

        public ConversionCheckpoint Capture() => new(AnimationBudget.Capture(), Models.Keys.ToHashSet());

        public void Restore(ConversionCheckpoint checkpoint)
        {
            AnimationBudget.Restore(checkpoint.Budget);
            foreach (var key in Models.Keys.Where(key => !checkpoint.Models.Contains(key)).ToArray()) Models.Remove(key);
        }
    }

    public static VfxConversion Convert(VfxPreviewSnapshot snapshot) => Convert(snapshot, collectEditorInfo: true);

    public static VfxConversion Convert(VfxPreviewSnapshot snapshot, bool collectEditorInfo)
        => Convert(snapshot, collectEditorInfo, CancellationToken.None);

    internal static VfxConversion Convert(VfxPreviewSnapshot snapshot, bool collectEditorInfo, CancellationToken cancellationToken)
        => Convert(snapshot, collectEditorInfo, cancellationToken, null);

    internal static VfxConversion Convert(VfxPreviewSnapshot snapshot, bool collectEditorInfo, CancellationToken cancellationToken,
        VfxPreviewXmlCache? previewDocuments)
    {
        var context = new ConversionContext { CollectEditorInfo = collectEditorInfo, CancellationToken = cancellationToken,
            PreviewDocuments = previewDocuments };
        var result = ConvertNested(snapshot, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0, null, context);
        var rejected = new HashSet<VfxPreviewEmitter>();
        foreach (var emitter in result.Emitters)
        {
            // Match runtime float arithmetic; a wider type could hide an intermediate overflow.
            var startMin = Math.Min(emitter.Start.Min, emitter.Start.Max);
            var startMax = Math.Max(emitter.Start.Min, emitter.Start.Max);
            foreach (var reference in emitter.References)
            {
                startMin = Math.Min(reference.Start.Min, reference.Start.Max) + startMin / reference.Speed;
                startMax = Math.Max(reference.Start.Min, reference.Start.Max) + startMax / reference.Speed;
            }
            // A parent offset can rescue an intermediate start, so check the complete reference chain here.
            if (!float.IsFinite(startMin) || !float.IsFinite(startMax) || startMin < -600 || startMax > 600)
            {
                rejected.Add(emitter);
                result.Diagnostics.Add(new(snapshot.SourcePath, emitter.Name, "StartTime/LifeTime", "Vfx.Preview.RangeUnsupported"));
            }
        }
        if (rejected.Count != 0)
        {
            result.Emitters.RemoveAll(rejected.Contains);
            foreach (var emitter in result.Emitters)
                foreach (var particleEvent in emitter.Events)
                    particleEvent.Children = particleEvent.Children.Where(child => !rejected.Contains(child)).ToArray();
        }
        result.EditorInfo = context.Editor?.Build(result) ?? VfxPreviewEditorInfo.Empty;
        return result;
    }

    private static VfxConversion ConvertNested(VfxPreviewSnapshot snapshot, HashSet<string> visited, int depth, XElement? overrides, ConversionContext context)
    {
        var key = snapshot.SourcePath.Replace('/', '\\') + "#" + (overrides == null ? "*" : string.Join("|", overrides.Elements("emitter")
            .Where(e => (string?)e.Attribute("enable") != "false").Select(e => (string?)e.Attribute("name") ?? "").Order(StringComparer.Ordinal)));
        if (depth > 8 || !visited.Add(key)) Fail("vfx_ref", "Vfx.Preview.ReferenceCycle");
        try { return ConvertContents(snapshot, visited, depth, overrides, context); }
        finally { visited.Remove(key); }
    }

    private static VfxConversion ConvertContents(VfxPreviewSnapshot snapshot, HashSet<string> visited, int depth, XElement? overrides, ConversionContext context)
    {
        var document = context.ReadDocument(snapshot);
        var editor = depth == 0 && context.CollectEditorInfo ? context.Editor = new(document) : null;
        var effect = document.Root.Element("vfx")!;
        var emitterLayers = (effect.Element("emitters")?.Elements("emitter") ?? []).Select(element => new
        {
            Name = (string?)element.Attribute("id") ?? "",
            Type = (string?)element.Attribute("type") ?? "",
            Element = element,
        }).ToArray();
        var references = document.Root.Elements("vfx_references").Elements("vfx").Select(element => new
        {
            Name = (string?)element.Attribute("inst_name") ?? "",
            Element = element,
        }).ToArray();
        var rootSelection = depth == 0 ? snapshot.RootSelection : null;
        var result = new VfxConversion { TotalEmitterCount = emitterLayers.Count(x => rootSelection == null || rootSelection.EmitterIds.Contains(x.Name, StringComparer.Ordinal)) };
        var animationBudget = context.AnimationBudget;
        var modelCache = context.Models;
        foreach (var layer in emitterLayers)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (rootSelection != null && !rootSelection.EmitterIds.Contains(layer.Name, StringComparer.Ordinal))
            {
                editor?.RecordRoot(layer.Element, VfxPreviewFieldState.Unselected);
                continue;
            }
            if (result.Emitters.Count >= 256) throw new InvalidDataException("VFX preview emitter limit");
            var emitterOverride = overrides?.Elements("emitter").FirstOrDefault(x => (string?)x.Attribute("name") == layer.Name);
            // Reference exclusions need no field parsing, resource lookup or animation checkpoint.
            if (overrides != null && emitterOverride == null || (string?)emitterOverride?.Attribute("enable") == "false")
            { result.DisabledEmitterCount++; continue; }
            var consumed = new HashSet<XElement>();
            var consumedModifiers = new HashSet<XElement>();
            var properties = ForQuality(layer.Element.Elements()).GroupBy(Name).ToDictionary(x => x.Key, x => x.Last());
            var chain = effect.Elements("behaviour_chains").Elements("behaviour_chain").FirstOrDefault(x => (string?)x.Attribute("id") == layer.Name);
            var behaviour = chain?.Elements("quality").FirstOrDefault(x => (string?)x.Attribute("value") == snapshot.Quality)
                ?? chain?.Elements("quality").FirstOrDefault(x => (string?)x.Attribute("value") == "0");
            var behaviourFields = behaviour?.Descendants().Where(x => x.Attribute("name") != null).ToArray() ?? [];
            var stack = effect.Elements("effect_render_stacks").Elements("effect_render_stack").FirstOrDefault(x => (string?)x.Attribute("id") == layer.Name);
            var render = ForQuality(stack?.Elements() ?? []).ToArray();
            var animationCheckpoint = context.Capture();
            var textureCount = result.Textures.Count;
            var modelCount = result.Models.Count;
            var materialCount = result.MeshMaterials.Count;
            var editorState = VfxPreviewFieldState.Unknown;
            var editorNotes = editor == null ? null : new List<VfxPreviewDiagnostic>();
            try
            {
                var enabled = Get("Enable");
                if (Scalar(enabled, 1) == 0)
                { result.DisabledEmitterCount++; editorState = VfxPreviewFieldState.Disabled; continue; }
                if (layer.Type is not ("continuous_mesh" or "continuous_rigid")) Fail("type", "Vfx.Preview.GeometryUnsupported");
                var native = EffekseerLayer.Create();
                native.Kind = layer.Type == "continuous_rigid" ? 1 : 0;
                var simulation = (string?)Get("ParticleSimulationSpace")?.Attribute("value")
                    ?? (native.Kind == 0 ? "WORLD_SPACE" : "LOCAL_SPACE_POSITION_ONLY");
                if (simulation is not ("LOCAL_SPACE_POSITION_ONLY" or "LOCAL_SPACE_POSITION_ROTATION" or "WORLD_SPACE"))
                    Fail("ParticleSimulationSpace", "Vfx.Preview.OrientationUnsupported");
                var emission = Curve("EmissionRateCurve", 10);
                var spawnSpacing = Scalar(Get("SpawnSpacing"), 0);
                if (!float.IsFinite(spawnSpacing) || spawnSpacing < 0) Fail("SpawnSpacing", "Vfx.Preview.RangeUnsupported");
                if (spawnSpacing > 0)
                {
                    Note("SpawnSpacing", "Vfx.Preview.DistanceEmissionCandidate");
                }
                var inheritedSpeed = Scalar(Get("SpawnInheritedSpeed"), 0);
                var inheritedDirectionBlend = Scalar(Get("SpawnInheritedDirectionBlend"), 0);
                if (!float.IsFinite(inheritedSpeed) || !float.IsFinite(inheritedDirectionBlend))
                    Fail("SpawnInheritedSpeed/SpawnInheritedDirectionBlend", "Vfx.Preview.RangeUnsupported");
                if (inheritedSpeed != 0) Note("SpawnInheritedSpeed", "Vfx.Preview.InitialMotionUncalibrated");
                var life = Curve("ParticleLifeTime", 1);
                foreach (var name in new[] { "EmissionRateCurve", "ParticleLifeTime" })
                    if (!properties.ContainsKey(name)) Note(name, "Vfx.Preview.DefaultEmitterCandidate");
                if (!float.IsFinite(emission.Minimum) || !float.IsFinite(emission.Maximum) || emission.Maximum > 10000
                    || !float.IsFinite(life.Minimum) || !float.IsFinite(life.Maximum) || life.Minimum <= 0)
                    Fail("EmissionRateCurve/ParticleLifeTime", "Vfx.Preview.RangeUnsupported");
                if (emission.Minimum < 0) Note("EmissionRateCurve", "Vfx.Preview.NegativeEmissionClamped");
                if (life.Maximum > 600) Note("ParticleLifeTime", "Vfx.Preview.LongLifetimeCandidate");
                var infinite = Scalar(Get("InfiniteLife"), 0) != 0;
                var durationNode = Get("LifeTime");
                if (!infinite && durationNode == null)
                {
                    infinite = true;
                    Note("LifeTime", "Vfx.Preview.DefaultEmitterDurationCandidate");
                }
                var duration = Range(durationNode, 1);
                var start = Range(Get("StartTime"), 0);
                if (Math.Min(start.Min, start.Max) < -600 || Math.Max(start.Min, start.Max) > 600 || duration.Min <= 0 || duration.Max > 600)
                    Fail("StartTime/LifeTime", "Vfx.Preview.RangeUnsupported");
                var burstInterval = Range(Get("BurstInterval"), 0);
                var burstLength = Range(Get("BurstIntervalLength"), 0);
                var fixedBurst = burstInterval.Min == burstInterval.Max && burstInterval.Min > 0 && burstInterval.Max <= 600
                    && burstLength.Min == burstLength.Max && burstLength.Min > 0 && burstLength.Max <= 600;
                if (!fixedBurst && (burstInterval.Min != 0 || burstInterval.Max != 0))
                    Note("BurstInterval/BurstIntervalLength", "Vfx.Preview.Unmapped");
                var maxParticles = Integer(Get("MaximumParticles"), 8192, 0, 1000000);
                if (maxParticles > TotalWarVfxSimulation.ParticleLimit) Note("MaximumParticles", "Vfx.Preview.CapacityLimited");
                if (maxParticles == 0 || emission.Maximum <= 0 && spawnSpacing == 0)
                { result.DisabledEmitterCount++; editorState = VfxPreviewFieldState.Disabled; continue; }

                var pass = render.LastOrDefault(x => (string?)x.Attribute("name") == "scene_view_type");
                if (pass != null)
                {
                    consumed.Add(pass);
                    var value = (string?)pass.Attribute("value");
                    if (value == "DISTORTION") { native.Distortion = 1; Note("scene_view_type", "Vfx.Preview.DistortionCandidate"); }
                    else if (value is "DEFERRED_DECAL" or "DEFERRED_DECAL_EMISSIVE")
                    {
                        if (native.Kind != 0) Fail("scene_view_type", "Vfx.Preview.PassUnsupported");
                        native.Kind = 2;
                        Note("scene_view_type", "Vfx.Preview.ProjectedDecalCandidate");
                    }
                    else if (value == "HALFRES_DEFERRED_ALPHA") Note("scene_view_type", "Vfx.Preview.HalfResolutionUnsupported");
                    else if (value is not ("FULLRES_DEFERRED_ALPHA" or "HALFRES_DEFERRED_ALPHA" or "HIRES_VFXUI3D"))
                        Fail("scene_view_type", "Vfx.Preview.PassUnsupported");
                }
                var scale = Curve("ParticleScale", 1, planarScale: native.Kind == 0);
                VfxModelNativeScale? modelScaleTable = null;
                if (native.Kind == 1 && Get("ParticleScale") is { } modelScale)
                {
                    try { modelScaleTable = VfxModelNativeScale.Create(modelScale.Value); }
                    catch (VfxMappingException exception) { Note(exception.Field, exception.Reason); }
                }
                if (!properties.ContainsKey("ParticleScale")) Note("ParticleScale", "Vfx.Preview.DefaultScaleCalibrated");
                var alignment = 0;
                var translation = Modifier("vs_translation");
                // The shipped geometry shader gives vs_translation precedence over velocity translation.
                var velocity = translation == null ? Modifier("vs_translation_velocity_v2") ?? Modifier("vs_translation_velocity") : null;
                if (translation != null || velocity != null)
                {
                    native.Pivot = new(Scalar(Behaviour("pivot_x"), 0.5f), Scalar(Behaviour("pivot_y"), 0.5f));
                    if (translation != null)
                    {
                        var restriction = Scalar(Behaviour("restriction"), 0);
                        if (native.Kind == 0 && restriction is 1 or 3)
                        {
                            alignment = restriction == 1 ? 6 : 2;
                            Note("restriction", restriction == 1 ? "Vfx.Preview.VerticalPlaneCandidate" : "Vfx.Preview.SpatialPlaneCandidate");
                        }
                        else if (restriction is not (0 or 2))
                            Fail("restriction", "Vfx.Preview.OrientationUnsupported");
                        if (native.Kind == 0 && restriction == 2)
                        {
                            alignment = 5;
                            Note("restriction", "Vfx.Preview.HorizontalPlaneCandidate");
                        }
                    }
                    if (native.Kind == 2 && velocity != null) Fail("orientation_mode", "Vfx.Preview.OrientationUnsupported");
                    if (velocity != null)
                    {
                        var mode = (string?)Behaviour("orientation_mode")?.Attribute("value") ?? "";
                        alignment = mode switch
                        {
                            "Camera-aligned with stretching factor" => 3,
                            "Camera-facing velocity-aligned" => 1,
                            "World-aligned" => 2,
                            "Horizontally-oriented" when native.Kind == 0 && (string?)velocity.Attribute("fragment") == "vs_translation_velocity_v2" => 4,
                            "Particle only rotating around local Y (horizontal)" when native.Kind == 0
                                && (string?)velocity.Attribute("fragment") == "vs_translation_velocity_v2" => 7,
                            _ => throw new VfxMappingException("orientation_mode", "Vfx.Preview.OrientationUnsupported"),
                        };
                    }
                }
                var stretch = BehaviourCurve("velocity_stretch_mul", 0);
                var stretchCap = Scalar(Behaviour("velocity_stretch_cap"), 1000);
                if (stretchCap < 0) Fail("velocity_stretch_cap", "Vfx.Preview.RangeUnsupported");
                var trailFields = render.Where(x => Name(x) == "enable_trail_mode").ToArray();
                var renderTrail = trailFields.LastOrDefault();
                var behaviourTrail = Behaviour("enable_trail_mode");
                if (trailFields.GroupBy(x => (string?)x.Attribute("quality") ?? "0").Any(group => group.Count() > 1)
                    || renderTrail != null && behaviourTrail != null && Scalar(renderTrail, 0) != Scalar(behaviourTrail, 0))
                    Fail("enable_trail_mode", "Vfx.Preview.AmbiguousField");
                foreach (var field in trailFields) consumed.Add(field);
                var trail = Scalar(renderTrail ?? behaviourTrail, 0) != 0;
                var minimumPixelSize = native.Kind == 0 && Modifier("vs_rendersize_limiter") != null
                    ? Scalar(Behaviour("pixelsize_limit", "vs_rendersize_limiter"), 1) : 0;
                if (!float.IsFinite(minimumPixelSize) || minimumPixelSize is < 0 or > 65504)
                    Fail("pixelsize_limit", "Vfx.Preview.RangeUnsupported");
                minimumPixelSize = (float)(Half)minimumPixelSize;
                if (velocity != null && (string?)velocity.Attribute("fragment") != "vs_translation_velocity_v2")
                {
                    if (!stretch.IsZero) Note("velocity_stretch_mul", "Vfx.Preview.StretchUnsupported");
                    stretch = new(null, "velocity_stretch_mul", 0);
                    alignment = alignment == 3 ? 0 : alignment;
                }
                else if (velocity != null) Note("vs_translation_velocity_v2", alignment == 4 ? "Vfx.Preview.HorizontalCandidate" : "Vfx.Preview.VelocityShaderCandidate");
                if (velocity != null && (string?)velocity.Attribute("fragment") != "vs_translation_velocity_v2")
                    Note("vs_translation_velocity", "Vfx.Preview.DirectionCandidate");
                var soft = Modifier("ps_soft_edge");
                if (soft != null)
                {
                    var softFactor = Scalar(Behaviour("soft_factor"), 0);
                    native.SoftDistance = Math.Max(0, softFactor);
                    var scaleStrength = Scalar(Behaviour("scale_strength"), 1);
                    native.DistortionSoftScaleStrength = scaleStrength;
                    if (native.Distortion != 0)
                    {
                        native.DistortionSoftDepth = 1;
                        native.DistortionSoftScaleStrength = scaleStrength;
                    }
                    else if (native.Kind == 0 && softFactor >= 0) native.NativeSoftDepth = 1;
                    else if (scaleStrength != 1) Note("scale_strength", "Vfx.Preview.Unmapped");
                    if (native.Distortion == 0 && softFactor < 0) Note("soft_factor", "Vfx.Preview.RangeUnsupported");
                }
                Modifier("vs_colour"); Modifier("vs_vertex_alpha");
                var textureModifier = Modifier("ps_texture");
                var normalModifier = Modifier("ps_normal");
                var colour = BehaviourCurve("curve_colour", 1);
                var alpha = BehaviourCurve("curve_alpha", 1);
                var alphaStart = Float2(Behaviour("alpha_START_offset", "vs_vertex_alpha"), Vector2.Zero);
                var alphaEnd = Float2(Behaviour("alpha_END_offset", "vs_vertex_alpha"), Vector2.Zero);
                if (alphaStart.X + alphaEnd.X >= 1 || alphaStart.Y + alphaEnd.Y >= 1)
                    Fail("alpha_START_offset/alpha_END_offset", "Vfx.Preview.RangeUnsupported");
                var initialAlpha = Curve("ParticleInitialAlpha", 1);
                VfxPreviewGradient? gradient = null;
                if (Modifier("ps_gradient") is { } gradientModifier)
                {
                    XElement? GradientField(string name)
                    {
                        var fields = gradientModifier.Elements().Where(x => Name(x) == name).ToArray();
                        if (fields.Length > 1) Fail(name, "Vfx.Preview.AmbiguousField");
                        if (fields.Length == 0) return null;
                        consumed.Add(fields[0]); return fields[0];
                    }
                    var gradientColour = MakeCurve(GradientField("curve_colour"), "ps_gradient/curve_colour", 1);
                    var gradientAlpha = MakeCurve(GradientField("curve_alpha"), "ps_gradient/curve_alpha", 1);
                    gradientColour.RequireChannels("r", "g", "b"); gradientAlpha.RequireChannels("alpha");
                    gradient = VfxPreviewGradient.Bake(gradientColour, gradientAlpha, Scalar(GradientField("alpha_mapping"), 0) != 0);
                    Note("ps_gradient", "Vfx.Preview.GradientCandidate");
                }
                if (colour.Maximum > 1) Note("curve_colour", "Vfx.Preview.HdrSceneDifference");
                if (alpha.Maximum > 1) Note("curve_alpha", "Vfx.Preview.AlphaSharpeningCandidate");
                if (initialAlpha.Maximum > 1) Note("ParticleInitialAlpha", "Vfx.Preview.AlphaSharpeningCandidate");
                var premultiplied = Scalar(Behaviour("pre_multiplied_alpha"), 0) != 0;
                VfxPreviewCurve? alphaCrush = null;
                var sharpAlphaCrush = false;
                if ((native.Kind == 0 || native.Kind == 2 && (string?)pass?.Attribute("value") == "DEFERRED_DECAL") && textureModifier != null
                    && behaviour?.Elements("modifier").Any(x => (string?)x.Attribute("fragment") == "ps_custom_shader") != true
                    && Modifier("ps_alpha_crush") != null)
                {
                    alphaCrush = MakeCurve(Behaviour("curve_alpha_crush", "ps_alpha_crush"), "curve_alpha_crush", 1);
                    alphaCrush.RequireChannels("progress");
                    sharpAlphaCrush = Scalar(Behaviour("sharp_transition", "ps_alpha_crush"), 0) != 0;
                    Note("ps_alpha_crush", "Vfx.Preview.AlphaCrushCandidate");
                }
                var coveredColourMultiplier = 1f;
                var lighting = Modifier("ps_lighting");
                var alphaLightingPass = pass == null || (string?)pass.Attribute("value") is "FULLRES_DEFERRED_ALPHA" or "HALFRES_DEFERRED_ALPHA";
                var defaultTextureEmission = native.Kind == 0 && native.Distortion == 0 && premultiplied
                    && lighting == null && behaviour?.Elements("modifier").Any(x => (string?)x.Attribute("fragment") == "ps_emissive") != true;
                var additiveField = Behaviour("additive_blend_mul");
                var additive = Scalar(additiveField, defaultTextureEmission ? 1 : 0);
                if (pass == null && native.Kind == 0 && native.Distortion == 0 && gradient == null && premultiplied && additive > 0)
                    native.EmissionShapeWeight = Math.Clamp(additive, 0, 1);
                if (defaultTextureEmission && additiveField == null) Note("pre_multiplied_alpha", "Vfx.Preview.DefaultTextureEmissionCandidate");
                if (lighting != null)
                {
                    var simple = render.LastOrDefault(node => Name(node) == "enable_simple_lighting");
                    var emissive = lighting.Elements("property").Where(node => Name(node) == "emissive").ToArray();
                    if (alphaLightingPass && native.Kind == 0 && native.Distortion == 0 && (additive == 0 || premultiplied)
                        && (string?)simple?.Attribute("value") == "true" && emissive.Length == 1
                        && (string?)emissive[0].Attribute("type") == "float"
                        && VfxCurve.TryNumber((string?)emissive[0].Attribute("value") ?? "", out var strength)
                        && strength >= 1 && strength <= 10)
                    {
                        coveredColourMultiplier = (float)strength;
                        consumed.Add(simple!); consumed.Add(emissive[0]);
                        Note("emissive", "Vfx.Preview.SimpleEmissiveCalibrated");
                    }
                    else
                    {
                        Note("ps_lighting", "Vfx.Preview.LightingCandidate");
                        var simpleLighting = (string?)simple?.Attribute("value") == "true";
                        if (alphaLightingPass && native.Kind == 0 && native.Distortion == 0 && (alignment is 0 or 3 || alignment == 1 && simpleLighting) && (additive == 0 || premultiplied)
                            && emissive.Length == 1 && (string?)emissive[0].Attribute("type") == "float"
                            && VfxCurve.TryNumber((string?)emissive[0].Attribute("value") ?? "", out var weatherEmissive)
                            && weatherEmissive >= 0 && weatherEmissive <= 8)
                        {
                            var small = Scalar(Behaviour("small_particles"), .1f);
                            var large = Scalar(Behaviour("large_particles"), .5f);
                            var directionality = Scalar(Behaviour("directionality"), .4f);
                            var average = Scalar(Behaviour("average_size"), .6f);
                            if (simpleLighting || small >= 0 && small <= 1 && large >= 0 && large <= 1
                                && (directionality is 0 or .4f || directionality == 1 && large == 0) && average > 0 && average <= 2.4f)
                            {
                                native.LightingMode = simpleLighting ? 1 : 2; native.Emissive = (float)weatherEmissive;
                                native.SmallParticles = small; native.LargeParticles = large;
                                native.Directionality = directionality; native.AverageSize = average;
                                if (simple != null) consumed.Add(simple);
                                consumed.Add(emissive[0]);
                                Note("ps_lighting", "Vfx.Preview.WeatherLightingCandidate");
                            }
                        }
                    }
                }
                if (additive != 0) Note("additive_blend_mul", "Vfx.Preview.MaterialCandidate");
                // Distortion outputs the captured background, not emitted light.
                native.Blend = native.Distortion == 0 && additive > 0 ? 2 : 1;
                if ((string?)pass?.Attribute("value") == "DEFERRED_DECAL_EMISSIVE")
                {
                    native.MaterialMode = premultiplied ? 4 : 3;
                    native.Blend = 2;
                }
                if (premultiplied) Note("pre_multiplied_alpha", "Vfx.Preview.PremultipliedCandidate");
                if (Scalar(Behaviour("overlay_colour"), 0) != 0) Note("overlay_colour", "Vfx.Preview.Unmapped");
                var uvFlip = Vector2.Zero;
                if (Modifier("vs_flip_uv") != null)
                {
                    uvFlip = new(Scalar(Behaviour("flip_u"), 0), Scalar(Behaviour("flip_v"), 0));
                    if (uvFlip.X < 0 || uvFlip.X > 1 || uvFlip.Y < 0 || uvFlip.Y > 1)
                        Fail("flip_u/flip_v", "Vfx.Preview.RangeUnsupported");
                    native.UvRotation = (string?)Behaviour("uv_orientation")?.Attribute("value") switch
                    {
                        null or "untouched" => 0,
                        "90_degrees_ccw" => 1,
                        "90_degrees_cw" => 2,
                        _ => throw new VfxMappingException("uv_orientation", "Vfx.Preview.UvUnsupported"),
                    };
                    if (native.UvRotation != 0 && (native.Kind != 0 || native.Distortion != 0))
                    {
                        native.UvRotation = 0;
                        Note("uv_orientation", "Vfx.Preview.UvUnsupported");
                    }
                    Note("vs_flip_uv", "Vfx.Preview.UvTransformCandidate");
                }
                var atlas = ReadAtlas();
                native.AtlasColumns = atlas.Columns;
                native.AtlasRows = atlas.RowRestricted ? 1 : atlas.Rows;
                native.AtlasLoop = atlas.Loop ? 1 : 0;
                native.AtlasBlend = atlas.Animated && atlas.Blend ? 1 : 0;
                var position = Curve("EmitterPosition", 0);
                var orientation = Curve("EmitterOrientation", 0);
                var speed = Curve("ParticleInitialSpeed", 0);
                var box = Curve("SpawnAreaBox", 0);
                var radius = Curve("SpawnAreaCurve", 0);
                var angles = Vector(Get("ParticleOrientationMin"), Vector3.Zero);
                var anglesMax = Vector(Get("ParticleOrientationMax"), angles);
                var spinMin = Vector(Get("ParticleSpin_Min"), Vector3.Zero);
                var spinMax = Vector(Get("ParticleSpin_Max"), spinMin);
                float SpinDirection(string axis) => (string?)Get("ParticleSpinDirection" + axis)?.Attribute("value") switch
                {
                    "CW" => -1,
                    "CCW" => 1,
                    null or "RANDOM" => 0,
                    _ => UnsupportedSpinDirection(axis),
                };
                float UnsupportedSpinDirection(string axis)
                {
                    Note("ParticleSpinDirection" + axis, "Vfx.Preview.OrientationUnsupported");
                    return float.NaN;
                }
                var spinDirection = new Vector3(SpinDirection("X"), SpinDirection("Y"), SpinDirection("Z"));
                var relative = Get("ParticleRelativeSpawnOrientation");
                var relativeMode = (string?)relative?.Attribute("value") switch
                {
                    null when relative == null => (int?)null,
                    "DISABLED" => 0,
                    "RELATIVE_TO_VFX_UP_AXIS" => 1,
                    "RELATIVE_TO_TERRAIN_NORMAL" => 2,
                    _ => throw new VfxMappingException("ParticleRelativeSpawnOrientation", "Vfx.Preview.OrientationUnsupported"),
                };
                foreach (var legacyName in new[] { "RelativeParticleOrientation", "ParticleRelativeOrientation" })
                    if (Get(legacyName) is { } legacyRelative)
                    {
                        var legacyMode = Scalar(legacyRelative, 0) != 0 ? 1 : 0;
                        if (relativeMode.HasValue && relativeMode.Value != legacyMode)
                            Fail("ParticleRelativeSpawnOrientation", "Vfx.Preview.AmbiguousField");
                        relativeMode = legacyMode;
                    }
                var relativeOrientation = relativeMode == 1;
                var spawnAngle = Get("SpawnAngle");
                var angle = spawnAngle == null ? new VfxRange(0, 0) : new VfxRange(Attribute(spawnAngle, "x", 0), Attribute(spawnAngle, "y", 0));
                var spawnSpread = Get("SpawnSpread");
                var spread = Range(spawnSpread, 0);
                var spawnArc = Get("SpawnArc");
                var zeroArc = spawnArc != null && Attribute(spawnArc, "x", 0) == 0 && Attribute(spawnArc, "y", 0) == 0;
                var zeroSpawnArc = zeroArc && spawnSpread == null && Scalar(properties.GetValueOrDefault("SpreadMethod"), 0) == 0;
                var defaultVerticalSpawn = native.Kind == 0 && alignment == 4 && orientation.IsZero && spawnAngle == null && spawnSpread == null
                    && Scalar(properties.GetValueOrDefault("UseSurfaceNormal"), 0) == 0 && Scalar(properties.GetValueOrDefault("SpreadMethod"), 0) == 0;
                if (defaultVerticalSpawn)
                {
                    // Native pole residue has a fixed azimuth; randomizing it rotates the entire velocity plane.
                    angle = new(0, 0); spread = new(90, 90);
                    Note("SpawnAngle/SpawnSpread", "Vfx.Preview.DefaultSpawnCandidate");
                }
                var spreadMethod = Get("SpreadMethod");
                if (Scalar(spreadMethod, 0) != 0) Note("SpreadMethod", "Vfx.Preview.Unmapped");
                var spawnDirection = (string?)properties.GetValueOrDefault("ParticleRelativeSpawnDirection")?.Attribute("value");
                var radialDecalSpawn = native.Kind == 2 && layer.Type == "continuous_mesh"
                    && (string?)pass?.Attribute("value") == "DEFERRED_DECAL"
                    && simulation is "WORLD_SPACE" or "LOCAL_SPACE_POSITION_ONLY";
                // Continuous-mesh decals use the same native birth direction; speed is sampled once per birth.
                var radialSpawnDirection = native.Distortion == 0
                    && (radialDecalSpawn || native.Kind == 0 && simulation == "WORLD_SPACE" && speed.IsConstant)
                    && spawnSpacing == 0 && inheritedSpeed == 0
                    && (string?)spreadMethod?.Attribute("value") == "false"
                    && Scalar(properties.GetValueOrDefault("UseSurfaceNormal"), 0) == 0
                    && orientation.IsZero && position.IsConstant && box.IsZero && radius.Minimum >= 0
                    && spawnDirection is null or "RELATIVE_TO_VFX_UP_AXIS"
                    && Scalar(properties.GetValueOrDefault("EventEnable"), 0) == 0;
                var nativeSpawnArea = radialSpawnDirection || native.Kind == 0 && native.Distortion != 0 && layer.Type == "continuous_mesh"
                    && (string?)pass?.Attribute("value") == "DISTORTION"
                    && simulation is "WORLD_SPACE" or "LOCAL_SPACE_POSITION_ONLY"
                    && spawnSpacing == 0 && inheritedSpeed == 0 && (string?)spreadMethod?.Attribute("value") == "false"
                    && Scalar(properties.GetValueOrDefault("UseSurfaceNormal"), 0) == 0
                    && orientation.IsZero && position.IsConstant && box.IsZero && radius.Minimum >= 0
                    && spawnDirection is null or "RELATIVE_TO_VFX_UP_AXIS"
                    && Scalar(properties.GetValueOrDefault("EventEnable"), 0) == 0;
                var spawnAreaArc = nativeSpawnArea && spawnArc != null
                    ? new VfxRange(Attribute(spawnArc, "x", 0), Attribute(spawnArc, "y", 0)) : (VfxRange?)null;
                var spawnAreaAngle = angle;
                if (radialSpawnDirection)
                {
                    Get("ParticleRelativeSpawnDirection");
                    Note("SpreadMethod", "Vfx.Preview.SpawnCandidate");
                }
                else if (native.Kind == 0 && spreadMethod != null && Scalar(spreadMethod, 1) == 0)
                    Note("SpreadMethod/ParticleRelativeSpawnDirection", "Vfx.Preview.Unmapped");
                var legacySpreadSpawn = native.Kind == 0 && spawnSpread != null
                    && (spawnArc == null || zeroArc && spawnAngle == null && spawnArc.Attribute("x") != null && spawnArc.Attribute("y") != null)
                    && Scalar(properties.GetValueOrDefault("SpreadMethod"), 0) == 0
                    && Scalar(properties.GetValueOrDefault("UseSurfaceNormal"), 0) == 0
                    && (string?)relative?.Attribute("value") is null or "DISABLED" or "RELATIVE_TO_VFX_UP_AXIS";
                if (legacySpreadSpawn)
                {
                    angle = new(0, 360);
                    if (Get("SpreadArc") is { } spreadArc)
                    {
                        var arc = Range(spreadArc, 0);
                        // Native legacy azimuth turns from +X toward -Z; SpawnSpread remains elevation.
                        angle = new(-arc.Min, -arc.Max);
                        Note("SpreadArc", "Vfx.Preview.SpawnCandidate");
                    }
                }
                if (spawnArc != null)
                    Note("SpawnArc", zeroSpawnArc ? "Vfx.Preview.ZeroSpawnArcCandidate"
                        : legacySpreadSpawn || spawnAreaArc != null ? "Vfx.Preview.SpawnCandidate" : "Vfx.Preview.Unmapped");
                var flatTerrain = false;
                var spawnOnTerrain = false;
                var modelTerrainBirth = false;
                var terrainSpawnDirection = 2;
                if (relativeMode == 2)
                {
                    spawnOnTerrain = Scalar(Get("ParticlesSpawnOnTerrain"), 0) != 0;
                    if ((string?)relative.Attribute("value") != "RELATIVE_TO_TERRAIN_NORMAL"
                        || !speed.IsZero || !orientation.IsZero
                        || native.Kind == 2 && (!position.IsZero
                            || (!spawnOnTerrain || simulation != "WORLD_SPACE") && (!box.IsZero || !radius.IsZero))
                        || !Curve("ForceA", 0).IsZero || !Curve("ForceB", 0).IsZero || !Curve("ForceC", 0).IsZero
                        || Scalar(Get("ParticlesFollowTerrain"), 0) != 0 || Scalar(Get("AllowEmitFromMesh"), 0) != 0
                        || Scalar(Get("EffectedByWindFactor"), 0) != 0 || Scalar(Get("WindVarianceFactor"), 0) != 0)
                        Fail("ParticleRelativeSpawnOrientation", "Vfx.Preview.OrientationUnsupported");
                    var direction = (string?)Get("ParticleRelativeSpawnDirection")?.Attribute("value");
                    if (direction is not (null or "RELATIVE_TO_VFX_UP_AXIS" or "RELATIVE_TO_TERRAIN_NORMAL")
                        && !(native.Kind == 2 && spawnOnTerrain && simulation == "WORLD_SPACE" && direction == "DISABLED"))
                        Fail("ParticleRelativeSpawnDirection", "Vfx.Preview.OrientationUnsupported");
                    if (native.Kind == 2)
                        terrainSpawnDirection = direction switch { "DISABLED" => 0, "RELATIVE_TO_TERRAIN_NORMAL" => 2, _ => 1 };
                    if (native.Kind == 2 && spawnOnTerrain && simulation == "WORLD_SPACE" && terrainSpawnDirection != 2
                        && (Get("SpreadMethod") is not { } terrainSpread || Scalar(terrainSpread, 1) != 0))
                        Fail("SpreadMethod", "Vfx.Preview.OrientationUnsupported");
                    if (native.Kind == 2 && spawnOnTerrain && simulation == "WORLD_SPACE"
                        && (spinMin != Vector3.Zero || spinMax != Vector3.Zero))
                        Fail("ParticleSpin_Min/ParticleSpin_Max", "Vfx.Preview.OrientationUnsupported");
                    flatTerrain = true;
                    Note("ParticleRelativeSpawnOrientation", native.Kind == 2 ? "Vfx.Preview.TerrainDecalCandidate" : "Vfx.Preview.FlatTerrainCandidate");
                }
                var force = Curve("ForceA", 0);
                var additionalForces = false;
                foreach (var name in new[] { "ForceB", "ForceC" })
                    if (!Curve(name, 0).IsZero) { additionalForces = true; Note(name, "Vfx.Preview.ForceUnsupported"); }
                var spinDeceleration = Curve("ParticleSpinDeceleration", 0);
                if (!spinDeceleration.IsZero) additionalForces = true;
                var deceleration = Curve("ParticleDeceleration", 0);
                var verticalAcceleration = 0f;
                float? constantForceDamping = null;
                var particleForceCurve = false;
                var modelConstantForce = false;
                if (!force.IsZero)
                {
                    if (native.Kind == 1 && simulation is "WORLD_SPACE" or "LOCAL_SPACE_POSITION_ONLY"
                        && force.TryConstantVector(out _) && Curve("ForceB", 0).IsZero && Curve("ForceC", 0).IsZero
                        && deceleration.IsConstant && deceleration.Range("x", 0) is var modelDamping
                        && modelDamping.Min >= 0 && modelDamping.Min == modelDamping.Max
                        && Scalar(Get("ParticlesFollowTerrain"), 0) == 0 && Scalar(Get("EnableCollisions"), 0) == 0
                        && Scalar(Get("EffectedByWindFactor"), 0) == 0 && Scalar(Get("WindVarianceFactor"), 0) == 0
                        && Scalar(Get("AllowEmitFromMesh"), 0) == 0 && Scalar(Get("EventEnable"), 0) == 0)
                    {
                        modelConstantForce = true;
                        Note("ForceA/ParticleDeceleration", "Vfx.Preview.ModelConstantForceCandidate");
                    }
                    else if (native.Kind == 0 && speed.IsZero && deceleration.IsZero && orientation.IsZero
                        && force.TryConstantVector(out var constantForce) && constantForce.X == 0 && constantForce.Z == 0)
                    {
                        // Native force-only controls support a 1/100 unit conversion; other motion remains diagnosed.
                        verticalAcceleration = constantForce.Y * 0.01f;
                        Note("ForceA", "Vfx.Preview.VerticalForceCalibrated");
                    }
                    else if (native.Kind == 0 && native.Distortion == 0 && alignment == 0 && !additionalForces
                        && orientation.IsZero && position.IsZero && box.IsZero && radius.IsZero && life.Maximum <= 4
                        && spawnAngle != null && spread.Min == 90 && spread.Max == 90
                        // Legacy azimuth spans a circle at this vertical pole; retain its spawn sampling.
                        && (angle.Min == 0 && angle.Max == 0
                            || legacySpreadSpawn && Get("SpreadArc") == null
                                && Attribute(spawnAngle, "x", 0) == 0 && Attribute(spawnAngle, "y", 0) == 0)
                        && (string?)relative?.Attribute("value") == "RELATIVE_TO_VFX_UP_AXIS" && Scalar(Get("SpreadMethod"), 0) == 0
                        && speed.IsConstant && speed.Range("x", 0) is var initialSpeed && initialSpeed.Min == initialSpeed.Max
                        && deceleration.IsConstant && deceleration.Range("x", 0) is var drag && drag.Min == drag.Max
                        && force.TryConstantVector(out var combinedForce) && combinedForce.X == 0 && combinedForce.Z == 0
                        && (initialSpeed.Min is 0 or 1 && drag.Min is 1 or 5 && Math.Abs(combinedForce.Y) <= 200
                            || initialSpeed.Min is 0 or .5f or 1 or 2 && drag.Min is .5f or 1 or 2 or 5 or 10 && Math.Abs(combinedForce.Y) <= 50
                            || initialSpeed.Min is .75f or 1.5f && drag.Min is .75f or 1.5f or 3 or 7.5f && Math.Abs(combinedForce.Y) <= 50)
                        && Scalar(Get("EffectedByWindFactor"), 0) == 0 && Scalar(Get("WindVarianceFactor"), 0) == 0
                        && Scalar(Get("AllowEmitFromMesh"), 0) == 0 && Scalar(Get("ParticlesFollowTerrain"), 0) == 0 && Scalar(Get("EventEnable"), 0) == 0)
                    {
                        verticalAcceleration = combinedForce.Y * 0.01f;
                        constantForceDamping = drag.Min;
                        Note("ForceA/ParticleDeceleration", "Vfx.Preview.ConstantForceDampingCandidate");
                    }
                    else if (native.Kind == 0 && native.Distortion == 0 && alignment is 0 or 4 && !additionalForces
                        && orientation.IsZero && radius.IsZero && life.Minimum > 0 && life.Maximum <= .4f
                        && position.TryConstantVector(out var defaultPosition) && box.TryConstantVector(out var defaultBox)
                        && (defaultPosition == Vector3.Zero && defaultBox == Vector3.Zero || defaultPosition == new Vector3(0, 4, 0) && defaultBox == new Vector3(2, 3, 2))
                        && spawnAngle == null && spawnSpread == null && relative == null && Scalar(Get("SpreadMethod"), 0) == 0
                        && Scalar(properties.GetValueOrDefault("UseSurfaceNormal"), 0) == 0
                        && speed.IsConstant && speed.Range("x", 0) is { Min: -10, Max: -10 }
                        && deceleration.IsConstant && deceleration.Range("x", 0) is { Min: .1f, Max: .1f }
                        && force.TryConstantVector(out var defaultForce) && defaultForce.X == 0 && defaultForce.Z == 0 && Math.Abs(defaultForce.Y + 981) <= .0001f
                        && Scalar(Get("EffectedByWindFactor"), 0) == 0 && Scalar(Get("WindVarianceFactor"), 0) == 0
                        && Scalar(Get("AllowEmitFromMesh"), 0) == 0 && Scalar(Get("ParticlesFollowTerrain"), 0) == 0 && Scalar(Get("EventEnable"), 0) == 0)
                    {
                        verticalAcceleration = defaultForce.Y * .01f;
                        constantForceDamping = .1f;
                        Note("ForceA/ParticleInitialSpeed/ParticleDeceleration", "Vfx.Preview.DefaultVerticalMotionCandidate");
                    }
                    else if (native.Kind == 0 && orientation.IsZero
                        && Curve("ForceB", 0).IsZero && Curve("ForceC", 0).IsZero
                        && Scalar(Get("AllowEmitFromMesh"), 0) == 0 && Scalar(Get("ParticlesFollowTerrain"), 0) == 0
                        && Scalar(Get("UseSurfaceNormal"), 0) == 0)
                    {
                        // Force and drag curves use particle age, independently of spawn rate and direction.
                        particleForceCurve = true;
                        Note("ForceA/ParticleDeceleration", "Vfx.Preview.ParticleForceCurveCandidate");
                        // Native adds wind separately; missing wind must not erase the supported force.
                        foreach (var name in new[] { "EffectedByWindFactor", "WindVarianceFactor" })
                            if (Scalar(Get(name), 0) != 0) Note(name, "Vfx.Preview.Unmapped");
                    }
                    else Note("ForceA", "Vfx.Preview.ForceUnsupported");
                }
                if (constantForceDamping == null)
                {
                    if (!speed.IsZero) Note("ParticleInitialSpeed", "Vfx.Preview.InitialMotionUncalibrated");
                    if (!deceleration.IsZero) Note("ParticleDeceleration", "Vfx.Preview.DragCandidate");
                    if (!speed.IsZero || !orientation.IsZero || !box.IsZero || !radius.IsZero)
                        Note("ParticleInitialSpeed/SpawnArea", "Vfx.Preview.SpawnCandidate");
                }
                var loop = Scalar(Get("EmissionLoopAnim"), 0) != 0;
                var initialParameterLoop = Scalar(Get("ParticleInitialParamLoopAnim"), 0) != 0;
                if (initialParameterLoop && native.Kind != 0) Note("ParticleInitialParamLoopAnim", "Vfx.Preview.EmitterTimelineCandidate");
                foreach (var name in new[] { "ParticleLifeTimeLoopAnim", "ParticleInitialSpeedLoopAnim", "SpawnAreaLoopAnim" })
                    if (Scalar(Get(name), 0) != 0) Note(name, "Vfx.Preview.EmitterTimelineCandidate");
                foreach (var name in new[] { "AllowEmitFromMesh", "ParticlesFollowTerrain" })
                    if (Scalar(Get(name), 0) != 0) Note(name, "Vfx.Preview.Unmapped");
                var particleCull = Get("ParticleCullDistances");
                Vector4? particleCullDistances = null;
                if (native.Kind is 0 or 1 && particleCull != null)
                {
                    var distances = new Vector4(Number((string?)particleCull.Attribute("x"), "ParticleCullDistances"),
                        Number((string?)particleCull.Attribute("y"), "ParticleCullDistances"),
                        Number((string?)particleCull.Attribute("z"), "ParticleCullDistances"),
                        Number((string?)particleCull.Attribute("w"), "ParticleCullDistances"));
                    if (!float.IsFinite(distances.W) || distances.X < 0 || distances.X > distances.Y
                        || distances.Y > distances.Z || distances.Z > distances.W)
                        Fail("ParticleCullDistances", "Vfx.Preview.RangeUnsupported");
                    particleCullDistances = distances;
                }
                Get("CullDistances");

                var eventNodes = properties.Values.Where(x => x.Name == "property_event"
                    && Name(x) is "OnParticleSpawn" or "OnParticleDeath").ToArray();
                var eventHelper = native.Kind == 1 && !layer.Element.Descendants("rigid_entry").Any() && eventNodes.Length > 0;
                var events = new List<VfxPreviewParticleEvent>();
                var eventEnabled = Scalar(Get("EventEnable"), 0) != 0;
                if (eventEnabled && native.Kind == 1 && !speed.IsZero)
                    Note("property_event/position", "Vfx.Preview.ParticleEventMotionCandidate");
                if (eventEnabled && native.Kind != 1)
                {
                    Note("EventEnable", "Vfx.Preview.ParticleEventEmitterUnsupported");
                    eventEnabled = false;
                }
                foreach (var node in eventNodes)
                {
                    consumed.Add(node);
                    if (!eventEnabled || string.IsNullOrWhiteSpace((string?)node.Attribute("vfx_name"))) continue;
                    var delay = Attribute(node, "spawn_event_time_offset", 0);
                    if (Math.Abs(delay) > 600) Fail(Name(node), "Vfx.Preview.RangeUnsupported");
                    bool Track(string name) => (string?)node.Attribute(name) == "true";
                    events.Add(new((string)node.Attribute("vfx_name")!, Name(node) == "OnParticleDeath",
                        Name(node) == "OnParticleDeath" ? 0 : Math.Max(0, delay), Track("track_position"), Track("track_scale"),
                        Track("track_colour"), Track("track_alpha"), Track("track_lifetime"), (string?)node.Attribute("track_orientation_mode") ?? "orientation"));
                }
                string texturePath = "";
                VfxPreviewModel? model = null;
                VfxScrollingMeshMaterial? meshMaterial = null;
                var additionalParts = new List<(VfxPreviewModel Model, string Texture, VfxScrollingMeshMaterial? Material)>();
                var modelVariants = new List<List<(VfxPreviewModel Model, string Texture, VfxScrollingMeshMaterial? Material)>>();
                var modelTextures = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                if (native.Kind == 1 && !eventHelper)
                {
                    var rigid = Get("RigidData");
                    var entries = rigid?.Elements("rigid_entry").ToArray() ?? [];
                    if (entries.Length is < 1 or > 32)
                        Fail("RigidData", "Vfx.Preview.GeometryUnsupported");
                    foreach (var entry in entries)
                    {
                        var modelPath = (string?)entry.Element("ModelFile")?.Attribute("value") ?? "";
                        var animation = (string?)entry.Element("AnimFile")?.Attribute("value") ?? "";
                        var animationSpeed = Scalar(entry.Element("AnimSpeed"), 1);
                        var pivot = Vector(entry.Element("PivotPoint"), new(.5f));
                        if (!string.IsNullOrWhiteSpace(animation) && pivot != new Vector3(.5f))
                            Fail("RigidData/PivotPoint", "Vfx.Preview.GeometryUnsupported");
                        var first = ReadModel(modelPath, 0, pivot, animation, animationSpeed, life.Maximum);
                        var parts = new List<(VfxPreviewModel Model, string Texture, VfxScrollingMeshMaterial? Material)>
                            { (first.Model, first.Texture, first.Material) };
                        for (var partIndex = 1; partIndex < first.PartCount; partIndex++)
                        {
                            var part = ReadModel(modelPath, partIndex, pivot, animation, animationSpeed, life.Maximum);
                            parts.Add((part.Model, part.Texture, part.Material));
                        }
                        modelVariants.Add(parts);
                    }
                    if (modelVariants.SelectMany(parts => parts).Any(part => part.Model.RigidAnimation))
                        Note("RigidData/Animation", "Vfx.Preview.RigidAnimationCandidate");
                    if (entries.Any(entry => !string.IsNullOrWhiteSpace((string?)entry.Element("AnimFile")?.Attribute("value")))
                        && modelVariants.SelectMany(parts => parts).Any(part => !part.Model.RigidAnimation))
                        Note("RigidData/Animation", "Vfx.Preview.ModelAnimationCandidate");
                    if (modelVariants.SelectMany(parts => parts).Any(part => part.Model.OpaqueMaterial && part.Material == null))
                        Note("RigidData/material", "Vfx.Preview.OpaqueCharacterCandidate");
                    foreach (var shader in modelVariants.SelectMany(parts => parts).Select(part => part.Model.ApproximateMaterialShader).OfType<string>().Distinct())
                        Note(shader, "Vfx.Preview.ModelMaterialFallback");
                    if (relativeMode == 1 && Scalar(Get("ParticlesSpawnOnTerrain"), 0) != 0)
                    {
                        spawnOnTerrain = true;
                        modelTerrainBirth = simulation is "WORLD_SPACE" or "LOCAL_SPACE_POSITION_ONLY"
                            && modelVariants.SelectMany(parts => parts).All(part => part.Model.AnimationVertices == null);
                        Note("ParticlesSpawnOnTerrain", modelTerrainBirth
                            ? "Vfx.Preview.ModelTerrainBirthCandidate" : "Vfx.Preview.ModelTerrainBirthUnsupported");
                    }
                    (model, texturePath, meshMaterial) = modelVariants[0][0];
                    if (modelVariants.SelectMany(parts => parts).Any(part => (part.Material == null) != (meshMaterial == null)))
                        Fail("RigidData", "Vfx.Preview.MaterialUnsupported");
                    if (result.MeshMaterials.Count + modelVariants.Sum(parts => parts.Count(part => part.Material != null)) > 256)
                        Fail("RigidData", "Vfx.Preview.RangeUnsupported");
                    additionalParts.AddRange(modelVariants[0].Skip(1));
                    if (modelVariants.Sum(parts => parts.Count) > 1)
                    {
                        foreach (var path in modelVariants.SelectMany(parts => parts).SelectMany(part => part.Material?.TexturePaths ?? [part.Texture]).Distinct(StringComparer.OrdinalIgnoreCase))
                            modelTextures.Add(path, Read(path));
                    }
                    if (entries.Length > 1)
                    {
                        Note("RigidData", "Vfx.Preview.ModelVariantsCandidate");
                    }
                    Note("RigidData", meshMaterial == null ? "Vfx.Preview.ModelMaterialCandidate"
                        : meshMaterial.Parameters.Length == VfxVertexAnimatedMeshMaterial.ParameterCount ? "Vfx.Preview.VertexAnimatedMaterialCandidate"
                        : meshMaterial.Parameters.Length == VfxTidecallMaterial.ParameterCount ? "Vfx.Preview.TidecallMaterialCandidate"
                        : meshMaterial.Parameters.Length == VfxRigidBasicDitherMaterial.ParameterCount ? "Vfx.Preview.RigidBasicDitherCandidate"
                        : meshMaterial.Parameters.Length == 60 ? "Vfx.Preview.RigidDissolveCandidate"
                        : meshMaterial.Parameters.Length == 12 ? "Vfx.Preview.GrowingVinesCandidate"
                        : meshMaterial.Parameters.Length == 56 ? "Vfx.Preview.SolidMeshCandidate"
                        : meshMaterial.Parameters.Length == 32 ? "Vfx.Preview.PremultiplyAnimatedMaterialCandidate"
                        : meshMaterial.Parameters.Length == 28 ? "Vfx.Preview.SoulPanningMaterialCandidate"
                        : meshMaterial.Parameters.Length == 40 ? "Vfx.Preview.GemstoneCandidate"
                        : meshMaterial.Parameters.Length == 55 ? "Vfx.Preview.BasicMeshCandidate" : "Vfx.Preview.ScrollingMeshCandidate");
                }
                else if (!eventHelper)
                {
                    if (native.Kind == 0 && Modifier("ps_custom_shader") != null)
                    {
                        if (textureModifier != null || native.Distortion != 0 || gradient != null || normalModifier != null
                            || Modifier("ps_lighting") != null || Modifier("ps_emissive") != null)
                            Fail("ps_custom_shader", "Vfx.Preview.MaterialUnsupported");
                        var path = (string?)Behaviour("material", "ps_custom_shader")?.Attribute("path") ?? "";
                        var spriteMaterial = VfxCustomSpriteMaterial.Read(Read(path));
                        meshMaterial = spriteMaterial.Material;
                        if (result.MeshMaterials.Count >= 256) Fail("material", "Vfx.Preview.RangeUnsupported");
                        texturePath = meshMaterial.TexturePaths[0];
                        native.SoftDistance = spriteMaterial.SoftDistance;
                        Note("ps_custom_shader", spriteMaterial.Diagnostic);
                    }
                    else texturePath = (string?)Behaviour(native.Distortion != 0 ? "normal" : "diffuse", "ps_texture")?.Attribute("path") ?? "";
                    if (native.Kind == 2) model = VfxDecalVolume.Create(native.Pivot);
                }
                Vector2? meshLoop = null;
                if (meshMaterial != null && maxParticles == 1 && speed.IsZero && force.IsZero && deceleration.IsZero && !additionalForces
                    && properties.TryGetValue("ParticleLoopParams", out var loopNode))
                {
                    var from = Attribute(loopNode, "x", 0); var to = Attribute(loopNode, "y", 0);
                    if (from >= 0 && from < to && to <= 1)
                    {
                        meshLoop = new(from, to); consumed.Add(loopNode);
                        Note("ParticleLoopParams", "Vfx.Preview.RigidLoopCandidate");
                    }
                }
                byte[] ModelTexture(string path) => modelTextures.GetValueOrDefault(path) ?? Read(path);
                var untexturedColour = native.Kind == 0 && native.Distortion == 0 && textureModifier == null
                    && !(behaviour?.Elements("modifier").Any(x => ((string?)x.Attribute("fragment")) is { } fragment
                        && fragment.StartsWith("ps_", StringComparison.Ordinal)
                        && fragment is not ("ps_soft_edge" or "ps_lighting" or "ps_emissive" or "ps_normal")) ?? false);
                var geometryDistortion = native.Distortion != 0 && string.IsNullOrWhiteSpace(texturePath);
                var textureBytes = eventHelper || geometryDistortion ? null : untexturedColour ? new byte[] { 255, 255, 255, 255 } : meshMaterial == null ? ModelTexture(texturePath) : null;
                if (untexturedColour)
                {
                    texturePath = "vertex_colour";
                    Note("ps_texture", "Vfx.Preview.VertexColourCalibrated");
                }
                if (meshMaterial != null)
                {
                    var indexes = new List<int>();
                    foreach (var path in meshMaterial.TexturePaths)
                    {
                        var bytes = ModelTexture(path);
                        indexes.Add(result.Textures.Count); result.Textures.Add(bytes); result.TexturePaths.Add(path);
                        result.TextureUsages.Add(meshMaterial.Parameters.Length == VfxRigidBasicDitherMaterial.ParameterCount && indexes.Count == 1 ? VfxTextureUsage.Diffuse
                            : meshMaterial.Parameters.Length == 60 && indexes.Count == 1 ? VfxTextureUsage.OpaqueDiffuse
                            : meshMaterial.Parameters.Length == 24 && indexes.Count == 4
                            ? VfxTextureUsage.Diffuse : VfxTextureUsage.MaterialValue);
                    }
                    native.MeshMaterialIndex = result.MeshMaterials.Count;
                    result.MeshMaterials.Add(new(indexes.ToArray(), meshMaterial.Parameters));
                    native.TextureIndex = indexes[0];
                }
                else native.TextureIndex = geometryDistortion ? -1 : result.Textures.Count;
                if (model != null) { native.ModelIndex = result.Models.Count; native.OpaqueModel = model.OpaqueMaterial ? 1 : 0; }
                native.CameraFacing = native.Kind == 0 && alignment is 0 or 6 ? 1 : 0;
                if (native.NativeSoftDepth != 0 && (native.MeshMaterialIndex >= 0 || !premultiplied
                    || native.DistortionSoftScaleStrength is < 0 or > 1 || native.SoftDistance > 65504))
                {
                    native.NativeSoftDepth = 0;
                    if (native.DistortionSoftScaleStrength != 1) Note("scale_strength", "Vfx.Preview.Unmapped");
                    if (native.SoftDistance > 65504) Note("soft_factor", "Vfx.Preview.RangeUnsupported");
                }
                var extendedSpriteDamping = spinDeceleration.Minimum < 0 || spinDeceleration.Maximum > 5;
                float? spriteConstantDamping = null;
                VfxSpriteNativeSpinDamping? spriteNativeDamping = null;
                if (native.Kind == 0 && spinDeceleration.IsConstant && extendedSpriteDamping)
                {
                    try { spriteConstantDamping = VfxSpriteConstantSpinDamping.Create(Get("ParticleSpinDeceleration")!.Value); }
                    catch (VfxMappingException exception) { Note(exception.Field, exception.Reason); }
                }
                if (native.Kind == 0 && !spinDeceleration.IsConstant && !spinDeceleration.HasBezierSegments
                    && (extendedSpriteDamping || simulation == "LOCAL_SPACE_POSITION_ROTATION"))
                {
                    try { spriteNativeDamping = VfxSpriteNativeSpinDamping.Create(Get("ParticleSpinDeceleration")!.Value); }
                    catch (VfxMappingException exception) { Note(exception.Field, exception.Reason); }
                }
                var dynamicSpinDamping = (!spinDeceleration.IsConstant && !spinDeceleration.HasBezierSegments
                    && spinDeceleration.Minimum >= 0 && spinDeceleration.Maximum <= 5 || spriteConstantDamping.HasValue || spriteNativeDamping != null)
                    && native.Kind == 0 && meshMaterial == null && meshLoop == null && eventNodes.Length == 0
                    && (relativeMode is null or 0) && (simulation is "WORLD_SPACE" or "LOCAL_SPACE_POSITION_ONLY")
                    && angles.X == 0 && angles.Y == 0 && anglesMax.X == 0 && anglesMax.Y == 0
                    && spinMin.X == 0 && spinMin.Y == 0 && spinMax.X == 0 && spinMax.Y == 0
                    && (spinMin.Z != 0 || spinMax.Z != 0) && float.IsFinite(spinDirection.Z);
                var spriteFullLocal = simulation == "LOCAL_SPACE_POSITION_ROTATION" && orientation.IsConstant && !flatTerrain
                    && (spriteConstantDamping.HasValue || spriteNativeDamping != null) && (relativeMode is null or 0)
                    && angles.X == 0 && angles.Y == 0 && anglesMax.X == 0 && anglesMax.Y == 0
                    && spinMin.X == 0 && spinMin.Y == 0 && spinMax.X == 0 && spinMax.Y == 0;
                var complexSpinDamping = !dynamicSpinDamping && !spinDeceleration.IsZero
                    && (spriteNativeDamping == null || spriteFullLocal)
                    && (spinDeceleration.IsConstant && spinDeceleration.Minimum >= 0 && spinDeceleration.Maximum <= 5
                        || spriteConstantDamping.HasValue || spriteNativeDamping != null)
                    && (spinMin.X != 0 || spinMin.Y != 0 || spinMax.X != 0 || spinMax.Y != 0
                        || spriteConstantDamping.HasValue || spriteNativeDamping != null)
                    && native.Kind == 0 && meshMaterial == null && meshLoop == null && eventNodes.Length == 0
                    && (relativeMode is null or 0 or 1) && ((simulation is "WORLD_SPACE" or "LOCAL_SPACE_POSITION_ONLY") || spriteFullLocal)
                    && (spinMin != Vector3.Zero || spinMax != Vector3.Zero)
                    && (spinMin.X == 0 && spinMax.X == 0 || float.IsFinite(spinDirection.X))
                    && (spinMin.Y == 0 && spinMax.Y == 0 || float.IsFinite(spinDirection.Y))
                    && (spinMin.Z == 0 && spinMax.Z == 0 || float.IsFinite(spinDirection.Z));
                VfxModelNativeCurve? nativeModelDamping = null;
                VfxModelNativeOrientation? nativeModelOrientation = null;
                VfxModelNativeMotion? nativeModelMotion = null;
                var modelDampingSupported = spinDeceleration.IsConstant || !spinDeceleration.HasBezierSegments;
                var fullLocalModel = native.Kind == 1 && simulation == "LOCAL_SPACE_POSITION_ROTATION"
                    && (relativeMode is null or 0 or 1) && meshLoop == null;
                if (native.Kind == 1 && meshLoop == null && (eventNodes.Length == 0 || fullLocalModel))
                {
                    if (spinDeceleration.HasBezierSegments || spinDeceleration.Minimum < 0 || spinDeceleration.Maximum > 5)
                    {
                        try
                        {
                            nativeModelDamping = VfxModelNativeCurve.Create(Get("ParticleSpinDeceleration")!.Value, "ParticleSpinDeceleration");
                            modelDampingSupported = true;
                        }
                        catch (VfxMappingException exception)
                        {
                            modelDampingSupported = spinDeceleration.IsConstant;
                            Note(exception.Field, exception.Reason);
                        }
                    }
                    if (simulation == "LOCAL_SPACE_POSITION_ROTATION" && !orientation.IsConstant)
                    {
                        try { nativeModelOrientation = VfxModelNativeOrientation.Create(Get("EmitterOrientation")!.Value); }
                        catch (VfxMappingException exception) { Note(exception.Field, exception.Reason); }
                    }
                }
                if (fullLocalModel && (!position.IsConstant || !orientation.IsConstant)
                    && (orientation.IsConstant || nativeModelOrientation != null))
                {
                    try { nativeModelMotion = VfxModelNativeMotion.Create(Get("EmitterPosition")?.Value, nativeModelOrientation); }
                    catch (VfxMappingException exception) { Note(exception.Field, exception.Reason); }
                }
                var modelQuaternionSpin = native.Kind == 1 && modelDampingSupported
                    && (spinMin.X != 0 || spinMin.Y != 0 || spinMax.X != 0 || spinMax.Y != 0
                        || (!spinDeceleration.IsConstant || nativeModelDamping != null)
                            && (spinMin.Z != 0 || spinMax.Z != 0)
                        || simulation == "LOCAL_SPACE_POSITION_ROTATION")
                    && meshLoop == null && (eventNodes.Length == 0 || nativeModelMotion != null)
                    && (relativeMode is null or 0 or 1)
                    && ((simulation is "WORLD_SPACE" or "LOCAL_SPACE_POSITION_ONLY")
                        || simulation == "LOCAL_SPACE_POSITION_ROTATION" && (orientation.IsConstant || nativeModelOrientation != null))
                    && (spinMin.X == 0 && spinMax.X == 0 || float.IsFinite(spinDirection.X))
                    && (spinMin.Y == 0 && spinMax.Y == 0 || float.IsFinite(spinDirection.Y))
                    && (spinMin.Z == 0 && spinMax.Z == 0 || float.IsFinite(spinDirection.Z));
                if (native.Kind == 1 && simulation == "LOCAL_SPACE_POSITION_ROTATION" && !orientation.IsConstant && nativeModelOrientation == null)
                    Note("EmitterOrientation", "Vfx.Preview.Unmapped");
                if (!spinDeceleration.IsZero && !modelQuaternionSpin && !dynamicSpinDamping && !complexSpinDamping
                    && (!spinDeceleration.IsConstant || extendedSpriteDamping))
                {
                    Note("ParticleSpinDeceleration", "Vfx.Preview.ForceUnsupported");
                    spinMin = spinMax = Vector3.Zero;
                }
                if (spinMin != Vector3.Zero || spinMax != Vector3.Zero)
                    Note("ParticleSpin_Min/ParticleSpin_Max", "Vfx.Preview.SpinCandidate");
                var spriteTables = native.Kind == 0 && native.Distortion == 0 && meshMaterial == null && meshLoop == null
                    && (string?)pass?.Attribute("value") is "FULLRES_DEFERRED_ALPHA" or "HALFRES_DEFERRED_ALPHA";
                var projected = new VfxPreviewEmitter
                {
                    Name = layer.Name, Render = native, Emission = emission, Life = life, Scale = scale,
                    IsEventHelper = eventHelper, Events = events,
                    InitialScale = Curve("ParticleInitialScale", 1, planarScale: native.Kind == 0), InitialAlpha = initialAlpha,
                    ModelScaleTable = modelScaleTable,
                    SpriteScaleTable = spriteTables ? VfxSpriteNativeScaleCurve.TryCreateAnalytic(properties.GetValueOrDefault("ParticleScale")?.Value) : null,
                    SpriteInitialAlphaTable = spriteTables ? VfxSpriteNativeScaleCurve.TryCreateAnalytic(properties.GetValueOrDefault("ParticleInitialAlpha")?.Value, "x") : null,
                    SpriteAlphaTable = spriteTables && alphaStart == Vector2.Zero && alphaEnd == Vector2.Zero
                        ? VfxSpriteNativeScaleCurve.TryCreateAnalytic(Behaviour("curve_alpha")?.Value, "alpha") : null,
                    Position = position, Orientation = orientation, Speed = speed, SpawnBox = box, SpawnRadius = radius,
                    Colour = colour, Alpha = alpha, Force = force, Deceleration = deceleration,
                    AlphaStartOffset = alphaStart, AlphaEndOffset = alphaEnd,
                    CalibratedVerticalAcceleration = verticalAcceleration,
                    ParticleForceCurve = particleForceCurve,
                    ModelConstantForce = modelConstantForce,
                    DestroyOnExplicitStop = native.Kind == 1 && Scalar(Get("ParticleDestroyImmediately"), 0) != 0,
                    ConstantForceDamping = constantForceDamping,
                    Start = start, Duration = duration, HasExplicitDuration = durationNode != null, Infinite = infinite, MaximumParticles = Math.Min(TotalWarVfxSimulation.ParticleLimit, maxParticles),
                    BurstInterval = fixedBurst ? burstInterval.Min : 0, BurstLength = fixedBurst ? burstLength.Min : 0,
                    SpawnSpacing = spawnSpacing,
                    SpawnInheritedSpeed = inheritedSpeed, SpawnInheritedDirectionBlend = inheritedDirectionBlend,
                    SimulationSpace = simulation == "WORLD_SPACE" ? 2 : simulation == "LOCAL_SPACE_POSITION_ROTATION" ? 1 : 0,
                    OrientationMin = angles, OrientationMax = anglesMax, RelativeOrientation = relativeOrientation,
                    SpinMin = spinMin, SpinMax = spinMax, SpinDirection = spinDirection,
                    SpinDeceleration = spinDeceleration, DynamicSpinDamping = dynamicSpinDamping,
                    SpriteConstantSpinDamping = spriteConstantDamping,
                    SpriteNativeSpinDamping = spriteNativeDamping,
                    SpriteIndependentSpinRandom = spriteFullLocal && complexSpinDamping,
                    ComplexSpinDamping = complexSpinDamping, ModelQuaternionSpin = modelQuaternionSpin,
                    ModelSpinDeceleration = nativeModelDamping, ModelEmitterOrientation = nativeModelOrientation,
                    ModelEmitterMotion = nativeModelMotion,
                    ModelEmitterBirthLoop = nativeModelOrientation != null && Scalar(Get("EmitterOrientationLoopAnim"), 0) != 0,
                    ModelEmitterFollowLoop = Scalar(Get("InfiniteLife"), 0) != 0,
                    RequiresFlatTerrain = flatTerrain, SpawnOnTerrain = spawnOnTerrain, ModelTerrainBirth = modelTerrainBirth,
                    TerrainSpawnDirection = terrainSpawnDirection,
                    ParticleCullDistances = particleCullDistances,
                    SpawnAngle = angle, SpawnSpread = spread, ZeroSpawnArc = zeroSpawnArc, DefaultVerticalSpawn = defaultVerticalSpawn,
                    LegacySpreadSpawn = legacySpreadSpawn, RadialSpawnDirection = radialSpawnDirection,
                    SpawnAreaArc = spawnAreaArc, SpawnAreaAngle = spawnAreaAngle,
                    Alignment = native.Kind is 1 or 2 ? 2 : alignment,
                    Atlas = atlas, LoopEmission = loop, InitialParameterLoop = initialParameterLoop,
                    VelocityStretch = stretch, VelocityStretchCap = stretchCap, TrailMode = trail,
                    MinimumPixelSize = minimumPixelSize,
                    MinimumPixelHalfResolution = (string?)pass?.Attribute("value") == "HALFRES_DEFERRED_ALPHA",
                    HalfResolution = (string?)pass?.Attribute("value") == "HALFRES_DEFERRED_ALPHA",
                    FullResolution = (string?)pass?.Attribute("value") == "FULLRES_DEFERRED_ALPHA",
                    PremultipliedAlpha = meshMaterial == null && premultiplied && native.Distortion == 0, AdditiveMultiplier = additive,
                    AlphaCrush = alphaCrush, SharpAlphaCrush = sharpAlphaCrush,
                    CoveredColourMultiplier = coveredColourMultiplier,
                    UvFlipProbability = uvFlip,
                    MeshLoop = meshLoop,
                };
                if (textureBytes != null)
                {
                    result.Textures.Add(textureBytes); result.TexturePaths.Add(texturePath);
                    result.TextureUsages.Add(untexturedColour ? VfxTextureUsage.SolidWhite : native.Distortion != 0 ? VfxTextureUsage.DistortionNormal
                        : native.OpaqueModel != 0 ? VfxTextureUsage.OpaqueDiffuse : VfxTextureUsage.Diffuse);
                }
                if (native.Distortion != 0)
                {
                    var maskPath = (string?)Behaviour("diffuse", "ps_texture")?.Attribute("path") ?? "";
                    if (!string.IsNullOrWhiteSpace(maskPath))
                    {
                        native.DistortionMaskIndex = result.Textures.Count;
                        result.Textures.Add(Read(maskPath)); result.TexturePaths.Add(maskPath);
                        result.TextureUsages.Add(VfxTextureUsage.Diffuse);
                    }
                    if (native.TextureIndex < 0 && native.DistortionMaskIndex < 0 && gradient == null)
                        Fail("ps_texture", "Vfx.Preview.MaterialUnsupported");
                    projected.Render = native;
                }
                if (native.LightingMode == 2 && normalModifier != null)
                {
                    var textureNormal = Behaviour("normal", "ps_texture");
                    var modifierNormal = Behaviour("normal", "ps_normal");
                    if (textureNormal != null && modifierNormal != null) Fail("normal", "Vfx.Preview.AmbiguousField");
                    var normalPath = (string?)(textureNormal ?? modifierNormal)?.Attribute("path");
                    if (!string.IsNullOrWhiteSpace(normalPath))
                    {
                        native.NormalIndex = result.Textures.Count;
                        result.Textures.Add(Read(normalPath)); result.TexturePaths.Add(normalPath);
                        result.TextureUsages.Add(VfxTextureUsage.ShadingNormal);
                        projected.Render = native;
                    }
                }
                if (gradient != null)
                {
                    native.GradientIndex = result.Textures.Count; native.GradientScale = gradient.ColourScale;
                    native.GradientAlphaMapping = gradient.MapAlpha ? 1 : 0;
                    result.Textures.Add(gradient.Pixels); result.TexturePaths.Add("ps_gradient/curve_colour");
                    result.TextureUsages.Add(VfxTextureUsage.GradientValue);
                    projected.Render = native;
                }
                if (model != null) result.Models.Add(model);
                EffekseerLayer BindModel(VfxPreviewModel geometry, string diffuse, VfxScrollingMeshMaterial? material)
                {
                    var partLayer = native;
                    partLayer.OpaqueModel = geometry.OpaqueMaterial ? 1 : 0;
                    if (material != null)
                    {
                        var indexes = new List<int>();
                        foreach (var path in material.TexturePaths)
                        {
                            var bytes = ModelTexture(path);
                            indexes.Add(result.Textures.Count); result.Textures.Add(bytes); result.TexturePaths.Add(path);
                            result.TextureUsages.Add(material.Parameters.Length == VfxRigidBasicDitherMaterial.ParameterCount && indexes.Count == 1
                                ? VfxTextureUsage.Diffuse : material.Parameters.Length == 60 && indexes.Count == 1
                                ? VfxTextureUsage.OpaqueDiffuse : VfxTextureUsage.MaterialValue);
                        }
                        partLayer.TextureIndex = indexes[0]; partLayer.MeshMaterialIndex = result.MeshMaterials.Count;
                        result.MeshMaterials.Add(new(indexes.ToArray(), material.Parameters));
                    }
                    else
                    {
                        partLayer.TextureIndex = result.Textures.Count; partLayer.MeshMaterialIndex = -1;
                        result.Textures.Add(ModelTexture(diffuse)); result.TexturePaths.Add(diffuse);
                        result.TextureUsages.Add(geometry.OpaqueMaterial ? VfxTextureUsage.OpaqueDiffuse : VfxTextureUsage.Diffuse);
                    }
                    partLayer.ModelIndex = result.Models.Count; result.Models.Add(geometry);
                    return partLayer;
                }
                projected.AdditionalModelParts = additionalParts.Select(part => BindModel(part.Model, part.Texture, part.Material)).ToArray();
                projected.ModelVariants = modelVariants.Skip(1).Select(parts => (IReadOnlyList<EffekseerLayer>)parts
                    .Select(part => BindModel(part.Model, part.Texture, part.Material)).ToArray()).ToArray();
                if (result.Emitters.Sum(emitter => emitter.RenderLayerCount) + projected.RenderLayerCount > 512)
                    Fail("RigidData", "Vfx.Preview.RangeUnsupported");
                result.Emitters.Add(projected);
                projected.PreviewRootKind = "Emitter";
                projected.PreviewRootName = layer.Name;
                projected.PreviewRootOccurrence = layer.Element.ElementsBeforeSelf("emitter")
                    .Count(element => ((string?)element.Attribute("id") ?? "") == layer.Name);
                editorState = VfxPreviewFieldState.Connected;
                Note("", "Vfx.Preview.Candidate");
            }
            catch (VfxMappingException exception)
            { editorState = VfxPreviewFieldState.Failed; DiscardLayerResources(); Note(exception.Field, exception.Reason); }
            catch (Exception exception) when (exception is InvalidDataException or FormatException or System.Xml.XmlException)
            { editorState = VfxPreviewFieldState.Failed; DiscardLayerResources(); Note("", "Vfx.Preview.InvalidData"); }
            finally
            {
                editor?.RecordEmitter(layer.Element, editorState, properties.Values.Concat(behaviourFields).Concat(render)
                    .SelectMany(element => element.DescendantsAndSelf()), consumed, consumedModifiers, editorNotes!);
            }
            foreach (var field in properties.Values.Concat(behaviourFields).Concat(render).Where(x => !consumed.Contains(x)))
                Note(Name(field), "Vfx.Preview.Unmapped");
            foreach (var modifier in behaviour?.Elements("modifier") ?? [])
                if (!consumedModifiers.Contains(modifier)) Note("modifier/" + (string?)modifier.Attribute("fragment"), "Vfx.Preview.Unmapped");

            void DiscardLayerResources()
            {
                context.Restore(animationCheckpoint);
                result.Textures.RemoveRange(textureCount, result.Textures.Count - textureCount);
                result.TexturePaths.RemoveRange(textureCount, result.TexturePaths.Count - textureCount);
                result.TextureUsages.RemoveRange(textureCount, result.TextureUsages.Count - textureCount);
                result.Models.RemoveRange(modelCount, result.Models.Count - modelCount);
                result.MeshMaterials.RemoveRange(materialCount, result.MeshMaterials.Count - materialCount);
            }

            IEnumerable<XElement> ForQuality(IEnumerable<XElement> nodes) => nodes
                .Where(x => (string?)x.Attribute("quality") is null or "0" || (string?)x.Attribute("quality") == snapshot.Quality)
                .OrderBy(x => (string?)x.Attribute("quality") == snapshot.Quality ? 1 : 0);
            XElement? Get(string name)
            {
                var node = properties.GetValueOrDefault(name);
                if (node != null) consumed.Add(node);
                return node;
            }
            VfxPreviewCurve Curve(string name, float fallback, bool required = false, bool planarScale = false)
            {
                var node = Get(name);
                if (required && node == null) Fail(name, "Vfx.Preview.MissingField");
                var curve = MakeCurve(node, name, fallback, planarScale);
                curve.RequireChannels(name switch
                {
                    "EmitterPosition" or "EmitterOrientation" or "ParticleScale" or "ParticleInitialScale" or "SpawnAreaBox" or "ForceA" or "ForceB" or "ForceC" => ["x", "y", "z"],
                    "SpawnAreaCurve" => ["inner_radius", "outer_radius"],
                    _ => ["x"],
                });
                return curve;
            }
            VfxPreviewCurve MakeCurve(XElement? node, string name, float fallback, bool planarScale = false)
            {
                var curve = node?.Attribute("value") != null
                    ? context.ReadCurve(null, name, Scalar(node, fallback)) : context.ReadCurve(node?.Value, name, fallback, planarScale);
                if (curve.HasBezierSegments) Note(name, "Vfx.Preview.BezierCandidate");
                if (curve.HasAnalyticSegments) Note(name, "Vfx.Preview.AnalyticCurveCalibrated");
                return curve;
            }
            XElement? Behaviour(string name, string? fragment = null)
            {
                fragment ??= name switch
                {
                    "curve_colour" => "vs_colour",
                    "curve_alpha" => "vs_vertex_alpha",
                    "diffuse" or "pre_multiplied_alpha" or "overlay_colour" => "ps_texture",
                    "normal" => "ps_normal",
                    "pivot_x" or "pivot_y" => Modifier("vs_translation") != null ? "vs_translation"
                        : Modifier("vs_translation_velocity_v2") != null ? "vs_translation_velocity_v2" : "vs_translation_velocity",
                    _ => null,
                };
                var nodes = behaviourFields.Where(x => (string?)x.Attribute("name") == name
                    && (fragment == null || (string?)x.Parent?.Attribute("fragment") == fragment)).ToArray();
                if (nodes.Length > 1) Fail(name, "Vfx.Preview.AmbiguousField");
                if (nodes.Length == 0) return null;
                consumed.Add(nodes[0]); return nodes[0];
            }
            VfxPreviewCurve BehaviourCurve(string name, float fallback)
            {
                var curve = MakeCurve(Behaviour(name), name, fallback);
                curve.RequireChannels(name == "curve_colour" ? ["r", "g", "b"] : name == "curve_alpha" ? ["alpha"] : ["x"]);
                return curve;
            }
            XElement? Modifier(string name)
            {
                var node = behaviour?.Elements("modifier").FirstOrDefault(x => (string?)x.Attribute("fragment") == name);
                if (node != null) consumedModifiers.Add(node);
                return node;
            }
            VfxAtlas ReadAtlas()
            {
                if (Modifier("vs_cell_texture") == null && Modifier("vs_animated_texture") == null) return new();
                var animated = Modifier("vs_animated_texture") != null;
                var outerX = Math.Max(1, Integer(Behaviour("atlas_number_of_cells_x"), 1, 0, 256));
                var outerY = Math.Max(1, Integer(Behaviour("atlas_number_of_cells_y"), 1, 0, 256));
                var outerCell = Integer(Behaviour("atlas_cell"), 0, 0, outerX * outerY - 1);
                var columns = 1; var rows = 1;
                if (animated)
                {
                    var dimensions = Float2(Behaviour("no_cells_xy"), new(1, 1));
                    columns = CheckedInteger(dimensions.X, "no_cells_xy", 1, 256); rows = CheckedInteger(dimensions.Y, "no_cells_xy", 1, 256);
                }
                else { columns = Integer(Behaviour("number_of_cells_x"), 1, 1, 256); rows = Integer(Behaviour("number_of_cells_y"), 1, 1, 256); }
                var cellValue = Scalar(Behaviour("cell"), 0);
                if (cellValue < 0 || cellValue != MathF.Truncate(cellValue)) Fail("cell", "Vfx.Preview.RangeUnsupported");
                var cell = CheckedInteger(animated ? cellValue : cellValue % (columns * rows), "cell", 0, columns * rows - 1);
                var random = Scalar(Behaviour("randomize_cell"), 0) != 0;
                var loops = Float2(Behaviour("no_loops"), new(1, 1));
                var mode = Behaviour("time_mode");
                var fixedSpeed = animated && (string?)mode?.Attribute("value") == "fixed_animation_speed";
                if (animated && mode != null && (string?)mode.Attribute("value") is not ("using_particle_lifetime" or "fixed_animation_speed"))
                    Fail("time_mode", "Vfx.Preview.UvUnsupported");
                if (fixedSpeed)
                {
                    if (loops.X < 0 || loops.Y < 0 || loops.X > 1000 || loops.Y > 1000) Fail("no_loops", "Vfx.Preview.RangeUnsupported");
                    Note("time_mode", "Vfx.Preview.FixedSpeedAtlasCandidate");
                }
                Note(animated ? "vs_animated_texture" : "vs_cell_texture", "Vfx.Preview.AtlasCandidate");
                return new(columns, rows, cell, random, animated, Scalar(Behaviour("loop"), 1) != 0, new(loops.X, loops.Y),
                    Scalar(Behaviour("row_restricted"), 0) != 0, Integer(Behaviour("row_restriction"), 0, 0, rows - 1),
                    Scalar(Behaviour("randomize_row"), 0) != 0, Scalar(Behaviour("cell_blend"), 0) != 0, outerX, outerY, outerCell, fixedSpeed);
            }
            void Note(string field, string reason)
            {
                var note = new VfxPreviewDiagnostic(snapshot.SourcePath, layer.Name, field, reason);
                result.Diagnostics.Add(note);
                editorNotes?.Add(note);
            }
        }
        foreach (var reference in references)
        {
            if (rootSelection != null && !rootSelection.ReferenceNames.Contains(reference.Name, StringComparer.Ordinal))
            {
                editor?.RecordReference(reference.Element, VfxPreviewFieldState.Unselected, [], []);
                continue;
            }
            var animationCheckpoint = context.Capture();
            var referenceDiagnosticStart = result.Diagnostics.Count;
            try
            {
                var node = reference.Element;
                var path = (string?)node.Attribute("vfx_ref") ?? "";
                if (!path.StartsWith("vfx/", StringComparison.OrdinalIgnoreCase) && !path.StartsWith("vfx\\", StringComparison.OrdinalIgnoreCase)) path = "vfx\\" + path;
                if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) path += ".xml";
                var child = ConvertNested(new(Read(path), path, snapshot.Resolve, snapshot.Quality), visited, depth + 1, node, context);
                if (result.Emitters.Count + child.Emitters.Count > 256) Fail("vfx_ref", "Vfx.Preview.RangeUnsupported");
                if (result.Emitters.Sum(emitter => emitter.RenderLayerCount) + child.Emitters.Sum(emitter => emitter.RenderLayerCount) > 512)
                    Fail("vfx_ref", "Vfx.Preview.RangeUnsupported");
                if (result.MeshMaterials.Count + child.MeshMaterials.Count > 256)
                    Fail("vfx_ref", "Vfx.Preview.RangeUnsupported");
                RebaseResources(child, result);
                var referencedEmitters = new List<VfxPreviewEmitter>();
                var referenceInstance = new object();
                foreach (var emitter in child.Emitters)
                {
                    emitter.PreviewRootKind = "Reference";
                    emitter.PreviewRootName = reference.Name;
                    emitter.PreviewRootOccurrence = node.ElementsBeforeSelf("vfx")
                        .Count(element => ((string?)element.Attribute("inst_name") ?? "") == reference.Name);
                    if (emitter.IsEventTemplate) { referencedEmitters.Add(emitter); continue; }
                    if ((string?)node.Attribute("infinite_life") == "false")
                    {
                        if (emitter.HasExplicitDuration) emitter.Infinite = false;
                        emitter.ModelEmitterFollowLoop = false;
                    }
                    var values = node.Elements("emitter").FirstOrDefault(x => (string?)x.Attribute("name") == emitter.Name);
                    VfxRange Bounds(XElement? element, string name, float fallback) => element == null ? new(fallback, fallback) :
                        new(Attribute(element, name + "_min", fallback), Attribute(element, name + "_max", fallback));
                    Vector4 Tint(string bound) => values == null ? Vector4.One : new(Attribute(values, "tint_" + bound + "_r", 1),
                        Attribute(values, "tint_" + bound + "_g", 1), Attribute(values, "tint_" + bound + "_b", 1), Attribute(values, "tint_" + bound + "_a", 1));
                    var speed = Attribute(node, "simulation_speed_multiplier", 1);
                    if (speed < 0.0001f || speed > 16) Fail("simulation_speed_multiplier", "Vfx.Preview.RangeUnsupported");
                    emitter.References = [.. emitter.References, new(Bounds(node, "scale", 1),
                        new(Attribute(node, "pos_x", 0), Attribute(node, "pos_y", 0), Attribute(node, "pos_z", 0)),
                        new(Attribute(node, "rot_x", 0), Attribute(node, "rot_y", 0), Attribute(node, "rot_z", 0)),
                        Bounds(node, "start_time", 0), speed, Bounds(values, "spawn_rate_scale_factor", 1),
                        Bounds(values, "particle_lifetime_bias", 0), Bounds(values, "emitter_lifetime_bias", 0), Tint("min"), Tint("max"))
                        { InstanceKey = referenceInstance }];
                    ValidateReferenceProjection(emitter);
                    referencedEmitters.Add(emitter);
                }
                result.Emitters.AddRange(referencedEmitters);
                result.Textures.AddRange(child.Textures); result.TexturePaths.AddRange(child.TexturePaths); result.Models.AddRange(child.Models);
                result.TextureUsages.AddRange(child.TextureUsages);
                result.MeshMaterials.AddRange(child.MeshMaterials);
                result.TotalEmitterCount += child.TotalEmitterCount; result.DisabledEmitterCount += child.DisabledEmitterCount;
                result.Diagnostics.AddRange(child.Diagnostics);
                result.Diagnostics.Add(new(snapshot.SourcePath, reference.Name, "vfx_ref", "Vfx.Preview.ReferenceCandidate"));
                result.Diagnostics.Add(new(snapshot.SourcePath, reference.Name, "emitter/enable", "Vfx.Preview.ReferenceSelectionCalibrated"));
                if (node.Attribute("infinite_life") != null)
                    result.Diagnostics.Add(new(snapshot.SourcePath, reference.Name, "infinite_life", "Vfx.Preview.EmitterTimelineCandidate"));
                var known = new HashSet<string>(["inst_name", "vfx_ref", "notes", "scale_min", "scale_max", "pos_x", "pos_y", "pos_z", "rot_x", "rot_y", "rot_z",
                    "start_time_min", "start_time_max", "simulation_speed_multiplier", "infinite_life"]);
                foreach (var attribute in node.Attributes().Where(x => !known.Contains(x.Name.LocalName)))
                    result.Diagnostics.Add(new(snapshot.SourcePath, reference.Name, attribute.Name.LocalName, "Vfx.Preview.Unmapped"));
                foreach (var values in node.Elements("emitter"))
                    foreach (var attribute in values.Attributes().Where(x => x.Name.LocalName is not ("name" or "enable")
                        && !x.Name.LocalName.StartsWith("tint_", StringComparison.Ordinal)
                        && !x.Name.LocalName.StartsWith("particle_lifetime_bias_", StringComparison.Ordinal)
                        && !x.Name.LocalName.StartsWith("emitter_lifetime_bias_", StringComparison.Ordinal)
                        && !x.Name.LocalName.StartsWith("spawn_rate_scale_factor_", StringComparison.Ordinal)
                        && !(x.Name.LocalName == "sound_record" && x.Value.Length == 0)))
                        result.Diagnostics.Add(new(snapshot.SourcePath, reference.Name, Name(values) + "/" + attribute.Name.LocalName, "Vfx.Preview.Unmapped"));
                editor?.RecordReference(node, referencedEmitters.Count > 0 ? VfxPreviewFieldState.Connected
                    : child.TotalEmitterCount > child.DisabledEmitterCount ? VfxPreviewFieldState.Failed
                    : child.DisabledEmitterCount > 0 ? VfxPreviewFieldState.Disabled : VfxPreviewFieldState.NotConnected,
                    referencedEmitters, result.Diagnostics.Skip(referenceDiagnosticStart).ToArray());
            }
            catch (VfxMappingException exception)
            {
                context.Restore(animationCheckpoint); result.Diagnostics.Add(new(snapshot.SourcePath, reference.Name, exception.Field, exception.Reason));
                editor?.RecordReference(reference.Element, VfxPreviewFieldState.Failed, [], result.Diagnostics.Skip(referenceDiagnosticStart).ToArray());
            }
            catch (Exception exception) when (exception is InvalidDataException or FormatException or System.Xml.XmlException)
            {
                context.Restore(animationCheckpoint); result.Diagnostics.Add(new(snapshot.SourcePath, reference.Name, "vfx_ref", "Vfx.Preview.InvalidData"));
                editor?.RecordReference(reference.Element, VfxPreviewFieldState.Failed, [], result.Diagnostics.Skip(referenceDiagnosticStart).ToArray());
            }
        }
        // Event children are templates. Only an actual parent particle instantiates them at runtime.
        foreach (var emitter in result.Emitters.Where(e => !e.IsEventTemplate).ToArray())
            foreach (var particleEvent in emitter.Events)
            {
                if (particleEvent.Resolved) continue;
                particleEvent.Resolved = true;
                var checkpoint = context.Capture();
                var field = particleEvent.OnDeath ? "OnParticleDeath" : "OnParticleSpawn";
                try
                {
                    var path = particleEvent.Path.Replace('/', '\\');
                    if (!path.StartsWith("vfx\\", StringComparison.OrdinalIgnoreCase)) path = "vfx\\" + path;
                    if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) path += ".xml";
                    var child = ConvertNested(new(Read(path), path, snapshot.Resolve, snapshot.Quality), visited, depth + 1, null, context);
                    if (result.Emitters.Count + child.Emitters.Count > 256
                        || result.Emitters.Sum(e => e.RenderLayerCount) + child.Emitters.Sum(e => e.RenderLayerCount) > 512
                        || result.MeshMaterials.Count + child.MeshMaterials.Count > 256)
                        Fail(field, "Vfx.Preview.RangeUnsupported");
                    particleEvent.Children = child.Emitters.Where(e => !e.IsEventTemplate).ToArray();
                    foreach (var definition in child.Emitters)
                    {
                        definition.IsEventTemplate = true;
                        definition.PreviewRootKind = emitter.PreviewRootKind;
                        definition.PreviewRootName = emitter.PreviewRootName;
                        definition.PreviewRootOccurrence = emitter.PreviewRootOccurrence;
                    }
                    RebaseResources(child, result);
                    result.Emitters.AddRange(child.Emitters);
                    result.Textures.AddRange(child.Textures); result.TexturePaths.AddRange(child.TexturePaths);
                    result.TextureUsages.AddRange(child.TextureUsages); result.Models.AddRange(child.Models);
                    result.MeshMaterials.AddRange(child.MeshMaterials); result.Diagnostics.AddRange(child.Diagnostics);
                    result.Diagnostics.Add(new(snapshot.SourcePath, emitter.Name, field, "Vfx.Preview.ParticleEventCandidate"));
                    editor?.RecordEventResult(emitter, field, VfxPreviewFieldState.PendingConfirmation, "Vfx.Preview.ParticleEventCandidate");
                }
                catch (VfxMappingException exception)
                {
                    context.Restore(checkpoint);
                    result.Diagnostics.Add(new(snapshot.SourcePath, emitter.Name, field + "/" + exception.Field, exception.Reason));
                    editor?.RecordEventResult(emitter, field, VfxPreviewFieldState.Failed, exception.Reason);
                }
                catch (Exception exception) when (exception is InvalidDataException or FormatException or System.Xml.XmlException)
                {
                    context.Restore(checkpoint);
                    result.Diagnostics.Add(new(snapshot.SourcePath, emitter.Name, field, "Vfx.Preview.InvalidData"));
                    editor?.RecordEventResult(emitter, field, VfxPreviewFieldState.Failed, "Vfx.Preview.InvalidData");
                }
            }
        foreach (var setting in effect.Attributes().Where(x => x.Name != "revision"))
            result.Diagnostics.Add(new(snapshot.SourcePath, "", "vfx/@" + setting.Name.LocalName, "Vfx.Preview.Unmapped"));
        return result;

        byte[] Read(string path)
        {
            path = path.Replace('/', '\\');
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':') || path.Split('\\').Any(x => x is "" or "." or ".."))
                Fail(path, "Vfx.Preview.MissingResource");
            var bytes = snapshot.Resolve(path);
            if (bytes == null) Fail(path, "Vfx.Preview.MissingResource");
            if (bytes!.Length > 64 * 1024 * 1024) Fail(path, "Vfx.Preview.RangeUnsupported");
            return bytes;
        }
        (VfxPreviewModel Model, string Texture, VfxScrollingMeshMaterial? Material, int PartCount) ReadModel(string path, int partIndex, Vector3 pivot,
            string animation = "", float animationSpeed = 1, float maximumLife = 1)
        {
            var animated = !string.IsNullOrWhiteSpace(animation);
            var cacheKey = new ModelCacheKey(path.Replace('\\', '/').ToLowerInvariant(), partIndex, animation.Replace('\\', '/').ToLowerInvariant(),
                animated ? animationSpeed : 0, animated ? maximumLife : 0, pivot);
            if (modelCache.TryGetValue(cacheKey, out var cached)) return cached;
            WsModelMaterialFile? material = null;
            byte[]? materialBytes = null;
            WsModelFile? wrapper = null;
            if (path.EndsWith(".wsmodel", StringComparison.OrdinalIgnoreCase))
            {
                wrapper = new WsModelFile(new PackFile(Path.GetFileName(path), new MemorySource(Read(path))));
                if (wrapper.GeometryPaths.Count != 1) Fail("RigidData", "Vfx.Preview.GeometryUnsupported");
                path = wrapper.GeometryPath;
            }
            if (!path.EndsWith(".rigid_model_v2", StringComparison.OrdinalIgnoreCase)) Fail("RigidData", "Vfx.Preview.GeometryUnsupported");
            var file = ModelFactory.Create().Load(Read(path));
            if (file.ModelList.Length == 0 || file.ModelList[0].Length is < 1 or > 64 || partIndex >= file.ModelList[0].Length)
                Fail("RigidData", "Vfx.Preview.GeometryUnsupported");
            if (wrapper != null)
            {
                var exact = wrapper.MaterialList.Where(x => x.LodIndex == 0 && x.PartIndex == partIndex).ToArray();
                if (exact.Length > 1) Fail("material", "Vfx.Preview.AmbiguousField");
                var entry = exact.SingleOrDefault();
                if (entry == null && wrapper.MaterialList.Any(x => x.LodIndex == -1 && x.PartIndex == -1))
                {
                    // Omitted indexes are unambiguous only for a single material and single mesh/LOD.
                    if (wrapper.MaterialList.Count != 1 || file.ModelList.Length != 1 || file.ModelList[0].Length != 1)
                        Fail("material", "Vfx.Preview.AmbiguousField");
                    entry = wrapper.MaterialList[0];
                }
                if (entry != null)
                {
                    materialBytes = Read(entry.MaterialPath);
                    material = new WsModelMaterialFile(new PackFile("material", new MemorySource(materialBytes)));
                }
            }
            VfxScrollingMeshMaterial? scrolling = null;
            if (material != null && VfxScrollingMeshMaterial.Supports(material.ShaderPath))
            {
                try { scrolling = VfxScrollingMeshMaterial.Read(materialBytes!, Read); }
                catch (VfxMappingException error) when (error.Field == "material/textures"
                    && string.Equals(material.ShaderPath.Replace('\\', '/'), "shaders/wh3_vfx_rigid_emissive_dissolve_one_shot.xml.shader", StringComparison.OrdinalIgnoreCase))
                {
                    // Keep the previously visible model with its explicit fallback diagnostic.
                }
            }
            if (scrolling == null && material != null && !material.Textures.ContainsKey(TextureType.Diffuse) && !material.Textures.ContainsKey(TextureType.BaseColour))
                Fail(material.ShaderPath, "Vfx.Preview.MaterialUnsupported");
            var part = file.ModelList[0][partIndex];
            if (!animated && part.Mesh.VertexList.Any(x => x.WeightCount > 0)) Fail("RigidData", "Vfx.Preview.GeometryUnsupported");
            if (scrolling?.UsesVertexColour == true && part.Mesh.VertexList.Any(x => x.UnormColour == null))
                Fail("RigidData/vertex_colour", "Vfx.Preview.GeometryUnsupported");
            var texture = material?.Textures.GetValueOrDefault(TextureType.Diffuse) ?? material?.Textures.GetValueOrDefault(TextureType.BaseColour)
                ?? part.Material.GetTexture(TextureType.Diffuse)?.Path ?? part.Material.GetTexture(TextureType.BaseColour)?.Path ?? "";
            if (material == null && part.Material.GetTexture(TextureType.Diffuse) is { } embeddedDiffuse)
                texture = VfxLegacyModelColour.ResolveEmbeddedDiffuse(file.Header.Version, part.Material.BinaryVertexFormat,
                    part.CommonHeader.ModelTypeFlag, embeddedDiffuse.Path);
            // Static entry pivots are local offsets from the neutral half, before particle scale/rotation.
            var offset = animated ? Vector3.Zero : part.Material.PivotPoint + new Vector3(.5f) - pivot;
            if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y) || !float.IsFinite(offset.Z))
                Fail("RigidData/PivotPoint", "Vfx.Preview.InvalidData");
            var vertices = part.Mesh.VertexList.Select(x => new EffekseerVertex
            {
                Position = new Vector3(x.Position.X, x.Position.Y, x.Position.Z) + offset, Normal = x.Normal, Tangent = x.Tangent, Binormal = x.BiNormal, UV = x.Uv,
                Colour = scrolling == null ? Vector4.One : x.UnormColour ?? Vector4.One,
            }).ToArray();
            var indices = part.Mesh.IndexList.Select(x => (int)x).ToArray();
            var rigidAnimation = animated && part.Mesh.VertexList.All(vertex => vertex.WeightCount == 0);
            var geometry = rigidAnimation ? VfxModelAnimation.BakeRigid(vertices, indices, file.Header.SkeletonName, Read(animation),
                (part.Material as Shared.GameFormats.RigidModel.MaterialHeaders.WeightedMaterial)?.MatrixIndex ?? -1,
                part.Material.PivotPoint, animationSpeed, maximumLife, animationBudget)
                : animated ? VfxModelAnimation.Bake(part.Mesh.VertexList, vertices, indices, file.Header.SkeletonName,
                Read(@"animations\skeletons\" + file.Header.SkeletonName + ".anim"), Read(animation), animationSpeed, maximumLife, animationBudget)
                : new VfxPreviewModel(vertices, indices);
            if (scrolling?.Parameters.Length is VfxRigidBasicDitherMaterial.ParameterCount or 12 or 40 or 56 or 60 or VfxTidecallMaterial.ParameterCount) geometry = geometry with { OpaqueMaterial = true };
            if (scrolling == null && material is { Alpha: false } && material.Name.Contains("alpha_off", StringComparison.OrdinalIgnoreCase)
                && string.Equals(material.ShaderPath.Replace('\\', '/'), "shaders/rigid_character.xml.shader", StringComparison.OrdinalIgnoreCase))
                geometry = geometry with { OpaqueMaterial = true };
            if (scrolling == null && material != null && !geometry.OpaqueMaterial)
                geometry = geometry with { ApproximateMaterialShader = material.ShaderPath };
            var converted = (geometry, scrolling?.TexturePaths[0] ?? texture, scrolling, file.ModelList[0].Length);
            modelCache.Add(cacheKey, converted);
            return converted;
        }
    }

    private static float Scalar(XElement? node, float fallback)
    {
        if (node == null) return fallback;
        var value = (string?)node.Attribute("value");
        if (value == "true") return 1;
        if (value == "false") return 0;
        return Number(value, Name(node));
    }
    private static float Number(string? value, string name)
    {
        if (!VfxCurve.TryNumber(value ?? "", out var number)) Fail(name, "Vfx.Preview.InvalidData");
        return (float)number;
    }
    private static float Attribute(XElement node, string name, float fallback) => node.Attribute(name) is { } value ? Number(value.Value, Name(node)) : fallback;
    private static VfxRange Range(XElement? node, float fallback) => node == null ? new(fallback, fallback) :
        new(Number((string?)node.Attribute("min"), Name(node)), Number((string?)node.Attribute("max"), Name(node)));
    private static Vector3 Vector(XElement? node, Vector3 fallback) => node == null ? fallback :
        new(Attribute(node, "x", fallback.X), Attribute(node, "y", fallback.Y), Attribute(node, "z", fallback.Z));
    private static Vector2 Float2(XElement? node, Vector2 fallback)
    {
        if (node == null) return fallback;
        var text = ((string?)node.Attribute("value") ?? "").Trim();
        if (!text.StartsWith("float2(", StringComparison.Ordinal) || !text.EndsWith(')')) Fail(Name(node), "Vfx.Preview.InvalidData");
        var parts = text[7..^1].Split(',');
        if (parts.Length != 2) Fail(Name(node), "Vfx.Preview.InvalidData");
        return new(Number(parts[0], Name(node)), Number(parts[1], Name(node)));
    }
    private static int Integer(XElement? node, int fallback, int min, int max) => CheckedInteger(Scalar(node, fallback), node == null ? "" : Name(node), min, max);
    private static int CheckedInteger(float value, string field, int min, int max)
    {
        if (value != MathF.Truncate(value) || value < min || value > max) Fail(field, "Vfx.Preview.RangeUnsupported");
        return (int)value;
    }
    private static string Name(XElement node) => (string?)node.Attribute("name") ?? node.Name.LocalName;
    private static void RebaseResources(VfxConversion child, VfxConversion parent)
    {
        var textureOffset = parent.Textures.Count; var modelOffset = parent.Models.Count;
        var meshMaterialOffset = parent.MeshMaterials.Count;
        foreach (var emitter in child.Emitters)
        {
            var native = emitter.Render;
            if (native.TextureIndex >= 0) native.TextureIndex += textureOffset;
            if (native.NormalIndex >= 0) native.NormalIndex += textureOffset;
            if (native.GradientIndex >= 0) native.GradientIndex += textureOffset;
            if (native.DistortionMaskIndex >= 0) native.DistortionMaskIndex += textureOffset;
            if (native.ModelIndex >= 0) native.ModelIndex += modelOffset;
            if (native.MeshMaterialIndex >= 0) native.MeshMaterialIndex += meshMaterialOffset;
            emitter.Render = native;
            emitter.AdditionalModelParts = emitter.AdditionalModelParts.Select(part =>
            {
                if (part.TextureIndex >= 0) part.TextureIndex += textureOffset;
                part.ModelIndex += modelOffset;
                if (part.MeshMaterialIndex >= 0) part.MeshMaterialIndex += meshMaterialOffset;
                if (part.NormalIndex >= 0) part.NormalIndex += textureOffset;
                if (part.GradientIndex >= 0) part.GradientIndex += textureOffset;
                if (part.DistortionMaskIndex >= 0) part.DistortionMaskIndex += textureOffset;
                return part;
            }).ToArray();
            emitter.ModelVariants = emitter.ModelVariants.Select(parts => (IReadOnlyList<EffekseerLayer>)parts.Select(part =>
            {
                if (part.TextureIndex >= 0) part.TextureIndex += textureOffset;
                part.ModelIndex += modelOffset;
                if (part.MeshMaterialIndex >= 0) part.MeshMaterialIndex += meshMaterialOffset;
                if (part.NormalIndex >= 0) part.NormalIndex += textureOffset;
                if (part.GradientIndex >= 0) part.GradientIndex += textureOffset;
                if (part.DistortionMaskIndex >= 0) part.DistortionMaskIndex += textureOffset;
                return part;
            }).ToArray()).ToArray();
        }
        for (var index = 0; index < child.MeshMaterials.Count; ++index)
        {
            var material = child.MeshMaterials[index];
            child.MeshMaterials[index] = new(material.TextureIndexes.Select(i => i + textureOffset).ToArray(), material.Parameters);
        }
    }

    private static void ValidateReferenceProjection(VfxPreviewEmitter emitter)
    {
        double rate = emitter.Emission.Maximum, life = emitter.Life.Maximum, duration = Math.Max(emitter.Duration.Min, emitter.Duration.Max);
        foreach (var reference in emitter.References)
        {
            if (Math.Min(reference.Rate.Min, reference.Rate.Max) < 0 || Math.Max(reference.Rate.Min, reference.Rate.Max) > 1000)
                Fail("spawn_rate_scale_factor", "Vfx.Preview.RangeUnsupported");
            rate *= Math.Max(reference.Rate.Min, reference.Rate.Max) * reference.Speed;
            life = (life + Math.Max(reference.LifeBias.Min, reference.LifeBias.Max)) / reference.Speed;
            duration = (duration + Math.Max(reference.DurationBias.Min, reference.DurationBias.Max)) / reference.Speed;
            if (!double.IsFinite(rate) || rate > 10000 || rate < 0
                || !double.IsFinite(life) || !double.IsFinite(duration) || duration > 600)
                Fail("vfx_ref", "Vfx.Preview.RangeUnsupported");
        }
    }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string field, string reason) => throw new VfxMappingException(field, reason);
}
