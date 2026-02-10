import 'dart:async';
import 'dart:js_interop';

import 'package:web/web.dart' as web;

/// Bridge class for communicating with Unity WebGL via JavaScript interop.
///
/// Flutter → Unity: Uses `window.sendToUnity()` (which calls `unityInstance.SendMessage`)
/// Unity → Flutter: Unity's jslib calls `window.FirsthabitBridge.onXxx()`,
///                   which we intercept here via dart:js_interop.
class UnityBridge {
  static const String _gameObject = 'FirsthabitWebGLBridge';

  // Stream controllers for Unity → Flutter callbacks
  final _onBridgeReady = StreamController<void>.broadcast();
  final _onPrepared = StreamController<String>.broadcast();
  final _onPrepareFailed = StreamController<({String id, String error})>.broadcast();
  final _onPlaybackStarted = StreamController<String>.broadcast();
  final _onPlaybackCompleted = StreamController<String>.broadcast();
  final _onSentenceStarted = StreamController<String>.broadcast();
  final _onSentenceEnded = StreamController<String>.broadcast();
  final _onSubtitleStarted = StreamController<String>.broadcast();
  final _onSubtitleEnded = StreamController<String>.broadcast();
  final _onRequestSent = StreamController<String>.broadcast();
  final _onResponseReceived = StreamController<String>.broadcast();
  final _onVolumeChanged = StreamController<double>.broadcast();
  final _onError = StreamController<({String method, String message})>.broadcast();

  // Public streams
  Stream<void> get onBridgeReady => _onBridgeReady.stream;
  Stream<String> get onPrepared => _onPrepared.stream;
  Stream<({String id, String error})> get onPrepareFailed => _onPrepareFailed.stream;
  Stream<String> get onPlaybackStarted => _onPlaybackStarted.stream;
  Stream<String> get onPlaybackCompleted => _onPlaybackCompleted.stream;
  Stream<String> get onSentenceStarted => _onSentenceStarted.stream;
  Stream<String> get onSentenceEnded => _onSentenceEnded.stream;
  Stream<String> get onSubtitleStarted => _onSubtitleStarted.stream;
  Stream<String> get onSubtitleEnded => _onSubtitleEnded.stream;
  Stream<String> get onRequestSent => _onRequestSent.stream;
  Stream<String> get onResponseReceived => _onResponseReceived.stream;
  Stream<double> get onVolumeChanged => _onVolumeChanged.stream;
  Stream<({String method, String message})> get onError => _onError.stream;

  UnityBridge() {
    _registerCallbacks();
  }

  /// Register Dart callbacks on window.FirsthabitBridge so Unity's jslib can call them.
  void _registerCallbacks() {
    final bridge = _FirsthabitBridgeJS(
      onBridgeReady: (() {
        _onBridgeReady.add(null);
      }).toJS,
      onPrepared: ((JSString id) {
        _onPrepared.add(id.toDart);
      }).toJS,
      onPrepareFailed: ((JSString id, JSString error) {
        _onPrepareFailed.add((id: id.toDart, error: error.toDart));
      }).toJS,
      onPlaybackStarted: ((JSString id) {
        _onPlaybackStarted.add(id.toDart);
      }).toJS,
      onPlaybackCompleted: ((JSString id) {
        _onPlaybackCompleted.add(id.toDart);
      }).toJS,
      onSentenceStarted: ((JSString text) {
        _onSentenceStarted.add(text.toDart);
      }).toJS,
      onSentenceEnded: ((JSString text) {
        _onSentenceEnded.add(text.toDart);
      }).toJS,
      onSubtitleStarted: ((JSString text) {
        _onSubtitleStarted.add(text.toDart);
      }).toJS,
      onSubtitleEnded: ((JSString text) {
        _onSubtitleEnded.add(text.toDart);
      }).toJS,
      onRequestSent: ((JSString id) {
        _onRequestSent.add(id.toDart);
      }).toJS,
      onResponseReceived: ((JSString id) {
        _onResponseReceived.add(id.toDart);
      }).toJS,
      onVolumeChanged: ((JSNumber volume) {
        _onVolumeChanged.add(volume.toDartDouble);
      }).toJS,
      onError: ((JSString method, JSString message) {
        _onError.add((method: method.toDart, message: message.toDart));
      }).toJS,
    );

    // Set window.FirsthabitBridge = bridge
    _setFirsthabitBridge(bridge);
  }

  // -------------------------------------------------------
  // Flutter → Unity methods
  // -------------------------------------------------------

  /// Prepare audio: sends base64-encoded audio to Unity for local storage.
  /// [base64Audio] is the base64-encoded audio bytes.
  /// [format] is one of: wav, mp3, m4a, ogg.
  void prepareAudio(String base64Audio, String format) {
    _pendingAudioBase64 = base64Audio.toJS;
    final json = '{"format":"${_escapeJson(format)}"}';
    _sendToUnity('PrepareAudio', json);
  }

  /// Play a previously prepared cache entry by cacheId.
  void play(String cacheId, {bool playAudio = true}) {
    final json = '{"cacheId":"${_escapeJson(cacheId)}","playAudio":$playAudio}';
    _sendToUnity('Play', json);
  }

  void stop() {
    _sendToUnity('Stop', '');
  }

  void setVolume(double volume) {
    _sendToUnity('SetVolume', volume.toStringAsFixed(2));
  }

  void clearAllCache() {
    _sendToUnity('ClearAllCache', '');
  }

  void changeAvatar(String key) {
    _sendToUnity('ChangeAvatar', key);
  }

  /// Set camera background color.
  /// [colorString] is "transparent" or hex like "#FF0000".
  void setBackgroundColor(String colorString) {
    _sendToUnity('SetBackgroundColor', colorString);
    _jsSetTransparentDebug(
      (colorString.toLowerCase() == 'transparent').toJS,
    );
  }

  // -------------------------------------------------------
  // Internal helpers
  // -------------------------------------------------------

  void _sendToUnity(String method, String param) {
    _jsSendToUnity(_gameObject.toJS, method.toJS, param.toJS);
  }

  String _escapeJson(String value) {
    return value
        .replaceAll('\\', '\\\\')
        .replaceAll('"', '\\"')
        .replaceAll('\n', '\\n')
        .replaceAll('\r', '\\r')
        .replaceAll('\t', '\\t');
  }

  void dispose() {
    _onBridgeReady.close();
    _onPrepared.close();
    _onPrepareFailed.close();
    _onPlaybackStarted.close();
    _onPlaybackCompleted.close();
    _onSentenceStarted.close();
    _onSentenceEnded.close();
    _onSubtitleStarted.close();
    _onSubtitleEnded.close();
    _onRequestSent.close();
    _onResponseReceived.close();
    _onVolumeChanged.close();
    _onError.close();
  }
}

// -------------------------------------------------------
// JS Interop bindings
// -------------------------------------------------------

/// Call window.sendToUnity(gameObject, method, param)
@JS('sendToUnity')
external void _jsSendToUnity(JSString gameObject, JSString method, JSString param);

/// Toggle checkerboard debug background in Unity iframe
@JS('setTransparentDebug')
external void _jsSetTransparentDebug(JSBoolean on);

/// Set window.FirsthabitBridge = bridge
@JS('FirsthabitBridge')
external set _firsthabitBridge(_FirsthabitBridgeJS bridge);

void _setFirsthabitBridge(_FirsthabitBridgeJS bridge) {
  _firsthabitBridge = bridge;
}

/// Set window._pendingAudioBase64 for Unity to read via jslib
@JS('_pendingAudioBase64')
external set _pendingAudioBase64(JSString value);

/// JavaScript object shape for window.FirsthabitBridge
extension type _FirsthabitBridgeJS._(JSObject _) implements JSObject {
  external factory _FirsthabitBridgeJS({
    JSFunction onBridgeReady,
    JSFunction onPrepared,
    JSFunction onPrepareFailed,
    JSFunction onPlaybackStarted,
    JSFunction onPlaybackCompleted,
    JSFunction onSentenceStarted,
    JSFunction onSentenceEnded,
    JSFunction onSubtitleStarted,
    JSFunction onSubtitleEnded,
    JSFunction onRequestSent,
    JSFunction onResponseReceived,
    JSFunction onVolumeChanged,
    JSFunction onError,
  });
}
