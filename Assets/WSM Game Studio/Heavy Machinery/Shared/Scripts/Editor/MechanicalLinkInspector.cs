using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace WSMGameStudio.HeavyMachinery
{
    [CustomEditor(typeof(MechanicalLink))]
    public class MechanicalLinkInspector : UnityEditor.Editor
    {
        private MechanicalLink _mechanicalLink;

        //Properties
        protected SerializedProperty _linkRotation;
        protected SerializedProperty _linkPosition;
        protected SerializedProperty _linkTarget;
        protected SerializedProperty _link;
        protected SerializedProperty _xPosConstraint;
        protected SerializedProperty _yPosConstraint;
        protected SerializedProperty _zPosConstraint;

        private GUIStyle _menuBoxStyle;

        public override void OnInspectorGUI()
        {
            //base.OnInspectorGUI();
            _mechanicalLink = target as MechanicalLink;

            //Set up the box style if null
            if (_menuBoxStyle == null)
            {
                _menuBoxStyle = new GUIStyle(GUI.skin.box);
                _menuBoxStyle.normal.textColor = GUI.skin.label.normal.textColor;
                _menuBoxStyle.fontStyle = FontStyle.Bold;
                _menuBoxStyle.alignment = TextAnchor.UpperLeft;
            }
            GUILayout.BeginVertical(_menuBoxStyle);

            EditorGUILayout.Space();
            GUILayout.Label("SETTINGS", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            serializedObject.Update();

            _linkRotation = serializedObject.FindProperty("linkRotation");
            _linkPosition = serializedObject.FindProperty("linkPosition");
            _linkTarget = serializedObject.FindProperty("linkTarget");
            _link = serializedObject.FindProperty("link");
            _xPosConstraint = serializedObject.FindProperty("xPosConstraint");
            _yPosConstraint = serializedObject.FindProperty("yPosConstraint");
            _zPosConstraint = serializedObject.FindProperty("zPosConstraint");

            EditorGUILayout.PropertyField(_linkRotation);
            EditorGUILayout.PropertyField(_linkPosition);
            EditorGUILayout.PropertyField(_linkTarget);
            EditorGUILayout.PropertyField(_link);
            EditorGUILayout.LabelField("Position Constraints");
            //GUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(_xPosConstraint, new GUIContent("x"));
            EditorGUILayout.PropertyField(_yPosConstraint, new GUIContent("y"));
            EditorGUILayout.PropertyField(_zPosConstraint, new GUIContent("z"));
            //GUILayout.EndHorizontal();

            if (serializedObject.hasModifiedProperties)
                serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            GUILayout.Label("OPERATIONS", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            if (GUILayout.Button("Link to Target"))
            {
                _mechanicalLink.LinkToTarget();
                MarkSceneAlteration();
            }

            GUILayout.EndVertical();
        }

        private void MarkSceneAlteration()
        {
            if (!Application.isPlaying)
            {
#if UNITY_EDITOR
                EditorUtility.SetDirty(_mechanicalLink);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
#endif
            }
        }
    } 
}
