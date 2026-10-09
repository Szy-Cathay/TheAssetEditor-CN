using Microsoft.Extensions.DependencyInjection;
using Shared.Core.DependencyInjection;
using Shared.Core.ToolCreation;

namespace Editors.VfxEditor;

public class DependencyInjectionContainer : DependencyContainer
{
    public override void Register(IServiceCollection services)
    {
        services.AddTransient<VfxEditorView>();
        services.AddScoped<VfxEditorViewModel>();
        services.AddSingleton<ITerryPreviewService, TerryPreviewService>();
    }

    public override void RegisterTools(IEditorDatabase database)
    {
        EditorInfoBuilder.Create<VfxEditorViewModel, VfxEditorView>(EditorEnums.Vfx_Editor)
            .AddExtention(".xml", EditorPriorites.High)
            .ValidForFoldersContaining("vfx\\")
            .ValidForFoldersContaining("vfx/")
            .AddToToolbar("Vfx.Title")
            .Build(database);
    }
}
