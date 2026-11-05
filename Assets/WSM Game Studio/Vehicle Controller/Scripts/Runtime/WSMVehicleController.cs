using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WSMGameStudio.GlowMesh;

namespace WSMGameStudio.Vehicles
{
    public enum LightSystem
    {
        GlowMesh,
        Legacy
    }

    [RequireComponent(typeof(Rigidbody))]
    public class WSMVehicleController : MonoBehaviour, IMotorizedVehicle
    {
        #region VARIABLES

        private const float _mphConversion = 2.23694f;
        private const float _kphConversion = 3.6f;

        public WSMVehicleDrivetrainType drivetrainType;
        public WSMVehicleSteeringMode steeringMode;
        public WSMVehicleSpeedUnit speedUnit;
        private WSMVehicleTransmissionType _transmissionType = WSMVehicleTransmissionType.Automatic;

        [SerializeField] public WheelCollider[] frontWheelsColliders;
        [SerializeField] public WheelCollider[] rearWheelsColliders;
        [SerializeField] public WheelCollider[] extraWheelsColliders;
        [SerializeField] public GameObject[] frontWheelsMeshes;
        [SerializeField] public GameObject[] rearWheelsMeshes;
        [SerializeField] public GameObject[] extraWheelsMeshes;
        [SerializeField] public Transform steeringWheel;
        [SerializeField] public Transform gasPedal;
        [SerializeField] public Transform brakesPedal;
        [SerializeField] public Transform clutchPedal;
        [SerializeField] public Transform centerOfMass;
        [SerializeField] public Transform articulatedVehicle_Front;
        [SerializeField] public Transform articulatedVehicle_Rear;
        [SerializeField] public AudioSource engineStartSFX;
        [SerializeField] public AudioSource engineSFX;
        [SerializeField] public AudioSource hornSFX;
        [SerializeField] public AudioSource wheelsSkiddingSFX;
        [SerializeField] public AudioSource backUpBeeperSFX;
        [SerializeField] public AudioSource signalLightsSFX;
        [SerializeField] public AudioSource openDoorSFX;
        [SerializeField] public AudioSource closeDoorSFX;
        [SerializeField] public Text speedText;
        //New Lights
        [SerializeField] private LightSystem lightSystem;
        //EditorGUILayout.PropertyField Doesnt support interfaces, therefore, it was not possible to use ILamp references
        [SerializeField] private GlowMeshLamp headlightLamps;
        [SerializeField] private GlowMeshLamp rearLightsLamps;
        [SerializeField] private GlowMeshLamp brakeLightsLamps;
        [SerializeField] private GlowMeshLamp interiorLamps;
        [SerializeField] private GlowMeshLamp signalLampsLeft;
        [SerializeField] private GlowMeshLamp signalLampsRight;
        [SerializeField] private GlowMeshLamp reverseAlarmLamps;
        //Old Lights
        [SerializeField] public Light[] headlights;
        [SerializeField] public Light[] rearLights;
        [SerializeField] public Light[] brakeLights;
        [SerializeField] public Light[] interiorLights;
        [SerializeField] public Light[] signalLightsLeft;
        [SerializeField] public Light[] signalLightsRight;
        [SerializeField] public Light[] reverseAlarmLights;
        [SerializeField] public Transform alarmLightsPivot;
        [SerializeField] public WSMVehicleDoor driverDoor;
        [SerializeField] public WSMVehicleDoor[] passengersDoors;
        // Settings
        [SerializeField] private bool _isEngineOn = true;
        [SerializeField] [Range(0f, 180f)] private float _maxSteeringAngle = 35f;
        [SerializeField] [Range(0f, 180f)] private float _highSpeedSteeringAngle = 10f;
        [SerializeField] [Range(0f, 180f)] private float _maxArticulationAngle = 45f;
        [SerializeField] [Range(0f, 180f)] private float _highSpeedArticulationAngle = 45f;
        [SerializeField] private AnimationCurve _articulationDampingCurve = AnimationCurve.EaseInOut(0f, 0.7f, 1f, 0f);
        [SerializeField] private float _maxTorque = 1000f;
        [SerializeField] private float _reverseTorque = 500f;
        [SerializeField] private float _brakesTorque = 5000f;
        [SerializeField] private float _handbrakeTorque = 10000f;
        [SerializeField] private float _idleEngineRevs = 600f; // Engine RPM when vehicle is idling
        [SerializeField] private float _maxEngineRevs = 6000f; // Maximum engine RPM
        [SerializeField] private float _maxSpeed = 150f;
        [SerializeField] private float _maxReverseSpeed = 20f;
        [SerializeField] private bool _softSteering = true;
        [SerializeField] private float _steeringWheelAngleMultiplier = 6f;
        [SerializeField] private float _downforce = 100f;
        [SerializeField] private int _numberOfGears = 6;
        [SerializeField] [Range(0f, 1f)] private float _steerHelper = 1;
        [SerializeField] [Range(0f, 1f)] private float _tractionControl = 1f;
        [SerializeField] private float _tractionSlipLimit = 0.6f;
        [SerializeField] private Vector3 _centerOfMassOffset = Vector3.zero;
        [SerializeField] private float _minEnginePitch = 1f;
        [SerializeField] private float _maxEnginePitch = 2f;
        [SerializeField] private float _sfxForwardSlipLimit = 0.9f;
        [SerializeField] private float _sfxSidewaysSlipLimit = 0.6f;
        [SerializeField] private bool _headlightsOn = false;
        [SerializeField] private bool _interiorLightsOn = false;
        [SerializeField] private bool _rotateAlarmLights = false;
        [SerializeField] private float _alarmLightsRotationSpeed = 10f;
        [SerializeField] private bool _blinkAlarmLights = false;

        // Local only
        private Rigidbody _rigidbody;
        private WheelCollider[] _steeringWheelsColliders;
        private List<WheelCollider> _torqueWheelsColliders;
        private List<WheelCollider> _allWheelsColliders;
        private float _torqueWheelsCount = 0f;
        private float _individualWheelTorque = 0f;
        private float _individualWheelReverseTorque = 0f;
        private float _thrustTorque = 0f;
        private float _currentTorque = 0f;
        private float _currentSteerAngle = 0f;
        private float _frontArticulation_CurrentSteerAngle = 0f;
        private float _rearArticulation_CurrentSteerAngle = 0f;
        private float _steerHelperLastRotation = 0f;
        private float _currentGasPedalAngle = 0f;
        private float _currentBrakesPedalAngle = 0f;
        private float _currentClutchPedalAngle = 0f;
        private float _currentRevs = 0f; // Calculated only when the Revs propertie is requested, if you need this, call Revs instead
        private bool _leftSinalLightsOn = false;
        private bool _rightSinalLightsOn = false;
        private bool _movingBackwards = false;
        private float _defaultSteeringSpeed = 100f;
        private float _articulatedSteeringSpeed = 50f;//25f;

        private Vector3 _wheelPosition;
        private Quaternion _wheelRotation;

        // Input
        private float _steering = 0f;
        private float _acceleration = 0f;
        private float _brakes = 0f;
        private float _handbrake = 0f;
        private float _clutch = 0f;
        //Other Settings
        private int _currentGear = 0;
        private float _currentSpeed = 0f;
        private float[] _gearsSpeedLimits;

        #endregion

        #region PROPERTIES

        // Inputs
        public float SteeringInput { get { return _steering; } set { _steering = Mathf.Clamp(value, -1f, 1f); } }
        public float AccelerationInput { get { return _acceleration; } set { _acceleration = Mathf.Clamp(value, -1f, 1f); } }
        public float BrakesInput { get { return _brakes; } set { _brakes = Mathf.Clamp01(value); } }
        public float HandBrakeInput { get { return _handbrake; } set { _handbrake = Mathf.Clamp01(value); } }
        public float ClutchInput { get { return _clutch; } set { _clutch = Mathf.Clamp01(value); } }

        // Settings
        public float MaxTorque { get { return _maxTorque; } set { _maxTorque = Mathf.Abs(value); } }
        public float ReverseTorque { get { return _reverseTorque; } set { _reverseTorque = Mathf.Abs(value); } }
        public float BrakesTorque { get { return _brakesTorque; } set { _brakesTorque = Mathf.Abs(value); } }
        public float HandbrakesTorque { get { return _handbrakeTorque; } set { _handbrakeTorque = Mathf.Abs(value); } }
        public float MaxSpeed { get { return _maxSpeed; } set { _maxSpeed = Mathf.Abs(value); } }
        public float MaxReverseSpeed { get { return _maxReverseSpeed; } set { _maxReverseSpeed = Mathf.Abs(value); } }
        public float Downforce { get { return _downforce; } set { _downforce = Mathf.Abs(value); } }
        public float MaxSteeringAngle { get { return _maxSteeringAngle; } set { _maxSteeringAngle = Mathf.Clamp(value, 0f, 180f); } }
        public float HighSpeedSteeringAngle { get { return _highSpeedSteeringAngle; } set { _highSpeedSteeringAngle = Mathf.Clamp(value, 0f, 180f); } }
        public float MaxArticulatedSteeringAngle { get { return _maxArticulationAngle; } set { _maxArticulationAngle = Mathf.Clamp(value, 0f, 180f); } }
        public float HighSpeedArticulatedSteeringAngle { get { return _highSpeedArticulationAngle; } set { _highSpeedArticulationAngle = Mathf.Clamp(value, 0f, 180f); } }
        public bool SoftSteering { get { return _softSteering; } set { _softSteering = value; } }
        public int NumberOfGears { get { return _numberOfGears; } set { _numberOfGears = Mathf.Abs(value); } }
        public float SteerHelper { get { return _steerHelper; } set { _steerHelper = Mathf.Clamp01(value); } }
        public float TractionControl { get { return _tractionControl; } set { _tractionControl = Mathf.Clamp01(value); } }
        public float TractionSlipLimit { get { return _tractionSlipLimit; } set { _tractionSlipLimit = Mathf.Abs(value); } }
        public float SfxForwardSlipLimit { get { return _sfxForwardSlipLimit; } set { _sfxForwardSlipLimit = Mathf.Abs(value); } }
        public float SfxSidewaysSlipLimit { get { return _sfxSidewaysSlipLimit; } set { _sfxSidewaysSlipLimit = Mathf.Abs(value); } }
        public bool RotateAlarmLights { get { return _rotateAlarmLights; } set { _rotateAlarmLights = value; } }
        public float AlarmLightsRotationSpeed { get { return _alarmLightsRotationSpeed; } set { _alarmLightsRotationSpeed = value; } }

        public float CurrentSpeed { get { return _currentSpeed; } }
        public int CurrentGear { get { return _currentGear; } }

        public bool IsBraking { get { return ( _isEngineOn? (_handbrake > 0f || _brakes > 0f) : _brakes > 0f); } }

        public bool IsMoving { get { return (Mathf.RoundToInt(_currentSpeed) > 0); } }

        public bool ArticulatedVehicle { get { return steeringMode == WSMVehicleSteeringMode.ArticulatedSteering; } }

        public float Revs
        {
            get
            {
                if (_isEngineOn)
                {
                    _currentGear = (_currentGear <= 0) ? 1 : _currentGear;

                    float speedGearRatio = _currentSpeed / _gearsSpeedLimits[_currentGear - 1];
                    _currentRevs = Mathf.Lerp(_idleEngineRevs, _maxEngineRevs, speedGearRatio);
                    _currentRevs = Mathf.Round(_currentRevs);
                }
                else
                    _currentRevs = 0f;

                return _currentRevs;
            }
        }

        public bool IsEngineOn
        {
            get { return _isEngineOn; }
            set
            {
                if (!_isEngineOn && value)
                    StartEngine();
                else if (_isEngineOn && !value)
                    StopEngine();
            }
        }

        public float IdleEngineRevs
        {
            get { return _idleEngineRevs; }
            set
            {
                _idleEngineRevs = Mathf.Abs(value);
                _maxEngineRevs = _maxEngineRevs < _idleEngineRevs ? _idleEngineRevs : _maxEngineRevs;
            }
        }
        public float MaxEngineRevs
        {
            get { return _maxEngineRevs; }
            set
            {
                _maxEngineRevs = value < _idleEngineRevs ? _idleEngineRevs : Mathf.Abs(value);
            }
        }

        public float SteeringWheelAngleMultiplier
        {
            get { return _steeringWheelAngleMultiplier; }
            set
            {
                _steeringWheelAngleMultiplier = Mathf.Abs(value);
                _steeringWheelAngleMultiplier = _steeringWheelAngleMultiplier < 1f ? 1f : _steeringWheelAngleMultiplier;
            }
        }

        public Vector3 CenterOfMassOffset
        {
            get { return _centerOfMassOffset; }
            set
            {
                _centerOfMassOffset = value;

                if (centerOfMass != null) centerOfMass.localPosition = _centerOfMassOffset;
                if (_rigidbody != null) _rigidbody.centerOfMass = _centerOfMassOffset;
            }
        }

        public float MinEnginePitch
        {
            get { return _minEnginePitch; }
            set
            {
                _minEnginePitch = value;
                _maxEnginePitch = _maxEnginePitch < _minEnginePitch ? _minEnginePitch : _maxEnginePitch;
            }
        }

        public float MaxEnginePitch
        {
            get { return _maxEnginePitch; }
            set
            {
                _maxEnginePitch = value < _minEnginePitch ? _minEnginePitch : value;
            }
        }

        public bool HeadlightsOn
        {
            get { return _headlightsOn; }
            set
            {
                _headlightsOn = value;

                if (lightSystem == LightSystem.GlowMesh)
                {
                    if (headlightLamps != null)
                        headlightLamps.LightsOn = _headlightsOn;

                    if (rearLightsLamps != null)
                        rearLightsLamps.LightsOn = _headlightsOn;
                }
                else if (lightSystem == LightSystem.Legacy)
                {
                    if (headlights != null)
                        foreach (var headlight in headlights)
                            headlight.enabled = _headlightsOn;

                    if (rearLights != null)
                        foreach (var rearLight in rearLights)
                            rearLight.enabled = _headlightsOn;
                }
            }
        }

        public bool InteriorLightsOn
        {
            get { return _interiorLightsOn; }
            set
            {
                _interiorLightsOn = value;

                if (lightSystem == LightSystem.GlowMesh && interiorLamps != null)
                {
                    interiorLamps.LightsOn = _interiorLightsOn;
                }
                else if (lightSystem == LightSystem.Legacy && interiorLights != null)
                {
                    foreach (var interiorLight in interiorLights)
                        interiorLight.enabled = _interiorLightsOn;
                }
            }
        }

        public bool LeftSinalLightsOn
        {
            get { return _leftSinalLightsOn; }
            set
            {
                _leftSinalLightsOn = value;

                if (lightSystem == LightSystem.GlowMesh)
                {
                    if (_leftSinalLightsOn)
                    {
                        signalLampsRight?.StopBlinking();
                        signalLampsLeft?.StartBlinking();
                        RightSinalLightsOn = false;
                    }
                    else
                        signalLampsLeft?.StopBlinking();
                }
                else if (lightSystem == LightSystem.Legacy)
                {
                    if (_leftSinalLightsOn)
                    {
                        InvokeRepeating("LeftSignalLights", 0.2f, 0.2f);
                        RightSinalLightsOn = false;
                    }
                    else
                    {
                        CancelInvoke("LeftSignalLights");
                        ToggleLights(signalLightsLeft, false);
                    }
                }
            }
        }

        public bool RightSinalLightsOn
        {
            get { return _rightSinalLightsOn; }
            set
            {
                _rightSinalLightsOn = value;

                if (lightSystem == LightSystem.GlowMesh)
                {
                    if (_rightSinalLightsOn)
                    {
                        signalLampsLeft?.StopBlinking();
                        signalLampsRight?.StartBlinking();
                        LeftSinalLightsOn = false;
                    }
                    else
                        signalLampsRight?.StopBlinking();
                }
                else if (lightSystem == LightSystem.Legacy)
                {
                    if (_rightSinalLightsOn)
                    {
                        InvokeRepeating("RightSignalLights", 0.2f, 0.2f);
                        LeftSinalLightsOn = false;
                    }
                    else
                    {
                        CancelInvoke("RightSignalLights");
                        ToggleLights(signalLightsRight, false);
                    }
                }
            }
        }

        public LightSystem LightSystem { get => lightSystem; set => lightSystem = value; }
        public bool BlinkAlarmLights { get => _blinkAlarmLights; set => _blinkAlarmLights = value; }

        #endregion

        #region UNITY DEFAULT EVENTS
        /// <summary>
        /// Use this for initialization
        /// </summary>
        void Start()
        {
            InitializeVehicle();

            if (_isEngineOn && engineSFX != null && !engineSFX.isPlaying)
                engineSFX.Play();
        }

        /// <summary>
        /// Update physics
        /// </summary>
        void FixedUpdate()
        {
            Update_Steering(Time.fixedDeltaTime);

            Update_Gearbox();
            Update_Engine(Time.fixedDeltaTime);

            ApplyDownForce();
            ApplyTractionControl();

            Update_Brakes();
        }

        /// <summary>
        /// Update visuals
        /// </summary>
        private void Update()
        {
            UpdateWheelMeshesRotation(frontWheelsColliders, frontWheelsMeshes);
            UpdateWheelMeshesRotation(rearWheelsColliders, rearWheelsMeshes);
            UpdateWheelMeshesRotation(extraWheelsColliders, extraWheelsMeshes);

            Update_Pedals(Time.deltaTime);
            Update_UI();
            Update_SFX();
            Update_Lights();
        }

        #endregion

        #region PRIVATE VEHICLE METHODS

        /// <summary>
        /// Initialize vehicle
        /// </summary>
        private void InitializeVehicle()
        {
            _rigidbody = GetComponent<Rigidbody>();

            if (centerOfMass != null) centerOfMass.localPosition = _centerOfMassOffset;
            if (_rigidbody != null) _rigidbody.centerOfMass = _centerOfMassOffset;

            // Set steering wheels
            switch (steeringMode)
            {
                case WSMVehicleSteeringMode.FrontWheelSteering:
                    _steeringWheelsColliders = frontWheelsColliders;
                    break;
                case WSMVehicleSteeringMode.RearWheelSteering:
                    _steeringWheelsColliders = rearWheelsColliders;
                    break;
                case WSMVehicleSteeringMode.ArticulatedSteering:
                    break;
            }

            _allWheelsColliders = new List<WheelCollider>();
            _allWheelsColliders.AddRange(frontWheelsColliders);
            _allWheelsColliders.AddRange(rearWheelsColliders);
            _allWheelsColliders.AddRange(extraWheelsColliders);

            // Wheels connected to the drivetrain
            _torqueWheelsColliders = new List<WheelCollider>();
            switch (drivetrainType)
            {
                case WSMVehicleDrivetrainType.FWD:
                    _torqueWheelsColliders.AddRange(frontWheelsColliders);
                    break;
                case WSMVehicleDrivetrainType.RWD:
                    _torqueWheelsColliders.AddRange(rearWheelsColliders);
                    break;
                case WSMVehicleDrivetrainType.AWD:
                    _torqueWheelsColliders.AddRange(frontWheelsColliders);
                    _torqueWheelsColliders.AddRange(rearWheelsColliders);
                    _torqueWheelsColliders.AddRange(extraWheelsColliders);
                    break;
            }

            // Calculate individual wheels torque
            _torqueWheelsCount = _torqueWheelsColliders.Count;
            if (_torqueWheelsCount > 0)
            {
                _individualWheelTorque = (_maxTorque / _torqueWheelsCount);
                _individualWheelReverseTorque = (_reverseTorque / _torqueWheelsCount);
            }

            // Calculate gears speed limit array
            float speedGearFactor = (_maxSpeed / _numberOfGears);
            _gearsSpeedLimits = new float[_numberOfGears];
            for (int i = 0; i < _gearsSpeedLimits.Length; i++)
            {
                _gearsSpeedLimits[i] = Mathf.RoundToInt(speedGearFactor * (i + 1));
            }

            _currentTorque = _maxTorque - (_tractionControl * _maxTorque);

            if (driverDoor != null)
            {
                driverDoor.OpenSFX = openDoorSFX;
                driverDoor.CloseSFX = closeDoorSFX;
            }

            if (passengersDoors != null)
            {
                for (int i = 0; i < passengersDoors.Length; i++)
                {
                    passengersDoors[i].OpenSFX = openDoorSFX;
                    passengersDoors[i].CloseSFX = closeDoorSFX;
                }
            }
        }

        /// <summary>
        /// Handles wheels steering
        /// </summary>
        private void Update_Steering(float deltaTime)
        {
            #region NON-ARTICULATED VEHICLES

            if (!ArticulatedVehicle)
            {
                float speedRatio = (_currentSpeed / _maxSpeed);
                float targetSteerAngle = Mathf.LerpAngle(_highSpeedSteeringAngle, _maxSteeringAngle, (1f - speedRatio));

                if (_softSteering)
                    _currentSteerAngle = Mathf.MoveTowards(_currentSteerAngle, _steering * targetSteerAngle, _defaultSteeringSpeed * deltaTime);
                else
                    _currentSteerAngle = _steering * targetSteerAngle;

                // Steering Wheel
                if (steeringWheel != null)
                    steeringWheel.localEulerAngles = new Vector3(0f, _currentSteerAngle * _steeringWheelAngleMultiplier, 0f);

                // Wheels
                if (_steeringWheelsColliders != null)
                {
                    for (int i = 0; i < _steeringWheelsColliders.Length; i++)
                        _steeringWheelsColliders[i].steerAngle = _currentSteerAngle;
                }
            }
            #endregion

            #region ARTICULATED VEHICLES

            bool nonMovingArticulatedSteering = false; //!EXPERIMENTAL!

            if (ArticulatedVehicle && (IsMoving || nonMovingArticulatedSteering))
            {
                float speedRatio = (_currentSpeed / _maxSpeed);
                float frontAngleRatio = Mathf.Abs(_frontArticulation_CurrentSteerAngle / MaxArticulatedSteeringAngle);
                float rearAngleRatio = Mathf.Abs(_rearArticulation_CurrentSteerAngle / MaxArticulatedSteeringAngle);
                
                float steeringWheelSpeed = nonMovingArticulatedSteering ? _articulatedSteeringSpeed : (_articulatedSteeringSpeed * speedRatio);
                float frontSteeringSpeed = steeringWheelSpeed * (_movingBackwards ? (1f - _articulationDampingCurve.Evaluate(frontAngleRatio)) : 1f);
                float rearSteeringSpeed = steeringWheelSpeed * (_movingBackwards ? 1f : (1f - _articulationDampingCurve.Evaluate(rearAngleRatio)));

                float targetSteerAngle = Mathf.LerpAngle(_highSpeedArticulationAngle, _maxArticulationAngle, (1f - speedRatio));
                float targetArticulationAngle = Mathf.LerpAngle(_highSpeedArticulationAngle * 0.5f, _maxArticulationAngle * 0.5f, (1f - speedRatio));

                if (_softSteering)
                {
                    _currentSteerAngle = Mathf.MoveTowards(_currentSteerAngle, _steering * targetSteerAngle, steeringWheelSpeed * deltaTime);
                    _frontArticulation_CurrentSteerAngle = Mathf.MoveTowards(_frontArticulation_CurrentSteerAngle, _steering * targetArticulationAngle, frontSteeringSpeed * deltaTime);
                    _rearArticulation_CurrentSteerAngle = Mathf.MoveTowards(_rearArticulation_CurrentSteerAngle, _steering * targetArticulationAngle, rearSteeringSpeed * deltaTime);
                }
                else
                {
                    _currentSteerAngle = _steering * targetSteerAngle;
                    _frontArticulation_CurrentSteerAngle = _steering * targetArticulationAngle;
                    _rearArticulation_CurrentSteerAngle = _steering * targetArticulationAngle;
                }

                // Steering Wheel
                if (steeringWheel != null)
                    steeringWheel.localEulerAngles = new Vector3(0f, _currentSteerAngle * _steeringWheelAngleMultiplier, 0f);

                if (articulatedVehicle_Front != null)
                    articulatedVehicle_Front.localEulerAngles = new Vector3(0f, _frontArticulation_CurrentSteerAngle, 0f);

                if (articulatedVehicle_Rear != null)
                    articulatedVehicle_Rear.localEulerAngles = new Vector3(0f, -_rearArticulation_CurrentSteerAngle, 0f);

                //Wheels
                if (frontWheelsColliders != null)
                {
                    for (int i = 0; i < frontWheelsColliders.Length; i++)
                        frontWheelsColliders[i].steerAngle = _frontArticulation_CurrentSteerAngle;
                }

                if (rearWheelsColliders != null)
                {
                    for (int i = 0; i < rearWheelsColliders.Length; i++)
                        rearWheelsColliders[i].steerAngle = -_rearArticulation_CurrentSteerAngle;
                }
            }
            #endregion
            
            ApplySteerHelper();

            #region DEBUGGING
            //if (_isEngineOn)
            //{
            //    WSMGameStudio.Utilities.ConsoleUtils.ClearLogConsole();
            //    Debug.Log(string.Format("{0}Front: {1}º | Rear: {2}º | Total: {3}",
            //        System.Environment.NewLine,
            //        Mathf.Round(_frontArticulation_CurrentSteerAngle),
            //        Mathf.Round(-_rearArticulation_CurrentSteerAngle),
            //        Mathf.Round(Mathf.Abs(_frontArticulation_CurrentSteerAngle) + Mathf.Abs(_rearArticulation_CurrentSteerAngle))));
            //} 
            #endregion
        }

        /// <summary>
        /// Apply steering helper
        /// </summary>
        private void ApplySteerHelper()
        {
            WheelHit wheelhit;
            foreach (WheelCollider wheelColliders in _torqueWheelsColliders)
            {
                wheelColliders.GetGroundHit(out wheelhit);
                if (wheelhit.normal == Vector3.zero)
                    return; // wheels arent on the ground so dont realign the rigidbody velocity
            }

            // this if is needed to avoid gimbal lock problems that will make the car suddenly shift direction
            if (Mathf.Abs(_steerHelperLastRotation - transform.eulerAngles.y) < 10f)
            {
                var turnadjust = (transform.eulerAngles.y - _steerHelperLastRotation) * _steerHelper;
                Quaternion velRotation = Quaternion.AngleAxis(turnadjust, Vector3.up);
#if UNITY_6000_0_OR_NEWER
                _rigidbody.linearVelocity = velRotation * _rigidbody.linearVelocity;
#else
                _rigidbody.velocity = velRotation * _rigidbody.velocity;
#endif
            }
            _steerHelperLastRotation = transform.eulerAngles.y;
        }

        /// <summary>
        /// Gears transmission
        /// </summary>
        private void Update_Gearbox()
        {
            if (_transmissionType == WSMVehicleTransmissionType.Automatic)
            {
#if UNITY_6000_0_OR_NEWER
                Vector3 localVelocity = transform.InverseTransformDirection(_rigidbody.linearVelocity);
#else
                Vector3 localVelocity = transform.InverseTransformDirection(_rigidbody.velocity);
#endif
                _movingBackwards = localVelocity.z < 0 && Mathf.RoundToInt(_currentSpeed) > 0;

                if (_movingBackwards)
                    _currentGear = -1;
                else //Automatic Transmission
                {
                    _currentGear = (_currentGear <= 0) ? 1 : _currentGear;

                    if (_currentGear < _numberOfGears && _currentSpeed >= _gearsSpeedLimits[_currentGear - 1])
                        _currentGear++;
                    else if (_currentGear > 1 && _currentSpeed < _gearsSpeedLimits[_currentGear - 2])
                        _currentGear--;
                }
            }
        }

        /// <summary>
        /// Apply torque to all wheels connected to the drivetrain and controls max speed
        /// </summary>
        private void Update_Engine(float deltaTime)
        {
            if (_torqueWheelsColliders == null)
                return;

            if (_isEngineOn)
            {
                _individualWheelTorque = _torqueWheelsCount > 0 ? (_currentTorque / _torqueWheelsCount) : _individualWheelTorque;
                _thrustTorque = _acceleration >= 0f ? (_acceleration * _individualWheelTorque) : (_acceleration * _individualWheelReverseTorque);
                _thrustTorque = _thrustTorque * (1f - _clutch);
            }
            else
                _thrustTorque = 0f;

            for (int i = 0; i < _torqueWheelsCount; i++)
                _torqueWheelsColliders[i].motorTorque = _thrustTorque;

            float gearSpeedLimit = _movingBackwards ? _maxReverseSpeed : _gearsSpeedLimits[_currentGear - 1];

            switch (speedUnit)
            {
                case WSMVehicleSpeedUnit.Mph:
                    SpeedControl(gearSpeedLimit, _mphConversion, deltaTime);
                    break;
                case WSMVehicleSpeedUnit.Kph:
                    SpeedControl(gearSpeedLimit, _kphConversion, deltaTime);
                    break;
            }
        }

        /// <summary>
        /// Keep velocity under gear speed limit
        /// </summary>
        private void SpeedControl(float gearSpeedLimit, float speedUnitConversion, float deltaTime)
        {
            float maxSpeedFactor = gearSpeedLimit / speedUnitConversion;
#if UNITY_6000_0_OR_NEWER
            _currentSpeed = _rigidbody.linearVelocity.magnitude * speedUnitConversion;
            if (_currentSpeed > gearSpeedLimit)
                _rigidbody.linearVelocity = Vector3.MoveTowards(_rigidbody.linearVelocity, maxSpeedFactor * _rigidbody.linearVelocity.normalized, 5f * deltaTime);
#else
            _currentSpeed = _rigidbody.velocity.magnitude * speedUnitConversion;
            if (_currentSpeed > gearSpeedLimit)
                _rigidbody.velocity = Vector3.MoveTowards(_rigidbody.velocity, maxSpeedFactor * _rigidbody.velocity.normalized, 5f * deltaTime);
#endif
        }

        /// <summary>
        /// Applies brake and hanbrake
        /// </summary>
        private void Update_Brakes()
        {
            if (_handbrake > 0f) // Handbrake has priority
            {
                float handBrakeTorque = _handbrake * _handbrakeTorque;
                foreach (var wheel in _allWheelsColliders)
                    wheel.brakeTorque = handBrakeTorque;
            }
            else
            {
                float brakeTorque = _brakes * _brakesTorque;
                foreach (var wheel in _allWheelsColliders)
                    wheel.brakeTorque = brakeTorque;
            }
        }

        /// <summary>
        /// Increase grip based on velocity
        /// </summary>
        private void ApplyDownForce()
        {
#if UNITY_6000_0_OR_NEWER
            _rigidbody.AddForce(-transform.up * _downforce * _rigidbody.linearVelocity.magnitude);
#else
            _rigidbody.AddForce(-transform.up * _downforce * _rigidbody.velocity.magnitude);
#endif
        }

        /// <summary>
        /// Traction control to avoid wheels spinning too much
        /// </summary>
        private void ApplyTractionControl()
        {
            WheelHit wheelHit;

            foreach (WheelCollider wheelCollider in _torqueWheelsColliders)
            {
                wheelCollider.GetGroundHit(out wheelHit);

                if (wheelHit.forwardSlip >= _tractionSlipLimit && _currentTorque >= 0)
                {
                    _currentTorque -= 10 * _tractionControl;
                }
                else
                {
                    _currentTorque += 10 * _tractionControl;
                    if (_currentTorque > _maxTorque)
                        _currentTorque = _maxTorque;
                }
            }
        }

        /// <summary>
        /// Transfer wheel collider rotation to respective wheel mesh
        /// </summary>
        /// <param name="wheelColliders"></param>
        /// <param name="wheelMeshes"></param>
        private void UpdateWheelMeshesRotation(WheelCollider[] wheelColliders, GameObject[] wheelMeshes)
        {
            for (int i = 0; i < wheelMeshes.Length; i++)
            {
                if (i < wheelColliders.Length)
                {
                    wheelColliders[i].GetWorldPose(out _wheelPosition, out _wheelRotation);
                    wheelMeshes[i].transform.position = _wheelPosition;
                    wheelMeshes[i].transform.rotation = _wheelRotation;
                }
                else break;
            }
        }

        /// <summary>
        /// Animate levers accordingly to player's input
        /// </summary>
        private void Update_Pedals(float deltaTime)
        {
            _currentGasPedalAngle = Mathf.MoveTowards(_currentGasPedalAngle, Mathf.Abs(_acceleration) * 25f, 70f * deltaTime);
            _currentBrakesPedalAngle = Mathf.MoveTowards(_currentBrakesPedalAngle, _brakes * 25f, 200f * deltaTime);
            _currentClutchPedalAngle = Mathf.MoveTowards(_currentClutchPedalAngle, _clutch * 25f, 200f * deltaTime);

            if (gasPedal != null) gasPedal.localEulerAngles = new Vector3(_currentGasPedalAngle, 0f, 0f);
            if (brakesPedal != null) brakesPedal.localEulerAngles = new Vector3(_currentBrakesPedalAngle, 0f, 0f);
            if (clutchPedal != null) clutchPedal.localEulerAngles = new Vector3(_currentClutchPedalAngle, 0f, 0f);
        }

        /// <summary>
        /// Update vehicle related UI
        /// </summary>
        private void Update_UI()
        {
            if (speedText != null)
                speedText.text = string.Format("{0} {1}", Mathf.RoundToInt(_currentSpeed), speedUnit.ToString());
        }

        /// <summary>
        /// Handles vehicle SFX changes
        /// </summary>
        private void Update_SFX()
        {
            if (_isEngineOn)
            {
                // Engine
                if (engineSFX != null && engineSFX.isPlaying)
                {
                    _currentGear = (_currentGear <= 0) ? 1 : _currentGear;

                    float speedGearRatio = _currentSpeed / _gearsSpeedLimits[_currentGear - 1];
                    float newEnginePitch = Mathf.Lerp(_minEnginePitch, _maxEnginePitch, speedGearRatio);
                    engineSFX.pitch = newEnginePitch;
                }

                // Truck and machinery reverse warning
                if (backUpBeeperSFX != null)
                {
                    if (_movingBackwards && !backUpBeeperSFX.isPlaying)
                        backUpBeeperSFX.Play();
                    else if (!_movingBackwards && backUpBeeperSFX.isPlaying)
                        backUpBeeperSFX.Stop();
                }
            }

            // Signal lights works even if engine is off
            if (signalLightsSFX != null)
            {
                if ((_leftSinalLightsOn || _rightSinalLightsOn) && !signalLightsSFX.isPlaying)
                    signalLightsSFX.Play();
                else if (!_leftSinalLightsOn && !_rightSinalLightsOn && signalLightsSFX.isPlaying)
                    signalLightsSFX.Stop();
            }

            // Wheels skidding
            if (wheelsSkiddingSFX != null)
            {
                bool isAnyWheelSlipping = false;
                WheelHit wheelHit;
                foreach (WheelCollider wheelCollider in _allWheelsColliders)
                {
                    if (wheelCollider.GetGroundHit(out wheelHit))
                    {
                        if (Mathf.Abs(wheelHit.forwardSlip) >= _sfxForwardSlipLimit || Mathf.Abs(wheelHit.sidewaysSlip) >= _sfxSidewaysSlipLimit)
                        {
                            isAnyWheelSlipping = true;
                            break;
                        }
                    }
                }

                if (isAnyWheelSlipping)
                {
                    if (!wheelsSkiddingSFX.isPlaying)
                        wheelsSkiddingSFX.Play();
                }
                else
                {
                    if (wheelsSkiddingSFX.isPlaying)
                        wheelsSkiddingSFX.Stop();
                }
            }
        }

        /// <summary>
        /// Handles vehicle lights
        /// </summary>
        private void Update_Lights()
        {
            if (_isEngineOn)
            {
                if (lightSystem == LightSystem.GlowMesh && reverseAlarmLamps != null)
                {
                    if (_blinkAlarmLights)
                        reverseAlarmLamps.Blinking = _movingBackwards; //Blinking property does not trigger events if the value hasn't changed
                    else
                        reverseAlarmLamps.LightsOn = _movingBackwards; //Lights On property does not trigger events if the value hasn't changed
                }
                else if (lightSystem == LightSystem.Legacy && reverseAlarmLights != null)
                {
                    foreach (var alarmLights in reverseAlarmLights)
                        alarmLights.enabled = _movingBackwards;

                    if (_movingBackwards && _rotateAlarmLights && alarmLightsPivot != null)
                        alarmLightsPivot.Rotate(alarmLightsPivot.up, _alarmLightsRotationSpeed, Space.Self);
                }
            }

            // Brake lights works even if the engine is off
            if (lightSystem == LightSystem.GlowMesh && brakeLightsLamps != null)
            {
                brakeLightsLamps.LightsOn = IsBraking; //Lights On property does not trigger events if the value hasn't changed
            }
            else if (lightSystem == LightSystem.Legacy && brakeLights != null)
            {
                foreach (var brakeLight in brakeLights)
                    brakeLight.enabled = IsBraking;
            }
        }

        /// <summary>
        /// Invoked on Legacy Lighting Mode
        /// </summary>
        private void LeftSignalLights()
        {
            ToggleLights(signalLightsLeft);
        }

        /// <summary>
        /// Invoked on Legacy Lighting Mode
        /// </summary>
        private void RightSignalLights()
        {
            ToggleLights(signalLightsRight);
        }

        /// <summary>
        /// Toggle lights on/off depending on their current status
        /// </summary>
        /// <param name="lights"></param>
        private void ToggleLights(Light[] lights)
        {
            if (lights != null)
            {
                foreach (var light in lights)
                    light.enabled = !light.enabled;
            }
        }

        /// <summary>
        /// Toggle lights to the desired state
        /// </summary>
        /// <param name="lights"></param>
        /// <param name="on"></param>
        private void ToggleLights(Light[] lights, bool on)
        {
            if (lights != null)
            {
                foreach (var light in lights)
                    light.enabled = on;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="door"></param>
        private void ToogleDoor(WSMVehicleDoor door)
        {
            if (door != null)
                door.IsOpen = !door.IsOpen;
        }

        #endregion

        #region PUBLIC VEHICLE METHODS

        /// <summary>
        /// Starts vehicle engine
        /// </summary>
        public void StartEngine()
        {
            _isEngineOn = true;

            if (Application.isPlaying)
            {
                if (engineStartSFX != null && !engineStartSFX.isPlaying)
                    engineStartSFX.Play();
                if (engineSFX != null && !engineSFX.isPlaying)
                    engineSFX.Play();
            }
        }

        /// <summary>
        /// Stop vehicle engine
        /// </summary>
        public void StopEngine()
        {
            _isEngineOn = false;

            if (Application.isPlaying)
            {
                if (engineSFX != null && engineSFX.isPlaying)
                    engineSFX.Stop();

                if (backUpBeeperSFX != null && backUpBeeperSFX.isPlaying)
                    backUpBeeperSFX.Stop();

                if (signalLightsSFX != null && signalLightsSFX.isPlaying)
                    signalLightsSFX.Stop();
            }
        }

        /// <summary>
        /// Vehicle horns
        /// </summary>
        public void Horn()
        {
            if (hornSFX != null)
                hornSFX.Play();
        }

        public void ToogleDriverDoor()
        {
            ToogleDoor(driverDoor);
        }

        public void TooglePassengerDoor(int index)
        {
            if (passengersDoors != null && passengersDoors.Length > index)
                ToogleDoor(passengersDoors[index]);
        }

        #endregion
    }
}
