using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SeaPower;
using SeapowerUI.ViewModels;

namespace QuitToDesktop;

internal sealed class MenuEntry
{
    private readonly ConditionalWeakTable<PauseMenuButton, MenuEntry> ownedEntries = new();
    private readonly Action show;
    private readonly Action<QuitText> refreshDialog;
    private LanguageResourceHandler? owner;
    private PauseMenuButton? entry;

    internal MenuEntry(Action show, Action<QuitText> refreshDialog)
    { this.show = show; this.refreshDialog = refreshDialog; }

    internal List<PauseMenuButton> Extend(List<PauseMenuButton> original, LanguageResourceHandler language)
    {
        var current = GetOrCreate(language);
        var result = MenuList.AppendOnce(original, current);
        result.RemoveAll(item => !ReferenceEquals(item, current) && ownedEntries.TryGetValue(item, out _));
        return result;
    }

    internal PauseMenuButton GetOrCreate(LanguageResourceHandler language)
    {
        if (!ReferenceEquals(owner, language))
        {
            if (!ReferenceEquals(owner, null)) owner.LanguageChanged -= LanguageChanged;
            owner = language;
            entry = null;
            language.LanguageChanged += LanguageChanged;
        }
        if (entry == null)
        {
            var created = new PauseMenuButton(Localization.Section, "Quit", new DelegateCommand(_ => Callbacks.Boundary.Run("Could not open the quit confirmation", show)), true, true);
            ownedEntries.Add(created, this);
            entry = created;
        }
        return entry;
    }

    private void LanguageChanged(object sender, EventArgs args) =>
        Callbacks.Boundary.Run("Could not refresh the quit confirmation language", () =>
            refreshDialog(Localization.For(owner!)));
}
