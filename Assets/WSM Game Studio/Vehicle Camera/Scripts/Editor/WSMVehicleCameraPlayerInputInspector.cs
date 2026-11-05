using WSMGameStudio.Utilities.InputSystem;
using UnityEditor;

namespace WSMGameStudio.Vehicles
{
    [CustomEditor(typeof(WSMVehicleCameraPlayerInput))]
    public class WSMVehicleCameraPlayerInputInspector : UnityEditor.Editor
    {
        private WSMVehicleCameraPlayerInput _vehicleCameraInput;

        protected SerializedProperty _enablePlayerInput;
        protected SerializedProperty _activeInputSystem;
        protected SerializedProperty _inputSettings;
        protected SerializedProperty _customEvents;

        public override void OnInspectorGUI()
        {
            //base.OnInspectorGUI();
            _vehicleCameraInput = target as WSMVehicleCameraPlayerInput;

            _enablePlayerInput = serializedObject.FindProperty("enablePlayerInput");
            _activeInputSystem = serializedObject.FindProperty("activeInputSystem");
            _inputSettings = serializedObject.FindProperty("inputSettings");
            _customEvents = serializedObject.FindProperty("customEvents");

            serializedObject.Update();
            EditorGUILayout.PropertyField(_enablePlayerInput);
            EditorGUILayout.PropertyField(_activeInputSystem);
            if (_vehicleCameraInput.activeInputSystem != InputSystemEnum.NewInputSystem)
                EditorGUILayout.PropertyField(_inputSettings);
            EditorGUILayout.PropertyField(_customEvents);
            serializedObject.ApplyModifiedProperties();
        }
    }

}