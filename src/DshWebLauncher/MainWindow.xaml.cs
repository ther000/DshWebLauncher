using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DshWebLauncher.ViewModels;

namespace DshWebLauncher;

public partial class MainWindow : Window
{
    private const int DwmUseImmersiveDarkMode = 20;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) => EnableDarkTitleBar();
    }

    private void EnableDarkTitleBar()
    {
        var enabled = 1;
        var handle = new WindowInteropHelper(this).Handle;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int valueSize);
}
