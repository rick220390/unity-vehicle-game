using UnityEngine;
using WSMGameStudio.Vehicles;

namespace WSMGameStudio.HeavyMachinery
{
    [System.Serializable]
    public class ForkliftController : MonoBehaviour
    {
        public float forksVerticalSpeed = 0.3f;
        public float forksHorizontalSpeed = 0.5f;
        public float widthAdjustmentSpeed = 0.5f;
        public float mastTiltSpeed = 0.3f;

        [SerializeField] public RotatingMechanicalPart mainMast;
        [SerializeField] public MovingMechanicalPart secondaryMast;
        [SerializeField] public MovingMechanicalPart loadBackrestCylinders;
        [SerializeField] public MovingMechanicalPart loadBackrest;
        [SerializeField] public MovingMechanicalPart fork_L;
        [SerializeField] public MovingMechanicalPart fork_R;
        [SerializeField] public Transform forksVerticalLever;
        [SerializeField] public Transform forksHorizontalLever;
        [SerializeField] public Transform forksWidthLever;
        [SerializeField] public Transform mastTiltLever;
        [SerializeField] public AudioSource forkMovingSFX;
        [SerializeField] public AudioSource forkStartMovingSFX;
        [SerializeField] public AudioSource forkStopMovingSFX;

        private IMotorizedVehicle _vehicleController;
        private float _verticalLeverAngle = 0;
        private float _horizontalLeverAngle = 0;
        private float _widthLeverAngle = 0;
        private float _tiltLeverAngle = 0;

        [Range(0f, 1f)] private float _forksVertical;
        [Range(0f, 1f)] private float _secondaryMastVertical;
        [Range(0f, 1f)] private float _forksHorizontal;
        [Range(0f, 1f)] private float _forksWidth;
        [Range(0f, 1f)] private float _mastTilt;

        public float ForksVertical { get { return _forksVertical; } set { _forksVertical = value; } }
        public float ForksHorizontal { get { return _forksHorizontal; } set { _forksHorizontal = value; } }
        public float ForksWidth { get { return _forksWidth; } set { _forksWidth = value; } }
        public float MastTilt { get { return _mastTilt; } set { _mastTilt = value; } }
        private bool IsEngineOn { get { return _vehicleController != null && _vehicleController.IsEngineOn; } }
        private bool MainMast_IsMoving { get { return mainMast == null ? false : mainMast.IsMoving; } }
        private bool SecondaryMast_IsMoving { get { return secondaryMast == null ? false : secondaryMast.IsMoving; } }
        private bool LoadBackrestCylinders_IsMoving { get { return loadBackrestCylinders == null ? false : loadBackrestCylinders.IsMoving; } }
        private bool LoadBackrest_IsMoving { get { return loadBackrest == null ? false : loadBackrest.IsMoving; } }
        private bool LeftFork_IsMoving { get { return fork_L == null ? false : fork_L.IsMoving; } }
        private bool RightFork_IsMoving { get { return fork_R == null ? false : fork_R.IsMoving; } }

        /// <summary>
        /// Initialize forklift
        /// </summary>
        private void OnEnable()
        {
            _vehicleController = GetComponent<IMotorizedVehicle>();

            _mastTilt = mainMast.MovementInput;
            _forksHorizontal = loadBackrestCylinders.MovementInput;
            _forksVertical = loadBackrest.MovementInput;
            _forksWidth = fork_L != null ? fork_L.MovementInput : 0f;
            _secondaryMastVertical = secondaryMast.MovementInput;

            forksVerticalSpeed = Mathf.Abs(forksVerticalSpeed);
            forksHorizontalSpeed = Mathf.Abs(forksHorizontalSpeed);
            widthAdjustmentSpeed = Mathf.Abs(widthAdjustmentSpeed);
            mastTiltSpeed = Mathf.Abs(mastTiltSpeed);
        }

        /// <summary>
        /// 
        /// </summary>
        private void LateUpdate()
        {
            if (IsEngineOn)
            {
                bool forksMoving = MainMast_IsMoving || SecondaryMast_IsMoving || LoadBackrestCylinders_IsMoving || LoadBackrest_IsMoving || LeftFork_IsMoving || RightFork_IsMoving;
                ForksMovementSFX(forksMoving); //Should be called on late update to track SFX correctly
            }
        }

        /// <summary>
        /// Handles forks vertical movement
        /// </summary>
        /// <param name="verticalInput">-1 = down | 0 = none | 1 = up</param>
        public void MoveForksVertically(float verticalInput)
        {
            if (IsEngineOn)
            {
                _forksVertical += _secondaryMastVertical <= 0 ? (verticalInput * Time.deltaTime * forksVerticalSpeed) : 1f;
                _forksVertical = Mathf.Clamp01(_forksVertical);

                _secondaryMastVertical += _forksVertical >= 1 ? (verticalInput * Time.deltaTime * forksVerticalSpeed) : 0f;
                _secondaryMastVertical = Mathf.Clamp01(_secondaryMastVertical);

                loadBackrest.MovementInput = _forksVertical;
                secondaryMast.MovementInput = _secondaryMastVertical;
            }
        }

        /// <summary>
        /// Handles forks horizontal movement
        /// </summary>
        /// <param name="horizontalInput">-1 = left | 0 = none | 1 = right</param>
        public void MoveForksHorizontally(float horizontalInput)
        {
            if (IsEngineOn)
            {
                _forksHorizontal += (horizontalInput * Time.deltaTime * forksHorizontalSpeed);
                _forksHorizontal = Mathf.Clamp01(_forksHorizontal);
                loadBackrestCylinders.MovementInput = _forksHorizontal;
            }
        }

        /// <summary>
        /// Adjust Forks Width
        /// </summary>
        /// <param name="input">-1 = Close | 0 = none | 1 = Open</param>
        public void AdjustForksWidth(float widthInput)
        {
            if (IsEngineOn)
            {
                if (fork_L == null || fork_R == null)
                    return;

                _forksWidth += (widthInput * Time.deltaTime * widthAdjustmentSpeed);
                _forksWidth = Mathf.Clamp01(_forksWidth);
                fork_L.MovementInput = _forksWidth;
                fork_R.MovementInput = _forksWidth;
            }
        }

        /// <summary>
        /// Handles mast rotation
        /// </summary>
        /// <param name="direction">-1 = backwards | 0 = none | 1 = forward</param>
        public void RotateMast(float direction)
        {
            if (IsEngineOn)
            {
                _mastTilt += (direction * Time.deltaTime * mastTiltSpeed);
                _mastTilt = Mathf.Clamp01(_mastTilt);
                mainMast.MovementInput = _mastTilt;
            }
        }

        /// <summary>
        /// Animate levers accordingly to player's input
        /// </summary>
        /// <param name="forkVerticalInput"></param>
        /// <param name="forkHorizontalInput"></param>
        /// <param name="mastTiltInput"></param>
        public void UpdateLevers(float forkVerticalInput, float forkHorizontalInput, float forkWidthInput, float mastTiltInput)
        {
            if (IsEngineOn)
            {
                _verticalLeverAngle = Mathf.MoveTowards(_verticalLeverAngle, forkVerticalInput * -10f, 70f * Time.deltaTime);
                _horizontalLeverAngle = Mathf.MoveTowards(_horizontalLeverAngle, forkHorizontalInput * 10f, 70f * Time.deltaTime);
                _widthLeverAngle = Mathf.MoveTowards(_widthLeverAngle, forkWidthInput * 10f, 70f * Time.deltaTime);
                _tiltLeverAngle = Mathf.MoveTowards(_tiltLeverAngle, mastTiltInput * 10f, 70f * Time.deltaTime);

                if (forksVerticalLever != null) forksVerticalLever.localEulerAngles = new Vector3(_verticalLeverAngle, 0f, 0f);
                if (forksHorizontalLever != null) forksHorizontalLever.localEulerAngles = new Vector3(_horizontalLeverAngle, 0f, 0f);
                if (forksWidthLever != null) forksWidthLever.localEulerAngles = new Vector3(_widthLeverAngle, 0f, 0f);
                if (mastTiltLever != null) mastTiltLever.localEulerAngles = new Vector3(_tiltLeverAngle, 0f, 0f);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="play"></param>
        private void ForksMovementSFX(bool forksMoving)
        {
            if (IsEngineOn && forkMovingSFX != null)
            {
                if (!forkMovingSFX.isPlaying && forksMoving)
                {
                    forkMovingSFX.Play();

                    if (forkStartMovingSFX != null && !forkStartMovingSFX.isPlaying)
                        forkStartMovingSFX.Play();
                }
                else if (forkMovingSFX.isPlaying && !forksMoving)
                {
                    forkMovingSFX.Stop();

                    if (forkStopMovingSFX != null && !forkStopMovingSFX.isPlaying)
                        forkStopMovingSFX.Play();
                }
            }
        }
    }
}
