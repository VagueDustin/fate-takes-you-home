using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using FateTakesYouHome.ViewModels;

namespace FateTakesYouHome.Views.Pages;

/// <summary>
/// The entity browser.
/// </summary>
/// <remarks>
/// <c>SearchBox</c> and <c>ResultsList</c> are named because the guided tour points at them.
/// </remarks>
public partial class EntitiesPage : UserControl
{
    public EntitiesPage()
    {
        InitializeComponent();

        IsVisibleChanged += OnVisibleChanged;
    }

    private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible)
        {
            return;
        }

        // Deferred to Background so the report describes a laid-out list rather than an empty one.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ReportVirtualisation));
    }

    /// <summary>
    /// Logs how much of the list is actually realised.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Virtualisation is easy to lose by accident — overriding the items panel, or letting
    /// <c>CanContentScroll</c> fall back to false, both switch it off silently — and the only
    /// symptom is that the interface gets slow on somebody else's much larger install. This turns
    /// that into a number in the log.
    /// </para>
    /// <para>
    /// Deliberately not measured through UI Automation: walking the automation tree forces WPF to
    /// realise virtualised containers, so any measurement taken that way reports full realisation
    /// whether or not virtualisation is working.
    /// </para>
    /// </remarks>
    private void ReportVirtualisation()
    {
        if (App.Current is not { } app || DataContext is not EntityBrowserViewModel model)
        {
            return;
        }

        VirtualizingPanel? panel = FindPanel(ResultsList);

        if (panel is null)
        {
            app.Log.Debug("Entity browser: no virtualising panel found — the list is not virtualised.");
            return;
        }

        int realised = panel.Children.Count;
        int total = model.Rows.Count;

        app.Log.Debug(
            $"Entity browser: {realised} of {total} rows realised "
            + $"({panel.GetType().Name}, virtualising={VirtualizingPanel.GetIsVirtualizing(ResultsList)}, "
            + $"mode={VirtualizingPanel.GetVirtualizationMode(ResultsList)}).");
    }

    private static VirtualizingPanel? FindPanel(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);

        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);

            if (child is VirtualizingPanel panel)
            {
                return panel;
            }

            if (FindPanel(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
