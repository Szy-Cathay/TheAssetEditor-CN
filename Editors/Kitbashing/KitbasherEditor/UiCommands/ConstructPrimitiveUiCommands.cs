using Editors.KitbasherEditor.Commands;
using Editors.KitbasherEditor.Core.MenuBarViews;
using GameWorld.Core.Commands;
using Shared.Core.Services;
using Shared.Ui.Common.MenuSystem;

namespace Editors.KitbasherEditor.UiCommands
{
    public abstract class ConstructPrimitiveUiCommand : ITransientKitbasherUiCommand
    {
        private readonly CommandFactory _commandFactory;
        private readonly PrimitiveType _primitiveType;

        public string ToolTip { get; set; }
        public ActionEnabledRule EnabledRule => ActionEnabledRule.Always;
        public Hotkey? HotKey => null;

        private protected ConstructPrimitiveUiCommand(
            CommandFactory commandFactory,
            PrimitiveType primitiveType,
            string localizationKey)
        {
            _commandFactory = commandFactory;
            _primitiveType = primitiveType;
            ToolTip = LocalizationManager.Instance?.Get(localizationKey) ?? localizationKey;
        }

        public void Execute()
        {
            _commandFactory
                .Create<ConstructPrimitiveCommand>()
                .Configure(x => x.Configure(_primitiveType))
                .BuildAndExecute();
        }
    }

    public sealed class ConstructBoxUiCommand : ConstructPrimitiveUiCommand
    {
        public ConstructBoxUiCommand(CommandFactory commandFactory)
            : base(
                commandFactory,
                PrimitiveType.Box,
                "Kitbash.ToolTip.ConstructBoxUiCommand")
        {
        }
    }

    public sealed class ConstructPlaneUiCommand : ConstructPrimitiveUiCommand
    {
        public ConstructPlaneUiCommand(CommandFactory commandFactory)
            : base(
                commandFactory,
                PrimitiveType.Plane,
                "Kitbash.ToolTip.ConstructPlaneUiCommand")
        {
        }
    }

    public sealed class ConstructSphereUiCommand : ConstructPrimitiveUiCommand
    {
        public ConstructSphereUiCommand(CommandFactory commandFactory)
            : base(
                commandFactory,
                PrimitiveType.Sphere,
                "Kitbash.ToolTip.ConstructSphereUiCommand")
        {
        }
    }
}
