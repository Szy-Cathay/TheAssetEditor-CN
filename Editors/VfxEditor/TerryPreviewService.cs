using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;

namespace Editors.VfxEditor;

public sealed record TerryPreviewResult(string ScenePath, string SourceName, int EffectCount, int FileCount);

public interface ITerryPreviewService
{
    Task<TerryPreviewResult> PreviewAsync(byte[] bytes, string sourcePath, PackFileContainer? owner, CancellationToken cancellationToken);
    Task DisableAsync();
}

public sealed class TerryPreviewService(IPackFileService files, ApplicationSettingsService settings,
    LocalizationManager localization) : ITerryPreviewService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _activeGeneration;
    public const string PackName = "asseteditor_cn_vfx_preview.pack";

    public async Task<TerryPreviewResult> PreviewAsync(byte[] bytes, string sourcePath, PackFileContainer? owner, CancellationToken cancellationToken)
    {
        var game = GamePath();
        EnsureGameClosed();
        var containers = new[] { owner, files.GetEditablePack() }
            .Concat(files.GetAllPackfileContainers().AsEnumerable().Reverse())
            .Where(x => x != null).Cast<PackFileContainer>().Distinct().ToArray();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(async () =>
            {
                var package = new TerryPreviewBuilder(localization).Build(bytes, sourcePath, path =>
                {
                    foreach (var container in containers)
                    {
                        var file = files.FindFile(path, container);
                        if (file != null) return new TerryPreviewResource(file, container.IsCaPackFile);
                    }
                    return null;
                });
                var cache = CachePath(game);
                Directory.CreateDirectory(cache);
                var target = Path.Combine(game, "data", PackName);
                var statePath = Path.Combine(cache, "state.json");
                var state = ReadOwnedState(target, statePath);
                // Each scene is a new export; a scene edited by the user is never overwritten.
                var session = Path.Combine(cache, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(session);
                var staged = Path.Combine(session, PackName);
                PackFileServiceUtility.ExportSnapshot(staged, package.Files,
                    GameInformationDatabase.Games[GameTypeEnum.Warhammer3], PackFileCAType.MOVIE);
                var sceneDirectory = Path.Combine(game, "assembly_kit", "working_data", "AssetEditor.CN", "TerryPreview", Path.GetFileName(session));
                Directory.CreateDirectory(sceneDirectory);
                var scene = TerryPreviewScene.Write(sceneDirectory);
                var hash = Hash(staged);
                cancellationToken.ThrowIfCancellationRequested();
                await TerryPreviewLauncher.CloseAsync(game, localization, cancellationToken);
                EnsureGameClosed();
                ReadOwnedState(target, statePath);
                // A pending hash also makes an interrupted installation recoverable.
                WriteState(statePath, new(state?.Hash, hash));
                var incoming = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.Copy(staged, incoming, false);
                File.Move(incoming, target, true);
                WriteState(statePath, new(hash, null));
                var process = TerryPreviewLauncher.Start(game, scene);
                _activeGeneration = session;
                _ = DisableWhenExitedAsync(process, game, hash, session);
                await TerryPreviewLauncher.WaitForSceneAsync(process, scene, localization, cancellationToken);
                return new TerryPreviewResult(scene, string.IsNullOrEmpty(sourcePath)
                    ? localization.Get("Vfx.Composition.Untitled") : Path.GetFileNameWithoutExtension(sourcePath),
                    package.EffectCount, package.Files.Count);
            }, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task DisableWhenExitedAsync(Process process, string game, string hash, string generation)
    {
        try
        {
            await process.WaitForExitAsync();
            await _gate.WaitAsync();
            try
            {
                var target = Path.Combine(game, "data", PackName);
                if (_activeGeneration == generation && File.Exists(target) && Hash(target) == hash)
                    File.Move(target, Path.Combine(CachePath(game), "disabled-" + Guid.NewGuid().ToString("N") + ".pack"));
            }
            finally { _gate.Release(); }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Shared.Core.ErrorHandling.Logging.Create<TerryPreviewService>().Warning(exception, "Could not deactivate Terry preview");
        }
        finally { process.Dispose(); }
    }

    public async Task DisableAsync()
    {
        var game = GamePath();
        EnsureGameClosed();
        await _gate.WaitAsync();
        try
        {
            await TerryPreviewLauncher.CloseAsync(game, localization, CancellationToken.None);
            var target = Path.Combine(game, "data", PackName);
            var cache = CachePath(game);
            ReadOwnedState(target, Path.Combine(cache, "state.json"));
            if (File.Exists(target))
                File.Move(target, Path.Combine(cache, "disabled-" + Guid.NewGuid().ToString("N") + ".pack"));
        }
        finally { _gate.Release(); }
    }

    private string GamePath()
    {
        var game = settings.GetGamePathForCurrentGame();
        if (settings.CurrentSettings.CurrentGame != GameTypeEnum.Warhammer3)
            throw new InvalidOperationException(localization.Get("Vfx.Terry.UnsupportedGame"));
        // Application settings store the data directory, not the installation root.
        if (!string.IsNullOrEmpty(game) && Path.GetFileName(Path.TrimEndingDirectorySeparator(game)).Equals("data", StringComparison.OrdinalIgnoreCase))
            game = Directory.GetParent(Path.TrimEndingDirectorySeparator(game))?.FullName;
        if (string.IsNullOrEmpty(game) || !Directory.Exists(Path.Combine(game, "data")))
            throw new InvalidOperationException(localization.Get("Vfx.Terry.NoGame"));
        if (!File.Exists(Path.Combine(game, "assembly_kit", "binaries", "tweak.modder.x64.exe")))
            throw new InvalidOperationException(localization.Get("Vfx.Terry.NoKit"));
        return Path.GetFullPath(game);
    }

    private void EnsureGameClosed()
    {
        foreach (var name in new[] { "Warhammer3" })
        {
            var processes = Process.GetProcessesByName(name);
            var running = processes.Length > 0;
            foreach (var process in processes) process.Dispose();
            if (running) throw new InvalidOperationException(localization.Get("Vfx.Terry.CloseFirst"));
        }
    }

    private PreviewState? ReadOwnedState(string target, string statePath)
    {
        foreach (var path in new[] { target, statePath, target + ".new", statePath + ".new" })
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(localization.GetFormat("Vfx.Terry.ProtectedFile", path));
        PreviewState? state = File.Exists(statePath) ? JsonSerializer.Deserialize<PreviewState>(File.ReadAllText(statePath)) : null;
        if (File.Exists(target) && (state == null || Hash(target) is var hash && hash != state.Hash && hash != state.PendingHash))
            throw new IOException(localization.GetFormat("Vfx.Terry.ProtectedFile", target));
        return state;
    }

    private static string CachePath(string game) => Path.Combine(DirectoryHelper.ApplicationDirectory, "TerryPreview",
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(game.ToLowerInvariant())))[..16]);
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
    private static void WriteState(string path, PreviewState state)
    {
        File.WriteAllText(path + ".new", JsonSerializer.Serialize(state));
        File.Move(path + ".new", path, true);
    }
    private sealed record PreviewState(string? Hash, string? PendingHash);
}
