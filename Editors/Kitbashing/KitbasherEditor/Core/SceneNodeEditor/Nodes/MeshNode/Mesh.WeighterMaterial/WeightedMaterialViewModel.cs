using System.Collections.ObjectModel;
using GameWorld.Core.SceneNodes;
using Shared.Core.Misc;
using Shared.Core.Services;
using Shared.GameFormats.RigidModel.MaterialHeaders;

namespace Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes.Rmv2
{
    public class WeightedMaterialViewModel : NotifyPropertyChangedImpl
    {
        WeightedMaterial _weightedMaterial = null!;

        public NotifyAttr<string> Filters { get; set; } = new NotifyAttr<string>();
        public NotifyAttr<int> MatrixIndex { get; set; } = new NotifyAttr<int>();
        public NotifyAttr<int> ParentMatrixIndex { get; set; } = new NotifyAttr<int>();
        public NotifyAttr<string> BinaryVertexFormat { get; set; } = new NotifyAttr<string>();
        public NotifyAttr<string> TransformInfo { get; set; } = new NotifyAttr<string>();
        public NotifyAttr<string> MaterialId { get; set; } = new NotifyAttr<string>();

        public ObservableCollection<(int Index, string Value)> StringParameters { get; set; } = [];
        public ObservableCollection<(int Index, float Value)> FloatParameters { get; set; } = [];
        public ObservableCollection<(int Index, int Value)> IntParameters { get; set; } = [];
        public ObservableCollection<string> TextureParameters { get; set; } = [];
        public ObservableCollection<string> AttachmentPointParameters { get; set; } = [];
        public ObservableCollection<string> VectorParameters { get; set; } = [];

        public void Initialize(Rmv2MeshNode node)
        {
            if (node.RmvMaterial is not WeightedMaterial castMaterial)
                throw new Exception($"Material is not WeightedMaterial - {node.RmvMaterial.GetType()}");
            _weightedMaterial = castMaterial;

            Filters.Value = _weightedMaterial.Filters;
            MatrixIndex.Value = _weightedMaterial.MatrixIndex;
            ParentMatrixIndex.Value = _weightedMaterial.ParentMatrixIndex;
            BinaryVertexFormat.Value = GetText(
                $"Kitbash.VertexFormat.{_weightedMaterial.BinaryVertexFormat}");
            TransformInfo.Value = GetFormat(
                "Kitbash.WeightedMaterial.TransformInfoValue",
                GetBooleanText(_weightedMaterial.OriginalTransform.IsIdentityPivot()),
                GetBooleanText(_weightedMaterial.OriginalTransform.IsIdentityMatrices()));
            MaterialId.Value = GetText(
                $"Kitbash.MaterialId.{_weightedMaterial.MaterialId}");
            
            StringParameters = new ObservableCollection<(int Index, string Value)>(_weightedMaterial.StringParams.Values);
            FloatParameters = new ObservableCollection<(int Index, float Value)>(_weightedMaterial.FloatParams.Values);
            IntParameters = new ObservableCollection<(int, int)>(_weightedMaterial.IntParams.Values);
            TextureParameters = new ObservableCollection<string>(
                _weightedMaterial.TexturesParams.Select(texture =>
                    $"{GetText($"Kitbash.TextureType.{texture.TexureType}")} - {texture.Path}"));
            AttachmentPointParameters = new ObservableCollection<string>(
                _weightedMaterial.AttachmentPointParams.Select(attachmentPoint =>
                    $"{attachmentPoint.BoneIndex} - {attachmentPoint.Name} - " +
                    GetFormat(
                        "Kitbash.WeightedMaterial.AttachmentIdentity",
                        GetBooleanText(attachmentPoint.Matrix.IsIdentity()))));
            VectorParameters = new ObservableCollection<string>(_weightedMaterial.Vec4Params.Values.Select(x => $"[{x.Value.X}] [{x.Value.Y}] [{x.Value.Z}] [{x.Value.W}]"));
        }

        private static string GetBooleanText(bool value) =>
            GetText(value ? "Kitbash.Sidebar.Yes" : "Kitbash.Sidebar.No");

        private static string GetText(string key) =>
            LocalizationManager.Instance?.Get(key) ?? key;

        private static string GetFormat(string key, params object[] args) =>
            LocalizationManager.Instance?.GetFormat(key, args) ?? key;
    }
}
