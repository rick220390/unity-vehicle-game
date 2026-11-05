using WSMGameStudio.Utilities.InputSystem;
using UnityEditor;

namespace WSMGameStudio.HeavyMachinery
{
    [CustomEditor(typeof(ForkliftPlayerInput))]
    public class ForkliftPlayerInputInspector : UnityEditor.Editor
    {
        private ForkliftPlayerInput _forkliftInput;

        protected SerializedProperty _enablePlayerInput;
        protected SerializedProperty _activeInputSystem;
        protected SerializedProperty _inputSettings;
        protected SerializedProperty _customEvents;

        public override void OnInspectorGUI()
        {
            //base.OnInspectorGUI();
            _forkliftInput = target as ForkliftPlayerInput;

            _enablePlayerInput = serializedObject.FindProperty("enablePlayerInput");
            _activeInputSystem = serializedObject.FindProperty("activeInputSystem");
            _inputSettings = serializedObject.FindProperty("inputSettings");
            _customEvents = serializedObject.FindProperty("customEvents");

            serializedObject.Update();
            EditorGUILayout.PropertyField(_enablePlayerInput);
            EditorGUILayout.PropertyField(_activeInputSystem);
            if (_forkliftInput.activeInputSystem != InputSystemEnum.NewInputSystem)
                EditorGUILayout.PropertyField(_inputSettings);
            EditorGUILayout.PropertyField(_customEvents);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
