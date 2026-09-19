using System.Text.Json;
using DiskSpaceInspector.Core;
using Xunit;

namespace DiskSpaceInspector.Tests;

public class CoreTests
{
    [Theory]
    [InlineData(0, "0 B")][InlineData(1023, "1023 B")][InlineData(1024, "1 KB")]
    [InlineData(870400, "850 KB")][InlineData(161690419, "154.2 MB")]
    [InlineData(3049426780, "2.84 GB")][InlineData(1264438371942, "1.15 TB")]
    public void FormatsBinarySizes(long bytes, string expected) => Assert.Equal(expected, SizeFormatter.Format(bytes));

    [Theory]
    [InlineData(@"C:\Users\Alice\movie.MKV", "Видео")]
    [InlineData(@"C:\Users\Alice\archive.zip", "Архивы")]
    [InlineData(@"C:\Images\disk.iso", "Образы дисков")]
    [InlineData(@"C:\Users\Alice\data.sqlite", "Базы данных")]
    [InlineData(@"C:\Users\Alice\report.pdf", "Документы")]
    [InlineData(@"C:\Users\Alice\AppData\Local\Cache\thing.db", "Кэш")]
    [InlineData(@"C:\dev\node_modules\file.js", "Разработка")]
    [InlineData(@"C:\Windows\System32\thing.dll", "Системные данные")]
    [InlineData(@"C:\somewhere\noextension", "Другое")]
    public void CategorizesActualPaths(string path, string category) => Assert.Equal(category, Categorizer.Category(path));

    [Theory]
    [InlineData(@"C:\Windows\WinSxS\thing", "Высокий")]
    [InlineData(@"C:\Program Files\App\data.db", "Высокий")]
    [InlineData(@"C:\ProgramData\cache", "Высокий")]
    [InlineData(@"C:\pagefile.sys", "Высокий")]
    [InlineData(@"C:\hiberfil.sys", "Высокий")]
    [InlineData(@"C:\swapfile.sys", "Высокий")]
    [InlineData(@"C:\EFI\data", "Высокий")]
    [InlineData(@"C:\Users\Alice\AppData\Local\App", "Средний")]
    [InlineData(@"C:\Users\Alice\WindowsHoliday.mp4", "Не определён")]
    public void RiskUsesPathSegmentsAndCriticalNames(string path, string risk) => Assert.Equal(risk, Categorizer.Risk(path));

    [Fact] public void SystemAttributeIsHighRisk() => Assert.Equal("Высокий", Categorizer.Risk(@"C:\Unknown\data", FileAttributes.System));
    [Theory]
    [InlineData(FileAttributes.Directory, true)]
    [InlineData(FileAttributes.Directory | FileAttributes.ReparsePoint, false)]
    [InlineData(FileAttributes.ReparsePoint, false)]
    [InlineData(FileAttributes.Hidden | FileAttributes.Directory, true)]
    public void NeverTraversesReparsePoints(FileAttributes attributes, bool expected) => Assert.Equal(expected, DiskScanner.ShouldTraverse(attributes));

    [Fact]
    public void AggregatesChildrenWithoutDoubleCounting()
    {
        var root = new DiskItem { FullPath = @"C:\", IsDirectory = true, DirectSize = 5, FileCount = 1 };
        var child = new DiskItem { FullPath = @"C:\data", IsDirectory = true, Parent = root, DirectSize = 10, FileCount = 2 };
        var deep = new DiskItem { FullPath = @"C:\data\deep", IsDirectory = true, Parent = child, DirectSize = 20, FileCount = 3, Status = "Ограниченный доступ" };
        Aggregator.AggregateFolders([root, child, deep], 100);
        Assert.Equal(35, root.Size); Assert.Equal(30, child.Size); Assert.Equal(20, deep.Size);
        Assert.Equal(6, root.FileCount); Assert.Equal(2, root.FolderCount); Assert.Equal(35, root.PercentUsed);
        Assert.Contains("Частично", root.Status);
    }

    [Theory]
    [InlineData(@"C:\Temp\old.tmp", -31, true)]
    [InlineData(@"C:\Temp\new.tmp", -2, false)]
    [InlineData(@"C:\Users\Alice\old.tmp", -60, false)]
    [InlineData(@"C:\Windows\Temp\old.tmp", -60, false)]
    public void TempRecommendationRequiresOldTempAndNonSystemLocation(string path, int ageDays, bool expected)
    {
        var now = new DateTime(2026, 9, 19);
        var item = new DiskItem { FullPath = path, Category = Categorizer.Category(path), Modified = now.AddDays(ageDays), Size = 1024 };
        Assert.Equal(expected, RecommendationService.ForFile(item, now) is not null);
    }
    [Fact]
    public void ScanReadsMetadataAndCancellationProducesConsistentPartialTotals()
    {
        using var fixture = new Fixture();
        var root = fixture.Path;
        Directory.CreateDirectory(System.IO.Path.Combine(root, "child"));
        File.WriteAllBytes(System.IO.Path.Combine(root, "one.txt"), new byte[17]);
        File.WriteAllBytes(System.IO.Path.Combine(root, "child", "two.mp4"), new byte[29]);
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(x => x, File.ReadAllBytes);
        var scanner = new DiskScanner();
        var result = scanner.Scan(root, 1000, 100, null, CancellationToken.None);
        Assert.Equal(46, result.Root.Size); Assert.Equal(2, result.Root.FileCount); Assert.Equal(1, result.Root.FolderCount);
        Assert.Equal(result.Root.Size, result.Categories.Sum(x => x.Size)); Assert.Equal(result.Root.Size, result.Extensions.Sum(x => x.Size));
        Assert.Equal(29, result.Files[0].Size); Assert.Empty(result.Issues);
        foreach (var (path, content) in before) Assert.Equal(content, File.ReadAllBytes(path));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var cancelled = scanner.Scan(root, 1000, 100, null, cancellation.Token);
        Assert.True(cancelled.Cancelled); Assert.Equal(0, cancelled.Root.Size);
        Assert.Contains("остановлено", cancelled.Root.Status);
    }
    [Fact]
    public void CancellationDuringTraversalPreservesAccounting()
    {
        using var fixture = new Fixture();
        for (int i = 0; i < 20; i++) File.WriteAllBytes(System.IO.Path.Combine(fixture.Path, $"{i}.bin"), new byte[7]);
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(_ => cancellation.Cancel());
        var result = new DiskScanner().Scan(fixture.Path, 1000, 100, progress, cancellation.Token);
        Assert.True(result.Cancelled); Assert.True(result.Root.FileCount < 20);
        Assert.Equal(result.Root.FileCount * 7, result.Root.Size);
        Assert.Equal(result.Root.Size, result.Extensions.Sum(x => x.Size));
        Assert.Equal(result.Root.Size, result.Categories.Sum(x => x.Size));
    }
    private sealed class InlineProgress(Action<ScanProgress> action) : IProgress<ScanProgress>
    { public void Report(ScanProgress value) => action(value); }

    [Fact]
    public async Task RejectsNonRootAndNetworkInput()
    {
        var scanner = new DiskScanner();
        await Assert.ThrowsAsync<ArgumentException>(() => scanner.ScanAsync(@"C:\Windows", null, CancellationToken.None));
    }
    [Fact]
    public void MissingFolderIsReportedRatherThanCrashing()
    {
        using var fixture = new Fixture();
        var result = new DiskScanner().Scan(System.IO.Path.Combine(fixture.Path, "missing"), 1000, 100, null, CancellationToken.None);
        Assert.Single(result.Issues); Assert.Equal(0, result.Root.Size); Assert.Equal("Ошибка чтения", result.Root.Status);
    }
    [Fact]
    public void ReportsEscapeMarkupAndSpreadsheetFormulas()
    {
        var root = new DiskItem { FullPath = @"C:\", IsDirectory = true };
        var result = new ScanResult { Drive = @"C:\", Root = root };
        result.Files.Add(new() { FullPath = "=HYPERLINK(test)", Size = 100, Category = "<script>alert(1)</script>" });
        string html = ReportBuilder.Build(result, "HTML"), csv = ReportBuilder.Build(result, "CSV");
        Assert.DoesNotContain("<script>", html); Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("'\u003dHYPERLINK(test)", csv);
        using var json = JsonDocument.Parse(ReportBuilder.Build(result, "JSON"));
        Assert.Equal(@"C:\", json.RootElement.GetProperty("Drive").GetString());
        Assert.Equal(50_000, json.RootElement.GetProperty("FileRetentionLimit").GetInt32());
    }
    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DiskSpaceInspector.Tests", Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
