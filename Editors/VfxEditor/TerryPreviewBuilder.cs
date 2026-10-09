using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.Vfx;

namespace Editors.VfxEditor;

public sealed record TerryPreviewResource(PackFile File, bool IsOfficial);
public sealed record TerryPreviewPackage(Dictionary<string, byte[]> Files, int EffectCount);

public sealed class TerryPreviewBuilder(LocalizationManager localization)
{
    public const string EffectName = "ae_cn_preview_current";

    public TerryPreviewPackage Build(byte[] currentBytes, string sourcePath,
        Func<string, TerryPreviewResource?> resolve)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var effects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        VisitEffect(Normalize(string.IsNullOrEmpty(sourcePath) ? "vfx\\untitled.xml" : sourcePath), currentBytes, EffectName, 0);
        return new(files, effects.Count);

        string VisitEffect(string path, byte[] bytes, string alias, int depth)
        {
            if (depth > 64 || !active.Add(path))
                throw new InvalidDataException(localization.GetFormat("Vfx.Terry.Cycle", path));
            if (effects.TryGetValue(path, out var existing))
            {
                active.Remove(path);
                return existing;
            }
            var document = VfxDocument.Read(bytes);
            effects.Add(path, alias);
            document.Root.Element("vfx")!.SetAttributeValue("enable_in_ted", "true");
            foreach (var reference in document.Root.Elements("vfx_references").Elements("vfx"))
            {
                var value = (string?)reference.Attribute("vfx_ref");
                if (string.IsNullOrWhiteSpace(value)) continue;
                var childPath = Normalize(value);
                if (!childPath.StartsWith("vfx\\", StringComparison.OrdinalIgnoreCase)) childPath = "vfx\\" + childPath;
                if (!childPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) childPath += ".xml";
                var child = Resolve(childPath);
                var childAlias = "ae_cn_preview_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(childPath)))[..16];
                reference.SetAttributeValue("vfx_ref", VisitEffect(childPath, child.File.DataSource.ReadData(), childAlias, depth + 1));
            }
            VisitXmlResources(document.Root, depth);
            var text = string.Concat(document.Root.Nodes().Select(node => node.ToString(SaveOptions.DisableFormatting)));
            Add("vfx\\" + alias + ".xml", [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)]);
            active.Remove(path);
            return alias;
        }

        TerryPreviewResource Resolve(string path) => resolve(path)
            ?? throw new InvalidDataException(localization.GetFormat("Vfx.Terry.MissingResource", path));

        void VisitXmlResources(XElement root, int depth, bool skipGeometry = false)
        {
            var values = root.DescendantsAndSelf().Attributes().Where(x => x.Name != "vfx_ref").Select(x => x.Value)
                .Concat(root.DescendantsAndSelf().Where(x => !x.HasElements && (!skipGeometry || x.Name != "geometry")).Select(x => x.Value));
            foreach (var value in values)
            {
                var candidate = value.Trim();
                var extension = Path.GetExtension(candidate).ToLowerInvariant();
                if (extension is ".dds" or ".wsmodel" or ".rigid_model_v2" or ".material"
                    || extension == ".xml" && (candidate.Contains('/') || candidate.Contains('\\')))
                    VisitResource(Normalize(candidate), depth + 1);
            }
        }

        void VisitResource(string path, int depth, bool readModelTextures = true)
        {
            if (depth > 64) throw new InvalidDataException(localization.GetFormat("Vfx.Terry.Cycle", path));
            if (!resources.Add(path + (readModelTextures ? "" : "#geometry"))) return;
            var resource = Resolve(path);
            var extension = Path.GetExtension(path).ToLowerInvariant();
            // Original textures already belong to the game; only inspect structured dependencies.
            if (resource.IsOfficial && (extension == ".dds" || extension == ".rigid_model_v2" && !readModelTextures)) return;
            var bytes = resource.File.DataSource.ReadData();
            if (!resource.IsOfficial) Add(path, bytes);
            if (extension is ".wsmodel" or ".material" or ".xml")
            {
                using var stream = new MemoryStream(bytes);
                using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                var root = XElement.Load(reader);
                var hasMaterialOverrides = extension == ".wsmodel" && root.Descendants("material").Any();
                if (hasMaterialOverrides)
                    foreach (var geometry in root.Elements("geometry"))
                        VisitResource(Normalize(geometry.Value.Trim()), depth + 1, false);
                VisitXmlResources(root, depth, hasMaterialOverrides);
            }
            else if (extension == ".rigid_model_v2" && readModelTextures)
            {
                var model = ModelFactory.Create().Load(bytes);
                foreach (var texture in model.ModelList.SelectMany(x => x).SelectMany(x => x.Material.GetAllTextures()))
                    if (!string.IsNullOrWhiteSpace(texture.Path)) VisitResource(Normalize(texture.Path), depth + 1);
            }
        }

        void Add(string path, byte[] bytes)
        {
            if (files.ContainsKey(path)) return;
            totalBytes += bytes.Length;
            if (files.Count >= 4096 || totalBytes > 512L * 1024 * 1024)
                throw new InvalidDataException(localization.Get("Vfx.Terry.TooLarge"));
            files.Add(path, bytes);
        }
    }

    private string Normalize(string value)
    {
        var path = value.Replace('/', '\\').ToLowerInvariant();
        if (Path.IsPathRooted(path) || path.Split('\\').Any(x => x is "" or "." or "..")
            || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || path.Contains(':'))
            throw new InvalidDataException(localization.GetFormat("Vfx.Terry.InvalidPath", value));
        return path;
    }
}
