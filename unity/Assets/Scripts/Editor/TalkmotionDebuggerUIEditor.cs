using UnityEngine;
using UnityEditor;

namespace Firsthabit.WebGL
{
    [CustomEditor(typeof(TalkmotionDebuggerUI))]
    public class TalkmotionDebuggerUIEditor : Editor
    {
        private bool showAvatarControls = true;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var debugger = (TalkmotionDebuggerUI)target;

            if (!Application.isPlaying) return;

            EditorGUILayout.Space(10);
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.3f, 0.3f, 0.3f));
            EditorGUILayout.Space(4);

            DrawAvatarControls(debugger);
        }

        private void DrawAvatarControls(TalkmotionDebuggerUI debugger)
        {
            showAvatarControls = EditorGUILayout.BeginFoldoutHeaderGroup(
                showAvatarControls, "Avatar Selection");

            if (showAvatarControls)
            {
                var avatars = debugger.Avatars;

                if (avatars == null || avatars.Length == 0)
                {
                    EditorGUILayout.HelpBox("No avatars found in scene.", MessageType.Info);
                    if (GUILayout.Button("Refresh"))
                    {
                        debugger.EditorRefreshAvatarList();
                    }
                }
                else
                {
                    // Build display names
                    var names = new string[avatars.Length];
                    for (int i = 0; i < avatars.Length; i++)
                    {
                        names[i] = avatars[i] != null ? avatars[i].gameObject.name : "(missing)";
                    }

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PrefixLabel($"Avatar ({avatars.Length})");

                    int newIndex = EditorGUILayout.Popup(debugger.SelectedAvatarIndex, names);
                    if (newIndex != debugger.SelectedAvatarIndex)
                    {
                        debugger.EditorSelectAvatar(newIndex);
                    }
                    EditorGUILayout.EndHorizontal();

                    // Prev / Next buttons
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("\u25c0 Prev", GUILayout.Height(24)))
                    {
                        debugger.EditorSelectAvatar(debugger.SelectedAvatarIndex - 1);
                    }
                    if (GUILayout.Button("Next \u25b6", GUILayout.Height(24)))
                    {
                        debugger.EditorSelectAvatar(debugger.SelectedAvatarIndex + 1);
                    }
                    EditorGUILayout.EndHorizontal();

                    if (GUILayout.Button("Refresh Avatar List"))
                    {
                        debugger.EditorRefreshAvatarList();
                    }
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying;
        }
    }
}
