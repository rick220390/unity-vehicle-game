using WSMGameStudio.Utilities.InputSystem;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace WSMGameStudio.Vehicles
{
    [RequireComponent(typeof(WSMVehicleCamera))]
    public class WSMVehicleCameraPlayerInput : MonoBehaviour
    {
        public bool enablePlayerInput = true;
        public InputSystemEnum activeInputSystem;
        //Legacy Input System
        public WSMVehicleCameraInputSettings inputSettings;
        public UnityEvent[] customEvents;

        //Caching
        private WSMVehicleCamera _vehicleCamera;
        private WSMVehicleCameraInputMapping _inputMapping; //New Input System
        private Vector2 _lookInput = Vector2.zero;

        /// <summary>
        /// Initializing references
        /// </summary>
        void OnEnable()
        {
            _vehicleCamera = GetComponent<WSMVehicleCamera>();

            if (activeInputSystem == InputSystemEnum.LegacyInputSystem)
                return;

            _inputMapping = new WSMVehicleCameraInputMapping();
            _inputMapping.Camera.Enable();
            _inputMapping.Camera.Toggle.performed += ToggleCamera_Pressed;
            _inputMapping.Camera.LookBack.canceled += LookBack_Released;
            _inputMapping.Camera.LookCustom.canceled += LookCustom_Released;
        }

        private void OnDisable()
        {
            if (_inputMapping == null)
                return;

            _inputMapping.Camera.Disable();
            _inputMapping.Camera.Toggle.performed -= ToggleCamera_Pressed;
            _inputMapping.Camera.LookBack.canceled -= LookBack_Released;
            _inputMapping.Camera.LookCustom.canceled -= LookCustom_Released;
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

            _vehicleCamera.ZoomInput = _inputMapping.Camera.Zoom.ReadValue<float>();
            _lookInput = _inputMapping.Camera.Look.ReadValue<Vector2>();

            _vehicleCamera.HorizontalInput = _lookInput.x;
            _vehicleCamera.VerticalInput = _lookInput.y;

            _vehicleCamera.CamLookDirection = WSMVehicleCameraLookDirection.Forward;
            _vehicleCamera.CamLookDirection = _lookInput.x == 1f ? WSMVehicleCameraLookDirection.Right : _vehicleCamera.CamLookDirection;
            _vehicleCamera.CamLookDirection = _lookInput.x == -1f ? WSMVehicleCameraLookDirection.Left : _vehicleCamera.CamLookDirection;
            _vehicleCamera.CamLookDirection = _lookInput.y == 1f ? WSMVehicleCameraLookDirection.Up : _vehicleCamera.CamLookDirection;
            _vehicleCamera.CamLookDirection = _lookInput.y == -1f ? WSMVehicleCameraLookDirection.Down : _vehicleCamera.CamLookDirection;
            _vehicleCamera.CamLookDirection = _inputMapping.Camera.LookCustom.IsPressed() ? WSMVehicleCameraLookDirection.Custom : _vehicleCamera.CamLookDirection;
            _vehicleCamera.CamLookDirection = _inputMapping.Camera.LookBack.IsPressed() ? WSMVehicleCameraLookDirection.Backwards : _vehicleCamera.CamLookDirection;

            // Custom Events Array Hack
            if (_inputMapping.Camera.CustomEvents.WasPressedThisFrame())
            {
                if (customEvents == null) return;

                int customEventIndex = (int)_inputMapping.Camera.CustomEvents.ReadValue<float>();
                if (customEvents.Length > customEventIndex && customEvents[customEventIndex] != null)
                    customEvents[customEventIndex].Invoke();
            }
        }

        #region Button Events
        
        private void ToggleCamera_Pressed(InputAction.CallbackContext context)
        {
            if (!enablePlayerInput) return;
            _vehicleCamera.ToggleCamera();
        }

        private void LookBack_Released(InputAction.CallbackContext context)
        {
            if (!enablePlayerInput) return;
            _vehicleCamera.MoveToStartPosition();
        }

        private void LookCustom_Released(InputAction.CallbackContext context)
        {
            if (!enablePlayerInput) return;
            _vehicleCamera.MoveToStartPosition();
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

            #region Camera Input

            _vehicleCamera.HorizontalInput = 0f;
            _vehicleCamera.VerticalInput = 0f;

            _vehicleCamera.CamLookDirection = WSMVehicleCameraLookDirection.Forward;

            if (Input.GetKey(inputSettings.cameraLookRight))
            {
                _vehicleCamera.CamLookDirection = WSMVehicleCameraLookDirection.Right;
                _vehicleCamera.HorizontalInput += 1f;
            }

            if (Input.GetKey(inputSettings.cameraLookLeft))
            {
                _vehicleCamera.CamLookDirection = WSMVehicleCameraLookDirection.Left;
                _vehicleCamera.HorizontalInput += -1f;
            }

            if (Input.GetKey(inputSettings.cameraLookUp))
            {
                _vehicleCamera.CamLookDirection = WSMVehicleCameraLookDirection.Up;
                _vehicleCamera.VerticalInput += 1f;
            }

            if (Input.GetKey(inputSettings.cameraLookDown))
            {
                _vehicleCamera.CamLookDirection = WSMVehicleCameraLookDirection.Down;
                _vehicleCamera.VerticalInput += -1;
            }

            if (Input.GetKey(inputSettings.cameraLookCustom))
            {
                _vehicleCamera.CamLookDirection = WSMVehicleCameraLookDirection.Custom;
            }

            //Look back input last for higher priority
            if (Input.GetKey(inputSettings.cameraLookBack))
            {
                _vehicleCamera.CamLookDirection = WSMVehicleCameraLookDirection.Backwards;
            }

            if (Input.GetKeyDown(inputSettings.toggleCamera))
                _vehicleCamera.ToggleCamera();

            _vehicleCamera.ZoomInput = 0f;
            _vehicleCamera.ZoomInput += (Input.GetKey(inputSettings.zoomIn) ? -1f : 0f);
            _vehicleCamera.ZoomInput += (Input.GetKey(inputSettings.zoomOut) ? 1f : 0f);

            #endregion

            #region Player Custom Events

            for (int i = 0; i < inputSettings.customEventTriggers.Length; i++)
            {
                if (Input.GetKeyDown(inputSettings.customEventTriggers[i]))
                {
                    if (customEvents.Length > i)
                        customEvents[i].Invoke();
                }
            }

            #endregion
        }
    }
}
