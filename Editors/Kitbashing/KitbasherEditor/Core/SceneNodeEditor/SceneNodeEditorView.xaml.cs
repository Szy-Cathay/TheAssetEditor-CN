using System.Windows.Controls;

namespace KitbasherEditor.Views
{
    /// <summary>
    /// Interaction logic for SceneItemEditorView.xaml
    /// </summary>
    public partial class SceneItemEditorView : UserControl
    {
        private Editors.KitbasherEditor.ViewModels.SceneNodeEditor.SceneNodeEditorViewModel? _numericEditor;
        public SceneItemEditorView()
        {
            InitializeComponent();
            InspectorViewState.SetState(this, new InspectorViewState());
            AddHandler(Shared.Ui.Common.Behaviors.TextBoxInputBehavior.NumericDragStartedEvent,
                new System.Windows.RoutedEventHandler((_, _) =>
                {
                    _numericEditor = DataContext as Editors.KitbasherEditor.ViewModels.SceneNodeEditor.SceneNodeEditorViewModel;
                    _numericEditor?.BeginNumericEdit();
                }));
            AddHandler(Shared.Ui.Common.Behaviors.TextBoxInputBehavior.NumericDragCompletedEvent,
                new EventHandler<Shared.Ui.Common.Behaviors.NumericDragCompletedEventArgs>((_, args) =>
                {
                    _numericEditor?.EndNumericEdit(args.Cancelled);
                    _numericEditor = null;
                }));
            Unloaded += (_, _) => { _numericEditor?.EndNumericEdit(true); _numericEditor = null; };
        }
    }
}
