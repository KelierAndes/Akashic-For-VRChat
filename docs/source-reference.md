# 源码注释归档

> 由 `scripts/` 外的一次性工具从源码中提取，对应提交剥离前的状态。
> 这些内容原本以注释形式住在代码里；代码现在不再携带注释，这里是它们的完整副本。
> 上半部是公开 API 的文档注释（原 `///`），下半部是解释性注释（原 `//`、`/* */`、`.ps1` 的 `#`）。
> 跨模块的通用约束已另写在 `DEVELOPMENT.md` 的「实现要点与已知坑」，两边不重复。


# 一 · API 文档注释

## app/lib/main.dart

### `class AppRoot extends StatelessWidget {`

Owns the light / dark / system choice and hands the matching token set to the
widget tree. The choice comes from settings.json through the sidecar.


## app/lib/src/app_nav.dart

### `class NavEntry {`

A single navigation destination. The icon rail and the side panel both
render from this list, so selecting in one always moves the other.

### `final String? section;`

Panel group header shown above this entry, first entry of a group only.

### `static const railCount = 9;`

The rail mirrors everything above the panel footer group.

### `static List<String> get defaultRail => [`

What the rail shows before the user rearranges it.

### `static String navTile(String id) => 'nav:$id';`

One rail tile: `nav:<id>` for a destination, `proj:<path>` for a pinned project.

### `static int settingsIndex =`

Settings lives in its own card under the rail.


## app/lib/src/browser/browser_panel.dart

### `class BrowserSession {`

Reads and writes the browser's own cookie jar.

### `static Future<List<CookieSpec>> harvest(List<String> origins) async {`

Origins to harvest, e.g. `https://booth.pm` and `https://accounts.booth.pm`.

### `static String _hostOf(String domain) =>`

A leading dot means "any host in this domain", which is not a URL.

### `class BrowserPanel extends StatefulWidget {`

The embedded browser, composited into the Flutter scene as a texture so it sits
inside the page tray like any other widget.

### `final double minZoom;`

Booth and the Asset Store both lay out at roughly 1000 CSS px, which is wider
than the tray, so the page is allowed to shrink until it fits.

### `Future<void> fitToWidth() async {`

Scales the page so its full width lands inside the panel instead of spilling
into a horizontal scrollbar. Uses CSS `zoom`: the plugin's own `zoomBy` is a
no-op on the Windows implementation.

### `Future<void> resetZoom() async {`

Cleared on every navigation, so the measurement is always taken unscaled.


## app/lib/src/host/host_client.dart

### `class HostException implements Exception {`

Raised for a refused request, carrying whatever `Problem` detail the Host sent.

### `class HostClient {`

Owns the sidecar process and talks to it over loopback HTTP.

The token is generated here and handed to the child through the environment, so
nothing sensitive ever has to be parsed back out of its output; the only thing
read from stdout is the bound url.

### `factory HostClient.attach(String url, String token) =>`

Connects to a sidecar that is already running, which is how the app is driven
while the Host is being debugged from a terminal.

### `Future<String> backupProject(String path) async {`

Returns the line the service wrote, so the shell can show it verbatim.

### `Future<Map<String, dynamic>> setVisual(double opacity) async =>`

Answers with what the sidecar actually kept, because it clamps the value.

### `Future<List<String>> setRail(List<String> items) async {`

Rewrites the icon rail and returns what the sidecar kept.

### `Future<List<CookieSpec>> sessionCookies(String site) async {`

The stored session for one site, ready to inject into the embedded browser.

### `List<dynamic> _urlsOf(Object? result) =>`

Every repository endpoint answers with the whole `{urls, known}` object, so the
caller gets the list back out of it rather than a cast that would throw.

### `Future<String?> assetStoreThumbnail(String path) async {`

Local path of a thumbnail pulled out of the package itself, for rows the store never
gave a cover to. Null when the package holds no usable image.

### `Future<Map<String, dynamic>> vcsInstall(String tool) async =>`

Runs winget, which can raise a UAC prompt the user has to answer.

### `Future<Map<String, dynamic>> githubStatus() async =>`

Who gh is signed in as. Never carries the token itself.

### `Future<Map<String, dynamic>> syncPush() async =>`

Shells out to gh and waits on GitHub, so this is a deliberate multi-minute call.

### `Future<Map<String, dynamic>> syncPull() async =>`

No gist id in either direction: the sidecar finds the account's own backup, so the page never
has to show or carry an address that could be opened outside it.

### `Future<Map<String, dynamic>> githubLogin() async =>`

The OAuth App that signs people in ships inside the sidecar, so the only GitHub login call is
this one: it opens the user's own browser and waits for them to click Authorize once.


## app/lib/src/host/host_models.dart

### `library;`

Mirrors of the records Akashic.Host serves. Field names are the C# ones in
camelCase, so a rename on either side has to be done in both places.

### `String originLabel(String code) => switch (code) {`

Who supplied a .unitypackage. The sidecar sends the enum name, which is not what
the interface says, so every list funnels it through here.

### `List<String> cloudBlueprintIds = [];`

Filled in by the background cloud match after the list is already on screen.

### `String get cloudLinkLabel => cloudBlueprintIds.length == 1`

Says what was found, not where the button goes. Spelled "VRC" rather than
"VRChat" because the card tile is 300 wide and the full name pushed the count
off the end of the pill.

### `final double sheetOpacity;`

Alpha of the glass surfaces (window sheet, rail trays, popups), so the
window looks the same on every start.

### `final List<String> rail;`

The icon rail's tiles, in order. Empty means the built-in set.

### `class CookieSpec {`

One cookie as it moves between the sidecar and the browser. Domain and path are
load-bearing: Booth hands out the same name on `booth.pm` and `accounts.booth.pm`
with different values, and Unity's session token lives on `.unity.com`, so a
flattened name-to-value view loses one of them.

### `class TemplateRow {`

A template row with every display string already resolved by the sidecar,
because those rules live on the C# model.


## app/lib/src/pages/about_page.dart

### `class AboutPage extends StatelessWidget {`

Static product information. The version line is read from the sidecar, so it
can no longer drift away from what is actually running.


## app/lib/src/pages/assetstore_page.dart

### `class AssetStorePage extends StatefulWidget {`

Unity Asset Store 我的资产. The store's GraphQL endpoint refuses a replayed
cookie, so the only way in is the signed-in page: this side runs the extractor the
sidecar provides, clicks through the pager, and hands the rows back for merging
with whatever already sits in the cache folder.

### `Future<List<Map<String, dynamic>>?> _extract(String script) async {`

Waits for the grid to paint, then runs the extractor until it answers with rows.

### `Future<void> _refreshCovers() async {`

Storefront covers first, then whatever is left gets one cut out of its own package.

### `Future<void> _fillLocalThumbnails() async {`

Rows the storefront has no cover for still have their .unitypackage on disk, and the
sidecar can pull a preview image straight out of it. Best effort, and cheap after the
first pass because the extraction is cached next to the package thumbnails.

### `static String _key(String raw) =>`

The loose key the storefront uses to match a cache file name to a search card.

### `Future<void> _fillMissingCovers() async {`

Cache-only rows carry no cover: the storefront search page is opened for each
missing name in the parked browser and the first matching card's key image is
remembered. Best effort — a failed lookup keeps the placeholder glyph.

### `Future<String?> _readCover(String name, String script) async {`

One search page read: wait for the cards to render, then hand the extractor
result back to the sidecar, which knows how to pick the right one.


## app/lib/src/pages/booth_page.dart

### `class BoothPage extends StatefulWidget {`

Booth purchases. The sidecar owns parsing, de-duplication and the archive cache;
this page only drives the embedded browser with the extractor scripts the sidecar
hands out and feeds their JSON back.

### `static const mediaFilters = ['可用软件包', '全部商品', '演示·图集'];`

Index 0 keeps demo videos, illustration packs and comics out of sight — the free
and gift lists are full of them and none of it installs into Unity. Titles and
file labels are matched by keyword, best effort; anything caught can still be
reviewed under 演示·图集.

### `void _applySnapshot(Map<String, dynamic> snapshot) {`

The sidecar folds the superseded versions of one product away and reports what
each survivor stands in for, so the page only has to show the counts.

### `void _absorb(List<Map<String, dynamic>> incoming) {`

The library keeps one row per downloadable, so a re-read merges by number
instead of dropping the other kinds that were already collected.

### `Future<void> _read() async {`

Walks every page of the selected list. The extractor reports the highest
pagination link it can see, which is how the loop knows where to stop.


## app/lib/src/pages/import_page.dart

### `class ImportPage extends StatefulWidget {`

Pick a .unitypackage, read its table of contents, untick whatever the project
does not need, then install exactly that subset.


## app/lib/src/pages/packages_page.dart

### `class PackagesPage extends StatefulWidget {`

The VPM registry: every package every configured source advertises, checked
against one target project so install state is readable per row.

### `Future<void> _matchTarget() async {`

Install state comes from the project's own manifest, which is already parsed
into `model.projects`, so this costs nothing extra.


## app/lib/src/pages/project_detail_page.dart

### `class ProjectDetailPage extends StatefulWidget {`

One project's package table: registry packages, whatever else is installed, and
the local .unitypackage shelf matched against this project's import ledger.


## app/lib/src/pages/project_picker.dart

### `class ProjectPickerForm extends StatefulWidget {`

A project picker used by every action that installs something somewhere.


## app/lib/src/pages/projects_page.dart

### `class ProjectsPage extends StatelessWidget {`

Local Unity projects: the same three filters and the same background cloud
match the desktop version has, rendered as tiles instead of a list.

### `List<Widget> hostingPills(AppModel model, String path) {`

Says how the folder is kept in sync. LFS is deliberately not shown here: it is a
property of the git repository, and the 版本托管 page is where it gets toggled.

### `class _PillRow extends StatelessWidget {`

One fixed line of pills. Each is allowed to shrink so a long label ellipsizes
instead of pushing the card taller than its neighbours.


## app/lib/src/pages/resources_page.dart

### `class ResourcesPage extends StatefulWidget {`

The local package library: registry sources, the .unitypackage shelf, external
packages, and the two account feeds that already have files on disk.

### `Future<void> _resolveNames() async {`

Names come from each registry, which is one request per source, so the derived
label stays on screen until the real one lands.

### `static const _origins = ['全部来源', '本地', 'Asset Store'];`

Booth-imported packages belong to the Booth shelf tab, so they are not an option
here — same as the old page, whose list excluded PackageOrigin.Booth outright.


## app/lib/src/pages/restore_page.dart

### `class RestorePage extends StatefulWidget {`

Getting a project back and keeping it in sync: the archives in the backup folder,
and the hosting tools installed on the machine.

### `Future<void> runVcsAndShow(AppModel model, String action, String path,`

A hosting operation ends with the tool's own words in a dialog: a one-line
status is not enough to act on when git or cm refuses something.

### `class _InitForm extends StatefulWidget {`

Picking the hosting tool is the only thing this form does, so the two options
say what will actually happen to the folder.


## app/lib/src/pages/settings_page.dart

### `class SettingsPage extends StatefulWidget {`

Paths, editors, backup behaviour, the hosting tools and the three stored logins —
all of it read back from the sidecar after every change so the page shows what is
actually on disk.

### `final GlobalKey _syncKey = GlobalKey();`

Marks the 配置同步 card so the sidebar account card can scroll to it.

### `static int _revealed = 0;`

Static on purpose: the page is thrown away and rebuilt every time it is opened, and a fresh
instance must not treat an older request from the account card as its own. Seeded at 0,
which is what the counter starts at, so clicking 设置 in the rail never scrolls.

### `class _ToolRow extends StatelessWidget {`

One dependency with what the probe found and the way to get it. The install button
only appears when the tool is missing: an already-installed git should not offer to
overwrite itself.

### `class _VisualCard extends StatelessWidget {`

颜色模式与页面全局不透明度. Both apply live: the slider drives the window sheet as the
thumb moves and the sidecar only stores the value once it settles.

### `double get _alpha => model.sheetOpacity.clamp(0.4, 1.0);`

The slider shows the sheet alpha as-is, so 70 % is the `0.7` written to settings.json.

### `class _SyncCard extends StatelessWidget {`

What would go up to the gist, where the last upload landed, and the two actions.

### `static String _when(Object? raw) {`

The sidecar writes a full ISO stamp; this row only has room for the minute.

### `Widget _row(String key, String value) => Padding(`

Values here are lists of names and paths, so they are allowed to take a second line
rather than ellipsize away the thing the user is checking for.

### `class _SyncRestoreForm extends StatelessWidget {`

Restoring overwrites what is on this machine, so it asks first and says what it will touch.
It never names the backup: the id alone is enough to open the gist on the web, so the page only
says where it will look, and the sidecar finds it under the signed-in account.


## app/lib/src/pages/stub_page.dart

### `class StubPage extends StatelessWidget {`

Placeholder body until each destination is ported page by page. It keeps
the real page chrome — title, subtitle, cards — so the layout is already
reviewed against the framework.


## app/lib/src/pages/templates_page.dart

### `class TemplatesPage extends StatefulWidget {`

Template list plus the two forms that create or capture one. The new-project
form picks the Unity version first, because the sidecar already knows which
templates that version can actually load.


## app/lib/src/pages/vrchat_page.dart

### `class VRChatSyncPage extends StatefulWidget {`

VRChat 数据同步. The content list comes off the disk cache first, so opening this
page costs nothing; only 刷新云端 walks the API. The local blueprint map is memoised
in the app model because scanning every project is the expensive half.

### `static int indexOfCode(String code) {`

Position of a server status inside [states] without the 全部状态 entry.

### `static String coverOf(Map<String, dynamic> row) {`

Some records only carry the full-size artwork, so the thumbnail falls back to it.

### `Set<String> _focus = {};`

Set by a jump from a project card: the list narrows to these blueprint ids until
the chip is cleared.

### `static String _stamp(Object? raw) {`

The sidecar writes a full ISO stamp; the subtitle only has room for the minute.

### `class _VrChatEditForm extends StatefulWidget {`

Name, description and — for a world — capacity and release status. Anything the
form leaves out is sent as null so the sidecar keeps the server's own value.


## app/lib/src/shell/app_scope.dart

### `class AppScope extends InheritedNotifier<AppModel> {`

Hands the one app model to the tree; every dependent rebuilds when it notifies.


## app/lib/src/shell/app_shell.dart

### `class AppShell extends StatefulWidget {`

Three pieces resting on the desktop: the icon rail, its settings card, and the
glass sheet that holds the nav panel plus the recessed page tray.


## app/lib/src/shell/left_rail.dart

### `class LeftRail extends StatelessWidget {`

Icon rail: the app mark in its own tray at the top, a black tray under it that holds
up to [AppModel.maxRailTiles] tiles — pages and pinned projects — and a second tray
below that carrying the wordmark and the settings button. The middle tray only ever
grows to the capped height, so pinning something can never squeeze the pieces around
it.

### `static const double _trayGap = 10;`

One gap for every seam between the rail's pieces: the icon tray, the quick bar and
the wordmark card. They used to differ, which read as a stray large gap.

### `static String initialsOf(String name) {`

Two letters, first upper and second lower ("EdenSpace" → "Ed"), or one
ideograph, so pinned projects stay tellable apart at 36px.

### `class RailPickerForm extends StatelessWidget {`

What can still be pinned. Anything already on the rail is left out, and the
settings button has its own fixed slot so it is not offered either.


## app/lib/src/shell/nav_panel.dart

### `class NavPanel extends StatefulWidget {`

Side panel: window chrome, account tile, the full destination list with
counts, and the theme switch pinned at the bottom.

### `final VoidCallback? onTap;`

Tapping the card goes to the GitHub login on the settings page.

### `final Map<String, dynamic> github;`

Who the GitHub CLI is signed in as, and when the config last went up. The card shows
the account rather than the app because that identity is what private external packages
and the sync gist both run on.

### `static String _stamp(Object? raw) {`

The sidecar writes a full ISO stamp; the card only has room for the minute.

### `class _SearchResults extends StatelessWidget {`

What the panel's search box matches: registered projects by name or path, and
registry packages by name, display name, description or keyword.


## app/lib/src/shell/window_chrome.dart

### `class WindowChrome extends StatelessWidget {`

Strip that owns the window: traffic lights on the left, drag surface in
the middle, optional trailing control. Only this band is draggable, so
clicks below it still reach the page.


## app/lib/src/state/app_model.dart

### `class AppModel extends ChangeNotifier {`

Everything the shell and the project list need to know, kept in one place so a
page never talks to the sidecar directly.

### `Future<void> refreshProjects() async {`

After anything that rewrites a project's manifests, so the cards and the
nav badge show the new package counts.

### `Future<void> updateSetting(Future<void> Function(HostClient client) change) =>`

Settings writes go straight through and pull a fresh state back, so the page
always shows what is actually on disk.

### `int themeChoice = 2;`

0 浅色 · 1 暗色 · 2 跟随系统. Seeded from settings.json, then the user wins.

### `double sheetOpacity = 0.7;`

Glass sheet alpha, from settings.json. The 视觉效果 card drives it and the shell repaints
on the spot, so a slider never waits for a restart. The slider shows this number as-is,
so the band is 0.4 (most see-through) up to 1.0, with 0.7 shipped.

### `List<String> rail = [];`

The icon rail's tiles as `nav:<id>` / `proj:<path>`; empty falls back to the
built-in set so a fresh install still looks like the product.

### `bool railEditing = false;`

True while the rail shows its remove buttons.

### `Map<String, int> get badges => {`

Rail dots: how many projects the 项目 destination should advertise.

### `static const int maxRailTiles = 10;`

A tile is 41px, so ten of them plus the header and the two editor buttons is
what the tray can hold without pushing the piece below it out of the window.

### `void removeRailTile(String raw) {`

The last tile stays: an empty rail would read back as the built-in set, which is
not what "I removed everything" should look like.

### `String packageQuery = '';`

Handed to 包浏览器 so a package result opens already filtered.

### `Future<void> matchCloud() async {`

The list is on screen before this finishes: each project answers for itself and
the tile fills in its cloud state as it goes.

### `String? openProjectPath;`

The detail view is swapped in inside the shell's own stack rather than pushed as
a route, because routes cannot receive pointer input in this window.

### `ValueChanged<String>? navJump;`

Set by the shell so any page can land on another destination — the VRChat list
jumps into a project's detail view.

### `List<String> _vrchatJumpIds = const [];`

Blueprint ids carried across a 项目 → VRChat 数据同步 jump. The sync page takes
them once and narrows its list to the uploads that project produced.

### `ValueChanged<int>? navSelect;`

Also set by the shell: the rail and the panel both select by index.

### `int syncReveal = 0;`

Bumped when something asks the settings page to bring the 配置同步 card into view.
The page compares it on every build, because it has no other reason to hear about it.

### `void openGitHubLogin() {`

The sidebar account card is the way into the GitHub login: the settings page is
the only place that can start it, and it is long enough to hide the card.

### `Future<Map<String, dynamic>>? _vrchatLocalMap;`

Walking every project's Packages folder is the slowest call on the VRChat page,
so it runs once per session and is kept here rather than re-read per visit.

### `String vcsBusyPath = '';`

One row is running an operation; the buttons on it go quiet until it returns.

### `bool installingTool = false;`

A winget install is in flight; the settings rows show it rather than go dark.

### `String restoreMessage = '';`

The last restore this page asked for, kept on screen because the job can run
for minutes and the shell status line is easy to miss.

### `Map<String, dynamic> vcsLog = const {};`

Last tool output, kept so the page can show git's or cm's own words.

### `Map<String, Map<String, dynamic>> vcsByPath = {};`

path → hosting marker. Read offline, so the project cards can tell a Git project
from a UVCS one without spawning anything per row.

### `Map<String, int> backupCounts = {};`

path → how many archives in the backup folder carry that project.

### `Map<String, dynamic> github = const {};`

Who the GitHub CLI is signed in as. Read once at connect time: it spawns gh, so it is
not something to re-ask on every repaint of the sidebar.

### `Map<String, dynamic> sync = const {};`

What the 配置同步 page shows: gist id, last upload, and what would be carried.

### `Map<String, dynamic> syncLog = const {};`

The last push or pull, kept on the page because the call runs for a minute or two.

### `Future<void> loadVcsTools({bool refresh = true}) async {`

Only the tool probe, for the settings page: the per-project states are heavier.

### `Future<void> pushSync() async {`

Pushing re-asks the CLI for its identity, so a login that happened in a terminal since
the app started is picked up without a restart.

### `Future<void> githubLogin() async {`

One click in the user's own browser, no code to copy and no passphrase to remember. The OAuth
App it rides on is compiled into the sidecar, so there is nothing to configure here.

A first login on an account without a backup uploads this machine's config straight away, which
is the point of the flow. When the account already has one, this only says so: overwriting local
settings is left to 从 gist 恢复, where the page can ask before it replaces anything.

### `Future<void> refreshVcsState(String path) async {`

The changed-file counts need git or cm, so they are only taken for the row
the user is actually looking at.

### `Widget? modal;`

Forms are hosted inside the shell's own stack rather than as a route: a route
pushed over this frameless transparent window never receives pointer events,
so its buttons are dead.

### `void setVisual({required double opacity}) {`

Applied to the window as the thumb moves; settings.json only sees it once the drag
settles, and then only with what the sidecar kept after clamping.


## app/lib/src/ui/app_colors.dart

### `class AppColors {`

Palette matched to the reference mock:
mid-gray canvas, lighter translucent window card, soft single-source
shadows (light from top-left). No heavy white halos.


## app/lib/src/ui/neu_dialog.dart

### `class NeuModalScaffold extends StatelessWidget {`

Panel chrome for a form shown in the shell's own modal layer.

Forms are NOT routes: a route pushed over this frameless, per-pixel transparent
window never receives pointer events, so every button in it is dead. The shell
keeps a modal slot inside the page tray instead.

### `class NeuField extends StatelessWidget {`

Label above any control, so forms read the same everywhere.

### `class NeuTextInput extends StatelessWidget {`

Inset text entry — the same carved look as the search field.

### `class NeuChoiceField extends StatefulWidget {`

A selector that unfolds in place, so a long option list never has to be
positioned against a layer the modal does not own.

### `class NeuPromptForm extends StatefulWidget {`

A one-field prompt shown through the shell's modal slot. Routes are unusable in
this window, so even a text entry has to live in the widget tree.

### `class NeuResultForm extends StatelessWidget {`

The outcome of one sidecar operation, in a dialog the user opens and closes on purpose.

Keeping this text on the page made the list rows underneath stop answering taps, so every
caller that gets a long tool transcript — git, cm, the config sync — hands it here instead.


## app/lib/src/ui/neu_kit.dart

### `Border glassEdgeBorderFallback() =>`

Hairline used by the reference sheet for glass-tinted containers.

### `class NeuPageBody extends StatelessWidget {`

Page body shared by every Akashic page.

With `content` set, the title and the `fixed` rows stay put and that one region
takes the rest of the tray: lists then reach the bottom edge instead of leaving
dead space under a fixed-height box, and an embedded browser parked in the fixed
area no longer shares a viewport with the scrolling list.

### `class NeuCard extends StatelessWidget {`

Convex content card — the default container for rows, tiles and groups.

### `class NeuTileButton extends StatelessWidget {`

Pressable card, used for project tiles and every list row that opens a
detail view.

### `class NeuPill extends StatelessWidget {`

Small status chip: 已安装 / 有更新 / Booth / 未登录.

### `class NeuTabs extends StatelessWidget {`

Underline tabs — the resource page's three-section switch and any
in-page view toggle.

### `class NeuSearchField extends StatelessWidget {`

The only inset (concave) surface in the kit — search boxes and text input.

### `class NeuCornerMark extends StatelessWidget {`

Bracket mark used at the corners of the empty state.

### `final bool expand;`

Fill whatever region the page gave instead of standing at a fixed height —
used inside `NeuPageBody(content:)`, where a short box would leave dead space.

### `class NeuDropdown extends StatefulWidget {`

Overlay dropdown — filters, version pickers, per-row menus.

### `class NeuActionButton extends StatelessWidget {`

Primary command button (安装 / 下载 / 新建项目).

### `class NeuSegmented extends StatelessWidget {`

Segmented control: light / dark / system, filter modes, view density.

### `class NeuCover extends StatefulWidget {`

Cover thumbnail used by the Booth, Asset Store and VRChat lists.

Fetched by hand instead of with `Image.network(headers:)`: Flutter *adds* that
header, so the request leaves with two User-Agent values, and VRChat's image CDN
answers that with 403.

Two levels of cache, because these lists are re-read every time a page opens: the
bytes stay in [fetched] for the run, and on disk under
`<LOCALAPPDATA>\Akashic\cache\covers` across runs. Booth's CDN and VRChat's both
rate-limit or refuse a cold reload of thirty covers.

### `static String? _directory;`

`%LOCALAPPDATA%\Akashic\cache\covers`, resolved once. [_noCache] is the negative
answer, so a machine with no writable folder does not retry it per thumbnail.

### `static const int _maxBytes = 4 << 20;`

Covers are thumbnails; anything bigger than this is not worth keeping on disk.

### `static Future<File?> _fileFor(String url) async {`

`null` when this machine has no writable cache directory, which leaves the old
fetch-every-launch behaviour as the fallback rather than failing the thumbnail.

### `static String _digest(String value) {`

FNV-1a over the URL. Only ever a cache file name, so it needs no more than that —
and on the Dart VM the multiply already wraps at 64 bits, which is what FNV wants.
The top bit is dropped because Dart prints a negative int with a leading minus.

### `class NeuCheckbox extends StatelessWidget {`

Round check mark used by the batch bars and the import checklist.

### `class NeuCell extends StatelessWidget {`

One column of the flex table used by 资源 / 包浏览器 / Booth 列表.


## app/lib/src/ui/neumorphic.dart

### `Color _ampDark(Color c, NeuTokens t) {`

Dark surfaces swallow shadows, so caller-provided shadow alphas are
amplified in dark mode (and white ambience capped) to keep the emboss
readable.

### `List<BoxShadow> neuRaised({`

Convex (raised) look as in the reference: the surface itself is lighter
than its surroundings and casts ONE soft dark shadow toward the bottom,
with only a faint white ambient at the top-left. Never a white halo.

### `class InsetShadowPainter extends CustomPainter {`

Concave (inset) look per neumorphic rules: a UNIFORM, low-contrast inner
shadow all around the edge, with the top-left lip slightly darker and the
bottom-right lip slightly lighter. Never a chunky or uneven border.

### `class GlassEdgeStyle {`

Glass edge reflection spec: bright top lip fading to a faint bottom lip.

### `class GlassEdgePainter extends CustomPainter {`

Strokes a rounded rect with a vertical white gradient — the specular
highlight along a glass edge.

### `class GlassEdgeOverlay extends StatelessWidget {`

Wraps any rounded widget with a glass edge reflection stroke.

### `class NeuBox extends StatelessWidget {`

A neumorphic box that switches between flat / convex / concave.

### `class NeuButton extends StatefulWidget {`

Interactive neumorphic control: press sinks the surface (convex -> concave),
hover lifts it, selected stays convex.

### `class GlassSheet extends StatelessWidget {`

Frosted glass window sheet. The app canvas is fully transparent, so this
sheet carries its own fill and reads as an independent window: hairline
highlight edge, no drop shadow. A shadow here used to paint a gray halo into
the transparent margin, which made the window edge look dirty.

`opacity` is the only alpha here — the sheet tokens are opaque — so 1 really
is a solid window and the slider number is what gets painted.

### `final double opacity;`

Alpha of the sheet gradient — the 全局不透明度 knob.

### `class NeuDivider extends StatelessWidget {`

A carved groove separator — concave per neumorphic rules:
dark hairline on top, light hairline below.


## app/lib/src/ui/theme.dart

### `class NeuTokens {`

Theme-aware neumorphic tokens (light / dark variants of the same
physical materials).

### `final Color warn;`

Damage and refusal ink — restore warnings, failed tool output.


## src/Akashic.Core/Models/AppSettings.cs

### `public class AppSettings`

Application settings persisted to disk.

### `[JsonPropertyName("unityEditorEntries")]`

Editors the user registered by hand. They are consulted before Unity Hub and VCC, so a
project always starts with the editor the user pointed at for that version.

### `[JsonPropertyName("projectBackupPath")]`

Where backup zips go. Empty falls back to VCC's own setting, then to the app folder.

### `[JsonPropertyName("backupExcludePackages")]`

Drops the package folders from a backup. The manifests stay in, so the packages can be
resolved again, and the archive only carries what no registry can hand back.

### `[JsonPropertyName("defaultProjectPath")]`

Where new projects are created and where the folder scan looks for projects.

### `[JsonPropertyName("projectTemplates")]`

Templates the user saved from existing projects, used by the new project dialog.

### `[JsonPropertyName("unityPackageOrigins")]`

Who supplied each manually tracked .unitypackage, keyed by its absolute path. Only non-local
entries are stored, so a plain hand-added file never appears here.

### `[JsonPropertyName("assetStoreCacheDirectory")]`

Where Unity's own Asset Store downloads live; scanned read-only.

### `[JsonPropertyName("assetStoreDownloadDirectory")]`

Where this app writes the .unitypackage files it downloads itself.

### `[JsonPropertyName("syncEnabled")]`

Whether the encrypted gist sync runs at all. Signing into GitHub is useful on its own —
private external packages go through the same credential — so the login stays available
with this off, and only the upload, the restore and the post-login auto-push honour it.

### `[JsonPropertyName("sheetOpacity")]`

Alpha of the glass surfaces: the window sheet, the rail trays and the popup panels.
The 不透明度 slider shows this number as-is, so the usable band is 0.4 … 1 and 0.7
is the shipped look.

### `[JsonPropertyName("railItems")]`

What the icon rail holds, in order: `nav:&lt;id&gt;` for a destination and
`proj:&lt;path&gt;` for a pinned project. Empty means the built-in set.

### `[JsonPropertyName("syncGistId")]`

The private gist this machine pushes its encrypted config to. Left empty, the first
upload creates one under the signed-in GitHub account and stores its id here.

### `[JsonPropertyName("syncPayloadBytes")]`

How many bytes the last upload carried, so the page can say what it sent.


## src/Akashic.Core/Models/AssetStoreAsset.cs

### `public class AssetStoreAsset`

One Asset Store product as this app sees it: the .unitypackage sitting in a scanned cache
folder, a row of the account's 我的资产 list, or both.

### `public string Publisher { get; set; } = string.Empty;`

Publisher folder in the cache, or the store's publisher name for account rows.

### `public string Category { get; set; } = string.Empty;`

Second cache level, e.g. "Textures MaterialsSkies". Empty for account-only rows.

### `public string? LocalPath { get; set; }`

Set when a downloaded .unitypackage of this product sits in a scanned folder.

### `public string? ProductId { get; set; }`

Numeric store product id, which is what the download endpoint is keyed by.

### `public string ImageUrl { get; set; } = string.Empty;`

Product cover image the 我的资产 row rendered, empty for cache-only entries.

### `public bool IsInLibrary { get; set; }`

True once the file itself has been registered in 用户软件包.

### `public bool IsFromAccount { get; set; }`

False for products only discovered by scanning the cache, i.e. not on the account list.

### `[JsonIgnore]`

Publisher and category as one line, with a dash for whatever level is missing.

### `public class AssetStoreSearchCover`

One product card of a storefront search page: its name and key image.

### `public class AssetStoreAccountCache`

Disk snapshot of the last 我的资产 read, so the Asset Store page opens with account rows and
their covers without re-walking the storefront.

### `public class AssetStorePurchasePage`

One rendered page of the account's 我的资产 list.

### `public bool LoginRequired { get; set; }`

The store bounced the request, which means the exported session died.

### `public string ReadDiagnostics { get; set; } = string.Empty;`

Where a page read ran and what it counted, so an empty list is explainable.


## src/Akashic.Core/Models/BackupArchive.cs

### `public class BackupArchive`

One backup archive sitting in the backup folder. The name carries the project and
the moment it was taken, and the two tools that produce these files disagree about
the shape, so both are read.

### `public required string ProjectName { get; init; }`

The project the archive claims to hold, taken from the name.

### `public required string Source { get; init; }`

Which naming scheme matched, so the list can say where a backup came from.

### `public bool Readable { get; init; }`

False when the central directory could not be read — a half-written zip.

### `public bool LooksLike(string projectName) =>`

Loose match against a registered project, for the "which project is this" column.

### `public static BackupArchive? TryParse(string path)`

Null when the file is not a backup this app can name.

### `private static (int Entries, long Bytes, bool Readable) ReadIndex(string path)`

Reads only the central directory, so a multi-gigabyte archive is cheap to list.


## src/Akashic.Core/Models/BoothPurchase.cs

### `public class BoothPurchaseItem`

One downloadable file of a product the account already owns, as read from the Booth library page.

### `[JsonPropertyName("formLabel")]`

The file name Booth shows next to this download.

### `[JsonPropertyName("external")]`

Booth hands this file out through an external link rather than its own CDN.

### `[JsonPropertyName("imageUrl")]`

Product cover image Booth renders on the library card or the item page.

### `[JsonPropertyName("isFree")]`

True when this entry came from a free item page rather than the purchase library.

### `[JsonIgnore]`

Identity of the product behind this downloadable: the /items/ link when the page offered
one, else the title plus shop. Multiple versions of one product share this key.

### `[JsonIgnore]`

The seller's name, read from the library card, falling back to its booth.pm subdomain.

### `[JsonIgnore]`

The archive name Booth shows, without its extension. The downloaded file carries a
CDN-sanitized variant of this.

### `[JsonIgnore]`

What parsing this archive registered, kept so refreshing the list does not turn a parsed
row back into a plain "downloaded" one.

### `public class BoothLibraryPage`

One rendered page of the account's Booth library, as returned by the WebView2 extractor.

### `[JsonPropertyName("loginRequired")]`

The page turned out to be a sign-in form, which means the exported session died.

### `public class BoothLibraryCache`

Disk snapshot of the last full library read, so the Booth page opens with a list and covers
without re-walking every library page.

### `public class BoothArchiveContents`

What a downloaded Booth archive turned out to contain.

### `public class BoothUpmPackage`

A folder inside the archive that carries a UPM package.json.


## src/Akashic.Core/Models/ConfigSync.cs

### `public class SyncSnapshot`

What the 配置同步 page needs to know before it offers to upload or restore anything.

### `public bool HasGistScope { get; init; }`

The token gh holds must carry the gist scope or the upload is refused.

### `public DateTime? UploadedAt { get; init; }`

Deliberately no gist id and no gist URL in here. The id is enough to build the web address of
the backup, and the encrypted blob names its owner — so the page shows only when the last
upload happened. Locating the gist itself is the sidecar's job.

### `public bool Enabled { get; init; } = true;`

The 启用 gist 同步 switch. GitHub sign-in stays useful without it — private external
packages run on the same credential — so this only speaks for the sync itself.

### `public List<string> Included { get; init; } = [];`

What the next upload would carry, in the order the page lists it.

### `public string Note { get; init; } = string.Empty;`

Anything the page should say: a missing CLI, a missing scope, a bad restore.


## src/Akashic.Core/Models/ExternalPackage.cs

### `public class ExternalPackage`

A UPM package attached outside of VPM: a local package folder or a GitHub repository.

### `[JsonPropertyName("id")]`

Stable key used by settings.json: kind + normalised location.

### `[JsonPropertyName("name")]`

UPM package name. Empty until a GitHub package's package.json has been read.

### `[JsonPropertyName("location")]`

Absolute folder path, or the repository's https URL.

### `[JsonPropertyName("reference")]`

Branch or tag used for GitHub packages.

### `[JsonPropertyName("origin")]`

Who supplied the package; entries written before this existed read back as Local.


## src/Akashic.Core/Models/PackageOrigin.cs

### `[JsonConverter(typeof(JsonStringEnumConverter))]`

Who supplied a package, which is separate from how it reached the library: a Booth archive and a
hand-picked file can both end up as the same kind of .unitypackage entry.


## src/Akashic.Core/Models/ProjectTemplate.cs

### `public class ProjectTemplate`

A saved project skeleton new projects are created from: the package manifests and editor
settings of a project the user liked, kept as a folder of its own so deleting the source project
never takes the template with it.

### `[System.Text.Json.Serialization.JsonPropertyName("kind")]`

世界 / 虚拟形象 / 自定义, mirroring what the VRChat SDK template a project uses.

### `[System.Text.Json.Serialization.JsonPropertyName("directory")]`

The folder holding the snapshot; absolute so the list survives any later move.

### `[System.Text.Json.Serialization.JsonPropertyName("sourcePath")]`

The project the snapshot was taken from, shown so the user recognises it.

### `[System.Text.Json.Serialization.JsonIgnore]`

Empty notes still need a line in the table, or the row reads as broken.

### `[System.Text.Json.Serialization.JsonIgnore]`

The folder name of the project this was taken from.

### `[System.Text.Json.Serialization.JsonIgnore]`

Set on the templates the app ships with, which have no snapshot folder and cannot be deleted.

### `[System.Text.Json.Serialization.JsonIgnore]`

Only a template this app saved can be deleted; the official ones come with VCC.

### `[System.Text.Json.Serialization.JsonIgnore]`

The folder this project starts from; empty for a built-in, which writes its own.

### `[System.Text.Json.Serialization.JsonIgnore]`

A template with a folder is only usable while that folder is there — which for an official one
means VCC still ships it. The compiled-in fallback has no folder and is always available.


## src/Akashic.Core/Models/UnifiedPackageItem.cs

### `public partial class UnifiedPackageItem : ObservableObject`

Flat unified package item for the ALCOM-style package management table.
Combines VPM packages and .unitypackage assets.

### `public PackageOrigin Origin { get; set; } = PackageOrigin.Local;`

Who supplied a local package, so a Booth import can be filtered as its own class.

### `public bool ForeignRepository { get; set; }`

Installed from a repository this app does not list, so it cannot be (re)installed from here.

### `public bool ShowVersionSelector => IsInstalled && Category == PackageCategory.Vpm && AvailableVersions.Count > 0;`

Only VPM packages have a version stream to pick from; everything else shows plain text.


## src/Akashic.Core/Models/UnityEditorEntry.cs

### `public class UnityEditorEntry`

One Unity editor this machine knows about: which version it is, where the exe lives, and who
reported it. Only 手动添加 entries are persisted; the rest are re-discovered on every refresh.

### `[JsonPropertyName("source")]`

手动添加 / Unity Hub / VCC / 扫描目录.


## src/Akashic.Core/Models/UnityPackageEntry.cs

### `public partial class UnityPackageEntry : ObservableObject`

Represents an entry (asset or folder) inside a .unitypackage file.

### `public bool IsDirectory => AssetData == null;`

Folder entries carry a pathname and a folderAsset meta but no asset payload.

### `public class UnityPackageArchive`

Represents a parsed .unitypackage file ready for installation.


## src/Akashic.Core/Models/UnityPackageLibrary.cs

### `public class UnityPackageFileInfo`

A .unitypackage file discovered in a local library folder or added by hand.

### `public PackageOrigin Origin { get; set; }`

Who supplied the file; the library scan fills this in from settings.json.

### `[JsonIgnore]`

Stable identity used to match install records: file name without extension.

### `public class UnityPackageInstallRecord`

One recorded .unitypackage import into a project, used to report installed state and to undo it.

### `[JsonPropertyName("installedPaths")]`

Project-relative paths (assets, folders and their .meta files) written by this import.


## src/Akashic.Core/Models/UnityProject.cs

### `public class UnityProject : System.ComponentModel.INotifyPropertyChanged`

Represents a Unity project on disk.

### `public string Name { get; set; } = string.Empty;`

Display name of the project (folder name).

### `public string Path { get; set; } = string.Empty;`

Full path to the project root directory.

### `public string UnityVersion { get; set; } = string.Empty;`

Unity editor version used by this project.

### `public bool HasVpmManifest { get; set; }`

Whether this project has VPM (vpm-manifest.json) configured.

### `public List<InstalledPackage> InstalledPackages { get; set; } = [];`

List of installed packages in this project.

### `public DateTime LastModified { get; set; }`

Last time the project was opened/modified.

### `public string AssetsPath => System.IO.Path.Combine(Path, "Assets");`

Project's Assets directory path.

### `public string PackagesPath => System.IO.Path.Combine(Path, "Packages");`

Project's Packages directory path.

### `public string ManifestPath => System.IO.Path.Combine(PackagesPath, "manifest.json");`

Path to the UPM manifest.json.

### `public string VpmManifestPath => System.IO.Path.Combine(PackagesPath, "vpm-manifest.json");`

Path to the VPM manifest (vpm-manifest.json).

### `public bool IsWorldProject => InstalledPackages.Any(p => p.Name.Equals("com.vrchat.worlds", StringComparison.OrdinalIgno`

Whether this project is a VRChat World project.

### `public bool IsAvatarProject => InstalledPackages.Any(p => p.Name.Equals("com.vrchat.avatars", StringComparison.OrdinalIg`

Whether this project is a VRChat Avatar project.

### `public bool IsVrcProject => InstalledPackages.Any(p => p.Name.StartsWith("com.vrchat.", StringComparison.OrdinalIgnoreCa`

Whether this is a VRChat project at all: any part of the SDK counts, including a base-only
project that is neither a world nor an avatar.

### `public List<string> CloudBlueprintIds`

Blueprint ids this project's scenes produce that the signed-in VRChat account also holds.
Never persisted: the scan that fills them now runs after the list is already on screen, so
changing them has to raise change notification for the card to update itself.

### `public bool HasCloudLink => CloudBlueprintIds.Count > 0;`

Whether the card should offer the jump to VRChat's own management page.

### `public string CloudLinkTip => CloudBlueprintIds.Count == 0`

What the jump control is worth showing, e.g. "wrld_…" or both ids.

### `public string CloudLinkLabel => CloudBlueprintIds.Count == 1 ? "云端管理" : $"云端管理（{CloudBlueprintIds.Count}）";`

Short label for the card, since the ids themselves are unreadable.

### `public string ProjectTypeDisplayName =>`

Project type label (世界, 虚拟形象, VRChat, Unity).

### `public string ProjectTypeGlyph =>`

Project type icon glyph.

### `public string LastModifiedDisplay => LastModified.ToString("yyyy/MM/dd HH:mm");`

Formatted last modified date string.

### `public class InstalledPackage`

Represents a package installed in a Unity project.

### `public enum PackageSource`

Indicates where a package originates from.


## src/Akashic.Core/Models/VRChatContent.cs

### `public enum VRChatContentKind`

Which kind of uploaded content a row stands for.

### `public static class VRChatTagCatalog`

The tag vocabulary and picker options the official edit pages use, so the app and the site agree
on what a switch actually writes.

### `public static readonly (string Tag, string Label)[] ContentWarnings =`

The site's Content Warnings switches, by their real tag values.

### `public const string AuthorTagPrefix = "author_tag_";`

Author-selectable tags, i.e. the site's Tags field (stored with this prefix).

### `public static readonly string[] ReadOnlyPrefixes = ["system_", "admin_", "feature_", "content_"];`

Tag families an author cannot edit; they are carried through untouched.

### `public static readonly int[] CapacityTiers = [6, 8, 12, 16, 20, 24, 32, 40];`

The site's Maximum Capacity dropdown; the API stores half of it as the recommended count.

### `public static readonly (string Key, string Label)[] ContentSettings =`

The site's Default Content Settings switches, by the internal names its form uses. Anything
the server reports beyond these is carried through untouched.

### `public static readonly (string Key, string Label)[] PropAbilities =`

The one prop ability the site exposes as a switch.

### `public sealed class LenientDateConverter : JsonConverter<DateTimeOffset?>`

VRChat's date fields are unreliable: an unpublished world reports an empty string rather than
null, which a plain DateTimeOffset? refuses to read and takes the whole list down with it.

### `public class VRChatContentItem`

One piece of content the signed-in account owns. VRChat's content id (avtr_… / wrld_…) is the
same value the Unity SDK shows as the Blueprint ID, so the page labels it that way.

### `[JsonPropertyName("imageUrl")]`

Full-size artwork; the API hands back an absolute api.vrchat.cloud URL.

### `[JsonPropertyName("thumbnailImageUrl")]`

The 256px thumbnail the list shows.

### `[JsonPropertyName("performance")]`

Performance ratings keyed by platform, e.g. standalonewindows = "Very Poor". The API nests
nothing here, so it is read as-is rather than through a rating object.

### `[JsonPropertyName("unityPackages")]`

Raw unityPackages array; only the newest entry's version is interesting here.

### `[JsonPropertyName("version")]`

Avatar records carry a version counter the update call expects back unchanged.

### `[JsonPropertyName("previewYoutubeId")]`

YouTube video id behind the world's preview player.

### `[JsonPropertyName("urlList")]`

Hosts the world's video players are allowed to stream from.

### `[JsonPropertyName("defaultContentSettings")]`

The site's "Default Content Settings" switches. Kept as raw JSON so keys this app does not
model survive an edit round-trip untouched.

### `[JsonPropertyName("disabledPropAbilities")]`

Prop abilities switched off in the site's props section.

### `[JsonPropertyName("styles")]`

Avatar styles for the site's carousel, as {primary, secondary}. Read as an object: the list
hands back an object, so modelling it as an array breaks deserialising every avatar row.

### `[JsonPropertyName("pendingUpload")]`

Set while the SDK has an upload staged for this record.

### `[JsonIgnore]`

True while the account's own copy is the world's author-controlled record.

### `[JsonIgnore]`

The SDK's Blueprint ID — the same id the API keys the content by.

### `[JsonIgnore]`

The site shows the hard limit; the API's capacity field is the recommended count.

### `[JsonIgnore]`

The PC rating the site shows, falling back to the mobile one.

### `public class VRChatContentEdit`

The editable half of a record. The two kinds share the text fields but their option sets differ,
exactly as the official edit pages do: worlds carry capacity, content settings and publishing,
avatars carry impostors and style slots. Nulls mean "leave the server's value alone" — the
update endpoint resets anything it is not given, so untouched fields are echoed back.

### `public List<string>? Tags { get; set; }`

Full tag list, prefixes included; system tags are carried over untouched.

### `public string? ReleaseStatus { get; set; }`

public / private, matching the site's visibility switch.

### `public string? ImageUrl { get; set; }`

Already-uploaded cover file URL, from the image upload endpoint.

### `public int? MaxCapacity { get; set; }`

The site's Maximum Capacity; the API stores half of it as the recommended count.

### `public string? PreviewYoutubeId { get; set; }`

YouTube video id behind the world's preview player.

### `public List<string>? UrlList { get; set; }`

Hosts the world's video players may stream from.

### `public Dictionary<string, bool>? ContentSettings { get; set; }`

The site's Default Content Settings switches, keyed by their internal names.

### `public List<string>? DisabledPropAbilities { get; set; }`

Prop abilities turned off, e.g. "movement".

### `public bool? Publish { get; set; }`

Ask the server to publish (true) or unpublish (false) the world.

### `public VRChatAvatarStyles? Styles { get; set; }`

Style slots shown in the site's avatar carousel.

### `public class VRChatAvatarStyles`

The two style slots an avatar can occupy in the site's carousel.

### `public class VRChatUnityPackage`

One entry of an item's unityPackages array (per-Unity-version builds).

### `public class VRChatUser`

The signed-in VRChat account, as far as this page needs it.

### `public class VRChatContentCache`

The last content list this machine read successfully. Only the list is kept — the session that
made it possible stays in memory and is never written out.


## src/Akashic.Core/Models/VccRepository.cs

### `public class VccRepository`

A VPM repository as VCC knows it: the URL it is served from and where its listing is cached on disk.


## src/Akashic.Core/Models/VersionControlState.cs

### `public class VcsTools`

What the machine has installed for hosting project history.

### `public bool GitLfsFound { get; init; }`

git ships without LFS; Unity projects need it for anything binary.

### `public bool GhFound { get; init; }`

GitHub CLI. Every GitHub call — private repositories and the sync gist — goes through it,
so it is a dependency of those two features rather than of hosting in general. It is
deliberately left out of `Missing`: a project that never touches GitHub should
not be nagged about it.

### `public bool UnityHubFound { get; init; }`

Unity itself only says which VCS mode the project was opened in, if any.

### `public bool InstallerFound { get; init; }`

winget is the only installer this app drives; without it the download pages are shown.

### `public IReadOnlyList<string> Missing`

What still has to be installed before every hosting feature works.

### `public class VcsCommit`

One revision shown in the history list.

### `public class ProjectVcsState`

How a single project is currently hosted, read from the markers on disk so that
listing ten projects never has to wait on ten external processes.

### `public string Kind { get; init; } = string.Empty;`

git, uvcs, both, or empty when the folder is unmanaged.

### `public int ChangedCount { get; init; }`

Files git or UVCS reports as changed; -1 when the count could not be taken.

### `public bool LfsEnabled { get; init; }`

True when the repository has the LFS filters installed.

### `public int AheadCount { get; init; } = -1;`

Commits ahead of the remote, -1 when unknown.

### `public int BehindCount { get; init; } = -1;`

-1 when unknown: commits behind the remote, i.e. a pull is pending.

### `public string Note { get; init; } = string.Empty;`

Anything the reader wants to say: a parse failure, a partial marker, an SSO hint.

### `public class VcsRunResult`

The outcome of one operation, with the tool's own output kept verbatim.

### `public string Action { get; init; } = string.Empty;`

What the caller asked for; the host echoes it back so the page can refresh itself.


## src/Akashic.Core/Models/VpmManifest.cs

### `public class VpmManifest`

Represents the VPM manifest file (vpm-manifest.json) in a Unity project.

### `public class UpmManifest`

Represents the Unity Package Manager manifest (Packages/manifest.json).

### `public class PackageJson`

Represents a package.json inside a package folder.


## src/Akashic.Core/Models/VpmPackage.cs

### `public class VpmPackage`

Represents a VPM package from a registry.

### `[JsonIgnore]`

Registry URL that advertises this package, so the 来源 column can name the repository.


## src/Akashic.Core/Services/AssetStoreService.cs

### `public class AssetStoreService : IAssetStoreService`

Reads the Unity Asset Store from two sides: the .unitypackage files Unity already dropped into
a local cache folder, and the signed-in account's 我的资产 list. The session comes from the
WebView2 login and is kept in memory only, exactly like Booth's.

### `private const string VaultKey = "assetstore";`

Name of this site's slot in the encrypted session store.

### `public AssetStoreAccountCache LoadAccountCache()`

Loads the disk snapshot of the last 我的资产 read, if any.

### `public async Task SaveAccountCacheAsync(IReadOnlyList<AssetStoreAsset> items, CancellationToken ct = default)`

Overwrites the disk snapshot with the given account read.

### `public IReadOnlyList<StoredCookie>? SessionEntries`

The same session with each cookie's own domain and path, which is the only form the WebView2
seed can use — Unity's session token is issued for `assetstore.unity.com`, and writing it
to any other domain means the storefront never receives it.

### `private void RestoreFromVault()`

Unity's session is the one the 我的资产 read and the download endpoint both need, so a stored
copy is picked up here rather than waiting for the storefront to be signed into again.

### `public async Task<List<AssetStoreAsset>> ScanCacheAsync(CancellationToken ct = default)`

Walks the cache and the download folder two levels deep, which is how Unity lays the store
out: publisher, then category, then the .unitypackage itself.

### `public void MergeCachedFiles(IEnumerable<AssetStoreAsset> accountItems, IEnumerable<AssetStoreAsset> cached)`

Attaches a downloaded file to the account row that owns it. Names are matched loosely because
the cache carries only the file name, while the store reports a display name.

### `public string MyAssetsExtractorScript => """`

Reads one page of 我的资源 the way the storefront actually renders it: every row is a
[data-test="asset-item-&lt;id&gt;"] div whose product name is plain text, not a link — the
only /packages/ anchor on that page belongs to a footer banner. The script stays
synchronous because WebView2's ExecuteScriptAsync does not wait for promises; walking the
下一页 pagination is done from C#. Multiple page results arrive as a JSON array.

### `private static string FileNameFrom(HttpResponseMessage response, AssetStoreAsset asset)`

The store names the file through Content-Disposition; a purchase without one keeps the
product name plus the extension Unity expects.

### `public string SearchCoverScript => """`

Reads the rendered search result cards. Covers there are plain &lt;img&gt; key-images and
every product card is an article[data-test=search-results-product-card-&lt;id&gt;].


## src/Akashic.Core/Services/BoothService.cs

### `public class BoothService : IBoothService`

Downloads what the signed-in Booth account already owns and classifies the unpacked archive
so it can join the local libraries. The session comes from the WebView2 login: its booth.pm
cookies are exported here so plain HTTP can follow the downloadables redirect.

### `private const string VaultKey = "booth";`

Name of this site's slot in the encrypted session store.

### `public IReadOnlyList<StoredCookie>? SessionEntries`

The same session with each cookie's own domain and path, which is the only form the WebView2
seed can use — a cookie written to the wrong domain is simply never sent to the site.

### `private void RestoreFromVault()`

Adopts a Booth session an earlier run stored, so a WebView2 reset is not a sign-out.

### `public async Task<bool?> ProbeSessionAsync(CancellationToken ct = default)`

Asks Booth whether the stored session still authenticates, without scraping the library.
A logged-out request for the library is redirected to the sign-in form, so the absence of that
redirect is the answer — one headers-only request instead of a paged DOM read.

Null means "no answer": offline, or Cloudflare pushing back on a bare request. That is
deliberately not reported as signed out, so a flaky network cannot log the user out on screen.

### `public void ReconcileLocalArchives(IEnumerable<BoothPurchaseItem> items)`

Points purchases at archives that already sit in the download folder, so reading the list
again does not forget what was downloaded in an earlier run. Each file is claimed by at most
one purchase.

### `private static Version? ParseVersion(BoothPurchaseItem item)`

A version written into the file name or the product title: "UnyStylus_v1.3.zip",
"Soramoyou ver1.2". Two-part versions count, so a bare "1" never wins by accident.

### `public static string NormalizeKey(string raw) => Key(raw);`

Normalises a file label or archive name for matching: letters and digits only,
case-insensitive by the caller's dictionary. Shared with the icon resolver so cached
covers can be matched back to their packages.

### `private async Task<(Uri Url, string? FileName)> ResolveFileAsync(string number, CancellationToken ct)`

Walks booth.pm's redirect chain for a downloadables id until it lands somewhere that is
not Booth, which is the short-lived signed link the CDN actually serves.


## src/Akashic.Core/Services/BuiltinTemplates.cs

### `public sealed class BuiltinTemplate`

A template the app did not create: either a folder VCC ships, or a compiled-in stand-in for when
VCC is not installed.

### `public required string Kind { get; init; }`

世界 / 虚拟形象 / 空白, matching how the rest of the app labels content kinds.

### `public required string UnityVersion { get; init; }`

Editor version the template itself declares, read from its ProjectVersion.txt.

### `public string Directory { get; init; } = string.Empty;`

The template folder to copy; empty for a compiled-in stand-in, which writes its own.

### `public IReadOnlyDictionary<string, string> PackageRanges { get; init; } =`

VPM packages the project needs, with the version range the template asks for.

### `public static partial class BuiltinTemplates`

The official VRChat project templates, taken from wherever Creator Companion keeps them:
`%LOCALAPPDATA%\VRChatCreatorCompanion\VRCTemplates`. Each folder is a complete project
skeleton — Assets, .gitignore, Packages and a full ProjectSettings — plus a package.json naming
it, so copying one produces exactly what VCC produces.

Reading them from disk rather than embedding a copy means the app follows VCC when VRChat updates
a template or its SDK range, and it is what makes the 2019 variants real: those ask for SDK
`~3.4.x` on Unity 2019.4.31f1, while the current ones ask for `~3.10.x` on 2022.3.22f1.
The compiled-in list is only a fallback for a machine with no VCC.

### `public static string VccTemplatesRoot => Path.Combine(`

Where Creator Companion keeps its project templates on this machine.

### `private static Dictionary<string, string> ReadVpmRanges(string folder)`

The template's own vpm-manifest is the authoritative list of what a new project needs, and it
carries the range VCC would resolve — so the app installs the same thing VCC would.

### `private static readonly BuiltinTemplate[] Fallback =`

Only used when VCC is absent, so it mirrors what the real templates declare.

### `public static string ManifestJson { get; } = BuildManifest();`

Unity's built-in modules, all versioned by the editor itself. Only the compiled-in fallback
needs this; a real VCC template brings its own manifest.

### `public static string? ChooseVersion(VpmPackage package, string? range, string? targetUnityVersion)`

Chooses the advertised version a template's range asks for: `~3.10.x` and `^3.4.x`
both mean "this minor, newest patch". A range that matches nothing falls back to the newest
version the target editor can load, then to whatever the registry calls latest.

### `public static int CompareUnityVersions(string? left, string? right)`

Orders editor versions newest first by their (major, minor), so Unity 6's 6000.x sorts above
2022.x rather than losing to it the way a plain string compare does. A version that will not
parse sorts last instead of disappearing.

### `public static bool MatchesUnity(string? templateVersion, string? editorVersion)`

Whether a template can be opened by the given editor. Unity only guarantees a project loads on
its own minor line, so 2022.3.62f1 takes a 2022.3.22f1 template while a 2019.4 one is out of
the question. Either side without a readable version filters nothing, which keeps a hand-made
template that never recorded its editor on the table.

### `private static (int Major, int Minor)? ParseMinor(string? value)`

Reads "2022.3", "2019.4.31f1" or "~3.10.x" down to a comparable (major, minor).


## src/Akashic.Core/Services/ConfigSyncService.cs

### `Task SetEnabledAsync(bool enabled, CancellationToken ct = default);`

Turns the sync on or off. The GitHub sign-in itself is untouched.

### `public sealed class ConfigSyncService : IConfigSyncService`

Moves this machine's configuration to another one through a private gist owned by the
user's own GitHub account.

The payload is encrypted before it leaves the box and only this app ever holds the key, so
GitHub stores opaque base64. The key is derived from the permanent numeric user.id of the
GitHub account plus a pepper built into Akashic, which is what makes the round trip zero-touch:
sign in on the new computer and the same key comes back, with no sync passphrase to set or to
remember.

Being exact about what that buys. user.id is public and the pepper ships inside the binary, so
this is strong obfuscation against GitHub, against anyone holding the gist, and against anyone
watching the transfer — it is not a secret that survives someone who has both the gist and
Akashic itself. The previous design derived the key from the OAuth token in the keyring, which
was genuinely secret but rotated on every sign-in and silently locked people out of their own
backup. The envelope still carries a four-byte key fingerprint, so restoring under another
account reports that rather than blaming a corrupt file.

DPAPI blobs are never shipped. The three storefront sessions are decrypted here, put in the
bundle as plain cookies, and re-protected by the local vault on the way back in — a copied
`sessions/*.bin` would be worthless on another machine anyway.

### `private const string KeyPepper = "Akashic_v1_AutoDeriveKey_9f8a7b6c!";`

Compiled into every copy of Akashic on purpose: it is what turns a public user.id into a key
nobody can rebuild from the gist alone. See the class summary for where that stops.

### `private static readonly (string Path, string Label)[] CacheFiles =`

The only files ever carried, by name relative to the data directory. Anything else in a
bundle is ignored, which is also what stops a crafted gist from writing outside it.

What a new feature has to do to be synced is short: a new setting lands in `settings.json` by
itself, provided the service that owns the field keeps its write whitelist up to date (a field
that never reaches the disk is a field the gist has never seen). A new list cache has to join
this table *and* the database snapshot export, otherwise it exists only on the machine that
made it. Scan results stay out on purpose — `resources`, `package_files` and `archive_files`
are keyed by absolute paths, so shipping them would describe directories the other machine does
not have. Categories and Booth matches still cross over, because `resource_meta` keys on the
resource name and the receiving side re-attaches them during its own first scan.

### `PreviewCachePaths.Configure(applied.PreviewCacheDirectory, applied.PreviewCacheQuotaBytes);`

A restore moves the preview cache with the configuration: the directory and the size cap come
from the file that has just landed, and one reclaim pass runs against the new cap immediately.
Without this the restored machine keeps writing where the old one pointed, at the old quota,
until someone happens to restart the sidecar.

### `private VcsRunResult? Blocked(string action) =>`

The 启用 gist 同步 switch, honoured by both directions — including the automatic upload
that follows a first sign-in, which is the path that would otherwise sync behind a user
who has just turned it off.

### `private static byte[] DeriveKey(long userId, byte[] salt, int iterations = Iterations)`

PBKDF2 over "<prefix><user.id>_<pepper>". The numeric account id never changes and the pepper
is identical in every build, so two machines that sign into the same GitHub account land on the
same key without anyone setting or remembering a sync password.

### `private static string OwnerHint(byte[] key) =>`

Four bytes of HMAC over the key, hex-encoded. Enough to tell "this backup was made by a
different GitHub authorisation" apart from "the file is corrupt", and it leaks nothing
about the token that produced the key.

### `private string? Resolve(string relative)`

Resolves a bundle name against the data directory, refusing anything that escapes.


## src/Akashic.Core/Services/DpapiSessionVault.cs

### `public sealed class DpapiSessionVault : ISessionVault`

Stores each site's cookies as one DPAPI blob under `%LOCALAPPDATA%\Akashic\sessions`.

The scope is `DataProtectionScope.CurrentUser`, so a blob can only be decrypted by
this Windows account: copying the folder to another machine or another user yields nothing. That
is the boundary worth being exact about — a process already running as this user can call DPAPI
itself, so this protects the file at rest and against casual leakage, not against malware on the
account. The previous design kept sessions in memory only, which meant any WebView2 profile reset
signed the user out of all three storefronts.

### `private static string? SafeName(string site)`

Site names become file names, so anything odder than a label is refused.


## src/Akashic.Core/Services/ExternalPackageService.cs

### `public class ExternalPackageService : IExternalPackageService`

Keeps the 外部包 library in settings.json and installs its entries into Unity projects:
local folders are referenced with a file: path, GitHub repositories are downloaded as
zipballs and embedded under Packages/.

GitHub is reached through the GitHub CLI rather than anonymous HTTP, which is what lets a
private repository install at all and stops the shared unauthenticated rate limit from
breaking the download halfway through a list. GitLab and Gitee have no CLI here, so they
keep plain HTTP.

### `private async Task AdoptResolvedNameAsync(ExternalPackage package, string name, PackageJson meta, CancellationToken ct)`

Stores the package.json values discovered during the first GitHub install.

### `private async Task RequireGitHubAsync(GitRepo repo, CancellationToken ct)`

GitHub has no anonymous fallback any more, so a missing or signed-out CLI is reported as
the thing to fix instead of as a download that mysteriously failed.

### `private async Task DownloadArchiveAsync(GitRepo repo, string reference, string target, CancellationToken ct)`

GitHub goes through gh; the other hosts keep the plain archive endpoint.

### `private async Task<PackageJson?> ProbeWithCliAsync(GitRepo repo, string reference, CancellationToken ct)`

One tree call names every path the ref holds, so the handful of candidate folders are
settled without a request each; the winner is then read through gh, which is the only
thing here allowed to see a private repository.

### `private static string RawUrl(GitRepo repo, string reference, string path) => repo.Host switch`

The raw-file URL each host serves package.json from.

### `private static string ArchiveUrl(GitRepo repo, string reference) => repo.Host switch`

The archive download each host serves a branch snapshot from.

### `private static string? FindPackageRoot(string root)`

Finds the shallowest folder holding a package.json, following how GitHub repos nest packages.

### `private static GitRepo? ParseGitRepo(string url)`

Recognises a git repository URL on any of the supported hosts (GitHub, GitLab, Gitee —
plus self-hosted GitLab instances, which answer to the same URL shapes as gitlab.com).

### `private static bool IsGitHost(string host) =>`

Any host ending in one of the known domains, so gitlab.example.com counts too.


## src/Akashic.Core/Services/GitHubCli.cs

### `public sealed record GitHubIdentity`

Who the GitHub CLI is signed in as, and what that token may do.

### `public string Token { get; init; } = string.Empty;`

The OAuth token gh keeps in the keyring. Read only as proof that somebody is signed in, and
never handed to another endpoint by this app — every GitHub call goes through gh itself.

### `public bool HasGistScope => Scopes.Count == 0 ||`

Whether this token may write gists. gh reports classic scopes as "gist" and GitHub App
permissions as "Gist access"; a token it reports no scopes for (a fine-grained PAT, or an App
token it cannot describe) is not blocked here — the gist call itself is the real oracle, and
its error is clearer than a wrong guess made in advance.

### `public bool SignedIn => !string.IsNullOrEmpty(Login) && !string.IsNullOrEmpty(Token);`

False when gh is installed but nothing is signed in, or the token was revoked.

### `public sealed class GitHubCli`

The one door this app uses for GitHub. Everything goes through the GitHub CLI rather than
hand-rolled HTTP: gh already holds an authorised token in the Windows keyring, so private
repositories and the gist API work without Akashic ever asking for a password, and the rate
limit follows the user's own account instead of the shared anonymous one.

Sign-in arrives here too: the browser flow in `GitHubLoopbackAuth` writes its token
into the CLI, so one authorisation covers private repositories and the sync gist alike, and no
second password prompt is ever needed.

Every call is a process spawn, so results that the UI reads on every paint are cached in the
`_identityTask` rather than re-run.

### `public const string GistFile = "akashic-sync.json";`

The single file the sync payload lives in, inside a private gist.

### `public static readonly string[] InstallCandidates =`

Where a winget or manual install leaves gh.exe.

### `public void Forget() => _identityTask = null;`

Drops the memo so a fresh login or install is picked up without a restart.

### `public async Task<(bool ok, string error)> LoginWithTokenAsync(string token, CancellationToken ct = default)`

Hands a token that this app minted itself straight to the CLI through
`gh auth login --with-token`, so one browser authorisation also signs gh in: repository
downloads and the gist API keep going through this one door, and the token lands in the same
keyring entry a manual `gh auth login` would have written.

### `private static List<string> ReadScopes((int code, string text) status)`

gh prints "Token scopes: 'gist', 'repo'"; only the quoted runs are scopes.

### `public async Task<string?> ApiAsync(string path, string? jq = null, CancellationToken ct = default)`

Runs `gh api <path>` and answers the body, or null when the call failed.

### `public Task<(bool ok, string error)> DownloadAsync(string apiPath, string target,`

Streams `gh api <path>` straight into a file. The zipball endpoint answers with the
archive bytes on stdout, and a gist read answers with the whole encrypted bundle —
neither may be captured as text, because `Trim` would cut them off.

### `private static async Task<(bool ok, string error)> RunToFileAsync(`

Runs gh with stdout wired to a file. The text capture used by every other call is
deliberately not applied here: its 6 KB cap is for human-readable logs.

### `public async Task<(string? id, string error)> PushGistAsync(`

Creates the gist when <paramref name="gistId"/> is empty, otherwise patches the one already
in use. The body goes in through a temp file because a several-megabyte payload will not fit
on a command line.

### `public async Task<string?> PullGistAsync(string gistId, CancellationToken ct = default)`

Reads the sync file back out of a gist. The bundle is far larger than the text capture
allows, so gh writes it straight to a temp file instead.

### `private static string? Unquote(string raw)`

gojq prints a top-level string bare, but an older gh quotes it as a JSON literal. Both
arrive here.

### `public async Task<string?> FindGistAsync(CancellationToken ct = default)`

Finds the sync gist on an account that has the backup but not the id — a second machine
signed into the same GitHub account should not have to hunt for it.


## src/Akashic.Core/Services/GitHubLoopbackAuth.cs

### `public string AuthorizeUrl { get; init; } = string.Empty;`

The page handed to the browser, so the UI can offer it when no browser opened.

### `public sealed class GitHubLoopbackAuth`

Signs a user into GitHub the way VS Code and JetBrains do: open GitHub's own authorisation page in
the user's browser, catch the redirect on `http://127.0.0.1:&lt;random port&gt;/callback/`,
and trade the code for a token here.

What the user does is click one green <em>Authorize</em> button. They never type an account, a
password, an 8-character device code, or a second sync passphrase into Akashic, and Akashic never
sees those secrets — the browser already holds the GitHub session.

PKCE (`code_verifier` + S256 challenge) binds the code to this socket and the random
`state` binds the callback to this attempt, so a request that reaches the port from anywhere
else is answered and ignored. The token then goes into the GitHub CLI's own credential store, which
keeps every GitHub call in this app behind the single `GitHubCli` door and makes one
authorisation cover private repositories as well as the sync gist.

The exchange also sends the client secret that `GitHubOAuthApp` ships with, because
GitHub documents it as required and does not distinguish public from confidential clients: PKCE is
an extra binding there rather than a replacement.

### `public const string CallbackPath = "/callback/";`

Redirect path. The OAuth App's 授权回调网址 has to be `http://127.0.0.1/callback/`: GitHub
matches the registered URL as a prefix of the redirect it is sent and lets the port vary, which
is what makes a random loopback port legal (RFC 8252 §7.3).

### `public static readonly string[] Scopes = ["repo", "read:org", "gist"];`

Classic scopes. `gist` is what config sync needs; `repo` covers the private
repositories the package browser reads. `read:org` is not optional in practice: the GitHub
CLI refuses to store a token that lacks it ("error validating token: missing required scope
'read:org'"), and gh is where this app keeps the credential.

### `public GitHubLoopbackAuth(GitHubCli gitHub,`

Both authorities exist so the self-test can run the whole flow against a local mock; shipped
Akashic always lands on github.com and api.github.com.

### `private static (HttpListener listener, int port) OpenLoopback()`

Binds 127.0.0.1 only — never `localhost`, which can resolve to ::1 while GitHub hands back a
v4 redirect, and never a wildcard, so nothing off this machine can be sent the code. The port is
whatever the OS has free, which is why the registered callback leaves it out.

### `private static async Task<(string code, string error)> WaitForCodeAsync(`

Keeps answering the browser until the callback arrives with our `state`, the window closes,
or GitHub reports a refusal. A stale request from an earlier attempt must not be able to log
anyone in, so anything else is discarded and the wait continues.

### `private static JsonDocument Parse(string text)`

GitHub answers JSON when asked for it, but a misconfigured App or an intermediate can hand back
the older urlencoded body. Both are read here instead of trusting the Accept header.


## src/Akashic.Core/Services/GitHubOAuthApp.cs

### `public static class GitHubOAuthApp`

The GitHub OAuth App Akashic ships with. The pair is registered once by the project owner and
stored as an AES-GCM blob inside the assembly, so a user never sees it, never configures it, and
never has to leave the settings page to start a sync.

Honest about what the encryption buys: the pepper that derives the key sits in the same binary,
so this conceals the pair from a casual reader of the DLL or the source — it does not keep it from
someone willing to reverse a release. That is unavoidable for a native client that talks to an
OAuth App, and the real limit on abuse is the registered callback URL, which pins where a token
may be sent. Rotate the secret at GitHub if it ever matters.

Development override: `AKASHIC_GITHUB_CLIENT_ID` and `AKASHIC_GITHUB_CLIENT_SECRET`
replace the built-in pair, so a second OAuth App can be tested without rebuilding.

### `private const string BuiltIn =`

The blob to ship. Regenerated with `Protect` whenever the App is edited at
GitHub; the plaintext never belongs in the repository.

### `public const string CallbackUrl = "http://127.0.0.1/callback/";`

What the App's 授权回调网址 has to be set to. No port: GitHub lets the port vary.

### `public static string Protect(string plainJson)`

Build-time twin of `Resolve`: wraps a JSON pair into the blob to paste into
`BuiltIn`. Nothing at runtime calls it.


## src/Akashic.Core/Services/IAssetStoreService.cs

### `public interface IAssetStoreService`

The Asset Store side of the local libraries: what Unity already downloaded into a cache folder,
plus what the signed-in account owns.

### `IReadOnlyDictionary<string, string>? SessionCookies { get; }`

The assetstore.unity.com cookies by name, which is the shape a hand-built `Cookie:`
header needs. Read-only: import through `ImportSession` so the domains survive.

### `IReadOnlyList<StoredCookie>? SessionEntries { get; }`

The same cookies with their domains, for seeding a fresh WebView2 back.

### `void ImportSession(IReadOnlyList<StoredCookie> cookies);`

Replaces the session with a fresh export from the storefront. An empty set is ignored rather
than treated as a sign-out, so a reset browser profile cannot erase a session that still works.

### `string MyAssetsUrl { get; }`

Where the WebView2 has to go to sign in and reach 我的资产.

### `string MyAssetsExtractorScript { get; }`

JavaScript that reads the rendered 我的资产 cards. The store's JSON API rejects
anything but its own token-carrying requests, so the rendered page is the source of truth.

### `Task<List<AssetStoreAsset>> ScanCacheAsync(CancellationToken ct = default);`

Every .unitypackage under the cache and download folders, merged with the library state.

### `AssetStorePurchasePage ParseMyAssets(string extractorResultJson);`

Turns the result of `MyAssetsExtractorScript` into rows.

### `Task<string> DownloadAsync(AssetStoreAsset asset, IProgress<double>? progress = null, CancellationToken ct = default);`

Downloads one purchased .unitypackage into the download folder and returns its path.

### `void MergeCachedFiles(IEnumerable<AssetStoreAsset> accountItems, IEnumerable<AssetStoreAsset> cached);`

Fills in `AssetStoreAsset.LocalPath` for account rows whose file is already in a
scanned folder, so the list shows them as cached rather than 未下载.

### `string GetSearchUrl(string term);`

The storefront search page for a product name, used to look up covers.

### `string SearchCoverExtractorScript { get; }`

JavaScript that reads the rendered search result cards: one name and key image per product.

### `List<AssetStoreSearchCover> ParseSearchCovers(string extractorResultJson);`

Turns the result of `SearchCoverExtractorScript` into cards.

### `void CacheCoverUrl(string name, string url);`

Remembers a product cover by name so cache-only rows can pick one up later without a
network round trip. Names are matched loosely, same as the cache-file merge.

### `bool TryGetCoverUrl(string name, out string url);`

Returns the cover remembered for a product name, if any.

### `string MatchKey(string raw);`

Loose name key shared by the cache merge and the cover cache.

### `AssetStoreAccountCache LoadAccountCache();`

Loads the disk snapshot of the last 我的资产 read, if any.

### `Task SaveAccountCacheAsync(IReadOnlyList<AssetStoreAsset> items, CancellationToken ct = default);`

Overwrites the disk snapshot with the given account read.


## src/Akashic.Core/Services/IBoothService.cs

### `public interface IBoothService`

Downloads what the signed-in Booth account already owns. The WebView2 login owns the
session, the list pages are read from the rendered DOM there, and the cookies they export are
handed over so plain HTTP can follow the downloadables redirect. The exported set is stored
encrypted under the current Windows account so a WebView2 profile reset is not a sign-out.

### `IReadOnlyDictionary<string, string>? SessionCookies { get; }`

The booth.pm cookies by name, which is the shape a hand-built `Cookie:` header needs.
Read-only: import through `ImportSession` so the domains survive.

### `IReadOnlyList<StoredCookie>? SessionEntries { get; }`

The same cookies with their domains, for seeding a fresh WebView2 back.

### `void ImportSession(IReadOnlyList<StoredCookie> cookies);`

Replaces the session with a fresh export. Booth rotates the session value, so re-export after
every login or list refresh. An empty set is ignored rather than treated as a sign-out.

### `Task<bool?> ProbeSessionAsync(CancellationToken ct = default);`

One cheap request that says whether the stored session still works: true signed in,
false signed out, null when Booth could not be asked.

### `string GetLibraryUrl(int page);`

The page the WebView2 has to open so the list extractor can run against it.

### `string GetFreeLibraryUrl(int page);`

The free downloads live on their own page tree under the account library; the purchase
list never shows them.

### `string GetGiftsUrl(int page);`

Gifted items live on a third page tree under the account library.

### `string PurchaseExtractorScript { get; }`

JavaScript that collects the download entries of an already rendered library page.

### `string FreeLibraryExtractorScript { get; }`

JavaScript that reads the free-downloads library cards. Those cards carry no download
control at all — only the product link, title, shop and thumbnail — so this returns
product metadata whose item pages are then visited for the real downloadables.

### `string ItemPageExtractorScript { get; }`

JavaScript that collects the downloadable entries of a product page, including free ones.

### `BoothLibraryPage ParseLibraryPage(string extractorResultJson);`

Turns the result of `PurchaseExtractorScript` into items, tolerating the
JSON-string wrapping WebView2's script execution adds.

### `BoothLibraryPage ParseItemPage(string extractorResultJson);`

Turns the result of `ItemPageExtractorScript` into items.

### `BoothLibraryPage ParseFreeLibraryPage(string extractorResultJson);`

Turns the result of `FreeLibraryExtractorScript` into product metadata.

### `BoothLibraryCache LoadLibraryCache();`

Loads the disk snapshot of the last full library read, so the page can open with a list
without re-walking every library page.

### `Task SaveLibraryCacheAsync(IReadOnlyList<BoothPurchaseItem> items, CancellationToken ct = default);`

Overwrites the disk snapshot with the given library read.

### `void ReconcileLocalArchives(IEnumerable<BoothPurchaseItem> items);`

Sets `BoothPurchaseItem.LocalZipPath` on purchases whose archive is already in
the download directory, so re-reading the list keeps them marked as downloaded.

### `int FoldOldVersions(List<BoothPurchaseItem> items, Dictionary<string, int> hiddenByNumber);`

Removes the superseded downloadables of products that appear several times, keeping the
newest of each, and reports per survivor how many versions it stands in for. One Booth
product carries a bundle, a rebuild and a renamed re-upload as separate rows, so a list
without this reads as a pile of duplicates.

- returns: How many rows were folded away in total.

### `Task<string> DownloadAsync(BoothPurchaseItem item, IProgress<double>? progress = null, CancellationToken ct = default);`

Downloads one purchased form into the download directory and returns the zip path.

### `Task<BoothArchiveContents> InspectAsync(BoothPurchaseItem item, CancellationToken ct = default);`

Unpacks an archive next to its zip and reports what kind of Unity content it holds.


## src/Akashic.Core/Services/IExternalPackageService.cs

### `public interface IExternalPackageService`

Packages the user attaches outside of VPM: local UPM package folders and git repositories
(GitHub, GitLab, Gitee and other public hosts). They are installed straight into a project's
Packages/manifest.json, so VCC is not involved.

### `Task<ExternalPackage> AddRepositoryAsync(string url, string? reference, CancellationToken ct = default);`

Attaches a git-hosted UPM package. The URL's host decides the platform (GitHub, GitLab,
Gitee), so callers do not need to know which one they are holding.


## src/Akashic.Core/Services/IPackageInstaller.cs

### `public interface IPackageInstaller`

Service for installing, updating, and removing packages in Unity projects.

### `Task<IReadOnlyList<string>> InstallUnityPackageAsync(UnityProject project, UnityPackageArchive archive, IProgress<double`

Writes the selected archive entries into the project and returns the installed project-relative paths.


## src/Akashic.Core/Services/IProjectService.cs

### `public interface IProjectService`

Service for managing Unity projects.

### `List<UnityEditorEntry> DiscoverUnityEditors();`

Editors known to this machine: manual list, Unity Hub, VCC and the scanned folders.

### `Task<string> AddUnityEditorAsync(string pickedPath);`

Registers an editor picked by folder or exe path; returns a line for the status text.

### `bool BackupExcludesPackages { get; }`

Whether backups leave the package folders out and keep only the manifests.

### `string GetDefaultProjectPath();`

Where new projects are created and where a folder scan looks for projects.

### `List<BackupArchive> ListBackupArchives();`

Every archive in the backup folder, newest first. Both this app's
`Name_20260102_030405.zip` and VCC's `Name-2026-01-02T03-04-05.zip`
are read, because a machine that has used VCC has both.

### `Task<(bool Success, string Message)> RestoreBackupAsync(`

Unpacks a backup into a project folder. Nothing is ever deleted: the default
only writes files that are not there yet, and overwriting has to be asked for.

### `List<ProjectTemplate> GetTemplates();`

Templates saved from existing projects, newest first.

### `string GetTemplatesDirectory();`

The folder this app owns its template snapshots in.

### `Task<string> SaveTemplateFromProjectAsync(UnityProject project, string name, string notes);`

Snapshots a project's ProjectSettings and package manifests as a reusable template.

### `Task<string> DeleteTemplateAsync(ProjectTemplate template);`

Forgets a template and deletes the snapshot folder this app created for it.

### `Task<(bool Success, string Message, string? ProjectPath)> CreateProjectAsync(`

Creates a new Unity project, optionally from a template, under the given parent folder.

### `Task<(int Added, int Already, string Message)> DetectProjectsAsync(string directory);`

Registers every Unity project found directly inside a folder. Returns added and already-known counts.


## src/Akashic.Core/Services/ISessionVault.cs

### `public sealed record StoredCookie(string Name, string Value, string Domain, string Path = "/");`

One cookie as the browser actually holds it. The domain is part of the identity, not decoration:
a session cookie belongs to the host that set it, and putting it anywhere else means the site
never sees it. `Domain` keeps Chromium's own spelling — a leading dot for a domain
cookie, a bare host for a host-only one.

### `public sealed record StoredSession(IReadOnlyList<StoredCookie> Cookies, DateTime SavedAt);`

A stored session with the time it was captured, so the UI can say how old it is.

### `public interface ISessionVault`

Keeps the Booth / Asset Store / VRChat sessions on disk so a WebView2 profile reset stops wiping
every login. See `DpapiSessionVault` for what the encryption does and does not buy.

### `StoredSession? Load(string site);`

The stored session for a site, or null when there is none or it cannot be read.

### `public static class StoredCookies`

Shared shaping of a cookie set, which all three storefront services need twice.

### `public static Dictionary<string, string> ToNameValue(IEnumerable<StoredCookie> cookies) =>`

The name/value view a hand-built `Cookie:` header needs. Such a header carries no domain,
so two cookies of the same name on different hosts collapse to one — the last one wins, which
is what the flattened export always produced.


## src/Akashic.Core/Services/IUnityPackageExtractor.cs

### `public interface IUnityPackageExtractor`

Service for parsing .unitypackage files.


## src/Akashic.Core/Services/IUnityPackageIconService.cs

### `public interface IUnityPackageIconService`

Pulls a representative image out of a .unitypackage so list rows can show one, caching the
result on disk because packages are read with a streaming tar walk.

### `Task<string?> GetThumbnailAsync(string unityPackagePath, CancellationToken ct = default);`

Returns the path of a cached thumbnail image for the package, or null when the package
holds no usable image. Missing or unreadable packages never throw.


## src/Akashic.Core/Services/IUnityPackageLibraryService.cs

### `public interface IUnityPackageLibraryService`

Tracks the local .unitypackage library (library folders + manually added files)
and which packages were imported into which project.

### `UnityPackageFileInfo DescribePackage(string filePath);`

Reads display name, version and size of a .unitypackage file name.


## src/Akashic.Core/Services/IVRChatBlueprintScanService.cs

### `public interface IVRChatBlueprintScanService`

Reads the blueprint ids the VRChat SDK stamps into a project's scenes and pairs them with the ids
the account holds in the cloud, so either side can link to the other.

### `Task<IReadOnlyList<string>> ScanAsync(UnityProject project, bool force = false, CancellationToken ct = default);`

Blueprint ids this project's scenes produce; cached per scene until it changes.

### `void PublishCloudIds(IEnumerable<string> blueprintIds);`

Records the ids the cloud list currently holds, which is everything matching runs against.

### `IReadOnlyList<string> CloudIds { get; }`

The cloud ids published so far.

### `Task<Dictionary<string, UnityProject>> MapByBlueprintAsync(`

Blueprint id → the project whose scenes produce it, across every registered project.


## src/Akashic.Core/Services/IVRChatSyncService.cs

### `public interface IVRChatSyncService`

Reads and edits the signed-in VRChat account's own uploaded content through the site's own JSON
API. The session comes from the WebView2 login and is stored encrypted under the current Windows
account so a profile reset is not a sign-out.

### `IReadOnlyDictionary<string, string>? SessionCookies { get; }`

The vrchat.com cookies by name, for the hand-built `Cookie:` header.

### `IReadOnlyList<StoredCookie>? SessionEntries { get; }`

The same cookies with their domains, for seeding a fresh WebView2 back.

### `void ImportSession(IReadOnlyList<StoredCookie> cookies);`

Replaces the session with a fresh export; an empty set is ignored, not treated as a sign-out.

### `string LoginUrl { get; }`

Where the WebView2 has to go to sign in.

### `Task<VRChatUser> GetSelfAsync(CancellationToken ct = default);`

Throws when the session is missing or dead, otherwise returns the account.

### `Task<List<VRChatContentItem>> GetMyContentAsync(CancellationToken ct = default);`

Every avatar and world the account owns, public and private alike.

### `Task<VRChatContentItem?> GetContentAsync(VRChatContentKind kind, string id, CancellationToken ct = default);`

The full record, which is where the fields the list omits live (a world's description,
content settings and allowed video domains).

### `Task<VRChatContentItem> UpdateContentAsync(VRChatContentEdit edit, CancellationToken ct = default);`

Writes an edit back; untouched fields are echoed so the server does not reset them.

### `Task DeleteContentAsync(VRChatContentKind kind, string id, CancellationToken ct = default);`

Permanent: the site only flips the release status but the id stays reserved.

### `Task SelectAvatarAsync(string id, bool fallback, CancellationToken ct = default);`

Makes the avatar the account's current one, or its fallback.

### `Task<string> UploadImageAsync(string filePath, CancellationToken ct = default);`

Uploads a local picture as the site's cover-image endpoint does; returns its URL.

### `VRChatContentCache LoadContentCache();`

The last list read successfully, so the page can show something before the network answers.


## src/Akashic.Core/Services/IVpmRegistryService.cs

### `public interface IVpmRegistryService`

Service for interacting with VPM package registries.

### `Task SaveRegistryOrderAsync(IEnumerable<string> orderedUrls, CancellationToken ct = default);`

Persists a user-dragged repository order, which is also the resolution priority.

### `Task<string?> FetchRepositoryNameAsync(string registryUrl, CancellationToken ct = default);`

Reads the advertised repository name from a listing, used for the 名称 column.

### `IReadOnlyList<VccRepository> GetKnownRepositories();`

Every repository VCC has a listing for, whether or not this app lists it.

### `Task<int> ImportVccRepositoriesAsync();`

Adds the repositories VCC knows about that this app does not list yet.

### `VccRepository? FindRepositoryFor(string packageName);`

The repository that advertises a package, used to name the origin of foreign installs.


## src/Akashic.Core/Services/PackageInstaller.cs

### `public class PackageInstaller : IPackageInstaller`

Installs and removes packages in Unity projects (VPM, UPM, and .unitypackage).


## src/Akashic.Core/Services/ProjectService.cs

### `public partial class ProjectService : IProjectService`

Manages Unity project discovery, registration, and metadata reading.

### `public string? FindUnityEditor(string? version = null)`

Picks the editor for a project version out of the same list the settings page shows, so what
you see is what launches. A version that no editor matches returns null rather than some
other release — opening a project with the wrong editor rewrites it.

### `public List<UnityEditorEntry> DiscoverUnityEditors()`

Every editor this machine can point at: the manual list first, then Unity Hub, VCC and the
scanned folders. A path is kept once, so the first source to report it owns the label.

### `private static void ScanEditorRoot(`

Adds the editors under a folder that is either an editor itself or holds several.

### `public async Task<string> AddUnityEditorAsync(string pickedPath)`

Registers an editor by hand. The picked path may be the folder that holds Unity.exe, its
Editor subfolder, or the executable itself.

### `public string GetBackupDirectory()`

Backup target: this app's setting wins, then VCC's, then the app data folder.

### `private static string ResolveVersion(string exePath, string? known)`

The version string Unity itself prints, e.g. 2022.3.22f1: a known key wins, then the exe's
ProductVersion, then the folder name the editor lives in.

### `private static readonly string[] BackupRoots = ["Assets", "Packages", "ProjectSettings"];`

The only folders a backup is allowed to write into.

### `public async Task<(bool Success, string Message)> RestoreBackupAsync(`

Unpacks a backup with a light touch: nothing already in the project is ever removed,
so a restore that turns out to be the wrong archive still leaves the old one openable.
Entries outside the three project folders are ignored rather than followed, because a
zip can carry "../" paths and a stray absolute path would write outside the project.

### `public List<ProjectTemplate> GetTemplates()`

The official VCC templates come first so the new project dialog opens on a real VRChat starting
point rather than an empty folder.

### `public async Task<string> SaveTemplateFromProjectAsync(UnityProject project, string name, string notes)`

Snapshots a project as a template. Only ProjectSettings and the package manifests are kept:
the packages themselves are re-resolvable, so copying them would make every template as large
as the project it came from.

### `private static void TryDeleteTemplateFolder(string? directory)`

Only ever removes a folder inside this app's own template root, so a stale or hand-edited
Directory value can never take a real project with it.

### `public async Task<(int Added, int Already, string Message)> DetectProjectsAsync(string directory)`

Registers the folder itself when it is a project, otherwise every project directly inside it.
Going one level deep is what makes a "E:\VRChat" archive show up without dragging in the
nested folders of a single project.

### `private static void CopyRelative(string source, string target, string relative)`

Copies one file across, keeping its relative path, and says nothing when it is absent.


## src/Akashic.Core/Services/RailLayoutService.cs

### `IReadOnlyList<string> Items { get; }`

The tiles the icon rail shows, in order, as `nav:&lt;id&gt;` or
`proj:&lt;path&gt;`.

### `Task SetItemsAsync(IReadOnlyList<string> items, CancellationToken ct = default);`

Persists the rail. An empty list is stored as empty, which reads back as
the built-in set.

### `public class RailLayoutService : IRailLayoutService`

Keeps the rail's contents in the shared settings.json, reading the file afresh on every
write so the sections owned by the other services survive.

### `private static bool Shaped(string raw) =>`

Drops anything that is not one of the two supported shapes.


## src/Akashic.Core/Services/ThemeService.cs

### `bool IsDarkTheme { get; }`

The persisted preference; defaults to dark.

### `Task SetDarkThemeAsync(bool dark, CancellationToken ct = default);`

Persists the preference. Reads settings.json afresh so edits made by the other
services sharing the file are not clobbered.

### `double SheetOpacity { get; }`

Alpha of the glass surfaces — window sheet, rail trays and popups.
The shell reads it at start-up and repaints immediately, so a slider never
needs a restart.

### `public class ThemeService : IThemeService`

Stores the light/dark preference in the shared settings.json, following the same
read-merge-write pattern as the other services so concurrent edits survive.

### `private static double ClampOpacity(double opacity) =>`

Keeps a value out of settings.json that the page cannot express any more.


## src/Akashic.Core/Services/UnityPackageExtractor.cs

### `public class UnityPackageExtractor : IUnityPackageExtractor`

Extracts and parses .unitypackage files using native .NET 8 TarReader and GZipStream.
A .unitypackage is a GZipped tar archive where each asset is stored under
a GUID-named directory containing: pathname, asset, asset.meta, and optionally preview.png.

### `private static string ReadPathName(byte[] data)`

A package `pathname` blob is stored as "&lt;asset path&gt;\n&lt;flags&gt;" (e.g. "Assets/Foo/Bar.cs\n00"),
so only its first line is usable as a path.


## src/Akashic.Core/Services/UnityPackageIconService.cs

### `public class UnityPackageIconService : IUnityPackageIconService`

Extracts a thumbnail image from a .unitypackage without loading the whole archive: a
streaming tar walk first looks for a `preview.png` entry (Unity writes one per asset),
then falls back to the first image asset the package names. Results are cached on disk,
keyed by path, size and mtime, so a list refresh re-reads nothing.

### `private static (byte[] Bytes, string Extension)? FirstPass(string packagePath, CancellationToken ct)`

First tar pass: any usable preview.png entry, preferably the largest offered.

### `private static string? FindFirstImageGuid(string packagePath, CancellationToken ct)`

Records each guid's asset path, returning the first one that names an image.

### `private static IEnumerable<(string Name, byte[] Data)> WalkEntries(string packagePath, CancellationToken ct)`

Streams every file entry of the archive as (entry name, fully read bytes). Small entries
(pathname, preview.png) are what callers want; anything huge is read anyway by the
stream copy, so the walk itself stays sequential and cheap on memory.


## src/Akashic.Core/Services/UnityPackageLibraryService.cs

### `public partial class UnityPackageLibraryService : IUnityPackageLibraryService`

Local .unitypackage library: scans configured folders plus manually added files,
and keeps a per-project ledger of what was imported so it can be reported and undone.

### `private static string Key(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);`

Origin keys are compared case-insensitively, so the stored form is normalised too.


## src/Akashic.Core/Services/VRChatBlueprintScanService.cs

### `public sealed partial class VRChatBlueprintScanService : IVRChatBlueprintScanService`

The SDK writes the id of whatever a scene was last built as into the scene's own YAML, as a
`blueprintId:` line on its descriptor component — the same field for worlds and avatars, so
one pattern covers both. Only the project's `Assets` folder is read: the SDK's bundled test
scenes under `Packages` carry other people's ids.

### `private void EnsureCloudIds()`

The cloud list is published by the sync page into memory, which is empty right after a restart
even though that same list has been sitting in the page's disk cache. Reading it back is what
lets the project pages offer the cloud jump without the sync page being opened first.

### `private IReadOnlyList<string> ReadScene(string path, bool force)`

Grabs every blueprint id token in a file rather than looking for a particular YAML key.

The SDK writes the id as `blueprintId: avtr_…` on its own component, but when the
descriptor lives in a prefab and the scene only overrides it, the same value arrives as a
two-line `propertyPath: blueprintId` / `value: avtr_…` modification pair — which a
key-shaped pattern silently misses, leaving whole projects unmatched. Matching the token is
safe because only ids the account actually holds are ever kept.


## src/Akashic.Core/Services/VRChatSyncService.cs

### `public class VRChatSyncService : IVRChatSyncService`

Talks to VRChat's own JSON API through the site's same-origin proxy: the browser session cookies
are accepted there, while the standalone api.vrchat.cloud host is a different registrable domain
and never receives them. Requests use a private HttpClient so these cookies can never travel
with the shared client the registry services use.

### `private const string VaultKey = "vrchat";`

Name of this site's slot in the encrypted session store.

### `public IReadOnlyList<StoredCookie>? SessionEntries`

The session with each cookie's own domain and path, for seeding the WebView2 back.

### `private void RestoreFromVault()`

Picks up a session saved by an earlier run, which is the point: a WebView2 profile reset used
to cost the user a fresh sign-in to vrchat.com.

### `private async Task<List<VRChatContentItem>> EnrichAsync(List<VRChatContentItem> rows, CancellationToken ct)`

Replaces list rows with their full records, a few requests at a time.

### `public async Task<VRChatContentItem> UpdateContentAsync(VRChatContentEdit edit, CancellationToken ct = default)`

The update endpoint resets anything it is not given, so every writable field is echoed back
from the freshly read record unless the edit changes it — a name-only edit once dropped a
world's capacity to the server default.

### `private static Dictionary<string, JsonElement> MergeSettings(VRChatContentItem current, VRChatContentEdit edit)`

Only the switches the dialog actually shows are merged in; every other key the server
reported is written back untouched, so unknown settings survive an edit.

### `public async Task<string> UploadImageAsync(string filePath, CancellationToken ct = default)`

Uploads a cover image the way the site does and returns the file URL to store.

### `private static string ContentCachePath => Path.Combine(`

The list lives beside the app's other read caches. It is plain content metadata — the session
that made the read possible is deliberately not part of it.

### `private static string? DescribeError(string payload)`

The API answers errors both as a string and as an object with a message.


## src/Akashic.Core/Services/VersionControlService.cs

### `Task<VcsTools> DetectToolsAsync(CancellationToken ct = default);`

Which hosting tools this machine has, with their versions.

### `void ForgetTools();`

Drops the memo so the next probe sees a tool that was installed, uninstalled or updated
outside this app. Without it the settings page's 重新检测 answers from cache forever.

### `Task<ProjectVcsState> ReadStateAsync(string projectPath, bool deep = false, CancellationToken ct = default);`

Reads how one project is hosted. Cheap by default: the markers on disk are parsed and no
external tool is asked. <paramref name="deep"/> adds the changed-file counts and history,
which does have to run git or cm.

### `Task<VcsRunResult> InstallAsync(string tool, CancellationToken ct = default);`

Installs a missing tool with winget, then re-detects.

### `Task<VcsRunResult> EnableLfsAsync(string projectPath, CancellationToken ct = default);`

Turns LFS on for one repository only, and writes the Unity attribute file.

### `Task<VcsRunResult> DisableLfsAsync(string projectPath, CancellationToken ct = default);`

Takes the LFS filters back out of this repository only.

### `public class VersionControlService : IVersionControlService`

Talks to git and to Unity Version Control (cm) for the projects that are hosted, and hosts the
ones that are not. Everything runs as an argument array against the executable, never through a
shell, so a folder or a commit message with spaces or quotes cannot turn into a command.

### `private static string? ShortVersion(string? raw)`

Each tool prints its version differently: `git version 2.42.0.windows.1`,
`git-lfs/3.8.0 (GitHub; windows amd64; go 1.25.9; git 77deabdf)` and a bare
`11.0.16.10275` from cm. Taking the last word, as this once did, reads the build
hash out of the LFS line and shows that as the version.

### `private static string? ReadOriginUrl(string config)`

Picks the origin URL out of git's ini-ish config without needing git itself.

### `private static readonly Dictionary<string, (string Id, string Name, string Url)> Installable =`

The three things the hosting features need. Git for Windows ships LFS with it, but
LFS is listed separately because a hand-rolled git install often lacks it.

### `public async Task<VcsRunResult> EnableLfsAsync(string projectPath, CancellationToken ct = default)`

Turns LFS on for one repository only. A global `git lfs install` would
rewrite the user's own git config, and a `migrate` would rewrite history
that may already be pushed, so neither is offered from here.

### `private const string UnityGitattributes = """`

Binary art goes through LFS; the .meta sidecars stay in the repository because
they are text and their diffs are what a merge conflict actually needs.


## src/Akashic.Core/Services/VpmRegistryService.cs

### `public class VpmRegistryService : IVpmRegistryService`

Fetches package listings from VPM registries and manages the local package cache.

### `public async Task<List<VpmPackage>> GetAllPackagesAsync(CancellationToken ct = default)`

Every package the configured repositories advertise, in repository priority order. A package
is owned by the first repository that lists it, so nothing appears without a home repository.

### `private async Task<List<VpmPackage>> ResolveRegistryPackagesAsync(string registryUrl, CancellationToken ct)`

Reads a repository listing, preferring the copy VCC already cached next to its settings.

### `public async Task<int> ImportVccRepositoriesAsync()`

Adds every repository VCC has cached that this app does not list yet. Returns how many were added.

### `public VccRepository? FindRepositoryFor(string packageName)`

The repository whose cached listing advertises this package, used to name the origin of packages
installed by VCC from a repository this app does not list.

### `private VccRepository? FindVccRepository(string registryUrl) =>`

Finds the repository VCC caches for a registry URL, ignoring a trailing slash or query.


## src/Akashic.Host/Api/AccountEndpoints.cs

### `public static class AccountEndpoints`

The three account-backed surfaces. Reading Booth's and Unity's lists still needs a
real browser, but that browser now lives in the frontend: the sidecar hands out the
extractor scripts and turns whatever JSON they return back into rows, so all of the
parsing, de-duplication and cache rules stay in the verified C# layer.


## src/Akashic.Host/Api/BootstrapEndpoints.cs

### `public static class BootstrapEndpoints`

Everything the shell needs before it can draw its first frame: which account is
signed in, where the folders are, and how many projects the nav badge should show.


## src/Akashic.Host/Api/EventEndpoints.cs

### `public static class EventEndpoints`

Server-sent events: download / install / backup progress and any state the core
changes on its own. Long jobs report through the hub instead of blocking a response.


## src/Akashic.Host/Api/GitHubEndpoints.cs

### `public static class GitHubEndpoints`

GitHub CLI status and the encrypted config sync that rides on top of it.

The identity route deliberately answers a hand-built object rather than the
`GitHubIdentity` record: that record carries the OAuth token, and the loopback
guard only keeps other local processes out — the token has no business in a JSON body.

The browser sign-in is here too. Its OAuth App pair is compiled into Akashic, so no route on this
box accepts or returns a credential — the surface for it simply does not exist.


## src/Akashic.Host/Api/LibraryEndpoints.cs

### `public static class LibraryEndpoints`

The local .unitypackage library, the import ledger per project, external packages
and the archive preview the import page draws its checklist from.


## src/Akashic.Host/Api/PackageEndpoints.cs

### `public static class PackageEndpoints`

The VPM registry: browsing, searching and installing into a project. Anything
that touches a project is addressed by its folder path, like the rest of the API.


## src/Akashic.Host/Api/ProjectEndpoints.cs

### `public static class ProjectEndpoints`

Local projects, the editors that open them, the templates new ones start from, and
the blueprint ids that tie them to the cloud. Everything is addressed by folder path,
which is the only identifier a Unity project really has.

### `private static async Task<IResult> Project<T>(IProjectService projects, string path,`

Runs <paramref name="action"/> against the project at that folder, or answers 404.


## src/Akashic.Host/Api/RestoreEndpoints.cs

### `public static class RestoreEndpoints`

The two ways a project comes back or goes out: the backup archives on disk, and the
hosting tools installed on the machine. Both address a project by its folder, and both
keep their answers in one shape so the page can render a list plus a result line.


## src/Akashic.Host/Api/SessionEndpoints.cs

### `public static class SessionEndpoints`

The browser lives in the frontend now, so the stored session has to travel both
ways: injected before the first navigation, exported after a login. Domains and
paths come along because a flattened name-to-value view loses the Booth pair and
plants Unity's token on the wrong host.


## src/Akashic.Host/Api/SettingsEndpoints.cs

### `public static class SettingsEndpoints`

Settings, and the two shell actions the shell needs on behalf of every page:
opening a folder in Explorer and a link in the default browser.


## src/Akashic.Host/Api/TemplateEndpoints.cs

### `public static class TemplateEndpoints`

Project templates, and the inputs the "new project" dialog needs. The display
strings live on `ProjectTemplate` as [JsonIgnore] computed members, so
they are resolved here rather than rebuilt in the frontend.


## src/Akashic.Host/EventHub.cs

### `public sealed class EventHub`

Fan-out for the server-sent events stream. Long jobs (download, install, backup)
report through `Progress` so the shell can draw a bar without polling.
A slow reader drops its oldest events rather than stalling the worker thread.

### `public IProgress<double> Progress(string kind, string id) =>`

Progress sink for one job, keyed so several bars can run at once.


## src/Akashic.Host/LoopbackGuard.cs

### `public static class LoopbackGuard`

The sidecar is loopback-only and every /api call has to carry the run token. That is
what keeps a browser page or another local process from driving the file system with
this user's rights, and it is also what stops a stray Origin from reaching the session
cookies the core services hold.


## src/Akashic.Host/SidecarOptions.cs

### `public sealed record SidecarOptions(int Port, string Token, int? ParentPid)`

Launch parameters of the sidecar. The token is generated per run unless the caller
(the Flutter shell that spawns this process) supplies one, and is handed back on
stdout so only that parent ever learns it.


# 二 · 解释性注释

## app/lib/main.dart

### `unawaited(`

The canvas is fully transparent so each piece casts its own shadow onto the
desktop; the runner needs the DWM "sheet of glass" margin for that to
actually be per-pixel alpha (see windows/runner/win32_window.cpp).

### `unawaited(_model.connect());`

The window is already up; the shell shows "connecting" until the sidecar
answers or refuses.

### `const fallback = [`

Segoe UI has no Chinese glyphs, so without this list the CJK runs drop to Skia's
generic substitute — which is what made the whole app look thin and misaligned.

### `final text = base.textTheme;`

Nearly every label in the app sets a size but no weight, so it inherits these
three. w500 is where 11-13 px Chinese stops looking shaved; the places that want
more already ask for w600/w700 themselves.


## app/lib/src/browser/browser_panel.dart

### `final raw = await controller.evaluateJavascript(`

The overflow can come from either box, and innerWidth is the layout
viewport the page actually laid out against.

### `if (progress > 0 && progress < 60) resetZoom();`

Undo our own scale early in a new navigation, so the next measurement
runs against the page's natural width.

### `Timer(const Duration(milliseconds: 1200), fitToWidth);`

Booth paints its grid a beat after the document settles.


## app/lib/src/host/host_client.dart

### `if (body != null) {`

A GET with a body trips dart:io's content-length guard, so only the
methods that actually send something write one.

### `final response = await request.close()`

Unpacking a multi-gigabyte backup, or a first commit over 30 000 files,
runs well past the ordinary read timeout.


## app/lib/src/host/host_models.dart

### `vrchat: HostSession.fromJson(`

The sidecar's camelCase of `VRChat` keeps the capital C: "vrChat".


## app/lib/src/pages/about_page.dart

### `final plus = model.hostVersion.indexOf('+');`

The sidecar reports "2.0.0+<full sha>"; the commit is useful, all 40 of it is not.

### `Container(`

The same treatment as the rail's mark: the light artwork on the
black base, so the two never drift apart by theme.


## app/lib/src/pages/assetstore_page.dart

### `final merged = await host.assetStoreMerge(accountRows);`

The cache folder is walked on the way in, so the assets that are already on
disk show up even before 我的资产 has ever been read.

### `if (byName.length == before) {`

The grid repaints after the pager settles, so an unchanged read is retried
a couple of times before it is taken for the end of the list.

### `for (var wait = 0; wait < 40 && _browser.currentState?.controller == null; wait++) {`

The parked browser is built during the first layout, which can land after this
page's own load has already asked it for a cover.

### `Positioned(`

The reader stays mounted off-canvas when the page is hidden: filling
covers walks a dozen storefront search pages, and a widget that is not
built has no browser to walk them in.


## app/lib/src/pages/booth_page.dart

### `await _seed();`

Reuse whatever is on the vault before asking the user to log in again.

### `final contents = await host.boothInspect(number);`

A Booth archive usually wraps the real deliverable, so the .unitypackage
files inside it — not the zip — are what gets registered.


## app/lib/src/pages/packages_page.dart

### `if (model.packageQuery.isNotEmpty) {`

A package picked from the panel's search arrives as a query to apply.


## app/lib/src/pages/projects_page.dart

### `await model.loadTemplates();`

The form needs the template matrix, which the sidecar computes.

### `_PillRow([`

Two rows with a fixed meaning each. A Wrap let a long VRChat label push the
Unity version onto a third line, which grew that one card and offset the grid.


## app/lib/src/pages/resources_page.dart

### `_imported = (await host.libraryFiles())`

This shelf is the imported side of Booth, same as the old page: only the
.unitypackage files tagged with the Booth origin, not the purchase list.


## app/lib/src/pages/restore_page.dart

### `final matchPath = widget.archive['projectPath'] as String?;`

Only a backup that names a registered project may pre-fill that project:
otherwise the form has to open on an empty 新目录, so no click can land on
somebody's real project by accident.

### `final isGit = kind == 'git' || kind == 'both';`

LFS is a git-only concern: a UVCS workspace already stores its binaries
natively, so offering the button there only invites a needless repo change.

### `Text('${isGit ? '远端' : '仓库'}：${state['remote']}',`

A UVCS workspace has no git remote: that string is the repository the
workspace pulls from, and UVCS itself owns it.


## app/lib/src/pages/settings_page.dart

### `WidgetsBinding.instance.addPostFrameCallback((_) async {`

The probe spawns git, git-lfs, cm and gh once each, so it runs when this page is
opened rather than on every start-up.

### `if (model.syncReveal != _revealed) {`

The account card asks for this by bumping a counter; scrolling has to wait for the
card to exist, which is the frame after the page is first shown.

### `crossAxisAlignment: CrossAxisAlignment.start,`

The section title carries its own bottom padding, so a centred row let
the taller button hang into the list below it.

### `_row('上次上传', uploaded.isEmpty`

The gist's address is deliberately never shown: the id alone is enough to open the
backup on the web, and the encrypted blob names its owner.


## app/lib/src/pages/vrchat_page.dart

### `if (mounted) setState(() => _status = _describe());`

The list is the reason this page exists, so it goes on screen before
anything that has to ask the network.

### `_self = await host.vrchatSelf();`

A dead session answers 401, which the client already folds into null.

### `if (_rows.isNotEmpty) _local = await model.vrchatLocalMap();`

Cached rows already answer the question the map is for, so the scan only
runs when there is something to match against.

### `if (_rows.isEmpty) await _refresh();`

Nothing on disk yet: the first read has to come from the API.

### `if (mounted) setState(() => _status = '读取失败：$error');`

A blank page with no explanation is worse than a red line of text.


## app/lib/src/shell/app_shell.dart

### `model.navJump = (id) => setState(() {`

Pages can ask the shell to change destination; the shell owns the index.

### `const SizedBox(width: 10),`

Same seam as the gaps between the rail's own pieces.

### `mode: NeuMode.inset,`

Page tray: recessed, thin uniform rim.


## app/lib/src/shell/left_rail.dart

### `final a = model.sheetOpacity;`

The same 全局不透明度 knob that fades the window sheet fades these trays, or
the rail would stay solid while everything around it turned glass.

### `final rows = tiles.length.clamp(1, AppModel.maxRailTiles);`

The tray grows with the tiles it actually holds and stops at the cap, so
pinning one more can never push the piece below it out of the window. A
fixed height here used to be a few pixels short of the chrome, which made
the list scroll even when every tile fitted.

### `Padding(`

The mark gets its own piece above the quick bar, built the same way as the
other two: glass base, 8 px inset, then the black tray. Inside a 36 px tile
it loses exactly the detail that makes it read as the app, so the black
square here is the same width as the quick bar's.

### `child: Image.asset('assets/brand/akashic-light.png',`

The light mark in both themes, as asked: the dark variant disappears
into the black tray.

### `maxHeight: (constraints.maxHeight - 242).clamp(180.0, double.infinity),`

72 px taller now that the icon piece sits above it, or a full quick
bar would push the wordmark strip out of the window.

### `child: ConstrainedBox(`

At most one row per tile, so nothing scrolls while the
window is tall enough; a short window still scrolls.

### `Padding(`

Pinned under the scrollable list: the way in and the way
out have to stay reachable however full the rail gets.
Same horizontal inset as the tile list, so the two
buttons sit on the same centre line as the tiles above.

### `child: RotatedBox(`

RotatedBox swaps the constraints, so the wordmark
stays on one line; a plain Transform.rotate would
wrap it inside the 62px rail. It lives down here so
a growing tray can never cover it.

### `final model = AppScope.of(context);`

Read the model through the scope: the shell keeps the same widget instance in
its modal slot, so a field would freeze the list at whatever it was when the
panel opened.


## app/lib/src/shell/nav_panel.dart

### `if (github.isEmpty) return detail;`

Before the first probe lands, the card still says what is on screen.


## app/lib/src/state/app_model.dart

### `await refreshBackupCounts();`

The card's 备份 pill has to move the moment a backup lands.

### `if (search != query) return;`

A newer query owns the results once it arrives.

### `fatal = '连接本地数据服务时出错：$error';`

Anything else still has to reach the screen instead of vanishing into
the unawaited task.

### `unawaited(loadGitHub());`

The sidebar card shows the account and the last sync, so both come in at startup;
the second one shares the single gh probe the first one makes.

### `return;`

The cards simply stay unmarked; 恢复/同步 reports the real failure.

### `status = '已检测：${vcsTools['summary'] ?? ''}';`

Without a line of its own the button looks dead when everything is already
installed, which is the common case.

### `await loadSync();`

The sync card's note is computed sidecar-side; without this it keeps saying
"未检测到 GitHub CLI" after the install that fixed exactly that.

### `github = const {'found': false, 'signedIn': false, 'login': ''};`

A missing CLI is the normal state on a fresh machine, not an error to report.

### `status = '已登录 $login。配置同步当前已关闭，需要时在 设置 → 配置同步 打开。';`

The switch means it: signing in is useful for private packages on its own, but
nothing leaves this machine until the user turns the sync back on.

### `if (choice != 2) unawaited(host?.setDarkTheme(choice == 1) ?? Future<void>.value());`

Only an explicit light / dark pick is a setting; "follow system" stays local.


## app/lib/src/ui/app_colors.dart

### `static const Color canvas = Color(0xFFBDBEBB);`

Canvas (outside the window card)

### `static const Color surface = Color(0xFFD3D5D1); // side panel zone`

Base surfaces (glass tints these further at render time)

### `static const Color rail = Color(0xFF2A2B2A);`

Dark rail

### `static const Color shadowDark = Color(0x2E000000); // ~18% black`

Soft neumorphic shadows: single light source, top-left

### `static const Color ink = Color(0xDE000000);`

Ink

### `static const Color greenBg = Color(0xFFBFE3C9);`

Accents


## app/lib/src/ui/neu_dialog.dart

### `final opacity = AppScope.of(context).sheetOpacity;`

Popups are glass too: without this the 全局不透明度 slider faded the window and
the rail but left every dialog — 新建项目 included — solid.

### `ConstrainedBox(`

The modal scaffold hands its child an unbounded height, so the cap has
to come from a box rather than an Expanded.


## app/lib/src/ui/neu_kit.dart

### `return SingleChildScrollView(`

A plain scroll view rather than a ListView: the settings page asks the shell to
scroll to one of these cards, and a lazily built list has not created that card
yet, so the request would find nothing to scroll to.

### `AnimatedContainer(`

Embossed underline: raised bar with a light top lip
and a cast shadow. Measured, so CJK labels work.

### `final box = context.findRenderObject() as RenderBox?;`

A 25-entry project list used to run off the bottom of the window with nothing to
scroll it, so the menu measures the room it has and flips above the trigger when
the space below is too tight.

### `var url = widget.url;`

The stores hand back protocol-relative covers ("//cdn…/x.png").

### `try {`

A cover the storefront never had is cut out of the package itself and handed over
as a local path. It is already on disk, so it needs no cache of its own.

### `}`

The extraction folder may have been cleaned; the placeholder glyph answers.

### `}`

A half-written or locked cache file is treated as no cache at all.

### `}`

Cover art is disposable: a full or read-only cache folder only costs the next fetch.

### `}`

A dead cover keeps the placeholder glyph, as it always has.

### `final local = Platform.environment['LOCALAPPDATA'];`

The sidecar keeps its data under the same folder, so the covers land beside the
list caches and the sync bundle picks nothing extra up.


## app/lib/src/ui/neumorphic.dart

### `Path innerRing() {`

A blurred ring hugging the inside of the edge — uniform by construction.

### `canvas.drawPath(`

Base uniform inner shadow.

### `canvas`

Directional pass: dark lip pushed toward top-left interior.

### `canvas`

Light lip pushed toward bottom-right interior.

### `final content = padding != null`

concave


## scripts/build-app.ps1

### `param(`

# Builds the Flutter shell together with the sidecar it spawns, and stages one into the other.
#
# The font step is not cosmetic: Flutter keeps its tree-shaken icon font in build\flutter_assets
# and does not notice when the set of Icons.* used in the app changes, so an incremental build
# ships the previous glyph list and buttons come out with blank icons. Deleting it forces a
# regeneration without paying for a full "flutter clean".
#
# Keep this file ASCII only: Windows PowerShell reads a BOM-less .ps1 as ANSI, so any non-ASCII
# character in a string breaks the parse.

### `$env:PATH = "$(Join-Path $root 'tools');$env:PATH"`

# The WebView2 plugin restores its NuGet package at build time, and the repo carries the
# client so a clean machine needs no global install.

### `if ($LASTEXITCODE -ne 0) { throw ('Akashic.Host build failed with exit code ' + $LASTEXITCODE) }`

# A native command's exit code does not raise, so without this check a failed C# build
# falls through to flutter and stages the PREVIOUS sidecar: the app then runs old code
# while looking like it was just rebuilt.

### `Get-Process akashic_ui, Akashic.Host -ErrorAction SilentlyContinue | ForEach-Object {`

# A running exe locks its own output, and the build then fails with MSB3027 rather than a
# clear message, so stop anything that is holding the directory.


## scripts/stage-dist.ps1

### `$ErrorActionPreference = 'Stop'`

# Replaces dist/Akashic with the Flutter build output and re-zips it with the
# same top-level folder the old archive used.
#
# The *.WebView2 directory is the runtime profile (login state, cache), not an
# artifact, so it must not ship.
#
# Keep this file ASCII only: Windows PowerShell reads a BOM-less .ps1 as ANSI.


## src/Akashic.Core/Models/BackupArchive.cs

### `new(@"^(?<name>.+)_(?<stamp>\d{8}_\d{6})$", RegexOptions.Compiled),`

This app: Seconds_of_Life_20260718_122519

### `new(@"^(?<name>.+)-(?<stamp>\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2})$", RegexOptions.Compiled),`

Unity VCC: EdenSpace-2026-07-13T18-20-05

### `var (fallbackEntries, fallbackBytes, fallbackReadable) = ReadIndex(path);`

A zip in the backup folder with an unrecognised name is still restorable;
only its date and owner have to come from the file system.


## src/Akashic.Core/Services/AssetStoreService.cs

### `private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _coverCache =`

Product covers remembered by name, so cache-only rows and list rows can share them
without hitting the storefront again. Filled from 我的资产 reads and search lookups,
and persisted to disk so they survive restarts.

### `_apiClient = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });`

Private clients: the shared HttpClient in the container also serves the registry
services, and a Unity session header must never travel with those requests.

### `if (fresh.Count == 0) return;`

An empty export means the browser had nothing to hand over — a reset profile, or a page
that never set cookies — so it must never overwrite a session that still works.

### `ImageUrl = TryGetCoverUrl(name, out var cover) ? cover : string.Empty`

The cover this name has already been matched to. Without it a cache-only row
always came back blank, so the page reopened the storefront search for it
on every visit and the downloaded thumbnails looked like they had vanished.

### `var files = new List<string>();`

An iterator cannot yield inside a try with a catch, so each directory is read into
a local list first and handed out after the guarded block.

### `var publisher = parts.Length >= 3 ? parts[0] : string.Empty;`

parts = [publisher, category, name.unitypackage] for the store's own layout; anything
shallower is a file the user dropped in themselves, so it has no publisher.

### `if (string.IsNullOrWhiteSpace(asset.ImageUrl) && TryGetCoverUrl(asset.Name, out var stored))`

The account row's cover also answers for the cache file it now owns. Both names get
the URL, because the file name and the display name differ often enough that
matching one of them alone left the other looking for a cover forever.

### `var payload = extractorResultJson.Trim();`

WebView2 hands script results back JSON-encoded, so a script that returns a string
arrives double-wrapped. The page reader may also hand back an array, one entry per
rendered 我的资源 page.

### `if (response.StatusCode is System.Net.HttpStatusCode.Found or System.Net.HttpStatusCode.MovedPermanently`

A redirect to the sign-in flow is how the store answers an expired session.

### `var current = LoadSettings();`

Re-read so edits made by the other services sharing settings.json are not clobbered.


## src/Akashic.Core/Services/BoothService.cs

### `private const string BrowserUserAgent =`

Booth answers 403 to non-browser user agents, so the requests made with the exported
cookies have to look like the WebView2 that produced them.

### `private const int MaxRedirectHops = 5;`

The downloadables endpoint hands out the file URL through a chain of redirects; the
signed link itself has to be fetched without cookies, so it is followed by hand.

### `_boothClient = new HttpClient(new HttpClientHandler`

Private clients: the shared HttpClient in the container is also used by the registry
services, and Booth's session header must never leak into those requests.

### `AllowAutoRedirect = false`

Redirecting away from booth.pm would drop the signed link into an unauthenticated
CDN hop, so the chain is walked one step at a time instead.

### `if (fresh.Count == 0) return;`

An empty export means the browser had nothing to hand over — a reset profile, or a page
that never set cookies — so it must never overwrite a session that still works.

### `public bool HasSession`

Rails hands out a session cookie to anonymous visitors too, so this only says a cookie set
was exported; whether it authenticates is Booth's answer.

### `var payload = extractorResultJson.Trim();`

WebView2 hands script results back JSON-encoded, so a script that returns a
string arrives double-wrapped.

### `var na = long.TryParse(a.Number, out var x) ? x : 0;`

Nothing states a version, so the larger downloadables id is the later upload.

### `if (!int.TryParse(item.Number, out _))`

Free items download anonymously, so a session is not demanded up front; the redirect
handler below reports a dead login only when Booth actually refuses the request.

### `if (response.IsSuccessStatusCode)`

Booth answers 200 with a sign-in page when the session has quietly expired.

### `if (sendCookies) AdoptRotatedCookies(response);`

The session value rotates on every call, so any refreshed cookie is kept for the next one.

### `if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))`

Archives from the web can carry traversal paths, so anything escaping the target is dropped.

### `var current = LoadSettings();`

Re-read so edits made by the other services sharing settings.json are not clobbered.

### `private const string ItemExtractorScript = """`

Runs on a product page (booth.pm/<shop>/items/<id>): free downloads show the same
.js-download-button controls the library uses, and the page's og: tags carry the
title and cover the library cards do not always expose.

### `private const string FreeExtractorScript = """`

Runs on the 無料ダウンロード library page. Those cards carry no download control at all —
just the product link, thumbnail, title and shop — so this yields product metadata whose
item pages are visited afterwards to collect the real downloadables.


## src/Akashic.Core/Services/BuiltinTemplates.cs

### `var (displayName, description) = ReadPackageInfo(folder, name);`

VCC names these com.vrchat.template.{world,avatar,base}; the folder is the fallback when a
hand-made template has no package.json at all.

### `if (!ranges.ContainsKey(Resolver)) ranges[Resolver] = "*";`

The resolver is what lets the user manage packages inside Unity afterwards; VCC adds it to
every project it creates even though no template declares it.

### `return parts[1].Contains('x') || parts[1].Contains('*') ? null : (major, minor);`

"x" is a wildcard patch, not a number, so a bare 0 would wrongly read as minor 0.


## src/Akashic.Core/Services/ConfigSyncService.cs

### `var hint = OwnerHint(key);`

Taken before the key is wiped: it is what lets a restore blame the wrong identity
rather than a corrupt file.

### `["secret"] = "github-id",`

What fed PBKDF2. Public and stable on purpose (account id + built-in pepper), so a
second machine derives the same key after one click in the browser.

### `["files"] = new JsonArray([.. bundle["files"]!.AsObject()`

Names only. Everything outside "payload" travels unencrypted, so a DeepClone
of the files here would ship the whole plaintext bundle — cookies included —
to GitHub beside the ciphertext it is supposed to hide.

### `var settings = LoadSettings();`

Nobody types or sees an id: this machine's own record first, then the account's gist list —
which is how a second machine finds the backup after one click in the browser.

### `owner = envelope["owner"]?["login"]?.GetValue<string>() ?? string.Empty;`

A field hand-edited into the wrong JSON type throws when it is read back, and that must
not turn into an unhandled error in the middle of a restore.

### `if (restored.Success) await RememberSourceAsync(envelope, target, ct).ConfigureAwait(false);`

The bundle holds settings.json as it looked when it was made — on a first push that is
before the gist id and the upload stamp were written down. Put them back so a machine
that has just been restored still knows where its backup lives.

### `}`

Bookkeeping only. A restore that worked must not be reported as a failure because the
stamp could not be written back.

### `if (entry.Name == "sessions") continue;`

Handled wholesale further down, and re-protected by the local vault.

### `try`

Refuse to write a settings file that would break every service on next read.

### `if (cookies.Count == 0) continue;`

The vault treats an empty set as a sign-out, so only a real list is written back.

### `var current = LoadSettings();`

Re-read first: every other service owns different fields of the same file.


## src/Akashic.Core/Services/DpapiSessionVault.cs

### `private static readonly byte[] Entropy =`

Not a key — DPAPI keys come from the Windows login. This only stops a same-user process from
handing our blob to any other ProtectedData caller and getting plaintext back.

### `if (clean.Count == 0)`

An empty set means the site reported a logged-out state, which is a reason to forget the
stored session rather than keep handing the services a dead cookie.

### `var target = Path.Combine(_directory, key + ".bin");`

Write aside so a crash mid-save cannot leave a half-written blob in place of a good one.

### `}`

Failing to remember a session must never break the read that is in flight.

### `return null;`

Wrong Windows account, or the blob was edited: treat it as no session at all.

### `if (element.ValueKind == JsonValueKind.Object)`

Blobs written before the domain was stored carry a flat name/value object. Promoting them
with the site's own root domain keeps a session that was already working — VRChat reads its
API off the name/value pair and never needed the domain — and gives the Asset Store's
session token back the domain it was actually issued on. The next export overwrites the
blob with the real per-cookie domains.


## src/Akashic.Core/Services/ExternalPackageService.cs

### `if (existing.Origin == PackageOrigin.Local && origin != PackageOrigin.Local)`

Re-adding a folder through Booth or Asset Store labels it, but never clears a label.

### `var refName = reference?.Trim();`

A branch name is only needed for the package.json probe; archive downloads accept HEAD.

### `if (package.Kind != ExternalPackageKind.LocalFolder)`

Git installs embed a copy under Packages/, local folders are only referenced.

### `foreach (var dir in GitHubPackageDirs.Append($"Assets/{repo.Repository}"))`

Assets/<repo> covers the layout git-hosted UPM packages usually ship with.

### `var scp = raw.Split(':');`

git@host:owner/repo(.git) style

### `var kind = host == "github.com" ? ExternalPackageKind.GitHub`

Self-hosted GitLab keeps the gitlab.com URL shapes, so it rides the same kind.

### `var skip = segments.Length > 2 && segments[2] == "-" ? 1 : 0;`

/tree/<ref> (GitHub, Gitee) and /- /tree/<ref> (GitLab) both name a branch or tag.

### `var current = LoadSettings();`

Re-read so edits made by the other services sharing settings.json are not clobbered.


## src/Akashic.Core/Services/GitHubCli.cs

### `var stdout = proc.StandardOutput.ReadToEndAsync(ct);`

Drained before the token is written: a pipe nobody reads would keep gh from finishing.

### `Forget();`

The keyring entry changed, so any cached identity is now about the old account.

### `if (_identityTask is { IsCompletedSuccessfully: true } cached && cached.Result?.SignedIn == true)`

A null or signed-out answer is not memoised: installing gh or running
`gh auth login` has to take effect without restarting the sidecar.

### `return new GitHubIdentity { Login = string.Empty, UserId = 0, Version = version };`

gh answers with the CLI present but nobody signed in; the settings page says so.

### `psi.Environment["GH_PAGER"] = "cat";`

No pager, no interactive prompt, no update nag: a spawn that waits for a keypress
would otherwise sit on the timeout and look like a network failure.

### `var body = JsonSerializer.SerializeToUtf8Bytes(new`

Bytes, not a string written with Encoding.UTF8: that pairing puts a BOM on the front
of the file and GitHub answers with "Problems parsing JSON".

### `var args = string.IsNullOrWhiteSpace(gistId)`

--jq .id keeps the reply to one line. Without it gh echoes the whole gist back,
which is the entire encrypted bundle, and the text capture would cut it off
mid-string while the gist had already been created.

### `(code, text) = await RunAsync(exe,`

The stored id points at a gist that is gone — deleted by hand, or left over
from another account. Creating a new one beats failing with a 404 the user
cannot act on.


## src/Akashic.Core/Services/GitHubLoopbackAuth.cs

### `using var http = new HttpClient(new HttpClientHandler`

私有 HttpClient：登录换到的东西绝不能进 DI 里那个共享客户端，三站请求靠它带 cookie。

### `}`

The browser may close the tab the moment it has the redirect; nothing to report.


## src/Akashic.Core/Services/GitHubOAuthApp.cs

### `return (string.Empty, string.Empty);`

A tampered or mismatched blob is the same as having none: the login page says so.


## src/Akashic.Core/Services/PackageInstaller.cs

### `ReadCommentHandling = JsonCommentHandling.Skip,`

Unity leaves // comments in some manifest.json files; parsing must survive them.

### `await ResolveDependenciesAsync(project, package, ct);`

1. Resolve dependencies first

### `var zipPath = await _registryService.DownloadPackageAsync(package, null, ct);`

2. Download the package

### `var targetDir = Path.Combine(project.PackagesPath, package.Name);`

3. Extract to project's Packages folder

### `await UpdateVpmManifestAsync(project, package);`

4. Update vpm-manifest.json

### `await AddLocalPackageReferenceAsync(project, package.Name);`

5. Update UPM manifest.json to reference the local package

### `var ordered = installedPaths`

Deepest paths first so folders are empty by the time they are deleted.


## src/Akashic.Core/Services/ProjectService.cs

### `if (File.Exists(project.ManifestPath))`

Read UPM manifest

### `if (IsUnityModule(name)) continue;`

Unity's own modules track the editor version, so they are out of scope here.

### `if (project.HasVpmManifest && File.Exists(project.VpmManifestPath))`

Read VPM manifest and override/add entries

### `foreach (var pkg in packages)`

package.json inside an embedded package carries the friendly name and the real version.

### `if (!File.Exists(metaPath) && pkg.Version.StartsWith("file:"))`

A file: reference keeps its files outside Packages/, so the metadata lives next to them.

### `var current = LoadSettings();`

This service only owns ProjectPaths; re-read so edits made by the registry
and UnityPackage library services in the same file are not clobbered.

### `try`

Unity Hub stores its registry with the version as the key.

### `try`

VCC lists the editors it was pointed at, keyed by version.

### `foreach (var root in settings.UnityEditorPaths)`

A scanned path is either an editor root itself or the folder that holds several of them.

### `foreach (var drive in DriveInfo.GetDrives()`

Editors installed outside Hub's registry — a copied Editor folder, or one Hub forgot to
report — still have to be startable, so each drive's usual Unity homes are walked a level
or two deep.

### `foreach (var inner in Directory.GetDirectories(sub))`

Hub-style layouts nest once more: <root>\Editor\<version>\Editor.

### `var current = LoadSettings();`

Re-read so the paths other services own are never clobbered.

### `var files = folder == "Packages" && excludePackages`

Dropping the package folders is where the size saving is: the manifests left
behind say which packages the project wants, and the Resolver can fetch the
packages themselves again.

### `if (string.IsNullOrEmpty(entry.Name)) continue;`

Directory entries have an empty Name; the files below them make the folders.

### `failed++;`

A read-only or locked file stops that one entry, not the whole restore.

### `private static string TemplatesRoot => Path.Combine(`

── Templates, new projects and folder detection ───────────────────────────────────────────

### `if (template.IsBuiltIn) return $"{template.Name} 是应用自带的模板，不能删除。";`

Built-ins are compiled in, so there is nothing on disk to forget.

### `foreach (var part in new[] { "Assets", "Packages", "ProjectSettings" })`

An official template is a complete skeleton, so it is copied the way Creator
Companion copies it. The template's own package.json describes the template rather
than the project, so it is deliberately left behind.

### `Directory.CreateDirectory(Path.Combine(target, "ProjectSettings"));`

No VCC on this machine: the compiled-in stand-in writes the smallest manifest that
still gives Unity its built-in modules, and the SDK packages are installed after.


## src/Akashic.Core/Services/ThemeService.cs

### `SheetOpacity = ClampOpacity(settings.SheetOpacity);`

Clamped on the way in as well: a file written by an older build, or by hand, must not
put the sheet outside the band the slider can express.

### `var settings = LoadSettings();`

Read the full settings afresh and write them back with only the theme changed, so
sections owned by the other services survive.

### `SheetOpacity = ClampOpacity(opacity);`

Clamped here rather than trusted from the shell: a NaN would otherwise be
written down and leave the window unreadable on the next start. The slider shows
this alpha as-is, so the band is 0.4 … 1.


## src/Akashic.Core/Services/UnityPackageIconService.cs

### `private const long PreviewMaxBytes = 8 * 1024 * 1024;`

Anything larger than this is a texture rather than a thumbnail; skip and keep looking.

### `var guid = FindFirstImageGuid(packagePath, ct);`

No preview entry anywhere: find the guid of the first image asset, then read that
asset in a second pass. pathname blobs are tiny, so pass one stays cheap.

### `break;`

Unity caps preview sizes, so the first valid entry is nearly always the answer.

### `if (parts[1].Equals("pathname", StringComparison.OrdinalIgnoreCase))`

Within the guid folder the pathname blob names the asset, so the image type is
known before the asset entry itself arrives.


## src/Akashic.Core/Services/UnityPackageLibraryService.cs

### `if (origin == PackageOrigin.Local)`

A file that came from Booth or Asset Store keeps that label even when a library folder
already covers it, so the tag survives moving the file into a scanned directory.

### `var boothRoot = NormalizeRoot(settings.BoothDownloadDirectory);`

Files sitting in the Booth cache came from a Booth purchase even when they were imported
before per-file origins were recorded, so infer the tag rather than leaving them 本地.

### `var current = LoadSettings();`

Re-read so concurrent edits by other services are not clobbered.


## src/Akashic.Core/Services/VRChatBlueprintScanService.cs

### `}`

An unreadable cache just means there is nothing to match against yet.

### `map.TryAdd(id, project);`

Two projects holding the same content means one of them is a copy; the first
registered wins rather than guessing.

### `string[] patterns = ["*.unity", "*.prefab", "*.asset"];`

The descriptor travels in scenes and in prefabs alike, so every YAML asset type that can
hold one is read. Packages is never touched: the SDK's own test scenes carry other
people's ids.

### `if (stamp.Value.Length <= MaxScanBytes)`

A scene is text, but an unusually huge one is not worth pulling into memory for.

### `}`

A scene Unity is still writing, or one we cannot open: nothing to link from it.

### `private const long MaxScanBytes = 128L * 1024 * 1024;`

Scene files are routinely tens of megabytes; past this a project is not worth the read.

### `private const int MaxIdsPerFile = 16;`

A project that references more content ids than this is a hub of imported assets, and the
extra entries would only be noise in the match list.


## src/Akashic.Core/Services/VRChatSyncService.cs

### `private const string ApiBase = "https://vrchat.com/api/1";`

Verified live against the account's own content: the list, detail and every write route below.

### `if (fresh.Count == 0) return;`

An empty export means the browser had nothing to hand over — a reset profile, or a page
that never set cookies — so it must never overwrite a session that still works.

### `_vault.Save(VaultKey, fresh);`

Whatever the page just handed over is the freshest truth about this session.

### `items.AddRange(await EnrichAsync(worlds, ct));`

The list endpoint reports capacity on a different scale than the record endpoint, and it
omits descriptions entirely, so each world is re-read through its own record.

### `if (root is null)`

A missing row is an answer, not a failure: the page shows it as "内容已不存在".

### `if (current.Kind == VRChatContentKind.World && edit.Publish is { } publish)`

Publishing is a separate endpoint on the site, so it follows the metadata write.

### `var id = root.TryGetProperty("id", out var i) ? i.GetString() : null;`

The response is the File record; its id is what imageUrl expects.

### `var root = await SendAsync(HttpMethod.Get,`

releaseStatus defaults to "public" server-side, which would hide every private draft.


## src/Akashic.Core/Services/VersionControlService.cs

### `if (_toolsTask is { IsCompletedSuccessfully: true }) return _toolsTask;`

A cancelled probe must not poison every later read, so only a finished one is kept.

### `var lfsVersion = gitPath == null`

git-lfs is a separate binary that git finds next to itself, so the only
portable question to ask git is whether the subcommand answers.

### `var utf8 = exe.EndsWith("git.exe", StringComparison.OrdinalIgnoreCase) ||`

git and gh always answer in UTF-8; cm writes in the console codepage, so it has to
keep the system default.

### `var parts = line[12..].Split(' ', StringSplitOptions.RemoveEmptyEntries);`

"# branch.ab +2 -1": two to push, one to pull.

### `lfs = config.Contains("[filter \"lfs\"]", StringComparison.OrdinalIgnoreCase);`

Reading the section is enough to know LFS is on, and it costs nothing:
the alternative is spawning `git config` once per project just to list them.

### `var note = branch == null ? "（.git 存在但读不出分支）" : string.Empty;`

A .git folder with no config at all is a repo that has never committed; saying
"已托管" for it would be a lie, so the branch stays empty and the note explains.

### `var text = ReadAll(direct);`

Worktrees and submodules leave a .git file that points at the real directory.

### `var selector = Path.Combine(plasticDir, "plastic.selector");`

Unity writes the workspace selector next to the workspace tree; it names the repository
and the branch or changeset the folder is currently loaded at.

### `var (fallbackCode, fallbackOut) = await RunProcAsync(tools.GitPath, path, ["init"],`

Older git has no -b; the branch is then renamed instead.

### `if (tools.GitLfsFound)`

Unity projects are mostly binary art, so a fresh repository starts with LFS on
whenever the machine has it; without it the first push of a few gigabytes fails.

### `_toolsTask = null;`

A binary installed moments ago is not on this process's PATH, so the probe has to
run again against the fixed install folders; dropping the cache is what makes it.

### `var attributes = Path.Combine(path, ".gitattributes");`

The attribute file this app wrote exists only to point at those filters. Leaving it
behind would have git ask for a clean/smudge command that is no longer configured.

### `await RunProcAsync(tools.GitPath, path, ["fetch", "--quiet", "origin"], NetworkTimeout,`

Tell the user which commits belong where without waiting for a push.

### `psi.Environment["GIT_TERMINAL_PROMPT"] = "0";`

A push to a private remote should open the credential window, not hang waiting for a
terminal that was never attached.


## src/Akashic.Core/Services/VpmRegistryService.cs

### `foreach (var missing in _registryUrls.Where(u => !ordered.Contains(u, StringComparer.OrdinalIgnoreCase)))`

A row filtered out of the view never reaches the reorder callback, so keep unknown urls.

### `var reposDir = Path.Combine(Path.GetDirectoryName(_vccSettingsPath) ?? string.Empty, "Repos");`

VCC's listing cache is what pairs a repository URL with a file we can read offline.

### `if (File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(known.LocalPath))`

VCC keeps one snapshot per fetch, and serves the newest one.

### `foreach (var user in ReadVccUserRepos())`

A name the user typed in VCC wins over whatever the listing advertises.


## src/Akashic.Host/Api/AccountEndpoints.cs

### `group.MapGet("/booth/library", (IBoothService booth, IUnityPackageLibraryService library) =>`

LocalZipPath and IsDownloaded are [JsonIgnore] on the model, so the numbers that
already have an archive are handed to the frontend next to the rows.

### `booth.ReconcileLocalArchives(request.Items);`

Reconciling marks which rows already have a local archive before the list is stored,
and folding keeps one row per product so the snapshot is not a pile of old versions.

### `group.MapPost("/assetstore/merge", async Task<IResult> (AssetStoreSave request,`

Account rows first with their local file attached, then whatever the cache
folder holds that the account list did not claim — otherwise the shelf
looks empty for everyone whose account list has not been read yet.

### `return Results.Json(request.Items.Concat(extra).ToList());`

A collection expression has no type to infer from here, so the union is
spelled out as a list.

### `group.MapPost("/assetstore/thumbnail", async Task<IResult> (AssetStoreThumbnail request,`

When the store never gave a cover, the package itself can: the first preview image
inside the .unitypackage, extracted once and kept on disk. Answered as a local path,
which the shell reads directly instead of pulling bytes through this socket.

### `var source = await vrchat.GetContentAsync(kind, request.Id, ct);`

Always edit against a freshly fetched record: VRChat's PUT resets any field
the request leaves out, so the list row is not a safe base.

### `group.MapPost("/vrchat/local-map", async Task<IResult> (IProjectService projects,`

One scan per call: the map is computed, published, and returned from the same pass,
because walking every project's Packages folder is the slow part of this page.

### `var hiddenByNumber = new Dictionary<string, int>(StringComparer.Ordinal);`

A snapshot written before folding existed still carries every version, so the
page gets the same view either way, plus what the survivors stand in for.

### `shelved = cache.Items.Where(i => Shelved(i, paths)).Select(i => i.Number).ToList(),`

A purchase counts as shelved when one of the .unitypackage files the library knows
about came out of that archive's own -extracted folder. Matching on the product
title never works: the package carries the file name, and versions differ.

### `booth.ReconcileLocalArchives(cache.Items);`

The archive path is not part of the cached JSON, so the download folder is
walked again here — otherwise inspect would report "还没有下载" for a zip
that is plainly on disk.


## src/Akashic.Host/Api/BootstrapEndpoints.cs

### `group.MapPost("/rail", async Task<IResult> (RailLayout layout, IRailLayoutService rail,`

The rail is the one piece of shell layout worth remembering across runs.

### `group.MapPost("/sessions/probe", async (IBoothService booth, CancellationToken ct) =>`

Only Booth can be asked cheaply whether a stored session still works; Unity and
VRChat answer 200 for anonymous requests too, so those stay "restored".

### `group.MapPost("/visual", async Task<IResult> (VisualSettings visual, IThemeService theme,`

Alpha of the glass surfaces. Answered with what was actually stored, because the
service clamps and the slider has to snap back to the allowed range.


## src/Akashic.Host/Api/GitHubEndpoints.cs

### `group.MapPost("/sync/push", async Task<IResult> (IConfigSyncService sync, CancellationToken ct) =>`

Both calls shell out to gh and wait on GitHub, so the shell gives them minutes.

### `group.MapPost("/sync/toggle", async Task<object> (SyncToggle toggle,`

The 启用 gist 同步 switch. Kept separate from /theme because it gates network writes,
not appearance.

### `group.MapPost("/sync/pull", async Task<IResult> (`

Restoring takes no argument: the sidecar knows its own record and can find the account's
backup, so no gist id ever has to be shown, typed, or handed around by the page.

### `group.MapPost("/github/login", async Task<IResult> (`

Waits on a human clicking Authorize in a browser, so this one is allowed to run for minutes.
The token itself is never part of the answer: it lands in the gh keyring and stays there.

### `var hasBackup = result.Ok && await gitHub.FindGistAsync(ct).ConfigureAwait(false) != null;`

Lets the page tell "this account already has a backup" apart from "let's make the
first one" without overwriting anything local on the way.


## src/Akashic.Host/Api/LibraryEndpoints.cs

### `group.MapPost("/library/files/remove", async (PathRequest request,`

Removing a row never deletes the file on disk; the library is a list of
pointers into folders the user owns.

### `group.MapPost("/library/parse", async Task<IResult> (DescribeRequest request,`

The archive holds every asset's bytes, so the preview sends a trimmed view —
shipping AssetData would put the whole package through JSON.


## src/Akashic.Host/Api/PackageEndpoints.cs

### `group.MapGet("/repositories", (IVpmRegistryService registry) => new`

Registry order is resolution priority, so the list itself is the setting.


## src/Akashic.Host/Api/ProjectEndpoints.cs

### `group.MapPost("/blueprints/scan", async Task<IResult> (ScanBlueprintRequest request,`

One project at a time: the shell renders the list first and fills the cloud
badge in as these come back, exactly like the desktop version does.


## src/Akashic.Host/Api/RestoreEndpoints.cs

### `group.MapGet("/backups/counts", async Task<IResult> (IProjectService projects) =>`

The project cards want just a count, so this avoids shipping the whole archive
list to a page that shows one pill.

### `group.MapGet("/vcs/tools", async Task<object> (IVersionControlService vcs,`

refresh=true is what the settings page's 重新检测 sends: without it a tool installed
or removed outside this app would never be noticed.

### `group.MapPost("/vcs/states", async Task<object> (VcsPathsRequest request,`

No paths means "every registered project", which is what the page opens with.

### `group.MapPost("/vcs/install", async Task<IResult> (VcsInstallRequest request,`

An install runs winget, which may raise a UAC prompt; the caller waits on it.


## src/Akashic.Host/Api/SessionEndpoints.cs

### `session.Value.Import(entries);`

ImportSession ignores an empty set on purpose, so a reset browser profile
cannot wipe a session that still works.


## src/Akashic.Host/Api/SettingsEndpoints.cs

### `group.MapPost("/shell/open", (OpenRequest request) =>`

Deliberately narrow: a link is only http(s) and a target is only an existing
folder. Shell-executing whatever path a caller names would turn this endpoint
into a way to run anything as this user.


## src/Akashic.Host/Api/TemplateEndpoints.cs

### `group.MapGet("/project-creation", (IProjectService projects) =>`

Unity only loads projects from its own minor line, so the dialog picks a
version first and offers only the templates that match it.


## src/Akashic.Host/LoopbackGuard.cs

### `var host = context.Request.Host.Host;`

A page that rebound its own host header to 127.0.0.1 still fails the token
check, but refusing the header outright keeps the surface honest.


## src/Akashic.Host/Program.cs

### `Environment.SetEnvironmentVariable("ASPNETCORE_URLS", null);`

The shell spawns this process and reads exactly one line back: the bound url and the
run token. Anything written to stdout would corrupt that handshake.

### `kestrel.Listen(System.Net.IPAddress.Loopback, options.Port);`

ListenLocalhost(0) refuses a dynamic port, so name the loopback address itself.

### `builder.Services.AddSingleton<HttpClient>();`

The same core graph the WinUI container builds. Only the views move to Flutter; every
service, and every behaviour verified against them, stays exactly where it is.
