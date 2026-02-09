import 'dart:js_interop';
import 'dart:ui_web' as ui_web;

import 'package:flutter/material.dart';
import 'package:web/web.dart' as web;

import 'unity_bridge.dart';

void main() {
  // Register Unity iframe as a platform view
  ui_web.platformViewRegistry.registerViewFactory('unity-webgl-iframe', (int viewId) {
    final iframe = web.document.createElement('iframe') as web.HTMLIFrameElement;
    iframe.id = 'unity-iframe';
    iframe.src = 'unity_wrapper.html';
    iframe.allow = 'autoplay';
    iframe.style.width = '100%';
    iframe.style.height = '100%';
    iframe.style.border = 'none';
    return iframe;
  });

  runApp(const MyApp());
}

class MyApp extends StatelessWidget {
  const MyApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Firsthabit Talkmotion Test',
      theme: ThemeData(
        colorSchemeSeed: const Color(0xFF1A73E8),
        useMaterial3: true,
      ),
      home: const BridgeTestPage(),
    );
  }
}

class BridgeTestPage extends StatefulWidget {
  const BridgeTestPage({super.key});

  @override
  State<BridgeTestPage> createState() => _BridgeTestPageState();
}

class _BridgeTestPageState extends State<BridgeTestPage> {
  late final UnityBridge _bridge;
  final _logs = <String>[];
  final _scrollController = ScrollController();
  final _cacheIdController = TextEditingController();

  String? _audioBase64;
  String _audioFormat = 'wav';
  String? _audioFileName;
  double _volume = 1.0;
  bool _playAudio = true;
  bool _bridgeReady = false;
  bool _unityConnected = false;

  @override
  void initState() {
    super.initState();
    _bridge = UnityBridge();
    _listenCallbacks();

    // Connect Unity iframe after a delay
    Future.delayed(const Duration(seconds: 2), () {
      _connectUnityIframe();
    });
  }

  void _connectUnityIframe() {
    _jsConnectUnityIframe();
    _addLog('>> Connecting to Unity iframe...');
  }

  void _listenCallbacks() {
    _bridge.onBridgeReady.listen((_) {
      setState(() {
        _bridgeReady = true;
        _unityConnected = true;
      });
      _addLog('BridgeReady');
    });

    _bridge.onPrepared.listen((id) {
      _cacheIdController.text = id;
      _addLog('Prepared: $id');
    });

    _bridge.onPrepareFailed.listen((e) {
      _addLog('PrepareFailed: ${e.id} - ${e.error}');
    });

    _bridge.onPlaybackStarted.listen((id) {
      _addLog('PlaybackStarted: $id');
    });

    _bridge.onPlaybackCompleted.listen((id) {
      _addLog('PlaybackCompleted: $id');
    });

    _bridge.onSentenceStarted.listen((text) {
      _addLog('SentenceStarted: $text');
    });

    _bridge.onSentenceEnded.listen((text) {
      _addLog('SentenceEnded: $text');
    });

    _bridge.onSubtitleStarted.listen((text) {
      _addLog('SubtitleStarted: $text');
    });

    _bridge.onSubtitleEnded.listen((text) {
      _addLog('SubtitleEnded: $text');
    });

    _bridge.onRequestSent.listen((id) {
      _addLog('RequestSent: $id');
    });

    _bridge.onResponseReceived.listen((id) {
      _addLog('ResponseReceived: $id');
    });

    _bridge.onVolumeChanged.listen((v) {
      _addLog('VolumeChanged: ${v.toStringAsFixed(2)}');
    });

    _bridge.onError.listen((e) {
      _addLog('ERROR [${e.method}]: ${e.message}');
    });
  }

  void _addLog(String message) {
    final timestamp = DateTime.now().toString().substring(11, 23);
    setState(() {
      _logs.add('[$timestamp] $message');
    });
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (_scrollController.hasClients) {
        _scrollController.animateTo(
          _scrollController.position.maxScrollExtent,
          duration: const Duration(milliseconds: 200),
          curve: Curves.easeOut,
        );
      }
    });
  }

  void _pickAudioFile() {
    final input = web.document.createElement('input') as web.HTMLInputElement;
    input.type = 'file';
    input.accept = '.wav,.mp3,.m4a,.ogg,audio/*';
    input.style.display = 'none';
    web.document.body!.append(input);

    input.onchange = ((web.Event event) {
      final files = input.files;
      if (files != null && files.length > 0) {
        final file = files.item(0)!;
        final fileName = file.name;
        final format = _getFormatFromName(fileName);

        final reader = web.FileReader();
        reader.onload = ((web.Event e) {
          final dataUrl = (reader.result as JSString).toDart;
          final base64 = dataUrl.split(',').last;
          setState(() {
            _audioBase64 = base64;
            _audioFormat = format;
            _audioFileName = fileName;
          });
          _addLog(
            '>> Audio loaded: $fileName ($format, ${(base64.length * 3 / 4 / 1024).toStringAsFixed(0)} KB)',
          );
          input.remove();
        }).toJS;
        reader.readAsDataURL(file);
      } else {
        input.remove();
      }
    }).toJS;

    input.click();
  }

  String _getFormatFromName(String name) {
    final ext = name.split('.').last.toLowerCase();
    return switch (ext) {
      'wav' => 'wav',
      'mp3' => 'mp3',
      'm4a' => 'm4a',
      'ogg' => 'ogg',
      _ => 'wav',
    };
  }

  @override
  void dispose() {
    _bridge.dispose();
    _scrollController.dispose();
    _cacheIdController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Row(
        children: [
          // Left: Unity WebGL
          const Expanded(
            flex: 3,
            child: HtmlElementView(viewType: 'unity-webgl-iframe'),
          ),
          // Right: Controls panel
          SizedBox(
            width: 380,
            child: _buildControlPanel(),
          ),
        ],
      ),
    );
  }

  Widget _buildControlPanel() {
    return Column(
      children: [
        // Header
        Container(
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
          color: Theme.of(context).colorScheme.primaryContainer,
          child: Row(
            children: [
              const Text(
                'Talkmotion Test',
                style: TextStyle(fontSize: 16, fontWeight: FontWeight.bold),
              ),
              const Spacer(),
              Icon(
                _bridgeReady ? Icons.check_circle : Icons.hourglass_empty,
                color: _bridgeReady ? Colors.green : Colors.orange,
                size: 20,
              ),
            ],
          ),
        ),

        // Controls
        Expanded(
          child: Padding(
            padding: const EdgeInsets.all(12),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                // Audio file picker
                OutlinedButton.icon(
                  onPressed: _pickAudioFile,
                  icon: const Icon(Icons.audio_file, size: 18),
                  label: Text(
                    _audioFileName ?? 'Pick Audio File',
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
                if (_audioFileName != null)
                  Padding(
                    padding: const EdgeInsets.only(top: 2),
                    child: Text(
                      'Format: $_audioFormat',
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  ),
                const SizedBox(height: 8),

                // Prepare
                FilledButton.icon(
                  onPressed: _audioBase64 != null
                      ? () {
                          _bridge.prepareAudio(
                            _audioBase64!,
                            _audioFormat,
                          );
                          _addLog('>> Prepare: $_audioFileName');
                        }
                      : null,
                  icon: const Icon(Icons.upload_file, size: 18),
                  label: const Text('Prepare'),
                ),
                const SizedBox(height: 6),

                // Cache ID input
                TextField(
                  controller: _cacheIdController,
                  decoration: const InputDecoration(
                    labelText: 'Cache ID',
                    hintText: 'Auto-filled on Prepare',
                    isDense: true,
                    border: OutlineInputBorder(),
                    contentPadding:
                        EdgeInsets.symmetric(horizontal: 10, vertical: 8),
                  ),
                  style: const TextStyle(fontFamily: 'monospace', fontSize: 12),
                ),
                const SizedBox(height: 6),

                // Play + Stop
                Row(
                  children: [
                    Expanded(
                      child: FilledButton.icon(
                        onPressed: () {
                          final cacheId = _cacheIdController.text.trim();
                          if (cacheId.isEmpty) {
                            _addLog('>> ERROR: No cache ID. Prepare first.');
                            return;
                          }
                          _bridge.play(cacheId, playAudio: _playAudio);
                          _addLog('>> Play: $cacheId (audio=$_playAudio)');
                        },
                        icon: const Icon(Icons.play_arrow, size: 18),
                        label: const Text('Play'),
                      ),
                    ),
                    const SizedBox(width: 6),
                    Expanded(
                      child: OutlinedButton.icon(
                        onPressed: () {
                          _bridge.stop();
                          _addLog('>> Stop');
                        },
                        icon: const Icon(Icons.stop, size: 18),
                        label: const Text('Stop'),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 8),

                // Play audio toggle + Volume
                Row(
                  children: [
                    const Text('Audio', style: TextStyle(fontSize: 13)),
                    Switch(
                      value: _playAudio,
                      onChanged: (v) => setState(() => _playAudio = v),
                    ),
                    const Icon(Icons.volume_down, size: 18),
                    Expanded(
                      child: Slider(
                        value: _volume,
                        min: 0.0,
                        max: 1.0,
                        divisions: 20,
                        onChanged: (v) => setState(() => _volume = v),
                        onChangeEnd: (v) {
                          _bridge.setVolume(v);
                          _addLog('>> SetVolume: ${v.toStringAsFixed(2)}');
                        },
                      ),
                    ),
                    const Icon(Icons.volume_up, size: 18),
                  ],
                ),
                const SizedBox(height: 4),

                // Log header
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    Text(
                      'Log (${_logs.length})',
                      style: Theme.of(context).textTheme.titleSmall,
                    ),
                    TextButton(
                      onPressed: () => setState(() => _logs.clear()),
                      child: const Text('Clear'),
                    ),
                  ],
                ),
                const Divider(height: 1),

                // Log area
                Expanded(
                  child: Container(
                    decoration: BoxDecoration(
                      color: Colors.grey.shade100,
                      borderRadius: BorderRadius.circular(6),
                    ),
                    child: ListView.builder(
                      controller: _scrollController,
                      padding: const EdgeInsets.all(6),
                      itemCount: _logs.length,
                      itemBuilder: (context, index) {
                        final log = _logs[index];
                        final isError = log.contains('ERROR') || log.contains('Failed');
                        final isSent = log.contains('>>');
                        return Text(
                          log,
                          style: TextStyle(
                            fontFamily: 'monospace',
                            fontSize: 11,
                            color: isError
                                ? Colors.red.shade700
                                : isSent
                                    ? Colors.blue.shade700
                                    : Colors.black87,
                          ),
                        );
                      },
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ],
    );
  }
}

@JS('connectUnityIframe')
external void _jsConnectUnityIframe();
