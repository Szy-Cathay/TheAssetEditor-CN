using Shared.ByteParsing;
using Shared.Core.PackFiles.Models;

namespace Shared.GameFormats.AnimationPack
{
    public class CampaignAnimationBin
    {
        public string Reference { get; set; }
        public string SkeletonName { get; set; }
        public int Version { get; set; }
        public int HeaderValue { get; set; } = 1;
        public List<StatusItem> Status { get; set; } = new List<StatusItem>();

        public interface ICampaignAnimationBinEntry
        {
            byte[] ToBytes(ref List<string> stringTable);
        }

        public class StatusItem
        {
            public string Name { get; set; }

            public List<PersistentMeta> PersitantMetaData { get; set; }
            public List<PersistentMeta_Pose> Poses { get; set; }
            public List<PersistentMeta_Dock> Docks { get; set; }
            public List<AnimationEntry> Idle { get; set; }
            public List<PortholeEntry> Porthole { get; set; }
            public List<AnimationEntry> Selection { get; set; }
            public List<TransitionEntry> Transitions { get; set; }
            public List<ActionEntry> Action { get; set; }
            public List<MissingType> Unk1 { get; set; }
            public List<UnknownEntry> Unknown { get; set; }
            public List<MissingType> Unk3 { get; set; }
            public List<LocomotionEntry> Locomotion { get; set; }
        }

        public class AnimationEntry : ICampaignAnimationBinEntry
        {
            public string Animation { get; set; }
            public string Type { get; set; }
            public string MetaFile { get; set; }
            public string SoundMeta { get; set; }
            public float BlendTime { get; set; }
            public float Weight { get; set; }

            public static AnimationEntry FromChunck(ByteChunk byteChunk, string[] stringTable)
            {
                var output = new AnimationEntry();
                output.Animation = byteChunk.ReadStringTableIndex(stringTable);
                output.Type = byteChunk.ReadStringTableIndex(stringTable);
                output.MetaFile = byteChunk.ReadStringTableIndex(stringTable);
                output.SoundMeta = byteChunk.ReadStringTableIndex(stringTable);
                output.BlendTime = byteChunk.ReadSingle();
                output.Weight = byteChunk.ReadSingle();
                return output;
            }

            public byte[] ToBytes(ref List<string> stringTable)
            {
                var chuck = new ChuckWriter();
                chuck.WriteStringTableIndex(Animation, ref stringTable, false);
                chuck.WriteStringTableIndex(Type, ref stringTable, false);
                chuck.WriteStringTableIndex(MetaFile, ref stringTable, false);
                chuck.WriteStringTableIndex(SoundMeta, ref stringTable, false);
                chuck.Write(BlendTime, ByteParsers.Single);
                chuck.Write(Weight, ByteParsers.Single);
                return chuck.GetBytes();
            }
        }

        public class TransitionEntry : ICampaignAnimationBinEntry
        {
            public string Animation { get; set; }
            public string AnimationMeta { get; set; }
            public string SoundMeta { get; set; }
            public string Type { get; set; }
            public float BlendTime { get; set; }
            public string TransitionTo { get; set; }

            public static TransitionEntry FromChunck(ByteChunk byteChunk, string[] stringTable)
            {
                var output = new TransitionEntry();
                output.Animation = byteChunk.ReadStringTableIndex(stringTable);
                output.AnimationMeta = byteChunk.ReadStringTableIndex(stringTable);
                output.SoundMeta = byteChunk.ReadStringTableIndex(stringTable);
                output.Type = byteChunk.ReadStringTableIndex(stringTable);
                output.BlendTime = byteChunk.ReadSingle();
                output.TransitionTo = byteChunk.ReadStringTableIndex(stringTable);
                return output;
            }

            public byte[] ToBytes(ref List<string> stringTable)
            {
                var chuck = new ChuckWriter();
                chuck.WriteStringTableIndex(Animation, ref stringTable, false);
                chuck.WriteStringTableIndex(AnimationMeta, ref stringTable, false);
                chuck.WriteStringTableIndex(SoundMeta, ref stringTable, false);
                chuck.WriteStringTableIndex(Type, ref stringTable, false);
                chuck.Write(BlendTime, ByteParsers.Single);
                chuck.WriteStringTableIndex(TransitionTo, ref stringTable, false);
                return chuck.GetBytes();
            }
        }

        public class PersistentMeta : ICampaignAnimationBinEntry
        {
            public string Animation { get; set; }
            public string AnimationMeta { get; set; }
            public string SoundMeta { get; set; }
            public string Type { get; set; }
            public float BlendTime { get; set; }

            public static PersistentMeta FromChunck(ByteChunk byteChunk, string[] stringTable)
            {
                var output = new PersistentMeta();
                output.Animation = byteChunk.ReadStringTableIndex(stringTable);
                output.AnimationMeta = byteChunk.ReadStringTableIndex(stringTable);
                output.SoundMeta = byteChunk.ReadStringTableIndex(stringTable);
                output.Type = byteChunk.ReadStringTableIndex(stringTable);
                output.BlendTime = byteChunk.ReadSingle();
                return output;
            }

            public byte[] ToBytes(ref List<string> stringTable)
            {
                var chuck = new ChuckWriter();
                chuck.WriteStringTableIndex(Animation, ref stringTable, false);
                chuck.WriteStringTableIndex(AnimationMeta, ref stringTable, false);
                chuck.WriteStringTableIndex(SoundMeta, ref stringTable, false);
                chuck.WriteStringTableIndex(Type, ref stringTable, false);
                chuck.Write(BlendTime, ByteParsers.Single);
                return chuck.GetBytes();
            }
        }

        public class PersistentMeta_Pose : ICampaignAnimationBinEntry
        {
            public string Animation { get; set; }
            public string AnimationMeta { get; set; }
            public string SoundMeta { get; set; }
            public float Weight { get; set; }
            public float BlendTime { get; set; }
            public int PoseId { get; set; }

            public static PersistentMeta_Pose FromChunck(ByteChunk byteChunk, string[] stringTable)
            {
                var output = new PersistentMeta_Pose();
                output.Animation = byteChunk.ReadStringTableIndex(stringTable);
                output.AnimationMeta = byteChunk.ReadStringTableIndex(stringTable);
                output.SoundMeta = byteChunk.ReadStringTableIndex(stringTable);
                output.Weight = byteChunk.ReadSingle();
                output.BlendTime = byteChunk.ReadSingle();
                output.PoseId = byteChunk.ReadInt32();
                return output;
            }


            public byte[] ToBytes(ref List<string> stringTable)
            {
                var chuck = new ChuckWriter();
                chuck.WriteStringTableIndex(Animation, ref stringTable, false);
                chuck.WriteStringTableIndex(AnimationMeta, ref stringTable, false);
                chuck.WriteStringTableIndex(SoundMeta, ref stringTable, false);
                chuck.Write(Weight, ByteParsers.Single);
                chuck.Write(BlendTime, ByteParsers.Single);
                chuck.Write(PoseId, ByteParsers.Int32);
                return chuck.GetBytes();
            }
        }

        public class PersistentMeta_Dock : ICampaignAnimationBinEntry
        {
            public string Animation { get; set; }
            public string AnimationMeta { get; set; }
            public string SoundMeta { get; set; }
            public float Weight { get; set; }
            public float BlendTime { get; set; }
            public string Dock { get; set; }

            public static PersistentMeta_Dock FromChunck(ByteChunk byteChunk, string[] stringTable)
            {
                var output = new PersistentMeta_Dock();
                output.Animation = byteChunk.ReadStringTableIndex(stringTable);
                output.AnimationMeta = byteChunk.ReadStringTableIndex(stringTable);
                output.SoundMeta = byteChunk.ReadStringTableIndex(stringTable);
                output.Weight = byteChunk.ReadSingle();
                output.BlendTime = byteChunk.ReadSingle();
                output.Dock = byteChunk.ReadStringTableIndex(stringTable);
                return output;
            }


            public byte[] ToBytes(ref List<string> stringTable)
            {
                var chuck = new ChuckWriter();
                chuck.WriteStringTableIndex(Animation, ref stringTable, false);
                chuck.WriteStringTableIndex(AnimationMeta, ref stringTable, false);
                chuck.WriteStringTableIndex(SoundMeta, ref stringTable, false);
                chuck.Write(Weight, ByteParsers.Single);
                chuck.Write(BlendTime, ByteParsers.Single);
                chuck.WriteStringTableIndex(Dock, ref stringTable, false);
                return chuck.GetBytes();
            }
        }

        public class PortholeEntry : ICampaignAnimationBinEntry
        {
            public string Value0 { get; set; }
            public string Value1 { get; set; }
            public string Value2 { get; set; }
            public string Value3 { get; set; }
            public float Value4 { get; set; }
            public float Value5 { get; set; }

            public static PortholeEntry FromChunck(ByteChunk byteChunk, string[] stringTable)
            {
                var output = new PortholeEntry();
                output.Value0 = stringTable[byteChunk.ReadInt32()];
                output.Value1 = stringTable[byteChunk.ReadInt32()];
                output.Value2 = stringTable[byteChunk.ReadInt32()];
                output.Value3 = stringTable[byteChunk.ReadInt32()];
                output.Value4 = byteChunk.ReadSingle();
                output.Value5 = byteChunk.ReadSingle();
                return output;
            }

            public byte[] ToBytes(ref List<string> stringTable)
            {
                var chuck = new ChuckWriter();
                chuck.WriteStringTableIndex(Value0, ref stringTable, false);
                chuck.WriteStringTableIndex(Value1, ref stringTable, false);
                chuck.WriteStringTableIndex(Value2, ref stringTable, false);
                chuck.WriteStringTableIndex(Value3, ref stringTable, false);
                chuck.Write(Value4, ByteParsers.Single);
                chuck.Write(Value5, ByteParsers.Single);
                return chuck.GetBytes();
            }
        }

        public class ActionEntry : ICampaignAnimationBinEntry
        {
            public bool HasExtraString { get; set; }
            public string ExtraString { get; set; } = string.Empty;
            public string Animation { get; set; }
            public string Type { get; set; }
            public string Meta { get; set; }
            public string SoundMeta { get; set; }
            public float BlendTime { get; set; }
            public string ActionType { get; set; }
            public int ActionId { get; set; }
            public bool Unknown { get; set; }

            public static ActionEntry FromChunck(ByteChunk byteChunk, string[] stringTable)
            {
                var start = byteChunk.Index;
                try
                {
                    var standard = ReadAction(byteChunk, stringTable, false);
                    var standardEnd = byteChunk.Index;
                    if (standard.ActionType == stringTable[0] || standard.ActionType == stringTable[1])
                    {
                        byteChunk.Index = start;
                        try { return ReadAction(byteChunk, stringTable, true); }
                        catch { byteChunk.Index = standardEnd; }
                    }
                    return standard;
                }
                catch
                {
                    byteChunk.Index = start;
                    return ReadAction(byteChunk, stringTable, true);
                }
            }

            static ActionEntry ReadAction(ByteChunk byteChunk, string[] stringTable, bool extraString)
            {
                var output = new ActionEntry();
                output.Animation = byteChunk.ReadStringTableIndex(stringTable);
                output.Type = byteChunk.ReadStringTableIndex(stringTable);
                output.Meta = byteChunk.ReadStringTableIndex(stringTable);
                output.HasExtraString = extraString;
                if (extraString)
                    output.ExtraString = byteChunk.ReadStringTableIndex(stringTable);

                output.SoundMeta = byteChunk.ReadStringTableIndex(stringTable);
                output.BlendTime = byteChunk.ReadSingle();

                output.ActionType = byteChunk.ReadStringTableIndex(stringTable);
                output.ActionId = byteChunk.ReadInt32();
                output.Unknown = byteChunk.ReadBool();
                return output;
            }

            public byte[] ToBytes(ref List<string> stringTable)
            {
                var chuck = new ChuckWriter();
                chuck.WriteStringTableIndex(Animation, ref stringTable, false);
                chuck.WriteStringTableIndex(Type, ref stringTable, false);
                chuck.WriteStringTableIndex(Meta, ref stringTable, false);
                if (HasExtraString)
                    chuck.WriteStringTableIndex(ExtraString, ref stringTable, false);
                chuck.WriteStringTableIndex(SoundMeta, ref stringTable, false);
                chuck.Write(BlendTime, ByteParsers.Single);
                chuck.WriteStringTableIndex(ActionType, ref stringTable, false);
                chuck.Write(ActionId, ByteParsers.Int32);
                chuck.Write(Unknown, ByteParsers.Bool);
                return chuck.GetBytes();
            }
        }

        public class LocomotionEntry : ICampaignAnimationBinEntry
        {
            public string Animation { get; set; }
            public string AnimationMeta { get; set; }
            public string SoundMeta { get; set; }
            public string Type { get; set; }
            public float Weight { get; set; }
            public float ModelScale { get; set; }
            public float DistanceTraveled { get; set; }
            public float DistanceMinTravled { get; set; }

            public static LocomotionEntry FromChunck(ByteChunk byteChunk, string[] stringTable)
            {
                var output = new LocomotionEntry();
                output.Animation = byteChunk.ReadStringTableIndex(stringTable);
                output.AnimationMeta = byteChunk.ReadStringTableIndex(stringTable);
                output.SoundMeta = byteChunk.ReadStringTableIndex(stringTable);
                output.Type = byteChunk.ReadStringTableIndex(stringTable);
                output.Weight = byteChunk.ReadSingle();
                output.ModelScale = byteChunk.ReadSingle();
                output.DistanceTraveled = byteChunk.ReadSingle();
                output.DistanceMinTravled = byteChunk.ReadSingle();
                return output;
            }

            public byte[] ToBytes(ref List<string> stringTable)
            {
                var chuck = new ChuckWriter();
                chuck.WriteStringTableIndex(Animation, ref stringTable, false);
                chuck.WriteStringTableIndex(AnimationMeta, ref stringTable, false);
                chuck.WriteStringTableIndex(SoundMeta, ref stringTable, false);
                chuck.WriteStringTableIndex(Type, ref stringTable, false);
                chuck.Write(Weight, ByteParsers.Single);
                chuck.Write(ModelScale, ByteParsers.Single);
                chuck.Write(DistanceTraveled, ByteParsers.Single);
                chuck.Write(DistanceMinTravled, ByteParsers.Single);
                return chuck.GetBytes();
            }
        }

        public class UnknownEntry : ICampaignAnimationBinEntry
        {
            public string Animation { get; set; }
            public string AnimationMeta { get; set; }
            public string SoundMeta { get; set; }
            public string Type { get; set; }
            public float BlendTime { get; set; }
            public float Weight { get; set; }
            public int Value { get; set; }

            public static UnknownEntry FromChunck(ByteChunk byteChunk, string[] stringTable)
            {
                var output = new UnknownEntry();
                output.Animation = byteChunk.ReadStringTableIndex(stringTable);
                output.AnimationMeta = byteChunk.ReadStringTableIndex(stringTable);
                output.SoundMeta = byteChunk.ReadStringTableIndex(stringTable);
                output.Type = byteChunk.ReadStringTableIndex(stringTable);
                output.BlendTime = byteChunk.ReadSingle();
                output.Weight = byteChunk.ReadSingle();
                output.Value = byteChunk.ReadInt32();
                return output;
            }

            public byte[] ToBytes(ref List<string> stringTable)
            {
                var chuck = new ChuckWriter();
                chuck.WriteStringTableIndex(Animation, ref stringTable, false);
                chuck.WriteStringTableIndex(AnimationMeta, ref stringTable, false);
                chuck.WriteStringTableIndex(SoundMeta, ref stringTable, false);
                chuck.WriteStringTableIndex(Type, ref stringTable, false);
                chuck.Write(BlendTime, ByteParsers.Single);
                chuck.Write(Weight, ByteParsers.Single);
                chuck.Write(Value, ByteParsers.Int32);
                return chuck.GetBytes();
            }
        }

        public class MissingType : ICampaignAnimationBinEntry
        {
            public string Animation { get; set; }
            public string Type { get; set; }
            public string AnimationMeta { get; set; }
            public string SoundMeta { get; set; }
            public float Value0 { get; set; }
            public float Value1 { get; set; }
            public float Value2 { get; set; }
            public int Value3 { get; set; }

            public static MissingType FromChunck(ByteChunk byteChunk, string[] stringTable)
            {
                return new MissingType
                {
                    Animation = byteChunk.ReadStringTableIndex(stringTable),
                    Type = byteChunk.ReadStringTableIndex(stringTable),
                    AnimationMeta = byteChunk.ReadStringTableIndex(stringTable),
                    SoundMeta = byteChunk.ReadStringTableIndex(stringTable),
                    Value0 = byteChunk.ReadSingle(), Value1 = byteChunk.ReadSingle(),
                    Value2 = byteChunk.ReadSingle(), Value3 = byteChunk.ReadInt32(),
                };
            }

            public byte[] ToBytes(ref List<string> stringTable)
            {
                var writer = new ChuckWriter();
                writer.WriteStringTableIndex(Animation, ref stringTable, false);
                writer.WriteStringTableIndex(Type, ref stringTable, false);
                writer.WriteStringTableIndex(AnimationMeta, ref stringTable, false);
                writer.WriteStringTableIndex(SoundMeta, ref stringTable, false);
                writer.Write(Value0, ByteParsers.Single); writer.Write(Value1, ByteParsers.Single);
                writer.Write(Value2, ByteParsers.Single); writer.Write(Value3, ByteParsers.Int32);
                return writer.GetBytes();
            }
        }
    }



    public class CampaignAnimationBinLoader
    {
        delegate T CreateEntryDelegate<T>(ByteChunk chung, string[] table);

        public static CampaignAnimationBin Load(ByteChunk data)
        {
            var outputFile = new CampaignAnimationBin();
            outputFile.Version = data.ReadInt32();

            if (outputFile.Version != 3 && outputFile.Version != 2)
                throw new InvalidDataException($"战役动画版本 {outputFile.Version} 不受支持，仅支持版本 2 和 3。");

            var strTableOffset = data.ReadInt32();
            if (strTableOffset < 24 || strTableOffset > data.Buffer.Length - 4)
                throw new InvalidDataException("战役动画的字符串表位置无效。");
            var strTableChunk = new ByteChunk(data.Buffer, strTableOffset);

            var stringTable = ReadStrTable(strTableChunk);

            var skeletonStrIndex = data.ReadInt32();                // 1
            var selfRefStringIndex = data.ReadInt32();              // 0
            outputFile.HeaderValue = data.ReadInt32();

            outputFile.Reference = stringTable[selfRefStringIndex];
            outputFile.SkeletonName = stringTable[skeletonStrIndex];

            var numStatuses = data.ReadInt32();
            if (numStatuses < 0 || numStatuses > (strTableOffset - data.Index) / 16)
                throw new InvalidDataException("战役状态数量超出实际数据范围。");
            for (var i = 0; i < numStatuses; i++)
            {
                var status = LoadStatus(data, stringTable, outputFile.Version);
                outputFile.Status.Add(status);
            }

            if (strTableOffset != data.Index)
                throw new InvalidDataException("战役动画包含未识别的数据，无法安全编辑。");

            return outputFile;
        }

        public static byte[] Write(CampaignAnimationBin bin, string fileName)
        {
            var dataWriter = new ChuckWriter();
            var stringTable = new List<string>();

            dataWriter.Write(1, ByteParsers.Int32);
            dataWriter.Write(0, ByteParsers.Int32);
            dataWriter.Write(bin.HeaderValue, ByteParsers.Int32);

            stringTable.Add(fileName);
            stringTable.Add(bin.SkeletonName);

            // Statuses
            dataWriter.Write(bin.Status.Count, ByteParsers.Int32);
            foreach (var status in bin.Status)
                WriteStatus(status, dataWriter, ref stringTable, bin.Version);

            var dataBytes = dataWriter.GetBytes();
            var finalWriter = new ChuckWriter();
            finalWriter.Write(bin.Version, ByteParsers.Int32);
            finalWriter.Write(dataBytes.Length + 8, ByteParsers.Int32);
            finalWriter.AddBytes(dataBytes);

            finalWriter.Write(stringTable.Count, ByteParsers.Int32);
            foreach (var str in stringTable)
                finalWriter.Write(str, ByteParsers.String);

            return finalWriter.GetBytes();
        }

        static CampaignAnimationBin.StatusItem LoadStatus(ByteChunk data, string[] strTable, int version)
        {
            var statusName = data.ReadStringTableIndex(strTable);
            var currentStatus = new CampaignAnimationBin.StatusItem() { Name = statusName };

            if (statusName == "global") // Special case
            {
                currentStatus.PersitantMetaData = LoadSlots(data, strTable, CampaignAnimationBin.PersistentMeta.FromChunck);
                currentStatus.Poses = LoadSlots(data, strTable, CampaignAnimationBin.PersistentMeta_Pose.FromChunck);
                currentStatus.Docks = LoadSlots(data, strTable, CampaignAnimationBin.PersistentMeta_Dock.FromChunck);
            }
            else
            {
                currentStatus.Idle = LoadSlots(data, strTable, CampaignAnimationBin.AnimationEntry.FromChunck);
                currentStatus.Porthole = LoadSlots(data, strTable, CampaignAnimationBin.PortholeEntry.FromChunck);
                currentStatus.Selection = LoadSlots(data, strTable, CampaignAnimationBin.AnimationEntry.FromChunck);
                if (version >= 3)
                    currentStatus.Transitions = LoadSlots(data, strTable, CampaignAnimationBin.TransitionEntry.FromChunck);
                currentStatus.Action = LoadSlots(data, strTable, CampaignAnimationBin.ActionEntry.FromChunck);
                currentStatus.Unk1 = LoadSlots(data, strTable, CampaignAnimationBin.MissingType.FromChunck);    // Always zero
                currentStatus.Unknown = LoadSlots(data, strTable, CampaignAnimationBin.UnknownEntry.FromChunck); // What is this?
                currentStatus.Unk3 = LoadSlots(data, strTable, CampaignAnimationBin.MissingType.FromChunck);    // Always zero
                currentStatus.Locomotion = LoadSlots(data, strTable, CampaignAnimationBin.LocomotionEntry.FromChunck);
            }

            return currentStatus;
        }

        static List<T> LoadSlots<T>(ByteChunk data, string[] stringTable, CreateEntryDelegate<T> createEntryDelegate)
        {
            var numItems = data.ReadInt32();
            if (numItems < 0 || numItems > data.BytesLeft / 20)
                throw new InvalidDataException("战役动作数量超出实际数据范围。");
            var output = new List<T>();
            for (var i = 0; i < numItems; i++)
            {
                var newItem = createEntryDelegate(data, stringTable);
                output.Add(newItem);
            }

            if (output.Count == 0)
                return null;

            return output;
        }

        static void WriteStatus(CampaignAnimationBin.StatusItem statusItem, ChuckWriter writer, ref List<string> stringTable, int version)
        {
            writer.WriteStringTableIndex(statusItem.Name, ref stringTable, false);

            if (statusItem.Name == "global")
            {
                WriteSlot(statusItem.PersitantMetaData, writer, ref stringTable);
                WriteSlot(statusItem.Poses, writer, ref stringTable);
                WriteSlot(statusItem.Docks, writer, ref stringTable);
            }
            else
            {
                WriteSlot(statusItem.Idle, writer, ref stringTable);
                WriteSlot(statusItem.Porthole, writer, ref stringTable);
                WriteSlot(statusItem.Selection, writer, ref stringTable);
                if (version >= 3)
                    WriteSlot(statusItem.Transitions, writer, ref stringTable);
                WriteSlot(statusItem.Action, writer, ref stringTable);
                WriteSlot(statusItem.Unk1, writer, ref stringTable);
                WriteSlot(statusItem.Unknown, writer, ref stringTable);
                WriteSlot(statusItem.Unk3, writer, ref stringTable);
                WriteSlot(statusItem.Locomotion, writer, ref stringTable);
            }
        }

        static void WriteSlot<T>(IEnumerable<T> entries, ChuckWriter writer, ref List<string> stringTable) where T : CampaignAnimationBin.ICampaignAnimationBinEntry
        {
            if (entries == null)
            {
                writer.Write(0, ByteParsers.Int32);
                return;
            }

            writer.Write(entries.Count(), ByteParsers.Int32);
            foreach (var entry in entries)
            {
                var bytes = entry.ToBytes(ref stringTable);
                writer.AddBytes(bytes);
            }
        }

        static string[] ReadStrTable(ByteChunk data)
        {
            var stringTable = new List<string>();
            var numTableEntires = data.ReadInt32();
            if (numTableEntires < 2 || numTableEntires > data.BytesLeft / 2)
                throw new InvalidDataException("战役动画的字符串数量超出实际数据范围。");
            for (var i = 0; i < numTableEntires; i++)
                stringTable.Add(data.ReadString());
            return stringTable.ToArray();
        }
    }

    public static class AnimationCampaignBinHelper
    {
        public static void BatchProcess(List<PackFile> fileList)
        {
            var counter = 0;
            var loadErrors = new List<string>();
            var writeErrors = new List<string>();
            var reLoadErrors = new List<string>();
            var output = new List<CampaignAnimationBin>();
            foreach (var f in fileList)
            {

                // Load
                // -----------------
                var chunkf = f.DataSource.ReadDataAsChunk();
                CampaignAnimationBin loadedBin = null;

                try
                {
                    loadedBin = CampaignAnimationBinLoader.Load(chunkf);
                    output.Add(loadedBin);
                }
                catch (Exception e)
                {
                    loadErrors.Add(f.Name + " " + e.Message);
                }


                // Write
                // -----------------


                // Reload
                // -----------------


                counter++;
            }

            var x0 = loadErrors.Where(x => x.Contains("missing schema")).ToList();      // 52
            var x1 = loadErrors.Where(x => x.Contains("Index was outside")).ToList();
            var x2 = loadErrors.Where(x => x.Contains("Metadata error")).ToList();
            var x3 = loadErrors.Where(x => x.Contains("Sequence contains no elements")).ToList();
            var x4 = loadErrors.Where(x => x.Contains("Version error")).ToList();   // 1
            var x5 = loadErrors.Where(x => x.Contains("Unknown datatype")).ToList();   // 1

            var eTotal = x0.Count + x1.Count + x2.Count + x3.Count + x4.Count + x5.Count;     // 53 
            return;
        }
    }


}
