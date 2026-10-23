using System;
using Noesis;
using SeapowerUI;
using SeapowerUI.ViewModels;

namespace QuitToDesktop;

internal static class MenuAccent
{
    private const string ResourceKey = "MebiusTos.QuitToDesktop.MenuAccent";

    internal static void Apply(PauseMenuView menu, PauseMenuButton quit)
    {
        if (menu.Resources.Contains(ResourceKey)) return;
        if (menu.Content is not MenuPage page || page.ShortLeftPane is not ScrollViewer scroll || scroll.Content is not ItemsControl items)
            throw new InvalidOperationException("The pause menu's item list is unavailable.");
        var original = menu.TryFindResource(nameof(Button)) as Style
            ?? throw new InvalidOperationException("The pause menu's button style is unavailable.");
        var style = new Style { TargetType = typeof(Button), BasedOn = original };
        // Match the shared entry itself, so translated labels and unrelated buttons cannot select these settings.
        var trigger = new DataTrigger { Binding = new Binding("."), Value = quit };
        trigger.Setters.Add(new Setter { Property = Control.BackgroundProperty,
            Value = new SolidColorBrush(Color.FromRgb(0x77, 0x3F, 0x37)) });
        trigger.Setters.Add(new Setter { Property = Control.ForegroundProperty,
            Value = new SolidColorBrush(Color.FromRgb(0xF1, 0xF4, 0xF5)) });
        trigger.Setters.Add(new Setter { Property = FrameworkElement.HorizontalAlignmentProperty, Value = HorizontalAlignment.Right });
        style.Triggers.Add(trigger);
        // The native button has a local Margin; space its item container instead.
        var rowStyle = new Style { TargetType = typeof(ContentPresenter), BasedOn = items.ItemContainerStyle };
        var spacing = new DataTrigger { Binding = new Binding("."), Value = quit };
        spacing.Setters.Add(new Setter { Property = FrameworkElement.MarginProperty, Value = new Thickness(0, 24, 0, 0) });
        rowStyle.Triggers.Add(spacing);
        items.ItemContainerStyle = rowStyle;
        menu.Resources[ResourceKey] = style;
        menu.Resources[nameof(Button)] = style;
    }
}
