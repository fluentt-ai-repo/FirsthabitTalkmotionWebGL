#if UNITY_WEBGL
using System;
using System.Collections;
using System.Runtime.InteropServices;
using FluentT.APIClient.V3;
using System.Collections.Generic;
using FluentT.Avatar.SampleFloatingHead;
using FluentT.Talkmotion;
using FluentT.Talkmotion.Timeline.UnityTimeline;
using TalkmotionTimelinePlayer = FluentT.Talkmotion.Timeline.Player.TalkmotionTimelinePlayer;
using PlayerSubtitleEventData = FluentT.Talkmotion.Timeline.Player.SubtitleEventData;
using UnityEngine;
using UnityEngine.Networking;

namespace Firsthabit.WebGL
{
    public class FirsthabitWebGLBridge : MonoBehaviour
    {
        #region Serialized Fields

        [SerializeField] private FluentTAvatar fluentTAvatar;
        [SerializeField] private bool enableLogging = true;

        [Header("Avatar Management")]
        [SerializeField] private AvatarEntry[] avatarEntries;

        #endregion

        #region Data Classes

        [Serializable]
        public class AvatarEntry
        {
            public string id;
            public GameObject prefab;
        }

        [Serializable]
        private class PrepareRequest
        {
            public string format = "wav";
            public string text = "";
        }

        [Serializable]
        private class PlayRequest
        {
            public string cacheId = "";
            public bool playAudio = true;
        }

        [Serializable]
        private class ChatRequest
        {
            public string text = "";
            public bool prepareOnly = false;
            public bool playAudio = true;
        }

        [Serializable]
        private class SpeakRequest
        {
            public string text = "";
            public string subtitleText = "";
            public bool prepareOnly = false;
            public bool playAudio = true;
        }

        [Serializable]
        private class CacheInfoResponse
        {
            public int count;
            public string[] ids;
        }

        [Serializable]
        private class AvatarListResponse
        {
            public string[] avatarIds;
            public string currentAvatarId;
        }

        [Serializable]
        private class AvatarChangedResponse
        {
            public string avatarId;
            public bool success;
            public string error;
        }

        [Serializable]
        private class TimelineAudioRequest
        {
            public string clipId;
            public string format = "wav";
        }

        [Serializable]
        private class PlayTimelineRequest
        {
            public bool playAudio = true;
            public string cacheId;
        }

        #endregion

        #region State

        private string currentPlayingCacheId;
        private Dictionary<string, GameObject> avatarPrefabMap;
        private string currentAvatarId;
        private GameObject currentAvatarInstance;
        private HashSet<string> pendingAutoPlay = new HashSet<string>();
        private Dictionary<string, bool> pendingPlayAudio = new Dictionary<string, bool>();
        private bool pendingPlaybackStarted = false;
        private TalkmotionTimelinePlayer timelinePlayer;
        private string currentTimelineCacheId;
        private bool timelineAudioMuted;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            // Build avatar prefab map
            avatarPrefabMap = new Dictionary<string, GameObject>();
            if (avatarEntries != null)
            {
                foreach (var entry in avatarEntries)
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.id) && entry.prefab != null)
                    {
                        avatarPrefabMap[entry.id] = entry.prefab;
                    }
                }
                Log($"Avatar prefab map built: {avatarPrefabMap.Count} entries");
            }

            if (fluentTAvatar == null)
            {
                fluentTAvatar = FindAnyObjectByType<FluentTAvatar>();
                if (fluentTAvatar == null)
                {
                    LogError("Awake", "FluentTAvatar not found in scene");
                    return;
                }
                Log($"FluentTAvatar auto-found: {fluentTAvatar.name}");
            }

            // Track the initial avatar instance
            currentAvatarInstance = fluentTAvatar.gameObject;
        }

        private void Start()
        {
            if (fluentTAvatar == null) return;

            RegisterCallbacks();
            InitializeTimelinePlayer();
            FH_OnBridgeReady();
            Log("Bridge ready");
        }

        private void OnDestroy()
        {
            CleanupTimelinePlayer();
            UnregisterCallbacks();
        }

        private void Update()
        {
            timelinePlayer?.Update(Time.deltaTime);
        }

        private void LateUpdate()
        {
            timelinePlayer?.LateUpdate(Time.deltaTime);
        }

        #endregion

        #region Callback Registration

        private void RegisterCallbacks()
        {
            if (fluentTAvatar == null) return;

            // Prepare cache events
            fluentTAvatar.onPrepared.AddListener(OnAvatarPrepared);
            fluentTAvatar.onPrepareFailed.AddListener(OnAvatarPrepareFailed);

            // Sentence events
            fluentTAvatar.callback.onSentenceStarted.AddListener(OnAvatarSentenceStarted);
            fluentTAvatar.callback.onSentenceEnded.AddListener(OnAvatarSentenceEnded);

            // Server events
            fluentTAvatar.callback.onTalkmotionRequestSent.AddListener(OnAvatarRequestSent);
            fluentTAvatar.callback.onTalkmotionResponseReceived.AddListener(OnAvatarResponseReceived);

            // Subtitle events
            fluentTAvatar.OnSubtitleTextStarted.AddListener(OnSubtitleStarted);
            fluentTAvatar.OnSubtitleTextEnded.AddListener(OnSubtitleEnded);
        }

        private void UnregisterCallbacks()
        {
            if (fluentTAvatar == null) return;

            try
            {
                fluentTAvatar.onPrepared.RemoveListener(OnAvatarPrepared);
                fluentTAvatar.onPrepareFailed.RemoveListener(OnAvatarPrepareFailed);

                var cb = fluentTAvatar.callback;
                if (cb != null)
                {
                    cb.onSentenceStarted.RemoveListener(OnAvatarSentenceStarted);
                    cb.onSentenceEnded.RemoveListener(OnAvatarSentenceEnded);
                    cb.onTalkmotionRequestSent.RemoveListener(OnAvatarRequestSent);
                    cb.onTalkmotionResponseReceived.RemoveListener(OnAvatarResponseReceived);
                }

                fluentTAvatar.OnSubtitleTextStarted.RemoveListener(OnSubtitleStarted);
                fluentTAvatar.OnSubtitleTextEnded.RemoveListener(OnSubtitleEnded);
            }
            catch (Exception e)
            {
                Log($"UnregisterCallbacks: partial failure - {e.Message}");
            }
        }

        #endregion

        #region Flutter → Unity Methods (called via SendMessage)

        /// <summary>
        /// Prepare audio: reads base64 from JS, creates AudioClip via blob URL,
        /// then calls PrepareSpeechMotion to send to server and cache.
        /// JSON: { "format": "wav", "text": "subtitle text" }
        /// onPrepared(cacheId) fires when server processing is complete.
        /// </summary>
        public void PrepareAudio(string json)
        {
            StartCoroutine(PrepareAudioCoroutine(json));
        }

        private IEnumerator PrepareAudioCoroutine(string json)
        {
            PrepareRequest request;
            try
            {
                request = JsonUtility.FromJson<PrepareRequest>(json);
            }
            catch (Exception e)
            {
                FH_OnError("PrepareAudio", $"Invalid JSON: {e.Message}");
                yield break;
            }

            // Create blob URL from pending audio base64 (reads from parent window)
            string blobUrl = FH_CreateAudioBlobUrl(request.format);
            if (string.IsNullOrEmpty(blobUrl))
            {
                FH_OnError("PrepareAudio", "No audio data pending");
                yield break;
            }

            Log($"PrepareAudio: loading AudioClip from blob URL, format={request.format}");

            // Determine Unity AudioType
            AudioType audioType = GetAudioType(request.format);

            // Load AudioClip from blob URL using UnityWebRequest
            using (var www = UnityWebRequestMultimedia.GetAudioClip(blobUrl, audioType))
            {
                // Disable streaming to ensure full decode before access
                ((DownloadHandlerAudioClip)www.downloadHandler).streamAudio = false;

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    FH_RevokeAudioBlobUrl(blobUrl);
                    FH_OnError("PrepareAudio", $"Failed to load AudioClip: {www.error}");
                    yield break;
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                if (clip == null)
                {
                    FH_RevokeAudioBlobUrl(blobUrl);
                    FH_OnError("PrepareAudio", "AudioClip creation returned null");
                    yield break;
                }

                // Wait for audio data to fully decode (WebGL decodes async)
                int waitFrames = 300; // ~5 seconds at 60fps
                while (clip.loadState != AudioDataLoadState.Loaded && waitFrames > 0)
                {
                    if (clip.loadState == AudioDataLoadState.Failed)
                    {
                        FH_RevokeAudioBlobUrl(blobUrl);
                        FH_OnError("PrepareAudio", "AudioClip decode failed");
                        yield break;
                    }
                    yield return null;
                    waitFrames--;
                }

                // Revoke blob URL after decode completes
                FH_RevokeAudioBlobUrl(blobUrl);

                if (clip.loadState != AudioDataLoadState.Loaded)
                {
                    FH_OnError("PrepareAudio", $"AudioClip decode timed out (state={clip.loadState})");
                    yield break;
                }

                Log($"PrepareAudio: AudioClip loaded - length={clip.length:F2}s, channels={clip.channels}, freq={clip.frequency}Hz");

                // Call FluentT PrepareSpeechMotion: sends audio to server, caches result
                // onPrepared(cacheId) will fire when server processing is complete
                try
                {
                    string cacheId = fluentTAvatar.PrepareSpeechMotion(clip, request.text ?? "", null);
                    Log($"PrepareAudio: PrepareSpeechMotion called, cacheId={cacheId}, text={request.text}");
                }
                catch (Exception e)
                {
                    FH_OnError("PrepareAudio", $"PrepareSpeechMotion failed: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Play a previously prepared cache entry.
        /// JSON: { "cacheId": "xxx", "playAudio": true }
        /// </summary>
        public void Play(string json)
        {
            try
            {
                var request = JsonUtility.FromJson<PlayRequest>(json);

                if (string.IsNullOrEmpty(request.cacheId))
                {
                    FH_OnError("Play", "No cacheId provided");
                    return;
                }

                Log($"Play: cacheId={request.cacheId}, playAudio={request.playAudio}");

                bool success = fluentTAvatar.PlayPrepared(
                    request.cacheId,
                    continuePlayback: false,
                    playAudio: request.playAudio
                );

                if (success)
                {
                    currentPlayingCacheId = request.cacheId;
                    pendingPlaybackStarted = false;
                    FH_OnPlaybackStarted(request.cacheId);
                }
                else
                {
                    FH_OnError("Play", $"PlayPrepared failed for cacheId: {request.cacheId}");
                }
            }
            catch (Exception e)
            {
                FH_OnError("Play", e.Message);
            }
        }

        /// <summary>
        /// Stop current playback
        /// </summary>
        public void Stop()
        {
            try
            {
                fluentTAvatar.StopTalkMotion();
                currentPlayingCacheId = null;
                pendingPlaybackStarted = false;
                Log("Stop called");
            }
            catch (Exception e)
            {
                FH_OnError("Stop", e.Message);
            }
        }

        /// <summary>
        /// Set volume (string "0.0"~"1.0")
        /// </summary>
        public void SetVolume(string volumeStr)
        {
            try
            {
                if (float.TryParse(volumeStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float volume))
                {
                    fluentTAvatar.SetVolume(volume);
                    FH_OnVolumeChanged(volume);
                    Log($"Volume set to {volume}");
                }
                else
                {
                    FH_OnError("SetVolume", $"Invalid volume value: {volumeStr}");
                }
            }
            catch (Exception e)
            {
                FH_OnError("SetVolume", e.Message);
            }
        }

        /// <summary>
        /// Get cache info (count and IDs) and send to Flutter via callback
        /// </summary>
        public void GetCacheInfo()
        {
            try
            {
                var response = new CacheInfoResponse
                {
                    count = fluentTAvatar.CacheCount,
                    ids = fluentTAvatar.GetCachedIds()
                };
                string json = JsonUtility.ToJson(response);
                Log($"CacheInfo: {json}");
                FH_OnCacheInfo(json);
            }
            catch (Exception e)
            {
                FH_OnError("GetCacheInfo", e.Message);
            }
        }

        /// <summary>
        /// Clear all cached motion data
        /// </summary>
        public void ClearAllCache()
        {
            try
            {
                fluentTAvatar.ClearCache(null);
                Log("All cache cleared");
                GetCacheInfo();
            }
            catch (Exception e)
            {
                FH_OnError("ClearAllCache", e.Message);
            }
        }

        /// <summary>
        /// Change avatar by ID. Destroys current avatar, instantiates new one,
        /// re-registers callbacks, and notifies Flutter.
        /// </summary>
        public void ChangeAvatar(string avatarId)
        {
            try
            {
                // Skip if same avatar
                if (avatarId == currentAvatarId)
                {
                    Log($"ChangeAvatar: already using '{avatarId}', skipping");
                    var skipResponse = new AvatarChangedResponse
                    {
                        avatarId = avatarId,
                        success = true,
                        error = ""
                    };
                    FH_OnAvatarChanged(JsonUtility.ToJson(skipResponse));
                    return;
                }

                // Look up prefab
                if (!avatarPrefabMap.TryGetValue(avatarId, out GameObject prefab))
                {
                    var errorMsg = $"Avatar '{avatarId}' not found in avatarEntries";
                    LogError("ChangeAvatar", errorMsg);
                    var errorResponse = new AvatarChangedResponse
                    {
                        avatarId = avatarId,
                        success = false,
                        error = errorMsg
                    };
                    FH_OnAvatarChanged(JsonUtility.ToJson(errorResponse));
                    return;
                }

                Log($"ChangeAvatar: switching from '{currentAvatarId}' to '{avatarId}'");

                // Unregister callbacks first (before SDK state changes)
                UnregisterCallbacks();

                // Stop playback and clear cache
                fluentTAvatar.StopTalkMotion();
                fluentTAvatar.ClearCache(null);
                currentPlayingCacheId = null;

                // Save transform of current avatar
                var pos = currentAvatarInstance != null
                    ? currentAvatarInstance.transform.position : Vector3.zero;
                var rot = currentAvatarInstance != null
                    ? currentAvatarInstance.transform.rotation : Quaternion.identity;

                // Destroy current avatar
                if (currentAvatarInstance != null)
                    Destroy(currentAvatarInstance);

                // Instantiate new avatar
                currentAvatarInstance = Instantiate(prefab, pos, rot);
                currentAvatarId = avatarId;

                // Get FluentTAvatar component from new instance
                fluentTAvatar = currentAvatarInstance.GetComponent<FluentTAvatar>();
                if (fluentTAvatar == null)
                {
                    fluentTAvatar = currentAvatarInstance.GetComponentInChildren<FluentTAvatar>();
                }

                if (fluentTAvatar == null)
                {
                    var errorMsg = "FluentTAvatar component not found on new avatar";
                    LogError("ChangeAvatar", errorMsg);
                    var errorResponse = new AvatarChangedResponse
                    {
                        avatarId = avatarId,
                        success = false,
                        error = errorMsg
                    };
                    FH_OnAvatarChanged(JsonUtility.ToJson(errorResponse));
                    return;
                }

                // Set look target on the floating head controller
                var headController = currentAvatarInstance
                    .GetComponent<FluentTAvatarControllerFloatingHead>();
                if (headController == null)
                {
                    headController = currentAvatarInstance
                        .GetComponentInChildren<FluentTAvatarControllerFloatingHead>();
                }
                if (headController != null && Camera.main != null)
                {
                    headController.SetLookTarget(Camera.main.transform);
                    Log("ChangeAvatar: look target set to main camera");
                }

                // Register callbacks on new avatar
                RegisterCallbacks();

                // Reinitialize timeline player for new avatar
                CleanupTimelinePlayer();
                InitializeTimelinePlayer();

                Log($"ChangeAvatar: successfully changed to '{avatarId}'");

                var successResponse = new AvatarChangedResponse
                {
                    avatarId = avatarId,
                    success = true,
                    error = ""
                };
                FH_OnAvatarChanged(JsonUtility.ToJson(successResponse));
            }
            catch (Exception e)
            {
                Debug.LogError($"[FirsthabitBridge] ChangeAvatar: {e.Message}\n{e.StackTrace}");
                var errorResponse = new AvatarChangedResponse
                {
                    avatarId = avatarId,
                    success = false,
                    error = e.Message
                };
                FH_OnAvatarChanged(JsonUtility.ToJson(errorResponse));
            }
        }

        /// <summary>
        /// Get list of available avatar IDs and current avatar ID.
        /// Sends result to Flutter via FH_OnAvatarList callback.
        /// String param is ignored (required for SendMessage compatibility).
        /// </summary>
        public void GetAvatarList(string ignored)
        {
            try
            {
                var keys = new string[avatarPrefabMap.Count];
                avatarPrefabMap.Keys.CopyTo(keys, 0);

                var response = new AvatarListResponse
                {
                    avatarIds = keys,
                    currentAvatarId = currentAvatarId
                };
                string json = JsonUtility.ToJson(response);
                Log($"AvatarList: {json}");
                FH_OnAvatarList(json);
            }
            catch (Exception e)
            {
                FH_OnError("GetAvatarList", e.Message);
            }
        }

        /// <summary>
        /// Set camera background color.
        /// Param: "transparent" or hex color string like "#FF0000", "#00B050"
        /// </summary>
        public void SetBackgroundColor(string colorString)
        {
            try
            {
                var cam = Camera.main;
                if (cam == null)
                {
                    FH_OnError("SetBackgroundColor", "Main camera not found");
                    return;
                }

                if (colorString.Equals("transparent", StringComparison.OrdinalIgnoreCase))
                {
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0, 0, 0, 0);
                    Log("Background set to transparent");
                }
                else if (ColorUtility.TryParseHtmlString(colorString, out Color color))
                {
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = color;
                    Log($"Background color set to {colorString}");
                }
                else
                {
                    FH_OnError("SetBackgroundColor", $"Invalid color: {colorString}. Use 'transparent' or '#RRGGBB'");
                }
            }
            catch (Exception e)
            {
                FH_OnError("SetBackgroundColor", e.Message);
            }
        }

        /// <summary>
        /// Start a chat conversation. Server uses LLM to generate response, then TTS + Motion.
        /// JSON: { "text": "user message", "prepareOnly": false, "playAudio": true }
        /// - prepareOnly=false (default): uses StartChat for direct streaming playback.
        ///   Callbacks: onRequestSent → onResponseReceived → onPlaybackStarted → onSentence* → onPlaybackCompleted
        /// - prepareOnly=true: only prepares. Use Play(cacheId) to play later.
        ///   Callbacks: onRequestSent → onResponseReceived → onPrepared
        /// </summary>
        public void Chat(string json)
        {
            try
            {
                var request = JsonUtility.FromJson<ChatRequest>(json);

                if (string.IsNullOrEmpty(request.text))
                {
                    FH_OnError("Chat", "No text provided");
                    return;
                }

                Log($"Chat: text={request.text}, prepareOnly={request.prepareOnly}, playAudio={request.playAudio}");

                if (request.prepareOnly)
                {
                    // Prepare-only: cache for later playback via Play()
                    string cacheId = fluentTAvatar.PrepareChat(request.text);
                    if (string.IsNullOrEmpty(cacheId))
                    {
                        FH_OnError("Chat", "PrepareChat returned null cacheId");
                        return;
                    }
                    Log($"Chat: prepared cacheId={cacheId}");
                }
                else
                {
                    // Direct play: use StartChat for streaming multi-sentence support
                    var options = new TalkmotionOptions { playAudio = request.playAudio };
                    TalkmotionID? requestId = fluentTAvatar.StartChat(request.text, options);
                    if (!requestId.HasValue)
                    {
                        FH_OnError("Chat", "StartChat returned null requestId");
                        return;
                    }
                    currentPlayingCacheId = requestId.Value.Value;
                    pendingPlaybackStarted = true;
                    Log($"Chat: started requestId={requestId.Value.Value}");
                }
            }
            catch (Exception e)
            {
                FH_OnError("Chat", e.Message);
            }
        }

        /// <summary>
        /// Convert text to speech with motion. Server generates TTS audio + Motion (no LLM).
        /// JSON: { "text": "text to speak", "subtitleText": "", "prepareOnly": false, "playAudio": true }
        /// - subtitleText: optional separate subtitle text. If empty, uses text.
        /// - prepareOnly=false (default): uses StartSpeak for direct streaming playback.
        ///   Callbacks: onRequestSent → onResponseReceived → onPlaybackStarted → onSentence* → onPlaybackCompleted
        /// - prepareOnly=true: only prepares. Use Play(cacheId) to play later.
        ///   Callbacks: onRequestSent → onResponseReceived → onPrepared
        /// </summary>
        public void Speak(string json)
        {
            try
            {
                var request = JsonUtility.FromJson<SpeakRequest>(json);

                if (string.IsNullOrEmpty(request.text))
                {
                    FH_OnError("Speak", "No text provided");
                    return;
                }

                Log($"Speak: text={request.text}, subtitleText={request.subtitleText}, prepareOnly={request.prepareOnly}, playAudio={request.playAudio}");

                if (request.prepareOnly)
                {
                    // Prepare-only: cache for later playback via Play()
                    string cacheId;
                    if (!string.IsNullOrEmpty(request.subtitleText))
                    {
                        cacheId = fluentTAvatar.PrepareSpeak(request.text, request.subtitleText);
                    }
                    else
                    {
                        cacheId = fluentTAvatar.PrepareSpeak(request.text);
                    }

                    if (string.IsNullOrEmpty(cacheId))
                    {
                        FH_OnError("Speak", "PrepareSpeak returned null cacheId");
                        return;
                    }
                    Log($"Speak: prepared cacheId={cacheId}");
                }
                else
                {
                    // Direct play: use StartSpeak for streaming multi-sentence support
                    var options = new TalkmotionOptions { playAudio = request.playAudio };
                    TalkmotionID? requestId;
                    if (!string.IsNullOrEmpty(request.subtitleText))
                    {
                        requestId = fluentTAvatar.StartSpeak(request.text, request.subtitleText, options);
                    }
                    else
                    {
                        requestId = fluentTAvatar.StartSpeak(request.text, options);
                    }

                    if (!requestId.HasValue)
                    {
                        FH_OnError("Speak", "StartSpeak returned null requestId");
                        return;
                    }
                    currentPlayingCacheId = requestId.Value.Value;
                    pendingPlaybackStarted = true;
                    Log($"Speak: started requestId={requestId.Value.Value}");
                }
            }
            catch (Exception e)
            {
                FH_OnError("Speak", e.Message);
            }
        }

        #endregion

        #region Helper Methods

        private AudioType GetAudioType(string format)
        {
            return format switch
            {
                "wav" => AudioType.WAV,
                "mp3" => AudioType.MPEG,
                "ogg" => AudioType.OGGVORBIS,
                "m4a" => AudioType.ACC,
                _ => AudioType.UNKNOWN
            };
        }

        #endregion

        #region Timeline Player

        private void InitializeTimelinePlayer()
        {
            if (fluentTAvatar == null) return;

            timelinePlayer = new TalkmotionTimelinePlayer();

            var audioSource = fluentTAvatar.GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = fluentTAvatar.gameObject.AddComponent<AudioSource>();
            }

            timelinePlayer.Initialize(audioSource, fluentTAvatar.TMAnimationComponent);

            // Register timeline events
            timelinePlayer.OnPlaybackStarted += OnTimelinePlaybackStarted;
            timelinePlayer.OnPlaybackCompleted += OnTimelinePlaybackCompleted;
            timelinePlayer.OnSentenceStarted += OnTimelineSentenceStarted;
            timelinePlayer.OnSentenceEnded += OnTimelineSentenceEnded;
            timelinePlayer.OnSubtitleTextStarted += OnTimelineSubtitleStarted;
            timelinePlayer.OnSubtitleTextEnded += OnTimelineSubtitleEnded;

            Log("TimelinePlayer initialized");
        }

        private void CleanupTimelinePlayer()
        {
            if (timelinePlayer == null) return;

            timelinePlayer.OnPlaybackStarted -= OnTimelinePlaybackStarted;
            timelinePlayer.OnPlaybackCompleted -= OnTimelinePlaybackCompleted;
            timelinePlayer.OnSentenceStarted -= OnTimelineSentenceStarted;
            timelinePlayer.OnSentenceEnded -= OnTimelineSentenceEnded;
            timelinePlayer.OnSubtitleTextStarted -= OnTimelineSubtitleStarted;
            timelinePlayer.OnSubtitleTextEnded -= OnTimelineSubtitleEnded;

            timelinePlayer.Stop();
            timelinePlayer = null;
            currentTimelineCacheId = null;

            Log("TimelinePlayer cleaned up");
        }

        /// <summary>
        /// Load timeline JSON data for direct playback.
        /// Param: timeline JSON string from FluentT API.
        /// </summary>
        public void LoadTimeline(string json)
        {
            try
            {
                if (timelinePlayer == null)
                {
                    FH_OnError("LoadTimeline", "TimelinePlayer not initialized");
                    return;
                }

                timelinePlayer.LoadTimeline(json);
                Log($"LoadTimeline: loaded, duration={timelinePlayer.Duration:F2}s");
            }
            catch (Exception e)
            {
                FH_OnError("LoadTimeline", e.Message);
            }
        }

        /// <summary>
        /// Load audio clip for timeline playback.
        /// Reads base64 audio from JS (same pattern as PrepareAudio).
        /// JSON: { "clipId": "clip-001", "format": "wav" }
        /// Fires FH_OnPrepared(clipId) when loading is complete.
        /// </summary>
        public void LoadTimelineAudio(string json)
        {
            StartCoroutine(LoadTimelineAudioCoroutine(json));
        }

        private IEnumerator LoadTimelineAudioCoroutine(string json)
        {
            TimelineAudioRequest request;
            try
            {
                request = JsonUtility.FromJson<TimelineAudioRequest>(json);
            }
            catch (Exception e)
            {
                FH_OnError("LoadTimelineAudio", $"Invalid JSON: {e.Message}");
                yield break;
            }

            if (timelinePlayer == null)
            {
                FH_OnError("LoadTimelineAudio", "TimelinePlayer not initialized");
                yield break;
            }

            if (string.IsNullOrEmpty(request.clipId))
            {
                FH_OnError("LoadTimelineAudio", "No clipId provided");
                yield break;
            }

            // Create blob URL from pending audio base64
            string blobUrl = FH_CreateAudioBlobUrl(request.format);
            if (string.IsNullOrEmpty(blobUrl))
            {
                FH_OnError("LoadTimelineAudio", "No audio data pending");
                yield break;
            }

            Log($"LoadTimelineAudio: loading clipId={request.clipId}, format={request.format}");

            AudioType audioType = GetAudioType(request.format);

            using (var www = UnityWebRequestMultimedia.GetAudioClip(blobUrl, audioType))
            {
                ((DownloadHandlerAudioClip)www.downloadHandler).streamAudio = false;

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    FH_RevokeAudioBlobUrl(blobUrl);
                    FH_OnError("LoadTimelineAudio", $"Failed to load AudioClip: {www.error}");
                    yield break;
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                if (clip == null)
                {
                    FH_RevokeAudioBlobUrl(blobUrl);
                    FH_OnError("LoadTimelineAudio", "AudioClip creation returned null");
                    yield break;
                }

                // Wait for audio data to fully decode (WebGL decodes async)
                int waitFrames = 300;
                while (clip.loadState != AudioDataLoadState.Loaded && waitFrames > 0)
                {
                    if (clip.loadState == AudioDataLoadState.Failed)
                    {
                        FH_RevokeAudioBlobUrl(blobUrl);
                        FH_OnError("LoadTimelineAudio", "AudioClip decode failed");
                        yield break;
                    }
                    yield return null;
                    waitFrames--;
                }

                FH_RevokeAudioBlobUrl(blobUrl);

                if (clip.loadState != AudioDataLoadState.Loaded)
                {
                    FH_OnError("LoadTimelineAudio", $"AudioClip decode timed out (state={clip.loadState})");
                    yield break;
                }

                Log($"LoadTimelineAudio: AudioClip loaded - clipId={request.clipId}, length={clip.length:F2}s");

                // Set audio clip on timeline player
                var audioClips = new Dictionary<string, AudioClip>
                {
                    { request.clipId, clip }
                };
                timelinePlayer.SetAudioClips(audioClips);

                FH_OnPrepared(request.clipId);
            }
        }

        /// <summary>
        /// Play the loaded timeline.
        /// JSON: { "playAudio": true, "cacheId": "timeline-001" }
        /// </summary>
        public void PlayTimeline(string json)
        {
            try
            {
                var request = JsonUtility.FromJson<PlayTimelineRequest>(json);

                if (timelinePlayer == null)
                {
                    FH_OnError("PlayTimeline", "TimelinePlayer not initialized");
                    return;
                }

                if (!timelinePlayer.HasTimeline)
                {
                    FH_OnError("PlayTimeline", "No timeline loaded. Call LoadTimeline first");
                    return;
                }

                currentTimelineCacheId = request.cacheId ?? "";

                // Mute audio if playAudio is false
                if (!request.playAudio)
                {
                    var audioSource = fluentTAvatar.GetComponent<AudioSource>();
                    if (audioSource != null) audioSource.mute = true;
                    timelineAudioMuted = true;
                }

                timelinePlayer.Play();
                Log($"PlayTimeline: playing cacheId={currentTimelineCacheId}, playAudio={request.playAudio}");
            }
            catch (Exception e)
            {
                FH_OnError("PlayTimeline", e.Message);
            }
        }

        // Timeline event handlers

        private void OnTimelinePlaybackStarted()
        {
            Log($"Timeline playback started: {currentTimelineCacheId}");
            FH_OnPlaybackStarted(currentTimelineCacheId ?? "");
        }

        private void OnTimelinePlaybackCompleted()
        {
            // Restore audio if it was muted
            if (timelineAudioMuted)
            {
                var audioSource = fluentTAvatar.GetComponent<AudioSource>();
                if (audioSource != null) audioSource.mute = false;
                timelineAudioMuted = false;
            }

            string completedId = currentTimelineCacheId ?? "";
            currentTimelineCacheId = null;
            Log($"Timeline playback completed: {completedId}");
            FH_OnPlaybackCompleted(completedId);
        }

        private void OnTimelineSentenceStarted(TalkMotionData data)
        {
            string text = data?.text ?? "";
            Log($"Timeline sentence started: {text}");
            FH_OnSentenceStarted(text);
        }

        private void OnTimelineSentenceEnded(TalkMotionData data)
        {
            string text = data?.text ?? "";
            Log($"Timeline sentence ended: {text}");
            FH_OnSentenceEnded(text);
        }

        private void OnTimelineSubtitleStarted(PlayerSubtitleEventData data)
        {
            string text = data?.text ?? "";
            Log($"Timeline subtitle started: {text}");
            FH_OnSubtitleStarted(text);
        }

        private void OnTimelineSubtitleEnded(PlayerSubtitleEventData data)
        {
            string text = data?.text ?? "";
            Log($"Timeline subtitle ended: {text}");
            FH_OnSubtitleEnded(text);
        }

        #endregion

        #region Avatar Callback Handlers

        private void OnAvatarPrepared(string cacheId)
        {
            Log($"Prepared: {cacheId}");
            FH_OnPrepared(cacheId);

            // Auto-play if this was a direct play request (Chat/Speak without prepareOnly)
            if (pendingAutoPlay.Contains(cacheId))
            {
                pendingAutoPlay.Remove(cacheId);
                bool playAudio = true;
                if (pendingPlayAudio.TryGetValue(cacheId, out bool pa))
                {
                    playAudio = pa;
                    pendingPlayAudio.Remove(cacheId);
                }

                Log($"AutoPlay: cacheId={cacheId}, playAudio={playAudio}");
                bool success = fluentTAvatar.PlayPrepared(cacheId, false, playAudio);
                if (success)
                {
                    currentPlayingCacheId = cacheId;
                    FH_OnPlaybackStarted(cacheId);
                }
                else
                {
                    FH_OnError("AutoPlay", $"PlayPrepared failed for cacheId: {cacheId}");
                }
            }
        }

        private void OnAvatarPrepareFailed(string cacheId, string error)
        {
            Log($"Prepare failed: {cacheId} - {error}");

            // Clean up pending auto-play state if this was a Chat/Speak request
            pendingAutoPlay.Remove(cacheId);
            pendingPlayAudio.Remove(cacheId);

            FH_OnPrepareFailed(cacheId, error);
        }

        private void OnAvatarSentenceStarted(TalkMotionData data)
        {
            string text = data?.text ?? "";
            Log($"Sentence started: {text} (isFirstSentence={data?.isFirstSentence})");
            FH_OnSentenceStarted(text);

            // For direct play (StartSpeak/StartChat), fire PlaybackStarted on first sentence
            if (data?.isFirstSentence == true && pendingPlaybackStarted)
            {
                pendingPlaybackStarted = false;
                FH_OnPlaybackStarted(currentPlayingCacheId);
            }
        }

        private void OnAvatarSentenceEnded(TalkMotionData data)
        {
            string text = data?.text ?? "";
            Log($"Sentence ended: {text} (isLastSentence={data?.isLastSentence})");
            FH_OnSentenceEnded(text);

            // Only trigger playback completed after the last sentence
            if (currentPlayingCacheId != null && data != null && data.isLastSentence)
            {
                StartCoroutine(CheckPlaybackCompleted());
            }
        }

        private IEnumerator CheckPlaybackCompleted()
        {
            // Wait 2 frames for any remaining SDK processing
            yield return null;
            yield return null;

            if (currentPlayingCacheId != null)
            {
                string completedId = currentPlayingCacheId;
                currentPlayingCacheId = null;
                Log($"Playback completed: {completedId}");
                FH_OnPlaybackCompleted(completedId);
            }
        }

        private void OnAvatarRequestSent(TalkmotionRequest request, TalkmotionID requestId)
        {
            string id = requestId.Value ?? "";
            Log($"Request sent: {id}");
            FH_OnRequestSent(id);
        }

        private void OnAvatarResponseReceived(TalkMotionData data)
        {
            string cacheId = data?.id.Value ?? "";
            Log($"Response received: {cacheId}");
            FH_OnResponseReceived(cacheId);
        }

        private void OnSubtitleStarted(SubtitleEventData data)
        {
            string text = data?.text ?? "";
            Log($"Subtitle started: {text}");
            FH_OnSubtitleStarted(text);
        }

        private void OnSubtitleEnded(SubtitleEventData data)
        {
            string text = data?.text ?? "";
            Log($"Subtitle ended: {text}");
            FH_OnSubtitleEnded(text);
        }

        #endregion

        #region Logging

        private void Log(string message)
        {
            if (enableLogging)
                Debug.Log($"[FirsthabitBridge] {message}");
        }

        private void LogError(string method, string message)
        {
            Debug.LogError($"[FirsthabitBridge] {method}: {message}");
            FH_OnError(method, message);
        }

        #endregion

        #region DllImport Declarations

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void FH_OnBridgeReady();
        [DllImport("__Internal")] private static extern void FH_OnPrepared(string id);
        [DllImport("__Internal")] private static extern void FH_OnPrepareFailed(string id, string error);
        [DllImport("__Internal")] private static extern void FH_OnPlaybackStarted(string id);
        [DllImport("__Internal")] private static extern void FH_OnPlaybackCompleted(string id);
        [DllImport("__Internal")] private static extern void FH_OnSentenceStarted(string text);
        [DllImport("__Internal")] private static extern void FH_OnSentenceEnded(string text);
        [DllImport("__Internal")] private static extern void FH_OnSubtitleStarted(string text);
        [DllImport("__Internal")] private static extern void FH_OnSubtitleEnded(string text);
        [DllImport("__Internal")] private static extern void FH_OnRequestSent(string id);
        [DllImport("__Internal")] private static extern void FH_OnResponseReceived(string id);
        [DllImport("__Internal")] private static extern void FH_OnVolumeChanged(float volume);
        [DllImport("__Internal")] private static extern void FH_OnCacheInfo(string json);
        [DllImport("__Internal")] private static extern void FH_OnAvatarChanged(string json);
        [DllImport("__Internal")] private static extern void FH_OnAvatarList(string json);
        [DllImport("__Internal")] private static extern void FH_OnError(string method, string message);
        [DllImport("__Internal")] private static extern string FH_CreateAudioBlobUrl(string format);
        [DllImport("__Internal")] private static extern void FH_RevokeAudioBlobUrl(string url);
#else
        // Editor stubs
        private static void FH_OnBridgeReady() => Debug.Log("[FirsthabitBridge] OnBridgeReady");
        private static void FH_OnPrepared(string id) => Debug.Log($"[FirsthabitBridge] OnPrepared: {id}");
        private static void FH_OnPrepareFailed(string id, string error) => Debug.LogError($"[FirsthabitBridge] OnPrepareFailed: {id} - {error}");
        private static void FH_OnPlaybackStarted(string id) => Debug.Log($"[FirsthabitBridge] OnPlaybackStarted: {id}");
        private static void FH_OnPlaybackCompleted(string id) => Debug.Log($"[FirsthabitBridge] OnPlaybackCompleted: {id}");
        private static void FH_OnSentenceStarted(string text) => Debug.Log($"[FirsthabitBridge] OnSentenceStarted: {text}");
        private static void FH_OnSentenceEnded(string text) => Debug.Log($"[FirsthabitBridge] OnSentenceEnded: {text}");
        private static void FH_OnSubtitleStarted(string text) => Debug.Log($"[FirsthabitBridge] OnSubtitleStarted: {text}");
        private static void FH_OnSubtitleEnded(string text) => Debug.Log($"[FirsthabitBridge] OnSubtitleEnded: {text}");
        private static void FH_OnRequestSent(string id) => Debug.Log($"[FirsthabitBridge] OnRequestSent: {id}");
        private static void FH_OnResponseReceived(string id) => Debug.Log($"[FirsthabitBridge] OnResponseReceived: {id}");
        private static void FH_OnVolumeChanged(float volume) => Debug.Log($"[FirsthabitBridge] OnVolumeChanged: {volume}");
        private static void FH_OnCacheInfo(string json) => Debug.Log($"[FirsthabitBridge] OnCacheInfo: {json}");
        private static void FH_OnAvatarChanged(string json) => Debug.Log($"[FirsthabitBridge] OnAvatarChanged: {json}");
        private static void FH_OnAvatarList(string json) => Debug.Log($"[FirsthabitBridge] OnAvatarList: {json}");
        private static void FH_OnError(string method, string message) => Debug.LogError($"[FirsthabitBridge] Error in {method}: {message}");
        private static string FH_CreateAudioBlobUrl(string format) { Debug.Log("[FirsthabitBridge] CreateAudioBlobUrl (Editor)"); return ""; }
        private static void FH_RevokeAudioBlobUrl(string url) { }
#endif

        #endregion
    }
}
#endif
