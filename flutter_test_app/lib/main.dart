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
    iframe.style.background = 'transparent';
    iframe.setAttribute('allowtransparency', 'true');
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
  final _subtitleController = TextEditingController();
  final _chatSpeakController = TextEditingController();

  String? _audioBase64;
  String _audioFormat = 'wav';
  String? _audioFileName;
  double _volume = 1.0;
  bool _playAudio = true;
  bool _bridgeReady = false;
  bool _unityConnected = false;

  // Avatar state
  static const _fallbackAvatarIds = [
    'sangjun', 'seokhee', 'taerin',
    'new01', 'new02', 'new03', 'new04', 'new05', 'new06', 'new07',
  ];
  List<String> _avatarIds = [];
  String? _currentAvatarId;

  // Batch test state
  final _batchFiles = <({String base64, String format, String fileName})>[];
  final _batchCacheIds = <String>[];
  bool _batchRunning = false;
  bool _batchPlaying = false;
  int _batchPlayIndex = 0;

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
      // Auto-fetch avatar list on bridge ready
      _bridge.getAvatarList();
    });

    _bridge.onPrepared.listen((id) {
      _cacheIdController.text = id;
      _addLog('Prepared: $id');
      _onBatchPrepared(id);
    });

    _bridge.onPrepareFailed.listen((e) {
      _addLog('PrepareFailed: ${e.id} - ${e.error}');
      _cancelBatch('prepare failed');
    });

    _bridge.onPlaybackStarted.listen((id) {
      _addLog('PlaybackStarted: $id');
    });

    _bridge.onPlaybackCompleted.listen((id) {
      _addLog('PlaybackCompleted: $id');
      _onBatchPlaybackCompleted();
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
      _cancelBatch('error occurred');
    });

    _bridge.onCacheInfo.listen((info) {
      _addLog('CacheInfo: count=${info.count}, ids=${info.ids}');
    });

    _bridge.onAvatarChanged.listen((e) {
      if (e.success) {
        setState(() => _currentAvatarId = e.avatarId);
        _addLog('AvatarChanged: ${e.avatarId}');
      } else {
        _addLog('AvatarChanged FAILED: ${e.avatarId} - ${e.error}');
      }
    });

    _bridge.onAvatarList.listen((e) {
      setState(() {
        _avatarIds = e.avatarIds;
        _currentAvatarId = e.currentAvatarId;
      });
      _addLog('AvatarList: ${e.avatarIds} (current: ${e.currentAvatarId})');
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

  // -------------------------------------------------------
  // Single file controls
  // -------------------------------------------------------

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

  // -------------------------------------------------------
  // Batch: pick multiple files, prepare all, then play all
  // -------------------------------------------------------

  void _pickBatchFiles() {
    final input = web.document.createElement('input') as web.HTMLInputElement;
    input.type = 'file';
    input.accept = '.wav,.mp3,.m4a,.ogg,audio/*';
    input.multiple = true;
    input.style.display = 'none';
    web.document.body!.append(input);

    input.onchange = ((web.Event event) {
      final files = input.files;
      if (files != null && files.length > 0) {
        final count = files.length > 5 ? 5 : files.length;
        final tempList = List<({String base64, String format, String fileName})?>.filled(count, null);
        var loaded = 0;

        for (var i = 0; i < count; i++) {
          final file = files.item(i)!;
          final fileName = file.name;
          final format = _getFormatFromName(fileName);
          final reader = web.FileReader();
          final index = i;

          reader.onload = ((web.Event e) {
            final dataUrl = (reader.result as JSString).toDart;
            final base64 = dataUrl.split(',').last;
            tempList[index] = (base64: base64, format: format, fileName: fileName);
            loaded++;

            if (loaded == count) {
              setState(() {
                _batchFiles.clear();
                _batchCacheIds.clear();
                _batchRunning = false;
                _batchPlaying = false;
                for (final item in tempList) {
                  if (item != null) _batchFiles.add(item);
                }
              });
              _addLog('>> Batch: ${_batchFiles.length} files loaded');
              for (var j = 0; j < _batchFiles.length; j++) {
                _addLog('>>   [${j + 1}] ${_batchFiles[j].fileName}');
              }
              input.remove();
            }
          }).toJS;
          reader.readAsDataURL(file);
        }
      } else {
        input.remove();
      }
    }).toJS;

    input.click();
  }

  void _startBatch() {
    if (_batchFiles.isEmpty || _batchRunning) return;
    setState(() {
      _batchCacheIds.clear();
      _batchRunning = true;
      _batchPlaying = false;
      _batchPlayIndex = 0;
    });
    _addLog('>> Batch: starting ${_batchFiles.length} files...');
    _prepareBatchItem(0);
  }

  void _prepareBatchItem(int index) {
    final item = _batchFiles[index];
    _addLog('>> Batch prepare [${index + 1}/${_batchFiles.length}]: ${item.fileName}');
    _bridge.prepareAudio(item.base64, item.format);
  }

  void _onBatchPrepared(String cacheId) {
    if (!_batchRunning) return;

    _batchCacheIds.add(cacheId);
    _addLog('>> Batch prepared [${_batchCacheIds.length}/${_batchFiles.length}]');

    // Start next prepare if remaining
    if (_batchCacheIds.length < _batchFiles.length) {
      _prepareBatchItem(_batchCacheIds.length);
    }

    // Start playback if not already playing and this is the one we need
    if (!_batchPlaying && _batchCacheIds.length > _batchPlayIndex) {
      _batchPlaying = true;
      _playBatchItem(_batchPlayIndex);
    }
  }

  void _playBatchItem(int index) {
    final cacheId = _batchCacheIds[index];
    final fileName = _batchFiles[index].fileName;
    _addLog('>> Batch play [${index + 1}/${_batchCacheIds.length}]: $fileName');
    _bridge.play(cacheId, playAudio: _playAudio);
  }

  void _onBatchPlaybackCompleted() {
    if (!_batchRunning || !_batchPlaying) return;

    _batchPlayIndex++;
    if (_batchPlayIndex >= _batchFiles.length) {
      // All done
      _addLog('>> Batch: all playback completed!');
      setState(() {
        _batchRunning = false;
        _batchPlaying = false;
      });
    } else if (_batchPlayIndex < _batchCacheIds.length) {
      // Next file already prepared, play immediately
      _playBatchItem(_batchPlayIndex);
    } else {
      // Next file not yet prepared, wait for _onBatchPrepared to trigger play
      _batchPlaying = false;
      _addLog('>> Batch: waiting for next prepare...');
    }
  }

  void _cancelBatch(String reason) {
    if (!_batchRunning) return;
    _addLog('>> Batch: stopped ($reason)');
    setState(() {
      _batchRunning = false;
      _batchPlaying = false;
    });
  }

  void _stopBatch() {
    if (!_batchRunning) return;
    _bridge.stop();
    _cancelBatch('user stopped');
  }

  // -------------------------------------------------------
  // Background helpers
  // -------------------------------------------------------

  Widget _bgColorChip(String label, String hex, Color displayColor) {
    return ActionChip(
      avatar: CircleAvatar(
        backgroundColor: displayColor,
        radius: 8,
        child: displayColor == Colors.white
            ? Container(
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  border: Border.all(color: Colors.grey, width: 0.5),
                ),
              )
            : null,
      ),
      label: Text(label),
      onPressed: () {
        _bridge.setBackgroundColor(hex);
        _addLog('>> Background: $label ($hex)');
      },
    );
  }

  // -------------------------------------------------------

  @override
  void dispose() {
    _bridge.dispose();
    _scrollController.dispose();
    _cacheIdController.dispose();
    _subtitleController.dispose();
    _chatSpeakController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Row(
        children: [
          // Left: Unity WebGL (with checkerboard behind for transparency test)
          Expanded(
            flex: 3,
            child: ClipRect(
              child: Stack(
                children: [
                  // Checkerboard background to verify transparency
                  Positioned.fill(
                    child: CustomPaint(painter: _CheckerboardPainter()),
                  ),
                  const Positioned.fill(
                    child: HtmlElementView(viewType: 'unity-webgl-iframe'),
                  ),
                ],
              ),
            ),
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
                // --- Single file controls ---
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
                const SizedBox(height: 6),
                TextField(
                  controller: _subtitleController,
                  decoration: const InputDecoration(
                    labelText: 'Subtitle Text',
                    hintText: 'Enter text for emotion tagging',
                    isDense: true,
                    border: OutlineInputBorder(),
                    contentPadding: EdgeInsets.symmetric(horizontal: 10, vertical: 8),
                  ),
                  style: const TextStyle(fontSize: 13),
                  maxLines: 2,
                  minLines: 1,
                ),
                const SizedBox(height: 6),
                FilledButton.icon(
                  onPressed: _audioBase64 != null
                      ? () {
                          final text = _subtitleController.text.trim();
                          _bridge.prepareAudio(_audioBase64!, _audioFormat, text: text);
                          _addLog('>> Prepare: $_audioFileName${text.isNotEmpty ? ' (text: ${text.length} chars)' : ' (no text)'}');
                        }
                      : null,
                  icon: const Icon(Icons.upload_file, size: 18),
                  label: const Text('Generate'),
                ),
                const SizedBox(height: 6),
                TextField(
                  controller: _cacheIdController,
                  decoration: const InputDecoration(
                    labelText: 'Cache ID',
                    hintText: 'Auto-filled on Generate',
                    isDense: true,
                    border: OutlineInputBorder(),
                    contentPadding: EdgeInsets.symmetric(horizontal: 10, vertical: 8),
                  ),
                  style: const TextStyle(fontFamily: 'monospace', fontSize: 12),
                ),
                const SizedBox(height: 6),
                Row(
                  children: [
                    Expanded(
                      child: FilledButton.icon(
                        onPressed: () {
                          final cacheId = _cacheIdController.text.trim();
                          if (cacheId.isEmpty) {
                            _addLog('>> ERROR: No cache ID. Generate first.');
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

                // --- Avatar controls ---
                const Divider(height: 16),
                Row(
                  children: [
                    Text('Avatar', style: Theme.of(context).textTheme.titleSmall),
                    if (_currentAvatarId != null) ...[
                      const SizedBox(width: 8),
                      Text(
                        '(current: $_currentAvatarId)',
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                    ],
                    const Spacer(),
                    SizedBox(
                      height: 28,
                      child: OutlinedButton(
                        onPressed: () {
                          _bridge.getAvatarList();
                          _addLog('>> GetAvatarList');
                        },
                        style: OutlinedButton.styleFrom(
                          padding: const EdgeInsets.symmetric(horizontal: 8),
                        ),
                        child: const Text('Refresh', style: TextStyle(fontSize: 11)),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 6),
                Wrap(
                  spacing: 6,
                  runSpacing: 6,
                  children: (_avatarIds.isNotEmpty ? _avatarIds : _fallbackAvatarIds).map((id) {
                    final isSelected = id == _currentAvatarId;
                    return ChoiceChip(
                      label: Text(id, style: const TextStyle(fontSize: 12)),
                      selected: isSelected,
                      onSelected: (_) {
                        if (!isSelected) {
                          _bridge.changeAvatar(id);
                          _addLog('>> ChangeAvatar: $id');
                        }
                      },
                    );
                  }).toList(),
                ),
                if (_avatarIds.isEmpty)
                  Padding(
                    padding: const EdgeInsets.only(top: 4),
                    child: Text(
                      'Using fallback list (Unity not configured yet)',
                      style: TextStyle(fontSize: 10, color: Colors.orange.shade700),
                    ),
                  ),

                // --- Background controls ---
                const Divider(height: 16),
                Text('Background', style: Theme.of(context).textTheme.titleSmall),
                const SizedBox(height: 6),
                Wrap(
                  spacing: 6,
                  runSpacing: 6,
                  children: [
                    _bgColorChip('Dark', '#231F20', const Color(0xFF231F20)),
                    _bgColorChip('White', '#FFFFFF', Colors.white),
                    _bgColorChip('Green', '#00B050', const Color(0xFF00B050)),
                    _bgColorChip('Blue', '#1A73E8', const Color(0xFF1A73E8)),
                    _bgColorChip('Gray', '#808080', const Color(0xFF808080)),
                    ActionChip(
                      avatar: const Icon(Icons.opacity, size: 16),
                      label: const Text('Transparent'),
                      onPressed: () {
                        _bridge.setBackgroundColor('transparent');
                        _addLog('>> Background: transparent');
                      },
                    ),
                  ],
                ),

                // --- Cache controls ---
                const Divider(height: 16),
                Text('Cache', style: Theme.of(context).textTheme.titleSmall),
                const SizedBox(height: 6),
                Row(
                  children: [
                    Expanded(
                      child: OutlinedButton.icon(
                        onPressed: () {
                          _bridge.getCacheInfo();
                          _addLog('>> GetCacheInfo');
                        },
                        icon: const Icon(Icons.info_outline, size: 18),
                        label: const Text('Cache Info'),
                      ),
                    ),
                    const SizedBox(width: 6),
                    Expanded(
                      child: OutlinedButton.icon(
                        onPressed: () {
                          _bridge.clearAllCache();
                          _addLog('>> ClearAllCache');
                        },
                        icon: const Icon(Icons.delete_outline, size: 18),
                        label: const Text('Clear All'),
                      ),
                    ),
                  ],
                ),

                // --- Chat / Speak controls ---
                const Divider(height: 16),
                Text('Chat / Speak', style: Theme.of(context).textTheme.titleSmall),
                const SizedBox(height: 6),
                TextField(
                  controller: _chatSpeakController,
                  decoration: const InputDecoration(
                    labelText: 'Text',
                    hintText: 'Enter text for Chat or Speak',
                    isDense: true,
                    border: OutlineInputBorder(),
                    contentPadding: EdgeInsets.symmetric(horizontal: 10, vertical: 8),
                  ),
                  style: const TextStyle(fontSize: 13),
                  maxLines: 2,
                  minLines: 1,
                ),
                const SizedBox(height: 6),
                Row(
                  children: [
                    Expanded(
                      child: FilledButton.icon(
                        onPressed: () {
                          final text = _chatSpeakController.text.trim();
                          if (text.isEmpty) {
                            _addLog('>> ERROR: No text entered.');
                            return;
                          }
                          _bridge.chat(text, playAudio: _playAudio);
                          _addLog('>> Chat: $text');
                        },
                        icon: const Icon(Icons.chat, size: 18),
                        label: const Text('Chat'),
                      ),
                    ),
                    const SizedBox(width: 6),
                    Expanded(
                      child: FilledButton.icon(
                        onPressed: () {
                          final text = _chatSpeakController.text.trim();
                          if (text.isEmpty) {
                            _addLog('>> ERROR: No text entered.');
                            return;
                          }
                          _bridge.speak(text, playAudio: _playAudio);
                          _addLog('>> Speak: $text');
                        },
                        icon: const Icon(Icons.record_voice_over, size: 18),
                        label: const Text('Speak'),
                      ),
                    ),
                  ],
                ),

                // --- Batch controls ---
                const Divider(height: 16),
                Text('Batch Test', style: Theme.of(context).textTheme.titleSmall),
                const SizedBox(height: 6),
                OutlinedButton.icon(
                  onPressed: _batchRunning ? null : _pickBatchFiles,
                  icon: const Icon(Icons.library_music, size: 18),
                  label: Text(
                    _batchFiles.isEmpty
                        ? 'Pick Audio Files (max 5)'
                        : '${_batchFiles.length} files selected',
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
                if (_batchFiles.isNotEmpty) ...[
                  const SizedBox(height: 4),
                  ...List.generate(
                    _batchFiles.length,
                    (i) => Padding(
                      padding: const EdgeInsets.only(left: 8),
                      child: Text(
                        '[${i + 1}] ${_batchFiles[i].fileName}',
                        style: const TextStyle(fontFamily: 'monospace', fontSize: 11),
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                  ),
                ],
                const SizedBox(height: 6),
                Row(
                  children: [
                    Expanded(
                      child: FilledButton.icon(
                        onPressed: _batchFiles.isNotEmpty && !_batchRunning
                            ? _startBatch
                            : null,
                        icon: const Icon(Icons.playlist_play, size: 18),
                        label: Text(_batchRunning
                            ? 'Running... (${_batchCacheIds.length}/${_batchFiles.length})'
                            : 'Generate & Play All'),
                      ),
                    ),
                    if (_batchRunning) ...[
                      const SizedBox(width: 6),
                      OutlinedButton.icon(
                        onPressed: _stopBatch,
                        icon: const Icon(Icons.stop, size: 18),
                        label: const Text('Stop'),
                      ),
                    ],
                  ],
                ),

                const SizedBox(height: 8),

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

class _CheckerboardPainter extends CustomPainter {
  @override
  void paint(Canvas canvas, Size size) {
    const cellSize = 20.0;
    final light = Paint()..color = const Color(0xFFCCCCCC);
    final dark = Paint()..color = const Color(0xFF999999);

    for (var y = 0.0; y < size.height; y += cellSize) {
      for (var x = 0.0; x < size.width; x += cellSize) {
        final isEven = ((x ~/ cellSize) + (y ~/ cellSize)) % 2 == 0;
        canvas.drawRect(
          Rect.fromLTWH(x, y, cellSize, cellSize),
          isEven ? light : dark,
        );
      }
    }
  }

  @override
  bool shouldRepaint(covariant CustomPainter oldDelegate) => false;
}
