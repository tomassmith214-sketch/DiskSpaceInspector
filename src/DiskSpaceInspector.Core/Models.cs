using System.Globalization;
using System.Text.Json.Serialization;

namespace DiskSpaceInspector.Core;

public static class SizeFormatter
{
    public static string Format(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = Math.Max(0, bytes); int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value.ToString(unit == 0 ? "0" : "0.##", CultureInfo.InvariantCulture)} {units[unit]}";
    }
}

public sealed class DiskItem
{
    public required string FullPath { get; init; }
    public string Name => Path.GetFileName(FullPath.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : FullPath;
    public string ParentPath => Path.GetDirectoryName(FullPath.TrimEnd('\\', '/')) ?? FullPath;
    public bool IsDirectory { get; init; }
    public string Kind => IsDirectory ? "Папка" : "Файл";
    public long Size { get; set; }
    public string SizeText => SizeFormatter.Format(Size);
    public long DirectSize { get; set; }
    public long FileCount { get; set; }
    public long FolderCount { get; set; }
    public DateTime? Created { get; init; }
    public DateTime? Modified { get; init; }
    public FileAttributes Attributes { get; init; }
    public string Extension => IsDirectory ? "—" : Path.GetExtension(FullPath).ToLowerInvariant() is { Length: > 0 } ext ? ext : "(без расширения)";
    public string Category { get; init; } = "Другое";
    public string Risk => Categorizer.Risk(FullPath, Attributes);
    public string Purpose => Categorizer.Purpose(FullPath, Category, Attributes);
    public string Advice => Categorizer.Advice(this);
    public string Status { get; set; } = "Доступно";
    public double PercentUsed { get; set; }
    public string PercentText => $"{PercentUsed:0.##}%";
    [JsonIgnore] public DiskItem? Parent { get; set; }
    [JsonIgnore] public List<DiskItem> Children { get; } = [];
    public string Tooltip => $"{FullPath}\n{SizeText} · {Size:N0} байт\n{Category} · {Kind}\nИзменён: {Modified:g}\n{Status}";
}

public sealed class ExtensionSummary
{
    public required string Extension { get; init; }
    public required string Category { get; init; }
    public long Count { get; set; }
    public long Size { get; set; }
    public long Largest { get; set; }
    public string LargestPath { get; set; } = "";
    public List<string> Examples { get; } = [];
    public string ExamplesText => string.Join("\n", Examples);
    public long Average => Count == 0 ? 0 : Size / Count;
    public string SizeText => SizeFormatter.Format(Size);
    public string AverageText => SizeFormatter.Format(Average);
    public string LargestText => SizeFormatter.Format(Largest);
}

public sealed record CategorySummary(string Category, long Size, long Count)
{
    public string SizeText => SizeFormatter.Format(Size);
    public string Color => Categorizer.Color(Category);
}
public sealed record ScanIssue(string FullPath, string Reason);
public sealed record Recommendation(string FullPath, long Size, string Category, string Risk, string Reason, string Check, string Warning)
{
    public string SizeText => SizeFormatter.Format(Size);
    [JsonIgnore] public DiskItem? Item { get; init; }
}
public sealed record ScanProgress(string CurrentPath, long Files, long Folders, long Bytes, TimeSpan Elapsed, int Issues);
public sealed class ScanResult
{
    public required string Drive { get; init; }
    public DateTime Started { get; init; } = DateTime.Now;
    public DateTime Finished { get; set; }
    public long Capacity { get; set; }
    public long Free { get; set; }
    public long Used => Capacity - Free;
    public bool Cancelled { get; set; }
    public required DiskItem Root { get; init; }
    public List<DiskItem> Folders { get; } = [];
    public List<DiskItem> Files { get; set; } = [];
    public List<ExtensionSummary> Extensions { get; set; } = [];
    public List<CategorySummary> Categories { get; set; } = [];
    public List<ScanIssue> Issues { get; } = [];
    public List<Recommendation> Recommendations { get; set; } = [];
    public int FileRetentionLimit { get; init; } = DiskScanner.FileLimit;
    public string SizeMeaning => "Логические размеры доступных файлов. Ссылки пропущены. Hard links могут учитываться повторно; сжатие, sparse-файлы и служебные данные NTFS не учитываются как физические блоки.";
}
