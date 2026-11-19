using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using Editors.Audio.Shared.AudioProject;
using Shared.Core.ModAssistant;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.GameFormats.Wwise;
using static Shared.Core.ModAssistant.AssistantJson;

namespace Editors.Audio;

internal sealed class AudioAssistantWriteSession : IDisposable
{
    private readonly IPackFileService _files;
    private readonly FolderProjectContainer _project;
    private readonly IAudioProjectFileService _parser;
    private readonly IDisposable _guard;
    private readonly List<AudioPackOutput> _written = [];
    public AudioAssistantWriteSession(IPackFileService files, IAudioPackOutputService outputs, IAudioProjectFileService parser,
        ApplicationSettingsService settings, CancellationToken token)
    {
        _files = files; _parser = parser;
        _project = files.GetEditablePack() as FolderProjectContainer ?? throw new InvalidOperationException(Text("NoProject"));
        var revision = _project.CurrentRevision; var game = settings.CurrentSettings.CurrentGame;
        _guard = outputs.BeginGuardedWrites(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_project, files.GetEditablePack()) || revision != _project.CurrentRevision || game != settings.CurrentSettings.CurrentGame)
                throw new InvalidOperationException(Text("StaleTarget"));
        }, written => { _written.AddRange(written); revision = _project.CurrentRevision; });
    }
    public JsonNode Result(bool success, string diagnostic)
    {
        var lastWrites = _written.GroupBy(w => w.FilePath, StringComparer.OrdinalIgnoreCase).Select(g => g.Last()).ToArray();
        foreach (var output in lastWrites)
        {
            var file = _files.FindFile(output.FilePath, _project) ?? throw new InvalidDataException(Text("ReadbackFailed"));
            if (!file.DataSource.ReadData().AsSpan().SequenceEqual(output.Data)) throw new InvalidDataException(Text("ReadbackFailed"));
            if (output.FilePath.EndsWith(".aproj", StringComparison.OrdinalIgnoreCase)) _parser.DeserialiseAudioProject(file);
            else if (output.FilePath.EndsWith(".bnk", StringComparison.OrdinalIgnoreCase)) BnkParser.Parse(file, output.FilePath, false);
            else if (output.FilePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            { using var stream = new MemoryStream(output.Data); using var wave = new NAudio.Wave.WaveFileReader(stream); }
        }
        return Node(new { outcome = success && lastWrites.Length > 0 ? "completed" : lastWrites.Length > 0 ? "partial" : "failed", diagnostic,
            outputs = lastWrites.Take(100).Select(o => new { path = o.FilePath, bytes = o.Data.Length }), total_outputs = lastWrites.Length,
            verification = "disk_bytes_and_supported_native_parsers", game_verified = false });
    }
    public void Dispose() => _guard.Dispose();
}
