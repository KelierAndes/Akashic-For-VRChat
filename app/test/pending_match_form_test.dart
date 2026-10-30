import 'package:akashic_ui/src/pages/resource_tabs.dart';
import 'package:akashic_ui/src/shell/app_scope.dart';
import 'package:akashic_ui/src/state/app_model.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('pending matches ask for a manual pick and report the choice',
      (tester) async {
    final model = AppModel();
    Map<String, dynamic>? chosen;
    Map<String, dynamic>? chosenEntry;

    final entry = <String, dynamic>{
      'metaKey': 'shinanover',
      'name': 'Shinano_ver1.02',
      'candidates': <Map<String, dynamic>>[
        {'id': '1001', 'title': 'しなの専用 ロリ化Prefab', 'shop': 'すとあ'},
        {'id': '1002', 'title': 'Shinano Hair', 'shop': 'ヘア屋'},
        {'id': '1003', 'title': 'しなの用 靴', 'shop': ''},
      ],
    };

    await tester.pumpWidget(
      AppScope(
        model: model,
        child: MaterialApp(
          home: Scaffold(
            body: PendingMatchForm(
              items: [entry],
              onChoose: (source, candidate) async {
                chosenEntry = source;
                chosen = candidate;
              },
              onSkip: (_) async {},
            ),
          ),
        ),
      ),
    );

    expect(find.text('确认 Booth 商品'), findsOneWidget);
    expect(find.text('Shinano_ver1.02'), findsOneWidget);
    expect(find.text('しなの専用 ロリ化Prefab'), findsOneWidget);
    expect(find.text('Shinano Hair'), findsOneWidget);

    await tester.tap(find.text('Shinano Hair'));
    await tester.pumpAndSettle();

    expect(chosenEntry, same(entry));
    expect(chosen?['id'], '1002');
    expect(find.text('Shinano_ver1.02'), findsNothing);
  });
}
