using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Akashic.Core.Models;
using Akashic.Core.Services;

namespace Akashic.ViewModels;

/// <summary>
/// ViewModel for browsing all available VPM packages across registries.
/// </summary>
public partial class PackageBrowserViewModel : ObservableObject
{
    private readonly IVpmRegistryService _registryService;

    [ObservableProperty]
    private ObservableCollection<VpmPackage> _packages = [];

    [ObservableProperty]
    private ObservableCollection<VpmPackage> _filteredPackages = [];

    [ObservableProperty]
    private VpmPackage? _selectedPackage;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public PackageBrowserViewModel(IVpmRegistryService registryService)
    {
        _registryService = registryService;
    }

    partial void OnSearchQueryChanged(string value)
    {
        FilterPackages();
    }

    [RelayCommand]
    public async Task LoadPackagesAsync()
    {
        IsLoading = true;
        StatusMessage = "正在从注册表获取包列表...";

        try
        {
            var packages = await _registryService.GetAllPackagesAsync();
            Packages = new ObservableCollection<VpmPackage>(packages);
            FilteredPackages = new ObservableCollection<VpmPackage>(packages);
            StatusMessage = $"已加载 {packages.Count} 个包";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void SelectPackage(VpmPackage package)
    {
        SelectedPackage = package;
    }

    private void FilterPackages()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            FilteredPackages = new ObservableCollection<VpmPackage>(Packages);
            return;
        }

        var q = SearchQuery.ToLowerInvariant();
        var filtered = Packages.Where(p =>
            p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            p.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            (p.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
        );

        FilteredPackages = new ObservableCollection<VpmPackage>(filtered);
    }
}
