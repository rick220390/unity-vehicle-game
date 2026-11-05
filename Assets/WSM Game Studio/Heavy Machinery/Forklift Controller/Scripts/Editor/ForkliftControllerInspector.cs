using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace WSMGameStudio.HeavyMachinery
{
    [CustomEditor(typeof(ForkliftController))]
    public class ForkliftControllerInspector : UnityEditor.Editor
    {
        private ForkliftController _forkliftController;

        protected SerializedProperty _mainMast;
        protected SerializedProperty _secondaryMast;
        protected SerializedProperty _loadBackrestCylinders;
        protected SerializedProperty _loadBackrest;
        protected SerializedProperty _fork_L;
        protected SerializedProperty _fork_R;
        protected SerializedProperty _forksVerticalLever;
        protected SerializedProperty _forksHorizontalLever;
        protected SerializedProperty _forksWidthLever;
        protected SerializedProperty _mastTiltLever;
        protected SerializedProperty _forkMovingSFX;
        protected SerializedProperty _forkStartMovingSFX;
        protected SerializedProperty _forkStopMovingSFX;

        private int _selectedMenuIndex = 0;
        private string[] _toolbarMenuOptions = new[] { "Settings", "Mechanical Parts", "SFX" };
        private GUIStyle _menuBoxStyle;

        public override void OnInspectorGUI()
        {
            //base.OnInspectorGUI();

            _forkliftController = target as ForkliftController;

            EditorGUI.BeginChangeCheck();
            _selectedMenuIndex = GUILayout.Toolbar(_selectedMenuIndex, _toolbarMenuOptions);
            if (EditorGUI.EndChangeCheck())
            {
                GUI.FocusControl(null);
            }

            //Set up the box style if null
            if (_menuBoxStyle == null)
            {
                _menuBoxStyle = new GUIStyle(GUI.skin.box);
                _menuBoxStyle.normal.textColor = GUI.skin.label.normal.textColor;
                _menuBoxStyle.fontStyle = FontStyle.Bold;
                _menuBoxStyle.alignment = TextAnchor.UpperLeft;
            }
            
            GUILayout.BeginVertical(_menuBoxStyle);

            if (_toolbarMenuOptions[_selectedMenuIndex] == "Settings")
            {
                /*
                 * SETTINGS
                 */
                GUILayout.Label("SETTINGS", EditorStyles.boldLabel);

                EditorGUI.BeginChangeCheck();
                float forksVerticalSpeed = EditorGUILayout.FloatField("Forks Vertical Speed", _forkliftController.forksVerticalSpeed);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_forkliftController, "Changed Forks Vertical Speed");
                    _forkliftController.forksVerticalSpeed = forksVerticalSpeed;
                    MarkSceneAlteration();
                }

                EditorGUI.BeginChangeCheck();
                float forksHorizontalSpeed = EditorGUILayout.FloatField("Forks Horizontal Speed", _forkliftController.forksHorizontalSpeed);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_forkliftController, "Changed Forks Horizontal Speed");
                    _forkliftController.forksHorizontalSpeed = forksHorizontalSpeed;
                    MarkSceneAlteration();
                }

                EditorGUI.BeginChangeCheck();
                float widthAdjustmentSpeed = EditorGUILayout.FloatField("Width Adjustment Speed", _forkliftController.widthAdjustmentSpeed);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_forkliftController, "Changed Forks Width Adjustment Speed");
                    _forkliftController.widthAdjustmentSpeed = widthAdjustmentSpeed;
                    MarkSceneAlteration();
                }

                EditorGUI.BeginChangeCheck();
                float mastTiltSpeed = EditorGUILayout.FloatField("Mast Tilt Speed", _forkliftController.mastTiltSpeed);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_forkliftController, "Changed Mast Tilt Speed");
                    _forkliftController.mastTiltSpeed = mastTiltSpeed;
                    MarkSceneAlteration();
                }
            }
            else if (_toolbarMenuOptions[_selectedMenuIndex] == "Mechanical Parts")
            {
                /*
                 * MECHANICAL PARTS
                 */
                GUILayout.Label("MECHANICAL PARTS", EditorStyles.boldLabel);

                serializedObject.Update();

                _mainMast = serializedObject.FindProperty("mainMast");
                _secondaryMast = serializedObject.FindProperty("secondaryMast");
                _loadBackrestCylinders = serializedObject.FindProperty("loadBackrestCylinders");
                _loadBackrest = serializedObject.FindProperty("loadBackrest");
                _fork_L = serializedObject.FindProperty("fork_L");
                _fork_R = serializedObject.FindProperty("fork_R");
                _forksVerticalLever = serializedObject.FindProperty("forksVerticalLever");
                _forksHorizontalLever = serializedObject.FindProperty("forksHorizontalLever");
                _forksWidthLever = serializedObject.FindProperty("forksWidthLever");
                _mastTiltLever = serializedObject.FindProperty("mastTiltLever");

                EditorGUILayout.PropertyField(_mainMast, new GUIContent("Main Mast"));
                EditorGUILayout.PropertyField(_secondaryMast, new GUIContent("Secondary Mast"));
                EditorGUILayout.PropertyField(_loadBackrestCylinders, new GUIContent("Load Backrest Cylinders"));
                EditorGUILayout.PropertyField(_loadBackrest, new GUIContent("Load Backrest"));
                EditorGUILayout.PropertyField(_fork_L, new GUIContent("Fork L"));
                EditorGUILayout.PropertyField(_fork_R, new GUIContent("Fork R"));

                GUILayout.Label("LEVERS", EditorStyles.boldLabel);

                EditorGUILayout.PropertyField(_forksVerticalLever, new GUIContent("Forks Vertical Lever"));
                EditorGUILayout.PropertyField(_forksHorizontalLever, new GUIContent("Forks Horizontal Lever"));
                EditorGUILayout.PropertyField(_forksWidthLever, new GUIContent("Forks Width Lever"));
                EditorGUILayout.PropertyField(_mastTiltLever, new GUIContent("Mast Tilt Lever"));

                serializedObject.ApplyModifiedProperties();
            }
            else if (_toolbarMenuOptions[_selectedMenuIndex] == "SFX")
            {
                /*
                 * SFX
                 */
                serializedObject.Update();

                _forkMovingSFX = serializedObject.FindProperty("forkMovingSFX");
                _forkStartMovingSFX = serializedObject.FindProperty("forkStartMovingSFX");
                _forkStopMovingSFX = serializedObject.FindProperty("forkStopMovingSFX");

                GUILayout.Label("AUDIO SOURCES", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_forkMovingSFX, new GUIContent("Fork Moving SFX"));
                EditorGUILayout.PropertyField(_forkStartMovingSFX, new GUIContent("Fork Start Moving SFX"));
                EditorGUILayout.PropertyField(_forkStopMovingSFX, new GUIContent("Fork Stop Moving SFX"));

                serializedObject.ApplyModifiedProperties();
            }

            GUILayout.EndVertical();
        }

        private void MarkSceneAlteration()
        {
            if (!Application.isPlaying)
            {
#if UNITY_EDITOR
                EditorUtility.SetDirty(_forkliftController);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
#endif
            }
        }
    }
}
