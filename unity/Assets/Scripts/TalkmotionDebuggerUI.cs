using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FluentT.Talkmotion;
using FluentT.Talkmotion.Timeline.UnityTimeline;
using FluentT.APIClient.V3;

namespace Firsthabit.WebGL
{
    /// <summary>
    /// OnGUI-based debugger UI for testing FluentTAvatar TalkMotion features.
    /// Provides avatar selection, API testing, playback control, cache management, and callback logging.
    /// </summary>
    public class TalkmotionDebuggerUI : MonoBehaviour
    {
        #region Settings

        [Header("UI Settings")]
        [Tooltip("Panel width in pixels")]
        [SerializeField] private float panelWidth = 220f;

        [Tooltip("Enable keyboard shortcuts (A/D or Arrow keys for avatar, W/S for zoom)")]
        [SerializeField] private bool enableKeyboardShortcuts = true;

        [Header("Camera Settings")]
        [Tooltip("Enable automatic camera tracking of selected avatar")]
        [SerializeField] private bool enableCameraTracking = true;

        [Tooltip("Camera offset from head bone")]
        [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 0f, 0.6f);

        [Tooltip("Camera zoom speed")]
        [SerializeField] private float zoomSpeed = 0.05f;

        [Tooltip("Camera follow smoothing")]
        [SerializeField] private float cameraSmoothSpeed = 5f;

        [Header("API Test Defaults")]
        [SerializeField] private string speakText = "이것은 음성 합성 테스트입니다.";

        [Header("Log Settings")]
        [Tooltip("Maximum log entries to keep")]
        [SerializeField] private int maxLogEntries = 50;

        #endregion

        #region Private Fields

        // Avatar management
        private FluentTAvatar[] avatars;
        private int selectedAvatarIndex = 0;
        private FluentTAvatar selectedAvatar;

        // Camera
        private Camera mainCamera;
        private float currentZoomOffset = 0f;

        // UI state
        private string inputText = "";
        private string cacheIdInput = "";
        private Vector2 logScrollPos;
        private bool showUI = true;

        // Callback log
        private readonly List<string> logEntries = new();

        // Registered avatar for callbacks (to properly unsubscribe)
        private FluentTAvatar registeredAvatar;

        // GUI styles (lazy initialized)
        private GUIStyle headerStyle;
        private GUIStyle logStyle;
        private GUIStyle panelStyle;
        private bool stylesInitialized;

        #endregion

        #region Lifecycle

        private void Start()
        {
            mainCamera = Camera.main;
            RefreshAvatarList();
            if (avatars.Length > 0)
            {
                SelectAvatar(0);
            }

            inputText = speakText;
        }

        private void Update()
        {
            if (!enableKeyboardShortcuts) return;

            // Toggle UI
            if (Input.GetKeyDown(KeyCode.F1))
            {
                showUI = !showUI;
            }

            if (!showUI) return;

            // Avatar navigation: A/D or Left/Right
            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
            {
                SelectAvatar(selectedAvatarIndex - 1);
            }
            if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
            {
                SelectAvatar(selectedAvatarIndex + 1);
            }

            // Zoom: W/S or Up/Down
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
            {
                currentZoomOffset -= zoomSpeed * Time.deltaTime;
            }
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
            {
                currentZoomOffset += zoomSpeed * Time.deltaTime;
            }
        }

        private void LateUpdate()
        {
            if (!enableCameraTracking || mainCamera == null || selectedAvatar == null) return;

            var headBone = FindHeadBone(selectedAvatar.transform);
            if (headBone == null) return;

            var targetPos = headBone.position + headBone.forward * (cameraOffset.z + currentZoomOffset)
                                               + headBone.up * cameraOffset.y
                                               + headBone.right * cameraOffset.x;

            mainCamera.transform.position = Vector3.Lerp(
                mainCamera.transform.position, targetPos, cameraSmoothSpeed * Time.deltaTime);
            mainCamera.transform.LookAt(headBone.position);
        }

        private void OnDestroy()
        {
            UnregisterCallbacks();
        }

        #endregion

        #region Avatar Management

        private void RefreshAvatarList()
        {
            avatars = FindObjectsByType<FluentTAvatar>(FindObjectsSortMode.None)
                .OrderBy(a => GetHierarchyOrder(a.transform))
                .ToArray();
        }

        /// <summary>
        /// Build a sortable hierarchy path so avatars are ordered top-to-bottom as in the Scene Hierarchy.
        /// Each level uses the sibling index zero-padded to 5 digits (supports up to 99999 siblings).
        /// </summary>
        private static string GetHierarchyOrder(Transform t)
        {
            var parts = new List<string>();
            var current = t;
            while (current != null)
            {
                parts.Add(current.GetSiblingIndex().ToString("D5"));
                current = current.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private void SelectAvatar(int index)
        {
            if (avatars == null || avatars.Length == 0) return;

            // Wrap around
            if (index < 0) index = avatars.Length - 1;
            if (index >= avatars.Length) index = 0;

            selectedAvatarIndex = index;
            selectedAvatar = avatars[selectedAvatarIndex];

            // Re-register callbacks
            RegisterCallbacks(selectedAvatar);

            AddLog($"Selected: {selectedAvatar.gameObject.name}");
        }

        private Transform FindHeadBone(Transform root)
        {
            var animator = root.GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null) return head;
            }

            // Fallback: search by name
            foreach (var t in root.GetComponentsInChildren<Transform>())
            {
                var name = t.name.ToLower();
                if (name.Contains("head") && !name.Contains("headtop"))
                    return t;
            }

            return root;
        }

        #endregion

        #region Callback Management

        private void RegisterCallbacks(FluentTAvatar avatar)
        {
            UnregisterCallbacks();

            if (avatar == null) return;
            registeredAvatar = avatar;

            avatar.onPrepared.AddListener(OnPrepared);
            avatar.onPrepareFailed.AddListener(OnPrepareFailed);
            avatar.callback.onSentenceStarted.AddListener(OnSentenceStarted);
            avatar.callback.onSentenceEnded.AddListener(OnSentenceEnded);
            avatar.OnSubtitleTextStarted.AddListener(OnSubtitleStarted);
            avatar.OnSubtitleTextEnded.AddListener(OnSubtitleEnded);
            avatar.callback.onTalkmotionRequestSent.AddListener(OnRequestSent);
            avatar.callback.onTalkmotionResponseReceived.AddListener(OnResponseReceived);
        }

        private void UnregisterCallbacks()
        {
            if (registeredAvatar == null) return;

            registeredAvatar.onPrepared.RemoveListener(OnPrepared);
            registeredAvatar.onPrepareFailed.RemoveListener(OnPrepareFailed);
            registeredAvatar.callback.onSentenceStarted.RemoveListener(OnSentenceStarted);
            registeredAvatar.callback.onSentenceEnded.RemoveListener(OnSentenceEnded);
            registeredAvatar.OnSubtitleTextStarted.RemoveListener(OnSubtitleStarted);
            registeredAvatar.OnSubtitleTextEnded.RemoveListener(OnSubtitleEnded);
            registeredAvatar.callback.onTalkmotionRequestSent.RemoveListener(OnRequestSent);
            registeredAvatar.callback.onTalkmotionResponseReceived.RemoveListener(OnResponseReceived);

            registeredAvatar = null;
        }

        #endregion

        #region Callback Handlers

        private void OnPrepared(string cacheId)
        {
            cacheIdInput = cacheId;
            AddLog($"[Prepared] {cacheId}");
        }

        private void OnPrepareFailed(string cacheId, string error)
        {
            AddLog($"[PrepareFailed] {cacheId}: {error}");
        }

        private void OnSentenceStarted(TalkMotionData data)
        {
            AddLog($"[SentenceStart] seq={data.sequenceNumber} \"{Truncate(data.text, 30)}\"");
        }

        private void OnSentenceEnded(TalkMotionData data)
        {
            AddLog($"[SentenceEnd] seq={data.sequenceNumber}");
        }

        private void OnSubtitleStarted(SubtitleEventData data)
        {
            AddLog($"[SubStart] [{data.index}/{data.total}] \"{Truncate(data.text, 30)}\"");
        }

        private void OnSubtitleEnded(SubtitleEventData data)
        {
            AddLog($"[SubEnd] [{data.index}/{data.total}]");
        }

        private void OnRequestSent(TalkmotionRequest request, TalkmotionID id)
        {
            AddLog($"[ReqSent] {id}");
        }

        private void OnResponseReceived(TalkMotionData data)
        {
            if (data != null)
                AddLog($"[RespRecv] id={data.id}, hasAudio={data.audioClip != null}");
        }

        #endregion

        #region Logging

        private void AddLog(string message)
        {
            string timestamp = System.DateTime.Now.ToString("HH:mm:ss.ff");
            logEntries.Add($"[{timestamp}] {message}");

            if (logEntries.Count > maxLogEntries)
                logEntries.RemoveAt(0);

            // Auto-scroll to bottom
            logScrollPos.y = float.MaxValue;
        }

        private static string Truncate(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "...";
        }

        #endregion

        #region OnGUI

        private void InitStyles()
        {
            if (stylesInitialized) return;

            headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12
            };

            logStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                wordWrap = true,
                richText = false
            };

            panelStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(6, 6, 4, 4)
            };

            stylesInitialized = true;
        }

        private void OnGUI()
        {
            if (!showUI) return;
            InitStyles();

            float x = 4f;
            float y = 4f;
            float w = panelWidth;

            GUILayout.BeginArea(new Rect(x, y, w, Screen.height - 8f), panelStyle);
            GUILayout.BeginVertical();

            // Title
            GUILayout.Label("TalkMotion Debugger", headerStyle);
            GUILayout.Label("(F1 to toggle)", GUI.skin.label);
            GUILayout.Space(4);

            DrawAvatarSelector();
            GUILayout.Space(6);
            DrawApiTestSection();
            GUILayout.Space(6);
            DrawPlaybackSection();
            GUILayout.Space(6);
            DrawCacheSection();
            GUILayout.Space(6);
            DrawLogSection();

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void DrawAvatarSelector()
        {
            GUILayout.Label("Avatar", headerStyle);

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("<", GUILayout.Width(24)))
                SelectAvatar(selectedAvatarIndex - 1);

            string label = avatars != null && avatars.Length > 0
                ? $"{selectedAvatarIndex + 1}/{avatars.Length}: {selectedAvatar?.gameObject.name ?? "?"}"
                : "No avatars";
            GUILayout.Label(label, GUI.skin.box, GUILayout.ExpandWidth(true));

            if (GUILayout.Button(">", GUILayout.Width(24)))
                SelectAvatar(selectedAvatarIndex + 1);

            GUILayout.EndHorizontal();

            if (GUILayout.Button("Refresh Avatars"))
            {
                RefreshAvatarList();
                if (avatars.Length > 0) SelectAvatar(0);
            }
        }

        private void DrawApiTestSection()
        {
            GUILayout.Label("Speak", headerStyle);

            GUILayout.Label("Text:", GUI.skin.label);
            inputText = GUILayout.TextField(inputText, GUILayout.Height(20));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Speak"))
            {
                if (selectedAvatar != null && !string.IsNullOrWhiteSpace(inputText))
                {
                    var id = selectedAvatar.StartSpeak(inputText);
                    AddLog($"StartSpeak -> {id}");
                }
            }
            if (GUILayout.Button("Stop"))
            {
                if (selectedAvatar != null)
                {
                    selectedAvatar.StopTalkMotion();
                    AddLog("StopTalkMotion");
                }
            }
            GUILayout.EndHorizontal();
        }

        private void DrawPlaybackSection()
        {
            GUILayout.Label("Playback", headerStyle);

            GUILayout.Label("Cache ID:", GUI.skin.label);
            cacheIdInput = GUILayout.TextField(cacheIdInput, GUILayout.Height(20));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Play"))
            {
                if (selectedAvatar != null && !string.IsNullOrEmpty(cacheIdInput))
                {
                    bool ok = selectedAvatar.PlayPrepared(cacheIdInput);
                    AddLog($"PlayPrepared({cacheIdInput}) -> {ok}");
                }
            }
            if (GUILayout.Button("Stop"))
            {
                if (selectedAvatar != null)
                {
                    selectedAvatar.StopTalkMotion();
                    AddLog("StopTalkMotion");
                }
            }
            GUILayout.EndHorizontal();

            // Volume control
            GUILayout.BeginHorizontal();
            GUILayout.Label("Vol:", GUILayout.Width(28));
            float vol = selectedAvatar != null ? selectedAvatar.GetVolume() : 1f;
            float newVol = GUILayout.HorizontalSlider(vol, 0f, 1f);
            if (selectedAvatar != null && !Mathf.Approximately(vol, newVol))
            {
                selectedAvatar.SetVolume(newVol);
            }
            GUILayout.Label($"{newVol:F1}", GUILayout.Width(24));
            GUILayout.EndHorizontal();
        }

        private void DrawCacheSection()
        {
            GUILayout.Label("Cache", headerStyle);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Info"))
            {
                if (selectedAvatar != null)
                {
                    int count = selectedAvatar.CacheCount;
                    string[] ids = selectedAvatar.GetCachedIds();
                    AddLog($"Cache: {count} items");
                    foreach (var id in ids)
                    {
                        AddLog($"  - {id}");
                    }
                }
            }
            if (GUILayout.Button("Clear All"))
            {
                if (selectedAvatar != null)
                {
                    selectedAvatar.ClearCache(null);
                    AddLog("Cache cleared");
                }
            }
            GUILayout.EndHorizontal();
        }

        private void DrawLogSection()
        {
            GUILayout.Label("Log", headerStyle);

            if (GUILayout.Button("Clear Log"))
            {
                logEntries.Clear();
            }

            // Calculate remaining height for log area
            float logHeight = Mathf.Max(120f, Screen.height * 0.3f);

            logScrollPos = GUILayout.BeginScrollView(logScrollPos, GUI.skin.box, GUILayout.Height(logHeight));

            foreach (var entry in logEntries)
            {
                GUILayout.Label(entry, logStyle);
            }

            GUILayout.EndScrollView();
        }

        #endregion
    }
}
