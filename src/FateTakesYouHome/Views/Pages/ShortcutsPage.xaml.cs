// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FateTakesYouHome.Views.Pages;

/// <summary>Every system-wide shortcut: the application's own, and those that act on devices.</summary>
/// <remarks>
/// The two handlers here move focus and nothing else. Adding a device is the suggestion's command,
/// so keyboard, mouse and automation all reach it the same way; where focus lands afterwards is a
/// question only the view can answer.
/// </remarks>
public partial class ShortcutsPage : UserControl
{
    public ShortcutsPage() => InitializeComponent();

    /// <summary>
    /// Returns focus to the search box once a suggestion is chosen. The suggestions show only
    /// while the picker has focus, and the chosen one is about to leave the list, so without this
    /// the list would close after every pick and adding a group would mean reopening it each time.
    /// </summary>
    /// <remarks>Click is raised before the command runs, so focus has moved before the list rebuilds.</remarks>
    private void OnSuggestionClicked(object sender, RoutedEventArgs e)
    {
        if (FindAncestor((DependencyObject)sender, "Picker") is { } picker
            && FindDescendant<TextBox>(picker) is { } search)
        {
            search.Focus();
        }
    }

    /// <summary>The down arrow steps from the search box into the suggestions, as it would in a combo box.</summary>
    private void OnSearchPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && sender is UIElement search)
        {
            e.Handled = search.MoveFocus(new TraversalRequest(FocusNavigationDirection.Down));
        }
    }

    private static FrameworkElement? FindAncestor(DependencyObject start, string name)
    {
        for (DependencyObject? node = start; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement { Name: var found } element && found == name)
            {
                return element;
            }
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);

            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }
}
