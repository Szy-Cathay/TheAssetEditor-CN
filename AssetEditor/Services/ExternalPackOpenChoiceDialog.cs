using AssetEditor.Views.ExternalPack;

namespace AssetEditor.Services
{
    public sealed class ExternalPackOpenChoiceDialog :
        IExternalPackOpenChoiceDialog
    {
        public ExternalPackOpenChoice Choose(string packPath)
        {
            var window = new ExternalPackOpenChoiceWindow(packPath);
            return window.ShowDialog() == true
                ? window.Choice
                : ExternalPackOpenChoice.Cancelled;
        }
    }
}
