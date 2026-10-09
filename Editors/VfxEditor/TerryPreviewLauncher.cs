using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Shared.Core.Services;

namespace Editors.VfxEditor;

internal static class TerryPreviewLauncher
{
    public static async Task CloseAsync(string game, LocalizationManager localization, CancellationToken cancellationToken)
    {
        foreach (var process in Process.GetProcessesByName("tweak.modder.x64"))
        {
            using (process)
            {
                if (process.HasExited) continue;
                if (!string.Equals(process.MainModule?.FileName, Executable(game), StringComparison.OrdinalIgnoreCase)) continue;
                if (!process.MainWindowTitle.Contains("Terry", StringComparison.OrdinalIgnoreCase) || !process.CloseMainWindow())
                    throw new InvalidOperationException(localization.Get("Vfx.Terry.CloseOtherWindow"));
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new InvalidOperationException(localization.Get("Vfx.Terry.SaveScene"));
                }
            }
        }
    }

    public static Process Start(string game, string scene)
    {
        var start = new ProcessStartInfo(Executable(game)) { WorkingDirectory = game, UseShellExecute = true };
        start.ArgumentList.Add("/standalone");
        start.ArgumentList.Add("TerrainMetadataEditor");
        // Terry's project loader cannot resolve some non-ASCII absolute paths.
        start.ArgumentList.Add(Path.GetRelativePath(game, scene));
        return Process.Start(start) ?? throw new IOException(LocalizationManager.Instance.Get("Vfx.Terry.StartFailed"));
    }

    public static async Task WaitForSceneAsync(Process process, string scene, LocalizationManager localization, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var name = Path.GetFileName(scene);
        var accepted = false;
        while (timer.Elapsed < TimeSpan.FromSeconds(90))
        {
            cancellationToken.ThrowIfCancellationRequested();
            process.Refresh();
            if (process.HasExited) throw new IOException(localization.Get("Vfx.Terry.StartFailed"));
            if (process.MainWindowTitle.Contains(name, StringComparison.OrdinalIgnoreCase)
                && process.MainWindowTitle.Contains("Terry", StringComparison.OrdinalIgnoreCase))
            {
                await PrepareCurrentEffectAsync(process, localization, cancellationToken);
                return;
            }
            if (!accepted && process.MainWindowHandle != IntPtr.Zero)
            {
                try
                {
                    var window = AutomationElement.FromHandle(process.MainWindowHandle);
                    var filename = window.FindFirst(TreeScope.Descendants, new AndCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text),
                        new PropertyCondition(AutomationElement.NameProperty, name)));
                    if (filename != null)
                    {
                        var button = window.FindFirst(TreeScope.Descendants, new AndCondition(
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                            new OrCondition(new PropertyCondition(AutomationElement.NameProperty, "OK"),
                                new PropertyCondition(AutomationElement.NameProperty, "确定"))));
                        // Accept only the component selector for the scene we just generated.
                        if (button?.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern) == true)
                        {
                            ((InvokePattern)pattern).Invoke();
                            accepted = true;
                        }
                    }
                }
                catch (ElementNotAvailableException) { }
            }
            await Task.Delay(300, cancellationToken);
        }
        throw new IOException(localization.Get("Vfx.Terry.StartTimeout"));
    }

    private static async Task PrepareCurrentEffectAsync(Process process, LocalizationManager localization, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var toolActivated = false;
        var filterApplied = false;
        while (timer.Elapsed < TimeSpan.FromSeconds(30))
        {
            cancellationToken.ThrowIfCancellationRequested();
            process.Refresh();
            if (process.HasExited) throw new IOException(localization.Get("Vfx.Terry.StartFailed"));
            try
            {
                // Only change the newly launched window's transient name filter.
                var window = AutomationElement.FromHandle(process.MainWindowHandle);
                if (!toolActivated)
                {
                    var tool = window.FindFirst(TreeScope.Descendants, new AndCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox),
                        new PropertyCondition(AutomationElement.NameProperty, "Create VFX")));
                    if (tool?.Current.IsEnabled == true && tool.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
                    {
                        // Invoke emits clicked; Toggle only changes the Qt button's checked state.
                        ((InvokePattern)invoke).Invoke();
                        toolActivated = true;
                    }
                }
                else
                {
                    var trees = window.FindAll(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Tree));
                    foreach (AutomationElement tree in trees)
                    {
                        var current = tree.FindFirst(TreeScope.Children,
                            new PropertyCondition(AutomationElement.NameProperty, TerryPreviewBuilder.EffectName));
                        if (current == null) continue;
                        var container = TreeWalker.ControlViewWalker.GetParent(tree);
                        var inputs = container?.FindAll(TreeScope.Descendants,
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                        if (inputs?.Count != 1) continue;
                        if (!inputs[0].TryGetCurrentPattern(ValuePattern.Pattern, out var value)) continue;
                        if (!filterApplied)
                        {
                            filterApplied = ApplyFilter(process, inputs[0], (ValuePattern)value);
                            break;
                        }
                        // Qt's grid reports filtered rows, including while the window is covered.
                        if (tree.TryGetCurrentPattern(GridPattern.Pattern, out var grid)
                            && ((GridPattern)grid).Current.RowCount == 1
                            && ((GridPattern)grid).GetItem(0, 0).Current.Name == TerryPreviewBuilder.EffectName
                            && ((ValuePattern)value).Current.Value == TerryPreviewBuilder.EffectName)
                        {
                            Shared.Core.ErrorHandling.Logging.CreateStatic(typeof(TerryPreviewLauncher))
                                .Information("Terry preview is ready in process {ProcessId}; current effect filter verified", process.Id);
                            return;
                        }
                    }
                }
            }
            catch (ElementNotAvailableException) { }
            await Task.Delay(300, cancellationToken);
        }
        Shared.Core.ErrorHandling.Logging.CreateStatic(typeof(TerryPreviewLauncher))
            .Warning("Could not prepare Terry preview VFX; tool activated {Activated}, filter applied {Filtered}", toolActivated, filterApplied);
        throw new IOException(localization.Get("Vfx.Terry.FilterUnavailable"));
    }

    private static bool ApplyFilter(Process process, AutomationElement input, ValuePattern value)
    {
        var handle = process.MainWindowHandle;
        GetWindowThreadProcessId(handle, out var owner);
        if (handle == IntPtr.Zero || owner != process.Id || input.Current.ProcessId != process.Id
            || input.Current.ClassName != "QLineEdit" || !input.Current.IsEnabled || value.Current.IsReadOnly)
            return false;
        // Qt's child focus stays inside Terry, even when another application is active.
        // Send the last character to this window so QLineEdit emits textEdited.
        var filter = TerryPreviewBuilder.EffectName;
        value.SetValue(filter[..^1]);
        input.SetFocus();
        return SendMessageTimeout(handle, 0x0102, (UIntPtr)filter[^1], (IntPtr)1,
            0x0002, 1000, out _) != IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out UIntPtr result);

    private static string Executable(string game) => Path.GetFullPath(Path.Combine(game, "assembly_kit", "binaries", "tweak.modder.x64.exe"));
}
