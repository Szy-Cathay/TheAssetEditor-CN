using System.Security.Cryptography;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;

namespace Editors.Reports;

public sealed class ReportExecutionContext
{
    public sealed record Input(string Path, PackFile File, string Container);
    public IReadOnlyList<Input> Inputs { get; }
    public string OutputDirectory { get; }
    public CancellationToken CancellationToken { get; }
    public GameTypeEnum Game { get; }
    public List<string> Diagnostics { get; } = [];
    public List<string> Outputs { get; } = [];
    public HashSet<string> VerifiedOutputs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, PackFile> Dependencies { get; } = new(StringComparer.OrdinalIgnoreCase);
    private long _writtenBytes;

    public ReportExecutionContext(IReadOnlyList<Input> inputs, string outputDirectory, CancellationToken cancellationToken, GameTypeEnum game = GameTypeEnum.Unknown)
    {
        Inputs = inputs;
        OutputDirectory = Path.TrimEndingDirectorySeparator(FolderProjectPathPolicy.ResolveExternalOutputPath(outputDirectory)) + Path.DirectorySeparatorChar;
        if (Directory.Exists(OutputDirectory) || File.Exists(outputDirectory)) throw new IOException(global::Shared.Core.ModAssistant.AssistantJson.Text("ReportNewDirectory"));
        CancellationToken = cancellationToken;
        Game = game;
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(OutputDirectory);
    }

    public List<(string FileName, PackFile Pack)> WithPaths(string extension) => Inputs
        .Where(input => input.Path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        .Select(input => (input.Path, input.File)).ToList();
    public List<PackFile> Files(string extension) => WithPaths(extension).Select(input => input.Pack).ToList();
    public string PathOf(PackFile file) => Inputs.Single(input => ReferenceEquals(input.File, file)).Path;
    public void Check() => CancellationToken.ThrowIfCancellationRequested();
    public void Error(string path, Exception error)
    {
        Check();
        Diagnostics.Add(path + ": " + error.Message);
    }
    public static void WriteText(ReportExecutionContext? context, string path, string text)
    {
        if (context == null) { File.WriteAllText(path, text); return; }
        context.Check();
        path = FolderProjectPathPolicy.ResolveExternalOutputPath(path);
        if (!path.StartsWith(context.OutputDirectory, StringComparison.OrdinalIgnoreCase)) throw new IOException(global::Shared.Core.ModAssistant.AssistantJson.Text("ReportPathEscaped"));
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        if (context.Outputs.Count >= 2048 || context._writtenBytes + bytes.Length > 128L * 1024 * 1024) throw new IOException(global::Shared.Core.ModAssistant.AssistantJson.Text("ReportOutputLimit"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            context.Outputs.Add(path);
            context._writtenBytes += bytes.Length;
            stream.Write(bytes);
        }
        if (!SHA256.HashData(bytes).SequenceEqual(SHA256.HashData(File.ReadAllBytes(path)))) throw new IOException(global::Shared.Core.ModAssistant.AssistantJson.Text("ReadbackFailed"));
        context.VerifiedOutputs.Add(path);
    }
}
