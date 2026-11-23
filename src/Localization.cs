using System;
using SeaPower;

namespace QuitToDesktop;

internal sealed class QuitText
{
    internal string Title { get; }
    internal string Category { get; }
    internal string Message { get; }
    internal string Warning { get; }
    internal string Cancel { get; }

    internal QuitText(string title, string category, string message, string warning, string cancel)
    { Title = title; Category = category; Message = message; Warning = warning; Cancel = cancel; }
}

internal static class Localization
{
    internal const string Section = "MebiusTosQuitToDesktop";

    internal static QuitText For(LanguageResourceHandler language)
    {
        if (language.LanguageDictionary == null)
            throw new InvalidOperationException("The language dictionary is uninitialized.");
        return new QuitText(
            language.getText(Section, "Quit"),
            language.getText(Section, "Category"),
            language.getText(Section, "Message"),
            language.getText(Section, "Warning"),
            language.getText(Section, "Cancel"));
    }
}
