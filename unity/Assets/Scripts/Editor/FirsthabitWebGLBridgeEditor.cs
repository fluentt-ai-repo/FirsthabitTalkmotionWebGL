#if UNITY_WEBGL
using UnityEngine;
using UnityEditor;

namespace Firsthabit.WebGL
{
    [CustomEditor(typeof(FirsthabitWebGLBridge))]
    public class FirsthabitWebGLBridgeEditor : Editor
    {
        // Serialized properties
        private SerializedProperty fluentTAvatarProp;
        private SerializedProperty enableLoggingProp;
        private SerializedProperty avatarCatalogProp;
        private SerializedProperty editorTestAvatarIdProp;

        // Foldout states
        private bool showCoreSettings = true;
        private bool showAvatarManagement = true;
        private bool showEditorControls = true;
        private bool showRuntimeStatus = true;

        // Style cache
        private static GUIStyle headerStyle;
        private static GUIStyle statusBoxStyle;
        private static GUIStyle statusLabelStyle;

        private void OnEnable()
        {
            fluentTAvatarProp = serializedObject.FindProperty("fluentTAvatar");
            enableLoggingProp = serializedObject.FindProperty("enableLogging");
            avatarCatalogProp = serializedObject.FindProperty("avatarCatalog");
            editorTestAvatarIdProp = serializedObject.FindProperty("editorTestAvatarId");
        }

        private static void InitStyles()
        {
            if (headerStyle != null) return;

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                fixedHeight = 24
            };

            statusBoxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 8, 8)
            };

            statusLabelStyle = new GUIStyle(EditorStyles.label)
            {
                richText = true
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            InitStyles();

            var bridge = (FirsthabitWebGLBridge)target;

            DrawBridgeHeader();
            EditorGUILayout.Space(4);

            DrawCoreSettings();
            EditorGUILayout.Space(2);
            DrawAvatarManagement(bridge);

            if (Application.isPlaying)
            {
                EditorGUILayout.Space(2);
                DrawRuntimeStatus(bridge);
                EditorGUILayout.Space(2);
                DrawEditorControls(bridge);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawBridgeHeader()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Firsthabit WebGL Bridge", headerStyle);

            if (Application.isPlaying)
            {
                var bridge = (FirsthabitWebGLBridge)target;
                var hasAvatar = bridge.CurrentFluentTAvatar != null;
                var dot = hasAvatar ? "\u25cf ACTIVE" : "\u25cb STANDBY";
                var color = hasAvatar ? "#4CAF50" : "#FF9800";
                EditorGUILayout.LabelField($"<color={color}>{dot}</color>", statusLabelStyle,
                    GUILayout.Width(80));
            }

            EditorGUILayout.EndHorizontal();

            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.3f, 0.3f, 0.3f));
        }

        private void DrawCoreSettings()
        {
            showCoreSettings = EditorGUILayout.BeginFoldoutHeaderGroup(showCoreSettings, "Core Settings");
            if (showCoreSettings)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(fluentTAvatarProp, new GUIContent("FluentT Avatar",
                    "Assign directly or leave empty for runtime creation"));
                EditorGUILayout.PropertyField(enableLoggingProp, new GUIContent("Enable Logging"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawAvatarManagement(FirsthabitWebGLBridge bridge)
        {
            showAvatarManagement = EditorGUILayout.BeginFoldoutHeaderGroup(showAvatarManagement,
                "Avatar Catalog");
            if (showAvatarManagement)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(avatarCatalogProp, new GUIContent("Catalog",
                    "ScriptableObject containing avatar prefab entries"));

                if (Application.isPlaying)
                {
                    EditorGUILayout.LabelField("Loaded Prefabs", bridge.AvatarPrefabCount.ToString());
                }

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawRuntimeStatus(FirsthabitWebGLBridge bridge)
        {
            showRuntimeStatus = EditorGUILayout.BeginFoldoutHeaderGroup(showRuntimeStatus,
                "Runtime Status");
            if (showRuntimeStatus)
            {
                EditorGUILayout.BeginVertical(statusBoxStyle);

                var avatar = bridge.CurrentFluentTAvatar;
                var avatarId = bridge.CurrentAvatarId ?? "(none)";
                var instance = bridge.CurrentAvatarInstance;

                StatusRow("Avatar ID", avatarId);
                StatusRow("FluentTAvatar", avatar != null ? avatar.name : "<color=#F44336>Not Created</color>");
                StatusRow("Instance", instance != null ? instance.name : "<color=#999>None</color>");
                StatusRow("Registered Prefabs", bridge.AvatarPrefabCount.ToString());

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawEditorControls(FirsthabitWebGLBridge bridge)
        {
            showEditorControls = EditorGUILayout.BeginFoldoutHeaderGroup(showEditorControls,
                "Editor Test Controls");
            if (showEditorControls)
            {
                EditorGUILayout.BeginVertical(statusBoxStyle);

                // Avatar dropdown
                var ids = bridge.AvailableAvatarIds;
                if (ids.Length > 0)
                {
                    var currentVal = editorTestAvatarIdProp.stringValue;
                    int selectedIdx = System.Array.IndexOf(ids, currentVal);
                    if (selectedIdx < 0) selectedIdx = 0;

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PrefixLabel("Target Avatar");
                    int newIdx = EditorGUILayout.Popup(selectedIdx, ids);
                    EditorGUILayout.EndHorizontal();

                    if (newIdx != selectedIdx || string.IsNullOrEmpty(currentVal))
                    {
                        editorTestAvatarIdProp.stringValue = ids[newIdx];
                        serializedObject.ApplyModifiedProperties();
                    }

                    EditorGUILayout.Space(4);

                    // Create / Change Avatar button
                    var avatarId = ids[newIdx];
                    var isSame = avatarId == bridge.CurrentAvatarId;
                    var buttonLabel = bridge.CurrentFluentTAvatar == null
                        ? $"Create Avatar ({avatarId})"
                        : isSame
                            ? $"Already Active: {avatarId}"
                            : $"Change to: {avatarId}";

                    EditorGUI.BeginDisabledGroup(isSame);
                    if (GUILayout.Button(buttonLabel, GUILayout.Height(28)))
                    {
                        bridge.ChangeAvatar(avatarId);
                    }
                    EditorGUI.EndDisabledGroup();
                }
                else
                {
                    EditorGUILayout.HelpBox("No avatar prefabs registered in Avatar Prefabs section.",
                        MessageType.Warning);
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void StatusRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(130));
            EditorGUILayout.LabelField(value, statusLabelStyle);
            EditorGUILayout.EndHorizontal();
        }

        // Continuously repaint during play mode for live status updates
        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying;
        }
    }
}
#endif
