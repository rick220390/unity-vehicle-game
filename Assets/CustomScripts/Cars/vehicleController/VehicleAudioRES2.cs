using UnityEngine;

namespace SkrilStudio
{
    /// <summary>
    /// Bridges the project's custom Vehicle physics controller to Realistic Engine Sounds 2.
    /// Attach this script to the RES2 prefab root (or a child containing RealisticEngineSound),
    /// then place that prefab somewhere under the vehicle GameObject.
    /// </summary>
    [DisallowMultipleComponent]
    public class VehicleAudioRES2 : MonoBehaviour
    {
        [Header("Optional explicit references")]
        [Tooltip("Leave empty to find the nearest parent Vehicle automatically.")]
        public global::Vehicle vehicle;

        [Tooltip("Leave empty to find RealisticEngineSound in this object or its children.")]
        public RealisticEngineSound res2;

        [Header("Input tuning")]
        [Tooltip("Throttle amount required to tell RES2 that the gas pedal is pressed.")]
        [Range(0.001f, 0.5f)]
        public float throttleDeadZone = 0.05f;

        [Tooltip("Speed in km/h below which a reverse throttle request selects reverse audio before the car has begun moving backward.")]
        [Min(0f)]
        public float reverseEngageSpeedKph = 3f;

        private Rigidbody vehicleRigidbody;
        private bool warnedMissingVehicle;
        private bool warnedMissingRes2;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Start()
        {
            ResolveReferences();

            if (vehicle == null && !warnedMissingVehicle)
            {
                Debug.LogWarning(
                    $"{name}: VehicleAudioRES2 could not find a Vehicle controller in its parent hierarchy. Assign the Vehicle field or make this object a child of the car.",
                    this);
                warnedMissingVehicle = true;
            }

            if (res2 == null && !warnedMissingRes2)
            {
                Debug.LogWarning(
                    $"{name}: VehicleAudioRES2 could not find RealisticEngineSound in this object or its children. Assign the RES2 field or attach this script to the RES2 prefab root.",
                    this);
                warnedMissingRes2 = true;
            }

            if (vehicle != null && res2 != null)
                ApplyStaticVehicleValues();
        }

        private void Update()
        {
            if (vehicle == null || res2 == null)
            {
                ResolveReferences();
                if (vehicle == null || res2 == null)
                    return;
            }

            float redline = Mathf.Max(vehicle.idleRpm + 1f, vehicle.redlineRpm);

            // The Vehicle controller stores speed in metres per second; RES2 expects
            // a speed value in km/h for this setup.
            res2.engineCurrentRPM = Mathf.Clamp(vehicle.EngineRpm, 0f, redline);
            res2.maxRPMLimit = redline;
            res2.carCurrentSpeed = vehicle.Speed * 3.6f;
            res2.carMaxSpeed = Mathf.Max(1f, vehicle.maxSpeedKph);

            // Use the actual physics controller's shift window, rather than starting
            // a separate timer that could drift out of sync with the gearbox.
            bool shifting = vehicle.HasDriver && vehicle.IsShifting;
            res2.isShifting = shifting;

            bool hasThrottleInput = Mathf.Abs(vehicle.Throttle) > Mathf.Max(0.001f, throttleDeadZone);
            res2.gasPedalPressing =
                vehicle.HasDriver &&
                hasThrottleInput &&
                !shifting &&
                !vehicle.IsBraking;

            // The custom controller represents reverse with negative throttle rather
            // than a -1 transmission gear. Prefer actual backward motion; at very low
            // speed, a negative throttle request selects reverse audio immediately.
            float speedKph = vehicle.Speed * 3.6f;
            bool movingBackwards = false;

            if (vehicleRigidbody != null)
            {
#if UNITY_6000_0_OR_NEWER
                Vector3 velocity = vehicleRigidbody.linearVelocity;
#else
                Vector3 velocity = vehicleRigidbody.velocity;
#endif
                movingBackwards = Vector3.Dot(velocity, vehicle.transform.forward) < -0.5f;
            }

            bool reverseRequestedAtLowSpeed =
                vehicle.Throttle < -Mathf.Max(0.001f, throttleDeadZone) &&
                speedKph <= Mathf.Max(0f, reverseEngageSpeedKph);

            res2.isReversing =
                vehicle.HasDriver &&
                (movingBackwards || reverseRequestedAtLowSpeed);
        }

        private void ResolveReferences()
        {
            if (vehicle == null)
            {
                vehicle = GetComponentInParent<global::Vehicle>();
                if (vehicle == null)
                    vehicle = GetComponentInChildren<global::Vehicle>(true);
            }

            if (res2 == null)
                res2 = GetComponentInChildren<RealisticEngineSound>(true);

            if (vehicle != null)
                vehicleRigidbody = vehicle.GetComponent<Rigidbody>();
        }

        private void ApplyStaticVehicleValues()
        {
            res2.maxRPMLimit = Mathf.Max(vehicle.idleRpm + 1f, vehicle.redlineRpm);
            res2.carMaxSpeed = Mathf.Max(1f, vehicle.maxSpeedKph);
        }

        private void OnDisable()
        {
            // Avoid leaving RES2 with a stale throttle/reverse/shift state if this
            // bridge is disabled while the player is driving.
            if (res2 == null)
                return;

            res2.gasPedalPressing = false;
            res2.isShifting = false;
            res2.isReversing = false;
        }
    }
}