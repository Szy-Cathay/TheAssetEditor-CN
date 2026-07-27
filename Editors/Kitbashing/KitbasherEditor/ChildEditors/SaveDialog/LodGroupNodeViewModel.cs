using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services.SceneSaving;
using Shared.Core.Misc;

namespace KitbasherEditor.ViewModels.SaveDialog
{
    public class LodGroupNodeViewModel : NotifyPropertyChangedImpl
    {
        private readonly Rmv2LodNode? _node;
        private readonly LodGenerationSettings _lodSettings;

        public NotifyAttr<int> PolygonCount { get; set; } = new NotifyAttr<int>(0);
        public NotifyAttr<int> TextureCount { get; set; } = new NotifyAttr<int>(0);
        public NotifyAttr<int> MeshCount { get; set; } = new NotifyAttr<int>(0);
        public int LodIndex { get; private set; }

        public LodGroupNodeViewModel(
            Rmv2LodNode? node,
            int lodIndex,
            LodGenerationSettings lodSettings,
            bool onlySaveVisible)
        {
            _node = node;
            _lodSettings = lodSettings;
            LodIndex = lodIndex;

            if (_node != null)
            {
                PolygonCount.Value = _node.GetAllModels(onlySaveVisible).Sum(x => x.Geometry.VertexCount() / 3);
                TextureCount.Value = _node.GetAllModels(onlySaveVisible).SelectMany(x => x.RmvMaterial.GetAllTextures().Select(x => x.Path)).Distinct().Count();
                MeshCount.Value = _node.GetAllModels(onlySaveVisible).Count();
            }
        }

        public float CameraDistance
        {
            get => _lodSettings.CameraDistance;
            set
            {
                _lodSettings.CameraDistance = value;
                NotifyPropertyChanged();
            }
        }

        public byte QualityLvl
        {
            get => _lodSettings.QualityLvl;
            set
            {
                _lodSettings.QualityLvl = value;
                NotifyPropertyChanged();
            }
        }

        public float LodReductionFactor
        {
            get => _lodSettings.LodRectionFactor;
            set
            {
                _lodSettings.LodRectionFactor = value;
                NotifyPropertyChanged();
            }
        }

    
        public bool OptimizeLod_Alpha
        {
            get => _lodSettings.OptimizeAlpha;
            set
            {
                _lodSettings.OptimizeAlpha = value;
                NotifyPropertyChanged();
            }
        }

        public bool OptimizeLod_Vertex 
        {
            get => _lodSettings.OptimizeVertex;
            set
            {
                _lodSettings.OptimizeVertex = value;
                NotifyPropertyChanged();
            }
        }
    }
}
