using System;
using System.IO;
using QuitToDesktop;
using static Program;

internal static class ActivationTests
{
    private sealed class Entry
    {
        public DirectoryInfo DirectoryInfo { get; set; } = null!;
        public bool IsChecked { get; set; }
    }
    private sealed class Manager
    {
        public static Manager Instance { get; } = new();
        public Entry[] Directories { get; set; } = Array.Empty<Entry>();
    }

    internal static void Run()
    {
        string folder = Path.Combine(Path.GetTempPath(), "QuitToDesktop-activation-test");
        var entry = new Entry { DirectoryInfo = new DirectoryInfo(folder), IsChecked = true };
        Manager.Instance.Directories = new[] { entry };
        Check("enabled registered package permits the quit-menu extension", () =>
            Require(ModActivation.IsEnabled(typeof(Manager), folder), "The enabled package was ignored."));
        Check("unchecked package does not install patches even when Anchor Chain discovers its DLL", () =>
        {
            entry.IsChecked = false;
            Require(!ModActivation.IsEnabled(typeof(Manager), folder), "The unchecked package was enabled.");
        });
        Check("activation matches the installed package path rather than another checked mod", () =>
        {
            Manager.Instance.Directories = new[] { new Entry { DirectoryInfo = new DirectoryInfo(folder + "-other"), IsChecked = true }, entry };
            Require(!ModActivation.IsEnabled(typeof(Manager), folder), "Another checked folder activated this mod.");
            Throws<InvalidOperationException>(() => ModActivation.IsEnabled(typeof(Manager), folder + "-missing"));
        });
        Check("missing activation API is reported as an incompatibility", () =>
            Throws<MissingMemberException>(() => ModActivation.IsEnabled(typeof(object), folder)));
    }
}
