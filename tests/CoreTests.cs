using System;
using System.Collections.Generic;
using QuitToDesktop;
using static Program;

internal static class CoreTests
{
    internal static void Run()
    {
        Check("opening and reopening confirmation never requests application exit", () =>
        {
            int quits = 0; var flow = new QuitFlow(() => quits++);
            long first = flow.Begin(); long second = flow.Begin();
            Require(first != 0 && first == second && quits == 0, "Opening either stacked or approved confirmation.");
        });
        Check("cancel does not quit and invalidates the old confirm action", () =>
        {
            int quits = 0; var flow = new QuitFlow(() => quits++); long token = flow.Begin();
            Require(flow.Cancel(token) && !flow.Confirm(token) && quits == 0, "Cancel allowed a stale confirmation.");
        });
        Check("explicit approval requests exit exactly once", () =>
        {
            int quits = 0; var flow = new QuitFlow(() => quits++); long token = flow.Begin();
            Require(flow.Confirm(token) && !flow.Confirm(token) && quits == 1, "Approval was lost or duplicated.");
        });
        Check("callbacks retained from an older dialog cannot approve a newer dialog", () =>
        {
            int quits = 0; var flow = new QuitFlow(() => quits++); long old = flow.Begin(); flow.Cancel(old);
            long current = flow.Begin();
            Require(!flow.Confirm(old) && !flow.Cancel(old) && flow.ActiveToken == current && quits == 0, "An older callback affected the active dialog.");
            Require(flow.Confirm(current) && quits == 1, "The current confirmation stopped working.");
        });
        Check("a failed exit request surfaces its error and cannot be retried by duplicate approval", () =>
        {
            int calls = 0; var flow = new QuitFlow(() => { calls++; throw new InvalidOperationException("test boundary failure"); });
            long token = flow.Begin(); Throws<InvalidOperationException>(() => flow.Confirm(token));
            Require(!flow.Confirm(token) && calls == 1, "An exit failure caused an automatic second exit attempt.");
        });
        Check("confirmation is required even when no dialog has ever opened", () =>
        {
            int quits = 0; var flow = new QuitFlow(() => quits++);
            Require(!flow.Confirm(0) && !flow.Confirm(1) && quits == 0, "Exit was permitted without a dialog.");
        });
        Check("native pause input is blocked in both input phases of the cancel press", () =>
        {
            var fence = new PauseInputFence(); Require(!fence.Blocks(9), "Unrelated input was blocked.");
            fence.Mark(10);
            Require(fence.Blocks(10) && fence.Blocks(11) && !fence.Blocks(12) && !fence.Pending, "The cancel press leaked or later pause input remained blocked.");
        });
        Check("rebuilding a menu preserves all native and other-mod actions in order", () =>
        {
            var resume = new object(); var exit = new object(); var other = new object(); var quit = new object();
            var original = new List<object> { resume, exit, other };
            var actual = MenuList.AppendOnce(original, quit);
            Require(original.Count == 3 && actual.Count == 4 && actual[0] == resume && actual[1] == exit && actual[2] == other && actual[3] == quit,
                "Existing actions were changed or the caller's list was mutated.");
        });
        Check("repeated population produces one quit item at the bottom", () =>
        {
            var quit = new object(); var native = new object();
            var items = MenuList.AppendOnce(new List<object> { quit, native, quit }, quit);
            items = MenuList.AppendOnce(items, quit);
            Require(items.Count == 2 && items[0] == native && items[1] == quit, "Quit items accumulated or moved native entries.");
        });
        Check("a replacement menu receives the quit item independently", () =>
        {
            var quit = new object(); var replacement = new List<object>();
            Require(MenuList.AppendOnce(replacement, quit).Count == 1 && replacement.Count == 0, "A replaced campaign menu lost the quit item.");
        });
        Check("invalid menu data is an explicit error", () =>
        {
            Throws<ArgumentNullException>(() => MenuList.AppendOnce<object>(null!, new object()));
            Throws<InvalidOperationException>(() => MenuList.AppendOnce(new List<object> { null! }, new object()));
        });
        Check("Japanese game locales select Japanese and every other locale selects English", () =>
        {
            var ja = Localization.For("ja"); var en = Localization.For("en");
            foreach (string language in new[] { "ja", "JA", "ja-JP", "ja_JP" }) Require(ReferenceEquals(ja, Localization.For(language)), "Japanese locale was not selected: " + language);
            foreach (string? language in new[] { "en", "de", "fr", "cn", "ru", "es", "ko", "javanese", "", null })
                Require(ReferenceEquals(en, Localization.For(language)), "An unsupported locale did not select English: " + language);
            Require(!ReferenceEquals(ja, en), "English and Japanese were conflated.");
        });
    }
}
