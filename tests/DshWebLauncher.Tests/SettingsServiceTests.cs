using DshWebLauncher.Models;
using DshWebLauncher.Services;

namespace DshWebLauncher.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DshWebLauncher.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndLoad_RoundTripsSettingsAtomically()
    {
        var service = new SettingsService(_directory);
        var expected = new AppSettings { Port = 4123, Host = "0.0.0.0", ExtraArguments = "--test" };

        await service.SaveAsync(expected);
        var actual = await service.LoadAsync();

        Assert.Equal(expected.Port, actual.Port);
        Assert.Equal(expected.Host, actual.Host);
        Assert.Equal(expected.ExtraArguments, actual.ExtraArguments);
        Assert.False(File.Exists(service.SettingsPath + ".tmp"));
    }

    [Fact]
    public async Task LoadAsync_MovesInvalidJsonAside()
    {
        Directory.CreateDirectory(_directory);
        var service = new SettingsService(_directory);
        await File.WriteAllTextAsync(service.SettingsPath, "{ not-json");

        var settings = await service.LoadAsync();

        Assert.Equal(3080, settings.Port);
        Assert.False(File.Exists(service.SettingsPath));
        Assert.Single(Directory.GetFiles(_directory, "settings.invalid.*.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
