using SharpGLTF.Schema2;

namespace Editors.ImportExport.Exporting.Exporters.RmvToGltf.Helpers;

public interface IGltfSceneSaver
{
    bool Save(ModelRoot modelRoot, string fullSystemPath);
}

public class GltfSceneSaver : IGltfSceneSaver
{
    public bool Save(ModelRoot modelRoot, string fullSystemPath)
    {
        modelRoot.SaveGLTF(fullSystemPath);
        return true;
    }
}
