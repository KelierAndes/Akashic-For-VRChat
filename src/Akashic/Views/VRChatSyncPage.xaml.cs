using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Akashic.Core.Models;
using Akashic.Core.Services;
using Akashic.ViewModels;

namespace Akashic.Views;

/// <summary>
/// VRChat 数据同步页。这里的 WebView2 负责登录并交出会话 Cookie，读取与保存都走 VRChat 站点自身的
/// JSON API，不解析页面 DOM。世界与形象能改的东西不同，两个表单按官网的两套管理页分别构建。
/// </summary>
public sealed partial class VRChatSyncPage : Page
{
    private static readonly TimeSpan WatchPoll = TimeSpan.FromSeconds(3);

    public VRChatSyncViewModel ViewModel { get; }

    private DispatcherTimer? _watchTimer;
    private bool _capturing;

    /// <summary>Blueprint ids a jump from a local project wants landed on, consumed once.</summary>
    private IReadOnlyList<string> _focusIds = [];

    public VRChatSyncPage()
    {
        ViewModel = App.Services.GetRequiredService<VRChatSyncViewModel>();
        InitializeComponent();

        ViewModel.CookieExporter = ExportCookiesAsync;
        ViewModel.LoginNavigator = OpenLoginPageAsync;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (App.MainWindow is MainWindow mw) _focusIds = mw.TakePendingVRChatBlueprintIds();

        // 首次进入且已有登录态时才自动读取；否则把面板交给用户去登录。列表读回来后由 Rows 的变更
        // 去落这次跳转的落点。
        if (ViewModel.RowCount == 0 && !ViewModel.IsLoginPanelOpen)
        {
            _ = ViewModel.LoadAsync();
            return;
        }

        ApplyFocus();
    }

    /// <summary>
    /// Lands a jump from a local project: marks the rows that project's scenes produced and scrolls to
    /// the first one. Nothing is highlighted when the account no longer holds the content.
    /// </summary>
    private void ApplyFocus()
    {
        if (_focusIds.Count == 0) return;

        var wanted = _focusIds;
        _focusIds = [];

        var hit = ViewModel.Highlight(wanted);
        if (hit == 0)
        {
            Notify("这个本地工程的蓝图 ID 已经不在账号的内容里了。");
            return;
        }

        var target = ViewModel.Rows.First(row => row.IsHighlighted);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => ContentList.ScrollIntoView(target));

        Notify($"已从本地工程跳到 {hit} 项云端内容。");
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VRChatSyncViewModel.Rows))
        {
            ApplyFocus();
            return;
        }

        if (e.PropertyName != nameof(VRChatSyncViewModel.IsLoginPanelOpen)) return;

        if (ViewModel.IsLoginPanelOpen) StartWatch();
        else StopWatch();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ViewModel.LoadAsync();

    private async void Login_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.OpenLoginCommand.ExecuteAsync(null);

    private async void Read_Click(object sender, RoutedEventArgs e) => await ViewModel.LoadAsync();

    private void ClosePanel_Click(object sender, RoutedEventArgs e) => ViewModel.CloseLoginCommand.Execute(null);

    private async void CopyBlueprint_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id } || string.IsNullOrEmpty(id)) return;

        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(id);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        Windows.ApplicationModel.DataTransfer.Clipboard.Flush();

        Notify("蓝图 ID 已复制");
    }

    // ── Edit dialogs ─────────────────────────────────────────────────────────────────────────

    private void OpenLocalProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: VRChatContentRow row }) return;
        if (row.LocalProject is not { } project) return;

        if (App.MainWindow is MainWindow mw) mw.NavigateToProjectDetail(project);
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: VRChatContentRow row }) return;

        // List rows omit fields the forms need (a world's description, its content settings).
        var current = await ViewModel.PrepareEditAsync(row.Item);
        if (current is null) return;

        var panel = new StackPanel { Spacing = 10, Padding = new Thickness(0, 0, 4, 0) };
        var common = CommonFields.Build(panel, current, ViewModel.UploadCoverAsync);

        var kind = current.Kind == VRChatContentKind.World
            ? WorldFields.Build(panel, current, ViewModel, common)
            : AvatarFields.Build(panel, current, ViewModel, common);

        var dialog = new ContentDialog
        {
            Title = $"编辑{current.KindDisplay} · {current.BlueprintId}",

            // The form is long and the dialog's own ContentScrollViewer never becomes scrollable
            // here, so it needs a scroller of its own. Focus-driven BringIntoView has to stay off:
            // with it on, every toggle click or text box blur resolved the target against the
            // nested viewport and snapped the whole form back to the top.
            Content = new ScrollViewer
            {
                Content = panel,
                MaxHeight = 560,
                HorizontalScrollMode = ScrollMode.Disabled,
                BringIntoViewOnFocusChange = false
            },

            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        Notify(await kind.Collect());
    }

    /// <summary>What a form contributes when the dialog's 保存 is pressed.</summary>
    private sealed class FormPart
    {
        public required Func<Task<string>> Collect { get; init; }
    }

    /// <summary>The fields both kinds share: text, cover, warnings, author tags, visibility.</summary>
    private sealed class CommonFields
    {
        private CommonFields(VRChatContentItem item, TextBox name, TextBox description, ComboBox visibility,
            List<(string Tag, ToggleSwitch Toggle)> warnings, StackPanel authorTags)
        {
            Item = item;
            Name = name;
            Description = description;
            Visibility = visibility;
            Warnings = warnings;
            AuthorTags = authorTags;
        }

        public VRChatContentItem Item { get; }

        public string? CoverUrl { get; set; }

        private TextBox Name { get; }
        private TextBox Description { get; }
        private ComboBox Visibility { get; }
        private List<(string Tag, ToggleSwitch Toggle)> Warnings { get; }
        private StackPanel AuthorTags { get; set; }

        public static CommonFields Build(StackPanel panel, VRChatContentItem item, Func<string, Task<string>> upload)
        {
            var name = new TextBox { Header = "名称", Text = item.Name, MaxLength = 120 };
            var description = new TextBox
            {
                Header = "描述",
                Text = item.Description,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 96,
                MaxLength = 2000
            };

            var visibility = new ComboBox
            {
                Header = "可见性",
                ItemsSource = new[] { "public", "private" },
                SelectedItem = item.ReleaseStatus == "private" ? "private" : "public"
            };

            panel.Children.Add(name);
            panel.Children.Add(description);

            var warnings = new List<(string, ToggleSwitch)>();
            var coverState = new TextBlock { Text = "未更换封面", Opacity = 0.7, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var fields = new CommonFields(item, name, description, visibility, warnings, null!);

            // The cover row is built once here; handing back the button it also placed would give
            // that button two parents and throw.
            var coverButton = new Button { Content = "选择封面图片…" };
            coverButton.Click += async (_, _) =>
            {
                var file = await PickImageAsync();
                if (file is null) return;

                try
                {
                    fields.CoverUrl = await upload(file);
                    coverState.Text = "已上传新封面，保存后生效";
                }
                catch (Exception ex)
                {
                    coverState.Text = ex.Message;
                }
            };

            panel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children = { coverButton, coverState }
            });

            warnings.AddRange(BuildWarningToggles(panel, item));
            fields.AuthorTags = BuildAuthorTagEditor(panel, item);

            var other = HiddenTags(item);
            if (other.Count > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "系统标签（只读）：" + string.Join(" · ", other),
                    FontSize = 11,
                    Opacity = 0.7,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            panel.Children.Add(visibility);

            return fields;
        }

        private static List<(string, ToggleSwitch)> BuildWarningToggles(StackPanel panel, VRChatContentItem item)
        {
            panel.Children.Add(new TextBlock { Text = "内容警告", FontWeight = FontWeights.SemiBold });

            var toggles = new List<(string, ToggleSwitch)>();

            foreach (var (tag, label) in VRChatTagCatalog.ContentWarnings)
            {
                var toggle = new ToggleSwitch
                {
                    Header = label,
                    IsOn = item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)
                };

                panel.Children.Add(toggle);
                toggles.Add((tag, toggle));
            }

            return toggles;
        }

        private static StackPanel BuildAuthorTagEditor(StackPanel panel, VRChatContentItem item)
        {
            panel.Children.Add(new TextBlock { Text = "作者标签", FontWeight = FontWeights.SemiBold });

            var list = new StackPanel { Spacing = 4 };
            foreach (var tag in item.Tags.Where(VRChatTagCatalog.IsAuthorTag).ToList())
                list.Children.Add(AuthorTagRow(list, tag));

            panel.Children.Add(list);

            var add = new TextBox { PlaceholderText = "添加标签，例如 adventure" };
            var addButton = new Button { Content = "添加" };
            addButton.Click += (_, _) =>
            {
                var raw = add.Text.Trim();
                if (raw.Length == 0) return;

                var slug = raw.ToLowerInvariant().Replace(' ', '_');
                var tag = slug.StartsWith(VRChatTagCatalog.AuthorTagPrefix, StringComparison.Ordinal)
                    ? slug
                    : VRChatTagCatalog.AuthorTagPrefix + slug;

                if (list.Children.OfType<StackPanel>().Any(r => (string)r.Tag! == tag)) return;

                list.Children.Add(AuthorTagRow(list, tag));
                add.Text = string.Empty;
            };

            panel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { add, addButton }
            });

            return list;
        }

        private static StackPanel AuthorTagRow(StackPanel owner, string tag)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Tag = tag };
            row.Children.Add(new TextBlock
            {
                Text = VRChatTagCatalog.AuthorTagLabel(tag),
                VerticalAlignment = VerticalAlignment.Center
            });

            var remove = new Button { Content = "×", Padding = new Thickness(7, 1, 7, 2) };
            remove.Click += (_, _) => owner.Children.Remove(row);
            row.Children.Add(remove);

            return row;
        }

        /// <summary>Tags neither the warning switches nor the author editor own — carried through.</summary>
        private static List<string> HiddenTags(VRChatContentItem item) => item.Tags
            .Where(t => !VRChatTagCatalog.IsAuthorTag(t)
                        && !VRChatTagCatalog.ContentWarnings.Any(
                            w => string.Equals(w.Tag, t, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        /// <summary>
        /// Rebuilt from the three editors: tags this dialog never shows are kept, so a system tag
        /// such as system_approved is never silently dropped by an edit.
        /// </summary>
        public List<string> CollectTags()
        {
            var tags = HiddenTags(Item);
            tags.AddRange(Warnings.Where(w => w.Toggle.IsOn).Select(w => w.Tag));
            tags.AddRange(AuthorTags.Children.OfType<StackPanel>().Select(r => (string)r.Tag!));
            return tags;
        }

        public VRChatContentEdit Collect() => new()
        {
            Source = Item,
            Name = Name.Text,
            Description = Description.Text,
            ReleaseStatus = Visibility.SelectedItem as string,
            Tags = CollectTags(),
            ImageUrl = CoverUrl
        };
    }

    /// <summary>The world-only switches: capacity, preview, domains, content settings, publishing.</summary>
    private sealed class WorldFields
    {
        public static FormPart Build(StackPanel panel, VRChatContentItem item, VRChatSyncViewModel viewModel,
            CommonFields common)
        {
            var tiers = VRChatTagCatalog.CapacityTiers
                .Where(t => t >= Math.Max(6, item.MaxCapacity))
                .Select(t => t.ToString())
                .ToList();

            var capacity = new ComboBox
            {
                Header = "最大人数",
                ItemsSource = tiers,
                SelectedItem = Math.Max(6, item.MaxCapacity).ToString()
            };

            var trailer = new TextBox
            {
                Header = "YouTube 预览链接或视频 ID",
                Text = item.PreviewYoutubeId,
                PlaceholderText = "https://www.youtube.com/watch?v=…"
            };

            var domains = new TextBox
            {
                Header = "允许播放视频的域名（每行一个）",
                Text = string.Join(Environment.NewLine, item.UrlList),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 72
            };

            panel.Children.Add(capacity);
            panel.Children.Add(trailer);
            panel.Children.Add(domains);

            var settings = BuildSettingToggles(panel, "默认内容设置", VRChatTagCatalog.ContentSettings,
                key => !item.DefaultContentSettings.TryGetValue(key, out var v) || v.ValueKind != JsonValueKind.False);

            var abilities = BuildAbilityToggles(panel, item);

            var publish = new ComboBox
            {
                Header = "发布操作",
                ItemsSource = new[] { "不改动", "提交发布", "取消发布" },
                SelectedIndex = 0
            };
            panel.Children.Add(publish);

            panel.Children.Add(BuildDangerZone(item, "删除世界", () => viewModel.DeleteAsync(item)));

            return new FormPart
            {
                Collect = async () =>
                {
                    var edit = common.Collect();

                    if (int.TryParse(capacity.SelectedItem as string, out var max)) edit.MaxCapacity = max;
                    edit.PreviewYoutubeId = ExtractYoutubeId(trailer.Text);
                    edit.UrlList = domains.Text
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(d => d.Trim())
                        .Where(d => d.Length > 0)
                        .ToList();
                    edit.ContentSettings = settings.Changed();
                    edit.DisabledPropAbilities = abilities;

                    edit.Publish = publish.SelectedIndex switch
                    {
                        1 => true,
                        2 => false,
                        _ => null
                    };

                    return await viewModel.SaveAsync(edit);
                }
            };
        }

        /// <summary>
        /// Only the switches the user actually moves are sent: writing a key the record never had
        /// would change how the world behaves.
        /// </summary>
        private static Toggles BuildSettingToggles(StackPanel panel, string title,
            (string Key, string Label)[] catalog, Func<string, bool> initial)
        {
            panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });

            var rows = new List<(string Key, bool Initial, ToggleSwitch Toggle)>();

            foreach (var (key, label) in catalog)
            {
                var on = initial(key);
                var toggle = new ToggleSwitch { Header = label, IsOn = on };

                panel.Children.Add(toggle);
                rows.Add((key, on, toggle));
            }

            return new Toggles(rows);
        }

        private static List<string> BuildAbilityToggles(StackPanel panel, VRChatContentItem item)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "道具能力",
                FontWeight = FontWeights.SemiBold
            });

            var abilities = new List<string>(item.DisabledPropAbilities);

            foreach (var (key, label) in VRChatTagCatalog.PropAbilities)
            {
                var toggle = new ToggleSwitch
                {
                    Header = label,
                    IsOn = !abilities.Contains(key, StringComparer.OrdinalIgnoreCase)
                };

                toggle.Toggled += (_, _) =>
                {
                    if (toggle.IsOn) abilities.RemoveAll(a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase));
                    else if (!abilities.Contains(key, StringComparer.OrdinalIgnoreCase)) abilities.Add(key);
                };

                panel.Children.Add(toggle);
            }

            return abilities;
        }

        private sealed class Toggles(List<(string Key, bool Initial, ToggleSwitch Toggle)> rows)
        {
            /// <summary>The changed switches, or null when nothing was touched.</summary>
            public Dictionary<string, bool>? Changed()
            {
                Dictionary<string, bool>? written = null;

                foreach (var (key, initial, toggle) in rows)
                {
                    if (toggle.IsOn == initial) continue;

                    written ??= [];
                    written[key] = toggle.IsOn;
                }

                return written;
            }
        }

        /// <summary>Accepts a bare video id or any of the usual YouTube link shapes.</summary>
        private static string ExtractYoutubeId(string? raw)
        {
            var text = (raw ?? string.Empty).Trim();
            if (text.Length == 0) return string.Empty;
            if (!text.Contains('/') && !text.Contains('?')) return text;

            var uri = new Uri(text.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? text : "https://" + text);

            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2 && kv[0].Equals("v", StringComparison.OrdinalIgnoreCase) && kv[1].Length > 0)
                    return Uri.UnescapeDataString(kv[1]);
            }

            var last = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            return last is { Length: >= 6 } ? last : string.Empty;
        }
    }

    /// <summary>The avatar-only actions, mirroring the site's Manage Avatar panel.</summary>
    private sealed class AvatarFields
    {
        public static FormPart Build(StackPanel panel, VRChatContentItem item, VRChatSyncViewModel viewModel,
            CommonFields common)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "形象动作",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 6, 0, 0)
            });

            var actions = new StackPanel { Spacing = 6 };
            AddActionButton(actions, "设为当前形象", () => viewModel.SelectAvatarAsync(item, false));
            AddActionButton(actions, "设为备用形象", () => viewModel.SelectAvatarAsync(item, true));
            AddActionButton(actions, "生成 Windows 分身", () => viewModel.GenerateImpostorsAsync(item, "standalonewindows"));
            AddActionButton(actions, "生成 Android 分身", () => viewModel.GenerateImpostorsAsync(item, "android"));
            AddActionButton(actions, "生成 iOS 分身", () => viewModel.GenerateImpostorsAsync(item, "ios"));
            AddActionButton(actions, "删除分身", () => viewModel.DeleteImpostorsAsync(item));
            panel.Children.Add(actions);

            panel.Children.Add(BuildDangerZone(item, "删除形象", () => viewModel.DeleteAsync(item)));

            return new FormPart
            {
                Collect = () => viewModel.SaveAsync(common.Collect())
            };
        }

        /// <summary>
        /// Adds one action row — the button plus where its result prints. The row is placed once;
        /// handing the button back for a second Add gives it two parents and throws.
        /// </summary>
        private static void AddActionButton(StackPanel owner, string text, Func<Task<string>> run)
        {
            var button = new Button { Content = text, MinWidth = 150, HorizontalAlignment = HorizontalAlignment.Left };
            var result = new TextBlock
            {
                FontSize = 11,
                Opacity = 0.75,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };

            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                result.Text = "正在执行…";
                try
                {
                    result.Text = await run();
                }
                finally
                {
                    button.IsEnabled = true;
                }
            };

            owner.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children = { button, result }
            });
        }
    }

    /// <summary>The site keeps deletion behind a typed confirmation; so does this dialog.</summary>
    private static StackPanel BuildDangerZone(VRChatContentItem item, string label, Func<Task> run)
    {
        var box = new TextBox
        {
            Header = $"{label}：输入名称“{item.Name}”以确认",
            PlaceholderText = item.Name,
            Visibility = Visibility.Collapsed
        };

        var confirm = new Button
        {
            Content = label,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.IndianRed)
        };

        var go = new Button { Content = "确认删除", Visibility = Visibility.Collapsed, IsEnabled = false };

        confirm.Click += (_, _) =>
        {
            box.Visibility = Visibility.Visible;
            go.Visibility = Visibility.Visible;
        };

        box.TextChanged += (_, _) => go.IsEnabled = string.Equals(box.Text.Trim(), item.Name, StringComparison.Ordinal);

        go.Click += async (_, _) =>
        {
            go.IsEnabled = false;
            await run();
        };

        return new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { confirm, box, go }
        };
    }

    // ── WebView2 session ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 登录成功后站点会自己跳转，所以面板打开期间定时把 Cookie 交给服务并试一次当前用户接口，
    /// 通了就自动读取。
    /// </summary>
    private void StartWatch()
    {
        if (_watchTimer is not null) return;

        _watchTimer = new DispatcherTimer { Interval = WatchPoll };
        _watchTimer.Tick += WatchTick;
        _watchTimer.Start();
    }

    private void StopWatch()
    {
        if (_watchTimer is null) return;

        _watchTimer.Stop();
        _watchTimer.Tick -= WatchTick;
        _watchTimer = null;
    }

    private async void WatchTick(object? sender, object e)
    {
        if (VrcWebView.CoreWebView2 is not { } core) return;
        if (ViewModel.IsLoading || _capturing) return;

        // 还在身份提供方那边时不必尝试，Cookie 还不属于 vrchat.com。
        if (core.Source is not { } source ||
            !source.Contains("vrchat.com", StringComparison.OrdinalIgnoreCase)) return;

        _capturing = true;
        try
        {
            if (await ViewModel.TryCaptureSessionAsync()) StopWatch();
        }
        catch (Exception)
        {
            // 页面正在跳转，下一轮再试。
        }
        finally
        {
            _capturing = false;
        }
    }

    private async Task OpenLoginPageAsync()
    {
        var core = await EnsureCoreAsync();
        core.Navigate(ViewModel.LoginUrl);
    }

    private async Task ExportCookiesAsync()
    {
        if (VrcWebView.CoreWebView2 is not { } core) return;

        var cookies = new List<StoredCookie>();

        foreach (var cookie in await core.CookieManager.GetCookiesAsync(ViewModel.LoginUrl))
            cookies.Add(new StoredCookie(cookie.Name, cookie.Value, cookie.Domain, cookie.Path));

        // ImportSession ignores an empty set, so a reset profile cannot clear a session that works.
        ViewModel.ApplySessionCookies(cookies);
    }

    private async Task<CoreWebView2> EnsureCoreAsync()
    {
        if (VrcWebView.CoreWebView2 is { } existing) return existing;

        await WaitForLoadedAsync();
        await VrcWebView.EnsureCoreWebView2Async();

        var core = VrcWebView.CoreWebView2
                   ?? throw new InvalidOperationException("WebView2 未能初始化，请确认系统已安装 WebView2 运行时。");

        // The API calls work off the stored session already; putting it back in the browser keeps the
        // login panel from claiming the user is signed out when they are not.
        WebViewCookieSeed.Apply(core,
            App.Services.GetRequiredService<IVRChatSyncService>().SessionEntries);

        return core;
    }

    private async Task WaitForLoadedAsync()
    {
        if (VrcWebView.IsLoaded) return;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnLoaded(object sender, RoutedEventArgs e) => tcs.TrySetResult(true);

        VrcWebView.Loaded += OnLoaded;
        try
        {
            await tcs.Task;
        }
        finally
        {
            VrcWebView.Loaded -= OnLoaded;
        }
    }

    private static async Task<string?> PickImageAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary
        };
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private void Notify(string message)
    {
        if (App.MainWindow is MainWindow mw && !string.IsNullOrEmpty(message))
            mw.ShowNotification("VRChat", message, InfoBarSeverity.Informational);
    }
}
