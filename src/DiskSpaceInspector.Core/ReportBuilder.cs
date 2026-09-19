using System.Net;
using System.Text;
using System.Text.Json;

namespace DiskSpaceInspector.Core;

public static class ReportBuilder
{
    public const int FolderLimit = 5000;
    private static object ItemData(DiskItem item) => new { item.FullPath, item.Kind, item.Size, item.FileCount, item.FolderCount, item.Category, item.Extension, item.Risk, item.Purpose, item.Modified, item.Created, item.Attributes, item.Status, item.PercentUsed, item.Advice };
    public static string Build(ScanResult result, string format) => format switch
    {
        "JSON" => JsonSerializer.Serialize(new
        {
            result.Started, result.Finished, result.Drive, result.Capacity, result.Free, result.Used, result.Cancelled,
            result.SizeMeaning, result.FileRetentionLimit,
            ScannedBytes = result.Root.Size, FilesRead = result.Root.FileCount, FoldersFound = result.Root.FolderCount,
            Warning = RecommendationService.Warning,
            FolderLimit,
            Scope = "До 5 000 крупнейших папок; до 50 000 крупнейших файлов; до 400 рекомендаций. Размеры вложенных папок пересекаются. Итоги расширений и категорий охватывают все прочитанные файлы.",
            Folders = result.Folders.OrderByDescending(x => x.Size).Take(FolderLimit).Select(ItemData), Files = result.Files.Select(ItemData), result.Extensions, result.Categories, result.Issues, result.Recommendations
        }, new JsonSerializerOptions { WriteIndented = true }),
        "CSV" => Csv(result), "HTML" => Html(result), _ => throw new ArgumentException("Неизвестный формат отчёта")
    };
    private static string Csv(ScanResult r)
    {
        var text = new StringBuilder();
        void Row(params object?[] cells) => text.AppendLine(string.Join(",", cells.Select(x => EscapeCsv(x?.ToString() ?? ""))));
        Row("Раздел", "Путь / ключ", "Размер (байт)", "Количество", "Категория", "Риск / статус", "Пояснение / значение");
        Row("Метаданные", "Диск", null, null, null, null, r.Drive);
        Row("Метаданные", "Начало", null, null, null, null, r.Started.ToString("O"));
        Row("Метаданные", "Конец", null, null, null, null, r.Finished.ToString("O"));
        Row("Метаданные", "Объём", r.Capacity); Row("Метаданные", "Свободно", r.Free); Row("Метаданные", "Занято", r.Used);
        Row("Метаданные", "Прочитано", r.Root.Size, r.Root.FileCount, null, r.Cancelled ? "Остановлено" : r.Root.Status, r.SizeMeaning);
        Row("Метаданные", "Предел файлов", null, r.FileRetentionLimit, null, null, "Таблица содержит крупнейшие файлы; агрегаты охватывают все прочитанные. Размеры вложенных папок пересекаются.");
        Row("Метаданные", "Предел папок в отчёте", null, FolderLimit);
        foreach (var item in r.Folders.OrderByDescending(x => x.Size).Take(FolderLimit).Concat(r.Files)) Row(item.Kind, item.FullPath, item.Size, item.FileCount, item.Category, item.Risk, $"{item.Status}; изменён {item.Modified:O}; {item.Advice}");
        foreach (var ext in r.Extensions) Row("Расширение", ext.Extension, ext.Size, ext.Count, ext.Category, null, $"Среднее {ext.Average}; максимум {ext.Largest}; {ext.LargestPath}; примеры: {ext.ExamplesText}");
        foreach (var cat in r.Categories) Row("Категория", cat.Category, cat.Size, cat.Count);
        foreach (var issue in r.Issues) Row("Не прочитано", issue.FullPath, null, null, null, null, issue.Reason);
        foreach (var rec in r.Recommendations) Row("Рекомендация", rec.FullPath, rec.Size, null, rec.Category, rec.Risk, $"{rec.Reason}; {rec.Check}; {rec.Warning}");
        return text.ToString();
    }
    private static string EscapeCsv(string value)
    {
        // Prevent spreadsheet formula execution, including after leading whitespace.
        if (value.TrimStart() is { Length: > 0 } trimmed && "=+-@".Contains(trimmed[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    private static string Html(ScanResult r)
    {
        static string E(object? value) => WebUtility.HtmlEncode(value?.ToString() ?? "");
        var s = new StringBuilder("<!doctype html><html lang=\"ru\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>Disk Space Inspector</title><style>body{background:#0d1422;color:#e6edf7;font:15px system-ui;margin:40px auto;max-width:1400px;padding:0 24px}h1{font-size:32px}h2{margin-top:36px;color:#55d8cb}p{line-height:1.6;color:#b0bed3}table{width:100%;border-collapse:collapse;background:#141f31}td,th{padding:12px;text-align:left;border-bottom:1px solid #29374d;overflow-wrap:anywhere}th{color:#96aac7}small{color:#adbcd0}.notice{padding:18px;border:1px solid #355c68;border-radius:12px}</style>");
        s.Append($"<h1>Disk Space Inspector — {E(r.Drive)}</h1><p class=notice>Только чтение · {E(r.Started)} — {E(r.Finished)} · {(r.Cancelled ? "Остановлено: частичные результаты" : E(r.Root.Status))}<br>{E(r.SizeMeaning)}<br>{E(RecommendationService.Warning)}</p>");
        s.Append($"<p>Объём: {E(SizeFormatter.Format(r.Capacity))} · Свободно: {E(SizeFormatter.Format(r.Free))} · Занято: {E(SizeFormatter.Format(r.Used))}<br>Прочитано: {E(r.Root.SizeText)} · Файлов: {r.Root.FileCount:N0} · Папок: {r.Root.FolderCount:N0}</p><p>В отчёте до {FolderLimit:N0} крупнейших папок и до {r.FileRetentionLimit:N0} крупнейших файлов. Размеры вложенных папок пересекаются. До 400 рекомендаций; сводки охватывают все прочитанные файлы.</p>");
        void Start(string title, params string[] headers) { s.Append($"<h2>{E(title)}</h2><table><thead><tr>"); foreach (var h in headers) s.Append($"<th>{E(h)}</th>"); s.Append("</tr></thead><tbody>"); }
        void Row(params object?[] cells) { s.Append("<tr>"); foreach (var c in cells) s.Append($"<td>{E(c)}</td>"); s.Append("</tr>"); }
        void End() => s.Append("</tbody></table>");
        Start("Папки", "Полный путь", "Размер", "Байт", "Файлы", "Статус / риск");
        foreach (var f in r.Folders.OrderByDescending(x => x.Size).Take(FolderLimit)) Row(f.FullPath, f.SizeText, f.Size, f.FileCount, $"{f.Status}; {f.Risk}; {f.Advice}"); End();
        Start("Крупнейшие файлы", "Полный путь", "Размер", "Байт", "Категория", "Риск");
        foreach (var f in r.Files) Row(f.FullPath, f.SizeText, f.Size, f.Category, f.Risk); End();
        Start("Расширения", "Расширение", "Категория", "Количество", "Размер", "Среднее", "Максимум / путь", "Примеры");
        foreach (var x in r.Extensions) Row(x.Extension, x.Category, x.Count, x.SizeText, x.AverageText, $"{x.LargestText} / {x.LargestPath}", x.ExamplesText); End();
        Start("Категории", "Категория", "Размер", "Файлы"); foreach (var x in r.Categories) Row(x.Category, x.SizeText, x.Count); End();
        Start("Не удалось прочитать / пропущено", "Путь", "Причина"); foreach (var x in r.Issues) Row(x.FullPath, x.Reason); End();
        Start("Что проверить вручную", "Путь", "Размер", "Категория", "Риск", "Причина", "Что проверить", "Предупреждение");
        foreach (var x in r.Recommendations) Row(x.FullPath, x.SizeText, x.Category, x.Risk, x.Reason, x.Check, x.Warning); End();
        return s.Append("</html>").ToString();
    }
}
