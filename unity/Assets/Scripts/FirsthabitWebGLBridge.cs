#if UNITY_WEBGL
using System;
using System.Collections;
using System.Runtime.InteropServices;
using FluentT.APIClient.V3;
using FluentT.Talkmotion;
using FluentT.Talkmotion.Timeline.UnityTimeline;
using UnityEngine;
using UnityEngine.Networking;

namespace Firsthabit.WebGL
{
    public class FirsthabitWebGLBridge : MonoBehaviour
    {
        #region Serialized Fields

        [SerializeField] private FluentTAvatar fluentTAvatar;
        [SerializeField] private bool enableLogging = true;

        #endregion

        #region Data Classes

        [Serializable]
        private class PrepareRequest
        {
            public string format = "wav";
        }

        [Serializable]
        private class PlayRequest
        {
            public string cacheId = "";
            public bool playAudio = true;
        }

        #endregion

        #region State

        private string currentPlayingCacheId;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
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
        }

        private void Start()
        {
            if (fluentTAvatar == null) return;

            RegisterCallbacks();
            FH_OnBridgeReady();
            Log("Bridge ready");
        }

        private void OnDestroy()
        {
            UnregisterCallbacks();
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

            fluentTAvatar.onPrepared.RemoveListener(OnAvatarPrepared);
            fluentTAvatar.onPrepareFailed.RemoveListener(OnAvatarPrepareFailed);
            fluentTAvatar.callback.onSentenceStarted.RemoveListener(OnAvatarSentenceStarted);
            fluentTAvatar.callback.onSentenceEnded.RemoveListener(OnAvatarSentenceEnded);
            fluentTAvatar.callback.onTalkmotionRequestSent.RemoveListener(OnAvatarRequestSent);
            fluentTAvatar.callback.onTalkmotionResponseReceived.RemoveListener(OnAvatarResponseReceived);
            fluentTAvatar.OnSubtitleTextStarted.RemoveListener(OnSubtitleStarted);
            fluentTAvatar.OnSubtitleTextEnded.RemoveListener(OnSubtitleEnded);
        }

        #endregion

        #region Flutter → Unity Methods (called via SendMessage)

        /// <summary>
        /// Prepare audio: reads base64 from JS, creates AudioClip via blob URL,
        /// then calls PrepareSpeechMotion to send to server and cache.
        /// JSON: { "format": "wav" }
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
                    string cacheId = fluentTAvatar.PrepareSpeechMotion(clip, "", null);
                    Log($"PrepareAudio: PrepareSpeechMotion called, cacheId={cacheId}");
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
        /// Clear all cached motion data
        /// </summary>
        public void ClearAllCache()
        {
            try
            {
                fluentTAvatar.ClearCache(null);
                Log("All cache cleared");
            }
            catch (Exception e)
            {
                FH_OnError("ClearAllCache", e.Message);
            }
        }

        /// <summary>
        /// Change avatar (TODO - Addressable)
        /// </summary>
        public void ChangeAvatar(string key)
        {
            Log($"ChangeAvatar: {key} (not yet implemented)");
            FH_OnError("ChangeAvatar", "Not yet implemented");
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

        #region Avatar Callback Handlers

        private void OnAvatarPrepared(string cacheId)
        {
            Log($"Prepared: {cacheId}");
            FH_OnPrepared(cacheId);
        }

        private void OnAvatarPrepareFailed(string cacheId, string error)
        {
            Log($"Prepare failed: {cacheId} - {error}");
            FH_OnPrepareFailed(cacheId, error);
        }

        private void OnAvatarSentenceStarted(TalkMotionData data)
        {
            string text = data?.text ?? "";
            Log($"Sentence started: {text}");
            FH_OnSentenceStarted(text);
        }

        private void OnAvatarSentenceEnded(TalkMotionData data)
        {
            string text = data?.text ?? "";
            Log($"Sentence ended: {text}");
            FH_OnSentenceEnded(text);

            if (currentPlayingCacheId != null)
            {
                StartCoroutine(CheckPlaybackCompleted());
            }
        }

        private IEnumerator CheckPlaybackCompleted()
        {
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
        private static void FH_OnError(string method, string message) => Debug.LogError($"[FirsthabitBridge] Error in {method}: {message}");
        private static string FH_CreateAudioBlobUrl(string format) { Debug.Log("[FirsthabitBridge] CreateAudioBlobUrl (Editor)"); return ""; }
        private static void FH_RevokeAudioBlobUrl(string url) { }
#endif

        #endregion
    }
}
#endif
