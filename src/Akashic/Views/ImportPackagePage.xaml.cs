using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Akashic.ViewModels;
using Windows.Storage.Pickers;

namespace Akashic.Views;

public sealed partial class ImportPackagePage : Page
{
    public ImportPackageViewModel ViewModel { get; }

    private List<string> _pickedPaths = [];

    public ImportPackagePage()
    {
        ViewModel = App.Services.GetRequiredService<ImportPackageViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadLibraryCommand.ExecuteAsync(null);
    }

    private async void BrowsePackage_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.SuggestedStartLocation = PickerLocationId.Downloads;
        picker.FileTypeFilter.Add(".unitypackage");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var files = await picker.PickMultipleFilesAsync();
        if (files == null || files.Count == 0) return;

        _pickedPaths = files.Select(f => f.Path).ToList();
        ViewModel.PickedFileName = files.Count == 1
            ? files[0].Name
            : $"{files[0].Name} 等 {files.Count} 个文件";

        await ViewModel.ParsePackageCommand.ExecuteAsync(_pickedPaths[0]);
    }

    private async void AddToLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (_pickedPaths.Count == 0)
        {
            if (App.MainWindow is MainWindow mw1)
                mw1.ShowNotification("提示", "请先点击“选择文件…”挑选 .unitypackage", InfoBarSeverity.Warning);
            return;
        }

        await ViewModel.AddFilesToLibraryCommand.ExecuteAsync(_pickedPaths);

        if (App.MainWindow is MainWindow mw)
            mw.ShowNotification("包列表", ViewModel.StatusMessage, InfoBarSeverity.Success);
    }

    private void OpenResources_Click(object sender, RoutedEventArgs e)
    {
        App.Services.GetRequiredService<ResourcesViewModel>().OpenTab(ResourcesViewModel.UserPackageTabIndex);
        (App.MainWindow as MainWindow)?.NavigateToTag("resources");
    }
}
