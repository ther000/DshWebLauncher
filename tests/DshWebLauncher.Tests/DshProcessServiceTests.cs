using System.Diagnostics;
using DshWebLauncher.Models;
using DshWebLauncher.Services;

namespace DshWebLauncher.Tests;

public sealed class DshProcessServiceTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DshWebLauncher.Tests", Guid.NewGuid().ToString("N"));
    private string _scriptPath = string.Empty;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        _scriptPath = Path.Combine(_directory, "fake-dsh.cmd");
        await File.WriteAllTextAsync(_scriptPath, "@echo off\r\nping 127.0.0.1 -n 30 >nul\r\n");
    }

    [Fact]
    public async Task StartAndStop_TracksAndTerminatesManagedProcessTree()
    {
        using var client = new HttpClient();
        await using var service = new DshProcessService(client, new FirstUnhealthyThenHealthyChecker());

        await service.StartAsync(new AppSettings { DshCommand = _scriptPath, Port = 43198 });
        var running = service.Snapshot;

        Assert.Equal(DshRuntimeState.Running, running.State);
        Assert.True(running.IsManaged);
        Assert.NotNull(running.ProcessId);
        Assert.True(running.WorkingSetBytes > 0);

        await service.StopAsync();

        Assert.Equal(DshRuntimeState.Stopped, service.Snapshot.State);
        Assert.False(service.Snapshot.IsManaged);
    }

    [Fact]
    public async Task Start_WhenExternalDshIsHealthy_ConnectsWithoutCreatingManagedProcess()
    {
        using var client = new HttpClient();
        await using var service = new DshProcessService(client, new AlwaysHealthyChecker());
        var settings = new AppSettings { DshCommand = Path.Combine(_directory, "missing-dsh.cmd"), Port = 43199 };

        await service.StartAsync(settings);

        Assert.Equal(DshRuntimeState.External, service.Snapshot.State);
        Assert.True(service.Snapshot.IsRunning);
        Assert.False(service.Snapshot.IsManaged);
        Assert.Null(service.Snapshot.ProcessId);
        Assert.Equal(settings.WebUri, service.Snapshot.WebUri);
    }

    [Fact]
    public async Task BeginMonitoring_ImmediatelyRecognizesExternalDsh()
    {
        using var client = new HttpClient();
        await using var service = new DshProcessService(client, new AlwaysHealthyChecker(), new MissingProcessLocator());
        var settings = new AppSettings { Port = 43200 };

        await service.BeginMonitoringAsync(settings);

        Assert.Equal(DshRuntimeState.External, service.Snapshot.State);
        Assert.True(service.Snapshot.IsRunning);
        Assert.False(service.Snapshot.IsManaged);
    }

    [Fact]
    public async Task BeginMonitoring_WhenExternalProcessIsLocated_AdoptsAndStopsIt()
    {
        using var externalProcess = StartFakeProcess();
        using var client = new HttpClient();
        await using var service = new DshProcessService(client, new AlwaysHealthyChecker(), new FixedProcessLocator(externalProcess.Id));

        await service.BeginMonitoringAsync(new AppSettings { Port = 43201 });

        Assert.Equal(DshRuntimeState.Running, service.Snapshot.State);
        Assert.True(service.Snapshot.IsManaged);
        Assert.Equal(externalProcess.Id, service.Snapshot.ProcessId);

        await service.StopAsync();

        Assert.True(externalProcess.WaitForExit(5000));
        Assert.Equal(DshRuntimeState.Stopped, service.Snapshot.State);
    }

    private Process StartFakeProcess()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = $"/d /s /c call \"{_scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        return Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动测试进程。");
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        return Task.CompletedTask;
    }

    private sealed class AlwaysHealthyChecker : IDshHealthChecker
    {
        public Task<bool> IsHealthyAsync(Uri uri, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class FirstUnhealthyThenHealthyChecker : IDshHealthChecker
    {
        private int _calls;

        public Task<bool> IsHealthyAsync(Uri uri, CancellationToken cancellationToken) =>
            Task.FromResult(Interlocked.Increment(ref _calls) > 1);
    }

    private sealed class MissingProcessLocator : IExternalDshProcessLocator
    {
        public Process? TryFind(Uri webUri) => null;
    }

    private sealed class FixedProcessLocator(int processId) : IExternalDshProcessLocator
    {
        public Process? TryFind(Uri webUri) => Process.GetProcessById(processId);
    }
}
