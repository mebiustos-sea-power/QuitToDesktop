using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Noesis;
using SeaPower;
using SeapowerUI;
using SeapowerUI.ViewModels;

namespace QuitToDesktop;

internal static class Runtime
{
    internal static readonly QuitCoordinator Confirmation = new(
        () => MenuSystemViewModel.Instance.CurrentWindow as PauseMenuView,
        () => Localization.For(LanguageResourceHandler.Instance.Language),
        () => UnityEngine.Time.frameCount, UnityEngine.Application.Quit);
    internal static readonly MenuEntry Entry = new(Confirmation.Show, Confirmation.RefreshText);
}

internal static class GamePatches
{
    internal static void Install(Harmony patches)
    {
        Patch(patches, AccessTools.PropertySetter(typeof(PauseMenuViewModel), "PauseMenuButtons"), nameof(BeforeButtons));
        Patch(patches, AccessTools.Method(typeof(MenuSystemView), "MenuSystem_PreviewKeyDown"), nameof(BeforeMenuKey));
        Patch(patches, AccessTools.Method(typeof(CancelAction), "cancel"), nameof(BeforePauseToggle));
        Patch(patches, AccessTools.Method(typeof(CampaignMain), "PauseMenu_performed"), nameof(BeforePauseToggle));
        Patch(patches, AccessTools.Method(typeof(CampaignMain).Assembly.GetType("SeaPower.LinearCampaign", true), "PauseMenu_performed"), nameof(BeforePauseToggle));
        Patch(patches, AccessTools.PropertySetter(typeof(MenuSystemViewModel), "CurrentWindow"), nameof(BeforeCurrentWindow));
    }

    private static void Patch(Harmony patches, MethodInfo? target, string prefix)
    {
        if (target == null) throw new MissingMethodException("A required game UI method is missing: " + prefix);
        patches.Patch(target, prefix: new HarmonyMethod(typeof(GamePatches), prefix));
    }

    private static void BeforeButtons(ref List<PauseMenuButton> __0)
    {
        var original = __0;
        __0 = Callbacks.Boundary.Protect("Could not add the Quit to Desktop menu entry",
            () => Runtime.Entry.Extend(original, LanguageResourceHandler.Instance), original);
    }

    private static bool BeforeMenuKey(KeyEventArgs __1) => Callbacks.Boundary.Protect("Quit confirmation keyboard handling failed", () =>
    {
        if (!Runtime.Confirmation.IsOpen) return true;
        __1.Handled = true;
        Runtime.Confirmation.HandleKey(__1.Key, __1.IsRepeat);
        return false;
    }, false);

    private static bool BeforePauseToggle() => Callbacks.Boundary.Protect(
        "Could not process the pause key while confirming application exit", () => !Runtime.Confirmation.ConsumePauseToggle(), false);

    private static void BeforeCurrentWindow(object __0) => Callbacks.Boundary.Run(
        "Could not prepare Quit to Desktop during navigation", () =>
        {
            Runtime.Confirmation.CloseIfLeaving(__0);
            if (__0 is PauseMenuView menu)
                MenuAccent.Apply(menu, Runtime.Entry.GetOrCreate(LanguageResourceHandler.Instance));
        });
}
