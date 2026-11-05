using WSMGameStudio.Utilities.InputSystem;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace WSMGameStudio.Vehicles
{
    [RequireComponent(typeof(WSMVehicleController))]
    public class WSMVehiclePlayerInput : MonoBehaviour
    {
        public bool enablePlayerInput = true;
        [Tooltip("Enable this for vehicles whose steering direction is reversed by their wheel/collider setup.")]
        public bool invertSteeringInput = false;
        public InputSystemEnum activeInputSystem;
        //Legacy Input System
        public WSMVehicleInputSettings inputSettings;
        public UnityEvent[] customEvents;

        //Caching
        private WSMVehicleController _vehicleController;
        private WSMVehicleInputMapping _inputMapping; //New Input System
        private float _acceleration = 0f;
        private float _steering = 0f;

        /// <summary>
        /// Initializing references
        /// </summary>
        void OnEnable()
        {
            _vehicleController = GetComponent<WSMVehicleController>();

            if (activeInputSystem == InputSystemEnum.LegacyInputSystem)
                return;

            _inputMapping = new WSMVehicleInputMapping();

            _inputMapping.Vehicle.Enable();
            _inputMapping.Vehicle.ToggleEngine.performed += ToggleEngine_Pressed;
            _inputMapping.Vehicle.Horn.performed += Horn_Pressed;
            _inputMapping.Vehicle.Headlights.performed += Headlights_Pressed;
            _inputMapping.Vehicle.InteriorLights.performed += InteriorLights_Pressed;
            _inputMapping.Vehicle.LeftSignal.performed += LeftSinalLights_Pressed;
            _inputMapping.Vehicle.RightSignal.performed += RightSinalLights_Pressed;
        }

        void OnDisable()
        {
            if (_inputMapping == null)
                return;

            _inputMapping.Vehicle.Disable();
            _inputMapping.Vehicle.ToggleEngine.performed -= ToggleEngine_Pressed;
            _inputMapping.Vehicle.Horn.performed -= Horn_Pressed;
            _inputMapping.Vehicle.Headlights.performed -= Headlights_Pressed;
            _inputMapping.Vehicle.InteriorLights.performed -= InteriorLights_Pressed;
            _inputMapping.Vehicle.LeftSignal.performed -= LeftSinalLights_Pressed;
            _inputMapping.Vehicle.RightSignal.performed -= RightSinalLights_Pressed;
        }

        /// <summary>
        /// Handling player input
        /// </summary>
        void Update()
        {
            if (enablePlayerInput)
            {
                LegacyInputSystemUpdate();
                NewInputSystemUpdate();
            }
        }

        /// <summary>
        /// Unity new input system
        /// </summary>
        private void NewInputSystemUpdate()
        {
            if (activeInputSystem == InputSystemEnum.LegacyInputSystem)
                return;

            if (_inputMapping == null)
                return;

            _vehicleController.AccelerationInput = _inputMapping.Vehicle.Acceleration.ReadValue<float>();
            float steeringInput = _inputMapping.Vehicle.Steering.ReadValue<float>();
            _vehicleController.SteeringInput = invertSteeringInput ? -steeringInput : steeringInput;
            _vehicleController.BrakesInput = _inputMapping.Vehicle.Brakes.ReadValue<float>();
            _vehicleController.HandBrakeInput = _inputMapping.Vehicle.Handbrake.ReadValue<float>();
            _vehicleController.ClutchInput = _inputMapping.Vehicle.Clutch.ReadValue<float>();

            // (standard PlayerInput class is not being used for legacy reasons: not losing customEvents references on asset update)
            // Unity's new input system doesn't support custom event arrays on custom input scripts. But MacGyver taught me there's always a way...
            // If you use ReadValue on a button, it will return 0f or 1f, (0 - Not Pressed | 1 - Pressed).
            // Multiply the return value with a Scale processor, and voila: you can pass indexes with a button :B
            if (_inputMapping.Vehicle.CustomEvents.WasPressedThisFrame())
            {
                if (customEvents == null) return;

                int customEventIndex = (int)_inputMapping.Vehicle.CustomEvents.ReadValue<float>();
                if (customEvents.Length > customEventIndex && customEvents[customEventIndex] != null)
                    customEvents[customEventIndex].Invoke();
            }
        }

        #region Button Events

        private void ToggleEngine_Pressed(InputAction.CallbackContext context)
        {
            if (!enablePlayerInput) return;
            _vehicleController.IsEngineOn = !_vehicleController.IsEngineOn;
        }

        private void Horn_Pressed(InputAction.CallbackContext context)
        {
            if (!enablePlayerInput) return;
            _vehicleController.Horn();
        }

        private void Headlights_Pressed(InputAction.CallbackContext context)
        {
            if (!enablePlayerInput) return;
            _vehicleController.HeadlightsOn = !_vehicleController.HeadlightsOn;
        }

        private void InteriorLights_Pressed(InputAction.CallbackContext context)
        {
            if (!enablePlayerInput) return;
            _vehicleController.InteriorLightsOn = !_vehicleController.InteriorLightsOn;
        }

        private void LeftSinalLights_Pressed(InputAction.CallbackContext context)
        {
            if (!enablePlayerInput) return;
            _vehicleController.LeftSinalLightsOn = !_vehicleController.LeftSinalLightsOn;
        }

        private void RightSinalLights_Pressed(InputAction.CallbackContext context)
        {
            if (!enablePlayerInput) return;
            _vehicleController.RightSinalLightsOn = !_vehicleController.RightSinalLightsOn;
        }

        #endregion

        /// <summary>
        /// Unity legacy input system
        /// </summary>
        private void LegacyInputSystemUpdate()
        {
            if (activeInputSystem == InputSystemEnum.NewInputSystem)
                return;

            if (inputSettings == null) return;

            #region Vehicle Controls

            _acceleration = Input.GetKey(inputSettings.acceleration) ? 1f : 0;
            _acceleration = Input.GetKey(inputSettings.reverse) ? _acceleration - 1 : _acceleration;
            _vehicleController.AccelerationInput = _acceleration;

            _steering = 0f;
            _steering = Input.GetKey(inputSettings.turnRight) ? _steering + 1 : _steering;
            _steering = Input.GetKey(inputSettings.turnLeft) ? _steering - 1 : _steering;
            _vehicleController.SteeringInput = invertSteeringInput ? -_steering : _steering;

            _vehicleController.BrakesInput = Input.GetKey(inputSettings.brakes) ? 1f : 0f;
            _vehicleController.HandBrakeInput = Input.GetKey(inputSettings.handbrake) ? 1f : 0f;
            _vehicleController.ClutchInput = Input.GetKey(inputSettings.clutch) ? 1f : 0f;

            if (Input.GetKeyDown(inputSettings.toggleEngine))
                _vehicleController.IsEngineOn = !_vehicleController.IsEngineOn;

            if (Input.GetKeyDown(inputSettings.horn))
                _vehicleController.Horn();

            if (Input.GetKeyDown(inputSettings.headlights))
                _vehicleController.HeadlightsOn = !_vehicleController.HeadlightsOn;

            if (Input.GetKeyDown(inputSettings.interiorLights))
                _vehicleController.InteriorLightsOn = !_vehicleController.InteriorLightsOn;

            if (Input.GetKeyDown(inputSettings.leftSignalLights))
                _vehicleController.LeftSinalLightsOn = !_vehicleController.LeftSinalLightsOn;

            if (Input.GetKeyDown(inputSettings.rightSignalLights))
                _vehicleController.RightSinalLightsOn = !_vehicleController.RightSinalLightsOn;

            #endregion

            #region Player Custom Events

            for (int i = 0; i < inputSettings.customEventTriggers.Length; i++)
            {
                if (Input.GetKeyDown(inputSettings.customEventTriggers[i]))
                {
                    if (customEvents.Length > i && customEvents[i] != null)
                        customEvents[i].Invoke();
                }
            }

            #endregion
        }
    }
}
