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
    private static FileManager files = null!;
    private static int otherCalls;
    private static bool FakeFiles(ref FileManager __result) { __result = files; return false; }
    private static bool FindLanguageFiles(string __0, string __1, ref List<string> __result)
    {
        string root = Path.GetDirectoryName(typeof(NativeTests).Assembly.Location)!;
        var result = new List<string>();
        foreach (string folder in new[] { "Translations", "Package" })
        {
            string path = Path.Combine(root, folder, __0);
            if (Directory.Exists(path)) result.AddRange(Directory.GetFiles(path, __1));
        }
        __result = result;
        return false;
    }
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
            files = (FileManager)FormatterServices.GetUninitializedObject(typeof(FileManager));
#pragma warning restore SYSLIB0050
            fixture.Patch(AccessTools.PropertyGetter(typeof(Singleton<FileManager>), "Instance"),
                prefix: new HarmonyMethod(typeof(NativeTests), nameof(FakeFiles)));
            fixture.Patch(AccessTools.Method(typeof(FileManager), "GetAllFiles"),
                prefix: new HarmonyMethod(typeof(NativeTests), nameof(FindLanguageFiles)));
            // The standalone fixture has no published file layers or INI modifier registry.
            AccessTools.Field(typeof(IniHandler), "_modifierScanComplete").SetValue(null, true);
            language = CreateLanguageHandler();
            ChangeLanguage("en", false);
            fixture.Patch(AccessTools.PropertyGetter(typeof(Singleton<LanguageResourceHandler>), "Instance"),
                prefix: new HarmonyMethod(typeof(NativeTests), nameof(FakeLanguage)));
            Console.WriteLine("Game hook assembly: " + typeof(PauseMenuView).Assembly.Location);
            Require(typeof(PauseMenuView).Assembly.Location.Contains("game-fixture"), "The standalone process did not load its isolated Unity-boundary fixture.");
            LanguageTests();
            MenuTests(directory);
            DialogTests();
            PatchTests();
        }
        finally { fixture.UnpatchSelf(); GUI.Shutdown(); }
    }

    private static LanguageResourceHandler CreateLanguageHandler()
    {
#pragma warning disable SYSLIB0050
        var handler = (LanguageResourceHandler)FormatterServices.GetUninitializedObject(typeof(LanguageResourceHandler));
#pragma warning restore SYSLIB0050
        Field("keyDictionary").SetValue(handler, new ConcurrentDictionary<(string, string), string>());
        Field("skipFiles").SetValue(handler, Array.Empty<string>());
        return handler;
    }

    private static FieldInfo Field(string name) => typeof(LanguageResourceHandler).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static void ChangeLanguage(string code, bool notify)
    {
        if (notify) language.Language = code;
        else InitializeLanguage(language, code);
    }

    private static void InitializeLanguage(LanguageResourceHandler handler, string code)
    {
        Field("_language").SetValue(handler, code);
        AccessTools.Method(typeof(LanguageResourceHandler), "initLanguages").Invoke(handler, null);
    }

    private static QuitText TextFor(string code)
    {
        var handler = CreateLanguageHandler();
        InitializeLanguage(handler, code);
        return Localization.For(handler);
    }

    private static bool SameText(QuitText? first, QuitText second) => first != null &&
        first.Title == second.Title && first.Category == second.Category && first.Message == second.Message &&
        first.Warning == second.Warning && first.Cancel == second.Cancel;

    private static void LanguageTests()
    {
        Check("native language loading reads packaged translations, extra languages and per-key English fallback", () =>
        {
            var english = TextFor("en");
            var japanese = TextFor("ja");
            Require(english.Title == "Quit Sea Power" && japanese.Title == "Sea Power を終了する" &&
                english.Category != japanese.Category && english.Message != japanese.Message &&
                english.Warning != japanese.Warning && english.Cancel != japanese.Cancel,
                "The native loader did not read both packaged languages.");
            foreach (string code in new[] { "de", "ko", "ru", "cn" })
                Require(SameText(english, TextFor(code)), "An untranslated language lost English fallback: " + code);
            var additional = TextFor("fr");
            Require(additional.Title == "Custom test quit" && additional.Message == "First test line\nSecond test line" &&
                additional.Category == english.Category && additional.Warning == english.Warning && additional.Cancel == english.Cancel,
                "An external partial translation was ignored, lost escaped newlines, or failed per-key fallback.");
            Throws<InvalidOperationException>(() => Localization.For(CreateLanguageHandler()));
        });
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
                Require(button.ButtonContent == TextFor(code).Title && !button.ButtonContent.Contains("MISSING TEXT"), "The native button label did not follow the replacement language dictionary.");
            }
        });
        Check("recreating the language handler restores a translated quit entry and releases the old language subscription", () =>
        {
            var originalLanguage = language;
            int opens = 0, refreshes = 0;
            QuitText? refreshedText = null;
            var entry = new MenuEntry(() => opens++, text => { refreshes++; refreshedText = text; });
            var previousButton = entry.GetOrCreate(language);
            var native = new PauseMenuButton("PauseMenu", "Exit", new DelegateCommand(_ => { }), true, true);
            var foreign = new PauseMenuButton("MebiusTosQuitToDesktop", "Quit", previousButton.ButtonCommand, true, true);
            var menu = entry.Extend(new List<PauseMenuButton> { native, foreign }, language);
            try
            {
                foreach (string code in new[] { "ja", "de", "en" })
                {
                    var retiredLanguage = language;
                    language = CreateLanguageHandler();
                    ChangeLanguage(code, false);
                    var button = entry.GetOrCreate(language);
                    Require(!ReferenceEquals(previousButton, button) && button.ButtonContent == TextFor(code).Title,
                        "A new language handler did not receive its own translated quit button.");

                    var previousMenu = menu;
                    menu = entry.Extend(previousMenu, language);
                    menu = entry.Extend(menu, language);
                    Require(menu.Count == 3 && ReferenceEquals(menu[0], native) && ReferenceEquals(menu[1], foreign) &&
                        ReferenceEquals(menu[2], button) && ReferenceEquals(entry.GetOrCreate(language), button) &&
                        !menu.Contains(previousButton) && previousMenu.Count == 3 && ReferenceEquals(previousMenu[2], previousButton),
                        "A retained menu kept an old Quit, changed another action, or mutated its previous list.");
                    int previousOpens = opens;
                    button.ButtonCommand.Execute(null);
                    Require(opens == previousOpens + 1, "The recreated quit button no longer invokes confirmation.");

                    int previousRefreshes = refreshes;
                    ((EventHandler?)Field("LanguageChanged").GetValue(retiredLanguage))?.Invoke(retiredLanguage, EventArgs.Empty);
                    Require(refreshes == previousRefreshes && button.ButtonContent == TextFor(code).Title,
                        "A retired language handler still updates the current quit UI.");

                    ChangeLanguage("ja", true);
                    Require(button.ButtonContent == TextFor("ja").Title && refreshes == previousRefreshes + 1 &&
                        SameText(refreshedText, TextFor("ja")),
                        "The replacement handler did not refresh Japanese menu and confirmation text exactly once.");
                    ChangeLanguage("de", true);
                    Require(button.ButtonContent == TextFor("en").Title && refreshes == previousRefreshes + 2 &&
                        SameText(refreshedText, TextFor("en")),
                        "The replacement handler lost English fallback or duplicated its language subscription.");
                    previousButton = button;
                }
            }
            finally { language = originalLanguage; }
        });

        Check("only the quit entry is compact and right-aligned, while neighboring mod actions and scrolling remain usable", () =>
        {
            bool Same(Color first, Color second) => first.A == second.A && first.R == second.R && first.G == second.G && first.B == second.B;
            ChangeLanguage("en", false);
            int opens = 0, nativeClicks = 0, beforeClicks = 0, afterClicks = 0;
            var entry = new MenuEntry(() => opens++, _ => { });
            var quit = entry.GetOrCreate(language);
            var native = new PauseMenuButton("PauseMenu", "Exit", new DelegateCommand(_ => nativeClicks++), true, true);
            var sameLabel = new PauseMenuButton("MebiusTosQuitToDesktop", "Quit", new DelegateCommand(_ => beforeClicks++), true, true);
            var following = new PauseMenuButton("PauseMenu", "SaveAs", new DelegateCommand(_ => afterClicks++), true, true);
            var menu = new PauseMenuView { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
            var original = (Style)GUI.ParseXaml("<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Setter Property='Background' Value='#3F4A4F'/><Setter Property='Foreground' Value='#D8D8D8'/><Setter Property='Padding' Value='8,7'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border Background='{TemplateBinding Background}'><ContentPresenter Margin='{TemplateBinding Padding}'/></Border></ControlTemplate></Setter.Value></Setter></Style>");
            menu.Resources[nameof(Button)] = original;
            var rows = new ItemsControl
            {
                ItemsSource = new[] { native, sameLabel, quit, following },
                ItemTemplate = (DataTemplate)GUI.ParseXaml("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Button Content='{Binding ButtonContent}' Command='{Binding ButtonCommand}' Height='40' Margin='0,0,0,15'/></DataTemplate>")
            };
            // The standalone fixture does not load the game's theme; provide its scrolling viewport explicitly.
            var scroll = new ScrollViewer
            {
                Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Template = (ControlTemplate)GUI.ParseXaml("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollViewer'><ScrollContentPresenter x:Name='PART_ScrollContentPresenter' Content='{TemplateBinding Content}'/></ControlTemplate>")
            };
            menu.Content = new MenuPage
            {
                ShortLeftPane = scroll,
                Template = (ControlTemplate)GUI.ParseXaml("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' xmlns:local='clr-namespace:SeapowerUI;assembly=Seapower-Scripts' TargetType='{x:Type local:MenuPage}'><ContentPresenter Content='{TemplateBinding ShortLeftPane}'/></ControlTemplate>")
            };
            Button RowButton(int index) => (Button)VisualTreeHelper.GetChild(rows.ItemContainerGenerator.ContainerFromIndex(index), 0);
            Rect Bounds(Button button) => button.TransformToAncestor(rows).TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
            float Gap(Button upper, Button lower) { var above = Bounds(upper); return Bounds(lower).Y - above.Y - above.Height; }
            void RequireCompact(Button full, Button compact)
            {
                var baseline = Bounds(full); var actual = Bounds(compact);
                Require(actual.Width > 0 && actual.X > baseline.X + baseline.Width / 2 &&
                    Math.Abs(actual.X + actual.Width - baseline.X - baseline.Width) < 0.1f && actual.Height == baseline.Height,
                    $"Quit must fit its label on the right, clear of the original button's center, while retaining its height. Full={baseline.X},{baseline.Y},{baseline.Width},{baseline.Height}; quit={actual.X},{actual.Y},{actual.Width},{actual.Height}; alignment={compact.HorizontalAlignment}; menu={menu.ActualWidth}; page={((MenuPage)menu.Content).ActualWidth}; scroll={scroll.ActualWidth}/{scroll.ViewportWidth}; items={rows.ActualWidth}.");
            }
            MenuAccent.Apply(menu, quit);
            var applied = menu.Resources[nameof(Button)];
            MenuAccent.Apply(menu, quit);
            var view = GUI.CreateView(menu);
            try
            {
                view.SetSize(640, 480); view.Update(0);
                var nativeButton = RowButton(0); var otherButton = RowButton(1); var quitButton = RowButton(2); var followingButton = RowButton(3);
                float ordinaryGap = Gap(nativeButton, otherButton), quitGap = Gap(otherButton, quitButton);
                RequireCompact(nativeButton, quitButton);
                Require(ordinaryGap > 0 && quitGap > ordinaryGap * 2 && Gap(quitButton, followingButton) == ordinaryGap &&
                    Bounds(otherButton).X == Bounds(nativeButton).X && otherButton.ActualWidth == nativeButton.ActualWidth &&
                    Bounds(followingButton).X == Bounds(nativeButton).X && followingButton.ActualWidth == nativeButton.ActualWidth,
                    "The extra separation must belong only to Quit; actions before and after it must retain their full width and normal spacing.");
                void ClickAt(float x, float y)
                {
                    view.MouseMove((int)x, (int)y); view.MouseButtonDown((int)x, (int)y, MouseButton.Left); view.MouseButtonUp((int)x, (int)y, MouseButton.Left);
                }
                void Click(Button button)
                {
                    var bounds = button.TransformToAncestor(menu).TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
                    ClickAt(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                }
                var normalBounds = Bounds(nativeButton); var quitBounds = Bounds(quitButton);
                ClickAt(normalBounds.X + normalBounds.Width / 2, quitBounds.Y + quitBounds.Height / 2);
                Require(opens == 0, "A downward miss from the full-width action's center clicked Quit.");
                Click(nativeButton); Click(otherButton); Click(followingButton); Click(quitButton);
                Require(nativeClicks == 1 && beforeClicks == 1 && afterClicks == 1 && opens == 1,
                    "Changing the quit layout broke a neighboring action or the quit command's actual hit area.");
                using var dialog = new UiFixture(); dialog.Open();
                Color background = ((SolidColorBrush)dialog.Button("ConfirmQuit").Background).Color;
                Color foreground = ((SolidColorBrush)dialog.Button("ConfirmQuit").Foreground).Color;
                Require(Same(((SolidColorBrush)quitButton.Background).Color, background) && Same(((SolidColorBrush)quitButton.Foreground).Color, foreground),
                    "The added entry does not match the confirmation action's colors: " + quitButton.Background + "/" + quitButton.Foreground + " expected " + background + "/" + foreground);
                Require(Same(((SolidColorBrush)nativeButton.Background).Color, Color.FromRgb(0x3F, 0x4A, 0x4F)) &&
                    Same(((SolidColorBrush)otherButton.Background).Color, ((SolidColorBrush)nativeButton.Background).Color) &&
                    Same(((SolidColorBrush)followingButton.Background).Color, ((SolidColorBrush)nativeButton.Background).Color),
                    "The accent changed a native entry or an unrelated action with the same label.");
                Require(ReferenceEquals(menu.Resources[nameof(Button)], applied) && ((Style)applied).BasedOn.Equals(original) &&
                    ReferenceEquals(quitButton.Template, nativeButton.Template), "Styling was stacked or the existing button template was replaced.");
                ChangeLanguage("ja", true); view.Update(0.1);
                RequireCompact(nativeButton, quitButton);
                Require(Same(((SolidColorBrush)quitButton.Background).Color, background) && Gap(otherButton, quitButton) == quitGap,
                    "A language change removed the accent or spacing.");
                var quitRow = (ContentPresenter)rows.ItemContainerGenerator.ContainerFromIndex(2);
                quitRow.DataContext = native; view.Update(0.2);
                Require(Same(((SolidColorBrush)quitButton.Background).Color, ((SolidColorBrush)nativeButton.Background).Color) &&
                    Gap(otherButton, quitButton) == ordinaryGap && quitButton.ActualWidth == nativeButton.ActualWidth && Bounds(quitButton).X == Bounds(nativeButton).X,
                    "A recycled native row retained the quit appearance or spacing.");
                quitRow.DataContext = quit; view.Update(0.3);
                RequireCompact(nativeButton, quitButton);
                Require(Same(((SolidColorBrush)quitButton.Background).Color, background) && Gap(otherButton, quitButton) == quitGap,
                    "A rebuilt quit row lost its accent or spacing.");
                view.SetSize(360, 180); view.Update(0.4);
                RequireCompact(nativeButton, quitButton);
                Require(scroll.ScrollableHeight > 0, "The fixture did not exercise an overflowing menu.");
                scroll.ScrollToBottom(); view.Update(0.5);
                Require(scroll.VerticalOffset > 0 && Gap(quitButton, followingButton) == ordinaryGap,
                    "The menu stopped scrolling or a following action was displaced.");

                var originalLanguage = language;
                double reloadTime = 0.6;
                try
                {
                    foreach (string code in new[] { "ja", "de", "en" })
                    {
                        language = CreateLanguageHandler();
                        ChangeLanguage(code, false);
                        quit = entry.GetOrCreate(language);
                        rows.ItemsSource = new[] { native, sameLabel, quit, following };
                        MenuAccent.Apply(menu, quit);
                        view.SetSize(640, 480); view.Update(reloadTime);
                        nativeButton = RowButton(0); otherButton = RowButton(1);
                        quitButton = RowButton(2); followingButton = RowButton(3);
                        RequireCompact(nativeButton, quitButton);
                        Require(Same(((SolidColorBrush)quitButton.Background).Color, background) &&
                            Gap(otherButton, quitButton) == quitGap && Gap(quitButton, followingButton) == ordinaryGap &&
                            Same(((SolidColorBrush)otherButton.Background).Color, ((SolidColorBrush)nativeButton.Background).Color),
                            "A reused view lost the current Quit appearance or changed another row.");

                        var currentStyle = (Style)menu.Resources[nameof(Button)];
                        Require(currentStyle.BasedOn.Equals(original) && rows.ItemContainerStyle.BasedOn == null,
                            "Rebinding Quit stacked its styles on previous applications.");
                        MenuAccent.Apply(menu, quit);
                        Require(ReferenceEquals(menu.Resources[nameof(Button)], currentStyle),
                            "Reapplying the same Quit changed its already prepared style.");
                        view.SetSize(360, 180); view.Update(reloadTime + 0.1);
                        scroll.ScrollToBottom(); view.Update(reloadTime + 0.2);
                        RequireCompact(nativeButton, quitButton);
                        Require(scroll.ScrollableHeight > 0 && scroll.VerticalOffset > 0,
                            "The reused pause menu stopped scrolling.");

                        ChangeLanguage("ja", true); view.Update(reloadTime + 0.3);
                        Require(quitButton.Content?.ToString() == TextFor("ja").Title &&
                            Same(((SolidColorBrush)quitButton.Background).Color, background),
                            "The reused styled entry stopped following language changes.");
                        reloadTime += 0.4;
                    }
                }
                finally { language = originalLanguage; }
            }
            finally { view.Dispose(); }
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
            using var ui = new UiFixture();
            var entry = new MenuEntry(() => { }, ui.Coordinator.RefreshText);
            entry.GetOrCreate(language);
            ui.Open();
            foreach (string code in new[] { "ja", "fr", "ko", "en" })
            {
                ChangeLanguage(code, true);
                var text = TextFor(code); ui.Update();
                var root = ui.Coordinator.Dialog!.Root;
                Require(((TextBlock)root.FindName("DialogTitle")).Text == text.Title &&
                    ((TextBlock)root.FindName("DialogCategory")).Text == text.Category &&
                    ((TextBlock)root.FindName("DialogMessage")).Text == text.Message &&
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
                var originalLanguage = language;
                try
                {
                    language = CreateLanguageHandler();
                    ChangeLanguage("ja", false);
                    var current = Runtime.Entry.GetOrCreate(language);
                    var previousMenu = model.PauseMenuButtons;
                    model.PauseMenuButtons = previousMenu;
                    Require(model.PauseMenuButtons.Count == 1 && ReferenceEquals(model.PauseMenuButtons[0], current) &&
                        previousMenu.Count == 1 && ReferenceEquals(previousMenu[0], first),
                        "The actual hook duplicated Quit in a retained menu or changed the previous list.");
                    var rebuilt = new PauseMenuViewModel();
                    var native = new PauseMenuButton("PauseMenu", "Exit", new DelegateCommand(_ => { }), true, true);
                    rebuilt.PauseMenuButtons = new List<PauseMenuButton> { native };
                    Require(rebuilt.PauseMenuButtons.Count == 2 && ReferenceEquals(rebuilt.PauseMenuButtons[0], native) &&
                        !ReferenceEquals(rebuilt.PauseMenuButtons[1], first) &&
                        rebuilt.PauseMenuButtons[1].ButtonContent == TextFor("ja").Title,
                        "The actual menu hook did not recover after the game recreated its language handler.");
                    ChangeLanguage("de", true);
                    Require(rebuilt.PauseMenuButtons[1].ButtonContent == TextFor("en").Title,
                        "The injected button stopped following language changes after a scene reload.");
                }
                finally { language = originalLanguage; }

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
            Coordinator = new QuitCoordinator(() => Current, () => Localization.For(language), () => Frame, () => Quits++);
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
