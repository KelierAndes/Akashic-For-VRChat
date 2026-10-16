import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math';

import 'host_models.dart';

/// Raised for a refused request, carrying whatever `Problem` detail the Host sent.
class HostException implements Exception {
  HostException(this.message, {this.status});

  final String message;
  final int? status;

  @override
  String toString() => message;
}

/// Owns the sidecar process and talks to it over loopback HTTP.
///
/// The token is generated here and handed to the child through the environment, so
/// nothing sensitive ever has to be parsed back out of its output; the only thing
/// read from stdout is the bound url.
class HostClient {
  HostClient._(this._base, this._token, this._process, this.log);

  final Uri _base;
  final String _token;
  final Process? _process;
  final List<String> log;
  final HttpClient _http = HttpClient()
    ..connectionTimeout = const Duration(seconds: 10);

  static const _handshakeKind = 'akashic-host';

  static Future<HostClient> start({String? executableOverride}) async {
    final log = <String>[];
    final exe = _locateHostExecutable(executableOverride, log);
    if (exe == null) {
      throw HostException(
        '找不到 Akashic.Host.exe。\n先构建侧车，再把它放到应用目录的 host\\ 子文件夹里。\n${log.join('\n')}',
      );
    }

    final token = base64Url.encode(List.generate(24, (_) => Random.secure().nextInt(256)))
        .replaceAll('=', '');

    final Process process;
    try {
      process = await Process.start(
        exe,
        ['--port', '0', '--parent-pid', '$pid'],
        environment: {'AKASHIC_HOST_TOKEN': token},
      );
    } catch (error) {
      throw HostException('侧车启动失败：$error');
    }

    process.stderr
        .transform(const Utf8Decoder(allowMalformed: true))
        .transform(const LineSplitter())
        .listen((line) {
      if (log.length < 200) log.add(line);
    });

    final handshake = await _readHandshake(process, log);
    if (handshake == null) {
      process.kill();
      throw HostException('侧车没有报告监听地址。\n${log.join('\n')}');
    }

    return HostClient._(Uri.parse(handshake['url'] as String), token, process, log);
  }

  /// Connects to a sidecar that is already running, which is how the app is driven
  /// while the Host is being debugged from a terminal.
  factory HostClient.attach(String url, String token) =>
      HostClient._(Uri.parse(url), token, null, []);

  static Future<Map<String, dynamic>?> _readHandshake(
      Process process, List<String> log) async {
    final lines = process.stdout
        .transform(const Utf8Decoder(allowMalformed: true))
        .transform(const LineSplitter());

    try {
      return await lines
          .map((line) => line.trim())
          .where((line) => line.startsWith('{'))
          .map((line) {
            try {
              return Map<String, dynamic>.from(jsonDecode(line) as Map);
            } catch (_) {
              return null;
            }
          })
          .firstWhere(
        (decoded) => decoded != null && decoded['kind'] == _handshakeKind,
        orElse: () => null,
      ).timeout(const Duration(seconds: 20));
    } on TimeoutException {
      log.add('handshake timed out');
      return null;
    }
  }

  static String? _locateHostExecutable(String? override, List<String> log) {
    final appDirectory = File(Platform.resolvedExecutable).parent.path;
    final fromEnvironment = Platform.environment['AKASHIC_HOST_EXE'];
    final candidates = <String>[
      ?override,
      ?fromEnvironment,
      '$appDirectory\\host\\Akashic.Host.exe',
      '$appDirectory\\Akashic.Host.exe',
    ];

    for (final candidate in candidates) {
      if (File(candidate).existsSync()) return candidate;
      log.add('未找到 $candidate');
    }

    return null;
  }

  Future<void> dispose() async {
    _process?.kill(ProcessSignal.sigterm);
    _http.close(force: true);
  }

  Future<dynamic> _send(String method, String path,
      [Object? body, Duration? responseTimeout]) async {
    final request = await _http
        .openUrl(method, _base.resolve(path))
        .timeout(const Duration(seconds: 15));
    request.headers.set('X-Akashic-Token', _token);

    // A GET with a body trips dart:io's content-length guard, so only the
    // methods that actually send something write one.
    if (body != null) {
      request.headers.contentType = ContentType.json;
      request.write(jsonEncode(body));
    }

    // Unpacking a multi-gigabyte backup, or a first commit over 30 000 files,
    // runs well past the ordinary read timeout.
    final response = await request.close()
        .timeout(responseTimeout ?? const Duration(seconds: 60));
    final text = await response.transform(utf8.decoder).join();

    if (response.statusCode >= 400) {
      var detail = text;
      try {
        detail = (jsonDecode(text) as Map)['detail'] as String? ?? text;
      } catch (_) {}
      throw HostException(detail, status: response.statusCode);
    }

    if (text.isEmpty) return null;
    return jsonDecode(text);
  }

  Future<HostState> state() async =>
      HostState.fromJson(await _send('GET', 'api/state') as Map<String, dynamic>);

  Future<List<UnityProject>> projects() async =>
      (await _send('GET', 'api/projects') as List)
          .cast<Map<String, dynamic>>()
          .map(UnityProject.fromJson)
          .toList();

  Future<UnityProject> addProject(String path) async => UnityProject.fromJson(
      await _send('POST', 'api/projects/add', {'path': path}) as Map<String, dynamic>);

  Future<void> removeProject(String path) =>
      _send('POST', 'api/projects/remove', {'path': path});

  Future<LaunchResult> launchProject(String path) async => LaunchResult.fromJson(
      await _send('POST', 'api/projects/launch', {'path': path}) as Map<String, dynamic>);

  /// Returns the line the service wrote, so the shell can show it verbatim.
  Future<String> backupProject(String path) async {
    final result = await _send('POST', 'api/projects/backup', {'path': path});
    return (result as Map)['message'] as String? ?? '备份完成';
  }

  Future<DetectResult> detectProjects(String directory) async => DetectResult.fromJson(
      await _send('POST', 'api/projects/detect', {'directory': directory})
          as Map<String, dynamic>);

  Future<List<String>> scanBlueprints(String path, {bool force = false}) async {
    final result = await _send('POST', 'api/blueprints/scan', {'path': path, 'force': force});
    return ((result as Map)['ids'] as List).cast<String>();
  }

  Future<void> setDarkTheme(bool dark) => _send('POST', 'api/theme', {'dark': dark});

  /// Answers with what the sidecar actually kept, because it clamps the value.
  Future<Map<String, dynamic>> setVisual(double opacity) async =>
      await _send('POST', 'api/visual', {'opacity': opacity})
          as Map<String, dynamic>;

  Future<void> setSyncEnabled(bool enabled) =>
      _send('POST', 'api/sync/toggle', {'enabled': enabled});

  Future<Map<String, dynamic>> info() async =>
      await _send('GET', 'api/info') as Map<String, dynamic>;

  Future<List<TemplateRow>> templates() async =>
      (await _send('GET', 'api/templates') as List)
          .cast<Map<String, dynamic>>()
          .map(TemplateRow.fromJson)
          .toList();

  Future<CreationInput> creationInput() async => CreationInput.fromJson(
      await _send('GET', 'api/project-creation') as Map<String, dynamic>);

  Future<String> templatesDirectory() async =>
      (await _send('GET', 'api/templates/directory') as Map<String, dynamic>)['directory']
          as String;

  Future<void> saveTemplate(String path, String name, String notes) => _send(
      'POST', 'api/templates/save', {'path': path, 'name': name, 'notes': notes});

  Future<void> deleteTemplate(String directory) =>
      _send('POST', 'api/templates/delete', {'directory': directory});

  Future<void> createProject(String name, String? parent, String unityVersion,
          String? templateDirectory) =>
      _send('POST', 'api/projects/create', {
        'name': name,
        'parentDirectory': parent,
        'unityVersion': unityVersion,
        'templateDirectory': templateDirectory,
      });

  Future<void> setDefaultProjectPath(String directory) =>
      _send('POST', 'api/settings/default-project-path', {'directory': directory});

  Future<void> setBackupPath(String directory) =>
      _send('POST', 'api/settings/backup-path', {'directory': directory});

  Future<void> setBackupExcludesPackages(bool exclude) =>
      _send('POST', 'api/settings/backup-excludes-packages', {'exclude': exclude});

  Future<void> setBoothDownloadDir(String directory) =>
      _send('POST', 'api/settings/booth-download-dir', {'directory': directory});

  Future<void> setAssetStoreCacheDir(String directory) =>
      _send('POST', 'api/settings/assetstore-cache-dir', {'directory': directory});

  Future<void> setAssetStoreDownloadDir(String directory) =>
      _send('POST', 'api/settings/assetstore-download-dir', {'directory': directory});

  Future<void> forgetSession(String site) =>
      _send('POST', 'api/settings/forget-session', {'site': site});

  /// Rewrites the icon rail and returns what the sidecar kept.
  Future<List<String>> setRail(List<String> items) async {
    final result = await _send('POST', 'api/rail', {'items': items});
    return (((result as Map)['items'] as List?) ?? const []).cast<String>();
  }

  /// The stored session for one site, ready to inject into the embedded browser.
  Future<List<CookieSpec>> sessionCookies(String site) async {
    final result = await _send('GET', 'api/sessions/$site/cookies');
    return ((result as Map)['cookies'] as List)
        .cast<Map<String, dynamic>>()
        .map(CookieSpec.fromJson)
        .toList();
  }

  Future<void> importSessionCookies(String site, List<CookieSpec> cookies) =>
      _send('POST', 'api/sessions/$site/cookies', {
        'cookies': [for (final cookie in cookies) cookie.toJson()],
      });

  Future<void> addEditor(String pickedPath) =>
      _send('POST', 'api/editors/add', {'path': pickedPath});

  Future<void> removeEditor(String exePath) =>
      _send('POST', 'api/editors/remove', {'exePath': exePath});

  Future<void> openFolder(String path) =>
      _send('POST', 'api/shell/open', {'target': path, 'kind': 'folder'});

  Future<void> openUrl(String url) =>
      _send('POST', 'api/shell/open', {'target': url, 'kind': 'url'});

  // ---------- registry ----------

  Future<List<dynamic>> packages() async =>
      await _send('GET', 'api/packages') as List<dynamic>;

  Future<List<dynamic>> searchPackages(String query) async =>
      await _send('GET', 'api/packages/search?query=${Uri.encodeQueryComponent(query)}')
          as List<dynamic>;

  Future<Map<String, dynamic>> installPackage(String path, String package) async =>
      await _send('POST', 'api/packages/install',
          {'path': path, 'packageName': package}) as Map<String, dynamic>;

  Future<Map<String, dynamic>> removePackage(String path, String package) async =>
      await _send('POST', 'api/packages/remove',
          {'path': path, 'packageName': package}) as Map<String, dynamic>;

  // ---------- local library ----------

  Future<List<dynamic>> libraryFiles() async =>
      await _send('GET', 'api/library/files') as List<dynamic>;

  Future<List<dynamic>> libraryFolders() async =>
      await _send('GET', 'api/library/folders') as List<dynamic>;

  Future<List<dynamic>> libraryAdded() async =>
      await _send('GET', 'api/library/added') as List<dynamic>;

  Future<List<dynamic>> addLibraryFolder(String path) async =>
      await _send('POST', 'api/library/folders/add', {'path': path}) as List<dynamic>;

  Future<List<dynamic>> removeLibraryFolder(String path) async =>
      await _send('POST', 'api/library/folders/remove', {'path': path}) as List<dynamic>;

  Future<List<dynamic>> addPackageFile(String path, {String origin = 'Local'}) async =>
      await _send('POST', 'api/library/files/add', {'path': path, 'origin': origin})
          as List<dynamic>;

  Future<List<dynamic>> removePackageFile(String path) async =>
      await _send('POST', 'api/library/files/remove', {'path': path}) as List<dynamic>;

  Future<List<dynamic>> installRecords(String path) async =>
      await _send('GET', 'api/library/records?path=${Uri.encodeQueryComponent(path)}')
          as List<dynamic>;

  Future<Map<String, dynamic>> parsePackage(String path) async =>
      await _send('POST', 'api/library/parse', {'path': path}) as Map<String, dynamic>;

  Future<Map<String, dynamic>> installPackageFile(String projectPath, String packagePath,
      List<String>? selected) async =>
      await _send('POST', 'api/library/install', {
        'path': projectPath,
        'packagePath': packagePath,
        'selectedPaths': selected,
      }) as Map<String, dynamic>;

  Future<Map<String, dynamic>> uninstallPackageFile(String projectPath, String packageId) async =>
      await _send('POST', 'api/library/uninstall',
          {'path': projectPath, 'packageId': packageId}) as Map<String, dynamic>;

  // ---------- external packages ----------

  Future<List<dynamic>> externalPackages() async =>
      await _send('GET', 'api/external') as List<dynamic>;

  Future<Map<String, dynamic>> addExternalFolder(String path) async =>
      await _send('POST', 'api/external/add-folder', {'path': path}) as Map<String, dynamic>;

  Future<Map<String, dynamic>> addExternalRepository(String url, String? reference) async =>
      await _send('POST', 'api/external/add-repository',
          {'url': url, 'reference': reference}) as Map<String, dynamic>;

  Future<void> removeExternal(String id) =>
      _send('POST', 'api/external/remove', {'id': id});

  Future<Map<String, dynamic>> installExternal(String projectPath, String id) async =>
      await _send('POST', 'api/external/install',
          {'path': projectPath, 'id': id}) as Map<String, dynamic>;

  // ---------- registry sources ----------

  /// Every repository endpoint answers with the whole `{urls, known}` object, so the
  /// caller gets the list back out of it rather than a cast that would throw.
  List<dynamic> _urlsOf(Object? result) =>
      ((result as Map)['urls'] as List?) ?? const [];

  Future<List<dynamic>> addRegistrySource(String url) async =>
      _urlsOf(await _send('POST', 'api/repositories/add', {'url': url}));

  Future<List<dynamic>> removeRegistrySource(String url) async =>
      _urlsOf(await _send('POST', 'api/repositories/remove', {'url': url}));

  Future<List<dynamic>> orderRegistrySources(List<String> urls) async =>
      _urlsOf(await _send('POST', 'api/repositories/order', {'urls': urls}));

  Future<Map<String, dynamic>> importVccRepositories() async =>
      await _send('POST', 'api/repositories/import-vcc') as Map<String, dynamic>;

  Future<String> resolveRepositoryName(String url) async {
    final result = await _send('POST', 'api/repositories/resolve', {'url': url});
    return (result as Map)['name'] as String? ?? '';
  }

  // ---------- booth ----------

  Future<Map<String, dynamic>> boothScripts() async =>
      await _send('GET', 'api/booth/scripts') as Map<String, dynamic>;

  Future<String> boothUrl(String kind, int page) async {
    final result = await _send('GET', 'api/booth/url?kind=$kind&page=$page');
    return (result as Map)['url'] as String;
  }

  Future<Map<String, dynamic>> boothParse(String kind, String json) async =>
      await _send('POST', 'api/booth/parse', {'kind': kind, 'json': json})
          as Map<String, dynamic>;

  Future<Map<String, dynamic>> boothLibrary() async =>
      await _send('GET', 'api/booth/library') as Map<String, dynamic>;

  Future<Map<String, dynamic>> boothSaveLibrary(List<dynamic> items) async =>
      await _send('POST', 'api/booth/library', {'items': items}) as Map<String, dynamic>;

  Future<String> boothDownload(String number) async {
    final result = await _send('POST', 'api/booth/download', {'number': number});
    return (result as Map)['path'] as String;
  }

  Future<Map<String, dynamic>> boothInspect(String number) async =>
      await _send('POST', 'api/booth/inspect', {'number': number}) as Map<String, dynamic>;

  // ---------- asset store ----------

  Future<Map<String, dynamic>> assetStoreScripts() async =>
      await _send('GET', 'api/assetstore/scripts') as Map<String, dynamic>;

  Future<String> assetStoreSearchUrl(String term) async {
    final result =
        await _send('GET', 'api/assetstore/search-url?term=${Uri.encodeQueryComponent(term)}');
    return (result as Map)['url'] as String;
  }

  Future<dynamic> assetStoreParse(String kind, String json) =>
      _send('POST', 'api/assetstore/parse', {'kind': kind, 'json': json});

  Future<List<dynamic>> assetStoreCache() async =>
      await _send('GET', 'api/assetstore/cache') as List<dynamic>;

  Future<Map<String, dynamic>> assetStoreAccountCache() async =>
      await _send('GET', 'api/assetstore/account-cache') as Map<String, dynamic>;

  Future<dynamic> assetStoreMerge(List<dynamic> items) =>
      _send('POST', 'api/assetstore/merge', {'items': items});

  Future<dynamic> assetStoreSaveAccountCache(List<dynamic> items) =>
      _send('POST', 'api/assetstore/account-cache', {'items': items});

  Future<String?> assetStoreCover(String name) async {
    try {
      final result = await _send(
          'GET', 'api/assetstore/cover?name=${Uri.encodeQueryComponent(name)}');
      return (result as Map)['url'] as String?;
    } on HostException {
      return null;
    }
  }

  Future<String> assetStoreDownload(String name, String? productId) async {
    final result = await _send('POST', 'api/assetstore/download',
        {'name': name, 'productId': productId});
    return (result as Map)['path'] as String;
  }

  Future<void> cacheAssetStoreCover(String name, String url) =>
      _send('POST', 'api/assetstore/cover', {'name': name, 'url': url});

  /// Local path of a thumbnail pulled out of the package itself, for rows the store never
  /// gave a cover to. Null when the package holds no usable image.
  Future<String?> assetStoreThumbnail(String path) async {
    try {
      final result = await _send(
          'POST', 'api/assetstore/thumbnail', {'path': path});
      return (result as Map)['path'] as String?;
    } on HostException {
      return null;
    }
  }

  // ---------- vrchat ----------

  Future<Map<String, dynamic>?> vrchatSelf() async {
    try {
      return await _send('GET', 'api/vrchat/self') as Map<String, dynamic>;
    } on HostException {
      return null;
    }
  }

  Future<List<dynamic>> vrchatContent() async {
    try {
      return await _send('GET', 'api/vrchat/content') as List<dynamic>;
    } on HostException {
      return const [];
    }
  }

  Future<dynamic> vrchatUpdateContent(Map<String, dynamic> edit) =>
      _send('POST', 'api/vrchat/content/update', edit);

  Future<dynamic> vrchatDeleteContent(String id, String kind) =>
      _send('POST', 'api/vrchat/content/delete', {'id': id, 'kind': kind});

  Future<dynamic> vrchatPublish(String id) =>
      _send('POST', 'api/vrchat/content/publish', {'id': id});

  Future<dynamic> vrchatUnpublish(String id) =>
      _send('POST', 'api/vrchat/content/unpublish', {'id': id});

  Future<Map<String, dynamic>?> vrchatSelectAvatar(String id, bool fallback) async =>
      await _send('POST', 'api/vrchat/avatar/select',
          {'id': id, 'fallback': fallback}) as Map<String, dynamic>?;

  Future<List<dynamic>> vrchatStyles() async =>
      await _send('GET', 'api/vrchat/styles') as List<dynamic>;

  Future<dynamic> vrchatSaveContentCache(List<dynamic> items) =>
      _send('POST', 'api/vrchat/content-cache', {'items': items});

  Future<Map<String, dynamic>> vrchatContentCache() async =>
      await _send('GET', 'api/vrchat/content-cache') as Map<String, dynamic>;

  Future<Map<String, dynamic>> vrchatLocalMap() async =>
      await _send('POST', 'api/vrchat/local-map') as Map<String, dynamic>;

  // ---------- 备份还原与版本托管 ----------

  Future<Map<String, dynamic>> backups() async =>
      await _send('GET', 'api/backups') as Map<String, dynamic>;

  Future<Map<String, dynamic>> backupCounts() async {
    final result = await _send('GET', 'api/backups/counts');
    return (((result as Map)['counts'] as Map?) ?? const {}).cast<String, dynamic>();
  }

  Future<String> restoreBackup(
      String filePath, String targetPath, bool overwrite) async {
    final result = await _send(
      'POST',
      'api/backups/restore',
      {'filePath': filePath, 'targetPath': targetPath, 'overwrite': overwrite},
      const Duration(minutes: 30),
    );
    return (result as Map)['message'] as String? ?? '还原完成';
  }

  Future<Map<String, dynamic>> vcsTools({bool refresh = true}) async =>
      await _send('GET', 'api/vcs/tools?refresh=$refresh') as Map<String, dynamic>;

  /// Runs winget, which can raise a UAC prompt the user has to answer.
  Future<Map<String, dynamic>> vcsInstall(String tool) async =>
      await _send('POST', 'api/vcs/install', {'tool': tool},
          const Duration(minutes: 20)) as Map<String, dynamic>;

  Future<List<dynamic>> vcsStates(List<String> paths, {bool deep = false}) async =>
      await _send('POST', 'api/vcs/states', {'paths': paths, 'deep': deep},
          const Duration(minutes: 5)) as List<dynamic>;

  Future<Map<String, dynamic>> vcsState(String path, {bool deep = false}) async =>
      await _send(
          'GET',
          'api/vcs/state?path=${Uri.encodeQueryComponent(path)}&deep=$deep',
          null,
          const Duration(minutes: 5)) as Map<String, dynamic>;

  Future<Map<String, dynamic>> vcsRun(
    String action,
    String path, {
    String? kind,
    String? message,
    String? url,
    bool push = false,
  }) async =>
      await _send(
        'POST',
        'api/vcs/run',
        {
          'action': action,
          'path': path,
          'kind': kind,
          'message': message,
          'url': url,
          'push': push,
        },
        const Duration(minutes: 30),
      ) as Map<String, dynamic>;

  // ---------- github ----------

  /// Who gh is signed in as. Never carries the token itself.
  Future<Map<String, dynamic>> githubStatus() async =>
      await _send('GET', 'api/github/status', null,
          const Duration(seconds: 90)) as Map<String, dynamic>;

  Future<Map<String, dynamic>> syncStatus() async =>
      await _send('GET', 'api/sync/status', null,
          const Duration(seconds: 90)) as Map<String, dynamic>;

  /// Shells out to gh and waits on GitHub, so this is a deliberate multi-minute call.
  Future<Map<String, dynamic>> syncPush() async =>
      await _send('POST', 'api/sync/push', {},
          const Duration(minutes: 5)) as Map<String, dynamic>;

  /// No gist id in either direction: the sidecar finds the account's own backup, so the page never
  /// has to show or carry an address that could be opened outside it.
  Future<Map<String, dynamic>> syncPull() async =>
      await _send('POST', 'api/sync/pull', {},
          const Duration(minutes: 5)) as Map<String, dynamic>;

  /// The OAuth App that signs people in ships inside the sidecar, so the only GitHub login call is
  /// this one: it opens the user's own browser and waits for them to click Authorize once.
  Future<Map<String, dynamic>> githubLogin() async =>
      await _send('POST', 'api/github/login', {},
          const Duration(minutes: 6)) as Map<String, dynamic>;
}
