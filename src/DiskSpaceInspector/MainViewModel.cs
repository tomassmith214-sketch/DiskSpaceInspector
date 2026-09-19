using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using DiskSpaceInspector.Core;
using Microsoft.Win32;

namespace DiskSpaceInspector;

public sealed class MainViewModel : Observable
{
    private readonly DiskScanner scanner = new();
    private CancellationTokenSource? scanCancellation, filterCancellation;
    private ScanResult? result;
    private bool isScanning, isFiltering;
    private string selectedDrive = "C:\\", status = "Ready to analyze. Choose a drive and click Scan.", currentPath = "File metadata stays in application memory.";
    private string progressText = "0 files · 0 folders · 0 B · 00:00", search = "", minimum = "0", extension = "", category = "All categories", sort = "Size ↓", chartMode = "Folders";
    private DiskItem? selected, mapFolder;
    private string exportFormat = "HTML";
    public MainViewModel()
    {
        Drives = DriveInfo.GetDrives().Where(d => { try { return d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable; } catch (IOException) { return false; } }).Select(d => d.Name).ToArray();
        if (!Drives.Contains(selectedDrive)) selectedDrive = Drives.FirstOrDefault() ?? "C:\\";
        ScanCommand = new(ScanAsync, () => !IsScanning && Drives.Count > 0);
        StopCommand = new(() => { scanCancellation?.Cancel(); Status = "Остановка… ожидаем завершения текущего чтения метаданных."; }, () => IsScanning);
        ExportCommand = new(ExportAsync, () => result is not null && !IsScanning);
        CopyCommand = new(Copy, () => Selected is not null);
        ExplorerCommand = new(OpenExplorer, () => Selected is not null);
        UpCommand = new(() => { if (mapFolder?.Parent is { } parent) Navigate(parent); }, () => mapFolder?.Parent is not null);
        ResetFiltersCommand = new(() => { Search = ""; Minimum = "0"; Extension = ""; Category = AllCategoriesLabel; Sort = SortOptions[0]; });
        ToggleLanguageCommand = new(ToggleLanguage);
        RefreshDriveMetrics();
    }
    public UiText T { get; } = new(true);
    public IReadOnlyList<string> Drives { get; }
    public string SelectedDrive { get => selectedDrive; set { if (Set(ref selectedDrive, value)) { Changed(nameof(Title)); Changed(nameof(ScanLabel)); RefreshDriveMetrics(); } } }
    public string Title => $"Disk Space Inspector — {SelectedDrive}";
    public string ScanLabel => T.English ? $"Scan {SelectedDrive}" : $"Сканировать {SelectedDrive}";
    public string ResultLabel => result is null ? (T.English ? "No results yet" : "Результатов пока нет") : $"{(T.English ? "Results" : "Результаты")} {result.Drive} · {result.Finished:g}";
    public bool IsScanning { get => isScanning; private set { Set(ref isScanning, value); Changed(nameof(CanChooseDrive)); ScanCommand.Refresh(); StopCommand.Refresh(); ExportCommand.Refresh(); } }
    public bool CanChooseDrive => !IsScanning;
    public bool IsFiltering { get => isFiltering; private set => Set(ref isFiltering, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public string CurrentPath { get => currentPath; private set => Set(ref currentPath, value); }
    public string ProgressText { get => progressText; private set => Set(ref progressText, value); }
    public string Search { get => search; set { if (Set(ref search, value)) QueueFilter(); } }
    public string Minimum { get => minimum; set { if (Set(ref minimum, value)) QueueFilter(); } }
    public string Extension { get => extension; set { if (Set(ref extension, value)) QueueFilter(); } }
    public string Category { get => category; set { if (Set(ref category, value)) QueueFilter(); } }
    public string Sort { get => sort; set { if (Set(ref sort, value)) QueueFilter(); } }
    public string ChartMode { get => chartMode; set { if (Set(ref chartMode, value)) RefreshTop(); } }
    public string ExportFormat { get => exportFormat; set => Set(ref exportFormat, value); }
    public string[] ExportFormats { get; } = ["HTML", "CSV", "JSON"];
    public string[] SortOptions { get; private set; } = ["Size ↓", "Size ↑", "Name A-Z", "Name Z-A", "Files ↓", "Date ↓", "Date ↑"];
    public string[] ChartModes { get; private set; } = ["Folders", "Files"];
    public string[] SizePresets { get; } = ["0", "100", "500", "1024", "5120"];
    public IReadOnlyList<string> Categories { get; private set; } = ["All categories"];
    public IReadOnlyList<Metric> Metrics { get; private set; } = [];
    public IReadOnlyList<DiskItem> Folders { get; private set; } = [];
    public IReadOnlyList<DiskItem> Files { get; private set; } = [];
    public IReadOnlyList<DiskItem> Packages { get; private set; } = [];
    public IReadOnlyList<FolderNode> Tree { get; private set; } = [];
    public IReadOnlyList<ExtensionSummary> Extensions { get; private set; } = [];
    public IReadOnlyList<CategorySummary> CategoryData { get; private set; } = [];
    public IReadOnlyList<Recommendation> Recommendations { get; private set; } = [];
    public IReadOnlyList<Recommendation> PreviewRecommendations => Recommendations.Take(5).ToList();
    public IReadOnlyList<ScanIssue> Issues { get; private set; } = [];
    public IReadOnlyList<ChartEntry> Map { get; private set; } = [];
    public IReadOnlyList<ChartEntry> Top { get; private set; } = [];
    public IReadOnlyList<ChartEntry> Donut { get; private set; } = [];
    public string MapPath => mapFolder?.FullPath ?? SelectedDrive;
    public string FilterSummary { get; private set; } = "Search and filters apply to folder, file, and program tables. The tree shows every found folder.";
    private string AllCategoriesLabel => T.English ? "All categories" : "Все категории";
    public string Coverage => result is null ? (T.English ? "Real data appears here after scanning. No files are changed." : "После сканирования здесь появятся реальные данные. Никакие файлы не изменяются.") :
        T.English
            ? $"{(result.Cancelled ? "STOPPED · partial results. " : "")}{result.Root.Status}. Read {result.Root.FileCount:N0} files. Table keeps {result.Files.Count:N0} largest files (50,000 limit). Skipped / errors: {result.Issues.Count:N0}."
            : $"{(result.Cancelled ? "ОСТАНОВЛЕНО · частичные результаты. " : "")}{result.Root.Status}. Прочитано {result.Root.FileCount:N0} файлов. В таблице {result.Files.Count:N0} крупнейших (предел 50 000). Пропущено / ошибок: {result.Issues.Count:N0}.";
    public string Causes => result is null ? (T.English ? "Run a scan to see the main sources of used space." : "Запустите сканирование, чтобы увидеть основные источники занятого места.") :
        string.Join("\n", result.Root.Children.OrderByDescending(x => x.Size).Take(5).Select(x => T.English ? $"{x.FullPath} — {x.SizeText} ({x.PercentText} of used space)" : $"{x.FullPath} — {x.SizeText} ({x.PercentText} занятого места)"));
    public DiskItem? Selected { get => selected; set { if (Set(ref selected, value)) { Changed(nameof(DetailText)); CopyCommand.Refresh(); ExplorerCommand.Refresh(); } } }
    public string DetailText => Selected is not { } s ? (T.English ? "Select a file, folder, or chart area to see the full path, exact size, and purpose." : "Выберите файл, папку или область диаграммы, чтобы увидеть полный путь, точный размер и назначение.") :
        T.English
            ? $"{(s.IsDirectory ? "Folder" : "File")} · {s.Category}\n{s.SizeText} · {s.Size:N0} bytes\n{s.PercentText} of used disk space\n\nPurpose: {s.Purpose}\nRisk: {s.Risk}\nExtension: {s.Extension}\nCreated: {s.Created:g}\nModified: {s.Modified:g}\nFiles: {s.FileCount:N0} · folders: {s.FolderCount:N0}\nAttributes: {s.Attributes}\nStatus: {s.Status}\n\n{(s.IsDirectory ? "Size is the sum of accessible nested files." : "Logical size from file metadata.")}\n\n{s.Advice}"
            : $"{s.Kind} · {s.Category}\n{s.SizeText} · {s.Size:N0} байт\n{s.PercentText} занятого места диска\n\nНазначение: {s.Purpose}\nРиск: {s.Risk}\nРасширение: {s.Extension}\nСоздан: {s.Created:g}\nИзменён: {s.Modified:g}\nФайлов: {s.FileCount:N0} · папок: {s.FolderCount:N0}\nАтрибуты: {s.Attributes}\nСтатус: {s.Status}\n\n{(s.IsDirectory ? "Размер — сумма доступных вложенных файлов." : "Логический размер из метаданных файла.")}\n\n{s.Advice}";
    public AsyncCommand ScanCommand { get; }
    public Command StopCommand { get; }
    public AsyncCommand ExportCommand { get; }
    public Command CopyCommand { get; }
    public Command ExplorerCommand { get; }
    public Command UpCommand { get; }
    public Command ResetFiltersCommand { get; }
    public Command ToggleLanguageCommand { get; }
    public void Cancel() { scanCancellation?.Cancel(); filterCancellation?.Cancel(); }
    private void ToggleLanguage()
    {
        bool toEnglish = !T.English;
        T.English = toEnglish;
        var previousSortIndex = Array.IndexOf(SortOptions, Sort);
        var previousChartIndex = Array.IndexOf(ChartModes, ChartMode);
        bool allCategories = Category == AllCategoriesLabel || Category is "All categories" or "Все категории";
        SortOptions = toEnglish ? ["Size ↓", "Size ↑", "Name A-Z", "Name Z-A", "Files ↓", "Date ↓", "Date ↑"] : ["Размер ↓", "Размер ↑", "Имя А–Я", "Имя Я–А", "Файлы ↓", "Дата ↓", "Дата ↑"];
        ChartModes = toEnglish ? ["Folders", "Files"] : ["Папки", "Файлы"];
        sort = previousSortIndex >= 0 && previousSortIndex < SortOptions.Length ? SortOptions[previousSortIndex] : SortOptions[0];
        chartMode = previousChartIndex >= 0 && previousChartIndex < ChartModes.Length ? ChartModes[previousChartIndex] : ChartModes[0];
        Categories = new[] { AllCategoriesLabel }.Concat(result?.Categories.Select(x => x.Category) ?? []).ToArray();
        if (allCategories || !Categories.Contains(Category)) category = AllCategoriesLabel;
        FilterSummary = result is null
            ? (T.English ? "Search and filters apply to folder, file, and program tables. The tree shows every found folder." : "Поиск и фильтры применяются к таблицам папок, файлов и программ. Дерево показывает все найденные папки.")
            : FilterSummary;
        foreach (string name in new[] { nameof(ScanLabel), nameof(ResultLabel), nameof(SortOptions), nameof(ChartModes), nameof(Sort), nameof(ChartMode), nameof(Categories), nameof(Category), nameof(FilterSummary), nameof(Coverage), nameof(Causes), nameof(DetailText) }) Changed(name);
        RefreshDriveMetrics();
        RefreshTop();
        QueueFilter();
    }

    private async Task ScanAsync()
    {
        scanCancellation?.Dispose(); scanCancellation = new();
        IsScanning = true; Status = T.English ? "Scanning… reading metadata" : "Сканирование… чтение метаданных";
        var progress = new Progress<ScanProgress>(p =>
        { CurrentPath = p.CurrentPath; ProgressText = T.English ? $"{p.Files:N0} files · {p.Folders:N0} folders · {SizeFormatter.Format(p.Bytes)} · {p.Elapsed:hh\\:mm\\:ss} · skipped {p.Issues:N0}" : $"{p.Files:N0} файлов · {p.Folders:N0} папок · {SizeFormatter.Format(p.Bytes)} · {p.Elapsed:hh\\:mm\\:ss} · пропущено {p.Issues:N0}"; });
        try
        {
            var scanned = await scanner.ScanAsync(SelectedDrive, progress, scanCancellation.Token);
            ApplyResult(scanned);
        }
        catch (Exception ex) { Status = T.English ? $"Could not finish scan: {ex.Message}" : $"Не удалось завершить сканирование: {ex.Message}"; }
        finally { IsScanning = false; }
    }
    internal void ApplyResult(ScanResult scanned)
    {
        result = scanned; Selected = null;
        Status = scanned.Cancelled ? (T.English ? "Scan stopped. Partial results are shown." : "Сканирование остановлено. Показаны частичные результаты.") : (T.English ? "Analysis complete. Results are available in every tab." : "Анализ завершён. Результаты доступны во всех вкладках.");
        CurrentPath = $"{scanned.Drive} · {(scanned.Issues.Count > 0 ? (T.English ? "Some objects were not read; see Recommendations for details." : "Есть непрочитанные объекты — подробности в разделе «Рекомендации».") : (T.English ? "All discovered objects were processed." : "Все обнаруженные объекты обработаны."))}";
        ProgressText = T.English ? $"{scanned.Root.FileCount:N0} files · {scanned.Root.FolderCount:N0} folders · {scanned.Root.SizeText} · {scanned.Finished - scanned.Started:hh\\:mm\\:ss} · skipped {scanned.Issues.Count:N0}" : $"{scanned.Root.FileCount:N0} файлов · {scanned.Root.FolderCount:N0} папок · {scanned.Root.SizeText} · {scanned.Finished - scanned.Started:hh\\:mm\\:ss} · пропущено {scanned.Issues.Count:N0}";
        Tree = [new(scanned.Root)]; Extensions = scanned.Extensions; CategoryData = scanned.Categories;
        Recommendations = scanned.Recommendations; Issues = scanned.Issues;
        Categories = new[] { AllCategoriesLabel }.Concat(scanned.Categories.Select(x => x.Category)).ToArray();
        if (!Categories.Contains(Category)) category = AllCategoriesLabel;
        Donut = scanned.Categories.Select(x => new ChartEntry(x.Category, T.English ? "All read files" : "Все прочитанные файлы", x.Size, x.Color, null)).ToList();
        RefreshDriveMetrics(); Navigate(scanned.Root); RefreshTop(); QueueFilter();
        foreach (string name in new[] { nameof(Tree), nameof(Extensions), nameof(CategoryData), nameof(Recommendations), nameof(PreviewRecommendations), nameof(Issues), nameof(Categories), nameof(Category), nameof(Donut), nameof(Coverage), nameof(Causes), nameof(ResultLabel) }) Changed(name);
        ExportCommand.Refresh();
    }
    private void RefreshDriveMetrics()
    {
        long capacity = 0, free = 0;
        try { var drive = new DriveInfo(SelectedDrive); capacity = drive.TotalSize; free = drive.TotalFreeSpace; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Status = ex.Message; }
        var r = result;
        if (r is not null) { capacity = r.Capacity; free = r.Free; }
        Metrics = T.English
            ? [new("Drive capacity", SizeFormatter.Format(capacity), r?.Drive ?? SelectedDrive), new("Free", SizeFormatter.Format(free), "Reported by the file system"),
                new("Used", SizeFormatter.Format(capacity - free), "Reported by the file system"), new("Analyzed", r?.Root.SizeText ?? "—", "Logical file size"),
                new("Files", r?.Root.FileCount.ToString("N0") ?? "—", "Successfully read metadata"), new("Folders", r?.Root.FolderCount.ToString("N0") ?? "—", "Found, including inaccessible"),
                new("Largest file", r?.Files.FirstOrDefault()?.SizeText ?? "—", r?.Files.FirstOrDefault()?.Name ?? "Appears after scanning"),
                new("Largest folder", r?.Folders.Where(x => x.Parent is not null).MaxBy(x => x.Size)?.SizeText ?? "—", "Excludes drive root")]
            : [new("Объём диска", SizeFormatter.Format(capacity), r?.Drive ?? SelectedDrive), new("Свободно", SizeFormatter.Format(free), "По данным файловой системы"),
                new("Занято", SizeFormatter.Format(capacity - free), "По данным файловой системы"), new("Проанализировано", r?.Root.SizeText ?? "—", "Логический размер файлов"),
                new("Файлы", r?.Root.FileCount.ToString("N0") ?? "—", "Успешно прочитанные метаданные"), new("Папки", r?.Root.FolderCount.ToString("N0") ?? "—", "Найденные, включая недоступные"),
                new("Крупнейший файл", r?.Files.FirstOrDefault()?.SizeText ?? "—", r?.Files.FirstOrDefault()?.Name ?? "Появится после сканирования"),
                new("Крупнейшая папка", r?.Folders.Where(x => x.Parent is not null).MaxBy(x => x.Size)?.SizeText ?? "—", "Без корня диска")];
        Changed(nameof(Metrics));
    }
    public void Navigate(DiskItem item)
    {
        Selected = item; if (!item.IsDirectory || result is null) return;
        mapFolder = item;
        var entries = item.Children.Select(ToChart).Concat(result.Files.Where(x => ReferenceEquals(x.Parent, item)).Select(ToChart)).OrderByDescending(x => x.Size).ToList();
        long knownDirect = result.Files.Where(x => ReferenceEquals(x.Parent, item)).Sum(x => x.Size);
        if (item.DirectSize > knownDirect) entries.Add(new(T.English ? "Other files" : "Остальные файлы", item.FullPath, item.DirectSize - knownDirect, "#8896AE", null));
        var top = entries.OrderByDescending(x => x.Size).Where(x => x.Size > 0).Take(39).ToList();
        long other = entries.Sum(x => x.Size) - top.Sum(x => x.Size);
        if (other > 0) top.Add(new(T.English ? "Other objects" : "Другие объекты", item.FullPath, other, "#8896AE", null));
        Map = top; Changed(nameof(Map)); Changed(nameof(MapPath)); UpCommand.Refresh();
    }
    public void SelectChartEntry(ChartEntry entry)
    {
        Selected = entry.Item ?? new DiskItem
        {
            FullPath = entry.FullPath,
            Size = entry.Size,
            Category = entry.Name,
            Status = T.English ? "Summary chart category; open the list below or the Files tab for specific objects" : "Сводная категория диаграммы; откройте список ниже или вкладку «Файлы» для конкретных объектов"
        };
    }
    public void SelectExtension(ExtensionSummary extension)
    {
        if (result?.Files.FirstOrDefault(x => x.FullPath.Equals(extension.LargestPath, StringComparison.OrdinalIgnoreCase)) is { } item)
        {
            Selected = item;
            return;
        }
        Selected = new DiskItem
        {
            FullPath = extension.LargestPath,
            Size = extension.Largest,
            Category = extension.Category,
            Status = T.English ? $"Largest file of type {extension.Extension}; row selected from the extension summary" : $"Крупнейший файл типа {extension.Extension}; строка выбрана из сводки расширений"
        };
    }
    public void SelectRecommendation(Recommendation recommendation)
    {
        Selected = recommendation.Item ?? result?.Files.FirstOrDefault(x => x.FullPath.Equals(recommendation.FullPath, StringComparison.OrdinalIgnoreCase))
            ?? result?.Folders.FirstOrDefault(x => x.FullPath.Equals(recommendation.FullPath, StringComparison.OrdinalIgnoreCase))
            ?? new DiskItem
            {
                FullPath = recommendation.FullPath,
                Size = recommendation.Size,
                Category = recommendation.Category,
                Status = T.English ? "Metadata-based recommendation; see the table for additional details" : "Рекомендация по метаданным; дополнительные данные см. в таблице"
            };
    }
    private static ChartEntry ToChart(DiskItem item) => new(item.Name, item.FullPath, item.Size, Categorizer.Color(item.Category), item);
    private void RefreshTop()
    {
        Top = result is null ? [] : (ChartMode is "Файлы" or "Files" ? result.Files : result.Folders.Where(x => x.Parent is not null)).OrderByDescending(x => x.Size).Take(20).Select(ToChart).ToList(); Changed(nameof(Top));
    }
    private async void QueueFilter()
    {
        filterCancellation?.Cancel(); filterCancellation?.Dispose(); filterCancellation = new();
        var token = filterCancellation.Token; var r = result;
        if (r is null) return;
        IsFiltering = true;
        try
        {
            await Task.Delay(250, token);
            string query = Search.Trim(), ext = Extension.Trim(), cat = Category, order = Sort;
            if (!double.TryParse(Minimum.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mb) || !double.IsFinite(mb) || mb < 0 || mb > 8_796_093_022_207d)
            { FilterSummary = T.English ? "Enter a minimum size from 0 MB to 8,796,093,022,207 MB." : "Введите минимальный размер от 0 MB до 8 796 093 022 207 MB."; Changed(nameof(FilterSummary)); return; }
            long min = (long)(mb * 1048576d);
            var lists = await Task.Run(() =>
            {
                bool Match(DiskItem x) => x.Size >= min && (query.Length == 0 || x.FullPath.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
                    (x.IsDirectory || (ext.Length == 0 || x.Extension.Equals(ext.StartsWith('.') ? ext : "." + ext, StringComparison.OrdinalIgnoreCase)) && (cat == AllCategoriesLabel || x.Category == cat));
                List<DiskItem> Filter(IEnumerable<DiskItem> source)
                {
                    token.ThrowIfCancellationRequested(); var matches = source.Where(Match);
                    var sorted = order switch
                    {
                        "Размер ↑" or "Size ↑" => matches.OrderBy(x => x.Size), "Имя А–Я" or "Name A-Z" => matches.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase),
                        "Имя Я–А" or "Name Z-A" => matches.OrderByDescending(x => x.Name, StringComparer.CurrentCultureIgnoreCase), "Файлы ↓" or "Files ↓" => matches.OrderByDescending(x => x.FileCount),
                        "Дата ↓" or "Date ↓" => matches.OrderByDescending(x => x.Modified), "Дата ↑" or "Date ↑" => matches.OrderBy(x => x.Modified), _ => matches.OrderByDescending(x => x.Size)
                    };
                    return sorted.ToList();
                }
                return (Folders: Filter(r.Folders), Files: Filter(r.Files), Packages: Filter(r.Folders.Where(RecommendationService.IsPackage)));
            }, token);
            token.ThrowIfCancellationRequested();
            Folders = lists.Folders; Files = lists.Files; Packages = lists.Packages;
            FilterSummary = T.English ? $"Found: {Folders.Count:N0} folders · {Files.Count:N0} files · {Packages.Count:N0} programs / packages. The tree is unfiltered. Extension and category apply to files." : $"Найдено: {Folders.Count:N0} папок · {Files.Count:N0} файлов · {Packages.Count:N0} программ / пакетов. Дерево без фильтров. Расширение и категория — для файлов.";
            Changed(nameof(Folders)); Changed(nameof(Files)); Changed(nameof(Packages)); Changed(nameof(FilterSummary));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status = T.English ? $"Filter error: {ex.Message}" : $"Ошибка фильтра: {ex.Message}"; }
        finally { if (!token.IsCancellationRequested) IsFiltering = false; }
    }
    private void Copy()
    {
        try { if (Selected is { } s) { Clipboard.SetText(s.FullPath); Status = T.English ? "Full path copied." : "Полный путь скопирован."; } }
        catch (Exception ex) { Status = T.English ? $"Clipboard is unavailable: {ex.Message}" : $"Буфер обмена недоступен: {ex.Message}"; }
    }
    private void OpenExplorer()
    {
        if (Selected is not { } item) return;
        try
        {
            string location = item.FullPath;
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false };
            if (item.IsDirectory || !string.IsNullOrWhiteSpace(Path.GetFileName(location)))
            {
                start.ArgumentList.Add($"/select,{location}");
            }
            else
            {
                start.ArgumentList.Add(location);
            }
            Process.Start(start);
            Status = T.English ? $"Explorer opened for: {location}" : $"Проводник открыт для: {location}";
        }
        catch (Exception ex) { Status = T.English ? $"Could not open location: {ex.Message}" : $"Не удалось открыть расположение: {ex.Message}"; }
    }
    private async Task ExportAsync()
    {
        if (result is not { } snapshot) return;
        var dialog = new OpenFolderDialog { Title = T.English ? "Explicitly choose a report folder (another drive is recommended)" : "Явно выберите папку для отчёта (рекомендуется другой диск)", Multiselect = false };
        string? otherDrive = Drives.FirstOrDefault(x => !x.StartsWith("C:", StringComparison.OrdinalIgnoreCase));
        if (otherDrive is not null) dialog.InitialDirectory = otherDrive;
        if (dialog.ShowDialog() != true) return;
        string format = ExportFormat;
        string path = Path.Combine(dialog.FolderName, $"DiskSpaceInspector-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.{format.ToLowerInvariant()}");
        try
        {
            Status = T.English ? "Building report…" : "Формирование отчёта…";
            await Task.Run(async () =>
            {
                string content = ReportBuilder.Build(snapshot, format);
                // The only application filesystem write, reachable only after the explicit folder dialog.
                await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(true));
                await writer.WriteAsync(content);
            });
            Status = T.English ? $"Report saved: {path}" : $"Отчёт сохранён: {path}";
        }
        catch (Exception ex) { Status = T.English ? $"Could not save report: {ex.Message}" : $"Не удалось сохранить отчёт: {ex.Message}"; }
    }
}
