using System.Windows.Controls;
using System.Windows.Input;

namespace Editors.AnimationMeta.SuperView.Inspection
{
    public partial class MetaDataProblemListView : UserControl
    {
        public MetaDataProblemListView()
        {
            InitializeComponent();
        }

        private void OnProblemItemMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem { IsSelected: true } &&
                DataContext is MetaDataProblemListViewModel viewModel)
                viewModel.NavigateSelectedProblemCommand.Execute(null);
        }
    }
}
