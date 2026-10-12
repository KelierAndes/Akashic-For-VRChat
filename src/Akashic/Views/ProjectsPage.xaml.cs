using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Akashic.Core.Models;
using Akashic.Core.Services;
using Akashic.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace Akashic.Views;

public sealed partial class ProjectsPage : Page
{
    public ProjectsViewModel ViewModel { get; }
    private readonly IProjectService _projectService;

    public ProjectsPage()
    {
        ViewModel = App.Services.GetRequiredService<ProjectsViewModel>();
        _projectService = App.Services.GetRequiredService<IProjectService>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        LoadingRing.IsActive = true;
        await ViewModel.LoadProjectsCommand.ExecuteAsync(null);
        LoadingRing.IsActive = false;
        UpdateEmptyState();

        // The cloud link needs the account's own list, which only the sync page is able to read.
        if (!string.IsNullOrEmpty(ViewModel.CloudLinkHint))
        {
            StatusInfoBar.Severity = InfoBarSeverity.Informational;
            StatusInfoBar.Message = ViewModel.CloudLinkHint;
            StatusInfoBar.IsOpen = true;
        }
        else
        {
            StatusInfoBar.IsOpen = false;
        }

        if (App.MainWindow is MainWindow mw)
            mw.UpdateProjectCount(ViewModel.Projects.Count);
    }

    private void CloudManage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: UnityProject project }) return;
        if (project.CloudBlueprintIds.Count == 0) return;

        if (App.MainWindow is MainWindow mw)
            mw.NavigateToVRChatContent(project.CloudBlueprintIds);
    }

    private static async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private void Report(string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.IsOpen = true;
    }

    private async void DetectProjects_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is null) return;

        LoadingRing.IsActive = true;
        var message = await ViewModel.DetectProjectsAsync(folder);
        LoadingRing.IsActive = false;

        UpdateEmptyState();
        Report(message);

        if (App.MainWindow is MainWindow mw)
            mw.UpdateProjectCount(ViewModel.Projects.Count);
    }

    /// <summary>
    /// Mirrors the site's own new project dialog: editor version first, then only the templates that
    /// version can open, then name and parent folder, with the resulting path and whether it can be
    /// created shown while the fields are edited.
    /// </summary>
    private async void NewProject_Click(object sender, RoutedEventArgs e)
    {
        var versions = ViewModel.UnityVersions.ToList();
        if (versions.Count == 0) versions.Add("未知版本");

        // Open on the newest version that actually has a template, so the template list is never
        // empty for no reason the moment the dialog appears.
        var versionBox = new ComboBox
        {
            Header = "Unity 版本",
            ItemsSource = versions,
            SelectedIndex = ViewModel.DefaultVersionIndex
        };
        var templateBox = new ComboBox { Header = "模板" };
        var templateNote = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };

        var nameBox = new TextBox { Header = "项目名称", Text = "New Project" };
        var parentBox = new TextBox { Header = "创建位置", Text = ViewModel.DefaultParent, HorizontalAlignment = HorizontalAlignment.Stretch };

        var location = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        var ready = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };

        var okBrush = new SolidColorBrush(Microsoft.UI.Colors.MediumSeaGreen);
        var blockedBrush = new SolidColorBrush(Microsoft.UI.Colors.DarkOrange);

        string Target() => Path.Combine(parentBox.Text.Trim(), nameBox.Text.Trim());

        void RefreshHint()
        {
            var name = nameBox.Text.Trim();
            var parent = parentBox.Text.Trim();

            if (name.Length == 0 || name.Any(c => Path.GetInvalidFileNameChars().Contains(c)))
            {
                location.Text = "请填写一个合法的项目名称。";
                ready.Text = string.Empty;
                ready.Foreground = blockedBrush;
                return;
            }

            if (parent.Length == 0)
            {
                location.Text = "请填写创建位置。";
                ready.Text = string.Empty;
                ready.Foreground = blockedBrush;
                return;
            }

            var target = Target();
            location.Text = $"新项目将位于 {target}。";

            if (Directory.Exists(target))
            {
                ready.Text = "该目录已经存在，请换一个名字。";
                ready.Foreground = blockedBrush;
            }
            else
            {
                ready.Text = "已准备好创建项目！";
                ready.Foreground = okBrush;
            }
        }

        nameBox.TextChanged += (_, _) => RefreshHint();
        parentBox.TextChanged += (_, _) => RefreshHint();

        // The template list follows the editor, never the other way round: an official skeleton pins
        // both a project version and an SDK line, so offering a 2019 template in a 2022 project would
        // hand back a folder the chosen editor refuses to open.
        var matching = new List<ProjectTemplate>();

        ProjectTemplate? Chosen() =>
            templateBox.SelectedIndex > 0 && templateBox.SelectedIndex - 1 < matching.Count
                ? matching[templateBox.SelectedIndex - 1]
                : null;

        void RebuildTemplates()
        {
            var editor = versionBox.SelectedItem as string ?? string.Empty;
            var previous = Chosen();

            matching.Clear();
            matching.AddRange(ViewModel.Templates.Where(t => BuiltinTemplates.MatchesUnity(t.UnityVersion, editor)));

            var labels = new List<string> { "空白项目（不使用模板）" };
            labels.AddRange(matching.Select(t => $"{t.Name} · {t.Kind}"));

            templateBox.ItemsSource = labels;
            templateBox.SelectedIndex = previous is null ? 0 : Math.Max(0, matching.IndexOf(previous));

            templateNote.Text = matching.Count == 0
                ? $"{editor} 没有可用的模板，将创建空白项目。"
                : string.Empty;
            templateNote.Visibility = matching.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        versionBox.SelectionChanged += (_, _) => RebuildTemplates();
        RebuildTemplates();

        var choose = new Button
        {
            Content = "选择",
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(8, 0, 0, 0)
        };

        choose.Click += async (_, _) =>
        {
            var picked = await PickFolderAsync();
            if (picked is null) return;

            parentBox.Text = picked;
            RefreshHint();
        };

        var locationRow = new Grid();
        locationRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        locationRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(parentBox, 0);
        Grid.SetColumn(choose, 1);
        locationRow.Children.Add(parentBox);
        locationRow.Children.Add(choose);

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(versionBox);
        panel.Children.Add(templateBox);
        panel.Children.Add(templateNote);
        panel.Children.Add(nameBox);
        panel.Children.Add(locationRow);
        panel.Children.Add(location);
        panel.Children.Add(ready);

        RefreshHint();

        var dialog = new ContentDialog
        {
            Title = "新建项目",
            Content = new ScrollViewer
            {
                Content = panel,
                MaxHeight = 520,
                HorizontalScrollMode = ScrollMode.Disabled,
                BringIntoViewOnFocusChange = false
            },
            PrimaryButtonText = "创建",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var chosen = Chosen();
        var version = versionBox.SelectedItem as string ?? string.Empty;

        var result = await ViewModel.CreateProjectAsync(nameBox.Text, parentBox.Text, chosen, version);

        UpdateEmptyState();
        Report(result.Message);

        if (App.MainWindow is MainWindow mw)
            mw.UpdateProjectCount(ViewModel.Projects.Count);

        if (!result.Success || chosen?.IsBuiltIn is not true || result.ProjectPath is not { } path) return;

        // A built-in template is only a skeleton until the SDK lands in it, so the packages follow
        // the folder into existence.
        var created = ViewModel.Projects.FirstOrDefault(p =>
            string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));

        if (created is null) return;

        Report($"正在为 {created.Name} 安装 {chosen.Name} 所需的软件包，请稍候…");

        var installed = await ViewModel.InstallTemplatePackagesAsync(created, chosen, version);
        Report(installed);

        await ViewModel.RefreshProjectCommand.ExecuteAsync(created);
        UpdateEmptyState();
    }

    private async void SaveAsTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: UnityProject project }) return;

        var nameBox = new TextBox { Header = "模板名称", Text = project.Name };
        var notesBox = new TextBox { Header = "说明（可选）", PlaceholderText = "例如：官方 Worlds 模板 + UdonSharp" };

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(nameBox);
        panel.Children.Add(notesBox);

        var dialog = new ContentDialog
        {
            Title = $"从“{project.Name}”创建模板",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        Report(await ViewModel.SaveAsTemplateAsync(project, nameBox.Text, notesBox.Text));
    }

    private async void AddProject_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            await ViewModel.AddProjectCommand.ExecuteAsync(folder.Path);
            UpdateEmptyState();

            if (!string.IsNullOrEmpty(ViewModel.StatusMessage))
            {
                StatusInfoBar.Message = ViewModel.StatusMessage;
                StatusInfoBar.Severity = ViewModel.Projects.Count > 0
                    ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
                StatusInfoBar.IsOpen = true;
            }

            if (App.MainWindow is MainWindow mw)
                mw.UpdateProjectCount(ViewModel.Projects.Count);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadingRing.IsActive = true;
        await ViewModel.LoadProjectsCommand.ExecuteAsync(null);
        LoadingRing.IsActive = false;
        UpdateEmptyState();

        if (App.MainWindow is MainWindow mw)
            mw.UpdateProjectCount(ViewModel.Projects.Count);
    }

    private void ProjectCard_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Ignore if clicked on a button or menu
        if (e.OriginalSource is DependencyObject dep && IsDescendantOfButton(dep))
            return;

        if (sender is FrameworkElement element && element.DataContext is UnityProject project)
        {
            if (App.MainWindow is MainWindow mw)
                mw.NavigateToProjectDetail(project);
        }
    }

    private static bool IsDescendantOfButton(DependencyObject? obj)
    {
        while (obj != null)
        {
            if (obj is Button) return true;
            obj = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(obj);
        }
        return false;
    }

    private void LaunchUnity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is UnityProject project)
        {
            var result = _projectService.LaunchUnity(project);
            if (App.MainWindow is MainWindow mw)
            {
                mw.ShowNotification(
                    result.Success ? "Unity 正在启动" : "无法启动 Unity",
                    result.Message,
                    result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error);
            }
        }
    }

    private void ManageProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is UnityProject project)
        {
            if (App.MainWindow is MainWindow mw)
                mw.NavigateToProjectDetail(project);
        }
    }

    private async void BackupProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is UnityProject project)
        {
            if (App.MainWindow is MainWindow mw)
                mw.ShowNotification("正在备份", $"正在创建项目 \"{project.Name}\" 的备份...", InfoBarSeverity.Informational);

            var (success, message, _) = await _projectService.CreateProjectBackupAsync(project);

            if (App.MainWindow is MainWindow mw2)
            {
                mw2.ShowNotification(
                    success ? "备份成功" : "备份失败",
                    message,
                    success ? InfoBarSeverity.Success : InfoBarSeverity.Error);
            }
        }
    }

    private void OpenExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is UnityProject project)
        {
            if (Directory.Exists(project.Path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = project.Path,
                    UseShellExecute = true
                });
            }
        }
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is UnityProject project)
        {
            var dp = new DataPackage();
            dp.SetText(project.Path);
            Clipboard.SetContent(dp);

            if (App.MainWindow is MainWindow mw)
                mw.ShowNotification("已复制", $"项目路径已复制到剪贴板: {project.Path}", InfoBarSeverity.Informational);
        }
    }

    private async void RemoveProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is UnityProject project)
        {
            var dialog = new ContentDialog
            {
                Title = "移除项目",
                Content = $"确定要从列表中移除 \"{project.Name}\" 吗？\n（不会删除磁盘中的项目文件）",
                PrimaryButtonText = "移除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.RemoveProjectCommand.ExecuteAsync(project);
                UpdateEmptyState();

                if (App.MainWindow is MainWindow mw)
                    mw.UpdateProjectCount(ViewModel.Projects.Count);
            }
        }
    }

    private void UpdateEmptyState()
    {
        var total = ViewModel.Projects.Count;
        var shown = ViewModel.Visible.Count;

        EmptyState.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoMatchState.Visibility = total > 0 && shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        ProjectsScroller.Visibility = shown > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ResetFilters_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Category = ProjectsViewModel.CategoryAll;
        ViewModel.CloudState = ProjectsViewModel.CloudAll;
        ViewModel.SortBy = ProjectsViewModel.SortName;
        UpdateEmptyState();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The cloud match runs in the background and can empty or refill a filtered view card by card.
        if (e.PropertyName == nameof(ProjectsViewModel.Visible))
            UpdateEmptyState();
    }
}
