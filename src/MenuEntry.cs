using System;
using System.Collections.Generic;
using SeaPower;
using SeapowerUI.ViewModels;

namespace QuitToDesktop;

internal sealed class MenuEntry
{
    private const string Section = "MebiusTosQuitToDesktop";
    private const string Key = "Quit";
    private readonly Action show;
    private readonly Action<QuitText> refreshDialog;
    private LanguageResourceHandler? owner;
    private PauseMenuButton? entry;

    internal MenuEntry(Action show, Action<QuitText> refreshDialog)
    { this.show = show; this.refreshDialog = refreshDialog; }

    internal List<PauseMenuButton> Extend(List<PauseMenuButton> original, LanguageResourceHandler language) =>
        MenuList.AppendOnce(original, GetOrCreate(language));

    internal PauseMenuButton GetOrCreate(LanguageResourceHandler language)
    {
        if (ReferenceEquals(owner, null))
        {
            owner = language;
            // Publish before the native button's LanguageChanged subscriber reads the new dictionary.
            language.LanguageChanged += LanguageChanged;
        }
        if (!ReferenceEquals(owner, language)) throw new InvalidOperationException("The language handler was replaced.");
        Publish();
        entry ??= new PauseMenuButton(Section, Key, new DelegateCommand(_ => Callbacks.Boundary.Run("Could not open the quit confirmation", show)), true, true);
        return entry;
    }

    private void Publish()
    {
        var language = owner ?? throw new InvalidOperationException("The language handler is unavailable.");
        var dictionary = language.LanguageDictionary ?? throw new InvalidOperationException("The language dictionary is uninitialized.");
        dictionary[(Section + Key).ToLowerInvariant()] = Localization.For(language.Language).Title;
    }

    private void LanguageChanged(object sender, EventArgs args)
    {
        Callbacks.Boundary.Run("Could not refresh the quit confirmation language", () =>
        { Publish(); refreshDialog(Localization.For(owner!.Language)); });
    }
}
