using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace Anibel.App.Services;

internal static class KeyboardNavigation
{
    internal static VirtualKeyModifiers Modifiers
    {
        get
        {
            var result = VirtualKeyModifiers.None;
            if (Down(VirtualKey.Control)) result |= VirtualKeyModifiers.Control;
            if (Down(VirtualKey.Menu)) result |= VirtualKeyModifiers.Menu;
            if (Down(VirtualKey.Shift)) result |= VirtualKeyModifiers.Shift;
            if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) result |= VirtualKeyModifiers.Windows;
            return result;
        }
    }

    private static bool Down(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

    internal static bool IsEditing(XamlRoot root)
    {
        for (var node = FocusManager.GetFocusedElement(root) as DependencyObject; node is not null;
             node = VisualTreeHelper.GetParent(node))
        {
            if (node is TextBox or PasswordBox or RichEditBox or AutoSuggestBox or ComboBox or WebView2) return true;
        }
        return false;
    }

    internal static bool FocusIsWithin(FrameworkElement root)
    {
        for (var node = FocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject; node is not null;
             node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, root)) return true;
        return false;
    }

    internal static T? Find<T>(DependencyObject root) where T : FrameworkElement
    {
        if (root is FrameworkElement { Visibility: Visibility.Collapsed }) return null;
        if (root is T match) return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (Find<T>(VisualTreeHelper.GetChild(root, i)) is { } child) return child;
        return null;
    }
}
