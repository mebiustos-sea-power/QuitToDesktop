using System;
using Noesis;
using SeapowerUI;
using SeapowerUI.ViewModels;

namespace QuitToDesktop;

internal sealed class QuitCoordinator
{
    private readonly Func<PauseMenuView?> currentMenu;
    private readonly Func<QuitText> text;
    private readonly Func<int> frame;
    private readonly QuitFlow flow;
    private readonly PauseInputFence fence = new();
    private QuitDialog? dialog;
    internal bool IsOpen => dialog != null;
    internal QuitDialog? Dialog => dialog;

    internal QuitCoordinator(Func<PauseMenuView?> currentMenu, Func<QuitText> text, Func<int> frame, Action quit)
    {
        this.currentMenu = currentMenu; this.text = text; this.frame = frame;
        flow = new QuitFlow(() => { CloseDialog(false); quit(); });
    }

    internal void Show()
    {
        var menu = currentMenu() ?? throw new InvalidOperationException("The active window is not a pause menu.");
        if (dialog != null)
        {
            if (dialog.Menu != menu) throw new InvalidOperationException("The quit confirmation belongs to a different menu.");
            dialog.FocusCancel();
            return;
        }
        if (menu.DataContext is not PauseMenuViewModel model) throw new InvalidOperationException("The pause menu has no view model.");
        if (model.ConfirmExitVisible) throw new InvalidOperationException("A mission-exit confirmation is already open.");
        long token = flow.Begin();
        try
        {
            dialog = new QuitDialog(menu, text(), () => Confirm(token), () => Cancel(token, true), () => Cancel(token, false));
            dialog.Show();
        }
        catch (Exception) { flow.Cancel(token); CloseDialog(false); throw; }
    }

    internal void RefreshText(QuitText value) => dialog?.RefreshText(value);

    internal void HandleKey(Key key, bool repeated)
    {
        if (dialog == null) return;
        if (key == Key.Escape) { DismissFromPauseKey(); return; }
        if (repeated) return;
        if (key == Key.Tab || key == Key.OemBackTab) dialog.ToggleFocus();
        else if (key == Key.Left) dialog.FocusCancel();
        else if (key == Key.Right) dialog.FocusQuit();
        else if (key == Key.Return || key == Key.Enter || key == Key.Space)
        {
            if (dialog.QuitHasFocus) Confirm(flow.ActiveToken);
            else Cancel(flow.ActiveToken, true);
        }
    }

    internal bool ConsumePauseToggle()
    {
        if (dialog != null) { DismissFromPauseKey(); return true; }
        return fence.Pending && fence.Blocks(frame());
    }

    private void DismissFromPauseKey()
    {
        fence.Mark(frame());
        Cancel(flow.ActiveToken, true);
    }

    internal void CloseIfLeaving(object next)
    {
        if (dialog != null && (next is not PauseMenuView menu || dialog.Menu != menu)) Cancel(flow.ActiveToken, false);
    }

    private void Confirm(long token)
    {
        if (token == 0 || token != flow.ActiveToken) return;
        if (dialog == null || !dialog.IsAttached || currentMenu() != dialog.Menu)
        {
            Cancel(token, false);
            throw new InvalidOperationException("The quit confirmation is no longer presented on the active menu.");
        }
        flow.Confirm(token);
    }

    private void Cancel(long token, bool restoreFocus)
    {
        if (flow.Cancel(token)) CloseDialog(restoreFocus);
    }

    private void CloseDialog(bool restoreFocus)
    {
        var old = dialog;
        dialog = null;
        old?.Close(restoreFocus);
    }
}
