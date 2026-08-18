using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using DshWebLauncher.Models;

namespace DshWebLauncher.Services;

public sealed class DshProcessService : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IDshHealthChecker _healthChecker;
    private readonly IExternalDshProcessLocator _externalProcessLocator;
    private readonly CancellationTokenSource _monitorCancellation = new();
    private readonly object _processLock = new();
    private Process? _process;
    private AppSettings _settings = new();
    private DshRuntimeState _transitionState = DshRuntimeState.Stopped;
    private bool _adoptedExternal;
    private string? _lastError;
    private Task? _monitorTask;

    public DshProcessService() : this(new HttpClient { Timeout = TimeSpan.FromMilliseconds(900) }) { }

    public DshProcessService(
        HttpClient httpClient,
        IDshHealthChecker? healthChecker = null,
        IExternalDshProcessLocator? externalProcessLocator = null)
    {
        _httpClient = httpClient;
        _healthChecker = healthChecker ?? new DshHealthChecker(_httpClient);
        _externalProcessLocator = externalProcessLocator ?? new ExternalDshProcessLocator();
    }

    public event EventHandler<RuntimeSnapshot>? SnapshotChanged;
    public event EventHandler<string>? LogReceived;
    public RuntimeSnapshot Snapshot { get; private set; } = new(DshRuntimeState.Stopped, false, null, 0, null, "尚未运行", new Uri("http://127.0.0.1:3080"));

    public async Task BeginMonitoringAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        _settings = settings;
        var reachable = await _healthChecker.IsHealthyAsync(settings.WebUri, cancellationToken);
        if (reachable) TryAdoptExternalProcess(settings.WebUri);
        PublishSnapshot(reachable, reachable ? "服务响应正常" : "等待服务响应");
        _monitorTask ??= MonitorLoopAsync(_monitorCancellation.Token);
    }

    public void UpdateSettings(AppSettings settings)
    {
        lock (_processLock)
        {
            if (_process is { HasExited: false }) return;
            _settings = settings;
        }
    }

    public async Task StartAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (settings.Validate() is { } error) throw new InvalidOperationException(error);
        lock (_processLock)
        {
            if (_process is { HasExited: false }) return;
            _settings = settings;
            _adoptedExternal = false;
            _lastError = null;
        }

        if (await _healthChecker.IsHealthyAsync(settings.WebUri, cancellationToken))
        {
            var adopted = TryAdoptExternalProcess(settings.WebUri);
            lock (_processLock) _transitionState = DshRuntimeState.Stopped;
            AppendLog("SYS", adopted
                ? $"已接管正在运行的 DSH Web：{settings.WebUri}"
                : $"已连接 DSH Web，但未能安全定位其进程：{settings.WebUri}");
            PublishSnapshot(true, adopted ? "已接管现有服务" : "已连接现有服务");
            return;
        }

        lock (_processLock) _transitionState = DshRuntimeState.Starting;
        PublishSnapshot(false, "正在启动 dsh web…");

        Process? process = null;
        try
        {
            process = new Process { StartInfo = CreateStartInfo(settings), EnableRaisingEvents = true };
            process.OutputDataReceived += (_, args) => AppendLog("OUT", args.Data);
            process.ErrorDataReceived += (_, args) => AppendLog("ERR", args.Data);
            process.Exited += (_, _) => OnProcessExited(process);
            if (!process.Start()) throw new InvalidOperationException("系统未能创建 dsh web 进程。");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            lock (_processLock) _process = process;
            AppendLog("SYS", $"已启动进程树 PID {process.Id}");

            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                if (process.HasExited) throw new InvalidOperationException($"dsh web 已退出，退出码 {process.ExitCode}。");
                if (await _healthChecker.IsHealthyAsync(settings.WebUri, cancellationToken))
                {
                    lock (_processLock) _transitionState = DshRuntimeState.Running;
                    PublishSnapshot(true, "服务响应正常");
                    return;
                }
                await Task.Delay(350, cancellationToken);
            }
            throw new TimeoutException("dsh web 在 20 秒内未响应，请检查运行日志。");
        }
        catch (OperationCanceledException)
        {
            await CleanupFailedProcessAsync(process);
            lock (_processLock)
            {
                if (ReferenceEquals(_process, process)) _process = null;
                _transitionState = DshRuntimeState.Stopped;
                _adoptedExternal = false;
            }
            PublishSnapshot(false, "启动已取消");
            throw;
        }
        catch (Exception exception)
        {
            await CleanupFailedProcessAsync(process);
            lock (_processLock)
            {
                if (ReferenceEquals(_process, process)) _process = null;
                _transitionState = DshRuntimeState.Faulted;
                _lastError = exception.Message.Trim();
            }
            AppendLog("ERR", _lastError);
            PublishSnapshot(false, _lastError);
            throw;
        }
    }

    public async Task StopAsync()
    {
        Process? process;
        lock (_processLock)
        {
            process = _process;
            if (process is null || process.HasExited)
            {
                _process = null;
                _transitionState = DshRuntimeState.Stopped;
                return;
            }
            _transitionState = DshRuntimeState.Stopping;
        }
        PublishSnapshot(Snapshot.IsReachable, "正在停止受管进程树…");
        try
        {
            process.Kill(true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
            lock (_processLock)
            {
                if (ReferenceEquals(_process, process)) _process = null;
                _transitionState = DshRuntimeState.Stopped;
                _adoptedExternal = false;
            }
            AppendLog("SYS", "受管进程树已停止");
            PublishSnapshot(false, "服务未运行");
        }
        catch (InvalidOperationException)
        {
            lock (_processLock) { if (ReferenceEquals(_process, process)) _process = null; _transitionState = DshRuntimeState.Stopped; _adoptedExternal = false; }
            PublishSnapshot(false, "服务未运行");
        }
        catch (TimeoutException)
        {
            lock (_processLock) _transitionState = DshRuntimeState.StopFailed;
            AppendLog("ERR", "停止进程树超时，仍保留进程控制权。");
            PublishSnapshot(Snapshot.IsReachable, "停止超时，请重试");
            throw;
        }
    }

    private bool TryAdoptExternalProcess(Uri webUri)
    {
        lock (_processLock)
        {
            if (_process is { HasExited: false }) return true;
        }

        var process = _externalProcessLocator.TryFind(webUri);
        if (process is null) return false;

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => OnProcessExited(process);
        lock (_processLock)
        {
            if (_process is { HasExited: false })
            {
                process.Dispose();
                return true;
            }
            _process = process;
            _transitionState = DshRuntimeState.Running;
            _adoptedExternal = true;
            _lastError = null;
        }
        AppendLog("SYS", $"已接管外部 DSH Web 进程 PID {process.Id}");
        return true;
    }

    private async Task CleanupFailedProcessAsync(Process? process)
    {
        if (process is null || process.HasExited) return;
        try
        {
            process.Kill(true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception cleanupException) when (cleanupException is InvalidOperationException or TimeoutException)
        {
            AppendLog("ERR", $"清理失败进程树时发生错误：{cleanupException.Message}");
        }
    }

    private static ProcessStartInfo CreateStartInfo(AppSettings settings)
    {
        var command = ResolveCommand(settings.DshCommand);
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (Path.GetExtension(command).Equals(".cmd", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(command).Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            startInfo.Arguments = $"/d /s /c {BuildCmdCommand(command, settings.BuildDshArguments())}";
        }
        else
        {
            startInfo.FileName = command;
            foreach (var argument in settings.BuildDshArguments()) startInfo.ArgumentList.Add(argument);
        }
        return startInfo;
    }

    private static string ResolveCommand(string command)
    {
        if (Path.IsPathFullyQualified(command)) return command;
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var extensions = Path.HasExtension(command) ? [string.Empty] : new[] { string.Empty, ".cmd", ".exe", ".bat" };
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        foreach (var extension in extensions)
        {
            var candidate = Path.Combine(directory.Trim('"'), command + extension);
            if (File.Exists(candidate)) return candidate;
        }
        return command;
    }

    internal static string BuildCmdCommand(string executable, IReadOnlyList<string> arguments)
    {
        static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        return $"\"chcp 65001>nul & call {string.Join(' ', new[] { Quote(executable) }.Concat(arguments.Select(Quote)))}\"";
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var reachable = await _healthChecker.IsHealthyAsync(_settings.WebUri, cancellationToken);
                if (reachable) TryAdoptExternalProcess(_settings.WebUri);
                PublishSnapshot(reachable, reachable ? "服务响应正常" : "等待服务响应");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void PublishSnapshot(bool reachable, string detail)
    {
        Process? process;
        DshRuntimeState transitionState;
        bool adoptedExternal;
        string? lastError;
        AppSettings settings;
        lock (_processLock) { process = _process; transitionState = _transitionState; adoptedExternal = _adoptedExternal; lastError = _lastError; settings = _settings; }
        var managedAlive = process is not null && !process.HasExited;
        var state = transitionState switch
        {
            DshRuntimeState.StopFailed => DshRuntimeState.StopFailed,
            DshRuntimeState.Starting when managedAlive && !reachable => DshRuntimeState.Starting,
            DshRuntimeState.Stopping when managedAlive => DshRuntimeState.Stopping,
            DshRuntimeState.Faulted when !reachable => DshRuntimeState.Faulted,
            _ when managedAlive && reachable => DshRuntimeState.Running,
            _ when managedAlive => DshRuntimeState.Starting,
            _ when reachable => DshRuntimeState.External,
            _ => DshRuntimeState.Stopped
        };
        long workingSet = 0; TimeSpan? uptime = null; int? processId = null;
        if (managedAlive && process is not null)
        {
            try
            {
                var metrics = ProcessTreeService.GetMetrics(process);
                processId = metrics.ServiceProcessId;
                workingSet = metrics.WorkingSetBytes;
                uptime = DateTime.Now - metrics.StartTime;
            }
            catch (InvalidOperationException) { }
        }
        var effectiveDetail = state switch
        {
            DshRuntimeState.External => "检测到外部 DSH Web 实例",
            DshRuntimeState.Stopped => "服务未运行",
            DshRuntimeState.Faulted => lastError ?? detail,
            DshRuntimeState.StopFailed => "停止超时，请重试",
            DshRuntimeState.Running when adoptedExternal => "已接管现有 DSH Web 实例",
            _ => detail
        };
        Snapshot = new RuntimeSnapshot(state, reachable, processId, workingSet, uptime, effectiveDetail, settings.WebUri);
        SnapshotChanged?.Invoke(this, Snapshot);
    }

    private void OnProcessExited(Process exitedProcess)
    {
        var exitCode = 0; try { exitCode = exitedProcess.ExitCode; } catch (InvalidOperationException) { }
        AppendLog("SYS", $"受管进程树已退出，退出码 {exitCode}");
        lock (_processLock)
        {
            if (ReferenceEquals(_process, exitedProcess)) _process = null;
            _adoptedExternal = false;
            if (_transitionState != DshRuntimeState.Stopping && exitCode != 0) { _transitionState = DshRuntimeState.Faulted; _lastError = $"dsh web 异常退出，退出码 {exitCode}。"; }
            else if (_transitionState != DshRuntimeState.StopFailed) _transitionState = DshRuntimeState.Stopped;
        }
    }

    private void AppendLog(string source, string? message)
    {
        if (!string.IsNullOrWhiteSpace(message)) LogReceived?.Invoke(this, $"{DateTime.Now:HH:mm:ss}  {source,-3}  {message.TrimEnd()}");
    }

    public async ValueTask DisposeAsync()
    {
        _monitorCancellation.Cancel();
        if (_monitorTask is not null) { try { await _monitorTask; } catch (OperationCanceledException) { } }
        try { await StopAsync(); } catch (TimeoutException) { }
        _httpClient.Dispose(); _monitorCancellation.Dispose();
    }
}
