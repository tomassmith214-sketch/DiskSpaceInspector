using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DiskSpaceInspector.Core;

namespace DiskSpaceInspector;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Changed(property); return true; }
}
public sealed class Command(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !running && (canExecute?.Invoke() ?? true);
    public async void Execute(object? parameter)
    { if (!CanExecute(parameter)) return; running = true; Refresh(); try { await execute(); } finally { running = false; Refresh(); } }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
public sealed class FolderNode(DiskItem item)
{
    public DiskItem Item { get; } = item;
    public string Label => $"{Item.Name}   {Item.SizeText}";
    private IReadOnlyList<FolderNode>? children;
    public IReadOnlyList<FolderNode> Children => children ??= Item.Children.OrderByDescending(x => x.Size).Select(x => new FolderNode(x)).ToList();
}
public sealed record Metric(string Label, string Value, string Hint);
public sealed record ChartEntry(string Name, string FullPath, long Size, string Color, DiskItem? Item)
{
    public string Tooltip => Item?.Tooltip ?? $"{Name}\n{FullPath}\n{SizeFormatter.Format(Size)} · {Size:N0} байт";
}

public sealed class UiText(bool initialEnglish) : Observable
{
    private bool english = initialEnglish;
    public bool English
    {
        get => english;
        set
        {
            if (!Set(ref english, value)) return;
            foreach (var property in GetType().GetProperties().Where(x => x.PropertyType == typeof(string))) Changed(property.Name);
        }
    }
    private string Pick(string en, string ru) => English ? en : ru;
    public string LanguageButton => English ? "RU" : "EN";
    public string Theme => Pick("◐  Theme", "◐  Тема");
    public string ThemeTip => Pick("Switch dark / light theme", "Переключить тёмную / светлую тему");
    public string ReadOnlyMode => Pick("●  Read-only mode", "●  Режим: только чтение");
    public string Subtitle => Pick("Safe disk space analysis. No files are changed.", "Безопасный анализ занятого места. Никакие файлы не изменяются.");
    public string ExportReport => Pick("↗  Export report", "↗  Экспорт отчёта");
    public string ExportTip => Pick("Writes only after your explicit action to the selected folder", "Запись только по вашему действию в выбранную папку");
    public string LocalDrive => Pick("Local drive", "Локальный диск");
    public string Stop => Pick("Stop", "Остановить");
    public string Overview => Pick("◫  Overview", "◫  Обзор");
    public string FoldersTab => Pick("▱  Folders", "▱  Папки");
    public string FilesTab => Pick("▤  Files", "▤  Файлы");
    public string FileTypesTab => Pick("◈  File types", "◈  Типы файлов");
    public string ProgramsTab => Pick("▦  Programs", "▦  Программы");
    public string RecommendationsTab => Pick("◇  Recommendations", "◇  Рекомендации");
    public string SpaceMap => Pick("Used space map", "Карта занятого места");
    public string UpOneLevel => Pick("↑ Up one level", "↑ На уровень выше");
    public string SpaceHint => Pick("Area = size · color = category · click a folder to open its contents", "Площадь = размер · цвет = категория · клик по папке открывает её содержимое");
    public string OverlapHint => Pick("Nested folders may overlap by size.", "Вложенные папки могут пересекаться по размеру.");
    public string Composition => Pick("Data composition", "Состав данных");
    public string MainCauses => Pick("Main causes", "Главные причины");
    public string ManualCandidates => Pick("Manual review candidates", "Кандидаты на ручную проверку");
    public string ManualHint => Pick("Size and file name do not prove an item is disposable. Full list and risks are in Recommendations.", "Размер и имя файла не доказывают, что он не нужен. Полный список и риски — во вкладке «Рекомендации».");
    public string TreeHeader => Pick("Disk tree · size ↓", "Дерево диска · размер ↓");
    public string FolderDoubleClickTip => Pick("Double-click a folder to open its map in Overview", "Двойной клик по папке открывает её карту на вкладке «Обзор»");
    public string Folder => Pick("Folder", "Папка");
    public string Size => Pick("Size", "Размер");
    public string Files => Pick("Files", "Файлы");
    public string Folders => Pick("Folders", "Папки");
    public string UsedPercent => Pick("% used", "% занято");
    public string FullPath => Pick("Full path", "Полный путь");
    public string Modified => Pick("Modified", "Изменён");
    public string Access => Pick("Access", "Доступ");
    public string FileName => Pick("File name", "Имя файла");
    public string Extension => Pick("Extension", "Расширение");
    public string Category => Pick("Category", "Категория");
    public string ParentFolder => Pick("Parent folder", "Родительская папка");
    public string Purpose => Pick("Purpose", "Назначение");
    public string Risk => Pick("Risk", "Риск");
    public string TypesTitle => Pick("File types and extensions", "Типы файлов и расширения");
    public string TypesHint => Pick("All read files. One extension can have different categories depending on path. Click a column header to sort.", "Все прочитанные файлы. Одно расширение может иметь разные категории в зависимости от пути. Нажмите заголовок столбца для сортировки.");
    public string Count => Pick("Files", "Файлы");
    public string Total => Pick("Total", "Всего");
    public string Average => Pick("Average", "В среднем");
    public string Maximum => Pick("Maximum", "Максимум");
    public string LargestFile => Pick("Largest file", "Крупнейший файл");
    public string ExamplePaths => Pick("Example paths", "Примеры путей");
    public string ProgramsHint => Pick("Application folders, AppData and detected dependencies. This is an estimate by location, not an installed programs list; rows can overlap by size.", "Папки приложений, AppData и найденные зависимости. Это оценка по расположению, а не список установленных программ; строки могут пересекаться по размеру.");
    public string ProgramFolder => Pick("Program / folder", "Программа / папка");
    public string WhatToCheck => Pick("What to check", "Что проверить");
    public string RecommendationsTitle => Pick("What is worth checking manually", "Что стоит проверить вручную");
    public string RecommendationsWarning => Pick("Recommendations are not delete commands. Windows, Program Files, ProgramData, WinSxS, System32, Drivers, EFI/Boot, pagefile.sys, hiberfil.sys and swapfile.sys are high risk. DO NOT DELETE THEM MANUALLY.", "Рекомендации не являются командой к удалению. Windows, Program Files, ProgramData, WinSxS, System32, Drivers, EFI/Boot, pagefile.sys, hiberfil.sys и swapfile.sys — высокий риск, НЕ УДАЛЯТЬ ВРУЧНУЮ.");
    public string RecommendationsLimit => Pick("Up to 400 largest metadata-based recommendations. If the list is empty, no candidates were found or scanning has not run yet.", "До 400 крупнейших рекомендаций по метаданным. Если список пуст, подходящие кандидаты не найдены или сканирование ещё не запускалось.");
    public string Reason => Pick("Reason", "Причина");
    public string Warning => Pick("Warning", "Предупреждение");
    public string IssuesTitle => Pick("Could not read / skipped", "Не удалось прочитать / пропущено");
    public string IssuesHint => Pick("Protected folders and links are skipped. You can run as administrator for broader access; this is optional. Links are skipped at every access level.", "Защищённые папки и ссылки пропускаются. Для более полного доступа можно перезапустить приложение от имени администратора; это необязательно. Ссылки пропускаются при любом уровне доступа.");
    public string ObjectDetails => Pick("Object details", "Детали объекта");
    public string CopyPath => Pick("Copy path", "Копировать путь");
    public string OpenExplorer => Pick("Open in Explorer", "Открыть в Проводнике");
    public string MetadataOnly => Pick("Metadata only. File contents are not read.", "Только метаданные. Содержимое файлов не читается.");
    public string SizeNote => Pick("Logical sizes of available files are not the same as physical disk usage. Hard links can be counted more than once; compression, sparse files and NTFS service data are not counted as physical blocks.", "Логические размеры доступных файлов ≠ физическое место на диске. Hard links могут учитываться повторно; сжатие, sparse-файлы и служебные данные NTFS не учитываются как физические блоки.");
    public string SearchLabel => Pick("Search by name and full path", "Поиск по имени и полному пути");
    public string MinimumMb => Pick("Minimum, MB", "Минимум, MB");
    public string MinimumTip => Pick("100 / 500 MB · 1024 = 1 GB · 5120 = 5 GB · custom value", "100 / 500 MB · 1024 = 1 GB · 5120 = 5 GB · своё значение");
    public string ExtensionTip => Pick("Example: .mp4", "Например: .mp4");
    public string FileCategory => Pick("File category", "Категория файлов");
    public string Sorting => Pick("Sorting", "Сортировка");
    public string Reset => Pick("Reset", "Сбросить");
}
