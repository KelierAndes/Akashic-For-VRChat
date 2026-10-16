/// Mirrors of the records Akashic.Host serves. Field names are the C# ones in
/// camelCase, so a rename on either side has to be done in both places.
library;

/// Who supplied a .unitypackage. The sidecar sends the enum name, which is not what
/// the interface says, so every list funnels it through here.
String originLabel(String code) => switch (code) {
      'Local' => '本地',
      'Booth' => 'Booth',
      'AssetStore' => 'Asset Store',
      _ => code,
    };

const Map<String, String> originCodes = {
  '本地': 'Local',
  'Booth': 'Booth',
  'Asset Store': 'AssetStore',
};

class HostSession {
  const HostSession({required this.signedIn, this.directory});

  final bool signedIn;
  final String? directory;

  static HostSession fromJson(Map<String, dynamic> json) => HostSession(
        signedIn: json['signedIn'] as bool? ?? false,
        directory: json['directory'] as String?,
      );
}

class UnityEditorEntry {
  const UnityEditorEntry({
    required this.version,
    required this.path,
    required this.source,
  });

  final String version;
  final String path;
  final String source;

  static UnityEditorEntry fromJson(Map<String, dynamic> json) => UnityEditorEntry(
        version: json['version'] as String? ?? '',
        path: json['path'] as String? ?? '',
        source: json['source'] as String? ?? '',
      );
}

class InstalledPackage {
  const InstalledPackage({
    required this.name,
    required this.version,
    required this.displayName,
    required this.source,
    required this.isLocked,
  });

  final String name;
  final String version;
  final String displayName;
  final String source;
  final bool isLocked;

  static InstalledPackage fromJson(Map<String, dynamic> json) => InstalledPackage(
        name: json['name'] as String? ?? '',
        version: json['version'] as String? ?? '',
        displayName: json['displayName'] as String? ?? '',
        source: json['source'] as String? ?? 'Unknown',
        isLocked: json['isLocked'] as bool? ?? false,
      );
}

class UnityProject {
  UnityProject({
    required this.name,
    required this.path,
    required this.unityVersion,
    required this.hasVpmManifest,
    required this.installedPackages,
    required this.lastModified,
    required this.isWorldProject,
    required this.isAvatarProject,
    required this.isVrcProject,
    required this.projectTypeDisplayName,
  });

  final String name;
  final String path;
  final String unityVersion;
  final bool hasVpmManifest;
  final List<InstalledPackage> installedPackages;
  final DateTime lastModified;
  final bool isWorldProject;
  final bool isAvatarProject;
  final bool isVrcProject;
  final String projectTypeDisplayName;

  /// Filled in by the background cloud match after the list is already on screen.
  List<String> cloudBlueprintIds = [];

  bool get hasCloudLink => cloudBlueprintIds.isNotEmpty;

  /// Says what was found, not where the button goes. Spelled "VRC" rather than
  /// "VRChat" because the card tile is 300 wide and the full name pushed the count
  /// off the end of the pill.
  String get cloudLinkLabel => cloudBlueprintIds.length == 1
      ? 'VRC 已上传'
      : 'VRC 已上传（${cloudBlueprintIds.length}）';

  String get cloudLinkTip => cloudBlueprintIds.isEmpty
      ? ''
      : '云端匹配到 ${cloudBlueprintIds.length} 项：${cloudBlueprintIds.join(' · ')}';

  String get lastModifiedDisplay {
    final local = lastModified.toLocal();
    String two(int v) => v.toString().padLeft(2, '0');
    return '${local.year}/${two(local.month)}/${two(local.day)} '
        '${two(local.hour)}:${two(local.minute)}';
  }

  static UnityProject fromJson(Map<String, dynamic> json) => UnityProject(
        name: json['name'] as String? ?? '',
        path: json['path'] as String? ?? '',
        unityVersion: json['unityVersion'] as String? ?? '',
        hasVpmManifest: json['hasVpmManifest'] as bool? ?? false,
        installedPackages: ((json['installedPackages'] as List?) ?? const [])
            .cast<Map<String, dynamic>>()
            .map(InstalledPackage.fromJson)
            .toList(),
        lastModified:
            DateTime.tryParse(json['lastModified'] as String? ?? '') ?? DateTime(1970),
        isWorldProject: json['isWorldProject'] as bool? ?? false,
        isAvatarProject: json['isAvatarProject'] as bool? ?? false,
        isVrcProject: json['isVrcProject'] as bool? ?? false,
        projectTypeDisplayName: json['projectTypeDisplayName'] as String? ?? 'Unity',
      );
}

class HostState {
  HostState({
    required this.projectCount,
    required this.defaultProjectPath,
    required this.backupDirectory,
    required this.backupExcludesPackages,
    required this.editors,
    required this.registrySources,
    required this.booth,
    required this.assetStore,
    required this.vrchat,
    required this.assetStoreDownloadDirectory,
    required this.darkTheme,
    required this.sheetOpacity,
    required this.rail,
  });

  final int projectCount;
  final String defaultProjectPath;
  final String backupDirectory;
  final bool backupExcludesPackages;
  final List<UnityEditorEntry> editors;
  final List<String> registrySources;
  final HostSession booth;
  final HostSession assetStore;
  final HostSession vrchat;
  final String assetStoreDownloadDirectory;
  final bool darkTheme;

  /// Alpha of the glass surfaces (window sheet, rail trays, popups), so the
  /// window looks the same on every start.
  final double sheetOpacity;

  /// The icon rail's tiles, in order. Empty means the built-in set.
  final List<String> rail;

  static HostState fromJson(Map<String, dynamic> json) => HostState(
        projectCount: json['projectCount'] as int? ?? 0,
        defaultProjectPath: json['defaultProjectPath'] as String? ?? '',
        backupDirectory: json['backupDirectory'] as String? ?? '',
        backupExcludesPackages: json['backupExcludesPackages'] as bool? ?? false,
        editors: ((json['editors'] as List?) ?? const [])
            .cast<Map<String, dynamic>>()
            .map(UnityEditorEntry.fromJson)
            .toList(),
        registrySources: ((json['registrySources'] as List?) ?? const [])
            .cast<String>(),
        booth: HostSession.fromJson(
            (json['booth'] as Map<String, dynamic>?) ?? const {}),
        assetStore: HostSession.fromJson(
            (json['assetStore'] as Map<String, dynamic>?) ?? const {}),
        // The sidecar's camelCase of `VRChat` keeps the capital C: "vrChat".
        vrchat: HostSession.fromJson(
            (json['vrChat'] as Map<String, dynamic>?) ?? const {}),
        assetStoreDownloadDirectory:
            json['assetStoreDownloadDirectory'] as String? ?? '',
        darkTheme: json['darkTheme'] as bool? ?? true,
        sheetOpacity: (json['sheetOpacity'] as num?)?.toDouble() ?? 0.9,
        rail: ((json['rail'] as List?) ?? const []).cast<String>(),
      );
}

/// One cookie as it moves between the sidecar and the browser. Domain and path are
/// load-bearing: Booth hands out the same name on `booth.pm` and `accounts.booth.pm`
/// with different values, and Unity's session token lives on `.unity.com`, so a
/// flattened name-to-value view loses one of them.
class CookieSpec {
  const CookieSpec({
    required this.name,
    required this.value,
    required this.domain,
    this.path = '/',
  });

  final String name;
  final String value;
  final String domain;
  final String path;

  factory CookieSpec.fromJson(Map<String, dynamic> json) => CookieSpec(
        name: json['name'] as String? ?? '',
        value: json['value'] as String? ?? '',
        domain: json['domain'] as String? ?? '',
        path: json['path'] as String? ?? '/',
      );

  Map<String, dynamic> toJson() => {
        'name': name,
        'value': value,
        'domain': domain,
        'path': path,
      };
}

class LaunchResult {
  const LaunchResult(this.success, this.message, this.editorPath);

  final bool success;
  final String message;
  final String? editorPath;

  static LaunchResult fromJson(Map<String, dynamic> json) => LaunchResult(
        json['success'] as bool? ?? false,
        json['message'] as String? ?? '',
        json['editorPath'] as String?,
      );
}

class DetectResult {
  const DetectResult(this.added, this.already, this.message);

  final int added;
  final int already;
  final String message;

  static DetectResult fromJson(Map<String, dynamic> json) => DetectResult(
        json['added'] as int? ?? 0,
        json['already'] as int? ?? 0,
        json['message'] as String? ?? '',
      );
}

/// A template row with every display string already resolved by the sidecar,
/// because those rules live on the C# model.
class TemplateRow {
  const TemplateRow({
    required this.name,
    required this.kind,
    required this.unityVersion,
    required this.notesDisplay,
    required this.directory,
    required this.sourceDisplay,
    required this.createdAtDisplay,
    required this.kindLabel,
    required this.isBuiltIn,
    required this.canDelete,
    required this.isAvailable,
  });

  final String name;
  final String kind;
  final String unityVersion;
  final String notesDisplay;
  final String directory;
  final String sourceDisplay;
  final String createdAtDisplay;
  final String kindLabel;
  final bool isBuiltIn;
  final bool canDelete;
  final bool isAvailable;

  static TemplateRow fromJson(Map<String, dynamic> json) => TemplateRow(
        name: json['name'] as String? ?? '',
        kind: json['kind'] as String? ?? '',
        unityVersion: json['unityVersion'] as String? ?? '',
        notesDisplay: json['notesDisplay'] as String? ?? '',
        directory: json['directory'] as String? ?? '',
        sourceDisplay: json['sourceDisplay'] as String? ?? '',
        createdAtDisplay: json['createdAtDisplay'] as String? ?? '',
        kindLabel: json['kindLabel'] as String? ?? '',
        isBuiltIn: json['isBuiltIn'] as bool? ?? false,
        canDelete: json['canDelete'] as bool? ?? false,
        isAvailable: json['isAvailable'] as bool? ?? false,
      );
}

class CreationVersion {
  const CreationVersion(this.version, this.isBuiltIn, this.templates);

  final String version;
  final bool isBuiltIn;
  final List<TemplateRow> templates;

  static CreationVersion fromJson(Map<String, dynamic> json) => CreationVersion(
        json['version'] as String? ?? '',
        json['isBuiltIn'] as bool? ?? false,
        ((json['templates'] as List?) ?? const [])
            .cast<Map<String, dynamic>>()
            .map(TemplateRow.fromJson)
            .toList(),
      );
}

class CreationInput {
  const CreationInput(this.versions, this.defaultIndex, this.defaultProjectPath);

  final List<CreationVersion> versions;
  final int defaultIndex;
  final String defaultProjectPath;

  static CreationInput fromJson(Map<String, dynamic> json) => CreationInput(
        ((json['versions'] as List?) ?? const [])
            .cast<Map<String, dynamic>>()
            .map(CreationVersion.fromJson)
            .toList(),
        json['defaultIndex'] as int? ?? 0,
        json['defaultProjectPath'] as String? ?? '',
      );
}
