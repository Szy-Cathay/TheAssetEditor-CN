using Shared.Core.Misc;
using System.Windows;
using CommonControls;
using WindowHandling;

namespace AnimationEditor.MountAnimationCreator
{
    /// <summary>
    /// Interaction logic for BatchProcessOptions.xaml
    /// </summary>
    public partial class BatchProcessOptionsWindow : Window
    {
        public BatchProcessOptionsWindow()
        {
            InitializeComponent();
            DarkTitleBarHelper.Enable(this);
        }

        public static BatchProcessOptions ShowDialog(string fragmentName, string savePrefix)
        {
            var options = new BatchProcessOptions() { FragmentName = fragmentName, SavePrefix = savePrefix };
            var window = new BatchProcessOptionsWindow();
            window.DataContext = options;
            AssetEditorWindow.SetOwnerToActiveWindow(window);
            window.WindowStartupLocation = window.Owner == null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner;
            if (window.ShowDialog() == true)
                return options;
            return null;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }

    public class BatchProcessOptions : NotifyPropertyChangedImpl
    {
        bool _createAnimPack = true;
        public bool CreateAnimPack
        {
            get => _createAnimPack;
            set => SetAndNotify(ref _createAnimPack, value);
        }

        string _animPackName = "my_anim_pack";
        public string AnimPackName
        {
            get => _animPackName;
            set => SetAndNotify(ref _animPackName, value);
        }


        bool _createFragment = true;
        public bool CreateFragment
        {
            get => _createFragment;
            set => SetAndNotify(ref _createFragment, value);
        }

        string _fragmentName;
        public string FragmentName
        {
            get => _fragmentName;
            set => SetAndNotify(ref _fragmentName, value);
        }

        bool _createAnimations = true;
        public bool CreateAnimations
        {
            get => _createAnimations;
            set => SetAndNotify(ref _createAnimations, value);
        }

        public string SavePrefix { get; set; }
    }
}
