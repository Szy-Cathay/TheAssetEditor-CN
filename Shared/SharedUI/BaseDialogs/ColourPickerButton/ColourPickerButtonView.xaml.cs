using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Shared.Ui.BaseDialogs.ColourPickerButton
{
    /// <summary>
    /// Interaction logic for ColourPickerButtonView.xaml
    /// </summary>
    public partial class ColourPickerButtonView : UserControl
    {
        private void PickerOpened(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
            {
                if (PickerPopup.IsOpen)
                    Keyboard.Focus((IInputElement)PickerPopup.Child);
            }));
        }

        private void PickerPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && e.OriginalSource is not ComboBox { IsDropDownOpen: true })
            {
                PickerButton.IsChecked = false;
                e.Handled = true;
            }
        }

        public ColourPickerButtonView()
        {
            InitializeComponent();
            Unloaded += (_, _) => PickerButton.IsChecked = false;
        }

    }
}
