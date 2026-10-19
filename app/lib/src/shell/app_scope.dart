import 'package:flutter/material.dart';

import '../state/app_model.dart';

class AppScope extends InheritedNotifier<AppModel> {
  const AppScope({super.key, required AppModel model, required super.child})
      : super(notifier: model);

  static AppModel of(BuildContext context) {
    final scope = context.dependOnInheritedWidgetOfExactType<AppScope>();
    assert(scope != null, 'AppScope 尚未挂在 widget 树上');
    return scope!.notifier!;
  }
}
