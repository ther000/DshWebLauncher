using System.Text.Json.Serialization;
using DshWebLauncher.Services;

namespace DshWebLauncher.Models;

public sealed class AppSettings
{
    public string DshCommand { get; set; } = "dsh.cmd";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 3080;
    public string TrustedHosts { get; set; } = string.Empty;
    public string ExtraArguments { get; set; } = string.Empty;
    public bool OpenBrowserAfterStart { get; set; } = true;
    public bool AutoStartDsh { get; set; }
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTrayOnClose { get; set; } = true;

    [JsonIgnore]
    public Uri WebUri
    {
        get
        {
            var browserHost = Host is "0.0.0.0" or "::" ? "127.0.0.1" : Host;
            return new UriBuilder(Uri.UriSchemeHttp, browserHost, Port).Uri;
        }
    }

    public IReadOnlyList<string> BuildDshArguments()
    {
        var arguments = new List<string> { "web", "--host", Host, "--port", Port.ToString() };
        foreach (var host in TrustedHosts.Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            arguments.Add("--trusted-host");
            arguments.Add(host);
        }

        arguments.AddRange(CommandLineParser.Split(ExtraArguments));
        return arguments;
    }

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(DshCommand)) return "请输入 dsh 命令路径。";
        if (string.IsNullOrWhiteSpace(Host)) return "监听地址不能为空。";
        if (Port is < 1 or > 65535) return "端口必须在 1 到 65535 之间。";
        if (Uri.CheckHostName(Host) != UriHostNameType.Unknown || Host is "0.0.0.0" or "::") return null;
        return "监听地址格式无效。";
    }
}
