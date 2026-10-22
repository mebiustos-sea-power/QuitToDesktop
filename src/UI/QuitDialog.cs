using System;
using System.IO;
using System.Text;
using Noesis;
using SeapowerUI;

namespace QuitToDesktop;

internal sealed class QuitDialog
{
    internal PauseMenuView Menu { get; }
    internal Grid Root { get; }
    private readonly Grid parent;
    private readonly Button cancel;
    private readonly Button quit;
    private readonly Border card;
    private readonly UIElement? previousFocus;
    private readonly Action invalidated;
    private bool closed;
    internal bool IsAttached => !closed && parent.Children.Contains(Root);
    internal bool QuitHasFocus => quit.IsKeyboardFocused;

    internal QuitDialog(PauseMenuView menu, QuitText text, Action confirm, Action cancelAction, Action invalidated)
    {
        Menu = menu;
        this.invalidated = invalidated;
        var page = menu.Content as MenuPage ?? throw new InvalidOperationException("The pause menu has no MenuPage.");
        parent = page.Overlay as Grid ?? throw new InvalidOperationException("The pause menu overlay is not a Grid.");
        using var source = typeof(QuitDialog).Assembly.GetManifestResourceStream("QuitToDesktop.QuitDialog.xaml")
            ?? throw new FileNotFoundException("The embedded quit confirmation is missing.");
        using var reader = new StreamReader(source, Encoding.UTF8);
        Root = GUI.ParseXaml(reader.ReadToEnd()) as Grid ?? throw new InvalidOperationException("The quit confirmation root is not a Grid.");
        card = Named<Border>("DialogCard");
        cancel = Named<Button>("CancelQuit");
        quit = Named<Button>("ConfirmQuit");
        previousFocus = menu.Keyboard?.FocusedElement;
        cancel.Click += (_, _) => Safe(cancelAction);
        quit.Click += (_, _) => Safe(confirm);
        RefreshText(text);
    }

    internal void Show()
    {
        if (closed) throw new InvalidOperationException("The quit confirmation was already closed.");
        if (parent.Children.Contains(Root)) return;
        Root.Loaded += Loaded;
        Root.Unloaded += Unloaded;
        Menu.Unloaded += Unloaded;
        Menu.SizeChanged += SizeChanged;
        parent.Children.Add(Root);
        Resize();
        FocusCancel();
    }

    internal void RefreshText(QuitText text)
    {
        Named<TextBlock>("DialogTitle").Text = text.Title;
        Named<TextBlock>("DialogCategory").Text = text.Category;
        Named<TextBlock>("DialogMessage").Text = text.Message;
        Named<TextBlock>("DialogWarning").Text = text.Warning;
        cancel.Content = text.Cancel;
        quit.Content = text.Title;
    }

    internal void FocusCancel() => cancel.Focus();
    internal void FocusQuit() => quit.Focus();
    internal void ToggleFocus() { if (QuitHasFocus) FocusCancel(); else FocusQuit(); }

    internal void Close(bool restoreFocus)
    {
        if (closed) return;
        closed = true;
        Root.Loaded -= Loaded;
        Root.Unloaded -= Unloaded;
        Menu.Unloaded -= Unloaded;
        Menu.SizeChanged -= SizeChanged;
        if (parent.Children.Contains(Root)) parent.Children.Remove(Root);
        if (restoreFocus && Menu.IsLoaded && previousFocus != null) previousFocus.Focus();
    }

    private void Loaded(object sender, RoutedEventArgs args) => Safe(() => { Resize(); FocusCancel(); });
    private void Unloaded(object sender, RoutedEventArgs args) => Safe(invalidated);
    private void SizeChanged(object sender, SizeChangedEventArgs args) => Safe(Resize);
    private void Resize() { if (Menu.ActualWidth > 0) card.MaxWidth = Math.Max(160, Menu.ActualWidth - 48); }
    private T Named<T>(string name) where T : class => Root.FindName(name) as T
        ?? throw new InvalidOperationException("The quit confirmation control is missing: " + name);
    private static void Safe(Action action)
    {
        Callbacks.Boundary.Run("Quit confirmation action failed; no alternative exit will be attempted", action);
    }
}
