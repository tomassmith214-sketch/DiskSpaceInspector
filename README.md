<p align="center">
  <img src="assets/app-icon.png" alt="DiskSpaceInspector app icon" width="160">
</p>

# DiskSpaceInspector

DiskSpaceInspector is a native Windows disk space analyzer built with WPF and .NET 8. It scans local fixed or removable drives, shows folder and file usage, visualizes categories, and gives conservative manual-review recommendations. The app starts in English and includes a RU/EN language toggle, dark and light themes, and a read-only analysis model.

## Build And Run

Requirements:

- Windows 10 or Windows 11
- .NET SDK 8 or newer
- .NET Desktop Runtime 8 x64 for running published builds

```powershell
dotnet build DiskSpaceInspector.sln -c Release
dotnet run --project src/DiskSpaceInspector -c Release
dotnet test tests/DiskSpaceInspector.Tests -c Release
```

The development executable is created at:

```text
src\DiskSpaceInspector\bin\Release\net8.0-windows\DiskSpaceInspector.exe
```

The application and core library have no third-party runtime dependencies. MVVM uses a small local Observable/Command implementation, and charts are rendered with native WPF drawing. NuGet packages are used only by the test project.

## Features

- **Overview:** drive metrics, folder treemap, Top 20 folders/files, category donut chart, main causes of used space, and recommendation preview.
- **Folders:** full lazy folder tree, sortable folder table, access status, child counts, disk-used percentage, and double-click navigation back to the overview map.
- **Files:** largest retained files with path, size, extension, category, purpose, and risk.
- **File types:** extension summaries with counts, totals, averages, largest file, and example paths.
- **Programs:** detected Program Files, ProgramData, AppData, cache, and dependency folders. This is path-based analysis, not a registry-based installed-program list.
- **Recommendations:** cautious metadata-based candidates for manual review, with risk and warning text. Recommendations are not delete commands.
- **Details panel:** exact bytes, dates, attributes, full path, copy-path action, and explicit Explorer opening for the selected item.
- **Export:** CSV, JSON, and standalone HTML reports after the user explicitly chooses an output folder.

Reports include up to 5,000 largest folders, up to 50,000 retained largest files, up to 400 recommendations, category and extension summaries, and scan issues.

Search, minimum size, and sorting apply to folder, file, and program tables. Extension and category filters apply to files. The tree remains complete. Table size columns sort numerically.

## Read-Only Safety

DiskSpaceInspector does not delete, move, rename, clean, or modify scanned files, file attributes, ACLs, registry keys, settings, or programs. It does not run cleanup commands, cmd, or PowerShell. It reads filesystem metadata only: path, logical length, extension, dates, attributes, and directory entries.

Relevant implementation boundaries:

- `DiskSpaceInspector.Core/DiskScanner.cs` uses `DirectoryInfo` and `FileInfo` metadata APIs. Reparse points, junctions, file links, and cloud placeholders are skipped.
- `DiskSpaceInspector.Core/ReportBuilder.cs` builds report strings in memory. HTML output is encoded and CSV output is guarded against formula interpretation.
- `DiskSpaceInspector/MainViewModel.cs`, `ExportAsync` is the only application filesystem write path, reached only after the user chooses a report folder. It uses `FileMode.CreateNew`.
- `OpenExplorer` opens Explorer for the selected item; it does not perform file-changing operations.

Windows and .NET may perform their normal runtime operations when any desktop application starts. The read-only guarantee refers to DiskSpaceInspector's own behavior. Building and testing the source creates normal project binaries and test artifacts.

## MVP Limits

- All accessible sizes are included in aggregates, but the UI keeps the 50,000 largest files for file-table search and detail browsing.
- The treemap shows up to 39 largest elements in a folder and groups the rest.
- Sizes are logical file sizes, not physically allocated NTFS blocks. Hard links can be counted more than once; compression, sparse files, VSS, NTFS metadata, and inaccessible files can differ from Windows Explorer's used-space numbers.
- Access errors and disappearing files do not stop the scan. Partial results are marked when scanning is cancelled.
- Cancellation is checked between metadata operations; a synchronous filesystem call already in progress cannot be interrupted instantly.
- Administrator rights are not required. Running as administrator may allow more folders to be read, but the app never elevates itself.
- Network drives are excluded.
- File contents are not read, duplicate detection is not performed, and installed programs are inferred by path rather than registry data.

## Project Layout

```text
src/DiskSpaceInspector.Core   Models, scanner, aggregation, categorization, recommendations, reports
src/DiskSpaceInspector        WPF app, MVVM, export dialog, charts, interactions
tests/DiskSpaceInspector.Tests
                              Unit tests for scanner behavior, categories, risk, reports, cancellation
tools/VisualSmoke             UI smoke-test utility that writes screenshots into ignored artifacts/
```

## License

MIT. See [LICENSE](LICENSE).
