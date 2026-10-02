using System.Linq;
using GridLookout.Cli;
using Xunit;

namespace GridLookout.Tests.Cli;

public class CommandLineTests
{
    [Theory]
    [InlineData("--help")]
    [InlineData("--HELP")]
    [InlineData("-h")]
    [InlineData("-?")]
    [InlineData("/?")]
    [InlineData("/help")]
    public void IsHelpRequested_RecognisesEveryForm(string form)
    {
        Assert.True(CommandLine.IsHelpRequested(new[] { "--recorder", "CAMWALL-01", form }));
    }

    [Fact]
    public void IsHelpRequested_FalseForAWallRun()
    {
        Assert.False(CommandLine.IsHelpRequested(new[] { "--recorder", "CAMWALL-01", "--monitor", "2" }));
        Assert.False(CommandLine.IsHelpRequested(System.Array.Empty<string>()));
    }

    [Fact]
    public void FindUnknownSwitches_FlagsTheTypo_NeverTheValues()
    {
        var unknown = CommandLine.FindUnknownSwitches(
            new[] { "--recoder", "CAMWALL-01", "--recorder", "/odd-name", "--monitor", "2", "/x", "plain" });

        Assert.Equal(new[] { "--recoder", "/x" }, unknown);
    }

    [Fact]
    public void FindUnknownSwitches_EmptyForEveryKnownSwitchAndHelpForm()
    {
        var args = CommandLine.KnownSwitches
            .SelectMany(s => s.TakesValue ? new[] { s.Name, "value" } : new[] { s.Name })
            .Concat(new[] { "-h", "/?" })
            .ToArray();

        Assert.Empty(CommandLine.FindUnknownSwitches(args));
    }

    [Fact]
    public void KnownSwitches_PinnedToWhatProgramCsReads()
    {
        // Program.cs dispatches on exactly these names; a new switch there must be added here too,
        // or the startup warning fires on it. Update both together.
        Assert.Equal(
            new[] { "--recorder", "--monitor", "--protect-password", "--health-probe", "--screenshot", "--export-camera-bindings", "--license", "--license-install", "--license-fingerprint", "--help" },
            CommandLine.KnownSwitches.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void BuildUsage_ListsEverySwitchAndTheVersion()
    {
        string usage = CommandLine.BuildUsage("1.0.2");

        Assert.Contains("GridLookout 1.0.2", usage);
        foreach (var s in CommandLine.KnownSwitches)
        {
            Assert.Contains(s.Name, usage);
        }
        Assert.Contains("--recorder <name>", usage);
    }
}
