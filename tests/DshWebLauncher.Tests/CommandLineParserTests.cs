using DshWebLauncher.Models;
using DshWebLauncher.Services;

namespace DshWebLauncher.Tests;

public sealed class CommandLineParserTests
{
    [Fact]
    public void Split_PreservesQuotedValues()
    {
        var result = CommandLineParser.Split("--name \"two words\" --path C:\\Work");

        Assert.Equal(["--name", "two words", "--path", "C:\\Work"], result);
    }

    [Fact]
    public void BuildDshArguments_IncludesWebHostAndPort()
    {
        var settings = new AppSettings
        {
            Host = "0.0.0.0",
            Port = 4010,
            TrustedHosts = "localhost\n127.0.0.1:4010",
            ExtraArguments = "--debug"
        };

        Assert.Equal(
            ["web", "--host", "0.0.0.0", "--port", "4010", "--trusted-host", "localhost", "--trusted-host", "127.0.0.1:4010", "--debug"],
            settings.BuildDshArguments());
    }

    [Fact]
    public void BuildCmdCommand_WrapsBatchPathWithoutBackslashEscaping()
    {
        var command = DshProcessService.BuildCmdCommand(@"C:\Program Files\dsh.cmd", ["web", "--port", "43199"]);

        Assert.Equal("\"chcp 65001>nul & call \"C:\\Program Files\\dsh.cmd\" \"web\" \"--port\" \"43199\"\"", command);
        Assert.DoesNotContain("\\\"", command);
    }

    [Fact]
    public void Split_PreservesEscapedQuoteAndTrailingSlash()
    {
        var result = CommandLineParser.Split("--label \\\"quoted\\\" --dir C:\\Work\\");

        Assert.Equal(["--label", "\"quoted\"", "--dir", "C:\\Work\\"], result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Validate_RejectsInvalidPort(int port)
    {
        var settings = new AppSettings { Port = port };
        Assert.Equal("端口必须在 1 到 65535 之间。", settings.Validate());
    }
}
