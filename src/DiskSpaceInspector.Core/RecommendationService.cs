namespace DiskSpaceInspector.Core;

public static class RecommendationService
{
    public const string Warning = "Только повод для ручной проверки. Рекомендация не является командой к удалению; приложение ничего не изменяет.";
    public static Recommendation? ForFile(DiskItem item, DateTime now)
    {
        if (Categorizer.IsProtected(item.FullPath, item.Attributes)) return null;
        bool old = item.Modified is { } modified && modified < now.AddDays(-30);
        bool large = item.Size >= 100L * 1024 * 1024;
        string? reason = item.Category switch
        {
            "Временные" when item.Extension == ".tmp" && old && Categorizer.HasSegment(item.FullPath, "Temp", "Tmp") => "Файл .tmp в Temp не менялся более 30 дней",
            "Логи" when old && item.Size >= 10 * 1024 * 1024 => "Журнал больше 10 MB не менялся более 30 дней",
            "Дампы" => "Найден диагностический дамп",
            "Видео" when large => "Видео больше 100 MB",
            "Архивы" when large => "Архив больше 100 MB",
            "Образы дисков" when large => "Образ диска больше 100 MB",
            "Приложения" when old && large && item.Extension is ".exe" or ".msi" && Categorizer.HasSegment(item.FullPath, "Downloads") => "В Downloads найден большой исполняемый файл старше 30 дней; возможно, установщик",
            _ => null
        };
        if (reason is null && large && Categorizer.HasSegment(item.FullPath, "Downloads")) reason = "Крупный файл в Downloads";
        if (reason is null) return null;
        string check = item.Category == "Приложения" ? "Проверьте, является ли файл установщиком, установлена ли программа и можно ли повторно получить дистрибутив." : item.Advice;
        return new(item.FullPath, item.Size, item.Category, "Средний", reason, check, Warning) { Item = item };
    }
    public static IEnumerable<Recommendation> ForFolders(IEnumerable<DiskItem> folders) => folders
        .Where(x => x.Parent is not null && x.Size > 0 && (Categorizer.IsCache(x.FullPath) && !Categorizer.IsCache(x.Parent.FullPath) ||
            Categorizer.HasSegment(x.FullPath, "$Recycle.Bin") && !Categorizer.HasSegment(x.Parent.FullPath, "$Recycle.Bin")))
        .OrderByDescending(x => x.Size).Take(100)
        .Select(x => new Recommendation(x.FullPath, x.Size, x.Category, Categorizer.IsProtected(x.FullPath, x.Attributes) ? "Высокий" : "Средний",
            Categorizer.HasSegment(x.FullPath, "$Recycle.Bin") ? "Доступная часть Корзины; размер для сведения" : "Имя каталога похоже на кэш",
            Categorizer.HasSegment(x.FullPath, "$Recycle.Bin") ? "Проверьте содержимое через интерфейс Корзины. Здесь могут находиться нужные файлы." : x.Advice, Warning) { Item = x });
    public static bool IsPackage(DiskItem item)
    {
        if (!item.IsDirectory || item.Parent is null) return false;
        string parent = item.Parent.Name;
        return new[] { "Program Files", "Program Files (x86)", "ProgramData" }.Contains(parent, StringComparer.OrdinalIgnoreCase) ||
            (parent.Equals("Local", StringComparison.OrdinalIgnoreCase) || parent.Equals("Roaming", StringComparison.OrdinalIgnoreCase)) && Categorizer.HasSegment(item.FullPath, "AppData") ||
            Categorizer.IsDevelopment(item.FullPath) && !Categorizer.IsDevelopment(item.Parent.FullPath) ||
            Categorizer.IsCache(item.FullPath) && !Categorizer.IsCache(item.Parent.FullPath);
    }
}
