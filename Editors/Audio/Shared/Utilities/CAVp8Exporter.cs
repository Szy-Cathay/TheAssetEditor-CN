using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Audio.Codecs.Vorbis;
using Shared.GameFormats.Video;

namespace Editors.Audio.Shared.Utilities
{
    public static class CAVp8Exporter
    {
        public static byte[] ExportToIvf(PackFile caVp8PackFile)
        {
            var caVp8File = new CAVp8File(
                caVp8PackFile.DataSource.ReadData());
            return new IvfFile(caVp8File).WriteData();
        }

        public static byte[] ExportToWebM(
            PackFile caVp8PackFile,
            PackFile? wemPackFile)
        {
            var caVp8File = new CAVp8File(
                caVp8PackFile.DataSource.ReadData());
            var webMFile = new WebMFile
            {
                Width = caVp8File.Width,
                Height = caVp8File.Height,
                Framerate = caVp8File.Framerate,
                FrameTable = caVp8File.FrameTable,
                FrameData = caVp8File.FrameData,
            };

            if (wemPackFile != null)
            {
                var vorbisAudio = VorbisAudio.CreateFromWemBytes(
                    wemPackFile.DataSource.ReadData());
                webMFile.VorbisCodecPrivate =
                    vorbisAudio.VorbisCodecPrivateData;
                webMFile.VorbisAudioPackets = vorbisAudio.Packets;
                webMFile.VorbisSampleRate =
                    checked((int)vorbisAudio.SampleRate);
                webMFile.VorbisChannels = vorbisAudio.Channels;
            }

            return webMFile.WriteData();
        }
    }
}
