using System.Collections;
using System.IO;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.GameFormats.AnimationPack;

namespace Editors.AnimationFragmentEditor.CampaignAnimBin
{
    public static class Validator
    {
        public static ErrorList Check(CampaignAnimationBin campaign, IPackFileService pfs, string path)
        {
            var report = new ErrorList();
            if (campaign.Version is not (2 or 3))
                report.Error(Text("AnimPack.Validation.Format"), Text("AnimPack.Campaign.VersionError"));
            var name = Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
            if (!string.Equals(campaign.Reference, name, StringComparison.OrdinalIgnoreCase))
                report.Error(Text("AnimPack.Campaign.Reference"), Format("AnimPack.Validation.NameMismatch", campaign.Reference, name));
            if (string.IsNullOrWhiteSpace(campaign.SkeletonName))
                report.Error(Text("AnimPack.Table.Skeleton"), Text("AnimPack.Validation.SkeletonRequired"));
            else
                CheckResource($"animations/skeletons/{campaign.SkeletonName}.anim", Text("AnimPack.Table.Skeleton"));
            if (campaign.Status == null || campaign.Status.Count == 0)
            {
                report.Error(Text("AnimPack.Campaign.State"), Text("AnimPack.Campaign.EmptyStates"));
                return report;
            }
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var state in campaign.Status)
            {
                if (string.IsNullOrWhiteSpace(state.Name) || !names.Add(state.Name))
                    report.Error(Text("AnimPack.Campaign.State"), Format("AnimPack.Campaign.InvalidState", state.Name));
                if (campaign.Version == 2 && state.Transitions?.Count > 0)
                    report.Error(state.Name, Text("AnimPack.Campaign.Version2Transitions"));
                foreach (var collection in typeof(CampaignAnimationBin.StatusItem).GetProperties())
                {
                    if (collection.GetValue(state) is not IEnumerable entries || collection.PropertyType == typeof(string))
                        continue;
                    bool globalCollection = collection.Name is "PersitantMetaData" or "Poses" or "Docks";
                    if ((state.Name == "global") != globalCollection && entries.Cast<object>().Any())
                        report.Error(state.Name, Text("AnimPack.Campaign.CategoryStateMismatch"));
                    foreach (var entry in entries)
                    {
                        foreach (var property in entry.GetType().GetProperties())
                        {
                            var value = property.GetValue(entry);
                            if (value is float number && (!float.IsFinite(number) || number < 0 && property.Name is "Weight" or "BlendTime" or "ModelScale"))
                                report.Error(state.Name, Format("AnimPack.Validation.NumberInvalid", property.Name));
                            if (value is string resource && (resource.Contains('/') || resource.Contains('\\')))
                                CheckResource(resource, state.Name);
                        }
                    }
                }
            }
            foreach (var state in campaign.Status)
                foreach (var transition in state.Transitions ?? [])
                    if (!string.IsNullOrWhiteSpace(transition.TransitionTo) && !names.Contains(transition.TransitionTo))
                        report.Warning(state.Name, Format("AnimPack.Campaign.TargetStateUnavailable", transition.TransitionTo));
            if (!names.Contains("status_normal"))
                report.Warning(Text("AnimPack.Campaign.State"), Text("AnimPack.Campaign.NoNormalState"));
            return report;

            void CheckResource(string resource, string label)
            {
                if (!string.IsNullOrWhiteSpace(resource) && resource != "global" && pfs.FindFile(resource) == null)
                    report.Warning(label, Format("AnimPack.Validation.ResourceUnavailable", resource));
            }
        }

        public static bool ValidateAnimationData(CampaignAnimationBin campaign, IPackFileService pfs, string path) =>
            !Check(campaign, pfs, path).Errors.Any(e => e.IsError);
        private static string Text(string key) => LocalizationManager.Instance?.Get(key) ?? key;
        private static string Format(string key, params object[] values) => LocalizationManager.Instance?.GetFormat(key, values) ?? key;
    }
}
