using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Akashic.Core.Services;
using Akashic.Views;

namespace Akashic;

/// <summary>
/// Main window with NavigationView-based navigation and Mica backdrop.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly IThemeService _themeService = App.Services.GetRequiredService<IThemeService>();

    public MainWindow()
    {
        InitializeComponent();

        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        ExtendsContentIntoTitleBar = true;

        Title = "Akashic - VRChat 包管理器";

        ApplyTheme(_themeService.IsDarkTheme);
    }

    /// <summary>
    /// Applies the light/dark preference to the whole visual tree and the title bar buttons,
    /// and keeps the footer item icon in step (moon while dark, sun while light — the icon
    /// shows the current theme, same as DGStudio's footer item).
    /// </summary>
    public void ApplyTheme(bool dark)
    {
        RootGrid.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        ThemeNavIcon.Glyph = dark ? "\uE793" : "\uE706";

        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonForegroundColor = dark
            ? Microsoft.UI.Colors.White
            : Microsoft.UI.Colors.Black;
        titleBar.ButtonHoverBackgroundColor = Microsoft.UI.Colors.Transparent;
    }

    /// <summary>The theme item never becomes selected; it only flips the theme on invoke.</summary>
    private async void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem item && item.Tag?.ToString() == "theme")
        {
            var dark = RootGrid.RequestedTheme != ElementTheme.Dark;
            ApplyTheme(dark);
            await _themeService.SetDarkThemeAsync(dark);
        }
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
            if (tag == "theme") return;

            var pageType = tag switch
            {
                "projects" => typeof(ProjectsPage),
                "resources" => typeof(ResourcesPage),
                "packages" => typeof(PackageBrowserPage),
                "booth" => typeof(BoothPage),
                "assetstore" => typeof(AssetStorePage),
                "import" => typeof(ImportPackagePage),
                "about" => typeof(AboutPage),
                "settings" => typeof(SettingsPage),
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
