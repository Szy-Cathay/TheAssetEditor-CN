using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Editors.Reports.Animation;
using Editors.Reports.Audio;
using Editors.Reports.DeepSearch;
using Editors.Reports.Files;
using Editors.Reports.Geometry;
using Editors.Audio.Shared.Wwise;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.ModAssistant;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Dat;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.WsModel;
using static Shared.Core.ModAssistant.AssistantJson;

namespace Editors.Reports;

public sealed class ReportTemplateAssistantAdapter(IServiceScopeFactory scopes, IPackFileService files,
    IEditorManager editors, ApplicationSettingsService settings) : IAssistantWorkspaceAdapter
{
    private static readonly Dictionary<string, string[]> Templates = new()
    {
        ["file_list"] = [],
        ["rmv2"] = [".rigid_model_v2"],
        ["materials"] = [".wsmodel"],
        ["meta_table"] = [".meta"],
        ["meta_json"] = [".meta", ".anim", ".animpack"],
        ["dat"] = [".dat"],
        ["dialogue_info"] = [".bnk", ".dat"],
        ["event_names"] = [".bnk", ".dat"],
        ["deep_search"] = [],
    };

    public IEnumerable<AssistantOperation> GetOperations(AssistantIds ids)
    {
        JsonNode State() => Node(new
        {
            game = settings.CurrentSettings.CurrentGame.ToString(),
            containers = files.GetAllPackfileContainers().Select(c => new
            {
                id = ids.Get(c),
                c.Name,
                revision = (c as FolderProjectContainer)?.CurrentRevision
            }),
            source = "saved_resources_only"
        });
        yield return AssistantOperation.Sync("reports.templates", "workspace", Text("ReportTemplates"), Schema(), AssistantEffect.Query,
            Text("NoDataChange"), State, _ => Node(new
            {
                templates = Templates.Select(t => new { name = t.Key, extensions = t.Value }),
                max_files = 100,
                max_total_bytes = 64 * 1024 * 1024,
                notes = Text("ReportTemplateLimits")
            }));
        yield return new AssistantOperation("reports.generate", "workspace", Text("ReportGenerate"), Schema(
            ("template", Str("", Templates.Keys.ToArray()), true),
            ("inputs", Array(Schema(("container", Str("stable container id from context"), true), ("path", Str("exact resource path"), true)), 100), true),
            ("output", Str("new absolute directory outside all loaded folder projects"), true),
            ("search", Str("deep_search only; ASCII resource contents"), false), ("case_sensitive", Bool(), false)),
            AssistantEffect.Disk, Text("ReportRecovery"), State, async (args, token) =>
            {
                var template = args.String("template");
                var extensions = Templates[template];
                var output = FolderProjectPathPolicy.ResolveExternalOutputPath(args.String("output"));
                foreach (var project in files.GetAllPackfileContainers().OfType<FolderProjectContainer>())
                    FolderProjectPathPolicy.EnsureOutputOutsideProject(project.ProjectRoot, output);
                if (Directory.Exists(output) || File.Exists(output)) throw new ArgumentException(Text("NewExportFolder"));
                if (template == "deep_search" && string.IsNullOrWhiteSpace(args.OptionalString("search"))) throw new ArgumentException(Text("InvalidParameters"));
                long total = 0;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var inputs = new List<ReportExecutionContext.Input>();
                foreach (var item in args["inputs"]!.AsArray().Cast<JsonObject>())
                {
                    token.ThrowIfCancellationRequested();
                    var owner = files.GetAllPackfileContainers().Single(c => ids.Get(c) == item.String("container"));
                    var path = item.String("path");
                    if (!seen.Add(path) || extensions.Length > 0 && !extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                        throw new ArgumentException(Text("InvalidParameters"));
                    // Report generators retain relative resource paths in their outputs.
                    FolderProjectPathPolicy.NormalizeRelativePath(path);
                    var file = files.FindFile(path, owner) ?? throw new FileNotFoundException(Text("MissingReference"));
                    if (editors.GetAllEditors().OfType<IFileEditor>().Any(e => ReferenceEquals(e.CurrentFile, file) && e is ISaveableEditor { HasUnsavedChanges: true }))
                        throw new InvalidOperationException(Text("UnsavedSource"));
                    total += file.DataSource.Size;
                    if (file.DataSource.Size > 32 * 1024 * 1024 || total > 64 * 1024 * 1024) throw new InvalidOperationException(Text("ResourceTooLarge"));
                    inputs.Add(new(path, PackFile.CreateFromBytes(file.Name, file.DataSource.ReadData()), owner.Name));
                }
                if (template is "dialogue_info" or "event_names" && !inputs.Any(i => i.Path.EndsWith(".bnk", StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException(Text("InvalidParameters"));
                var dependencies = new Dictionary<string, PackFile>(StringComparer.OrdinalIgnoreCase);
                if (template == "materials")
                {
                    foreach (var input in inputs)
                        foreach (var material in new WsModelFile(input.File).MaterialList)
                        {
                            token.ThrowIfCancellationRequested();
                            if (dependencies.ContainsKey(material.MaterialPath)) continue;
                            var dependency = files.FindFile(material.MaterialPath);
                            if (dependency == null) continue;
                            if (editors.GetAllEditors().OfType<IFileEditor>().Any(e => ReferenceEquals(e.CurrentFile, dependency) && e is ISaveableEditor { HasUnsavedChanges: true }))
                                throw new InvalidOperationException(Text("UnsavedSource"));
                            total += dependency.DataSource.Size;
                            if (dependency.DataSource.Size > 32 * 1024 * 1024 || total > 64 * 1024 * 1024 || dependencies.Count >= 100)
                                throw new InvalidOperationException(Text("ResourceTooLarge"));
                            dependencies.Add(material.MaterialPath, PackFile.CreateFromBytes(dependency.Name, dependency.DataSource.ReadData()));
                        }
                }
                var context = new ReportExecutionContext(inputs, output, token, settings.CurrentSettings.CurrentGame);
                foreach (var pair in dependencies) context.Dependencies.Add(pair.Key, pair.Value);
                var attila = settings.CurrentSettings.CurrentGame == GameTypeEnum.Attila;
                var outcome = "completed";
                try
                {
                    using var scope = scopes.CreateScope();
                    var services = scope.ServiceProvider;
                    await Task.Run(() =>
                    {
                        context.Check();
                        switch (template)
                        {
                            case "file_list": services.GetRequiredService<FileListReportGenerator>().Create(context); break;
                            case "rmv2": services.GetRequiredService<Rmv2ReportGenerator>().Create(context: context); break;
                            case "materials": services.GetRequiredService<MaterialReportGenerator>().Create(context); break;
                            case "meta_table": services.GetRequiredService<AnimMetaDataReportGenerator>().Create(context); break;
                            case "meta_json": services.GetRequiredService<AnimMetaDataJsonGenerator>().Create(context); break;
                            case "dat": services.GetRequiredService<DatDumper>().Create(context); break;
                            case "dialogue_info": case "event_names": WriteAudio(context, template, attila); break;
                            case "deep_search":
                                var matches = inputs.Where(input => { context.Check(); return DeepSearchReport.Contains(input.File, args.String("search"), args.Boolean("case_sensitive")); });
                                ReportExecutionContext.WriteText(context, Path.Combine(output, "DeepSearch.txt"), string.Join(Environment.NewLine, matches.Select(i => i.Container + ": " + i.Path)));
                                break;
                        }
                    }, token);
                    if (context.Diagnostics.Count != 0) outcome = "partial";
                    if (context.Outputs.Count == 0)
                    {
                        context.Diagnostics.Add(Text("ReportNoOutput"));
                        outcome = "failed";
                    }
                }
                catch (OperationCanceledException) { outcome = "cancelled"; }
                catch (Exception error) { context.Diagnostics.Add(error.Message); outcome = context.Outputs.Count == 0 ? "failed" : "partial"; }
                return Node(new
                {
                    outcome,
                    template,
                    output,
                    outputs = context.Outputs.Select(path => new
                    {
                        path,
                        bytes = new FileInfo(path).Length,
                        readback_verified = context.VerifiedOutputs.Contains(path),
                        sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
                    }),
                    diagnostics = context.Diagnostics,
                    source = "saved_resource_snapshot",
                    inputs = inputs.Select(i => new
                    {
                        i.Path,
                        i.Container,
                        sha256 = Convert.ToHexString(SHA256.HashData(i.File.DataSource.ReadData()))
                    }),
                    dependencies = dependencies.Select(i => new { path = i.Key, sha256 = Convert.ToHexString(SHA256.HashData(i.Value.DataSource.ReadData())) }),
                    readback_verified = context.Outputs.Count > 0 && context.Outputs.Count == context.VerifiedOutputs.Count
                });
            });
    }

    private static void WriteAudio(ReportExecutionContext context, string template, bool attila)
    {
        var names = new Dictionary<uint, string>();
        foreach (var (_, file) in context.WithPaths(".dat"))
        {
            context.Check();
            foreach (var name in DatFileParser.Parse(file, attila).CreateFileNameList()) names.TryAdd(WwiseHash.Compute(name), name);
        }
        string Name(uint id) => names.TryGetValue(id, out var name) ? name : id.ToString();
        foreach (var (path, file) in context.WithPaths(".bnk"))
        {
            context.Check();
            try
            {
                if (file.Name.Equals("init.bnk", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(Text("ReportInitUnsupported"));
                var bank = BnkParser.Parse(file, path, true);
                var items = bank.HircChunk.HircItems;
                if (items.Any(item => item is UnknownHircItem)) context.Diagnostics.Add(path + ": " + Text("ReportUnknownHirc"));
                var lines = template == "dialogue_info"
                    ? items.OfType<ICAkDialogueEvent>().Select(item => DialogueEventInfoPrinter.Format(item, Name))
                    : items.Where(item => item is ICAkDialogueEvent or ICAkEvent).Select(item => DialogueEventAndEventNamePrinter.Format(item, Name));
                ReportExecutionContext.WriteText(context, Path.Combine(context.OutputDirectory, path + ".txt"), string.Join(Environment.NewLine, lines));
            }
            catch (Exception error) { context.Error(path, error); }
        }
    }
}
