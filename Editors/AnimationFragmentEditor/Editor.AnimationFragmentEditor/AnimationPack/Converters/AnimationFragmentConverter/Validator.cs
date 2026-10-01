using GameWorld.Core.Services;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.AnimationPack;
using Shared.Ui.Editors.TextEditor;

namespace Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationFragmentConverter
{
    public static class Validator
    {
        public static ITextConverter.SaveError Validate(ISkeletonAnimationLookUpHelper helper, Animation animation, string text, IPackFileService pfs, string filepath, GameTypeEnum game = GameTypeEnum.Unknown)
        {
            var report = Check(helper, animation, pfs, game);
            return report.Errors.Any(e => e.IsError) ? new ITextConverter.SaveError { Text = string.Join(Environment.NewLine, report.Errors.Where(e => e.IsError).Select(e => e.Description)) } : null;
        }

        public static ErrorList Check(ISkeletonAnimationLookUpHelper helper, Animation animation, IPackFileService pfs, GameTypeEnum game)
        {
            var report = new ErrorList();
            if (string.IsNullOrWhiteSpace(animation.Skeleton))
                report.Error(Text("AnimPack.Table.Skeleton"), Text("AnimPack.Validation.SkeletonRequired"));
            else if (helper.GetSkeletonFileFromName(animation.Skeleton) == null)
                report.Warning(Text("AnimPack.Table.Skeleton"), Format("AnimPack.Validation.ResourceUnavailable", animation.Skeleton));
            if (animation.AnimationFragmentEntry == null)
            { report.Error(Text("AnimPack.Validation.Format"), Text("AnimPack.Validation.AnimationsRequired")); return report; }
            foreach (var item in animation.AnimationFragmentEntry)
            {
                var label = item.Slot ?? Text("AnimPack.Table.Slot");
                var slot = game == GameTypeEnum.Troy ? AnimationSlotTypeHelperTroy.GetfromValue(item.Slot ?? "") : DefaultAnimationSlotTypeHelper.GetfromValue(item.Slot ?? "");
                if (slot == null && item.SlotId < 0) report.Error(label, Text("AnimPack.Validation.SlotInvalid"));
                if (item.File == null || item.Meta == null || item.Sound == null || item.BlendInTime == null || item.SelectionWeight == null)
                { report.Error(label, Text("AnimPack.Validation.DataRequired")); continue; }
                if (!float.IsFinite(item.BlendInTime.Value) || item.BlendInTime.Value < 0 || !float.IsFinite(item.SelectionWeight.Value) || item.SelectionWeight.Value < 0)
                    report.Error(label, Text("AnimPack.Validation.ParametersInvalid"));
                if (!ValueConverterHelper.ValidateBoolArray(item.WeaponBone))
                    report.Error(label, Text("AnimPack.Validation.WeaponFlagsInvalid"));
                if (string.IsNullOrWhiteSpace(item.File.Value))
                    report.Error(label, Text("AnimPack.Validation.AnimationRequired"));
                foreach (var path in new[] { item.File.Value, item.Meta.Value, item.Sound.Value })
                    if (!string.IsNullOrWhiteSpace(path) && pfs.FindFile(path) == null)
                        report.Warning(label, Format("AnimPack.Validation.ResourceUnavailable", path));
            }
            return report;
        }
        private static string Text(string key) => LocalizationManager.Instance?.Get(key) ?? key;
        private static string Format(string key, params object[] values) => LocalizationManager.Instance?.GetFormat(key, values) ?? key;
    }
}
