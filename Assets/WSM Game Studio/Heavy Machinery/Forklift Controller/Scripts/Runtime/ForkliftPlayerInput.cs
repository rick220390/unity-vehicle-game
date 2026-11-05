using WSMGameStudio.Utilities.InputSystem;
using UnityEngine;
using UnityEngine.Events;

namespace WSMGameStudio.HeavyMachinery
{
    [RequireComponent(typeof(ForkliftController))]
    public class ForkliftPlayerInput : MonoBehaviour
    {
        public bool enablePlayerInput = true;
        public InputSystemEnum activeInputSystem;
        //Legacy Input System
        public ForkliftInputSettings inputSettings;
        public UnityEvent[] customEvents;

        //Caching
        private ForkliftController _forkliftController;
        private ForkliftInputMapping _inputMapping; //New Input System
        private float _forksHorizontalInput = 0f;
        private float _forksVerticalInput = 0f;
        private float _forksWidthInput = 0f;
        private float _mastTiltInput = 0f;

        /// <summary>
        /// Initializing references
        /// </summary>
        void OnEnable()
        {
            _forkliftController = GetComponent<ForkliftController>();

            if (activeInputSystem == InputSystemEnum.LegacyInputSystem)
                return;

            _inputMapping = new ForkliftInputMapping();
            _inputMapping.Forklift.Enable();
        }

        void OnDisable()
        {
            if (_inputMapping == null)
                return;

            _inputMapping.Forklift.Disable();
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

            _forksHorizontalInput = _inputMapping.Forklift.ForksHorizontal.ReadValue<float>();
            _forksVerticalInput = _inputMapping.Forklift.ForksVertical.ReadValue<float>();
            _forksWidthInput = _inputMapping.Forklift.ForksWidth.ReadValue<float>();
            _mastTiltInput = _inputMapping.Forklift.MastTilt.ReadValue<float>();

            UpdateForkliftInput();

            // Custom Events Array Hack
            if (_inputMapping.Forklift.CustomEvents.WasPressedThisFrame())
            {
                if (customEvents == null) return;

                int customEventIndex = (int)_inputMapping.Forklift.CustomEvents.ReadValue<float>();
                if (customEvents.Length > customEventIndex && customEvents[customEventIndex] != null)
                    customEvents[customEventIndex].Invoke();
            }
        }

        /// <summary>
        /// Unity legacy input system
        /// </summary>
        private void LegacyInputSystemUpdate()
        {
            if (activeInputSystem == InputSystemEnum.NewInputSystem)
                return;

            if (inputSettings == null) return;

            #region Forklift Controls

            _forksHorizontalInput = Input.GetKey(inputSettings.forksRight) ? 1f : (Input.GetKey(inputSettings.forksLeft) ? -1f : 0f);
            _forksVerticalInput = Input.GetKey(inputSettings.forksUp) ? 1f : (Input.GetKey(inputSettings.forksDown) ? -1f : 0f);
            _forksWidthInput = Input.GetKey(inputSettings.forksOpen) ? 1f : (Input.GetKey(inputSettings.forksClose) ? -1f : 0f);
            _mastTiltInput = Input.GetKey(inputSettings.mastTiltForwards) ? 1f : (Input.GetKey(inputSettings.mastTiltBackwards) ? -1f : 0f);
            
            UpdateForkliftInput();

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

        private void UpdateForkliftInput()
        {
            _forkliftController.UpdateLevers(_forksVerticalInput, _forksHorizontalInput, _forksWidthInput, _mastTiltInput);
            _forkliftController.MoveForksHorizontally(_forksHorizontalInput);
            _forkliftController.MoveForksVertically(_forksVerticalInput);
            _forkliftController.AdjustForksWidth(_forksWidthInput);
            _forkliftController.RotateMast(_mastTiltInput);
        }
    }
}
