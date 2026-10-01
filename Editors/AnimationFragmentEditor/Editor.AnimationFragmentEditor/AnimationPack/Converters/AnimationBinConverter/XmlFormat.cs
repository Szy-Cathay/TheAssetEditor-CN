using System.Xml;
using System.Xml.Serialization;

namespace Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationBinConverter
{
    [XmlRoot(ElementName = "Skeleton")]
    public class Skeleton
    {
        [XmlAttribute(AttributeName = "value")]
        public string Value { get; set; }
    }

    [XmlRoot(ElementName = "MountSkeleton")]
    public class MountSkeleton
    {
        [XmlAttribute(AttributeName = "value")]
        public string Value { get; set; }
    }

    [XmlRoot(ElementName = "Unknown")]
    public class Unknown
    {
        [XmlAttribute(AttributeName = "value")]
        public short Value { get; set; }
    }

    [XmlRoot(ElementName = "BinEntry")]
    public class BinEntry
    {
        [XmlElement(ElementName = "Skeleton")]
        public Skeleton Skeleton { get; set; }
        [XmlElement(ElementName = "MountSkeleton")]
        public MountSkeleton MountSkeleton { get; set; }
        [XmlElement(ElementName = "Fragments")]
        public string Fragments { get; set; }
        [XmlElement(ElementName = "FragmentParameter")]
        public List<FragmentParameter> FragmentParameters { get; set; } = new();
        [XmlElement(ElementName = "Unknown")]
        public Unknown Unknown { get; set; }
        [XmlAttribute(AttributeName = "name")]
        public string Name { get; set; }
    }

    public class FragmentParameter
    {
        [XmlAttribute] public string Name { get; set; } = string.Empty;
        [XmlAttribute] public int Value { get; set; }
    }

    public partial class AnimationBinFileToXmlConverter
    {
        [XmlRoot(ElementName = "Bin")]
        public class Bin
        {
            [XmlElement(ElementName = "BinEntry")]
            public List<BinEntry> BinEntry { get; set; }
        }
    }
}
