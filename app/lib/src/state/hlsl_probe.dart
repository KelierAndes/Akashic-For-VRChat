import 'dart:async';
import 'dart:convert';

class HlslProbe {
  HlslProbe({
    required this.evaluate,
    required this.onChange,
    this.onEnabled,
    bool Function()? isPaused,
    this.recheckDelay = const Duration(milliseconds: 1500),
    this.pollInterval = const Duration(milliseconds: 900),
  }) : isPaused = isPaused ?? (() => false);

  static const setSource = 'window.akashicSetHlsl';
  static const stateSource =
      'window.akashicHlslState ? JSON.stringify(window.akashicHlslState()) : ""';
  static const infoSource =
      'window.akashicHlslInfo ? JSON.stringify(window.akashicHlslInfo()) : ""';

  final Future<String?> Function(String source) evaluate;
  final void Function() onChange;
  final Future<void> Function()? onEnabled;
  final bool Function() isPaused;
  final Duration recheckDelay;
  final Duration pollInterval;

  bool enabled = false;
  Map<String, dynamic>? info;

  int _generation = 0;
  Timer? _poll;
  bool _disposed = false;

  Future<void> toggle() async {
    final generation = _generation;
    final next = !enabled;

    enabled = next;
    if (next) info = null;
    onChange();

    await _evaluate('$setSource && $setSource($next)');
    if (_stale(generation)) return;

    if (!next) {
      _stop();
      return;
    }

    await Future<void>.delayed(recheckDelay);
    if (_stale(generation)) return;

    final state = await _evaluate(stateSource);
    if (_stale(generation)) return;

    if ('$state'.contains('"enabled":false')) {
      enabled = false;
      onChange();
      return;
    }

    await refresh();
    if (_stale(generation)) return;

    _start();
    await onEnabled?.call();
  }

  Future<void> refresh() async {
    if (isPaused()) return;
    final decoded = await _evaluate(infoSource);
    if (decoded == null || decoded.isEmpty || decoded == 'null') return;

    final Object? value;
    try {
      value = jsonDecode(decoded);
    } on FormatException {
      return;
    }
    if (value is! Map) return;

    info = Map<String, dynamic>.from(value);
    onChange();
  }

  void start() => _start();

  void reset() {
    _generation++;
    _stop();
    enabled = false;
    info = null;
  }

  void dispose() {
    _disposed = true;
    _generation++;
    _stop();
  }

  bool _stale(int generation) => _disposed || generation != _generation;

  Future<String?> _evaluate(String source) async {
    try {
      return await evaluate(source);
    } on Object {
      return null;
    }
  }

  void _start() {
    _poll?.cancel();
    _poll = Timer.periodic(pollInterval, (_) {
      refresh();
    });
  }

  void _stop() {
    _poll?.cancel();
    _poll = null;
  }
}
