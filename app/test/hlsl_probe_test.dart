import 'package:flutter_test/flutter_test.dart';

import 'package:akashic_ui/src/state/hlsl_probe.dart';

class _Harness {
  _Harness() {
    probe = HlslProbe(
      evaluate: (source) async {
        calls.add(source);
        if (source.contains('akashicHlslState')) return stateResult;
        if (source.contains('akashicHlslInfo')) return infoResult;
        return null;
      },
      isPaused: () => paused,
      onChange: () => changes++,
      onEnabled: () async => enabledCallbacks++,
    );
  }

  late final HlslProbe probe;
  final List<String> calls = [];
  int changes = 0;
  int enabledCallbacks = 0;
  bool paused = false;
  String? stateResult = '{"enabled":true,"busy":false,"mode":"textured"}';
  String? infoResult = '{"draws":"18","mode":"textured"}';

  int get setCalls => calls.where((source) => source.contains('akashicSetHlsl')).length;
  int get stateCalls => calls.where((source) => source.contains('akashicHlslState')).length;
  int get infoCalls => calls.where((source) => source.contains('akashicHlslInfo')).length;

  void close() => probe.dispose();
}

Future<void> advance(WidgetTester tester, Duration delta) async {
  await tester.pump(delta);
  await tester.pump();
  await tester.pump();
}

void main() {
  testWidgets('开启后 1.5 秒才复查状态，复查通过才起 900 毫秒轮询', (tester) async {
    final box = _Harness();

    final pending = box.probe.toggle();
    await advance(tester, Duration.zero);
    expect(box.setCalls, 1);
    expect(box.stateCalls, 0);
    expect(box.probe.enabled, isTrue);

    await advance(tester, const Duration(milliseconds: 1499));
    expect(box.stateCalls, 0);

    await advance(tester, const Duration(milliseconds: 1));
    expect(box.stateCalls, 1);
    expect(box.infoCalls, 1);
    await pending;
    expect(box.enabledCallbacks, 1);

    final before = box.infoCalls;
    await advance(tester, const Duration(milliseconds: 900));
    expect(box.infoCalls, before + 1);
    await advance(tester, const Duration(milliseconds: 900));
    expect(box.infoCalls, before + 2);

    box.close();
  });

  testWidgets('复查发现没开成，就退回近似且不起轮询', (tester) async {
    final box = _Harness()..stateResult = '{"enabled":false,"busy":false,"mode":"textured"}';

    final pending = box.probe.toggle();
    await advance(tester, const Duration(seconds: 2));
    await pending;

    expect(box.probe.enabled, isFalse);
    expect(box.stateCalls, 1);
    expect(box.infoCalls, 0);

    final before = box.calls.length;
    await advance(tester, const Duration(seconds: 3));
    expect(box.calls.length, before);

    box.close();
  });

  testWidgets('关闭时立刻停轮询，不再发信息请求', (tester) async {
    final box = _Harness();

    var pending = box.probe.toggle();
    await advance(tester, const Duration(milliseconds: 1600));
    await pending;
    expect(box.probe.enabled, isTrue);

    pending = box.probe.toggle();
    await advance(tester, Duration.zero);
    await pending;
    expect(box.setCalls, 2);
    expect(box.probe.enabled, isFalse);

    final before = box.infoCalls;
    await advance(tester, const Duration(seconds: 3));
    expect(box.infoCalls, before);

    box.close();
  });

  testWidgets('选择器挡住视图时轮询静默，恢复后继续', (tester) async {
    final box = _Harness();

    final pending = box.probe.toggle();
    await advance(tester, const Duration(milliseconds: 1600));
    await pending;

    box.paused = true;
    final hidden = box.infoCalls;
    await advance(tester, const Duration(milliseconds: 2700));
    expect(box.infoCalls, hidden);

    box.paused = false;
    await advance(tester, const Duration(milliseconds: 900));
    expect(box.infoCalls, hidden + 1);

    box.close();
  });

  testWidgets('信息读不到或不是对象时保留上一次结果', (tester) async {
    final box = _Harness();

    final pending = box.probe.toggle();
    await advance(tester, const Duration(milliseconds: 1600));
    await pending;
    expect(box.probe.info?['draws'], '18');
    final changes = box.changes;

    for (final junk in <String?>['', 'null', 'not json', '[1,2]']) {
      box.infoResult = junk;
      await advance(tester, const Duration(milliseconds: 900));
      expect(box.probe.info?['draws'], '18');
    }
    expect(box.changes, changes);

    box.close();
  });

  testWidgets('换内容时 reset 会作废在途的复查，不会给新会话起轮询', (tester) async {
    final box = _Harness();

    final pending = box.probe.toggle();
    await advance(tester, const Duration(milliseconds: 500));

    box.probe.reset();
    await advance(tester, const Duration(seconds: 3));
    await pending;

    expect(box.probe.enabled, isFalse);
    expect(box.probe.info, isNull);
    expect(box.stateCalls, 0);
    expect(box.infoCalls, 0);
    expect(box.enabledCallbacks, 0);

    box.close();
  });

  testWidgets('销毁之后在途的复查同样不产生请求', (tester) async {
    final box = _Harness();

    final pending = box.probe.toggle();
    await advance(tester, const Duration(milliseconds: 500));
    box.probe.dispose();
    await advance(tester, const Duration(seconds: 3));
    await pending;

    expect(box.stateCalls, 0);
    expect(box.infoCalls, 0);
  });

  testWidgets('视窗一创建就开始取信息，即使还没开 HLSL', (tester) async {
    final box = _Harness();

    box.probe.start();
    await advance(tester, const Duration(milliseconds: 900));
    expect(box.infoCalls, 1);
    await advance(tester, const Duration(milliseconds: 900));
    expect(box.infoCalls, 2);

    box.close();
  });
}
