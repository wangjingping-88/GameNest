using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GameNest.App.Views;

public sealed partial class SettingsPageView : UserControl
{
    public SettingsPageView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? ExportDiagnosticsRequested;

    private void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        ExportDiagnosticsRequested?.Invoke(sender, e);
    }
}
