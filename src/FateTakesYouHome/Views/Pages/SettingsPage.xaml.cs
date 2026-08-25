// Copyright © 2026 VagueDustin Enterprises
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using System.Windows.Controls;
using FateTakesYouHome.ViewModels;

namespace FateTakesYouHome.Views.Pages;

/// <summary>
/// Connection, startup and diagnostics.
/// </summary>
/// <remarks>
/// The token is handled in code rather than bound. <see cref="PasswordBox.Password"/> is
/// deliberately not a dependency property — binding it would put the credential in the binding
/// engine and keep a managed copy alive — so the value is pushed straight to the view model, which
/// encrypts it before anything persistent sees it.
/// </remarks>
public partial class SettingsPage : UserControl
{
    public SettingsPage() => InitializeComponent();

    private void OnTokenChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsPageViewModel viewModel && sender is PasswordBox box)
        {
            viewModel.SetAccessToken(box.Password);
        }
    }
}
