using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Akashic.Core.Models;
using Akashic.Core.Services;
using Akashic.ViewModels;
using Windows.Storage;
using Windows.System;

namespace Akashic.Views;

/// <summary>
/// 项目模板页。模板是本应用自己存放的快照，删除只作用于 %LOCALAPPDATA%\Akashic\Templates，
/// 不会碰到任何真实工程。
/// </summary>
public sealed partial class ProjectTemplatesPage : Page
{
    public ProjectTemplatesViewModel ViewModel { get; }
    private readonly IProjectService _projectService;

    public ProjectTemplatesPage()
    {
        ViewModel = App.Services.GetRequiredService<ProjectTemplatesViewModel>();
        _projectService = App.Services.GetRequiredService<IProjectService>();
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e) => ViewModel.Refresh();

    private void Refresh_Click(object sender, RoutedEventArgs e) => ViewModel.Refresh();

    private async void DeleteTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ProjectTemplate template }) return;

        var dialog = new ContentDialog
        {
            Title = $"删除模板“{template.Name}”？",
            Content = "只会删掉本应用为这个模板建的快照文件夹，不会动到任何真实工程。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        Notify(await ViewModel.DeleteAsync(template));
    }

    private async void OpenTemplateFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ProjectTemplate template }) return;

        await OpenInExplorerAsync(template.Directory);
    }

    private async void OpenTemplatesFolder_Click(object sender, RoutedEventArgs e) =>
        await OpenInExplorerAsync(_projectService.GetTemplatesDirectory());

    private static async Task OpenInExplorerAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

        await Launcher.LaunchFolderAsync(await StorageFolder.GetFolderFromPathAsync(path));
    }

    private void Notify(string message)
    {
        if (App.MainWindow is MainWindow mw && !string.IsNullOrEmpty(message))
            mw.ShowNotification("项目模板", message, InfoBarSeverity.Informational);
    }
}
