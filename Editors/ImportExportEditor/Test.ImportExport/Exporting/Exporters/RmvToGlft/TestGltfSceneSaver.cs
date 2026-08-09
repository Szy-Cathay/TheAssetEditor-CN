using Editors.ImportExport.Exporting.Exporters.RmvToGltf.Helpers;
using SharpGLTF.Schema2;

namespace Test.ImportExport.Exporting.Exporters.RmvToGlft
{
    public class TestGltfSceneSaver : IGltfSceneSaver
    {
        public bool Save(ModelRoot modelRoot, string fullSystemPath)
        {
            IsSaveCalled = true;
            FullSystemPath = fullSystemPath;
            ModelRoot = modelRoot;
            return true;
        }

        public bool IsSaveCalled { get; set; }
        public string? FullSystemPath { get; set; }
        public ModelRoot? ModelRoot { get; set; }
    }
}
