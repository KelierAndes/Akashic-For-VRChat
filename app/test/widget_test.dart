import 'package:akashic_ui/src/app_nav.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('navigation covers every ported destination', () {
    expect(AppNav.entries.map((e) => e.id), containsAll(<String>[
      'projects',
      'resources',
      'packages',
      'import',
      'booth',
      'assetstore',
      'vrchatsync',
      'templates',
      'settings',
      'about',
    ]));
    expect(AppNav.railCount, lessThanOrEqualTo(AppNav.entries.length));
    expect(AppNav.settingsIndex, greaterThan(-1));
  });
}
