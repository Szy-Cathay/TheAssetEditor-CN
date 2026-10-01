using System.IO;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimationFragmentEditor.CampaignAnimBin;
using Editors.AnimationFragmentEditor.AnimationFilePreviewEditor;
using Editors.Shared.Core.Common.BaseControl;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Shared.Core.Services;
using Shared.GameFormats.DB;
using Shared.Core.Settings;

namespace CommonControls.Editors.AnimationPack
{
    public partial class AnimPackViewModel
    {
        private string[]? _resourcePaths;
        public Func<bool>? CommitPendingEdits { get; set; }
        private bool _isCommittingInputs;
        private bool CommitInputs()
        {
            if (_isCommittingInputs) return true;
            _isCommittingInputs = true;
            try
            {
                if (CommitPendingEdits?.Invoke() != false) return true;
                _standardDialogs.ShowDialogBox(GetLocalizedText("AnimPack.Campaign.InvalidField"), GetLocalizedText("Msg.GeneralError"));
                return false;
            }
            finally { _isCommittingInputs = false; }
        }
        private bool _onlyEditableFiles;
        private AnimationSetFormatChoice _newFormat = NewFormats[0];
        private string _newSkeletonName = "humanoid01";
        public static List<AnimationSetFormatChoice> NewFormats { get; } = [new("Wh3"), new("ThreeKingdom"), new("Fragment"), new("Campaign")];
        public AnimationSetFormatChoice NewFormat
        {
            get => _newFormat;
            set => SetAndNotifyWhenChanged(ref _newFormat, value, _ =>
            {
                NewTemplate = null;
                NotifyPropertyChanged(nameof(NewTemplate));
                NotifyPropertyChanged(nameof(TemplateFiles));
            });
        }
        public string NewSkeletonName { get => _newSkeletonName; set => SetAndNotifyWhenChanged(ref _newSkeletonName, value); }
        public List<string> AvailableSkeletons => _skeletonAnimationLookUpHelper.GetAllSkeletonFileNames()?.ToList() ?? [];
        public List<IAnimationPackFile> TemplateFiles => AnimationPackItems.PossibleValues.Where(f => NewFormat.Id switch
        {
            "Campaign" => f is CampaignAnimationPackFile,
            "Fragment" => f is AnimationFragmentFile,
            "Wh3" => f is AnimationBinWh3 bin && bin.TableVersion == 4,
            _ => f is AnimationBinWh3 bin && bin.TableVersion == 2,
        }).ToList();
        public IAnimationPackFile? NewTemplate { get; set; }
        public bool OnlyEditableFiles { get => _onlyEditableFiles; set => SetAndNotifyWhenChanged(ref _onlyEditableFiles, value, _ => RefreshFileFilter()); }
        private static bool IsEditable(IAnimationPackFile file) => file is AnimationBinWh3 or AnimationFragmentFile or AnimationBin or CampaignAnimationPackFile;
        private string[] ResourcePaths => _resourcePaths ??= (_pfs.GetAllPackfileContainers() ?? []).SelectMany(c => c.FileList.Keys)
            .Concat(AnimationPackItems.PossibleValues.Select(f => f.FileName)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        public bool ValidateNewPath(string proposed, IAnimationPackFile? existing, out string normalized)
        {
            normalized = string.Empty;
            try
            {
                normalized = FolderProjectPathPolicy.NormalizeRelativePath(proposed).Replace('\\', '/').ToLowerInvariant();
                var normalizedPath = normalized;
                if (AnimationPackItems.PossibleValues.Any(f => f != existing && string.Equals(f.FileName.Replace('\\', '/'), normalizedPath, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException(GetLocalizedText("AnimPack.DuplicateName"));
                if (existing != null && !string.Equals(Path.GetExtension(existing.FileName), Path.GetExtension(normalized), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(GetLocalizedText("AnimPack.ExtensionChangeInvalid"));
                if (existing != null && !HasCompatiblePath(existing, normalized))
                    throw new InvalidDataException(GetLocalizedText("AnimPack.FormatPathInvalid"));
                return true;
            }
            catch (Exception e) when (e is ArgumentException or InvalidDataException or IOException)
            {
                _standardDialogs.ShowDialogBox(GetLocalizedText("AnimPack.InvalidName") + Environment.NewLine + e.Message, GetLocalizedText("Msg.GeneralError"));
                return false;
            }
        }

        public bool AddAnimationSet(IAnimationPackFile file)
        {
            if (IsStandaloneCampaign) return false;
            if (HasUnsavedChildChanges() && !SaveActiveFile()) return false;
            if (!ValidateNewPath(file.FileName, null, out var path)) return false;
            if (!HasCompatiblePath(file, path))
            { _standardDialogs.ShowDialogBox(GetLocalizedText("AnimPack.FormatPathInvalid"), GetLocalizedText("Msg.GeneralError")); return false; }
            file.FileName = path;
            file.IsChanged.Value = true;
            AnimationPackItems.PossibleValues.Add(file);
            AnimationPackItems.UpdatePossibleValues(AnimationPackItems.PossibleValues);
            _resourcePaths = null;
            NotifyPropertyChanged(nameof(TemplateFiles));
            FileFilterText = string.Empty;
            RefreshFileFilter();
            AnimationPackItems.SelectedItem = file;
            IsTableView = file is not AnimationBin;
            NotifySelectionState(); HasUnsavedChanges = true;
            return true;
        }

        public bool RenameAnimationSet(IAnimationPackFile file, string proposed)
        {
            proposed = SiblingPath(file.FileName, proposed);
            if (IsStandaloneCampaign || !ValidateNewPath(proposed, file, out var name)) return false;
            if (name == file.FileName) return true;
            if (HasUnsavedChildChanges() && !SaveActiveFile()) return false;
            var oldPath = file.FileName;
            var oldName = Path.GetFileNameWithoutExtension(oldPath.Replace('\\', '/'));
            var newName = Path.GetFileNameWithoutExtension(name);
            file.FileName = name;
            if (file is AnimationBinWh3 battle) battle.Name = newName;
            if (file is CampaignAnimationPackFile campaign) campaign.Data.Reference = newName;
            file.IsChanged.Value = true;
            foreach (var related in AnimationPackItems.PossibleValues.OfType<AnimationBinWh3>())
            {
                if (string.Equals(related.MountBin, oldName, StringComparison.OrdinalIgnoreCase)) { related.MountBin = newName; related.IsChanged.Value = true; }
                if (string.Equals(related.Unknown, oldName, StringComparison.OrdinalIgnoreCase)) { related.Unknown = newName; related.IsChanged.Value = true; }
            }
            _resourcePaths = null;
            OnItemSelected(file); RefreshFileFilter(); HasUnsavedChanges = true;
            return true;
        }

        private static string SiblingPath(string original, string proposed)
        {
            if (proposed.Contains('/') || proposed.Contains('\\')) return proposed;
            var path = original.Replace('\\', '/');
            var separator = path.LastIndexOf('/');
            return separator < 0 ? proposed : path[..(separator + 1)] + proposed;
        }

        private static bool HasCompatiblePath(IAnimationPackFile file, string path) => file switch
        {
            AnimationBinWh3 => path.StartsWith("animations/database/battle/bin/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase),
            CampaignAnimationPackFile => path.StartsWith("animations/campaign/database/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase),
            AnimationFragmentFile => path.EndsWith(".frg", StringComparison.OrdinalIgnoreCase),
            _ => true,
        };

        private bool CanCreateFile() => !IsStandaloneCampaign;
        [RelayCommand(CanExecute = nameof(CanCreateFile))]
        private void CreateAnimationSet()
        {
            var result = _standardDialogs.ShowTextInputDialog(GetLocalizedText("AnimPack.CreateFileTitle"), "");
            if (!result.Result) return;
            var input = result.Text.Trim();
            if (string.IsNullOrWhiteSpace(input) || input.Contains('/') || input.Contains('\\'))
            { _standardDialogs.ShowDialogBox(GetLocalizedText("AnimPack.InvalidName"), GetLocalizedText("Msg.GeneralError")); return; }
            if (HasUnsavedChildChanges() && !SaveActiveFile()) return;
            var name = Path.GetFileNameWithoutExtension(input);
            var extension = NewFormat.Id == "Fragment" ? ".frg" : ".bin";
            var directory = NewFormat.Id == "Campaign" ? "animations/campaign/database/bin/" : NewFormat.Id == "Fragment" ? "animations/animation_tables/" : "animations/database/battle/bin/";
            var path = directory + name + extension;
            if (!ValidateNewPath(path, null, out var normalized)) return;
            IAnimationPackFile created;
            if (NewFormat.Id == "Campaign")
            {
                var campaign = NewTemplate is CampaignAnimationPackFile template ? CampaignAnimationBinLoader.Load(new Shared.ByteParsing.ByteChunk(template.ToByteArray()))
                    : new CampaignAnimationBin { Version = 3, Status = [new() { Name = "global" }, new() { Name = "status_normal" }] };
                campaign.Reference = name; campaign.SkeletonName = NewSkeletonName;
                created = new CampaignAnimationPackFile(normalized, CampaignAnimationBinLoader.Write(campaign, name));
            }
            else if (NewFormat.Id == "Fragment")
            {
                var fragment = NewTemplate is AnimationFragmentFile template ? new AnimationFragmentFile(normalized, template.ToByteArray(), _appSettings.CurrentSettings.CurrentGame)
                    : new AnimationFragmentFile(normalized, null!, _appSettings.CurrentSettings.CurrentGame);
                var previous = fragment.Skeletons.Values.FirstOrDefault();
                var skeletons = fragment.Skeletons.Values.ToArray();
                fragment.Skeletons = new StringArrayTable(skeletons.Length == 0
                    ? [NewSkeletonName, NewSkeletonName]
                    : skeletons.Select(s => s == previous ? NewSkeletonName : s).ToArray());
                foreach (var entry in fragment.Fragments) if (entry.Skeleton == previous) entry.Skeleton = NewSkeletonName;
                created = fragment;
            }
            else
            {
                var version = NewFormat.Id == "Wh3" ? 4u : 2u;
                var bin = NewTemplate is AnimationBinWh3 template && template.TableVersion == version ? new AnimationBinWh3(normalized, template.ToByteArray()) : new AnimationBinWh3(normalized);
                bin.TableVersion = version; bin.TableSubVersion = version == 4 ? 3u : 0u;
                bin.Name = name; bin.SkeletonName = NewSkeletonName;
                if (version == 4 && string.IsNullOrEmpty(bin.LocomotionGraph)) bin.LocomotionGraph = "animations/locomotion_graphs/entity_locomotion_graph.xml";
                created = bin;
            }
            AddAnimationSet(created);
        }

        [RelayCommand(CanExecute = nameof(CanCreateFile))]
        private void CreateCampaignFile()
        {
            var result = _standardDialogs.ShowTextInputDialog(GetLocalizedText("AnimPack.Campaign.CreateTitle"), "");
            if (!result.Result || string.IsNullOrWhiteSpace(result.Text)) return;
            var input = result.Text.Trim();
            if (input.Contains('/') || input.Contains('\\'))
            { _standardDialogs.ShowDialogBox(GetLocalizedText("AnimPack.InvalidName"), GetLocalizedText("Msg.GeneralError")); return; }
            var name = Path.GetFileNameWithoutExtension(input);
            var model = new CampaignAnimationBin
            {
                Version = 3, Reference = name, SkeletonName = TableEditorVM?.SkeletonName ?? CampaignEditorVM?.SkeletonName ?? "humanoid01",
                Status = [new() { Name = "global" }, new() { Name = "status_normal" }],
            };
            AddAnimationSet(new CampaignAnimationPackFile($"animations/campaign/database/bin/{name}.bin", CampaignAnimationBinLoader.Write(model, name)));
        }

        private bool CanClone() => AnimationPackItems.SelectedItem != null && IsEditable(AnimationPackItems.SelectedItem) && !IsStandaloneCampaign;
        [RelayCommand(CanExecute = nameof(CanClone))]
        private void CloneSelectedFile()
        {
            if (HasUnsavedChildChanges() && !SaveActiveFile()) return;
            var selected = AnimationPackItems.SelectedItem!;
            var result = _standardDialogs.ShowTextInputDialog(GetLocalizedText("AnimPack.CloneTitle"), selected.FileName);
            if (!result.Result || !ValidateNewPath(SiblingPath(selected.FileName, result.Text), null, out var path)) return;
            IAnimationPackFile copy = selected switch
            {
                AnimationBinWh3 => new AnimationBinWh3(path, selected.ToByteArray()),
                AnimationFragmentFile => new AnimationFragmentFile(path, selected.ToByteArray(), _appSettings.CurrentSettings.CurrentGame),
                CampaignAnimationPackFile => new CampaignAnimationPackFile(path, selected.ToByteArray()),
                _ => new AnimationBin(path, selected.ToByteArray()),
            };
            var name = Path.GetFileNameWithoutExtension(path);
            if (copy is AnimationBinWh3 bin) bin.Name = name;
            if (copy is CampaignAnimationPackFile campaign) campaign.Data.Reference = name;
            AddAnimationSet(copy);
        }

        private PackFile? FindResource(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var file = _pfs.FindFile(path);
            if (file != null) return file;
            var embedded = AnimationPackItems.PossibleValues.FirstOrDefault(f => string.Equals(f.FileName.Replace('\\', '/'), path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
            return embedded == null ? null : PackFile.CreateFromBytes(path, embedded.ToByteArray());
        }

        private void OpenReferencedResource(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var embedded = AnimationPackItems.PossibleValues.FirstOrDefault(f => string.Equals(f.FileName.Replace('\\', '/'), path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
            if (embedded != null && IsEditable(embedded)) { AnimationPackItems.SelectedItem = embedded; return; }
            var file = _pfs.FindFile(path);
            if (file != null) { _uiCommandFactory.Create<OpenEditorCommand>().Execute(file); return; }
            var resource = FindResource(path);
            if (resource == null) { _standardDialogs.ShowDialogBox(Shared.Core.Services.LocalizationManager.Instance.GetFormat("AnimPack.Validation.ResourceUnavailable", path), GetLocalizedText("AnimPack.ResourceTitle")); return; }
            try
            {
                var text = path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase) ? new AnimFileToTextConverter().GetText(resource.DataSource.ReadData())
                    : path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ? Newtonsoft.Json.JsonConvert.SerializeObject(_metaDataFileParser.ParseFile(resource), Newtonsoft.Json.Formatting.Indented)
                    : System.Text.Encoding.UTF8.GetString(resource.DataSource.ReadData());
                var preview = new ResourcePreviewWindow(path, text);
                preview.Show();
            }
            catch (Exception e) { _standardDialogs.ShowExceptionWindow(e, GetLocalizedText("AnimPack.ResourceTitle")); }
        }

        private void PreviewAnimation(string animationPath, string skeletonName, string metaPath, string soundPath)
        {
            if (!CommitInputs()) return;
            if (TableEditorVM?.SelectedRow is { } battleRow)
            {
                animationPath = battleRow.AnimationFile;
                skeletonName = TableEditorVM.IsWh3 ? TableEditorVM.SkeletonName
                    : string.IsNullOrEmpty(battleRow.FragmentSkeleton) ? TableEditorVM.Skeleton : battleRow.FragmentSkeleton;
                metaPath = battleRow.MetaFile;
            }
            else if (CampaignEditorVM?.SelectedRow is { } campaignRow)
            { animationPath = campaignRow.Animation; skeletonName = CampaignEditorVM.SkeletonName; metaPath = campaignRow.Meta; }
            var animation = FindResource(animationPath);
            var skeleton = FindResource($"animations/skeletons/{skeletonName}.anim");
            if (animation == null || skeleton == null) { _standardDialogs.ShowDialogBox(GetLocalizedText("AnimPack.Preview.MissingResources"), GetLocalizedText("AnimPack.Preview.Title")); return; }
            try
            {
                var persistentPath = TableEditorVM?.Rows.FirstOrDefault(r => r.SlotName == "PERSISTENT_METADATA_ALIVE" && !string.IsNullOrWhiteSpace(r.MetaFile))?.MetaFile
                    ?? TableEditorVM?.Rows.FirstOrDefault(r => r.SlotName == "PERSISTENT_METADATA_FLYING" && !string.IsNullOrWhiteSpace(r.MetaFile))?.MetaFile
                    ?? CampaignEditorVM?.PersistentMetadataPath;
                var viewer = _uiCommandFactory.Create<OpenEditorCommand>().Execute(EditorEnums.SuperView_Editor);
                if (viewer is IAnimationPreviewEditor preview)
                    preview.PreviewAnimation(AnimationFile.Create(animation), AnimationFile.Create(skeleton), animationPath, FindResource(metaPath), FindResource(persistentPath ?? string.Empty));
            }
            catch (Exception e) { _standardDialogs.ShowExceptionWindow(e, GetLocalizedText("AnimPack.Preview.Title")); }
        }
    }

    public record AnimationSetFormatChoice(string Id)
    {
        public string Label => LocalizationManager.Instance?.Get($"AnimPack.NewFormat.{Id}") ?? Id;
    }
}
