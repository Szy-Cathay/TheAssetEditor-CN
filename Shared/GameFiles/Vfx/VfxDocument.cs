using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Shared.GameFormats.Vfx;

// VFX files are XML fragments, commonly UTF-16, with several top-level elements.
public sealed class VfxDocument
{
    private readonly byte[] _originalBytes;
    private readonly Encoding _encoding;
    private readonly bool _hasPreamble;
    private readonly string _baseline;
    private readonly string? _declaration;

    public XElement Root { get; }
    public IReadOnlyList<VfxLayer> Layers => BuildLayers();

    private VfxDocument(byte[] bytes, Encoding encoding, bool hasPreamble, XElement root, string? declaration)
    {
        _originalBytes = bytes.ToArray();
        _encoding = encoding;
        _hasPreamble = hasPreamble;
        _declaration = declaration;
        Root = root;
        _baseline = SerializeText();
    }

    public static VfxDocument CreateComposition()
    {
        const string xml = """
            <shader_library file="fx/particle_vfx_library.hlsl"/>
            <vfx revision="2" construction_type="2" enable_in_ted="true" camera_following="false" ref_spawn_speed="-1.00">
              <emitters/><behaviour_chains/><effect_render_stacks/>
            </vfx>
            <vfx_references/>
            <editor_data><effect_components/></editor_data>
            """;
        return Read([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(xml)]);
    }

    public VfxStructureEdit AddReference(string reference, VfxDocument source)
    {
        var name = UniqueName(reference.Replace('\\', '/').Split('/')[^1]);
        var element = new XElement("vfx", new XAttribute("inst_name", name), new XAttribute("vfx_ref", reference),
            new XAttribute("notes", ""), new XAttribute("scale_min", "1.00"), new XAttribute("scale_max", "1.00"),
            new XAttribute("pos_x", "0.00"), new XAttribute("pos_y", "0.00"), new XAttribute("pos_z", "0.00"),
            new XAttribute("rot_x", "0.00"), new XAttribute("rot_y", "0.00"), new XAttribute("rot_z", "0.00"),
            new XAttribute("start_time_min", "0.00"), new XAttribute("start_time_max", "0.00"),
            new XAttribute("infinite_life", "false"), new XAttribute("simulation_speed_multiplier", "1.00"));
        foreach (var emitter in source.Root.Elements("vfx").Elements("emitters").Elements("emitter"))
        {
            var settings = new XElement("emitter", new XAttribute("name", (string?)emitter.Attribute("id") ?? ""),
                new XAttribute("enable", "true"));
            foreach (var bound in new[] { "min", "max" })
            {
                foreach (var channel in new[] { "r", "g", "b", "a" })
                    settings.Add(new XAttribute($"tint_{bound}_{channel}", "1.00"));
                settings.Add(new XAttribute("particle_lifetime_bias_" + bound, "0.00"),
                    new XAttribute("emitter_lifetime_bias_" + bound, "0.00"),
                    new XAttribute("spawn_rate_scale_factor_" + bound, "1.00"));
            }
            settings.Add(new XAttribute("sound_record", ""));
            element.Add(settings);
        }
        var edit = new VfxStructureEdit(element);
        edit.Insert(Container(Root, "vfx_references", edit), element);
        edit.Insert(Container(Container(Root, "editor_data", edit), "effect_components", edit),
            new XElement("component", new XAttribute("id", name)));
        return edit;
    }

    public VfxStructureEdit DuplicateLayer(VfxLayer layer)
    {
        var nodes = ComponentNodes(layer).ToArray();
        var name = UniqueName(layer.Name + "_copy");
        var copies = nodes.Select(node => new XElement(node)).ToArray();
        var edit = new VfxStructureEdit(copies[0]);
        for (var index = 0; index < nodes.Length; index++)
        {
            copies[index].SetAttributeValue(nodes[index] == layer.Element && layer.Kind == "Reference" ? "inst_name" : "id", name);
            edit.Insert(nodes[index].Parent!, copies[index], nodes[index]);
        }
        return edit;
    }

    public VfxStructureEdit RemoveLayer(VfxLayer layer)
    {
        var edit = new VfxStructureEdit(layer.Element);
        foreach (var node in ComponentNodes(layer)) edit.Remove(node);
        return edit;
    }

    private IEnumerable<XElement> ComponentNodes(VfxLayer layer)
    {
        if (layer.Kind is not ("Emitter" or "Reference") || !layer.Element.Ancestors().Contains(Root))
            throw new InvalidOperationException("Vfx.InvalidLayer");
        yield return layer.Element;
        if (layer.Kind == "Emitter")
            foreach (var node in Root.Elements("vfx").Elements().Where(x => x.Name == "behaviour_chains" || x.Name == "effect_render_stacks")
                .Elements().Where(x => (string?)x.Attribute("id") == layer.Name))
                yield return node;
        foreach (var node in Root.Elements("editor_data").Elements("effect_components").Elements("component")
            .Where(x => (string?)x.Attribute("id") == layer.Name))
            yield return node;
    }

    private string UniqueName(string seed)
    {
        var names = Layers.Select(layer => layer.Name).Concat(Root.Descendants("component").Select(x => (string?)x.Attribute("id") ?? ""))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = seed;
        for (var index = 2; names.Contains(name); index++) name = seed + "_" + index;
        return name;
    }

    private static XElement Container(XElement parent, string name, VfxStructureEdit edit)
    {
        var element = parent.Element(name);
        if (element != null) return element;
        element = new XElement(name);
        edit.Insert(parent, element);
        return element;
    }

    public static VfxDocument Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var textReader = new StreamReader(stream, new UTF8Encoding(false, true), true);
        var text = textReader.ReadToEnd();
        var encoding = textReader.CurrentEncoding;
        var preamble = encoding.GetPreamble();
        var hasPreamble = preamble.Length > 0 && bytes.AsSpan().StartsWith(preamble);
        var settings = new XmlReaderSettings
        {
            ConformanceLevel = ConformanceLevel.Fragment,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 32 * 1024 * 1024,
        };
        using var reader = XmlReader.Create(new StringReader(text), settings);
        var root = new XElement("vfx_document");
        string? declaration = null;
        reader.Read();
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.XmlDeclaration)
            {
                declaration = reader.Value;
                reader.Read();
            }
            else
                root.Add(XNode.ReadFrom(reader));
        }

        var effect = root.Element("vfx");
        if (effect == null || (string?)effect.Attribute("revision") is not ("1" or "2"))
            throw new InvalidDataException("Vfx.UnsupportedDocument");
        return new VfxDocument(bytes, encoding, hasPreamble, root, declaration);
    }

    public byte[] Write()
    {
        var text = SerializeText();
        if (text == _baseline)
            return _originalBytes.ToArray();
        var payload = _encoding.GetBytes(text);
        return _hasPreamble ? [.. _encoding.GetPreamble(), .. payload] : payload;
    }

    private string SerializeText()
    {
        var builder = new StringBuilder();
        if (_declaration != null)
            builder.Append("<?xml ").Append(_declaration).Append("?>");
        using (var writer = XmlWriter.Create(builder, new XmlWriterSettings
        {
            ConformanceLevel = ConformanceLevel.Fragment,
            OmitXmlDeclaration = true,
            NewLineHandling = NewLineHandling.None,
        }))
        {
            foreach (var node in Root.Nodes())
                node.WriteTo(writer);
        }
        return builder.ToString();
    }

    private List<VfxLayer> BuildLayers()
    {
        var layers = new List<VfxLayer>();
        var effect = Root.Element("vfx")!;
        foreach (var emitter in effect.Element("emitters")?.Elements("emitter") ?? [])
        {
            var id = (string?)emitter.Attribute("id") ?? "";
            var nodes = new List<XElement> { emitter };
            nodes.AddRange(effect.Elements("behaviour_chains").Elements("behaviour_chain")
                .Where(x => (string?)x.Attribute("id") == id));
            nodes.AddRange(effect.Elements("effect_render_stacks").Elements("effect_render_stack")
                .Where(x => (string?)x.Attribute("id") == id));
            var fields = nodes.SelectMany(x => x.Descendants())
                .Where(x => !x.HasElements && (x.HasAttributes || !string.IsNullOrWhiteSpace(x.Value)))
                .Select(VfxField.FromElement).Where(x => x != null).Cast<VfxField>().ToList();
            layers.Add(new VfxLayer(id, (string?)emitter.Attribute("type") ?? "", "Emitter", "", fields, emitter));
        }

        foreach (var reference in Root.Elements("vfx_references").Elements("vfx"))
        {
            var name = (string?)reference.Attribute("inst_name") ?? "";
            var target = (string?)reference.Attribute("vfx_ref") ?? "";
            layers.Add(new VfxLayer(name, "", "Reference", target,
                AttributeFields(reference, ["inst_name", "vfx_ref"]), reference));
            foreach (var emitter in reference.Elements("emitter"))
            {
                var emitterName = (string?)emitter.Attribute("name") ?? "";
                layers.Add(new VfxLayer(emitterName, name, "Override", target,
                    AttributeFields(emitter, ["name"]), emitter));
            }
        }

        layers.Add(new VfxLayer("", "", "Settings", "", AttributeFields(effect, []), effect));
        return layers;
    }

    private static List<VfxField> AttributeFields(XElement element, string[] excluded)
    {
        var fields = new List<VfxField>();
        var consumed = new HashSet<string>(excluded);
        if (element.Parent?.Name == "vfx_references")
        {
            Group("reference_position", ["pos_x", "pos_y", "pos_z"]);
            Group("reference_rotation", ["rot_x", "rot_y", "rot_z"]);
            Group("reference_scale", ["scale_min", "scale_max"]);
            Group("reference_start", ["start_time_min", "start_time_max"]);
        }
        foreach (var prefix in new[] { "tint_min", "tint_max" })
        {
            var attributes = new[] { "r", "g", "b", "a" }.Select(x => element.Attribute(prefix + "_" + x)).ToArray();
            if (attributes.Any(x => x == null))
                continue;
            fields.Add(new VfxField(prefix, "", element.Name.LocalName, element, attributes!));
            foreach (var attribute in attributes)
                consumed.Add(attribute!.Name.LocalName);
        }
        foreach (var attribute in element.Attributes().Where(x => !consumed.Contains(x.Name.LocalName)))
            fields.Add(new VfxField(attribute.Name.LocalName, "", element.Name.LocalName, element, [attribute]));
        return fields;

        void Group(string name, string[] names)
        {
            var attributes = names.Select(key => element.Attribute(key)).ToArray();
            if (attributes.Any(x => x == null)) return;
            fields.Add(new VfxField(name, "", element.Name.LocalName, element, attributes!));
            foreach (var key in names) consumed.Add(key);
        }
    }
}

public sealed record VfxLayer(string Name, string Type, string Kind, string Reference, IReadOnlyList<VfxField> Fields, XElement Element);

// Preserve node identities so field edits remain valid across structural undo/redo.
public sealed class VfxStructureEdit(XElement selection)
{
    private readonly List<(Action Apply, Action Restore)> _steps = [];
    public XElement Selection { get; } = selection;
    public void Apply() { foreach (var step in _steps) step.Apply(); }
    public void Restore() { foreach (var step in _steps.AsEnumerable().Reverse()) step.Restore(); }
    internal void Insert(XElement parent, XElement node, XNode? after = null) => _steps.Add((
        () => { if (after != null) after.AddAfterSelf(node); else parent.Add(node); }, node.Remove));
    internal void Remove(XElement node)
    {
        var parent = node.Parent!;
        var next = node.NextNode;
        _steps.Add((node.Remove, () => { if (next?.Parent == parent) next.AddBeforeSelf(node); else parent.Add(node); }));
    }
}

public sealed class VfxField(string name, string quality, string context, XElement element, IReadOnlyList<XAttribute> attributes)
{
    public string Name { get; } = name;
    public string Quality { get; } = quality;
    public string Context { get; } = context;
    public XElement Element { get; } = element;
    public IReadOnlyList<XAttribute> Attributes { get; } = attributes;
    public bool IsText => Attributes.Count == 0;
    public string RawValue => IsText ? Element.Value : string.Join(" / ", Attributes.Select(x => x.Value));

    public static VfxField? FromElement(XElement element)
    {
        var attributes = element.Attributes().Where(x => x.Name.LocalName is not ("name" or "type" or "quality" or "id" or "fragment")).ToArray();
        if (attributes.Length == 0 && string.IsNullOrWhiteSpace(element.Value))
            return null;
        var quality = (string?)element.Attribute("quality")
            ?? (string?)element.Ancestors("quality").FirstOrDefault()?.Attribute("value") ?? "";
        var context = (string?)element.Ancestors("modifier").FirstOrDefault()?.Attribute("fragment")
            ?? element.Parent?.Name.LocalName ?? "";
        return new VfxField((string?)element.Attribute("name") ?? element.Name.LocalName, quality, context, element, attributes);
    }
}
