using System.Windows;
using System.Windows.Controls;
using FateTakesYouHome.Branding;

namespace FateTakesYouHome.Views.Pages;

/// <summary>
/// The first thing somebody sees, once.
/// </summary>
/// <remarks>
/// Deliberately not a wizard. A wizard implies the app is unusable until it is finished, and this
/// one works fine in a degraded way with no connection at all — so the page offers three exits and
/// gets out of the way.
/// </remarks>
public partial class WelcomePage : UserControl
{
    public WelcomePage()
    {
        InitializeComponent();

        // Rendered rather than loaded from a file: the mark is vector, and at 72px on a scaled
        // display a pre-rendered bitmap would be visibly soft.
        Mark.Source = FateMark.Render(160);
    }

    /// <summary>Raised when the user wants to go straight to the connection settings.</summary>
    public event EventHandler? GetStarted;

    /// <summary>Raised when the user asks for the guided tour.</summary>
    public event EventHandler? TourRequested;

    /// <summary>Raised when the user would rather just look around themselves.</summary>
    public event EventHandler? Dismissed;

    private void OnStartClicked(object sender, RoutedEventArgs e) =>
        GetStarted?.Invoke(this, EventArgs.Empty);

    private void OnTourClicked(object sender, RoutedEventArgs e) =>
        TourRequested?.Invoke(this, EventArgs.Empty);

    private void OnSkipClicked(object sender, RoutedEventArgs e) =>
        Dismissed?.Invoke(this, EventArgs.Empty);
}
