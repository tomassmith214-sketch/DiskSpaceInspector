using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security;

[assembly: InternalsVisibleTo("DiskSpaceInspector.Tests")]
[assembly: InternalsVisibleTo("VisualSmoke")]
namespace DiskSpaceInspector.Core;

public sealed class DiskScanner
{
    public const int FileLimit = 50_000;
    public static bool ShouldTraverse(FileAttributes attributes) => (attributes & FileAttributes.ReparsePoint) == 0;

    public Task<ScanResult> ScanAsync(string root, IProgress<ScanProgress>? progress, CancellationToken token)
    {
        var drive = new DriveInfo(root);
        if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable) ||
            !string.Equals(Path.GetFullPath(root), drive.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Выберите корень доступного локального диска.");
        return Task.Run(() => Scan(root, drive.TotalSize, drive.TotalFreeSpace, progress, token));
    }

    internal ScanResult Scan(string rootPath, long capacity, long free, IProgress<ScanProgress>? progress, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        var root = CreateRoot(rootPath);
        var result = new ScanResult { Drive = rootPath, Root = root, Capacity = capacity, Free = free };
        result.Folders.Add(root);
        var largest = new PriorityQueue<DiskItem, long>();
        var recommendations = new PriorityQueue<Recommendation, long>();
        var extensions = new Dictionary<string, ExtensionSummary>(StringComparer.OrdinalIgnoreCase);
        var categories = new Dictionary<string, (long Size, long Count)>();
        var stack = new Stack<(DiskItem Folder, IEnumerator<FileSystemInfo> Entries)>();
        long files = 0, bytes = 0; double lastProgress = -1000;
        string currentPath = rootPath;

        void Report(bool force = false)
        {
            if (!force && clock.Elapsed.TotalMilliseconds - lastProgress < 150) return;
            lastProgress = clock.Elapsed.TotalMilliseconds;
            progress?.Report(new(currentPath, files, result.Folders.Count - 1, bytes, clock.Elapsed, result.Issues.Count));
        }
        void Issue(DiskItem folder, string path, Exception exception)
        {
            folder.Status = exception is UnauthorizedAccessException or SecurityException ? "Ограниченный доступ" : "Ошибка чтения";
            result.Issues.Add(new(path, $"{folder.Status}: {exception.Message}"));
        }
        void Open(DiskItem folder)
        {
            try
            {
                var info = new DirectoryInfo(folder.FullPath);
                var attributes = info.Attributes;
                if (attributes == (FileAttributes)(-1)) throw new DirectoryNotFoundException("Каталог исчез или недоступен.");
                if (!ShouldTraverse(attributes))
                {
                    folder.Status = "Ссылка пропущена";
                    result.Issues.Add(new(folder.FullPath, "Reparse point: переход по ссылке отключён.")); return;
                }
                stack.Push((folder, info.EnumerateFileSystemInfos("*", new EnumerationOptions
                {
                    RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0, ReturnSpecialDirectories = false
                }).GetEnumerator()));
            }
            catch (Exception ex) when (IsReadError(ex)) { Issue(folder, folder.FullPath, ex); }
        }

        Open(root);
        try
        {
            while (stack.Count > 0)
            {
                if (token.IsCancellationRequested) { result.Cancelled = true; break; }
                var frame = stack.Peek();
                FileSystemInfo entry;
                try
                {
                    if (!frame.Entries.MoveNext()) { frame.Entries.Dispose(); stack.Pop(); continue; }
                    entry = frame.Entries.Current;
                }
                catch (Exception ex) when (IsReadError(ex))
                { Issue(frame.Folder, frame.Folder.FullPath, ex); frame.Entries.Dispose(); stack.Pop(); continue; }
                currentPath = entry.FullName;
                Report();
                try
                {
                    var attributes = entry.Attributes;
                    if (attributes == (FileAttributes)(-1)) throw new FileNotFoundException("Объект исчез во время сканирования.", entry.FullName);
                    bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                    if (!ShouldTraverse(attributes))
                    {
                        result.Issues.Add(new(entry.FullName, "Reparse point: ссылка / junction / облачный placeholder пропущен; содержимое не читается."));
                        frame.Folder.Status = "Частично: пропущены ссылки";
                        if (isDirectory)
                        {
                            var link = new DiskItem { FullPath = entry.FullName, IsDirectory = true, Attributes = attributes, Status = "Ссылка пропущена", Parent = frame.Folder };
                            frame.Folder.Children.Add(link); result.Folders.Add(link);
                        }
                        continue;
                    }
                    var item = new DiskItem
                    {
                        FullPath = entry.FullName, IsDirectory = isDirectory, Attributes = attributes,
                        Created = entry.CreationTime, Modified = entry.LastWriteTime,
                        Category = Categorizer.Category(entry.FullName, isDirectory), Parent = frame.Folder,
                        Size = isDirectory ? 0 : ((FileInfo)entry).Length
                    };
                    if (isDirectory)
                    {
                        frame.Folder.Children.Add(item); result.Folders.Add(item); Open(item);
                    }
                    else
                    {
                        files++; bytes += item.Size;
                        frame.Folder.DirectSize += item.Size; frame.Folder.FileCount++;
                        KeepLargest(largest, item, item.Size, FileLimit);
                        // Extension + category retains path-based classification without losing extension totals.
                        string key = item.Extension + "|" + item.Category;
                        if (!extensions.TryGetValue(key, out var summary))
                        { summary = new() { Extension = item.Extension, Category = item.Category }; extensions.Add(key, summary); }
                        summary.Count++; summary.Size += item.Size;
                        if (summary.Count == 1 || item.Size > summary.Largest) { summary.Largest = item.Size; summary.LargestPath = item.FullPath; }
                        if (summary.Examples.Count < 3) summary.Examples.Add(item.FullPath);
                        var category = categories.GetValueOrDefault(item.Category);
                        categories[item.Category] = (category.Size + item.Size, category.Count + 1);
                        if (RecommendationService.ForFile(item, result.Started) is { } recommendation)
                            KeepLargest(recommendations, recommendation, recommendation.Size, 300);
                    }
                }
                catch (Exception ex) when (IsReadError(ex)) { Issue(frame.Folder, currentPath, ex); }
            }
        }
        finally { while (stack.TryPop(out var frame)) frame.Entries.Dispose(); }
        result.Cancelled |= token.IsCancellationRequested;
        if (result.Cancelled)
        {
            foreach (var folder in result.Folders.Where(f => f.Status == "Доступно")) folder.Status = "Неполные данные: сканирование остановлено";
        }
        Aggregator.AggregateFolders(result.Folders, result.Used);
        result.Files = largest.UnorderedItems.Select(x => x.Element).OrderByDescending(x => x.Size).ToList();
        foreach (var file in result.Files) file.PercentUsed = result.Used > 0 ? 100d * file.Size / result.Used : 0;
        result.Extensions = extensions.Values.OrderByDescending(x => x.Size).ToList();
        result.Categories = categories.Select(x => new CategorySummary(x.Key, x.Value.Size, x.Value.Count)).OrderByDescending(x => x.Size).ToList();
        result.Recommendations = recommendations.UnorderedItems.Select(x => x.Element)
            .Concat(RecommendationService.ForFolders(result.Folders)).OrderByDescending(x => x.Size).Take(400).ToList();
        foreach (var recommendation in result.Recommendations)
            if (recommendation.Item is { } item) item.PercentUsed = result.Used > 0 ? 100d * item.Size / result.Used : 0;
        result.Finished = DateTime.Now; Report(true);
        return result;
    }
    private static bool IsReadError(Exception ex) => ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException;
    private static DiskItem CreateRoot(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            if (info.Attributes != (FileAttributes)(-1)) return new DiskItem { FullPath = path, IsDirectory = true, Category = Categorizer.Category(path, true), Created = info.CreationTime, Modified = info.LastWriteTime, Attributes = info.Attributes };
        }
        catch (Exception ex) when (IsReadError(ex)) { /* Open records access errors in the result. */ }
        return new DiskItem { FullPath = path, IsDirectory = true, Category = Categorizer.Category(path, true) };
    }
    private static void KeepLargest<T>(PriorityQueue<T, long> queue, T item, long size, int limit)
    { if (queue.Count < limit) queue.Enqueue(item, size); else if (queue.TryPeek(out _, out var min) && size > min) queue.EnqueueDequeue(item, size); }
}

public static class Aggregator
{
    // Scan order is parent-before-child. Reverse reduction uses one writer and no UI-shared state.
    public static void AggregateFolders(IReadOnlyList<DiskItem> folders, long used)
    {
        for (int index = folders.Count - 1; index >= 0; index--)
        {
            var item = folders[index]; item.Size += item.DirectSize;
            item.PercentUsed = used > 0 ? 100d * item.Size / used : 0;
            if (item.Parent is not { } parent) continue;
            parent.Size += item.Size; parent.FileCount += item.FileCount; parent.FolderCount += 1 + item.FolderCount;
            if (item.Status != "Доступно" && parent.Status == "Доступно") parent.Status = "Частично: есть непрочитанные объекты";
        }
    }
}
