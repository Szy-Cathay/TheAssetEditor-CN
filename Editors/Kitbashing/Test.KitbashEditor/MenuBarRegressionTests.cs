using System.Windows.Input;
using Editors.KitbasherEditor.UiCommands;
using GameWorld.Core.Components.Selection;
using KitbasherEditor.ViewModels.MenuBarViews;
using Moq;
using Shared.Core.Events;
using Shared.Ui.Common.MenuSystem;

namespace Test.KitbashEditor;

[TestFixture]
public class MenuBarRegressionTests
{
    [Test]
    public void TriggerCommand_DisabledActionDoesNotExecute()
    {
        var executionCount = 0;
        var action = new MenuAction
        {
            Hotkey = new Hotkey(Key.Delete, ModifierKeys.None),
            ActionTriggeredCallback = () => executionCount++
        };
        action.IsActionEnabled.Value = false;
        var handler = new ActionHotkeyHandler();
        handler.Register(action);

        var handled = handler.TriggerCommand(Key.Delete, ModifierKeys.None);

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.False);
            Assert.That(executionCount, Is.Zero);
        });
    }

    [Test]
    public void Register_DuplicateHotkeyIsRejected()
    {
        var handler = new ActionHotkeyHandler();
        handler.Register(new MenuAction
        {
            Hotkey = new Hotkey(Key.Add, ModifierKeys.None)
        });

        Assert.Throws<InvalidOperationException>(() =>
            handler.Register(new MenuAction
            {
                Hotkey = new Hotkey(Key.Add, ModifierKeys.None)
            }));
    }

    [Test]
    public void Commands_UseSafeUniqueShortcuts()
    {
        var save = new SaveCommand(null!, null!, null!, null!);
        var split = new DivideSubMeshCommand(
            null!,
            null!,
            null!,
            null!);
        var increase = new ScaleGizmoUpCommand(null!);
        var decrease = new ScaleGizmoDownCommand(null!);

        Assert.Multiple(() =>
        {
            Assert.That(save.HotKey?.Key, Is.EqualTo(Key.S));
            Assert.That(
                save.HotKey?.ModifierKeys,
                Is.EqualTo(ModifierKeys.Control));
            Assert.That(split.HotKey, Is.Null);
            Assert.That(increase.HotKey?.Key, Is.EqualTo(Key.Add));
            Assert.That(decrease.HotKey?.Key, Is.EqualTo(Key.Subtract));
        });
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    [TestCase(-1.0)]
    [TestCase(0.0)]
    [TestCase(0.0005)]
    public void FalloffDistance_InvalidValueUsesDocumentedMinimum(
        double invalidValue)
    {
        var eventHub = new Mock<IEventHub>();
        var selectionManager = new SelectionManager(
            eventHub.Object,
            null!,
            null!,
            null!);
        selectionManager.SetState(new ObjectSelectionState());
        var viewModel = new ProportionalEditingViewModel(
            selectionManager,
            eventHub.Object);

        viewModel.FalloffDistance = invalidValue;

        Assert.That(viewModel.FalloffDistance, Is.EqualTo(0.001));
    }

    [Test]
    public void HotkeyText_UsesConventionalModifierOrder()
    {
        var action = new MenuAction
        {
            Hotkey = new Hotkey(
                Key.Z,
                ModifierKeys.Control | ModifierKeys.Shift)
        };

        Assert.That(action.ToopTipText(), Is.EqualTo(" (Ctrl+Shift+Z)"));
    }
}
