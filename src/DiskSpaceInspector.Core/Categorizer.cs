namespace DiskSpaceInspector.Core;

public static class Categorizer
{
    private static readonly Dictionary<string, string> Extensions = BuildExtensions();
    private static Dictionary<string, string> BuildExtensions()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string category, string extensions) { foreach (var ext in extensions.Split(' ')) result[ext] = category; }
        Add("Видео", ".mp4 .mkv .avi .mov .webm .wmv .m4v");
        Add("Архивы", ".zip .rar .7z .tar .gz .bz2 .xz");
        Add("Образы дисков", ".iso .vhd .vhdx .img .vmdk");
        Add("Приложения", ".exe .msi .msp .dll .appx .msix");
        Add("Документы", ".pdf .doc .docx .xls .xlsx .ppt .pptx .txt .rtf .odt");
        Add("Изображения", ".jpg .jpeg .png .webp .psd .gif .bmp .svg .heic .tif");
        Add("Аудио", ".mp3 .flac .wav .m4a .ogg .aac");
        Add("Базы данных", ".db .sqlite .sqlite3 .mdf .ldf");
        Add("Логи", ".log .etl"); Add("Временные", ".tmp .bak .old"); Add("Дампы", ".dmp .mdmp");
        Add("Системные данные", ".sys .efi");
        return result;
    }
    public static string[] Segments(string path) => path.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
    public static bool HasSegment(string path, params string[] values) => Segments(path).Any(s => values.Contains(s, StringComparer.OrdinalIgnoreCase));
    public static bool IsProtected(string path, FileAttributes attributes = 0) =>
        (attributes & FileAttributes.System) != 0 || HasSegment(path, "Windows", "Program Files", "Program Files (x86)", "ProgramData", "System Volume Information", "Recovery", "EFI", "Boot", "System32", "WinSxS", "Drivers") ||
        new[] { "pagefile.sys", "hiberfil.sys", "swapfile.sys", "bootmgr" }.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);
    public static bool IsCache(string path) => HasSegment(path, "cache", "caches", "code cache", "gpucache", "pip-cache");
    public static bool IsDevelopment(string path) => HasSegment(path, "node_modules", ".nuget", "packages", ".gradle", ".m2", "Docker", "pip");
    public static string Category(string path, bool directory = false)
    {
        if (HasSegment(path, "Windows", "System Volume Information", "Recovery", "EFI", "Boot")) return "Системные данные";
        if (IsCache(path)) return "Кэш";
        if (IsDevelopment(path)) return "Разработка";
        if (directory)
        {
            if (HasSegment(path, "Temp", "Tmp")) return "Временные";
            if (HasSegment(path, "Program Files", "Program Files (x86)")) return "Приложения";
            if (HasSegment(path, "AppData", "ProgramData")) return "Данные приложений";
            return "Другое";
        }
        return Extensions.GetValueOrDefault(Path.GetExtension(path), "Другое");
    }
    public static string Risk(string path, FileAttributes attributes = 0) => IsProtected(path, attributes) ? "Высокий" : HasSegment(path, "AppData", "$Recycle.Bin") || IsDevelopment(path) || IsCache(path) ? "Средний" : "Не определён";
    public static string Purpose(string path, string category, FileAttributes attributes) => IsProtected(path, attributes) ? "Системный / приложение" : category == "Временные" ? "Временный" : HasSegment(path, "AppData") ? "Данные приложения" : HasSegment(path, "Users") ? "Пользовательский" : "Неизвестно";
    public static string Advice(DiskItem item)
    {
        if (IsProtected(item.FullPath, item.Attributes)) return "НЕ УДАЛЯТЬ ВРУЧНУЮ. Системный компонент или данные установленного приложения. Используйте только официальные средства после изучения последствий.";
        if (IsCache(item.FullPath)) return "Вероятный кэш — проверьте назначение и возможность управления через настройки приложения.";
        if (IsDevelopment(item.FullPath)) return "Зависимости или данные инструментов разработки. Проверьте используемые проекты и документацию инструмента; здесь могут быть рабочие данные.";
        if (HasSegment(item.FullPath, "AppData")) return "Данные приложения — не удаляйте вручную без понимания назначения. Возможны настройки, профили и локальные базы.";
        return item.Category switch
        {
            "Видео" => "Большой видеофайл — проверьте, нужен ли он, и наличие резервной копии.",
            "Архивы" => "Архив — проверьте резервную копию и распакованное содержимое.",
            "Образы дисков" => "Образ диска — проверьте, не используется ли он виртуальной машиной и содержит ли уникальные данные.",
            "Дампы" => "Дамп может быть нужен для диагностики. Проверьте, завершено ли расследование сбоя.",
            "Логи" => "Журнал может быть нужен приложению или для диагностики. Проверьте срок хранения и настройки ротации.",
            "Временные" => "Назначение предполагается по пути и расширению. Проверьте, не используется ли файл приложением; имя не гарантирует безопасность.",
            _ => "Назначение нельзя достоверно определить по метаданным. Проверьте вручную; размер сам по себе не означает, что объект не нужен."
        };
    }
    public static string Color(string category) => category switch
    {
        "Видео" => "#8B86FF", "Архивы" => "#F5B65A", "Образы дисков" => "#DC9968", "Приложения" => "#559CFA",
        "Системные данные" => "#7487AB", "Кэш" => "#3ED6C6", "Документы" => "#62B4FF", "Изображения" => "#EC91C3",
        "Аудио" => "#BD8EF4", "Базы данных" => "#4CB4A8", "Временные" => "#D3BB65", "Логи" => "#91B06B", "Дампы" => "#D77F85", "Разработка" => "#71C09D", _ => "#8896AE"
    };
}
