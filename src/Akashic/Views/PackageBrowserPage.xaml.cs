using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Akashic.Core.Models;
using Akashic.ViewModels;

namespace Akashic.Views;

public sealed partial class PackageBrowserPage : Page
{
    public PackageBrowserViewModel ViewModel { get; }

    public PackageBrowserPage()
    {
        ViewModel = App.Services.GetRequiredService<PackageBrowserViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        LoadingRing.IsActive = true;
        await ViewModel.LoadPackagesCommand.ExecuteAsync(null);
        LoadingRing.IsActive = false;
        PackageCountText.Text = $"{ViewModel.FilteredPackages.Count} 个包";
    }

    private void PackageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.FirstOrDefault() is VpmPackage package)
        {
            ShowPackageDetail(package);
        }
    }

    private void ShowPackageDetail(VpmPackage package)
    {
        DetailPanel.Visibility = Visibility.Visible;
        DetailDisplayName.Text = package.DisplayName;
        DetailName.Text = package.Name;
        DetailDescription.Text = package.Description ?? "无描述";
        DetailVersion.Text = $"最新版本: {package.Version}";
        DetailUnity.Text = $"Unity 兼容: {package.Unity ?? "未指定"}";

        var deps = package.VpmDependencies ?? package.Dependencies;
        if (deps != null && deps.Count > 0)
        {
            DetailDependencies.Visibility = Visibility.Visible;
            DepsRepeater.ItemsSource = deps.Select(d => $"{d.Key} >= {d.Value}").ToList();
        }
        else
        {
            DetailDependencies.Visibility = Visibility.Collapsed;
        }

        if (package.Versions.Count > 0)
        {
            DetailVersionsList.Visibility = Visibility.Visible;
            VersionsRepeater.ItemsSource = package.Versions.Keys
                .OrderByDescending(v => v)
                .ToList();
        }
        else
        {
            DetailVersionsList.Visibility = Visibility.Collapsed;
        }
    }
}
