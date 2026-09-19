using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DiskSpaceInspector;
using DiskSpaceInspector.Core;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string output = Path.GetFullPath(args.FirstOrDefault(x => !x.StartsWith("--")) ?? "artifacts");
        Directory.CreateDirectory(output);
        if (args.FirstOrDefault(x => x.StartsWith("--junction-fixture=")) is { } fixtureArg)
        {
            string fixture = fixtureArg["--junction-fixture=".Length..];
            var check = new DiskScanner().Scan(fixture, 10000, 1000, null, CancellationToken.None);
            if (check.Root.Size != 1 || check.Root.FileCount != 1 || !check.Issues.Any(x => x.Reason.Contains("Reparse point"))) throw new InvalidOperationException("Junction exclusion failed");
            Console.WriteLine("PASS: real NTFS junction excluded; linked target bytes were not counted.");
        }
        var bindingErrors = new StringBuilder();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(new StringWriter(bindingErrors)));
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var app = new App(); app.InitializeComponent();
        var window = new MainWindow { Width = 1520, Height = 980 };
        int exitCode = 0;
        window.Loaded += async (_, _) =>
        {
            try
            {
                await Ready(window); Capture(window, output, "01-empty");
                ScanResult data;
                if (args.Contains("--scan-c"))
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
                    long last = 0;
                    var clock = Stopwatch.StartNew();
                    data = await new DiskScanner().ScanAsync(@"C:\", new Progress<ScanProgress>(p =>
                    {
                        if (clock.ElapsedMilliseconds - last < 10_000) return;
                        last = clock.ElapsedMilliseconds;
                        Console.WriteLine($"SCAN {p.Files:N0} files; {p.Folders:N0} folders; {SizeFormatter.Format(p.Bytes)}; {p.Elapsed}; issues={p.Issues}");
                    }), cts.Token);
                    Console.WriteLine($"SCAN COMPLETE cancelled={data.Cancelled}; files={data.Root.FileCount}; dirs={data.Root.FolderCount}; bytes={data.Root.Size}; issues={data.Issues.Count}");
                    if (data.Root.Size != data.Categories.Sum(x => x.Size) || data.Root.Size != data.Extensions.Sum(x => x.Size)) throw new InvalidOperationException("Aggregate mismatch");
                    File.WriteAllText(Path.Combine(output, "scan-summary.txt"), $"Drive={data.Drive}\nStarted={data.Started:O}\nFinished={data.Finished:O}\nCancelled={data.Cancelled}\nFiles={data.Root.FileCount}\nFolders={data.Root.FolderCount}\nBytes={data.Root.Size}\nRetainedFiles={data.Files.Count}\nIssues={data.Issues.Count}\nCategoriesAndExtensionsMatch=True\n");
                }
                else data = Sample();
                window.ViewModel.ApplyResult(data); await Filters(window.ViewModel); await Ready(window);
                var tabs = (TabControl)window.FindName("MainTabs");
                for (int i = 0; i < tabs.Items.Count; i++)
                {
                    tabs.SelectedIndex = i; await Ready(window); Capture(window, output, $"{i + 2:00}-tab-{i}");
                }
                // Exercise actual command bindings, numeric sorting and asynchronously applied filters.
                window.ViewModel.Search = "AppData"; await Filters(window.ViewModel);
                if (window.ViewModel.Files.Any(x => !x.FullPath.Contains("AppData", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Search filter failed");
                window.ViewModel.Search = ""; window.ViewModel.Minimum = "100"; window.ViewModel.Sort = window.ViewModel.T.English ? "Size ↑" : "Размер ↑"; await Filters(window.ViewModel);
                if (window.ViewModel.Files.Any(x => x.Size < 100L * 1024 * 1024)) throw new InvalidOperationException("Minimum size filter failed");
                if (!window.ViewModel.Files.SequenceEqual(window.ViewModel.Files.OrderBy(x => x.Size))) throw new InvalidOperationException("Numeric sorting failed");
                window.ViewModel.ResetFiltersCommand.Execute(null); await Filters(window.ViewModel);
                tabs.SelectedIndex = 0;
                var overview = FindVisual<ScrollViewer>((DependencyObject)((TabItem)tabs.Items[0]).Content);
                overview?.ScrollToVerticalOffset(650); await Ready(window); Capture(window, output, "09-charts");
                overview?.ScrollToTop();
                typeof(MainWindow).GetMethod("ToggleTheme", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [window, new RoutedEventArgs()]);
                window.Width = 1120; window.Height = 760; await Ready(window); Capture(window, output, "08-light-narrow");
                if (bindingErrors.Length > 0) throw new InvalidOperationException("WPF binding errors: " + bindingErrors);
                Console.WriteLine("UI PASS: six tabs, empty state, light theme, search, minimum size, numeric sorting; no binding errors.");
                window.ViewModel.ScanCommand.Execute(null);
                await Task.Delay(500); window.ViewModel.StopCommand.Execute(null);
                var stopWatch = Stopwatch.StartNew();
                while (window.ViewModel.IsScanning && stopWatch.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(100);
                if (window.ViewModel.IsScanning || !(window.ViewModel.Coverage.Contains("STOPPED") || window.ViewModel.Coverage.Contains("ОСТАНОВЛЕНО"))) throw new InvalidOperationException("Scan / Stop command failed");
                Console.WriteLine($"UI PASS: actual C: Scan / Stop commands, cancellation published in {stopWatch.ElapsedMilliseconds} ms.");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); exitCode = 1; }
            finally { window.Close(); }
        };
        app.Run(window);
        return exitCode;
    }
    private static async Task Ready(Window window)
    { await Task.Delay(200); window.UpdateLayout(); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render); }
    private static async Task Filters(MainViewModel model)
    {
        var timer = Stopwatch.StartNew();
        while (model.IsFiltering && timer.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(100);
        if (model.IsFiltering) throw new TimeoutException("Filter did not finish");
    }
    private static T? FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T found) return found;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindVisual<T>(VisualTreeHelper.GetChild(root, i)) is { } child) return child;
        return null;
    }
    private static void Capture(Window window, string output, string name)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, name + ".png")); encoder.Save(stream);
    }
    private static ScanResult Sample()
    {
        const long gb = 1024L * 1024 * 1024;
        var root = new DiskItem { FullPath = @"C:\", IsDirectory = true, Size = 208 * gb, FileCount = 267_123, FolderCount = 35_120 };
        var result = new ScanResult { Drive = @"C:\", Capacity = 512 * gb, Free = 167 * gb, Root = root, Finished = DateTime.Now };
        result.Folders.Add(root);
        foreach (var (name, size) in new[] { ("Users", 98), ("Windows", 48), ("Program Files", 34), ("ProgramData", 18), ("Program Files (x86)", 8), ("Temp", 2) })
        {
            var folder = new DiskItem { FullPath = @"C:\" + name, IsDirectory = true, Size = size * gb, Parent = root, Category = Categorizer.Category(@"C:\" + name, true), FileCount = size * 400, FolderCount = size * 30, Modified = DateTime.Now.AddDays(-1), PercentUsed = size * 100d / 345 };
            root.Children.Add(folder); result.Folders.Add(folder);
        }
        foreach (var (path, size) in new[] { (@"C:\Users\User\Videos\Поездка.mp4", 8), (@"C:\Users\User\Downloads\backup.zip", 5), (@"C:\Users\User\Downloads\system.iso", 4), (@"C:\Users\User\AppData\Local\App\cache\data.db", 2) })
        {
            var file = new DiskItem { FullPath = path, Size = size * gb, Category = Categorizer.Category(path), Modified = DateTime.Now.AddDays(-90), Created = DateTime.Now.AddDays(-100), Parent = root.Children[0] };
            result.Files.Add(file);
            if (RecommendationService.ForFile(file, DateTime.Now) is { } rec) result.Recommendations.Add(rec);
        }
        result.Categories = [new("Видео", 65 * gb, 182), new("Системные данные", 48 * gb, 90732), new("Приложения", 42 * gb, 87632), new("Архивы", 23 * gb, 143), new("Кэш", 18 * gb, 45123), new("Документы", 12 * gb, 14231)];
        foreach (var file in result.Files) result.Extensions.Add(new() { Extension = file.Extension, Category = file.Category, Size = file.Size, Count = 1, Largest = file.Size, LargestPath = file.FullPath });
        result.Issues.Add(new(@"C:\System Volume Information", "Ограниченный доступ: отказано в доступе."));
        return result;
    }
}
