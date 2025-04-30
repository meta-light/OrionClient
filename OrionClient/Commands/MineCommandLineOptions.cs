using CommandLine;

namespace OrionClient.Commands
{
    [Verb("mine", HelpText = "Automatically start mining module.")]
    internal class MineCommandLineOptions : CommandLineOptions
    {
    }
}
