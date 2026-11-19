using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;
using Editors.ImportExport.Exporting.Exporters;
using Editors.ImportExport.Exporting.Exporters.DdsToMaterialPng;
using Editors.ImportExport.Exporting.Exporters.DdsToNormalPng;
using Editors.ImportExport.Exporting.Presentation.DdsToPng;
using Editors.ImportExport.Exporting.Presentation.RmvToGltf;
using Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;
using Editors.ImportExport.Importing.Importers.GltfToRmv;
using Editors.ImportExport.Misc;
using Editors.ImportImport.Importing.Presentation.RmvToGltf;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Misc;
using Shared.Core.ModAssistant;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using static Shared.Core.ModAssistant.AssistantJson;

namespace Editors.ImportExport;

public sealed class ImportExportAssistantAdapter(IServiceScopeFactory scopeFactory, IPackFileService files, ApplicationSettingsService settings)
    : IAssistantResourceAdapter, IAssistantWorkspaceAdapter
{
    public IEnumerable<AssistantOperation> GetOperations(PackFileContainer owner, PackFile file, AssistantIds ids)
    {
        var extension = Path.GetExtension(file.Name).ToLowerInvariant();
        if (extension is not (".dds" or ".rigid_model_v2" or ".wsmodel")) yield break;
        JsonNode State() => Node(new { file = files.GetFullPath(file, owner), source = "saved_resource" });
        yield return new AssistantOperation("convert.export", ids.Get(file), Text("ConvertExport"), Schema(
            ("format", Str("", extension == ".dds" ? ["png", "normal_png", "material_png"] : ["gltf"]), true),
            ("destination", Str("", "project", "external"), true), ("folder", Str("project-relative folder, or new absolute external folder"), true),
            ("textures", Bool(), false), ("skeleton", Bool(), false), ("animations", Array(Str("full resource path"), 100), false)),
            AssistantEffect.Disk, Text("DiskRecovery"), State, async (args, token) =>
            {
                if (!ReferenceEquals(files.FindFile(files.GetFullPath(file, owner)), file)) throw new InvalidOperationException(Text("ShadowedSource"));
                var boundProject = files.GetEditablePack();
                var sourceHash = SHA256.HashData(file.DataSource.ReadData());
                using var scope = scopeFactory.CreateScope();
                var services = scope.ServiceProvider;
                var format = args.String("format");
                var stage = Path.Combine(DirectoryHelper.LocalApplicationDirectory, "Assistant", "exports", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stage);
                var outputName = Path.GetFileNameWithoutExtension(file.Name) + (format == "gltf" ? ".gltf" : ".png");
                var outputPath = Path.Combine(stage, outputName);
                if (format == "gltf")
                {
                    var exporter = services.GetServices<IExporterViewModel>().OfType<RmvToGltfExporterViewModel>().Single();
                    if (exporter.CanExportFile(file) == ExportSupportEnum.NotSupported) throw new ArgumentException(Text("Unavailable"));
                    exporter.ExportTextures = args.Boolean("textures", true);
                    exporter.ExportSkeleton = args.Boolean("skeleton", true);
                    if (args["animations"] is JsonArray paths)
                    {
                        foreach (var path in paths)
                            exporter.AnimationFiles.Add(exporter.AvailableAnimations.Single(x => string.Equals(x.PackPath, path!.GetValue<string>(), StringComparison.OrdinalIgnoreCase)));
                        exporter.ExportAnimations = true;
                    }
                    if (!await Task.Run(() => exporter.Execute(file, outputPath), token)) throw new InvalidOperationException(Text("NotCompleted"));
                    SharpGLTF.Schema2.ModelRoot.Load(outputPath);
                }
                else if (format == "png")
                {
                    var exporter = services.GetServices<IExporterViewModel>().OfType<DdsToPngExporterViewModel>().Single();
                    await Task.Run(() => exporter.Execute(file, outputPath), token);
                }
                else if (format == "normal_png")
                    await Task.Run(() => services.GetRequiredService<IDdsToNormalPngExporter>().Export(files.GetFullPath(file, owner), outputPath, true, outputName), token);
                else
                    await Task.Run(() => services.GetRequiredService<IDdsToMaterialPngExporter>().Export(files.GetFullPath(file, owner), outputPath, true, outputName), token);
                token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(boundProject, files.GetEditablePack()) || !sourceHash.SequenceEqual(SHA256.HashData(file.DataSource.ReadData())))
                    throw new InvalidOperationException(Text("StaleTarget") + ": " + stage);
                var outputs = Directory.GetFiles(stage, "*", SearchOption.AllDirectories);
                if (outputs.Length == 0 || outputs.Length > 2048 || outputs.Sum(p => new FileInfo(p).Length) > 256L * 1024 * 1024) throw new InvalidOperationException(Text("ResourceTooLarge"));
                foreach (var png in outputs.Where(p => Path.GetExtension(p).Equals(".png", StringComparison.OrdinalIgnoreCase)))
                {
                    using var stream = File.OpenRead(png);
                    var decoded = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    if (decoded.Frames.Count == 0) throw new InvalidDataException(Text("ReadbackFailed"));
                }
                var destination = args.String("folder");
                string[] written;
                if (args.String("destination") == "project")
                {
                    var project = files.GetEditablePack() as FolderProjectContainer ?? throw new InvalidOperationException(Text("NoProject"));
                    var writes = outputs.Select(p => new PackFileWrite(Path.Combine(destination, Path.GetRelativePath(stage, p)), File.ReadAllBytes(p))).ToArray();
                    var result = files.ApplyFileWrites(project, writes, false);
                    written = result.Select(f => files.GetFullPath(f, project)).ToArray();
                    foreach (var write in writes)
                        if (!files.FindFile(write.Path, project)!.DataSource.ReadData().SequenceEqual(write.Content)) throw new InvalidDataException(Text("ReadbackFailed"));
                }
                else
                {
                    if (!Path.IsPathFullyQualified(destination) || Directory.Exists(destination) || File.Exists(destination)) throw new ArgumentException(Text("NewExportFolder"));
                    destination = FolderProjectPathPolicy.ResolveExternalOutputPath(destination);
                    foreach (var project in files.GetAllPackfileContainers().OfType<FolderProjectContainer>()) FolderProjectPathPolicy.EnsureOutputOutsideProject(project.ProjectRoot, destination);
                    Directory.CreateDirectory(destination);
                    var copied = new List<string>();
                    foreach (var source in outputs)
                    {
                        var target = Path.Combine(destination, Path.GetRelativePath(stage, source));
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(source, target, false);
                        if (!SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(target)))) throw new InvalidDataException(Text("ReadbackFailed"));
                        copied.Add(target);
                    }
                    written = copied.ToArray();
                }
                return Node(new { outputs = written, readback_verified = true, format, staging_copy = stage, source = "saved_resource" });
            });
    }

    public IEnumerable<AssistantOperation> GetOperations(AssistantIds ids)
    {
        yield return AssistantOperation.Sync("convert.formats", "workspace", Text("ConvertFormats"), Schema(), AssistantEffect.Query,
            Text("NoDataChange"), () => Node(new { game = settings.CurrentSettings.CurrentGame.ToString() }),
            _ => Node(new { model_import = new[] { "gltf", "glb" }, model_export = new[] { "gltf" },
                texture_export = new[] { "png", "normal_png", "material_png" }, fbx_supported = false, detail = Text("FbxUnsupported") }));
        yield return new AssistantOperation("convert.import_gltf", "workspace", Text("ConvertImport"), Schema(
            ("source", Str("absolute gltf/glb path"), true), ("folder", Str("project-relative destination folder"), true),
            ("meshes", Bool(), false), ("materials", Bool(), false), ("animations", Bool(), false),
            ("auto_scale_humanoid", Bool(), false), ("new_skeleton", Str(), false),
            ("forward", Str("", Enum.GetNames<GltfSourceForwardDirection>()), false), ("sample_rate", Num(.1, 240, "samples per second; omitted means automatic"), false)),
            AssistantEffect.Disk, Text("DiskRecovery"), () => Node(new { game = settings.CurrentSettings.CurrentGame.ToString() }), async (args, token) =>
            {
                var project = files.GetEditablePack() as FolderProjectContainer ?? throw new InvalidOperationException(Text("NoProject"));
                var source = args.String("source");
                if (!Path.IsPathFullyQualified(source) || !File.Exists(source) || Path.GetExtension(source).ToLowerInvariant() is not (".gltf" or ".glb")) throw new ArgumentException(Text("InvalidParameters"));
                FolderProjectPathPolicy.ResolveFilePath(project.ProjectRoot, Path.Combine(args.String("folder"), "destination.rigid_model_v2"));
                using var scope = scopeFactory.CreateScope();
                var importer = scope.ServiceProvider.GetServices<Importing.Presentation.IImporterViewModel>().OfType<RmvToGltfImporterViewModel>().Single();
                var input = new PackFile(source, new FileSystemSource(source));
                importer.Initialize(input);
                importer.ImportMeshes = args.Boolean("meshes", true); importer.ImportMaterials = args.Boolean("materials", true);
                importer.ImportAnimations = args.Boolean("animations", true); importer.AutoScaleHumanoid = args.Boolean("auto_scale_humanoid", true);
                if (args["new_skeleton"] != null) importer.NewSkeletonName = args.String("new_skeleton");
                if (args["forward"] != null) importer.SourceForwardDirection = Enum.Parse<GltfSourceForwardDirection>(args.String("forward"));
                if (args["sample_rate"] != null) { importer.AutoDetectAnimationKeysPerSecond = false; importer.AnimationKeysPerSecond = (float)args.Number("sample_rate"); }
                var revision = project.CurrentRevision;
                var game = settings.CurrentSettings.CurrentGame;
                var result = await Task.Run(() => importer.Execute(input, args.String("folder"), project, game, null, token,
                    () => !token.IsCancellationRequested && ReferenceEquals(project, files.GetEditablePack()) && project.CurrentRevision == revision && settings.CurrentSettings.CurrentGame == game), token);
                var verified = new List<string>();
                foreach (var path in result.OutputPaths)
                {
                    var output = files.FindFile(path, project) ?? throw new InvalidDataException(Text("ReadbackFailed"));
                    if (Path.GetExtension(path) == ".anim") AnimationFile.Create(output);
                    if (Path.GetExtension(path) == ".rigid_model_v2") ModelFactory.Create().Load(output.DataSource.ReadData());
                    verified.Add(path);
                }
                return Node(new { outcome = result.Succeeded ? "completed" : result.OutputPaths.Count > 0 ? "partial" : "failed", result.Succeeded, result.OutputPaths, result.Warnings, result.Errors, result.HumanoidScale, result.SourceForward, result.MaterialSummary, verified });
            }, () => files.GetEditablePack() is FolderProjectContainer ? null : Text("NoProject"));
    }
}
