using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Akashic.Views;

namespace Akashic;

/// <summary>
/// Main window with NavigationView-based navigation and Mica backdrop.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        ExtendsContentIntoTitleBar = true;

        Title = "Akashic - VRChat 包管理器";
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        NavView.SelectedItem = NavView.MenuItems[0];
        ContentFrame.Navigate(typeof(ProjectsPage));
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItemContainer is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString();
            var pageType = tag switch
            {
                "projects" => typeof(ProjectsPage),
                "resources" => typeof(ResourcesPage),
                "packages" => typeof(PackageBrowserPage),
                "booth" => typeof(BoothPage),
                "assetstore" => typeof(AssetStorePage),
                "import" => typeof(ImportPackagePage),
                "about" => typeof(AboutPage),
                _ => typeof(ProjectsPage)
            };
            ContentFrame.Navigate(pageType);
        }
    }

    public void ShowNotification(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        GlobalInfoBar.Title = title;
        GlobalInfoBar.Message = message;
        GlobalInfoBar.Severity = severity;
        GlobalInfoBar.IsOpen = true;
    }

    public void NavigateToProjectDetail(Core.Models.UnityProject project)
    {
        ContentFrame.Navigate(typeof(ProjectDetailPage), project);
    }

    /// <summary>Selects a navigation entry by its Tag so pages can jump between each other.</summary>
    public void NavigateToTag(string tag)
    {
        foreach (var obj in NavView.MenuItems)
        {
            if (obj is NavigationViewItem item && item.Tag?.ToString() == tag)
            {
                NavView.SelectedItem = item;
                return;
            }
        }
    }

    public void UpdateProjectCount(int count)
    {
        ProjectCountBadge.Value = count;
    }
}
