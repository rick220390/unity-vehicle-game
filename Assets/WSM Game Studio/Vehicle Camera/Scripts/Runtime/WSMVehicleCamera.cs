using System;
using UnityEngine;

namespace WSMGameStudio.Vehicles
{
    [RequireComponent(typeof(Camera))]
    public class WSMVehicleCamera : MonoBehaviour
    {
        #region VARIABLES
        [SerializeField] private Transform _target;
        [SerializeField] private bool _cameraToggleEnabled = true;
        [SerializeField] private ActiveCameraTypes _activeCameraTypes;
        [SerializeField] private WSMVehicleCameraType _selectedCamera;
        [SerializeField] private WSMVehicleCameraSettings _cameraSettings;
        [SerializeField] private WSMVehicleCameraMode _cameraMode;
        [SerializeField] private bool _invertHorizontalAxis = false;
        [SerializeField] private bool _invertVerticalAxis = false;
        [SerializeField] private bool _cameraCollision = true;
        [SerializeField] private LayerMask _collisionMask;

        //Vehicle
        private Rigidbody _targetRigidbody;
        private float _vehicleSpeed = 0f;
        private float _cameraDamping = 5f;
        private float _cameraDeadZone = 30f;
        //Camera
        private Camera _camera;
        private Transform _transform;
        private int _selectedCameraIndex;
        private int _cameraTypesLength;
        private float _followSpeed = 0f;
        private float _lookSpeed = 0f;
        private float _zoom;
        private Vector3 _targetCamLocalPosition = Vector3.zero;
        private Vector2 _targetCamLocalRotation = Vector2.zero;
        private Vector3 _targetCamPosition = Vector3.zero;
        private Vector3 _targetCamRotation = Vector3.zero;
        private Vector3 _lookTarget = Vector3.zero;
        private Vector3 _defaultRotation = Vector3.zero;
        private float _defaultFOV = 60f;
        //Camera Collision
        private int _hitsCount;
        private RaycastHit[] _hitResults = new RaycastHit[5];
        private Vector3 _rayDirection;
        //Camera Input
        private float _horizontalInput = 0f;
        private float _verticalInput = 0f;
        private float _zoomInput = 0f;
        private WSMVehicleCameraLookDirection _lookDirection;
        #endregion

        #region PROPERTIES
        public Transform Target { get { return _target; } set { _target = value; InitializeVehicle(); } }
        public WSMVehicleCameraType SelectedType { get { return _selectedCamera; } set { _selectedCamera = value; } }
        public bool CameraToggleEnabled { get { return _cameraToggleEnabled; } set { _cameraToggleEnabled = value; } }
        public WSMVehicleCameraMode CameraMode { get { return _cameraMode; } set { _cameraMode = value; } }
        public bool InvertHorizontalAxis { get { return _invertHorizontalAxis; } set { _invertHorizontalAxis = value; } }
        public bool InvertVerticalAxis { get { return _invertVerticalAxis; } set { _invertVerticalAxis = value; } }
        public bool CameraCollision { get { return _cameraCollision; } set { _cameraCollision = value; } }
        public LayerMask CollisionMask { get { return _collisionMask; } set { _collisionMask = value; } }
        public float HorizontalInput { get { return _horizontalInput; } set { _horizontalInput = Mathf.Clamp(value, -1f, 1f); } }
        public float VerticalInput { get { return _verticalInput; } set { _verticalInput = Mathf.Clamp(value, -1f, 1f); } }
        public float ZoomInput { get { return _zoomInput; } set { _zoomInput = Mathf.Clamp(value, -1f, 1f); } }
        public WSMVehicleCameraLookDirection CamLookDirection { get { return _lookDirection; } set { _lookDirection = value; } }
        #endregion

        #region UNITY EVENTS

        /// <summary>
        /// On Begin Play
        /// </summary>
        void OnEnable()
        {
            InitializeCamera();
            InitializeVehicle();
            MoveToStartPosition();
        }

        private void Awake()
        {
            // Disable VSync to allow the target frame rate to be set
            //QualitySettings.vSyncCount = 0;
            // Set the target frame rate to 60 fps
            //Application.targetFrameRate = -1;
        }

        private void Update()
        {
            if (_target == null || _targetRigidbody == null || _cameraSettings == null || _camera == null)
                return;

#if UNITY_6000_0_OR_NEWER
            _vehicleSpeed = Mathf.Lerp(_vehicleSpeed, _target.InverseTransformDirection(_targetRigidbody.linearVelocity).z * 3.6f, Time.deltaTime * 3);
#else
            _vehicleSpeed = Mathf.Lerp(_vehicleSpeed, _target.InverseTransformDirection(_targetRigidbody.velocity).z * 3.6f, Time.deltaTime * 3);
#endif

            if (_selectedCamera == WSMVehicleCameraType.RearView)
            {
                if (Mathf.Abs(_vehicleSpeed) >= _cameraSettings.rearView.dampingSpeedMin)
                    _cameraDamping = Mathf.Lerp(_cameraSettings.rearView.dampingMin, _cameraSettings.rearView.dampingMax, Mathf.Abs(_vehicleSpeed) / _cameraSettings.rearView.dampingSpeedMax);
                else
                    _cameraDamping = _cameraSettings.rearView.dampingMin;
            }

            UpdateFieldOfView();
        }

        /// <summary>
        /// FixedUpdate used instead of LateUpdate to avoid camera stuttering
        /// </summary>
        void LateUpdate()
        {
            if (_target == null)
                return;

            switch (_selectedCamera)
            {
                case WSMVehicleCameraType.RearView:
                    UpdateRearCamera(Time.deltaTime);
                    break;
                case WSMVehicleCameraType.CabinView:
                    UpdateCabinCamera(Time.deltaTime, false);
                    break;
                case WSMVehicleCameraType.TopDown:
                    UpdateTopDownCamera(Time.deltaTime);
                    break;
            }
        }
        #endregion

        #region PRIVATE FUNCTIONS

        /// <summary>
        /// Handles TPS Camera
        /// </summary>
        private void UpdateRearCamera(float deltaTime = 0f)
        {
            if (_cameraSettings == null) return;

            _lookSpeed = _cameraSettings.rearView.lookSpeed * deltaTime;

            _zoom += (_zoomInput * _cameraSettings.rearView.zoomRate);
            _zoom = Math.Clamp(_zoom, _cameraSettings.rearView.minZoom, _cameraSettings.rearView.maxZoom);

            UpdateLookTarget(ref _lookTarget, ref _target, _cameraSettings.rearView.targetOffset);

            _targetCamPosition = _lookTarget; // Reset position

            Quaternion newRotation = Quaternion.Euler(0f, _target.eulerAngles.y, 0f);

            Vector3 targetDir = (_target.position - _transform.position).normalized;
            float angleYSign = -Mathf.Sign(Mathf.Round(Vector3.SignedAngle(targetDir, _target.forward, Vector3.up)));
            float angleYDiff = Quaternion.Angle(newRotation, Quaternion.Euler(0f, _transform.eulerAngles.y, 0f));
            float targetY;

            //Inside the deadzone
            if (angleYDiff <= _cameraDeadZone && _horizontalInput == 0)
                _targetCamRotation.y = (angleYDiff * angleYSign);

            if (_cameraMode == WSMVehicleCameraMode.Enhanced)
            {
                targetY = _targetCamRotation.y + _horizontalInput * (_invertHorizontalAxis ? 1f : -1f) * _lookSpeed;
                _targetCamRotation.x += _verticalInput * (_invertVerticalAxis ? -1f : 1f) * _lookSpeed;

                CameraDamping(deltaTime, angleYSign, angleYDiff, targetY);

                _targetCamRotation.y = ClampAngle180(_targetCamRotation.y);
                _targetCamRotation.x = Mathf.Clamp(_targetCamRotation.x, -_cameraSettings.rearView.verticalAngleLimit, _cameraSettings.rearView.verticalAngleLimit);

                if (_lookDirection == WSMVehicleCameraLookDirection.Backwards)
                {
                    _targetCamRotation = _defaultRotation; //Reset rotation after stoping looking back
                    newRotation = Quaternion.Euler(_target.rotation.eulerAngles + (Vector3.up * 180));
                }
                else
                    newRotation *= Quaternion.Euler(_targetCamRotation);
            }
            else if (_cameraMode == WSMVehicleCameraMode.Legacy)
            {
                switch (_lookDirection)
                {
                    case WSMVehicleCameraLookDirection.Backwards: _targetCamRotation = _defaultRotation + (Vector3.up * 180f); break;
                    case WSMVehicleCameraLookDirection.Right: _targetCamRotation = _defaultRotation + (Vector3.up * 90f); break;
                    case WSMVehicleCameraLookDirection.Left: _targetCamRotation = _defaultRotation + (Vector3.up * -90f); break;
                    default:
                        _targetCamRotation = _defaultRotation;
                        angleYDiff = Mathf.Clamp(angleYDiff, -_cameraDeadZone, _cameraDeadZone);
                        _targetCamRotation.y = Mathf.Clamp(angleYDiff * angleYSign, -_cameraDeadZone, _cameraDeadZone);
                        CameraDamping(deltaTime, angleYSign, angleYDiff, _targetCamRotation.y);
                        break;
                }

                newRotation *= Quaternion.Euler(_targetCamRotation);
            }

            _targetCamPosition += newRotation * -Vector3.forward * _zoom;
            _targetCamPosition = CameraCollisionCheck(_lookTarget, _targetCamPosition, _zoom);

            // Apply distance & Rotation
            _transform.position = _targetCamPosition;
            _transform.LookAt(_lookTarget);
        }

        /// <summary>
        /// Handles FPS Camera
        /// </summary>
        private void UpdateCabinCamera(float deltaTime = 0f, bool teleport = false)
        {
            if (_cameraSettings == null) return;

            _followSpeed = _cameraSettings.cabinView.followSpeed * deltaTime;
            _lookSpeed = _cameraSettings.cabinView.lookSpeed * deltaTime;

            if (_cameraMode == WSMVehicleCameraMode.Enhanced)
            {
                if (_lookDirection == WSMVehicleCameraLookDirection.Backwards)
                {
                    _targetCamLocalRotation = Vector2.zero;
                    _targetCamLocalRotation.y = 180f;
                    _targetCamLocalPosition = _cameraSettings.cabinView.lookBackPosition;
                }
                else if (_lookDirection == WSMVehicleCameraLookDirection.Custom)
                {
                    _targetCamLocalRotation = _cameraSettings.cabinView.customLookRotation;
                    _targetCamLocalPosition = _cameraSettings.cabinView.customLookPosition;
                }
                else if (_targetCamLocalRotation.y == 180f || (_targetCamLocalPosition == _cameraSettings.cabinView.customLookPosition)) //lookback or custom released
                {
                    _targetCamLocalRotation = Vector2.zero;
                    _targetCamLocalPosition = _cameraSettings.cabinView.defaultLookPosition;
                }
                else
                {
                    _targetCamLocalRotation.y += _horizontalInput * (_invertHorizontalAxis ? -1f : 1f) * _lookSpeed;
                    _targetCamLocalRotation.x += _verticalInput * (_invertVerticalAxis ? 1f : -1f) * _lookSpeed;

                    _targetCamLocalRotation.y = Mathf.Clamp(_targetCamLocalRotation.y, -_cameraSettings.cabinView.horizontalAngleLimit, _cameraSettings.cabinView.horizontalAngleLimit);
                    _targetCamLocalRotation.x = Mathf.Clamp(_targetCamLocalRotation.x, -_cameraSettings.cabinView.verticalAngleLimit, _cameraSettings.cabinView.verticalAngleLimit);
                }
            }
            else if (_cameraMode == WSMVehicleCameraMode.Legacy)
            {
                _lookSpeed = _followSpeed; //Use a single speed on legacy cabin camera
                _targetCamLocalRotation = Vector3.zero;

                switch (_lookDirection)
                {
                    case WSMVehicleCameraLookDirection.Backwards:
                        _targetCamLocalRotation.y = 180;
                        _targetCamLocalPosition = _cameraSettings.cabinView.lookBackPosition;
                        break;
                    case WSMVehicleCameraLookDirection.Right:
                        _targetCamLocalRotation.y = _cameraSettings.cabinView.horizontalAngleLimit;
                        _targetCamLocalPosition = _cameraSettings.cabinView.sideLookPosition;
                        break;
                    case WSMVehicleCameraLookDirection.Left:
                        _targetCamLocalRotation.y = -_cameraSettings.cabinView.horizontalAngleLimit;
                        _targetCamLocalPosition = _cameraSettings.cabinView.sideLookPosition;
                        break;
                    case WSMVehicleCameraLookDirection.Up:
                        _targetCamLocalRotation.x = -_cameraSettings.cabinView.verticalAngleLimit;
                        _targetCamLocalPosition = _cameraSettings.cabinView.defaultLookPosition;
                        break;
                    case WSMVehicleCameraLookDirection.Down:
                        _targetCamLocalRotation.x = _cameraSettings.cabinView.verticalAngleLimit;
                        _targetCamLocalPosition = _cameraSettings.cabinView.lookDownPosition;
                        break;
                    case WSMVehicleCameraLookDirection.Custom:
                        _targetCamLocalRotation = _cameraSettings.cabinView.customLookRotation;
                        _targetCamLocalPosition = _cameraSettings.cabinView.customLookPosition;
                        break;
                    default:
                        _targetCamLocalRotation = Vector3.zero;
                        _targetCamLocalPosition = _cameraSettings.cabinView.defaultLookPosition;
                        break;
                }
            }

            if (teleport)
            {
                _transform.localPosition = _targetCamLocalPosition;
                _transform.localRotation = Quaternion.Euler(_targetCamLocalRotation);
            }
            else
            {
                _transform.localPosition = Vector3.MoveTowards(_transform.localPosition, _targetCamLocalPosition, _followSpeed);
                _transform.localRotation = Quaternion.Slerp(_transform.localRotation, Quaternion.Euler(_targetCamLocalRotation), _lookSpeed);
            }
        }

        /// <summary>
        /// Handles Top Down Camera
        /// </summary>
        private void UpdateTopDownCamera(float deltaTime = 0f)
        {
            if (_cameraSettings == null) return;

            _lookSpeed = _cameraSettings.topDown.lookSpeed * deltaTime;

            _targetCamPosition = _target.position;
            _lookTarget = _target.position;

            if (_cameraMode == WSMVehicleCameraMode.Enhanced)
            {
                _targetCamRotation.y += _horizontalInput * (_invertHorizontalAxis ? 1f : -1f) * _lookSpeed;
                _targetCamRotation.y = _targetCamRotation.y % 360;

                //horizontal direction
                _targetCamPosition += (Quaternion.Euler(_targetCamRotation) * -Vector3.forward).normalized * Math.Abs(_cameraSettings.topDown.positionOffset.x);
                _targetCamPosition.y += Math.Abs(_cameraSettings.topDown.positionOffset.y); //height
            }
            else if (_cameraMode == WSMVehicleCameraMode.Legacy)
            {
                //horizontal direction
                _targetCamPosition += (Quaternion.Euler(0f, _cameraSettings.topDown.initialYaw, 0f) * -Vector3.forward).normalized * Math.Abs(_cameraSettings.topDown.positionOffset.x);
                _targetCamPosition.y += Math.Abs(_cameraSettings.topDown.positionOffset.y); //height
            }

            _targetCamPosition = CameraCollisionCheck(_lookTarget, _targetCamPosition, Vector3.Distance(_lookTarget, _targetCamPosition));

            _transform.position = _targetCamPosition;
            _transform.LookAt(_lookTarget);
        }

        /// <summary>
        /// Update FOV on higher speeds
        /// </summary>
        private void UpdateFieldOfView()
        {
            switch (_selectedCamera)
            {
                case WSMVehicleCameraType.RearView:
                    _camera.fieldOfView = Mathf.Lerp(_defaultFOV, _defaultFOV + _cameraSettings.rearView.FOVDistortion, Mathf.Abs(_vehicleSpeed) / _cameraSettings.rearView.FOVDistortionSpeedLimit);
                    break;
                case WSMVehicleCameraType.CabinView:
                    _camera.fieldOfView = Mathf.Lerp(_defaultFOV, _defaultFOV + _cameraSettings.cabinView.FOVDistortion, Mathf.Abs(_vehicleSpeed) / _cameraSettings.cabinView.FOVDistortionSpeedLimit);
                    break;
                case WSMVehicleCameraType.TopDown:
                    _camera.fieldOfView = Mathf.Lerp(_defaultFOV, _defaultFOV + _cameraSettings.topDown.FOVDistortion, Mathf.Abs(_vehicleSpeed) / _cameraSettings.topDown.FOVDistortionSpeedLimit);
                    break;
            }
        }

        /// <summary>
        /// Rear View camera damping
        /// </summary>
        /// <param name="deltaTime"></param>
        /// <param name="angleYSign"></param>
        /// <param name="angleYDiff"></param>
        /// <param name="targetY"></param>
        private void CameraDamping(float deltaTime, float angleYSign, float angleYDiff, float targetY)
        {
            //Rotation Damping
            if (angleYDiff <= _cameraDeadZone && _horizontalInput == 0)
            {
                targetY = 0.01f * angleYSign; //Using a small value instead of ZERO, reduces the camera stucking on zero y rotation.
                _targetCamRotation.y = Mathf.LerpAngle(_targetCamRotation.y, targetY, _cameraDamping * deltaTime);
            }
            else
                _targetCamRotation.y = targetY;
        }

        /// <summary>
        /// Camera Collision Check
        /// </summary>
        /// <param name="lookTarget"></param>
        /// <param name="targetCamPosition"></param>
        /// <param name="maxDistance"></param>
        /// <returns></returns>
        private Vector3 CameraCollisionCheck(Vector3 lookTarget, Vector3 targetCamPosition, float maxDistance)
        {
            if (!_cameraCollision) return targetCamPosition;

            _rayDirection = (targetCamPosition - lookTarget).normalized;
            _hitsCount = Physics.RaycastNonAlloc(lookTarget, _rayDirection, _hitResults, maxDistance, _collisionMask, QueryTriggerInteraction.Ignore);

            if (_hitsCount > 0)
            {
                Array.Sort(_hitResults, (RaycastHit x, RaycastHit y) => x.distance.CompareTo(y.distance));

                for (int i = 0; i < _hitsCount; i++)
                {
                    if (_hitResults[i].transform == null) continue;
                    if (_hitResults[i].transform == _target) continue;

                    if (_hitResults[i].transform.gameObject.isStatic)
                        return _hitResults[i].point;
                }
            }

            return targetCamPosition;
        }

        /// <summary>
        /// Make sure the camera is parented to the target
        /// </summary>
        private void EnforceTargetParenting()
        {
            if (_transform.parent != _target)
                _transform.SetParent(_target);
        }

        /// <summary>
        /// Make sure the camera is NOT parented to the target
        /// </summary>
        private void ClearTargetParenting()
        {
            if (_transform.parent == _target)
                _transform.SetParent(null);
        }

        /// <summary>
        /// Reset Rear Camera
        /// </summary>
        private void ResetRearCamera()
        {
            _camera.orthographic = false;
            _camera.fieldOfView = _defaultFOV;
            _defaultRotation.x = Mathf.Clamp(_cameraSettings.rearView.defaultVerticalAngle, -_cameraSettings.rearView.verticalAngleLimit, _cameraSettings.rearView.verticalAngleLimit);
            _targetCamRotation = _defaultRotation;
            _zoom = Math.Clamp(_cameraSettings.rearView.defaultZoom, _cameraSettings.rearView.minZoom, _cameraSettings.rearView.maxZoom);
            ClearTargetParenting();
            UpdateRearCamera();
        }

        /// <summary>
        /// Reset Cabin Camera
        /// </summary>
        private void ResetCabinCamera()
        {
            _camera.orthographic = false;
            _camera.fieldOfView = _defaultFOV;
            _targetCamLocalRotation = Vector2.zero;
            _targetCamLocalPosition = _cameraSettings.cabinView.defaultLookPosition;
            EnforceTargetParenting();
            UpdateCabinCamera(0f, true);
        }

        /// <summary>
        /// Reset Top Down Camera
        /// </summary>
        private void ResetTopDowCamera()
        {
            _camera.fieldOfView = _defaultFOV;
            _camera.orthographic = _cameraSettings.topDown.orthographicCam;
            _targetCamRotation = new Vector3(0f, _cameraSettings.topDown.initialYaw, 0f);
            ClearTargetParenting();
            UpdateTopDownCamera(0f);
        }

        /// <summary>
        /// Clamps angle between -180 and 180
        /// </summary>
        /// <param name="angle"></param>
        /// <returns></returns>
        private float ClampAngle180(float angle)
        {
            // Normalize the angle to be within the range of -360 to 360
            angle %= 360f;

            // Adjust angles greater than 180 to their negative equivalent
            if (angle > 180f)
                return angle - 360f;

            // Adjust angles less than or equal to -180 to their positive equivalent
            if (angle <= -180f)
                return angle + 360f;

            // If already in the desired range, return as is
            return angle;
        }

        /// <summary>
        /// Update look target position
        /// </summary>
        /// <param name="lookTarget"></param>
        /// <param name="target"></param>
        /// <param name="offset"></param>
        private void UpdateLookTarget(ref Vector3 lookTarget, ref Transform target, Vector3 offset)
        {
            lookTarget = target.position;
            lookTarget = lookTarget + (target.right * offset.x);
            lookTarget = lookTarget + (target.up * offset.y);
            lookTarget = lookTarget + (target.forward * offset.z);
        }

        #endregion

        #region PUBLIC FUNCTIONS

        /// <summary>
        /// Initialize camera variables
        /// </summary>
        public void InitializeCamera(bool customEditor = false)
        {
            Cursor.lockState = CursorLockMode.Locked;
            _camera = GetComponent<Camera>();
            _transform = GetComponent<Transform>();
            _selectedCameraIndex = (int)_selectedCamera;
            _cameraTypesLength = Enum.GetNames(typeof(WSMVehicleCameraType)).Length;

            if (!customEditor)
                _defaultFOV = _camera.fieldOfView; //Camera is marked has a RequiredComponent
        }

        /// <summary>
        /// Initialize vehicle variables
        /// </summary>
        public void InitializeVehicle()
        {
            if (_target == null)
                _targetRigidbody = null;
            else
                _targetRigidbody = _target.GetComponent<Rigidbody>();
        }

        /// <summary>
        /// Reset camera to start position
        /// </summary>
        public void MoveToStartPosition()
        {
            if (_cameraSettings == null) return;

            if (_target == null)
                return;

            if (_camera == null)
                _camera = GetComponent<Camera>();

            if (_transform == null)
                _transform = GetComponent<Transform>();

            switch (_selectedCamera)
            {
                case WSMVehicleCameraType.RearView:
                    ResetRearCamera();
                    break;
                case WSMVehicleCameraType.CabinView:
                    ResetCabinCamera();
                    break;
                case WSMVehicleCameraType.TopDown:
                    ResetTopDowCamera();
                    break;
            }
        }

        /// <summary>
        /// Toggle camera
        /// </summary>
        public void ToggleCamera()
        {
            if (_cameraToggleEnabled)
            {
                int previousCam = _selectedCameraIndex;

                _selectedCameraIndex++;
                _selectedCameraIndex = _selectedCameraIndex % _cameraTypesLength;

                bool[] activeCameras = new bool[]
                {
                    _activeCameraTypes.RearView,
                    _activeCameraTypes.CabinView,
                    _activeCameraTypes.TopDown
                };

                int iteration = 1;
                int maxIterations = activeCameras.Length;
                while (!activeCameras[_selectedCameraIndex])
                {
                    if (iteration > maxIterations)
                    {
                        _selectedCameraIndex = previousCam; //if no cameras are enabled, defaults to starting cam
                        break;
                    }

                    _selectedCameraIndex++;
                    _selectedCameraIndex = _selectedCameraIndex % _cameraTypesLength;
                    iteration++;
                }

                _selectedCamera = (WSMVehicleCameraType)_selectedCameraIndex;

                MoveToStartPosition();
            }
        }
        #endregion
    }
}