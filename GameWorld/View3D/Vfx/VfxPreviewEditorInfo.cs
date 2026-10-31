using System.Xml.Linq;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx;

public sealed record VfxPreviewLayerIdentity(string Kind, string Name, int Occurrence = 0);
public enum VfxPreviewFieldState { Unknown, Connected, InactiveQuality, Disabled, Unselected, Failed, NotConnected, PendingConfirmation, Ignored }

public sealed record VfxPreviewEditorFieldInfo(string Key, string Name, string Context, string Quality,
    VfxPreviewFieldState State, IReadOnlyList<string> ReasonKeys);
public sealed record VfxPreviewEditorLayerInfo(string Kind, string Name, int Occurrence,
    VfxPreviewFieldState State, IReadOnlyList<VfxPreviewEditorFieldInfo> Fields)
{
    public string Key { get; init; } = "";
}

public sealed class VfxPreviewEditorInfo
{
    public static VfxPreviewEditorInfo Empty { get; } = new([]);
    public IReadOnlyList<VfxPreviewEditorLayerInfo> Layers { get; }
    internal VfxPreviewEditorInfo(IEnumerable<VfxPreviewEditorLayerInfo> layers) => Layers = Array.AsReadOnly(layers.ToArray());
}

// Structural identities are stable across numeric edits and distinguish duplicate names and grouped attributes.
public static class VfxPreviewFieldIdentity
{
    public static string GetKey(VfxField field) => GetKey(field, ElementKey(field.Element));
    internal static string GetKey(VfxField field, string elementKey) => elementKey + "/field:" + Encode(field.Name)
        + "/attributes:" + string.Join(',', field.Attributes.Select(attribute => Encode(attribute.Name.ToString())).Order(StringComparer.Ordinal));
    public static string GetKey(VfxLayer layer) => ElementKey(layer.Element);
    private static string ElementKey(XElement element) => string.Join('/', element.AncestorsAndSelf().Reverse().Select(node =>
        Encode(node.Name.ToString()) + "[" + node.ElementsBeforeSelf(node.Name).Count() + "]"));
    private static string Encode(string value) => Uri.EscapeDataString(value);
}

// Reload-only readback: no editable XML or resource objects escape into the editor-facing result.
internal sealed class VfxPreviewEditorInfoBuilder(VfxDocument document)
{
    private sealed record Capture(VfxPreviewFieldState State, HashSet<XElement> Active, HashSet<XElement> Read,
        IReadOnlyList<VfxPreviewDiagnostic> Notes);
    private readonly IReadOnlyList<VfxLayer> _layers = document.Layers;
    private readonly Dictionary<XElement, Capture> _captures = [];
    private readonly Dictionary<XElement, string> _elementKeys = [];
    private readonly Dictionary<IReadOnlyList<VfxPreviewDiagnostic>, Dictionary<(string Name, string Context), IReadOnlyList<string>>> _reasonCaches
        = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(VfxPreviewLayerIdentity Root, string Field), (VfxPreviewFieldState State, string Reason)> _events = [];
    private static readonly HashSet<string> ConditionalReads = new(StringComparer.Ordinal)
    {
        "ForceB", "ForceC", "UseSurfaceNormal", "ParticlesFollowTerrain", "AllowEmitFromMesh", "EffectedByWindFactor",
        "WindVarianceFactor", "EventEnable", "BurstInterval", "BurstIntervalLength", "ParticleRelativeSpawnDirection"
    };
    private static readonly HashSet<string> ReferenceAttributes = new(StringComparer.Ordinal)
    {
        "scale_min", "scale_max", "pos_x", "pos_y", "pos_z", "rot_x", "rot_y", "rot_z", "start_time_min", "start_time_max",
        "simulation_speed_multiplier", "infinite_life"
    };
    private static readonly HashSet<string> OverrideAttributes = new(StringComparer.Ordinal)
    {
        "enable", "tint_min_r", "tint_min_g", "tint_min_b", "tint_min_a", "tint_max_r", "tint_max_g", "tint_max_b", "tint_max_a",
        "particle_lifetime_bias_min", "particle_lifetime_bias_max", "emitter_lifetime_bias_min", "emitter_lifetime_bias_max",
        "spawn_rate_scale_factor_min", "spawn_rate_scale_factor_max"
    };

    internal void RecordEmitter(XElement element, VfxPreviewFieldState state, IEnumerable<XElement> active,
        HashSet<XElement> consumed, HashSet<XElement> modifiers, IReadOnlyList<VfxPreviewDiagnostic> notes)
    {
        var read = consumed.ToHashSet();
        // A modifier node is only a field when VfxDocument exposes that exact node, never its unread descendants.
        read.UnionWith(modifiers);
        _captures[element] = new(state, active.ToHashSet(), read, notes);
    }

    internal void RecordRoot(XElement element, VfxPreviewFieldState state, IReadOnlyList<VfxPreviewDiagnostic>? notes = null)
        => _captures[element] = new(state, [], [], notes?.ToArray() ?? []);

    internal void RecordEventResult(VfxPreviewEmitter emitter, string field, VfxPreviewFieldState state, string reason)
        => _events[(new(emitter.PreviewRootKind, emitter.PreviewRootName, emitter.PreviewRootOccurrence), field)] = (state, reason);

    internal void RecordReference(XElement element, VfxPreviewFieldState state, IReadOnlyList<VfxPreviewEmitter> children,
        IReadOnlyList<VfxPreviewDiagnostic> notes)
    {
        var sharedNotes = notes.ToArray();
        _captures[element] = new(state, [], [], sharedNotes);
        foreach (var layer in _layers.Where(layer => layer.Kind == "Override" && layer.Element.Parent == element))
        {
            var childState = state;
            if (state is VfxPreviewFieldState.Connected or VfxPreviewFieldState.PendingConfirmation)
                childState = (string?)layer.Element.Attribute("enable") == "false" ? VfxPreviewFieldState.Disabled
                    : children.Any(child => !child.IsEventTemplate && child.Name == layer.Name)
                        ? VfxPreviewFieldState.Connected : VfxPreviewFieldState.NotConnected;
            _captures[layer.Element] = new(childState, [], [], sharedNotes);
        }
    }

    internal VfxPreviewEditorInfo Build(VfxConversion conversion)
    {
        var occurrences = new Dictionary<(string Kind, string Name), int>();
        return new(_layers.Select(layer =>
        {
            var group = (layer.Kind, layer.Name);
            var occurrence = occurrences.GetValueOrDefault(group);
            occurrences[group] = occurrence + 1;
            _captures.TryGetValue(layer.Element, out var capture);
            var state = capture?.State ?? VfxPreviewFieldState.Unknown;
            if (layer.Kind is "Emitter" or "Reference" && state == VfxPreviewFieldState.Connected
                && !conversion.Emitters.Any(emitter => emitter.PreviewRootKind == layer.Kind && emitter.PreviewRootName == layer.Name
                    && emitter.PreviewRootOccurrence == occurrence)) state = VfxPreviewFieldState.Failed;
            var identity = new VfxPreviewLayerIdentity(layer.Kind, layer.Name, occurrence);
            var fields = layer.Fields.Select(field =>
            {
                var info = Field(layer, field, state, capture);
                if (state == VfxPreviewFieldState.Connected && info.State != VfxPreviewFieldState.InactiveQuality
                    && _events.TryGetValue((identity, field.Name), out var feedback))
                    info = info with { State = feedback.State, ReasonKeys = Array.AsReadOnly(new[] { feedback.Reason }) };
                return info;
            }).ToArray();
            return new VfxPreviewEditorLayerInfo(layer.Kind, layer.Name, occurrence, state, Array.AsReadOnly(fields))
                { Key = ElementKey(layer.Element) };
        }));
    }

    private VfxPreviewEditorFieldInfo Field(VfxLayer layer, VfxField field, VfxPreviewFieldState layerState, Capture? capture)
    {
        var state = VfxPreviewFieldState.Unknown;
        var reasons = Reasons(capture, field);
        if (layer.Kind == "Emitter")
        {
            state = capture == null ? VfxPreviewFieldState.Unknown
                : layerState is VfxPreviewFieldState.Disabled or VfxPreviewFieldState.Unselected or VfxPreviewFieldState.Failed ? layerState
                : !capture.Active.Contains(field.Element) ? VfxPreviewFieldState.InactiveQuality
                : !capture.Read.Contains(field.Element) && !field.Element.Ancestors().Any(capture.Read.Contains) ? VfxPreviewFieldState.NotConnected
                : !capture.Read.Contains(field.Element) ? VfxPreviewFieldState.PendingConfirmation
                : field.Name == "CullDistances" ? VfxPreviewFieldState.NotConnected
                : ConditionalReads.Contains(field.Name) ? VfxPreviewFieldState.PendingConfirmation : VfxPreviewFieldState.Connected;
        }
        else if (layer.Kind is "Reference" or "Override")
        {
            state = layerState is VfxPreviewFieldState.Disabled or VfxPreviewFieldState.Unselected or VfxPreviewFieldState.Failed or VfxPreviewFieldState.NotConnected
                ? layerState : field.Name is "notes" or "sound_record" ? VfxPreviewFieldState.Ignored
                : field.Attributes.All(attribute => (layer.Kind == "Reference" ? ReferenceAttributes : OverrideAttributes).Contains(attribute.Name.LocalName))
                    ? VfxPreviewFieldState.Connected : VfxPreviewFieldState.Unknown;
        }
        else if (layer.Kind == "Settings")
            state = field.Name == "revision" ? VfxPreviewFieldState.Connected : VfxPreviewFieldState.NotConnected;
        if (state == VfxPreviewFieldState.Connected && reasons.Any(reason => reason == "Vfx.Preview.Unmapped"))
            state = VfxPreviewFieldState.NotConnected;
        else if (state == VfxPreviewFieldState.Connected && reasons.Count != 0)
            state = VfxPreviewFieldState.PendingConfirmation;
        return new(VfxPreviewFieldIdentity.GetKey(field, ElementKey(field.Element)), field.Name, field.Context, field.Quality, state, reasons);
    }

    private IReadOnlyList<string> Reasons(Capture? capture, VfxField field)
    {
        if (capture == null || capture.Notes.Count == 0) return Array.Empty<string>();
        if (!_reasonCaches.TryGetValue(capture.Notes, out var cache))
            _reasonCaches.Add(capture.Notes, cache = []);
        var key = (field.Name, field.Context);
        if (cache.TryGetValue(key, out var existing)) return existing;
        var reasons = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var note in capture.Notes)
            if (Matches(note.Field, field) && seen.Add(note.ReasonKey)) reasons.Add(note.ReasonKey);
        IReadOnlyList<string> result = reasons.Count == 0 ? Array.Empty<string>() : Array.AsReadOnly(reasons.ToArray());
        cache.Add(key, result);
        return result;
    }

    private string ElementKey(XElement element)
    {
        if (_elementKeys.TryGetValue(element, out var existing)) return existing;
        var missing = new Stack<XElement>();
        for (var node = element; !_elementKeys.ContainsKey(node); node = node.Parent!)
        {
            missing.Push(node);
            if (node.Parent == null) break;
        }
        while (missing.TryPop(out var node))
        {
            if (_elementKeys.ContainsKey(node)) continue;
            if (node.Parent == null)
            {
                _elementKeys.Add(node, Uri.EscapeDataString(node.Name.ToString()) + "[0]");
                continue;
            }
            var prefix = _elementKeys[node.Parent] + "/";
            var indexes = new Dictionary<XName, int>();
            foreach (var sibling in node.Parent.Elements())
            {
                var index = indexes.GetValueOrDefault(sibling.Name);
                indexes[sibling.Name] = index + 1;
                _elementKeys.TryAdd(sibling, prefix + Uri.EscapeDataString(sibling.Name.ToString()) + "[" + index + "]");
            }
        }
        return _elementKeys[element];
    }

    private static bool Matches(string note, VfxField field)
    {
        if (note == field.Name || note == field.Context
            || note.StartsWith("modifier/", StringComparison.Ordinal) && note.AsSpan(9).SequenceEqual(field.Context)) return true;
        var remaining = note.AsSpan();
        while (true)
        {
            var slash = remaining.IndexOf('/');
            var segment = slash < 0 ? remaining : remaining[..slash];
            if (segment.SequenceEqual(field.Name)) return true;
            if (slash < 0) return false;
            remaining = remaining[(slash + 1)..];
        }
    }
}
