using System;
using System.IO;
using AnchorChain;
using SeaPower;

namespace QuitToDesktop;

[ACPlugin(ModInfo.Id, ModInfo.Name, ModInfo.Version)]
public sealed class Plugin : IAnchorChainMod
{
    private static bool initialized;

    public void TriggerEntryPoint() => Callbacks.Boundary.Run("Quit to Desktop startup registration failed", () =>
    {
        if (initialized) return;
        initialized = true;
        string package = Path.GetDirectoryName(typeof(Plugin).Assembly.Location)
            ?? throw new InvalidOperationException("The mod package directory is unavailable.");
        if (!ModActivation.IsEnabled(typeof(FileManager), package))
        {
            ModLog.Info("Disabled in the mod manager; no patches installed.");
            return;
        }
        NoesisStartup.Start();
    });
}
