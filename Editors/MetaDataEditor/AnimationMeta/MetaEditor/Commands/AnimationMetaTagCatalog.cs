using Shared.Core.Settings;

namespace Editors.AnimationMeta.MetaEditor.Commands
{
    public static class AnimationMetaTagCatalog
    {
        private static readonly HashSet<string> s_warhammer3Types =
        [
            "ALLOWED_DELTA_SCALE_10", "ALPHA_10",
            "ANIMATED_PROP_10", "ANIMATED_PROP_11", "ANIMATED_PROP_13",
            "ANIMATED_PROP_14", "ANIMATED_PROP_15", "BEARING_10",
            "BLEND_OVERRIDE_11", "BLOOD_11", "BLOOD_12",
            "BOUNDING_VOLUME_OVERRIDE_10", "CAMERA_SHAKE_POS_10",
            "CAMERA_SHAKE_SCALE_10", "CANNOT_DISMEMBER_10",
            "CREW_LOCATION_2", "CREW_LOCATION_10", "DISABLE_FACIAL_10",
            "DISABLE_HEAD_TRACKING_10", "DISABLE_MODEL_10",
            "DISABLE_PERSISTENT_2", "DISABLE_PERSISTENT_10",
            "DISABLE_PERSISTENT_ID_11", "DISABLE_PERSISTENT_VFX_10",
            "DISTANCE_10", "DOCK_EQPT_BACK_10", "DOCK_EQPT_BACK_11",
            "DOCK_EQPT_LHAND_3", "DOCK_EQPT_LHAND_10",
            "DOCK_EQPT_LHAND_11", "DOCK_EQPT_LHAND_2_11",
            "DOCK_EQPT_LWAIST_3", "DOCK_EQPT_LWAIST_10",
            "DOCK_EQPT_LWAIST_11", "DOCK_EQPT_RHAND_3",
            "DOCK_EQPT_RHAND_10", "DOCK_EQPT_RHAND_11",
            "DOCK_EQPT_RHAND_2_11", "DOCK_EQPT_RWAIST_10",
            "DOCK_EQPT_RWAIST_11", "EFFECT_11", "EFFECT_12",
            "EJECT_ATTACHED_10", "FACE_POSE_10", "FIRE_POS_10",
            "FULL_BODY_10", "IGNORE_FOOT_SLIDING_10", "IMPACT_POS_2",
            "IMPACT_POS_10", "IMPACT_SPEED_10", "LHAND_POSE_2",
            "LHAND_POSE_10", "MAX_TARGET_SIZE_11", "MIN_TARGET_SIZE_11",
            "NOT_BUILDING_10", "PARENT_CONSTRAINT_10", "POSITION_10",
            "PROP_4", "PROP_10", "PROP_11", "PROP_13", "PROP_14",
            "PROP_15", "RESCALE_10", "RHAND_POSE_2", "RHAND_POSE_10",
            "RIDER_ANIMATION_REQUIRED_10", "RIDER_IDLE_SPEED_SCALE_10",
            "SC_HEIGHT_10", "SC_RADIUS_10", "SC_RATIO_10",
            "SHADER_PARAMETER_12", "SNIP_10", "SOUND_DEFEND_TYPE_11",
            "SOUND_TRIGGER_10", "SOUND_TRIGGER_11", "SPLASH_ATTACK_10",
            "SPLICE_5", "SPLICE_6", "SPLICE_10", "SPLICE_11",
            "SPLICE_12", "SPLICE_OVERRIDE_12", "SYNC_MARKER_10",
            "TARGET_POS_10", "TIME_10", "TRANSFORM_10",
            "TURRET_ATTACHMENT_14", "USE_BASE_METADATA_10",
            "WEAPON_HIP_3", "WEAPON_HIP_11", "WEAPON_LHAND_10",
            "WEAPON_LHAND_11", "WEAPON_ON_10", "WEAPON_ON_11",
            "WEAPON_RHAND_3", "WEAPON_RHAND_10", "WEAPON_RHAND_11",
            "WOUNDED_POSE_10",
        ];

        public static IEnumerable<string> FilterForGame(
            GameTypeEnum game,
            IEnumerable<string> definitions,
            Func<string, List<Type>>? definitionResolver = null)
        {
            if (game == GameTypeEnum.Warhammer3)
                return definitions.Where(s_warhammer3Types.Contains);

            if (definitionResolver == null)
                return definitions;

            return definitions.Where(definitionName =>
                definitionResolver(definitionName).Any(type =>
                    IsCompatibleVariant(game, type)));
        }

        private static bool IsCompatibleVariant(
            GameTypeEnum game,
            Type type)
        {
            var isTroyOnly = type.Name.Contains(
                "_Troy",
                StringComparison.Ordinal);
            var isThreeKingdomsOnly = type.Name.Contains(
                "_3K",
                StringComparison.Ordinal);
            return game switch
            {
                GameTypeEnum.Troy => !isThreeKingdomsOnly,
                GameTypeEnum.ThreeKingdoms => !isTroyOnly,
                _ => !isTroyOnly && !isThreeKingdomsOnly,
            };
        }

        public static string GetCategoryKey(string definitionName)
        {
            var tagName = definitionName[..definitionName.LastIndexOf('_')];
            if (tagName.StartsWith("PROP", StringComparison.Ordinal) ||
                tagName.StartsWith("ANIMATED_PROP", StringComparison.Ordinal) ||
                tagName is "CREW_LOCATION" or "RIDER_ATTACHMENT" or
                    "TURRET_ATTACHMENT")
            {
                return "MetaData.NewEntryCategory.Model";
            }

            if (tagName.Contains("POS", StringComparison.Ordinal) ||
                tagName.StartsWith("SPLASH_ATTACK", StringComparison.Ordinal) ||
                tagName.StartsWith("IMPACT_", StringComparison.Ordinal) ||
                tagName.Contains("TARGET_SIZE", StringComparison.Ordinal) ||
                tagName.Contains("DISMEMBER", StringComparison.Ordinal))
            {
                return "MetaData.NewEntryCategory.Combat";
            }

            if (tagName.StartsWith("EFFECT", StringComparison.Ordinal) ||
                tagName.StartsWith("BLOOD", StringComparison.Ordinal) ||
                tagName.StartsWith("CAMERA_SHAKE", StringComparison.Ordinal) ||
                tagName is "SHADER_PARAMETER" or "ALPHA")
            {
                return "MetaData.NewEntryCategory.Visual";
            }

            if (tagName.StartsWith("DOCK_EQPT", StringComparison.Ordinal) ||
                tagName.StartsWith("WEAPON_", StringComparison.Ordinal) ||
                tagName is "TRANSFORM" or "PARENT_CONSTRAINT")
            {
                return "MetaData.NewEntryCategory.Equipment";
            }

            if (tagName.StartsWith("SPLICE", StringComparison.Ordinal) ||
                tagName.Contains("POSE", StringComparison.Ordinal) ||
                tagName is "FULL_BODY" or "SNIP" or "RESCALE")
            {
                return "MetaData.NewEntryCategory.Animation";
            }

            if (tagName.StartsWith("SOUND", StringComparison.Ordinal) ||
                tagName.StartsWith("SYNC_", StringComparison.Ordinal))
            {
                return "MetaData.NewEntryCategory.Audio";
            }

            return "MetaData.NewEntryCategory.Other";
        }

        public static int GetCategoryOrder(string categoryKey) =>
            categoryKey switch
            {
                "MetaData.NewEntryCategory.Model" => 0,
                "MetaData.NewEntryCategory.Combat" => 1,
                "MetaData.NewEntryCategory.Visual" => 2,
                "MetaData.NewEntryCategory.Equipment" => 3,
                "MetaData.NewEntryCategory.Animation" => 4,
                "MetaData.NewEntryCategory.Audio" => 5,
                _ => 6,
            };
    }
}
