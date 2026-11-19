using System.Text;
using System.Text.Json.Nodes;
using Editors.Reports.Geometry;
using Shared.Core.ModAssistant;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.WsModel;
using GameWorld.Core.Utility;
using static Shared.Core.ModAssistant.AssistantJson;

namespace Editors.Reports;

public sealed class ReportAssistantAdapter(IPackFileService files, MetaDataFileParser metadata) : IAssistantResourceAdapter
{
    public IEnumerable<AssistantOperation> GetOperations(PackFileContainer owner, PackFile file, AssistantIds ids)
    {
        var extension = Path.GetExtension(file.Name).ToLowerInvariant();
        if (extension is not (".rigid_model_v2" or ".anim" or ".meta" or ".wsmodel")) yield break;
        JsonNode State() => Node(new { path = files.GetFullPath(file, owner), source = "saved_resource" });
        string Format()
        {
            if (file.DataSource.Size > 32 * 1024 * 1024) throw new InvalidOperationException(Text("ResourceTooLarge"));
            if (extension == ".rigid_model_v2") return new RmvToTextReport().Format(ModelFactory.Create().Load(file.DataSource.ReadData()), false);
            if (extension == ".meta") return Newtonsoft.Json.JsonConvert.SerializeObject(metadata.ParseFile(file.DataSource.ReadData()), Newtonsoft.Json.Formatting.Indented);
            if (extension == ".wsmodel")
            {
                var model = new WsModelFile(file);
                return Node(new { geometry = model.GeometryPaths, materials = model.MaterialList.Select(m => new { m.LodIndex, m.PartIndex, m.MaterialPath,
                    material = files.FindFile(m.MaterialPath) is { } material ? new WsModelMaterialFile(material) : null }) }).ToJsonString(new() { WriteIndented = true });
            }
            var animation = AnimationFile.Create(file);
            return Node(new { animation.Header, parts = animation.AnimationParts.Select(p => new { frames = p.DynamicFrames.Count,
                translations = p.TranslationMappings, rotations = p.RotationMappings }) }).ToJsonString(new() { WriteIndented = true });
        }
        yield return AssistantOperation.Sync("report.read", ids.Get(file), Text("ReportRead"), Schema(("offset", Num(0, 10000000, integer: true), false)), AssistantEffect.Query, Text("NoDataChange"), State, args =>
        { var report = Format(); var offset = Math.Min(args.Integer("offset"), report.Length); return Node(new { length = report.Length, offset, text = report.Substring(offset, Math.Min(16000, report.Length - offset)) }); });
        yield return AssistantOperation.Sync("report.export", ids.Get(file), Text("ReportExport"), Schema(("output", Str("new absolute text file path"), true)), AssistantEffect.Disk, Text("DiskRecovery"), State, args =>
        {
            var path = args.String("output");
            if (!Path.IsPathFullyQualified(path)) throw new ArgumentException(Text("InvalidParameters"));
            path = FolderProjectPathPolicy.ResolveExternalOutputPath(path);
            foreach (var project in files.GetAllPackfileContainers().OfType<FolderProjectContainer>()) FolderProjectPathPolicy.EnsureOutputOutsideProject(project.ProjectRoot, path);
            var report = Format();
            using (var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write), new UTF8Encoding(false))) writer.Write(report);
            if (File.ReadAllText(path) != report) throw new InvalidDataException(Text("ReadbackFailed"));
            return Node(new { output = path, bytes = new FileInfo(path).Length, source = "saved_resource" });
        });
        if (extension != ".rigid_model_v2") yield break;
        yield return AssistantOperation.Sync("model.references", ids.Get(file), Text("ModelReferences"), Schema(("offset", Num(0, 100000, integer: true), false)), AssistantEffect.Query, Text("NoDataChange"), State, args =>
        {
            if (file.DataSource.Size > 32 * 1024 * 1024) throw new InvalidOperationException(Text("ResourceTooLarge"));
            var model = ModelFactory.Create().Load(file.DataSource.ReadData());
            var paths = model.ModelList.SelectMany(l => l).SelectMany(m => m.Material.GetAllTextures()).Select(t => t.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return Node(new { skeleton = model.Header.SkeletonName, total = paths.Length, textures = paths.Skip(args.Integer("offset")).Take(100).Select(path =>
            {
                var resolved = TexturePathResolver.FindTextureFile(files, path, out var resolvedPath);
                return new { path, resolved_path = resolvedPath, found = resolved != null, owner = resolved == null ? null : files.GetPackFileContainer(resolved)?.Name };
            }) });
        });
    }
}
