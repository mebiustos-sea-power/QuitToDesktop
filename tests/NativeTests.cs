using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Path = System.IO.Path;
using EventHandler = System.EventHandler;
using EventArgs = System.EventArgs;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Noesis;
using QuitToDesktop;
using SeaPower;
using SeapowerUI;
using SeapowerUI.ViewModels;
using static Program;

internal static class NativeTests
{
    private static LanguageResourceHandler language = null!;
    private static int otherCalls;
    private static void OtherPrefix() => otherCalls++;
    private static bool FakeLanguage(ref LanguageResourceHandler __result) { __result = language; return false; }

    internal static void Run(string directory)
    {
        if (Type.GetType("Mono.Runtime") == null) throw new PlatformNotSupportedException("Use the game Mono runtime.");
        StartupTests.RunCold();
        GUI.Init();
        var fixture = new Harmony("mebiustos.quittodesktop.tests.fixture");
        try
        {
#pragma warning disable SYSLIB0050
            language = (LanguageResourceHandler)FormatterServices.GetUninitializedObject(typeof(LanguageResourceHandler));
#pragma warning restore SYSLIB0050
            Field("keyDictionary").SetValue(language, new ConcurrentDictionary<(string, string), string>());
            ChangeLanguage("en", false);
            fixture.Patch(AccessTools.PropertyGetter(typeof(Singleton<LanguageResourceHandler>), "Instance"),
                prefix: new HarmonyMethod(typeof(NativeTests), nameof(FakeLanguage)));
            Console.WriteLine("Game hook assembly: " + typeof(PauseMenuView).Assembly.Location);
            Require(typeof(PauseMenuView).Assembly.Location.Contains("game-fixture"), "The standalone process did not load its isolated Unity-boundary fixture.");
            MenuTests(directory);
            DialogTests();
            PatchTests();
        }
        finally { fixture.UnpatchSelf(); GUI.Shutdown(); }
    }

    private static FieldInfo Field(string name) => typeof(LanguageResourceHandler).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static void ChangeLanguage(string code, bool notify)
    {
        Field("_language").SetValue(language, code);
        var dict = new DictionaryWithFallbacks<string, string>();
        dict.Add("pausemenuexit", "Native mission exit"); dict.Add("pausemenusaveas", "Native save");
        Field("_languageDictionary").SetValue(language, dict);
        if (notify) ((EventHandler?)Field("LanguageChanged").GetValue(language))?.Invoke(language, EventArgs.Empty);
    }

    private static void MenuTests(string directory)
    {
        Check("the real native menu command opens confirmation without directly requesting exit", () =>
        {
            ChangeLanguage("en", false); int opens = 0;
            var entry = new MenuEntry(() => opens++, _ => { });
            var exit = new PauseMenuButton("PauseMenu", "Exit", new DelegateCommand(_ => { }), true, true);
            var original = new List<PauseMenuButton> { exit };
            var first = entry.Extend(original, language);
            first[1].ButtonCommand.Execute(null);
            var second = entry.Extend(first, language);
            Require(opens == 1 && second.Count == 2 && ReferenceEquals(first[1], second[1]) && original.Count == 1 && ReferenceEquals(second[0], exit),
                "The native menu lost, duplicated, or bypassed its confirmation action.");
        });
        Check("language dictionary reload updates the same native button for Japanese and unsupported locales", () =>
        {
            ChangeLanguage("en", false);
            var entry = new MenuEntry(() => { }, _ => { });
            var button = entry.Extend(new List<PauseMenuButton>(), language)[0];
            foreach (string code in new[] { "ja", "de", "fr", "en" })
            {
                ChangeLanguage(code, true);
                Require(button.ButtonContent == Localization.For(code).Title && !button.ButtonContent.Contains("MISSING TEXT"), "The native button label did not follow the replacement language dictionary.");
            }
        });
        string campaign = Path.GetFullPath(Path.Combine(directory, "..", "..", "workshop", "content", "1286220", "3813157776", "DynamicCampaignMod.dll"));
        if (!File.Exists(campaign)) { Console.WriteLine("SKIP optional installed campaign filter: Dynamic Campaign Mod is not installed."); return; }
        Check("the installed campaign's actual battle-menu filter retains the new quit action", () =>
        {
            var assembly = Assembly.LoadFrom(campaign);
            var closure = assembly.GetType("DynamicCampaignMod.Game.Patches.BattlePauseMenuPatch+<>c", true)!;
            var filter = closure.GetMethod("<Postfix>b__1_0", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object instance = Activator.CreateInstance(closure, true)!;
            var entry = new MenuEntry(() => { }, _ => { });
            var quit = entry.Extend(new List<PauseMenuButton>(), language)[0];
            var save = new PauseMenuButton("PauseMenu", "SaveAs", new DelegateCommand(_ => { }), true, true);
            Require((bool)filter.Invoke(instance, new object[] { quit })! && !(bool)filter.Invoke(instance, new object[] { save })!,
                "The actual campaign filter discarded Quit to Desktop or the fixture did not exercise filtering.");
        });
    }

    private static void DialogTests()
    {
        Check("native confirmation overlay blocks background clicks and starts on Cancel", () =>
        {
            using var ui = new UiFixture();
            ui.Mouse(40, 40); Require(ui.BackgroundClicks == 1, "The background probe was not clickable before the dialog.");
            ui.Open(); ui.Mouse(40, 40);
            Require(ui.BackgroundClicks == 1 && ui.Coordinator.IsOpen && ui.Quits == 0 && ui.Button("CancelQuit").IsKeyboardFocused,
                "The modal shield leaked mouse input, quit early, or focused the destructive action.");
        });
        Check("Enter on the initially focused Cancel closes the native dialog without quitting", () =>
        {
            using var ui = new UiFixture(); ui.Open(); ui.Key(Key.Return);
            Require(!ui.Coordinator.IsOpen && ui.Quits == 0, "The opening/default Enter key approved application exit.");
        });
        Check("keyboard navigation and explicit approval quit once through the native dialog", () =>
        {
            using var ui = new UiFixture(); ui.Open(); ui.Key(Key.Tab);
            Require(ui.Button("ConfirmQuit").IsKeyboardFocused, "Tab did not stay in the dialog and select Quit.");
            ui.Key(Key.Return);
            Require(!ui.Coordinator.IsOpen && ui.Quits == 1, "Explicit keyboard approval did not request exactly one quit.");
        });
        Check("the native cancel button dismisses confirmation and preserves original exit commands", () =>
        {
            using var ui = new UiFixture();
            var model = (PauseMenuViewModel)ui.Menu.DataContext;
            var accept = new DelegateCommand(_ => { }); var cancel = new DelegateCommand(_ => { });
            model.AcceptExit = accept; model.CancelExit = cancel; model.ConfirmExitMessage = "native message";
            ui.Open(); ui.Click("CancelQuit");
            Require(!ui.Coordinator.IsOpen && ui.Quits == 0 && ReferenceEquals(model.AcceptExit, accept) && ReferenceEquals(model.CancelExit, cancel)
                && model.ConfirmExitMessage == "native message" && !model.ConfirmExitVisible, "The new dialog replaced the mission confirmation state.");
        });
        Check("Esc cancels only the new dialog and fences the game's matching pause input", () =>
        {
            using var ui = new UiFixture(); ui.Open(); ui.Key(Key.Escape);
            Require(!ui.Coordinator.IsOpen && ui.Quits == 0 && ui.Coordinator.ConsumePauseToggle(), "Esc leaked to the native pause toggle.");
            ui.Frame++; Require(ui.Coordinator.ConsumePauseToggle(), "The other input phase leaked the cancel key.");
            ui.Frame++; Require(!ui.Coordinator.ConsumePauseToggle(), "A later deliberate pause press was blocked.");
        });
        Check("Unity's pause callback can dismiss the dialog before Noesis sees Esc", () =>
        {
            using var ui = new UiFixture(); ui.Open();
            Require(ui.Coordinator.ConsumePauseToggle() && !ui.Coordinator.IsOpen && ui.Quits == 0, "The Unity-first event order resumed or quit the game.");
            ui.Key(Key.Escape); Require(ui.Quits == 0, "The later native Esc event requested quit.");
        });
        Check("repeated open requests do not stack dialogs or move focus to Quit", () =>
        {
            using var ui = new UiFixture(); ui.Open(); var first = ui.Coordinator.Dialog;
            ui.Coordinator.Show(); ui.Update();
            Require(ReferenceEquals(first, ui.Coordinator.Dialog) && ui.Overlay.Children.Count == 2 && ui.Button("CancelQuit").IsKeyboardFocused,
                "Reopening stacked a dialog or armed the Quit button.");
        });
        Check("navigation invalidates retained confirmation callbacks", () =>
        {
            using var ui = new UiFixture(); ui.Open(); var button = ui.Button("ConfirmQuit");
            ui.Current = null; ui.Coordinator.CloseIfLeaving(new object());
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
            Require(!ui.Coordinator.IsOpen && ui.Quits == 0, "A callback retained after navigation still quit.");
        });
        Check("native unloading invalidates the confirmation", () =>
        {
            using var ui = new UiFixture(); ui.Open();
            ui.Menu.Content = null; ui.Update();
            Require(!ui.Coordinator.IsOpen && ui.Quits == 0, "Unloading left a live confirmation.");
        });
        Check("an existing native mission confirmation cannot be covered by a second confirmation", () =>
        {
            using var ui = new UiFixture(); ((PauseMenuViewModel)ui.Menu.DataContext).ConfirmExitVisible = true;
            Throws<InvalidOperationException>(ui.Coordinator.Show);
            Require(!ui.Coordinator.IsOpen && ui.Quits == 0, "A second confirmation covered the native one.");
        });
        Check("dialog labels switch together between Japanese and English", () =>
        {
            using var ui = new UiFixture(); ui.Open();
            foreach (string code in new[] { "ja", "ko", "en" })
            {
                var text = Localization.For(code); ui.Coordinator.RefreshText(text); ui.Update();
                var root = ui.Coordinator.Dialog!.Root;
                Require(((TextBlock)root.FindName("DialogTitle")).Text == text.Title &&
                    ((TextBlock)root.FindName("DialogWarning")).Text == text.Warning &&
                    (string)ui.Button("ConfirmQuit").Content == text.Title && (string)ui.Button("CancelQuit").Content == text.Cancel,
                    "The title, warning or action buttons retained the previous language.");
            }
        });
    }

    private static void PatchTests()
    {
        Check("actual game hooks extend menu assignment and roll back without removing other mods", () =>
        {
            var ours = new Harmony("mebiustos.quittodesktop.tests.product");
            var other = new Harmony("mebiustos.quittodesktop.tests.other");
            var setter = AccessTools.PropertySetter(typeof(PauseMenuViewModel), "PauseMenuButtons");
            try
            {
                other.Patch(setter, prefix: new HarmonyMethod(typeof(NativeTests), nameof(OtherPrefix)));
                GamePatches.Install(ours);
                var model = new PauseMenuViewModel();
                model.PauseMenuButtons = new List<PauseMenuButton>();
                Require(model.PauseMenuButtons.Count == 1 && otherCalls > 0, "The actual property hook did not populate the menu alongside another patch.");
                var first = model.PauseMenuButtons[0]; model.PauseMenuButtons = model.PauseMenuButtons;
                Require(model.PauseMenuButtons.Count == 1 && ReferenceEquals(first, model.PauseMenuButtons[0]), "The actual hook duplicated its menu item.");
                foreach (var target in new[] { setter, AccessTools.Method(typeof(MenuSystemView), "MenuSystem_PreviewKeyDown"),
                    AccessTools.Method(typeof(CancelAction), "cancel"), AccessTools.Method(typeof(CampaignMain), "PauseMenu_performed"),
                    AccessTools.Method(typeof(CampaignMain).Assembly.GetType("SeaPower.LinearCampaign", true), "PauseMenu_performed"),
                    AccessTools.PropertySetter(typeof(MenuSystemViewModel), "CurrentWindow") })
                    Require(Harmony.GetPatchInfo(target)!.Owners.Contains(ours.Id), "A required game hook was not installed.");
                ours.UnpatchSelf();
                Require(Harmony.GetPatchInfo(setter)!.Owners.Contains(other.Id) && !Harmony.GetPatchInfo(setter)!.Owners.Contains(ours.Id), "Rollback removed another mod's patch.");
                model.PauseMenuButtons = new List<PauseMenuButton>();
                Require(model.PauseMenuButtons.Count == 0, "Rollback left the menu injection active.");
            }
            finally { ours.UnpatchSelf(); other.UnpatchSelf(); }
        });
    }

    private sealed class UiFixture : IDisposable
    {
        internal PauseMenuView Menu { get; }
        internal PauseMenuView? Current;
        internal Grid Overlay { get; } = new();
        internal QuitCoordinator Coordinator { get; }
        internal int Frame, Quits, BackgroundClicks;
        private readonly View view;
        private double time;

        internal UiFixture()
        {
            Menu = new PauseMenuView { DataContext = new PauseMenuViewModel() };
            Current = Menu;
            var template = (ControlTemplate)GUI.ParseXaml("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:local='clr-namespace:SeapowerUI;assembly=Seapower-Scripts' TargetType='{x:Type local:MenuPage}' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><ContentPresenter Content='{TemplateBinding Overlay}'/></ControlTemplate>");
            Menu.Content = new MenuPage { Overlay = Overlay, Template = template };
            var background = new Button { Width = 100, Height = 70, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Template = (ControlTemplate)GUI.ParseXaml("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='#345678'/></ControlTemplate>") };
            background.Click += (_, _) => BackgroundClicks++;
            Overlay.Children.Add(background);
            Coordinator = new QuitCoordinator(() => Current, () => Localization.For("en"), () => Frame, () => Quits++);
            Menu.PreviewKeyDown += (_, e) => { if (Coordinator.IsOpen) { e.Handled = true; Coordinator.HandleKey(e.Key, e.IsRepeat); } };
            view = GUI.CreateView(Menu); view.SetSize(1280, 720); Update();
        }
        internal void Update() { time += 0.1; view.Update(time); }
        internal void Open() { Coordinator.Show(); Update(); }
        internal Button Button(string name) => (Button)Coordinator.Dialog!.Root.FindName(name);
        internal void Click(string name) { var button = Button(name); button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button)); Update(); }
        internal void Key(Key key) { view.KeyDown(key); view.KeyUp(key); Update(); }
        internal void Mouse(int x, int y) { view.MouseMove(x, y); view.MouseButtonDown(x, y, MouseButton.Left); view.MouseButtonUp(x, y, MouseButton.Left); Update(); }
        public void Dispose() { Current = null; Coordinator.CloseIfLeaving(new object()); view.Dispose(); }
    }
}
