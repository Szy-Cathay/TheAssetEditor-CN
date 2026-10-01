using System.Xml;
using System.Xml.Serialization;

namespace Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationFragmentConverter
{

        [XmlRoot(ElementName = "BlendInTime")]
        public class BlendInTime
        {
            [XmlAttribute(AttributeName = "Value")]
            public float Value { get; set; }
        }

        [XmlRoot(ElementName = "SelectionWeight")]
        public class SelectionWeight
        {
            [XmlAttribute(AttributeName = "Value")]
            public float Value { get; set; }
        }

        public class ValueItem
        {
            [XmlAttribute(AttributeName = "Value")]
            public string Value { get; set; }
        }

        [XmlRoot(ElementName = "AnimationEntry")]
        public class AnimationEntry
        {
            [XmlElement(ElementName = "File")]
            public ValueItem File { get; set; }

            [XmlElement(ElementName = "Meta")]
            public ValueItem Meta { get; set; }

            [XmlElement(ElementName = "Sound")]
            public ValueItem Sound { get; set; }


            [XmlElement(ElementName = "BlendInTime")]
            public BlendInTime BlendInTime { get; set; }
            [XmlElement(ElementName = "SelectionWeight")]
            public SelectionWeight SelectionWeight { get; set; }

            [XmlElement(ElementName = "Unknown")]
            public int Unknown { get; set; }

            [XmlElement(ElementName = "WeaponBone")]
            public string WeaponBone { get; set; }

            [XmlAttribute(AttributeName = "Slot")]
            public string Slot { get; set; }
            [XmlAttribute, System.ComponentModel.DefaultValue(-1)]
            public int SlotId { get; set; } = -1;
            public int? RecordId { get; set; }
            public string Skeleton { get; set; }
            public string Comment { get; set; } = string.Empty;
            public bool Ignore { get; set; }
            public int ReservedWeaponFlags { get; set; }
        }

        [XmlRoot(ElementName = "Animation")]
        public class Animation
        {
            [XmlElement(ElementName = "AnimationFragmentEntry")]
            public List<AnimationEntry> AnimationFragmentEntry { get; set; }
            [XmlAttribute(AttributeName = "skeleton")]
            public string Skeleton { get; set; }
            public List<string> Skeletons { get; set; } = new();
        }
}
