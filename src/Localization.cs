using System;

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
    private static readonly QuitText english = new("Quit Sea Power", "APPLICATION EXIT",
        "Quit Sea Power and return to the desktop?",
        "Unsaved progress will be lost. This does not save or finalize the mission.", "Cancel");
    private static readonly QuitText japanese = new("Sea Power を終了する", "アプリケーションの終了",
        "Sea Power を終了してデスクトップに戻りますか？",
        "保存していない進行状況は失われます。この操作では保存やミッション結果の確定を行いません。", "キャンセル");

    internal static QuitText For(string? language) => language != null &&
        (language.Equals("ja", StringComparison.OrdinalIgnoreCase) ||
         language.StartsWith("ja-", StringComparison.OrdinalIgnoreCase) ||
         language.StartsWith("ja_", StringComparison.OrdinalIgnoreCase)) ? japanese : english;
}
