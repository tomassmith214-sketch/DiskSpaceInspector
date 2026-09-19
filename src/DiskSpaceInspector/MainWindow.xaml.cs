using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DiskSpaceInspector.Core;

namespace DiskSpaceInspector;

public partial class MainWindow : Window
{
    private bool light;
    public MainViewModel ViewModel { get; } = new();
    public MainWindow()
    {
        InitializeComponent(); DataContext = ViewModel;
        MapChart.EntryClicked += entry => { if (entry.Item is { } item) ViewModel.Navigate(item); };
        TopChart.EntryClicked += entry => { if (entry.Item is { } item) ViewModel.Selected = item; };
        CompositionChart.EntryClicked += entry => ViewModel.SelectChartEntry(entry);
        Closed += (_, _) => ViewModel.Cancel();
        SourceInitialized += (_, _) => FitToHalfScreen();
    }
    private void TreeSelection(object sender, RoutedPropertyChangedEventArgs<object> e)
    { if (e.NewValue is FolderNode node) ViewModel.Navigate(node.Item); }
    private void FolderDoubleClick(object sender, MouseButtonEventArgs e)
    { if ((sender as DataGrid)?.SelectedItem is DiskItem { IsDirectory: true } item) { ViewModel.Navigate(item); MainTabs.SelectedIndex = 0; } }
    private void RecommendationSelection(object sender, SelectionChangedEventArgs e)
    {
        if ((sender as DataGrid)?.SelectedItem is not Recommendation recommendation) return;
        ViewModel.SelectRecommendation(recommendation);
    }
    private void ExtensionSelection(object sender, SelectionChangedEventArgs e)
    {
        if ((sender as DataGrid)?.SelectedItem is ExtensionSummary extension) ViewModel.SelectExtension(extension);
    }
    private void ToggleTheme(object sender, RoutedEventArgs e)
    {
        light = !light;
        string[] keys = ["BackgroundBrush", "PanelBrush", "InputBrush", "BorderBrush", "TextBrush", "MutedBrush", "AccentBrush"];
        string[] colors = light ? ["#EDF2F8", "#FFFFFF", "#E3EBF5", "#C5D1E0", "#17253B", "#50627C", "#087E80"] : ["#0B1220", "#121D2E", "#1B293E", "#2B3B52", "#EEF4FC", "#A4B5CC", "#52D9CC"];
        for (int i = 0; i < keys.Length; i++) Application.Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        Application.Current.Resources["AccentTextBrush"] = new SolidColorBrush(light ? Colors.White : (Color)ColorConverter.ConvertFromString("#071D24"));
        RefreshVisuals(this);
    }
    private void FitToHalfScreen()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, area.Width * 0.56);
        Height = Math.Max(MinHeight, area.Height * 0.58);
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
    }
    private static void RefreshVisuals(DependencyObject element)
    {
        if (element is UIElement visual) visual.InvalidateVisual();
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) RefreshVisuals(VisualTreeHelper.GetChild(element, i));
    }
}
