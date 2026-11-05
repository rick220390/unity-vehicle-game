using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using WSMGameStudio.Vehicles;

namespace WSMGameStudio.HeavyMachinery
{
    public class ForkliftDemo : MonoBehaviour
    {
        public GameObject forklift;
        public GameObject vehicleCamera;
        public Text txtControls;

        private ForkliftPlayerInput _forkliftInput;
        private WSMVehiclePlayerInput _vehicleInput;
        private WSMVehicleCameraPlayerInput _cameraInput;

        private ForkliftInputMapping _forkliftInputMapping; //New Input System
        private WSMVehicleInputMapping _vehicleInputMapping; //New Input System
        private WSMVehicleCameraInputMapping _cameraInputMapping; //New Input System

        private bool _showControlsText = false;
        private const string _defaultText = "Show/Hide Controls: Tab";
        private string _controlsText = string.Empty;

        // Use this for initialization
        void OnEnable()
        {
            if (forklift != null && vehicleCamera != null)
            {
                _forkliftInput = forklift.GetComponent<ForkliftPlayerInput>();
                _vehicleInput = forklift.GetComponent<WSMVehiclePlayerInput>();
                _cameraInput = vehicleCamera.GetComponent<WSMVehicleCameraPlayerInput>();

                _forkliftInputMapping = new ForkliftInputMapping();
                _vehicleInputMapping = new WSMVehicleInputMapping();
                _cameraInputMapping = new WSMVehicleCameraInputMapping();

                FormatControlsText(); 
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                FormatControlsText();
                _showControlsText = !_showControlsText;
                txtControls.text = _showControlsText ? _controlsText : _defaultText;
            }
        }

        private void FormatControlsText()
        {
            if (_forkliftInput == null || _vehicleInput == null || _cameraInput == null)
                return;

            _controlsText = string.Format("{0}{1}", _defaultText, System.Environment.NewLine);

            FormatForkliftInputMappings();
            FormatVehicleInputMappings();
            FormatCameraInputMappings();
        }

        /// <summary>
        /// Append Forklift Input Mappings Text
        /// </summary>
        private void FormatForkliftInputMappings()
        {
            //Forklift
            _controlsText += string.Format("{0}FORKLIFT{0}", System.Environment.NewLine);
            if (_forkliftInput.activeInputSystem == Utilities.InputSystem.InputSystemEnum.LegacyInputSystem)
            {
                _controlsText += string.Format("Forks up/down: {0}/{1}{2}", _forkliftInput.inputSettings.forksUp, _forkliftInput.inputSettings.forksDown, System.Environment.NewLine);
                _controlsText += string.Format("Forks left/right: {0}/{1}{2}", _forkliftInput.inputSettings.forksLeft, _forkliftInput.inputSettings.forksRight, System.Environment.NewLine);
                _controlsText += string.Format("Mast Tilt Back/Forward: {0}/{1}{2}", _forkliftInput.inputSettings.mastTiltBackwards, _forkliftInput.inputSettings.mastTiltForwards, System.Environment.NewLine);
            }
            else  //New or Both
            {
                _controlsText += string.Format("Forks Vertical: {0}{1}", _forkliftInputMapping.Forklift.ForksVertical.GetBindingDisplayString(), System.Environment.NewLine); ;
                _controlsText += string.Format("Forks Horizontal: {0}{1}", _forkliftInputMapping.Forklift.ForksHorizontal.GetBindingDisplayString(), System.Environment.NewLine);
                _controlsText += string.Format("Mast Tilt: {0}{1}", _forkliftInputMapping.Forklift.MastTilt.GetBindingDisplayString(), System.Environment.NewLine);
            }
        }

        /// <summary>
        /// Append Vehicle Input Mappings Text
        /// </summary>
        private void FormatVehicleInputMappings()
        {
            //Vehicle
            _controlsText += string.Format("{0}VEHICLE{0}", System.Environment.NewLine);
            if (_vehicleInput.activeInputSystem == Utilities.InputSystem.InputSystemEnum.LegacyInputSystem)
            {
                _controlsText += string.Format("Engine On/Off: {0}{1}", _vehicleInput.inputSettings.toggleEngine, System.Environment.NewLine);
                _controlsText += string.Format("Acceleration/Reverse: {0}/{1}{2}", _vehicleInput.inputSettings.acceleration, _vehicleInput.inputSettings.reverse, System.Environment.NewLine);
                _controlsText += string.Format("Steering Left/Right: {0}/{1}{2}", _vehicleInput.inputSettings.turnLeft, _vehicleInput.inputSettings.turnRight, System.Environment.NewLine);
                _controlsText += string.Format("Brakes: {0}{1}", _vehicleInput.inputSettings.brakes, System.Environment.NewLine);
                _controlsText += string.Format("Handbrake: {0}{1}", _vehicleInput.inputSettings.handbrake, System.Environment.NewLine);
                _controlsText += string.Format("Clutch: {0}{1}", _vehicleInput.inputSettings.clutch, System.Environment.NewLine);
                _controlsText += string.Format("Horn: {0}{1}", _vehicleInput.inputSettings.horn, System.Environment.NewLine);
                _controlsText += string.Format("Headlights: {0}{1}", _vehicleInput.inputSettings.headlights, System.Environment.NewLine);
                _controlsText += string.Format("Left Signal Light: {0}{1}", _vehicleInput.inputSettings.leftSignalLights, System.Environment.NewLine);
                _controlsText += string.Format("Right Signal Light: {0}{1}", _vehicleInput.inputSettings.rightSignalLights, System.Environment.NewLine);
                if (_vehicleInput.inputSettings.customEventTriggers != null && _vehicleInput.inputSettings.customEventTriggers.Length >= 2)
                {
                    _controlsText += string.Format("Left Door: {0}{1}", _vehicleInput.inputSettings.customEventTriggers[0], System.Environment.NewLine);
                    _controlsText += string.Format("Right Door: {0}{1}", _vehicleInput.inputSettings.customEventTriggers[1], System.Environment.NewLine);
                }
            }
            else //New or Both
            {
                _controlsText += string.Format("Engine On/Off: {0}{1}", _vehicleInputMapping.Vehicle.ToggleEngine.GetBindingDisplayString(), System.Environment.NewLine);
                _controlsText += string.Format("Acceleration/Reverse: {0}{1}", _vehicleInputMapping.Vehicle.Acceleration.GetBindingDisplayString(), System.Environment.NewLine);
                _controlsText += string.Format("Steering: {0}{1}", _vehicleInputMapping.Vehicle.Steering.GetBindingDisplayString(), System.Environment.NewLine);
                _controlsText += string.Format("Brakes: {0}{1}", _vehicleInputMapping.Vehicle.Brakes.GetBindingDisplayString(), System.Environment.NewLine);
                _controlsText += string.Format("Handbrake: {0}{1}", _vehicleInputMapping.Vehicle.Handbrake.GetBindingDisplayString(), System.Environment.NewLine);
                _controlsText += string.Format("Clutch: {0}{1}", _vehicleInputMapping.Vehicle.Clutch.GetBindingDisplayString(), System.Environment.NewLine);
                _controlsText += string.Format("Horn: {0}{1}", _vehicleInputMapping.Vehicle.Horn.GetBindingDisplayString(), System.Environment.NewLine);
                _controlsText += string.Format("Headlights: {0}{1}", _vehicleInputMapping.Vehicle.Headlights.GetBindingDisplayString(), System.Environment.NewLine);
                _controlsText += string.Format("Left Signal Light: {0}{1}", _vehicleInputMapping.Vehicle.LeftSignal.GetBindingDisplayString(), System.Environment.NewLine);
                _controlsText += string.Format("Right Signal Light: {0}{1}", _vehicleInputMapping.Vehicle.RightSignal.GetBindingDisplayString(), System.Environment.NewLine);
                _controlsText += string.Format("Doors: {0}{1}", _vehicleInputMapping.Vehicle.CustomEvents.GetBindingDisplayString(), System.Environment.NewLine);
            }
        }

        /// <summary>
        /// Append Camera Input Mappings Text
        /// </summary>
        private void FormatCameraInputMappings()
        {
            //Camera
            _controlsText += string.Format("{0}CAMERA{0}", System.Environment.NewLine);
            if (_cameraInput.activeInputSystem == Utilities.InputSystem.InputSystemEnum.LegacyInputSystem)
            {
                _controlsText += string.Format("Camera Look Right: {0}{1}", _cameraInput.inputSettings.cameraLookRight, System.Environment.NewLine);
                _controlsText += string.Format("Camera Look Left: {0}{1}", _cameraInput.inputSettings.cameraLookLeft, System.Environment.NewLine);
                _controlsText += string.Format("Camera Look Back: {0}{1}", _cameraInput.inputSettings.cameraLookBack, System.Environment.NewLine);
                _controlsText += string.Format("Camera Look Up: {0}{1}", _cameraInput.inputSettings.cameraLookUp, System.Environment.NewLine);
                _controlsText += string.Format("Camera Look Down: {0}{1}", _cameraInput.inputSettings.cameraLookDown, System.Environment.NewLine);
                _controlsText += string.Format("Toggle Camera: {0}{1}", _cameraInput.inputSettings.toggleCamera, System.Environment.NewLine);
            }
            else //New or Both
            {
                _controlsText += string.Format("Toggle Camera: {0}{1}", _cameraInputMapping.Camera.Toggle.GetBindingDisplayString(), System.Environment.NewLine);
                _controlsText += string.Format("Camera Zoom: {0}{1}", _cameraInputMapping.Camera.Zoom.GetBindingDisplayString(), System.Environment.NewLine);
                _controlsText += string.Format("Camera Look: {0}{1}", _cameraInputMapping.Camera.Look.GetBindingDisplayString(), System.Environment.NewLine);
                _controlsText += string.Format("Camera Look Back: {0}{1}", _cameraInputMapping.Camera.LookBack.GetBindingDisplayString(), System.Environment.NewLine);
                _controlsText += string.Format("Camera Look Custom: {0}{1}", _cameraInputMapping.Camera.LookCustom.GetBindingDisplayString(), System.Environment.NewLine);
            }
        }
    }
}
