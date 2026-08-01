using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Shared.Ui.Common;
using Shared.Ui.Common.ValueConverters;

namespace AssetEditorTests;

internal static class WpfTestApplicationHost
{
    private static readonly object SyncRoot = new();
    private static readonly ManualResetEventSlim Started = new();
    private static Dispatcher? _dispatcher;
    private static TestApplication? _application;
    private static Exception? _startupException;

    public static void Invoke(Action<Application> action)
    {
        EnsureStarted();
        _dispatcher!.Invoke(() => action(_application!));
    }

    public static void InvokeWithThemeResources(
        IServiceProvider serviceProvider,
        Action action)
    {
        EnsureStarted();
        _dispatcher!.Invoke(() =>
        {
            _application!.ServiceProvider = serviceProvider;
            _application.EnsureThemeResources();
            action();
        });
    }

    private static void EnsureStarted()
    {
        if (_dispatcher != null)
            return;

        lock (SyncRoot)
        {
            if (_dispatcher != null)
                return;
            if (_startupException != null)
                throw new InvalidOperationException(
                    "The WPF test application failed to start.",
                    _startupException);

            var thread = new Thread(() =>
            {
                try
                {
                    _dispatcher = Dispatcher.CurrentDispatcher;
                    _application = new TestApplication();
                }
                catch (Exception exception)
                {
                    _startupException = exception;
                }
                finally
                {
                    Started.Set();
                }

                if (_startupException == null)
                    Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "AssetEditorTests.WpfApplication"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Started.Wait();

            if (_startupException != null)
                throw new InvalidOperationException(
                    "The WPF test application failed to start.",
                    _startupException);
        }
    }

    private sealed class TestApplication : Application, IAssetEditorMain
    {
        private bool _themeResourcesLoaded;

        public IServiceProvider ServiceProvider { get; set; } =
            EmptyServiceProvider.Instance;

        public TestApplication()
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        public void EnsureThemeResources()
        {
            if (_themeResourcesLoaded)
                return;

            Resources.MergedDictionaries.Add(CreateResourceDictionary(
                "Themes/ColourDictionaries/DarkTheme.xaml"));
            Resources.MergedDictionaries.Add(CreateResourceDictionary(
                "Themes/ControlColours.xaml"));
            Resources.MergedDictionaries.Add(CreateResourceDictionary(
                "Themes/Controls.xaml"));
            Resources["BoolToChangedPrefixStr"] =
                new BoolToStringConverter { TrueValue = "*" };
            _themeResourcesLoaded = true;
        }

        private static ResourceDictionary CreateResourceDictionary(
            string path) => new()
            {
                Source = new Uri(
                    $"pack://application:,,,/AssetEditor.CN;component/{path}")
            };
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new();

        public object? GetService(Type serviceType) => null;
    }
}
