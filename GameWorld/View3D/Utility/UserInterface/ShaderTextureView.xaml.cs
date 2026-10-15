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

namespace GameWorld.Core.Utility.UserInterface
{
    /// <summary>
    /// Interaction logic for ShaderTextureView.xaml
    /// </summary>
    public partial class ShaderTextureView : UserControl
    {
        public static readonly DependencyProperty ShowRenderToggleProperty = DependencyProperty.Register(
            nameof(ShowRenderToggle), typeof(bool), typeof(ShaderTextureView), new PropertyMetadata(true));

        public bool ShowRenderToggle
        {
            get => (bool)GetValue(ShowRenderToggleProperty);
            set => SetValue(ShowRenderToggleProperty, value);
        }

        public ShaderTextureView()
        {
            InitializeComponent();
        }
    }
}
