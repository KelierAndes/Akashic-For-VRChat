using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Akashic.Core.Models;
using Akashic.Core.Services;

namespace Akashic.ViewModels;

/// <summary>
/// ViewModel for adding .unitypackage files to the package library. Browsing and removing library
/// entries lives on the 资源 page, which is where the package list itself is managed.
/// </summary>
public partial class ImportPackageViewModel : ObservableObject
{
    private readonly IUnityPackageExtractor _extractor;
    private readonly IUnityPackageLibraryService _libraryService;

    [ObservableProperty]
    private UnityPackageArchive? _archive;

    [ObservableProperty]
    private ObservableCollection<UnityPackageEntry> _entries = [];

    [ObservableProperty]
    private string _librarySummary = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _packageInfo = string.Empty;

    [ObservableProperty]
    private string _pickedFileName = string.Empty;

    public ImportPackageViewModel(IUnityPackageExtractor extractor, IUnityPackageLibraryService libraryService)
    {
        _extractor = extractor;
        _libraryService = libraryService;
    }

    [RelayCommand]
    public async Task LoadLibraryAsync()
    {
        IsLoading = true;
        try
        {
            var files = await _libraryService.ScanAsync();
            var folders = _libraryService.GetLibraryFolders();
            var manual = _libraryService.GetAddedFiles();

            LibrarySummary = $"包列表共 {files.Count} 个 .unitypackage · 库目录 {folders.Count} 个 · 手动添加 {manual.Count} 个";
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"读取包列表失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Adds picked .unitypackage files to the library so they appear in project package lists.</summary>
    [RelayCommand]
    public async Task AddFilesToLibraryAsync(IEnumerable<string> filePaths)
    {
        var added = 0;
        foreach (var path in filePaths)
        {
            if (!File.Exists(path)) continue;
            await _libraryService.AddPackageFileAsync(path);
            added++;
        }

        await LoadLibraryAsync();
        StatusMessage = added == 0
            ? "没有文件被加入包列表"
            : $"已将 {added} 个文件加入包列表，可在项目详情页的包列表中安装";
    }

    /// <summary>Previews the assets inside a .unitypackage before it is added to the library.</summary>
    [RelayCommand]
    public async Task ParsePackageAsync(string filePath)
    {
        IsLoading = true;
        StatusMessage = "正在解析 .unitypackage 文件...";

        try
        {
            Archive = await _extractor.ParseAsync(filePath);
            Entries = new ObservableCollection<UnityPackageEntry>(Archive.Entries);
            PickedFileName = Archive.Name;
            PackageInfo = $"{Archive.Name} | {Archive.FileCount} 个文件 · {Archive.DirectoryCount} 个文件夹";
            StatusMessage = "预览完成，点击“加入包列表”后即可在项目中使用";
        }
        catch (Exception ex)
        {
            PackageInfo = string.Empty;
            Entries = [];
            StatusMessage = $"解析失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
