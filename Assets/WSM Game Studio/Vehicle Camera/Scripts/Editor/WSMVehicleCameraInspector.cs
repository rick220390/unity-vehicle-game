using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace WSMGameStudio.Vehicles
{
    [CustomEditor(typeof(WSMVehicleCamera))]
    public class WSMVehicleCameraInspector : UnityEditor.Editor
    {
        private WSMVehicleCamera _vehicleCamera;

        protected SerializedProperty _target;
        protected SerializedProperty _cameraSettings;
        protected SerializedProperty _cameraMode;
        protected SerializedProperty _selectedCamera;
        protected SerializedProperty _cameraToggleEnabled;
        protected SerializedProperty _activeCameraTypes;
        protected SerializedProperty _invertHorizontalAxis;
        protected SerializedProperty _invertVerticalAxis;
        protected SerializedProperty _cameraCollision;
        protected SerializedProperty _collisionMask;

        private GUIStyle _menuBoxStyle;

        public override void OnInspectorGUI()
        {
            //base.OnInspectorGUI();
            _vehicleCamera = target as WSMVehicleCamera;

            //Set up the box style if null
            if (_menuBoxStyle == null)
            {
                _menuBoxStyle = new GUIStyle(GUI.skin.box);
                _menuBoxStyle.normal.textColor = GUI.skin.label.normal.textColor;
                _menuBoxStyle.fontStyle = FontStyle.Bold;
                _menuBoxStyle.alignment = TextAnchor.UpperLeft;
            }
            GUILayout.BeginVertical(_menuBoxStyle);

            _target = serializedObject.FindProperty("_target");
            _cameraSettings = serializedObject.FindProperty("_cameraSettings");
            _cameraMode = serializedObject.FindProperty("_cameraMode");
            _selectedCamera = serializedObject.FindProperty("_selectedCamera");
            _cameraToggleEnabled = serializedObject.FindProperty("_cameraToggleEnabled");
            _activeCameraTypes = serializedObject.FindProperty("_activeCameraTypes");
            _invertHorizontalAxis = serializedObject.FindProperty("_invertHorizontalAxis");
            _invertVerticalAxis = serializedObject.FindProperty("_invertVerticalAxis");
            _cameraCollision = serializedObject.FindProperty("_cameraCollision");
            _collisionMask = serializedObject.FindProperty("_collisionMask");

            serializedObject.Update();
            EditorGUILayout.PropertyField(_target);
            EditorGUILayout.PropertyField(_cameraSettings);
            EditorGUILayout.PropertyField(_cameraMode);
            EditorGUILayout.PropertyField(_selectedCamera);
            EditorGUILayout.PropertyField(_cameraToggleEnabled);
            EditorGUILayout.PropertyField(_activeCameraTypes);
            GUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(_invertHorizontalAxis);
            EditorGUILayout.PropertyField(_invertVerticalAxis);
            GUILayout.EndHorizontal();
            EditorGUILayout.PropertyField(_cameraCollision);
            EditorGUILayout.PropertyField(_collisionMask);

            if (serializedObject.hasModifiedProperties)
            {
                if (_selectedCamera.enumValueIndex != (int)_vehicleCamera.SelectedType)
                {
                    serializedObject.ApplyModifiedProperties();
                    ApplySelectedCamera();
                }
                else
                    serializedObject.ApplyModifiedProperties();
            }

            GUILayout.Label("CAMERA OPERATIONS", EditorStyles.boldLabel);

            if (GUILayout.Button("Move to Start Position"))
            {
                ApplySelectedCamera();
                MarkSceneAlteration();
            }

            GUILayout.EndVertical();
        }

        private void ApplySelectedCamera()
        {
            _vehicleCamera.InitializeCamera(true);
            _vehicleCamera.InitializeVehicle();
            _vehicleCamera.MoveToStartPosition();
        }

        private void MarkSceneAlteration()
        {
            if (!Application.isPlaying)
            {
#if UNITY_EDITOR
                EditorUtility.SetDirty(_vehicleCamera);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
#endif
            }
        }
    }
}
