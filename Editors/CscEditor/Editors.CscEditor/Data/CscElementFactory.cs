using System;
using System.Collections.Generic;
using System.Linq;
using Shared.GameFormats.Esf;

namespace Editors.CscEditor.Data
{
    /// <summary>
    /// Builds new elements (domain object + backing ESF records) matching the corpus-dominant
    /// v100 record shapes. Records with version > 15 must carry the HasNonOptimizedInfo flag -
    /// the packed 2-byte record header only has 4 bits for the version.
    /// </summary>
    public static class CscElementFactory
    {
        const EsfRecordFlags RecordV100Flags = EsfRecordFlags.IsRecordNode | EsfRecordFlags.HasNonOptimizedInfo;

        public static CscElement Create(CscScene scene, CscElementKind kind, string assetPath = "")
        {
            var element = new CscElement
            {
                Id = scene.NextFreeElementId(),
                Kind = kind,
                GroupLabel = DefaultGroupLabel(kind),
                AssetPath = assetPath,
                Begin = 0,
                End = scene.Duration,
                TimingMode = "duration",
                AnchorMode = "free",
                ElementVersion = 100,
                Position = CscChannelGroup.CreateStatic(0, 0, 0),
                Rotation = CscChannelGroup.CreateStatic(0, 0, 0),
                Scale = CscChannelGroup.CreateStatic(1),
                Weight = CscChannelGroup.CreateStatic(1),
            };

            // Points the cone downward by default (identity rotation aims it along the
            // horizontal forward axis, which is rarely what's wanted for a fresh light).
            if (kind == CscElementKind.SpotLight)
                element.Rotation = CscChannelGroup.CreateStatic(0, 0, -MathF.PI / 2);

            foreach (var group in DefaultTypeGroups(kind))
                element.TypeGroups.Add(group);

            element.ElementRecord = BuildElementRecord(element);
            element.Records.Add(element.ElementRecord);

            if (kind == CscElementKind.Sfx)
            {
                // New SFX uses the primary (start, stop) event pair; the optional second pair
                // remains empty until the user explicitly fills it.
                element.SfxHasSecondEventPair = true;

                // ELEMENT_PERIOD (speed/offset/unknown-flag) is an optional sibling record, not
                // something every SFX carries in the corpus (well under half of SFX/animation
                // bundles combined have one) - left absent by default rather than always created.
            }

            var typeRecord = BuildTypeRecord(element);
            if (typeRecord != null)
            {
                element.TypeRecord = typeRecord;
                element.Records.Add(typeRecord);
            }

            return element;
        }

        static string DefaultGroupLabel(CscElementKind kind) => kind switch
        {
            CscElementKind.Model => "model",
            CscElementKind.VariantModel => "model",
            CscElementKind.Vfx => "vfx",
            CscElementKind.Sfx => "sfx",
            CscElementKind.Animation => "animation",
            CscElementKind.Camera => "camera",
            CscElementKind.PointLight => "point_light",
            CscElementKind.SpotLight => "spot_light",
            CscElementKind.Prefab => "prefab",
            CscElementKind.RootRef => "root_ref",
            _ => "group",
        };

        static List<CscChannelGroup> DefaultTypeGroups(CscElementKind kind) => kind switch
        {
            // colour(R,G,B), intensity, range, scatter_diameter
            CscElementKind.PointLight =>
            [
                CscChannelGroup.CreateStatic(1, 1, 1),
                CscChannelGroup.CreateStatic(100),
                CscChannelGroup.CreateStatic(10),
                CscChannelGroup.CreateStatic(0.05f),
            ],
            // colour(R,G,B), intensity, length, inner_angle, outer_angle, scatter_diameter
            CscElementKind.SpotLight =>
            [
                CscChannelGroup.CreateStatic(1, 1, 1),
                CscChannelGroup.CreateStatic(10000),
                CscChannelGroup.CreateStatic(30),
                CscChannelGroup.CreateStatic(0),
                CscChannelGroup.CreateStatic(MathF.PI / 6),
                CscChannelGroup.CreateStatic(0.05f),
            ],
            // fov(deg), roll(rad), near, far, focal_depth/width, falloff near/far, kernal fg/bg,
            // pivot distance + 2 unnamed trailing groups (the v7/v100 shape has 13 groups) - see
            // CscElement.CameraGroupIndex for the confirmed order (applies to every version).
            CscElementKind.Camera =>
            [
                CscChannelGroup.CreateStatic(45),
                CscChannelGroup.CreateStatic(0),
                CscChannelGroup.CreateStatic(0.01f),
                CscChannelGroup.CreateStatic(10000),
                CscChannelGroup.CreateStatic(0),
                CscChannelGroup.CreateStatic(0),
                CscChannelGroup.CreateStatic(0),
                CscChannelGroup.CreateStatic(0),
                CscChannelGroup.CreateStatic(0),
                CscChannelGroup.CreateStatic(0),
                CscChannelGroup.CreateStatic(0),
                CscChannelGroup.CreateStatic(0),
                CscChannelGroup.CreateStatic(0),
            ],
            // game_time_multiplier, scene_time_multiplier - both default to 1 (neutral speed).
            CscElementKind.RootRef =>
            [
                CscChannelGroup.CreateStatic(1),
                CscChannelGroup.CreateStatic(1),
            ],
            _ => [],
        };

        static EsfNode BuildElementRecord(CscElement element)
        {
            var fields = new List<EsfNode>
            {
                EsfNode.Leaf(EsfNodeKind.U32, (uint)element.Id, optimized: true),
                EsfNode.Leaf(EsfNodeKind.Ascii, element.HeaderString),
                EsfNode.Leaf(EsfNodeKind.F32, element.Begin, optimized: true),
                EsfNode.Leaf(EsfNodeKind.F32, element.End),
                EsfNode.Leaf(EsfNodeKind.Ascii, element.TimingMode),
                EsfNode.Leaf(EsfNodeKind.Ascii, element.AnchorMode),
            };

            foreach (var group in element.ElementGroups())
                CscSceneWriter.AppendChannelGroup(fields, group);

            fields.Add(EsfNode.Leaf(EsfNodeKind.Coord3d, new Coord3d(0, 0, 0)));
            fields.Add(EsfNode.Leaf(EsfNodeKind.Coord3d, new Coord3d(0, 0, 0)));
            fields.Add(EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true));
            element.ElementTrailingFields = [fields[^1]];

            return EsfNode.NewRecord("ELEMENT", 100, RecordV100Flags, [fields]);
        }

        static EsfNode? BuildTypeRecord(CscElement element)
        {
            switch (element.Kind)
            {
                case CscElementKind.Model:
                {
                    // Ascii(path), Ascii(faction override), U32(colour-triplet count = 3),
                    // 9 x F32 player colour (white), Bool, Bool, Bool.
                    var fields = new List<EsfNode>
                    {
                        EsfNode.Leaf(EsfNodeKind.Ascii, element.AssetPath),
                        EsfNode.Leaf(EsfNodeKind.Ascii, ""),
                        EsfNode.Leaf(EsfNodeKind.U32, 3u, optimized: true),
                    };
                    for (var i = 0; i < 9; i++)
                        fields.Add(EsfNode.Leaf(EsfNodeKind.F32, 1f));
                    fields.Add(EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true));
                    fields.Add(EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true));
                    fields.Add(EsfNode.Leaf(EsfNodeKind.Bool, true, optimized: true));
                    return EsfNode.NewRecord("MODEL_ELEMENT", 100, RecordV100Flags, [fields]);
                }

                case CscElementKind.VariantModel:
                {
                    // Shares MODEL_ELEMENT's confirmed prefix (path, faction override, colour-triplet
                    // count = 3, 9 x F32 player colour, Bool, Bool, Bool), followed by a variable-length
                    // list of named material/mesh-part overrides (corpus-observed, not decoded/edited by
                    // this editor) terminated by a trailing Bool - empty here (no overrides).
                    var fields = new List<EsfNode>
                    {
                        EsfNode.Leaf(EsfNodeKind.Ascii, element.AssetPath),
                        EsfNode.Leaf(EsfNodeKind.Ascii, ""),
                        EsfNode.Leaf(EsfNodeKind.U32, 3u, optimized: true),
                    };
                    for (var i = 0; i < 9; i++)
                        fields.Add(EsfNode.Leaf(EsfNodeKind.F32, 1f));
                    fields.Add(EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true));
                    fields.Add(EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true));
                    fields.Add(EsfNode.Leaf(EsfNodeKind.Bool, true, optimized: true));
                    fields.Add(EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true));
                    return EsfNode.NewRecord("VARIANT_MODEL_ELEMENT", 100, RecordV100Flags, [fields]);
                }

                case CscElementKind.Animation:
                    // Most-common real v100 shape in the corpus (904/1255
                    // instances): Ascii(path), Bool, Bool(true), Bool, Ascii("linear"), Ascii,
                    // Unknown23, Ascii, U32(0), U32(repeat count = 0, no extra Ascii names), then
                    // Bool, Bool(false), Ascii("spherical_linear"), Bool(true), U8(255), U8(0),
                    // U8(255). Only some of these positions are corpus-confirmed constants (the
                    // ones with a literal default here); the rest vary per instance and are given
                    // a plausible default rather than a verified one.
                    return EsfNode.NewRecord("ANIMATION_ELEMENT", 100, RecordV100Flags,
                    [[
                        EsfNode.Leaf(EsfNodeKind.Ascii, element.AssetPath),
                        EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true),
                        EsfNode.Leaf(EsfNodeKind.Bool, true, optimized: true),
                        EsfNode.Leaf(EsfNodeKind.Bool, true, optimized: true),
                        EsfNode.Leaf(EsfNodeKind.Ascii, "linear"),
                        EsfNode.Leaf(EsfNodeKind.Ascii, ""),
                        EsfNode.Leaf(EsfNodeKind.Unknown23, (byte)0),
                        EsfNode.Leaf(EsfNodeKind.Ascii, ""),
                        EsfNode.Leaf(EsfNodeKind.U32, 0u, optimized: true),
                        EsfNode.Leaf(EsfNodeKind.U32, 0u, optimized: true),
                        EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true),
                        EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true),
                        EsfNode.Leaf(EsfNodeKind.Ascii, "spherical_linear"),
                        EsfNode.Leaf(EsfNodeKind.Bool, true, optimized: true),
                        EsfNode.Leaf(EsfNodeKind.U8, (byte)255),
                        EsfNode.Leaf(EsfNodeKind.U8, (byte)0),
                        EsfNode.Leaf(EsfNodeKind.U8, (byte)255),
                    ]]);

                case CscElementKind.Vfx:
                    return EsfNode.NewRecord("VFX_ELEMENT", 100, RecordV100Flags,
                    [[
                        EsfNode.Leaf(EsfNodeKind.Ascii, element.AssetPath),
                        EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true),
                        EsfNode.Leaf(EsfNodeKind.Ascii, "default"),
                    ]]);

                case CscElementKind.Sfx:
                    // Two (start, stop) Wwise event pairs + flags/parameters + trailing
                    // U32(rtpc curve count = 0).
                    return EsfNode.NewRecord("SFX_ELEMENT", 100, RecordV100Flags,
                    [[
                        EsfNode.Leaf(EsfNodeKind.Ascii, element.AssetPath),
                        EsfNode.Leaf(EsfNodeKind.Ascii, element.SfxStopEvent),
                        EsfNode.Leaf(EsfNodeKind.Ascii, element.SfxEvent2Start),
                        EsfNode.Leaf(EsfNodeKind.Ascii, element.SfxEvent2Stop),
                        EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true),
                        EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true),
                        EsfNode.Leaf(EsfNodeKind.F32, 0f, optimized: true),
                        EsfNode.Leaf(EsfNodeKind.U8, (byte)0),
                        EsfNode.Leaf(EsfNodeKind.F32, 0f, optimized: true),
                        EsfNode.Leaf(EsfNodeKind.F32, 0f, optimized: true),
                        EsfNode.Leaf(EsfNodeKind.F32, 0f, optimized: true),
                        EsfNode.Leaf(EsfNodeKind.F32, 0f, optimized: true),
                        EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true),
                        EsfNode.Leaf(EsfNodeKind.U32, 0u, optimized: true),
                    ]]);

                case CscElementKind.PointLight:
                {
                    // Group(3) colour, Group(1) intensity, Group(1) range, Bool, Group(1) scatter.
                    var fields = new List<EsfNode>();
                    CscSceneWriter.AppendChannelGroup(fields, element.TypeGroups[0]);
                    CscSceneWriter.AppendChannelGroup(fields, element.TypeGroups[1]);
                    CscSceneWriter.AppendChannelGroup(fields, element.TypeGroups[2]);
                    fields.Add(EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true));
                    CscSceneWriter.AppendChannelGroup(fields, element.TypeGroups[3]);
                    return EsfNode.NewRecord("POINT_LIGHT_ELEMENT", 100, RecordV100Flags, [fields]);
                }

                case CscElementKind.SpotLight:
                {
                    // Group(3) colour, Group(1) intensity/length/inner/outer, F32(falloff),
                    // Ascii(gobo), F32, Bool, Group(1) scatter.
                    var fields = new List<EsfNode>();
                    for (var i = 0; i < 5; i++)
                        CscSceneWriter.AppendChannelGroup(fields, element.TypeGroups[i]);
                    fields.Add(EsfNode.Leaf(EsfNodeKind.F32, 1f));
                    fields.Add(EsfNode.Leaf(EsfNodeKind.Ascii, ""));
                    fields.Add(EsfNode.Leaf(EsfNodeKind.F32, 0f, optimized: true));
                    fields.Add(EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true));
                    CscSceneWriter.AppendChannelGroup(fields, element.TypeGroups[5]);
                    return EsfNode.NewRecord("SPOT_LIGHT_ELEMENT", 100, RecordV100Flags, [fields]);
                }

                case CscElementKind.Camera:
                {
                    // Group(fov), Group(roll), Bool(unidentified), Group(near), Group(far), then
                    // the remaining 9 groups (always default/inert) - confirmed order, every version.
                    var fields = new List<EsfNode>();
                    CscSceneWriter.AppendChannelGroup(fields, element.TypeGroups[0]);
                    CscSceneWriter.AppendChannelGroup(fields, element.TypeGroups[1]);
                    var unknownFlagNode = EsfNode.Leaf(EsfNodeKind.Bool, false, optimized: true);
                    fields.Add(unknownFlagNode);
                    element.CameraUnknownFlagNode = unknownFlagNode;
                    element.CameraUnknownFlag = false;
                    for (var i = 2; i < element.TypeGroups.Count; i++)
                        CscSceneWriter.AppendChannelGroup(fields, element.TypeGroups[i]);
                    return EsfNode.NewRecord("CAMERA_ELEMENT", 100, RecordV100Flags, [fields]);
                }

                case CscElementKind.Prefab:
                    return EsfNode.NewRecord("PREFAB_ELEMENT", 0, EsfRecordFlags.IsRecordNode,
                        [[EsfNode.Leaf(EsfNodeKind.Ascii, element.AssetPath)]]);

                case CscElementKind.RootRef:
                {
                    var fields = new List<EsfNode> { EsfNode.Leaf(EsfNodeKind.Ascii, element.AssetPath) };
                    CscSceneWriter.AppendChannelGroup(fields, element.TypeGroups[0]);
                    CscSceneWriter.AppendChannelGroup(fields, element.TypeGroups[1]);
                    return EsfNode.NewRecord("ROOT_REF_ELEMENT", 100, RecordV100Flags, [fields]);
                }

                default:
                    return null; // Locator: an ELEMENT with no type record.
            }
        }
    }
}
