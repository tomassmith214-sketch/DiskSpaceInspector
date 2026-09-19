using System.Windows;
using System.Globalization;
using System.Windows.Markup;
namespace DiskSpaceInspector;
public partial class App : Application
{
    public App()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("ru-RU")));
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (MainWindow is null) { MainWindow = new MainWindow(); MainWindow.Show(); }
    }
}
