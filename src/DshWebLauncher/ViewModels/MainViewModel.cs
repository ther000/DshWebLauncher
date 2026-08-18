using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using DshWebLauncher.Models;
using DshWebLauncher.Services;

namespace DshWebLauncher.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly SettingsService _settingsService;
    private readonly DshProcessService _processService;
    private AppSettings _settings = new();
    private bool _settingsDirty;
    private RuntimeSnapshot _snapshot = new(DshRuntimeState.Stopped, false, null, 0, null, "正在加载", new Uri("http://127.0.0.1:3080"));
    private string _dshCommand = "dsh.cmd";
    private string _host = "127.0.0.1";
    private string _port = "3080";
    private string _trustedHosts = string.Empty;
    private string _extraArguments = string.Empty;
    private bool _openBrowserAfterStart = true;
    private bool _autoStartDsh;
    private bool _startWithWindows;
    private bool _minimizeToTrayOnClose = true;
    private bool _isBusy;
    private string _notice = string.Empty;
    private bool _hasNotice;

    public MainViewModel(SettingsService settingsService, DshProcessService processService)
    {
        _settingsService = settingsService;
        _processService = processService;
        Logs = new ObservableCollection<string>();
        StartCommand = new AsyncRelayCommand(StartAsync, () => !IsBusy && !IsRunning && !Snapshot.IsManaged);
        StopCommand = new AsyncRelayCommand(StopAsync, () => !IsBusy && Snapshot.IsManaged);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        OpenWebCommand = new RelayCommand(OpenWeb, () => IsRunning);
        ClearLogsCommand = new RelayCommand(Logs.Clear);
        _processService.SnapshotChanged += OnSnapshotChanged;
        _processService.LogReceived += OnLogReceived;
    }

    public ObservableCollection<string> Logs { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand OpenWebCommand { get; }
    public ICommand ClearLogsCommand { get; }

    public string DshCommand { get => _dshCommand; set { if (SetProperty(ref _dshCommand, value)) MarkSettingsDirty(); } }
    public string Host { get => _host; set { if (SetProperty(ref _host, value)) MarkSettingsDirty(); } }
    public string Port { get => _port; set { if (SetProperty(ref _port, value)) MarkSettingsDirty(); } }
    public string TrustedHosts { get => _trustedHosts; set { if (SetProperty(ref _trustedHosts, value)) MarkSettingsDirty(); } }
    public string ExtraArguments { get => _extraArguments; set { if (SetProperty(ref _extraArguments, value)) MarkSettingsDirty(); } }
    public bool OpenBrowserAfterStart { get => _openBrowserAfterStart; set { if (SetProperty(ref _openBrowserAfterStart, value)) MarkSettingsDirty(); } }
    public bool AutoStartDsh { get => _autoStartDsh; set { if (SetProperty(ref _autoStartDsh, value)) MarkSettingsDirty(); } }
    public bool StartWithWindows { get => _startWithWindows; set { if (SetProperty(ref _startWithWindows, value)) MarkSettingsDirty(); } }
    public bool MinimizeToTrayOnClose { get => _minimizeToTrayOnClose; set { if (SetProperty(ref _minimizeToTrayOnClose, value)) MarkSettingsDirty(); } }
    public bool HasPendingSettings => _settingsDirty;
    public string SettingsStatus => _settingsDirty ? (IsManagedProcessRunning ? "有参数待下次启动生效" : "有未保存参数") : string.Empty;

    public RuntimeSnapshot Snapshot
    {
        get => _snapshot;
        private set
        {
            if (!SetProperty(ref _snapshot, value)) return;
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsManagedProcessRunning));
            OnPropertyChanged(nameof(StatusTitle));
            OnPropertyChanged(nameof(StatusDetail));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(ProcessInfo));
            OnPropertyChanged(nameof(UptimeText));
            OnPropertyChanged(nameof(MemoryText));
            OnPropertyChanged(nameof(WebAddress));
            OnPropertyChanged(nameof(SettingsStatus));
            RaiseCommandStates();
        }
    }

    public bool IsRunning => Snapshot.IsRunning;
    public bool IsManagedProcessRunning => Snapshot.IsManaged;
    public string StatusTitle => Snapshot.State switch
    {
        DshRuntimeState.Running => "运行中",
        DshRuntimeState.External => "外部运行",
        DshRuntimeState.Starting => "正在启动",
        DshRuntimeState.Stopping => "正在停止",
        DshRuntimeState.Faulted => "启动失败",
        DshRuntimeState.StopFailed => "停止失败",
        _ => "未运行"
    };
    public string StatusDetail => Snapshot.Detail;
    public string StatusColor => Snapshot.State switch
    {
        DshRuntimeState.Running or DshRuntimeState.External => "#4D6BFE",
        DshRuntimeState.Starting or DshRuntimeState.Stopping => "#F2B84B",
        DshRuntimeState.Faulted or DshRuntimeState.StopFailed => "#F06A6A",
        _ => "#8791A5"
    };
    public string ProcessInfo => Snapshot.ProcessId is { } pid ? $"PID {pid}" : "未分配进程";
    public string UptimeText => Snapshot.Uptime is { } uptime ? FormatDuration(uptime) : "--";
    public string MemoryText => Snapshot.WorkingSetBytes > 0 ? $"{Snapshot.WorkingSetBytes / 1024d / 1024d:0.0} MB" : "--";
    public string WebAddress => Snapshot.WebUri.ToString().TrimEnd('/');
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) RaiseCommandStates(); } }
    public string Notice { get => _notice; private set { if (SetProperty(ref _notice, value)) OnPropertyChanged(nameof(HasNotice)); } }
    public bool HasNotice { get => _hasNotice; private set => SetProperty(ref _hasNotice, value); }

    public async Task InitializeAsync(bool background)
    {
        _settings = await _settingsService.LoadAsync();
        _settings.StartWithWindows = StartupService.IsEnabled();
        ApplySettings(_settings);
        _settingsDirty = false;
        OnPropertyChanged(nameof(HasPendingSettings));
        OnPropertyChanged(nameof(SettingsStatus));
        await _processService.BeginMonitoringAsync(_settings);
        if (_settings.AutoStartDsh && !_processService.Snapshot.IsRunning)
        {
            await StartAsync();
        }
        if (!background) return;
    }

    private AppSettings ReadSettings()
    {
        var port = int.TryParse(Port, out var parsedPort) ? parsedPort : 0;
        return new AppSettings
        {
            DshCommand = DshCommand.Trim(), Host = Host.Trim(), Port = port,
            TrustedHosts = TrustedHosts.Trim(), ExtraArguments = ExtraArguments.Trim(),
            OpenBrowserAfterStart = OpenBrowserAfterStart, AutoStartDsh = AutoStartDsh,
            StartWithWindows = StartWithWindows, MinimizeToTrayOnClose = MinimizeToTrayOnClose
        };
    }

    private void ApplySettings(AppSettings settings)
    {
        DshCommand = settings.DshCommand;
        Host = settings.Host;
        Port = settings.Port.ToString();
        TrustedHosts = settings.TrustedHosts;
        ExtraArguments = settings.ExtraArguments;
        OpenBrowserAfterStart = settings.OpenBrowserAfterStart;
        AutoStartDsh = settings.AutoStartDsh;
        _startWithWindows = settings.StartWithWindows;
        OnPropertyChanged(nameof(StartWithWindows));
        MinimizeToTrayOnClose = settings.MinimizeToTrayOnClose;
    }

    private async Task SaveAsync()
    {
        try
        {
            var settings = ReadSettings();
            if (settings.Validate() is { } error) { ShowNotice(error); return; }
            await PersistSettingsAsync(settings);
            _settings = settings;
            _settingsDirty = false;
            OnPropertyChanged(nameof(HasPendingSettings));
            OnPropertyChanged(nameof(SettingsStatus));
            _processService.UpdateSettings(settings);
            ShowNotice(IsManagedProcessRunning ? "参数已保存，将在下次启动时生效" : "启动参数已保存");
        }
        catch (Exception exception) { ShowNotice($"保存失败：{exception.Message}"); }
    }

    private async Task StartAsync()
    {
        try
        {
            var settings = ReadSettings();
            if (settings.Validate() is { } error) { ShowNotice(error); return; }
            await PersistSettingsAsync(settings);
            _settings = settings;
            _settingsDirty = false;
            OnPropertyChanged(nameof(HasPendingSettings));
            OnPropertyChanged(nameof(SettingsStatus));
            IsBusy = true;
            await _processService.StartAsync(settings);
            if (settings.OpenBrowserAfterStart) OpenWeb();
        }
        catch (Exception exception) { ShowNotice(exception.Message); }
        finally { IsBusy = false; }
    }

    private async Task StopAsync()
    {
        try
        {
            IsBusy = true;
            await _processService.StopAsync();
            _processService.UpdateSettings(_settings);
        }
        catch (Exception exception) { ShowNotice($"停止失败：{exception.Message}"); }
        finally { IsBusy = false; }
    }

    private void OpenWeb()
    {
        try { Process.Start(new ProcessStartInfo(WebAddress) { UseShellExecute = true }); }
        catch (Exception exception) { ShowNotice($"无法打开浏览器：{exception.Message}"); }
    }

    private void OnSnapshotChanged(object? sender, RuntimeSnapshot snapshot) =>
        System.Windows.Application.Current.Dispatcher.Invoke(() => Snapshot = snapshot);

    private void OnLogReceived(object? sender, string message) => System.Windows.Application.Current.Dispatcher.Invoke(() =>
    {
        Logs.Add(message);
        while (Logs.Count > 300) Logs.RemoveAt(0);
    });

    private async Task PersistSettingsAsync(AppSettings settings)
    {
        var previousStartup = StartupService.IsEnabled();
        try
        {
            StartupService.SetEnabled(settings.StartWithWindows);
            await _settingsService.SaveAsync(settings);
        }
        catch
        {
            StartupService.SetEnabled(previousStartup);
            throw;
        }
    }

    private void MarkSettingsDirty()
    {
        _settingsDirty = true;
        OnPropertyChanged(nameof(HasPendingSettings));
        OnPropertyChanged(nameof(SettingsStatus));
    }

    private void ShowNotice(string message)
    {
        Notice = message;
        HasNotice = true;
    }

    public async ValueTask DisposeAsync()
    {
        _processService.SnapshotChanged -= OnSnapshotChanged;
        _processService.LogReceived -= OnLogReceived;
        await _processService.DisposeAsync();
    }

    private void RaiseCommandStates()
    {
        if (StartCommand is AsyncRelayCommand start) start.RaiseCanExecuteChanged();
        if (StopCommand is AsyncRelayCommand stop) stop.RaiseCanExecuteChanged();
        if (SaveCommand is AsyncRelayCommand save) save.RaiseCanExecuteChanged();
        if (OpenWebCommand is RelayCommand open) open.RaiseCanExecuteChanged();
    }

    private static string FormatDuration(TimeSpan value) => value.TotalHours >= 1
        ? $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}"
        : $"{value.Minutes:00}:{value.Seconds:00}";
}
