namespace Shared.Ui.Common.MenuSystem
{
    public enum ActionEnabledRule
    {
        Always,
        OneObjectSelected,
        AtleastOneObjectSelected,
        TwoObjectesSelected,
        TwoOrMoreObjectsSelected,
        FaceSelected,
        VertexSelected,
        AnythingSelected,
        ObjectOrVertexSelected,
        ObjectOrFaceSelected,
        OneObjectOrFaceSelected,
        Custom
    }

    public enum ButtonVisibilityRule
    {
        Always,
        ObjectMode,
        FaceMode,
        VertexMode,
        EditMode,  // Visible in Vertex/Face/Edge edit modes, hidden in Object mode
    }
}
