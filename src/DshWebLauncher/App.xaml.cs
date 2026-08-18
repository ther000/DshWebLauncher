using System.Windows;
using DshWebLauncher.Services;
using DshWebLauncher.ViewModels;

namespace DshWebLauncher;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\DshWebLauncher.SingleInstance";
    private const string ShowEventName = @"Local\DshWebLauncher.ShowWindow";
    private MainWindow? _mainWindow;
    private MainViewModel? _viewModel;
    private IconService? _iconService;
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showRegistration;
    private bool _allowExit;
    private bool _exiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            _singleInstanceMutex = new Mutex(true, MutexName, out var isFirstInstance);
            if (!isFirstInstance)
            {
                _singleInstanceMutex.Dispose();
                try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch (WaitHandleCannotBeOpenedException) { }
                Shutdown();
                return;
            }

            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            _showRegistration = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => Dispatcher.BeginInvoke(ShowMainWindow), null, Timeout.Infinite, false);
            _iconService = new IconService();
            _iconService.BuildMenu();
            _iconService.ShowRequested += (_, _) => ShowMainWindow();
            _iconService.StartRequested += (_, _) => _viewModel?.StartCommand.Execute(null);
            _iconService.StopRequested += (_, _) => _viewModel?.StopCommand.Execute(null);
            _iconService.OpenWebRequested += (_, _) => _viewModel?.OpenWebCommand.Execute(null);
            _iconService.ExitRequested += async (_, _) => await ExitAsync();

            _viewModel = new MainViewModel(new SettingsService(), new DshProcessService());
            _viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(MainViewModel.Snapshot) or nameof(MainViewModel.StatusTitle))
                    _iconService.Update(_viewModel.Snapshot, $"DSH Web · {_viewModel.StatusTitle}");
            };
            _mainWindow = new MainWindow(_viewModel);
            MainWindow = _mainWindow;
            _mainWindow.Closing += OnMainWindowClosing;
            await _viewModel.InitializeAsync(e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase));
            if (e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase)) _mainWindow.Hide(); else ShowMainWindow();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show($"DSH Web Launcher 启动失败。\n\n{exception.Message}", "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            await ExitAsync();
        }
    }

    private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowExit || _viewModel is null) return;
        e.Cancel = true;
        if (_viewModel.MinimizeToTrayOnClose) _mainWindow?.Hide(); else _ = ExitAsync();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
        _mainWindow.Focus();
    }

    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        _allowExit = true;
        if (_mainWindow is not null) _mainWindow.Closing -= OnMainWindowClosing;
        if (_viewModel is not null) await _viewModel.DisposeAsync();
        _iconService?.Dispose();
        _showRegistration?.Unregister(null);
        _showEvent?.Dispose();
        if (_singleInstanceMutex is not null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch (ApplicationException) { }
            _singleInstanceMutex.Dispose();
        }
        Shutdown();
    }
}
