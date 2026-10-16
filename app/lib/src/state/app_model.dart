import 'dart:async';

import 'package:flutter/widgets.dart';

import '../app_nav.dart';
import '../host/host_client.dart';
import '../host/host_models.dart';

/// Everything the shell and the project list need to know, kept in one place so a
/// page never talks to the sidecar directly.
class AppModel extends ChangeNotifier {
  HostClient? host;

  bool loading = true;
  String? fatal;
  String status = '';
  HostState? state;
  List<UnityProject> projects = [];
  List<TemplateRow> templates = [];
  CreationInput? creation;
  String hostVersion = '';
  bool templatesLoading = false;

  Future<void> refreshState() async {
    final client = host;
    if (client == null) return;

    try {
      state = await client.state();
    } on HostException catch (error) {
      status = error.message;
    }
    notifyListeners();
  }

  /// After anything that rewrites a project's manifests, so the cards and the
  /// nav badge show the new package counts.
  Future<void> refreshProjects() async {
    final client = host;
    if (client == null) return;

    try {
      projects = await client.projects();
      state = await client.state();
    } on HostException catch (error) {
      status = error.message;
    }
    notifyListeners();
  }

  Future<void> loadTemplates() async {
    final client = host;
    if (client == null || templates.isNotEmpty) return;

    templatesLoading = true;
    notifyListeners();
    try {
      templates = await client.templates();
      creation = await client.creationInput();
    } on HostException catch (error) {
      status = error.message;
    } finally {
      templatesLoading = false;
      notifyListeners();
    }
  }

  int get builtInTemplateCount => templates.where((t) => t.isBuiltIn).length;

  String get templateSummary =>
      '共 ${templates.length} 个模板 · 内置 $builtInTemplateCount · 自建 ${templates.length - builtInTemplateCount}';

  /// Settings writes go straight through and pull a fresh state back, so the page
  /// always shows what is actually on disk.
  Future<void> updateSetting(Future<void> Function(HostClient client) change) =>
      _run((client) async {
        await change(client);
        state = await client.state();
      });

  Future<void> saveTemplate(String projectPath, String name, String notes) =>
      _run((client) async {
        await client.saveTemplate(projectPath, name, notes);
        templates = await client.templates();
        creation = await client.creationInput();
        status = '已保存模板「$name」';
      });

  Future<void> deleteTemplate(TemplateRow template) => _run((client) async {
        await client.deleteTemplate(template.directory);
        templates = await client.templates();
        creation = await client.creationInput();
        status = '已删除模板「${template.name}」';
      });

  Future<void> createProject(
          {required String name,
          required String? parent,
          required String unityVersion,
          required String? templateDirectory}) =>
      _run((client) async {
        await client.createProject(name, parent, unityVersion, templateDirectory);
        projects = await client.projects();
        state = await client.state();
        status = '已创建 $name';
      });

  Future<void> backupProject(UnityProject project) => _run((client) async {
        final result = await client.backupProject(project.path);
        status = result;
        // The card's 备份 pill has to move the moment a backup lands.
        await refreshBackupCounts();
      });

  Future<void> openFolder(String path) => _run((client) => client.openFolder(path));

  Future<void> openUrl(String url) => _run((client) => client.openUrl(url));

  int cloudMatched = 0;
  int cloudTotal = 0;

  bool get cloudMatching => cloudTotal > 0 && cloudMatched < cloudTotal;

  static const categories = ['全部分类', '世界', '虚拟形象', 'VRChat', 'Unity'];
  static const cloudStates = ['VRChat：全部', '已上传', '未上传'];
  static const sortModes = ['最近启动', '最早启动', '名称 A→Z'];

  String category = categories.first;
  String cloudFilter = cloudStates.first;
  String sortBy = sortModes.first;

  /// 0 浅色 · 1 暗色 · 2 跟随系统. Seeded from settings.json, then the user wins.
  int themeChoice = 2;
  bool _themeOverridden = false;

  /// Glass sheet alpha, from settings.json. The 视觉效果 card drives it and the shell repaints
  /// on the spot, so a slider never waits for a restart. The slider shows this number as-is,
  /// so the band is 0.4 (most see-through) up to 1.0, with 0.7 shipped.
  double sheetOpacity = 0.7;
  Timer? _visualSave;

  /// The icon rail's tiles as `nav:<id>` / `proj:<path>`; empty falls back to the
  /// built-in set so a fresh install still looks like the product.
  List<String> rail = [];

  /// True while the rail shows its remove buttons.
  bool railEditing = false;

  void setRailEditing(bool editing) {
    railEditing = editing;
    notifyListeners();
  }

  List<String> get railTiles => rail.isEmpty ? AppNav.defaultRail : rail;

  /// Rail dots: how many projects the 项目 destination should advertise.
  Map<String, int> get badges => {
        if (projects.isNotEmpty) 'projects': projects.length,
      };

  Future<void> setRail(List<String> next) async {
    rail = next;
    notifyListeners();

    final client = host;
    if (client == null) return;
    try {
      rail = await client.setRail(next);
    } on HostException catch (error) {
      status = error.message;
    }
    notifyListeners();
  }

  /// A tile is 41px, so ten of them plus the header and the two editor buttons is
  /// what the tray can hold without pushing the piece below it out of the window.
  static const int maxRailTiles = 10;

  void toggleRailTile(String raw) {
    final next = [...railTiles];
    if (next.contains(raw)) {
      next.remove(raw);
    } else {
      if (next.length >= maxRailTiles) {
        status = '快捷栏最多放 $maxRailTiles 个，先移除一个。';
        notifyListeners();
        return;
      }
      next.add(raw);
    }
    setRail(next);
  }

  /// The last tile stays: an empty rail would read back as the built-in set, which is
  /// not what "I removed everything" should look like.
  void removeRailTile(String raw) {
    if (railTiles.length <= 1) return;
    setRail([...railTiles]..remove(raw));
  }

  // ---------- 搜索：项目与包 ----------

  String search = '';
  List<UnityProject> searchProjects = [];
  List<Map<String, dynamic>> searchPackages = [];
  bool searchBusy = false;
  Timer? _searchTimer;

  /// Handed to 包浏览器 so a package result opens already filtered.
  String packageQuery = '';

  bool get hasSearchResults =>
      search.isNotEmpty && (searchProjects.isNotEmpty || searchPackages.isNotEmpty);

  void runSearch(String value) {
    search = value.trim();
    _searchTimer?.cancel();

    if (search.isEmpty) {
      searchProjects = [];
      searchPackages = [];
      searchBusy = false;
      notifyListeners();
      return;
    }

    notifyListeners();
    _searchTimer = Timer(const Duration(milliseconds: 250), _performSearch);
  }

  Future<void> _performSearch() async {
    final client = host;
    if (client == null) return;

    final query = search;
    final lower = query.toLowerCase();
    searchBusy = true;
    searchProjects = projects
        .where((p) =>
            p.name.toLowerCase().contains(lower) ||
            p.path.toLowerCase().contains(lower))
        .take(8)
        .toList();
    notifyListeners();

    try {
      searchPackages = (await client.searchPackages(query))
          .cast<Map<String, dynamic>>()
          .take(8)
          .toList();
    } on HostException {
      searchPackages = [];
    }

    // A newer query owns the results once it arrives.
    if (search != query) return;
    searchBusy = false;
    notifyListeners();
  }

  void clearSearch() {
    _searchTimer?.cancel();
    search = '';
    searchProjects = [];
    searchPackages = [];
    searchBusy = false;
    notifyListeners();
  }

  void openSearchProject(UnityProject project) {
    clearSearch();
    openProject(project.path);
    jumpTo('projects');
  }

  void openSearchPackage(String name) {
    packageQuery = name;
    clearSearch();
    jumpTo('packages');
  }

  List<UnityProject> get visible {
    final shown = projects.where((p) {
      if (category != categories.first && p.projectTypeDisplayName != category) {
        return false;
      }
      if (cloudFilter == cloudStates[1] && !p.hasCloudLink) return false;
      if (cloudFilter == cloudStates[2] && p.hasCloudLink) return false;
      return true;
    }).toList();

    if (sortBy == sortModes[1]) {
      shown.sort((a, b) => a.lastModified.compareTo(b.lastModified));
    } else if (sortBy == sortModes[2]) {
      shown.sort((a, b) => a.name.toLowerCase().compareTo(b.name.toLowerCase()));
    } else {
      shown.sort((a, b) => b.lastModified.compareTo(a.lastModified));
    }

    return shown;
  }

  String get listSummary => projects.isEmpty
      ? '还没有登记任何工程'
      : (visible.length == projects.length
          ? '共 ${projects.length} 个项目'
          : '显示 ${visible.length} / 共 ${projects.length} 个项目');

  Future<void> connect() async {
    try {
      final client = await HostClient.start();
      host = client;
      state = await client.state();
      rail = state!.rail;
      projects = await client.projects();
      hostVersion = (await client.info())['version'] as String? ?? '';
      if (!_themeOverridden) themeChoice = state!.darkTheme ? 1 : 0;
      sheetOpacity = state!.sheetOpacity;
      status = '已加载 ${projects.length} 个项目';
    } on HostException catch (error) {
      fatal = error.message;
    } catch (error) {
      // Anything else still has to reach the screen instead of vanishing into
      // the unawaited task.
      fatal = '连接本地数据服务时出错：$error';
    } finally {
      loading = false;
      notifyListeners();
    }

    unawaited(matchCloud());
    unawaited(refreshVcsMarkers());
    unawaited(refreshBackupCounts());
    // The sidebar card shows the account and the last sync, so both come in at startup;
    // the second one shares the single gh probe the first one makes.
    unawaited(loadGitHub());
    unawaited(loadSync());
  }

  /// The list is on screen before this finishes: each project answers for itself and
  /// the tile fills in its cloud state as it goes.
  Future<void> matchCloud() async {
    final client = host;
    if (client == null || projects.isEmpty) return;

    cloudTotal = projects.length;
    cloudMatched = 0;
    notifyListeners();

    for (final project in projects) {
      try {
        project.cloudBlueprintIds = await client.scanBlueprints(project.path);
      } on HostException {
        project.cloudBlueprintIds = [];
      }
      cloudMatched += 1;
      await Future<void>.delayed(Duration.zero);
      notifyListeners();
    }
  }

  /// The detail view is swapped in inside the shell's own stack rather than pushed as
  /// a route, because routes cannot receive pointer input in this window.
  String? openProjectPath;

  void openProject(String path) {
    openProjectPath = path;
    notifyListeners();
  }

  void closeProject() {
    openProjectPath = null;
    notifyListeners();
  }

  /// Set by the shell so any page can land on another destination — the VRChat list
  /// jumps into a project's detail view.
  ValueChanged<String>? navJump;

  void jumpTo(String id) => navJump?.call(id);

  /// Blueprint ids carried across a 项目 → VRChat 数据同步 jump. The sync page takes
  /// them once and narrows its list to the uploads that project produced.
  List<String> _vrchatJumpIds = const [];

  void jumpToVrchatContent(List<String> ids) {
    _vrchatJumpIds = ids;
    jumpTo('vrchatsync');
  }

  List<String> takeVrchatJump() {
    final ids = _vrchatJumpIds;
    _vrchatJumpIds = const [];
    return ids;
  }

  /// Also set by the shell: the rail and the panel both select by index.
  ValueChanged<int>? navSelect;

  void selectNav(int index) => navSelect?.call(index);

  /// Bumped when something asks the settings page to bring the 配置同步 card into view.
  /// The page compares it on every build, because it has no other reason to hear about it.
  int syncReveal = 0;

  /// The sidebar account card is the way into the GitHub login: the settings page is
  /// the only place that can start it, and it is long enough to hide the card.
  void openGitHubLogin() {
    jumpTo('settings');
    syncReveal++;
    notifyListeners();
  }

  /// Walking every project's Packages folder is the slowest call on the VRChat page,
  /// so it runs once per session and is kept here rather than re-read per visit.
  Future<Map<String, dynamic>>? _vrchatLocalMap;

  Future<Map<String, dynamic>> vrchatLocalMap() {
    final cached = _vrchatLocalMap;
    if (cached != null) return cached;

    final client = host;
    if (client == null) return Future.value(const <String, dynamic>{});

    final started = client
        .vrchatLocalMap()
        .then((result) =>
            ((result['map'] as Map?) ?? const {}).cast<String, dynamic>())
        .catchError((Object _) {
      _vrchatLocalMap = null;
      return const <String, dynamic>{};
    });
    _vrchatLocalMap = started;
    return started;
  }

  Future<Map<String, dynamic>> refreshVrchatLocalMap() {
    _vrchatLocalMap = null;
    return vrchatLocalMap();
  }

  // ---------- 备份还原与版本托管 ----------

  String backupDirectory = '';
  List<Map<String, dynamic>> backups = [];
  bool backupsLoading = false;
  bool restoring = false;

  Map<String, dynamic> vcsTools = const {};
  List<Map<String, dynamic>> vcsStates = [];
  bool vcsLoading = false;

  /// One row is running an operation; the buttons on it go quiet until it returns.
  String vcsBusyPath = '';

  /// A winget install is in flight; the settings rows show it rather than go dark.
  bool installingTool = false;

  /// The last restore this page asked for, kept on screen because the job can run
  /// for minutes and the shell status line is easy to miss.
  String restoreMessage = '';

  /// Last tool output, kept so the page can show git's or cm's own words.
  Map<String, dynamic> vcsLog = const {};

  /// path → hosting marker. Read offline, so the project cards can tell a Git project
  /// from a UVCS one without spawning anything per row.
  Map<String, Map<String, dynamic>> vcsByPath = {};

  String hostingOf(String path) =>
      vcsByPath[path]?['kind'] as String? ?? '';

  /// path → how many archives in the backup folder carry that project.
  Map<String, int> backupCounts = {};

  /// Who the GitHub CLI is signed in as. Read once at connect time: it spawns gh, so it is
  /// not something to re-ask on every repaint of the sidebar.
  Map<String, dynamic> github = const {};

  /// What the 配置同步 page shows: gist id, last upload, and what would be carried.
  Map<String, dynamic> sync = const {};

  bool syncing = false;

  /// The last push or pull, kept on the page because the call runs for a minute or two.
  Map<String, dynamic> syncLog = const {};

  Future<void> refreshBackupCounts() async {
    final client = host;
    if (client == null) return;

    try {
      final counts = await client.backupCounts();
      backupCounts = {
        for (final entry in counts.entries) entry.key: (entry.value as num).toInt(),
      };
    } on HostException {
      return;
    }
    notifyListeners();
  }

  void _indexVcsStates(List<dynamic> states) {
    vcsByPath = {
      for (final state in states.cast<Map<String, dynamic>>())
        '${state['projectPath']}': state,
    };
  }

  Future<void> refreshVcsMarkers() async {
    final client = host;
    if (client == null) return;

    try {
      _indexVcsStates(await client
          .vcsStates([for (final project in projects) project.path]));
    } on HostException {
      // The cards simply stay unmarked; 恢复/同步 reports the real failure.
      return;
    }
    notifyListeners();
  }

  String get backupSummary => backupsLoading
      ? '正在扫描备份目录…'
      : backups.isEmpty
          ? '备份目录里没有可还原的压缩包'
          : '${backups.length} 个备份 · ${_shortDir(backupDirectory)}';

  String get vcsSummary => vcsTools.isEmpty
      ? '正在检测本机的版本托管工具…'
      : '${vcsTools['summary'] ?? ''} · 已托管 ${vcsStates.where((s) => s['kind'] != '').length} / ${vcsStates.length}';

  static String _shortDir(String path) =>
      path.length <= 46 ? path : '…${path.substring(path.length - 44)}';

  Future<void> loadBackups() async {
    final client = host;
    if (client == null) return;

    backupsLoading = true;
    notifyListeners();

    try {
      final result = await client.backups();
      backupDirectory = result['directory'] as String? ?? '';
      backups = ((result['items'] as List?) ?? const [])
          .cast<Map<String, dynamic>>();
    } on HostException catch (error) {
      status = error.message;
    } finally {
      backupsLoading = false;
      notifyListeners();
    }
  }

  Future<void> restoreBackup(
      {required String filePath,
      required String targetPath,
      required bool overwrite}) async {
    final client = host;
    if (client == null) return;

    restoring = true;
    status = overwrite ? '正在还原（覆盖同名文件）…' : '正在还原（保留已有文件）…';
    restoreMessage = status;
    notifyListeners();

    try {
      status = restoreMessage =
          await client.restoreBackup(filePath, targetPath, overwrite);
    } on HostException catch (error) {
      status = restoreMessage = error.message;
    } finally {
      restoring = false;
      notifyListeners();
    }
  }

  Future<void> loadVcs() async {
    final client = host;
    if (client == null) return;

    vcsLoading = true;
    notifyListeners();

    try {
      vcsTools = await client.vcsTools();
      vcsStates = (await client.vcsStates([for (final p in projects) p.path]))
          .cast<Map<String, dynamic>>();
      _indexVcsStates(vcsStates);
    } on HostException catch (error) {
      status = error.message;
    } finally {
      vcsLoading = false;
      notifyListeners();
    }
  }

  /// Only the tool probe, for the settings page: the per-project states are heavier.
  Future<void> loadVcsTools({bool refresh = true}) async {
    final client = host;
    if (client == null) return;

    try {
      vcsTools = await client.vcsTools(refresh: refresh);
      // Without a line of its own the button looks dead when everything is already
      // installed, which is the common case.
      status = '已检测：${vcsTools['summary'] ?? ''}';
    } on HostException catch (error) {
      status = error.message;
    }
    notifyListeners();
  }

  Future<void> installVcsTool(String tool) async {
    final client = host;
    if (client == null) return;

    installingTool = true;
    status = '正在安装 $tool，可能弹出 Windows 授权窗口…';
    notifyListeners();

    try {
      final result = await client.vcsInstall(tool);
      vcsLog = result;
      status = '${result['title'] ?? ''}';
      vcsTools = await client.vcsTools();
      if (tool == 'gh') {
        await loadGitHub();
        // The sync card's note is computed sidecar-side; without this it keeps saying
        // "未检测到 GitHub CLI" after the install that fixed exactly that.
        await loadSync();
      }
    } on HostException catch (error) {
      status = error.message;
    } finally {
      installingTool = false;
      notifyListeners();
    }
  }

  Future<void> loadGitHub() async {
    final client = host;
    if (client == null) return;

    try {
      github = await client.githubStatus();
    } on HostException {
      // A missing CLI is the normal state on a fresh machine, not an error to report.
      github = const {'found': false, 'signedIn': false, 'login': ''};
    }
    notifyListeners();
  }

  Future<void> loadSync() async {
    final client = host;
    if (client == null) return;

    try {
      sync = await client.syncStatus();
    } on HostException catch (error) {
      sync = {'note': error.message};
    }
    notifyListeners();
  }

  /// Pushing re-asks the CLI for its identity, so a login that happened in a terminal since
  /// the app started is picked up without a restart.
  Future<void> pushSync() async {
    final client = host;
    if (client == null) return;

    syncing = true;
    status = '正在加密并上传配置…';
    notifyListeners();

    try {
      syncLog = await client.syncPush();
      sync = await client.syncStatus();
      status = '${syncLog['title'] ?? ''}';
    } on HostException catch (error) {
      status = error.message;
    } finally {
      syncing = false;
      notifyListeners();
    }
  }

  Future<void> pullSync() async {
    final client = host;
    if (client == null) return;

    syncing = true;
    status = '正在从 gist 下载并解密…';
    notifyListeners();

    try {
      syncLog = await client.syncPull();
      await loadGitHub();
      await loadSync();
      status = '${syncLog['title'] ?? ''}';
    } on HostException catch (error) {
      status = error.message;
    } finally {
      syncing = false;
      notifyListeners();
    }
  }

  bool githubLoggingIn = false;

  /// One click in the user's own browser, no code to copy and no passphrase to remember. The OAuth
  /// App it rides on is compiled into the sidecar, so there is nothing to configure here.
  ///
  /// A first login on an account without a backup uploads this machine's config straight away, which
  /// is the point of the flow. When the account already has one, this only says so: overwriting local
  /// settings is left to 从 gist 恢复, where the page can ask before it replaces anything.
  Future<void> githubLogin() async {
    final client = host;
    if (client == null) return;

    githubLoggingIn = true;
    status = '已在浏览器打开 GitHub 授权页，点一次 Authorize 就能回来…';
    notifyListeners();

    try {
      final result = await client.githubLogin();
      if (result['ok'] != true) {
        final guidance = '${result['guidance'] ?? ''}';
        status = guidance.isEmpty
            ? '${result['error'] ?? 'GitHub 登录失败'}'
            : '${result['error']}：${guidance.split('\n').first}';
        await loadGitHub();
        await loadSync();
        return;
      }

      await loadGitHub();
      await loadSync();

      final login = '${result['login'] ?? ''}';
      if (sync['enabled'] == false) {
        // The switch means it: signing in is useful for private packages on its own, but
        // nothing leaves this machine until the user turns the sync back on.
        status = '已登录 $login。配置同步当前已关闭，需要时在 设置 → 配置同步 打开。';
      } else if (result['hasBackup'] == true) {
        status = '已登录 $login。这个账号已经有配置备份，点「从 gist 恢复」就能套到本机。';
      } else {
        status = '已登录 $login，正在把本机配置上传成第一份备份…';
        notifyListeners();
        await pushSync();
      }
    } on HostException catch (error) {
      status = error.message;
    } finally {
      githubLoggingIn = false;
      notifyListeners();
    }
  }

  /// The changed-file counts need git or cm, so they are only taken for the row
  /// the user is actually looking at.
  Future<void> refreshVcsState(String path) async {
    final client = host;
    if (client == null || path.isEmpty) return;

    try {
      _mergeVcsState(await client.vcsState(path, deep: true));
    } on HostException catch (error) {
      status = error.message;
    }
    notifyListeners();
  }

  void _mergeVcsState(Map<String, dynamic> state) {
    final path = state['projectPath'] as String? ?? '';
    final index =
        vcsStates.indexWhere((s) => (s['projectPath'] as String?) == path);

    if (index >= 0) {
      vcsStates[index] = state;
    } else {
      vcsStates = [...vcsStates, state];
    }

    vcsByPath[path] = state;
  }

  Future<void> runVcs(String action, String path,
      {String? kind, String? message, String? url, bool push = false}) async {
    final client = host;
    if (client == null) return;

    vcsBusyPath = path;
    notifyListeners();

    try {
      final result = await client.vcsRun(action, path,
          kind: kind, message: message, url: url, push: push);
      vcsLog = result;
      status = '${result['title'] ?? ''}';
      _mergeVcsState(await client.vcsState(path, deep: true));
    } on HostException catch (error) {
      status = error.message;
    } finally {
      vcsBusyPath = '';
      notifyListeners();
    }
  }

  void setStatus(String message) {
    status = message;
    notifyListeners();
  }

  /// Forms are hosted inside the shell's own stack rather than as a route: a route
  /// pushed over this frameless transparent window never receives pointer events,
  /// so its buttons are dead.
  Widget? modal;

  void showModal(Widget form) {
    modal = form;
    notifyListeners();
  }

  void closeModal() {
    modal = null;
    notifyListeners();
  }

  void applyFilters({String? category, String? cloudFilter, String? sortBy}) {
    if (category != null) this.category = category;
    if (cloudFilter != null) this.cloudFilter = cloudFilter;
    if (sortBy != null) this.sortBy = sortBy;
    notifyListeners();
  }

  void resetFilters() {
    category = categories.first;
    cloudFilter = cloudStates.first;
    sortBy = sortModes.first;
    notifyListeners();
  }

  Future<void> addProject(String path) => _run((client) async {
        final added = await client.addProject(path);
        if (!projects.any((p) => p.path == added.path)) projects.add(added);
        status = '已添加 ${added.name}';
        await matchCloudFor(added);
      });

  Future<void> detectProjects(String directory) => _run((client) async {
        final result = await client.detectProjects(directory);
        projects = await client.projects();
        status = result.message;
      });

  Future<void> removeProject(UnityProject project) => _run((client) async {
        await client.removeProject(project.path);
        projects.removeWhere((p) => p.path == project.path);
        status = '已从列表移除 ${project.name}';
      });

  Future<void> launch(UnityProject project) => _run((client) async {
        final result = await client.launchProject(project.path);
        status = result.message;
      });

  Future<void> matchCloudFor(UnityProject project) async {
    final client = host;
    if (client == null) return;

    try {
      project.cloudBlueprintIds = await client.scanBlueprints(project.path);
    } on HostException {
      return;
    }
    notifyListeners();
  }

  void chooseTheme(int choice) {
    _themeOverridden = true;
    themeChoice = choice;
    notifyListeners();

    // Only an explicit light / dark pick is a setting; "follow system" stays local.
    if (choice != 2) unawaited(host?.setDarkTheme(choice == 1) ?? Future<void>.value());
  }

  /// Applied to the window as the thumb moves; settings.json only sees it once the drag
  /// settles, and then only with what the sidecar kept after clamping.
  void setVisual({required double opacity}) {
    sheetOpacity = opacity.clamp(0.4, 1.0).toDouble();
    notifyListeners();

    _visualSave?.cancel();
    _visualSave = Timer(const Duration(milliseconds: 450), () async {
      final client = host;
      if (client == null) return;
      try {
        final kept = await client.setVisual(sheetOpacity);
        sheetOpacity = (kept['opacity'] as num?)?.toDouble() ?? sheetOpacity;
        notifyListeners();
      } on HostException catch (error) {
        status = error.message;
      }
    });
  }

  Future<void> setSyncEnabled(bool enabled) async {
    final client = host;
    if (client == null) return;

    try {
      await client.setSyncEnabled(enabled);
      status = enabled ? '已启用 gist 同步' : '已关闭 gist 同步，GitHub 登录仍然可用';
    } on HostException catch (error) {
      status = error.message;
    }
    await loadSync();
  }

  Future<void> _run(Future<void> Function(HostClient client) action) async {
    final client = host;
    if (client == null) return;

    try {
      await action(client);
    } on HostException catch (error) {
      status = error.message;
    }
    notifyListeners();
  }

  @override
  void dispose() {
    _visualSave?.cancel();
    unawaited(host?.dispose() ?? Future<void>.value());
    super.dispose();
  }
}
