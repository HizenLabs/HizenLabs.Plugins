using HizenLabs.Shared;

namespace HizenLabs.Plugins.TestPlugin;

[Description("Plugin for testing purposes.")]
public class TestPlugin : PluginBase
{
    [ChatCommand("test")]
    private void TestCommand(BasePlayer player, string command, string[] args)
    {
        Puts("This is a test plugin!");
    }
}
