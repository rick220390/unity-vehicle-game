using UnityEngine;

namespace WSMGameStudio.Vehicles
{
    [CreateAssetMenu(fileName = "VehicleCameraInputSettings", menuName = "WSM Game Studio/Vehicle Camera/Legacy Input System/Vehicle Camera Input Settings", order = 1)]
    public class WSMVehicleCameraInputSettings : ScriptableObject
    {
        public KeyCode cameraLookRight = KeyCode.RightArrow;
        public KeyCode cameraLookLeft = KeyCode.LeftArrow;
        public KeyCode cameraLookUp = KeyCode.UpArrow;
        public KeyCode cameraLookDown = KeyCode.DownArrow;
        public KeyCode cameraLookBack = KeyCode.RightShift;
        public KeyCode cameraLookCustom = KeyCode.RightControl;
        public KeyCode zoomIn = KeyCode.KeypadPlus;
        public KeyCode zoomOut = KeyCode.KeypadMinus;
        public KeyCode toggleCamera = KeyCode.C;

        public KeyCode[] customEventTriggers;
    } 
}
