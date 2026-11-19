#nullable enable annotations
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Editors.Audio.Shared.Utilities;
using Shared.Core.ModAssistant;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Video;
using static Shared.Core.ModAssistant.AssistantJson;

namespace Editors.Audio;

public sealed class VideoAssistantAdapter(IPackFileService files) : IAssistantResourceAdapter
{
    public IEnumerable<AssistantOperation> GetOperations(PackFileContainer owner, PackFile file, AssistantIds ids)
    {
        if (!file.Name.EndsWith(".ca_vp8", StringComparison.OrdinalIgnoreCase)) yield break;
        byte[] Read(PackFile source)
        {
            if (source.DataSource.Size > 32 * 1024 * 1024) throw new InvalidOperationException(Text("ResourceTooLarge"));
            return source.DataSource.ReadData();
        }
        JsonNode State() => Node(new { path = files.GetFullPath(file, owner), bytes = file.DataSource.Size, source = "saved_resource" });
        yield return AssistantOperation.Sync("video.inspect", ids.Get(file), Text("VideoInspect"), Schema(), AssistantEffect.Query, Text("NoDataChange"), State, _ =>
        {
            var video = new CAVp8File(Read(file));
            return Node(new { video.Version, video.CodecFourCC, video.Width, video.Height, video.Framerate, video.NumberOfFrames });
        });
        yield return new AssistantOperation("video.export", ids.Get(file), Text("VideoExport"),
            Schema(("format", Str("", "ivf", "webm"), true), ("output", Str("new absolute file outside projects"), true),
                ("wem_path", Str("optional audio for WebM, from this same source container"), false)), AssistantEffect.Disk, Text("DiskRecovery"), State,
            async (args, token) =>
            {
                var output = args.String("output"); var format = args.String("format");
                if (!Path.IsPathFullyQualified(output) || !output.EndsWith("." + format, StringComparison.OrdinalIgnoreCase) || format == "ivf" && args["wem_path"] != null)
                    throw new ArgumentException(Text("InvalidParameters"));
                output = FolderProjectPathPolicy.ResolveExternalOutputPath(output);
                foreach (var project in files.GetAllPackfileContainers().OfType<FolderProjectContainer>()) FolderProjectPathPolicy.EnsureOutputOutsideProject(project.ProjectRoot, output);
                if (File.Exists(output)) throw new IOException(Text("OutputExists"));
                var source = PackFile.CreateFromBytes(file.Name, Read(file));
                PackFile? audio = null;
                if (args["wem_path"] != null)
                {
                    var wem = files.FindFile(args.String("wem_path"), owner) ?? throw new FileNotFoundException(Text("MissingReference"));
                    audio = PackFile.CreateFromBytes(wem.Name, Read(wem));
                }
                var bytes = await Task.Run(() => format == "ivf" ? CAVp8Exporter.ExportToIvf(source) : CAVp8Exporter.ExportToWebM(source, audio), token);
                token.ThrowIfCancellationRequested();
                FolderProjectPathPolicy.ResolveExternalOutputPath(output);
                AtomicFileWriter.WriteAllBytes(output, bytes, overwrite: false);
                if (!File.ReadAllBytes(output).AsSpan().SequenceEqual(bytes)) throw new InvalidDataException(Text("ReadbackFailed"));
                return Node(new { output, format, bytes = bytes.Length, audio = audio != null, verified = "native_container_conversion_and_disk_bytes", decoded_playback_verified = false });
            });
    }
}
