using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Vehicle : MonoBehaviour
{
    public static readonly List<Vehicle> All = new List<Vehicle>();

    [Header("Vehicle points")]
    public Transform seatPoint;
    [Tooltip("Optional target for the driving camera to orbit around and look at. If empty, the camera uses its default height above the vehicle root.")]
    public Transform cameraPoint;
    public Transform[] exitPoints;

    [Header("WheelCollider setup")]
    [Tooltip("Assign the four WheelColliders in matching order to Wheel Visuals. If empty, the temporary fallback controller is used.")]
    public WheelCollider[] wheelColliders;
    [Tooltip("Visible wheel transforms in exactly the same order as Wheel Colliders.")]
    public Transform[] wheelVisuals;
    [Tooltip("Indexes in Wheel Colliders that steer. Usually the two front wheels (for example 0 and 1).")]
    public int[] steeringWheelIndexes = { 0, 1 };
    [Tooltip("Indexes in Wheel Colliders that receive engine torque. Rear-wheel drive default: 2 and 3.")]
    public int[] drivenWheelIndexes = { 2, 3 };
    [Tooltip("When enabled, wheel visuals follow their WheelCollider's suspension and rotation.")]
    public bool updateWheelVisuals = true;

    [Header("Engine and gearbox feel")]
    [Tooltip("Automatic transmission forward gear ratios, from first to top gear.")]
    public float[] forwardGearRatios = { 3.20f, 2.10f, 1.45f, 1.00f, 0.78f };
    [Min(1f)] public float finalDriveRatio = 3.42f;
    [Min(500f)] public float idleRpm = 850f;
    [Min(1000f)] public float redlineRpm = 6500f;
    [Min(100f)] public float upshiftRpm = 5700f;
    [Min(100f)] public float downshiftRpm = 1800f;
    [Min(0.1f)] public float gearShiftTime = 0.28f;
    [Min(0.1f)] public float engineRpmResponse = 4f;
    [Min(0.1f)] public float clutchEngagementSpeed = 8f;
    [Min(100f)] public float motorTorque = 2600f;
    [Tooltip("Caps delivered wheel torque at very low road speeds. The cap blends out as the clutch fully engages. Set to 0 to disable the cap.")]
    [Min(0f)] public float maxMotorTorqueAtLowSpeed = 2200f;
    [Min(1f)] public float maxSpeedKph = 150f;
    [Min(1f)] public float maxReverseSpeedKph = 30f;
    [Min(100f)] public float serviceBrakeTorque = 1800f;
    [Min(100f)] public float handbrakeTorque = 4500f;
    [Tooltip("Engine-braking torque at the engine crankshaft while coasting, multiplied through the current gear and final-drive ratios. Set to 0 to disable engine braking. Existing prefabs may retain their serialized Inspector value.")]
    [Min(0f)] public float engineBrakeTorque = 120f;
    [Tooltip("Grounded rolling resistance acceleration while coasting, in m/s². Keep low so gravity can accelerate the car down slopes.")]
    [Min(0f)] public float coastResistanceAcceleration = 0.04f;
    [Tooltip("Aerodynamic drag coefficient in 1/s. Applied whenever the vehicle is moving, including under throttle and braking. 0.008 gives about 0.22 m/s² drag at 100 km/h.")]
    [Min(0f)] public float coastAirDrag = 0.008f;
    [Tooltip("Independent immediate chassis deceleration while the service brake is pressed, in m/s². Supplements WheelCollider brakes so stopping does not depend on tyre brake torque alone.")]
    [Min(0f)] public float serviceBrakeDeceleration = 6f;
    [Min(0.1f)] public float throttleResponse = 3f;
    [Min(0.1f)] public float brakeResponse = 8f;

    [Header("Steering")]
    [Range(5f, 55f)] public float maxSteerAngle = 45f;
    [Range(5f, 30f)] public float highSpeedSteerAngle = 16f;
    [Min(1f)] public float steeringFullSpeedKph = 140f;
    [Min(0.1f)] public float steeringResponse = 10f;

    [Header("Tyres and suspension")]
    [Min(0.1f)] public float wheelRadius = 0.34f;
    [Min(0.05f)] public float suspensionDistance = 0.22f;
    [Min(1000f)] public float suspensionSpring = 35000f;
    [Min(100f)] public float suspensionDamper = 4500f;
    [Range(0.1f, 5f)] public float forwardGrip = 1.15f;
    [Tooltip("Lateral grip for steering/front wheels. Higher values resist understeer less only when the front tyres have enough grip.")]
    [Range(0.1f, 5f)] public float frontSidewaysGrip = 1.5f;
    [Tooltip("Lateral grip for non-steering/rear wheels. Lower values make a rear-wheel-drive car easier to slide.")]
    [Range(0.1f, 5f)] public float rearSidewaysGrip = 0.55f;
    [Range(0.1f, 5f)] public float handbrakeRearGrip = 0.55f;
    [Tooltip("Reduces brake torque when a grounded wheel is close to locking, approximating ABS.")]
    public bool useABS = true;
    [Range(0.05f, 1f)] public float absSlipThreshold = 0.35f;
    [Range(0.1f, 1f)] public float absMinimumBrakeFactor = 0.35f;

    [Header("Chassis physics")]
    public Vector3 centerOfMassOffset = new Vector3(0f, -0.45f, 0f);
    [Min(0f)] public float linearDamping = 0.02f;
    [Min(0f)] public float angularDamping = 0.8f;
    [Min(1f)] public float maxAngularVelocity = 7f;

    [Header("Temporary fallback (used only before WheelColliders are assigned)")]
    public float fallbackAcceleration = 10f;
    public float fallbackMaxSpeed = 32f;
    public float fallbackMaxReverseSpeed = 9f;
    public float fallbackBrakeDeceleration = 18f;
    public float fallbackSideGrip = 5f;

    [Header("Debug")]
    public bool debugGrounded;
    public float debugThrottle;
    public float debugSpeedKph;
    public bool debugHasDriver;
    public bool debugUsingWheelColliders;

    [Header("Drivetrain Diagnostics")]
    [Tooltip("Runtime gear number, including the current selected forward gear.")]
    public int debugCurrentGear = 1;
    public int debugForwardGearCount;
    public float debugUpshiftThresholdRpm;
    [Tooltip("RPM predicted from road speed and the selected gear; this is the RPM used for automatic shift decisions.")]
    public float debugShiftRpm;
    public float debugDrivenWheelRpm;
    public int debugDrivenWheelCount;
    public float debugShiftCooldownSeconds;
    public float debugShiftTimerSeconds;
    [Tooltip("Shows why the automatic gearbox is or is not currently shifting.")]
    public string debugShiftStatus;

    public bool HasDriver { get; private set; }
    /// <summary>Whether an external system (for example the fuel tank) allows the engine to run.</summary>
    public bool ExternalEngineEnabled { get; private set; } = true;
    /// <summary>External multiplier for delivered engine power, useful for fuel and future damage systems.</summary>
    public float ExternalEnginePowerMultiplier { get; private set; } = 1f;
    public float Throttle { get; private set; }
    public float Steer { get; private set; }
    public float ServiceBrake { get; private set; }
    public bool Handbrake { get; private set; }
    /// <summary>True while the service brake or handbrake is being applied by the driver.</summary>
    public bool IsBraking { get; private set; }
    public float EngineRpm { get; private set; } = 850f;
    public int CurrentGear { get; private set; } = 1;
    public bool IsShifting => shiftTimer > 0f;
    public float Speed => GetVelocity().magnitude;
    public float ForwardSpeed => Vector3.Dot(GetVelocity(), transform.forward);
    public Collider BodyCollider { get; private set; }

    private Collider[] detectionColliders;
    private Rigidbody rb;

    /// <summary>
    /// Returns the shortest distance from a world-space point to this vehicle's
    /// enabled physical colliders. This is more reliable than measuring only from
    /// the root pivot, which may be far from the visible body on imported models.
    /// </summary>
    public float DistanceToPoint(Vector3 point)
    {
        float bestDistance = float.PositiveInfinity;

        if (detectionColliders != null)
        {
            foreach (Collider candidate in detectionColliders)
            {
                if (candidate == null ||
                    !candidate.enabled ||
                    !candidate.gameObject.activeInHierarchy ||
                    candidate.isTrigger ||
                    candidate is WheelCollider)
                    continue;

                Vector3 closest = candidate.ClosestPoint(point);
                float distance = Vector3.Distance(point, closest);
                if (distance < bestDistance)
                    bestDistance = distance;
            }
        }

        if (!float.IsInfinity(bestDistance))
            return bestDistance;

        // Keep compatibility with vehicles that only have a root collider,
        // including prototypes that use a trigger as their only collider.
        if (BodyCollider != null &&
            BodyCollider.enabled &&
            BodyCollider.gameObject.activeInHierarchy)
        {
            return Vector3.Distance(point, BodyCollider.ClosestPoint(point));
        }

        // Final fallback for collider-free prototypes.
        return Vector3.Distance(point, transform.position);
    }
    private float appliedThrottle;
    private float appliedBrake;
    private float appliedSteer;
    private bool warnedNoWheels;
    private float shiftTimer;
    private float shiftCooldown;
    private readonly List<WheelCollider> validWheels = new List<WheelCollider>();
    private Vector3[] wheelVisualPositionOffsets;
    private Quaternion[] wheelVisualRotationOffsets;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        BodyCollider = GetComponent<Collider>();

        // Cache colliders from the complete vehicle hierarchy once. Imported cars
        // often keep their physical colliders on child objects instead of the root.
        detectionColliders = GetComponentsInChildren<Collider>(true);

        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.maxAngularVelocity = maxAngularVelocity;
        rb.drag = linearDamping;
        rb.angularDrag = angularDamping;
        rb.ResetCenterOfMass();
        rb.centerOfMass += centerOfMassOffset;

        CacheWheels();
        CacheWheelVisualOffsets();
        ConfigureWheelColliders();
    }

    private void OnEnable()
    {
        if (!All.Contains(this))
            All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
        StopWheelTorque();
    }

    private void CacheWheels()
    {
        validWheels.Clear();
        if (wheelColliders == null)
            return;

        foreach (WheelCollider wheel in wheelColliders)
        {
            if (wheel != null && !validWheels.Contains(wheel))
                validWheels.Add(wheel);
        }
    }

    private void CacheWheelVisualOffsets()
    {
        if (wheelColliders == null || wheelVisuals == null)
            return;

        int count = Mathf.Min(wheelColliders.Length, wheelVisuals.Length);
        wheelVisualPositionOffsets = new Vector3[count];
        wheelVisualRotationOffsets = new Quaternion[count];

        for (int i = 0; i < count; i++)
        {
            WheelCollider wheel = wheelColliders[i];
            Transform visual = wheelVisuals[i];

            if (wheel == null || visual == null)
            {
                wheelVisualRotationOffsets[i] = Quaternion.identity;
                continue;
            }

            // Preserve the model's original alignment relative to the collider's
            // initial pose. Imported vehicle models often have different wheel axes.
            wheel.GetWorldPose(out Vector3 wheelPosition, out Quaternion wheelRotation);
            wheelVisualPositionOffsets[i] =
                Quaternion.Inverse(wheelRotation) * (visual.position - wheelPosition);
            wheelVisualRotationOffsets[i] =
                Quaternion.Inverse(wheelRotation) * visual.rotation;
        }
    }

    private void ConfigureWheelColliders()
    {
        foreach (WheelCollider wheel in validWheels)
        {
            if (wheel == null)
                continue;

            // Substeps are setup, not a per-frame tuning value.
            wheel.ConfigureVehicleSubsteps(5f, 12, 15);
        }

        ApplyWheelColliderTuning();
    }

    private void ApplyWheelColliderTuning()
    {
        // Apply these values continuously so Inspector changes and future upgrades
        // take effect during Play mode without restarting the vehicle.
        for (int i = 0; i < validWheels.Count; i++)
        {
            WheelCollider wheel = validWheels[i];
            if (wheel == null)
                continue;

            wheel.radius = Mathf.Max(0.05f, wheelRadius);
            wheel.suspensionDistance = Mathf.Max(0.01f, suspensionDistance);

            JointSpring spring = wheel.suspensionSpring;
            spring.spring = Mathf.Max(1f, suspensionSpring);
            spring.damper = Mathf.Max(0f, suspensionDamper);
            wheel.suspensionSpring = spring;

            WheelFrictionCurve forward = wheel.forwardFriction;
            forward.stiffness = Mathf.Max(0.01f, forwardGrip);
            wheel.forwardFriction = forward;

            WheelFrictionCurve sideways = wheel.sidewaysFriction;
            bool frontWheel = ContainsIndex(steeringWheelIndexes, i);
            sideways.stiffness = Mathf.Max(0.01f,
                frontWheel ? frontSidewaysGrip : rearSidewaysGrip);
            wheel.sidewaysFriction = sideways;
        }
    }

    public bool TryReserve()
    {
        if (HasDriver)
            return false;

        HasDriver = true;
        return true;
    }

    public void Release()
    {
        HasDriver = false;
        IsBraking = false;
        SetInput(0f, 0f, false, 0f);
        StopWheelTorque();
    }

    public void SetInput(float throttle, float steer, bool handbrake = false, float serviceBrake = 0f)
    {
        Throttle = Mathf.Clamp(throttle, -1f, 1f);
        Steer = Mathf.Clamp(steer, -1f, 1f);
        Handbrake = handbrake;
        ServiceBrake = Mathf.Clamp01(serviceBrake);
    }

    /// <summary>
    /// Allows optional vehicle systems to control whether the engine can run and
    /// how much power it can deliver without taking ownership of vehicle physics.
    /// </summary>
    public void SetExternalEngineState(bool engineEnabled, float powerMultiplier = 1f)
    {
        ExternalEngineEnabled = engineEnabled;
        ExternalEnginePowerMultiplier = engineEnabled
            ? Mathf.Clamp01(powerMultiplier)
            : 0f;
    }

    private void FixedUpdate()
    {
        debugThrottle = Throttle;
        debugSpeedKph = Speed * 3.6f;
        debugHasDriver = HasDriver;
        debugUsingWheelColliders = validWheels.Count > 0;

        // Keep WheelCollider physics synchronized with live tuning values.
        // This also gives future upgrades one authoritative path to apply changes.
        if (validWheels.Count > 0)
            ApplyWheelColliderTuning();

        UpdateDrivetrain(Time.fixedDeltaTime);

        if (validWheels.Count > 0)
            DriveWithWheelColliders();
        else
            DriveFallback();
    }

    private void DriveWithWheelColliders()
    {
        float dt = Time.fixedDeltaTime;
        float speedKph = Speed * 3.6f;
        float targetThrottle = HasDriver ? Throttle : 0f;
        float targetBrake = HasDriver ? ServiceBrake * serviceBrakeTorque : 0f;

        // A negative driving axis (for example S/down-arrow) brakes while the car
        // is travelling forward. Once almost stopped, the same held input may
        // engage reverse. A separate ServiceBrake signal also works independently
        // of throttle. Service brakes take priority over propulsion to prevent the
        // engine fighting the brakes, especially with AWD.
        float forwardSpeed = Vector3.Dot(GetVelocity(), transform.forward);
        bool serviceBrakeRequested = HasDriver && ServiceBrake > 0.01f;
        if (serviceBrakeRequested)
            targetThrottle = 0f;
        bool oppositeInput = Mathf.Abs(forwardSpeed) > 1.0f &&
                             Mathf.Abs(targetThrottle) > 0.05f &&
                             Mathf.Sign(targetThrottle) != Mathf.Sign(forwardSpeed);

        if (oppositeInput)
        {
            targetBrake = Mathf.Max(targetBrake, Mathf.Abs(targetThrottle) * serviceBrakeTorque);
            targetThrottle = 0f;
        }

        // If the input caller uses the traditional shared reverse/brake axis,
        // hold the brakes while moving forward, then allow reverse only at a crawl.
        if (HasDriver && Throttle < -0.05f && forwardSpeed > 0.5f)
        {
            targetBrake = Mathf.Max(targetBrake, Mathf.Abs(Throttle) * serviceBrakeTorque);
            targetThrottle = 0f;
        }

        if (HasDriver && Handbrake)
            targetBrake = Mathf.Max(targetBrake, handbrakeTorque);

        // Release propulsion immediately when the accelerator returns to neutral.
        // A brake request also cuts propulsion and applies braking in this same
        // physics tick; brake response smoothing must not introduce a dangerous delay.
        if (serviceBrakeRequested || Mathf.Abs(targetThrottle) < 0.01f)
            appliedThrottle = 0f;
        else
            appliedThrottle = Mathf.MoveTowards(
                appliedThrottle, targetThrottle, throttleResponse * dt);

        if (targetBrake > 1f || serviceBrakeRequested)
            appliedBrake = targetBrake;
        else
            appliedBrake = Mathf.MoveTowards(
                appliedBrake, targetBrake, brakeResponse * serviceBrakeTorque * dt);
        IsBraking = HasDriver && appliedBrake > 1f;
        appliedSteer = Mathf.Lerp(
            appliedSteer, HasDriver ? Steer : 0f,
            1f - Mathf.Exp(-steeringResponse * dt));

        // Aerodynamic drag acts whenever the car has planar velocity, not just
        // after the throttle is released. This avoids an artificial "drag kicks in"
        // feeling and lets air resistance scale naturally with road speed.
        Vector3 planarVelocity = Vector3.ProjectOnPlane(GetVelocity(), Vector3.up);
        if (planarVelocity.sqrMagnitude > 0.0001f)
        {
            Vector3 resistanceAcceleration = -planarVelocity * coastAirDrag;

            // Independent chassis-level service braking: do not rely solely on
            // WheelCollider brakeTorque to create stopping force. Apply it in the
            // same FixedUpdate as the brake input, with no response ramp or delay.
            // This supplements normal wheel braking and protects against weak or
            // misconfigured wheel brake response. Handbrake remains wheel-based.
            if (targetBrake > 1f && serviceBrakeDeceleration > 0f)
            {
                float brakeStrength = Mathf.Clamp01(targetBrake /
                    Mathf.Max(1f, serviceBrakeTorque));
                resistanceAcceleration -= planarVelocity.normalized *
                    (serviceBrakeDeceleration * brakeStrength);
            }

            bool coasting = Mathf.Abs(targetThrottle) < 0.05f &&
                targetBrake <= 1f &&
                !(HasDriver && Handbrake);

            // Rolling resistance and engine braking require tyre contact and only
            // act while coasting. Service brakes remain independent and take priority.
            if (coasting && AnyWheelGrounded())
            {
                resistanceAcceleration -= planarVelocity.normalized * coastResistanceAcceleration;

                if (engineBrakeTorque > 0f &&
                    Mathf.Abs(forwardSpeed) > 0.5f &&
                    rb.mass > 0.01f)
                {
                    float ratio = 1f;
                    if (forwardGearRatios != null && forwardGearRatios.Length > 0)
                    {
                        int gearIndex = Mathf.Clamp(CurrentGear - 1, 0, forwardGearRatios.Length - 1);
                        ratio = Mathf.Max(0.1f, forwardGearRatios[gearIndex]);
                    }

                    // Convert engine crankshaft braking torque to approximate
                    // longitudinal deceleration at the tyres. No arbitrary 0.22
                    // reduction: torque is transmitted through the selected gear
                    // and final drive, then divided by wheel radius and vehicle mass.
                    float engineBrakeAcceleration = engineBrakeTorque * ratio *
                        Mathf.Max(0.1f, finalDriveRatio) /
                        (Mathf.Max(0.05f, wheelRadius) * rb.mass);

                    resistanceAcceleration -= transform.forward *
                        Mathf.Sign(forwardSpeed) * engineBrakeAcceleration;
                }
            }

            rb.AddForce(resistanceAcceleration, ForceMode.Acceleration);
        }

        float speedLimit = appliedThrottle < 0f ? maxReverseSpeedKph : maxSpeedKph;
        bool speedLimited = speedKph >= speedLimit && Mathf.Sign(appliedThrottle) == Mathf.Sign(forwardSpeed);
        // Hard interlock: any brake torque or explicit service-brake request
        // prevents motor torque, regardless of driven-wheel configuration.
        float torque = (targetBrake > 1f || serviceBrakeRequested || speedLimited)
            ? 0f
            : CalculateDriveTorque(appliedThrottle);

        // Keep useful steering at normal road speeds, then progressively reduce
        // the angle at high speed to avoid an abrupt, unstable steering response.
        float speedFactor = Mathf.InverseLerp(0f, steeringFullSpeedKph, speedKph);
        speedFactor = speedFactor * speedFactor * (3f - 2f * speedFactor);
        float steerAngle = Mathf.Lerp(maxSteerAngle, highSpeedSteerAngle, speedFactor) * appliedSteer;

        for (int i = 0; i < validWheels.Count; i++)
        {
            WheelCollider wheel = validWheels[i];
            if (wheel == null)
                continue;

            bool isSteeringWheel = ContainsIndex(steeringWheelIndexes, i);
            bool isDrivenWheel = ContainsIndex(drivenWheelIndexes, i);
            bool isRearWheel = !isSteeringWheel;

            wheel.steerAngle = isSteeringWheel ? steerAngle : 0f;
            wheel.motorTorque = isDrivenWheel ? torque : 0f;

            float brake = appliedBrake;
            if (HasDriver && Handbrake && isRearWheel)
                brake = Mathf.Max(brake, handbrakeTorque);

            // Basic ABS approximation: reduce brake torque if a grounded tyre is
            // producing excessive longitudinal slip, rather than locking solid.
            if (useABS && brake > 0f && wheel.GetGroundHit(out WheelHit hit) &&
                Mathf.Abs(hit.forwardSlip) > absSlipThreshold)
            {
                float slipAmount = Mathf.InverseLerp(absSlipThreshold, absSlipThreshold * 2.5f,
                    Mathf.Abs(hit.forwardSlip));
                float brakeFactor = Mathf.Lerp(1f, absMinimumBrakeFactor, slipAmount);
                brake *= brakeFactor;
            }
            wheel.brakeTorque = brake;

            // Give the front axle more lateral grip than the rear. The rear grip
            // is reduced further under handbrake to allow controllable oversteer.
            WheelFrictionCurve sideways = wheel.sidewaysFriction;
            float baseGrip = isSteeringWheel ? frontSidewaysGrip : rearSidewaysGrip;
            float targetGrip = HasDriver && Handbrake && isRearWheel
                ? baseGrip * handbrakeRearGrip
                : baseGrip;
            sideways.stiffness = Mathf.MoveTowards(sideways.stiffness, targetGrip, 3f * dt);
            wheel.sidewaysFriction = sideways;
        }

        UpdateWheelVisuals();
        debugGrounded = AnyWheelGrounded();
    }

    private void UpdateDrivetrain(float dt)
    {
        shiftCooldown = Mathf.Max(0f, shiftCooldown - dt);
        shiftTimer = Mathf.Max(0f, shiftTimer - dt);
        debugCurrentGear = CurrentGear;
        debugForwardGearCount = forwardGearRatios != null ? forwardGearRatios.Length : 0;
        debugShiftCooldownSeconds = shiftCooldown;
        debugShiftTimerSeconds = shiftTimer;
        debugDrivenWheelRpm = 0f;
        debugDrivenWheelCount = 0;
        debugShiftStatus = "Evaluating drivetrain";

        if (!ExternalEngineEnabled)
        {
            EngineRpm = Mathf.MoveTowards(
                EngineRpm, 0f, Mathf.Max(1f, engineRpmResponse * redlineRpm) * dt);
            shiftTimer = 0f;
            debugShiftStatus = "Engine disabled (fuel or external engine system)";
            return;
        }

        float throttle = HasDriver && ServiceBrake < 0.01f ? Mathf.Abs(Throttle) : 0f;
        float speedKph = Speed * 3.6f;
        int gearCount = forwardGearRatios != null ? forwardGearRatios.Length : 0;
        if (gearCount == 0)
        {
            EngineRpm = Mathf.MoveTowards(EngineRpm,
                idleRpm + throttle * (redlineRpm - idleRpm),
                engineRpmResponse * redlineRpm * dt);
            CurrentGear = 1;
            debugCurrentGear = CurrentGear;
            debugShiftStatus = "No forward gear ratios configured";
            return;
        }

        // Calculate wheel RPM from the vehicle's actual road speed, wheel radius,
        // and selected ratio. WheelCollider.rpm can be wildly out of step with road
        // speed on imported cars with mismatched collider sizes or wheel assignments;
        // using it as the shift trigger can make an automatic gearbox race through gears.
        float circumference = 2f * Mathf.PI * Mathf.Max(0.05f, wheelRadius);
        float roadSpeedWheelRpm = (speedKph / 3.6f) / circumference * 60f;

        float drivenWheelRpm = 0f;
        int drivenWheelCount = 0;
        if (wheelColliders != null)
        {
            for (int i = 0; i < wheelColliders.Length; i++)
            {
                WheelCollider wheel = wheelColliders[i];
                if (wheel != null && ContainsIndex(drivenWheelIndexes, i))
                {
                    drivenWheelRpm += Mathf.Abs(wheel.rpm);
                    drivenWheelCount++;
                }
            }
        }
        debugDrivenWheelRpm = drivenWheelCount > 0 ? drivenWheelRpm / drivenWheelCount : 0f;
        debugDrivenWheelCount = drivenWheelCount;

        float coupledRpm;
        // If stopped, force the selected transmission gear back to first. Engine RPM
        // may still rise when the driver revs the engine, but that must never trigger
        // a gear change while the vehicle is stationary.
        if (speedKph <= 2f)
        {
            CurrentGear = 1;
            shiftTimer = 0f;
            shiftCooldown = 0f;
        }
        else
        {
            CurrentGear = Mathf.Clamp(CurrentGear, 1, gearCount);
        }

        float currentRatio = Mathf.Max(0.1f, forwardGearRatios[CurrentGear - 1]);
        float totalRatio = currentRatio * Mathf.Max(0.1f, finalDriveRatio);
        coupledRpm = Mathf.Max(idleRpm, roadSpeedWheelRpm * totalRatio);
        debugShiftRpm = coupledRpm;

        float clutchBlend = Mathf.Clamp01(speedKph / Mathf.Max(0.1f, clutchEngagementSpeed));
        float freeRevRpm = idleRpm + throttle * (redlineRpm - idleRpm);
        float targetRpm = Mathf.Lerp(freeRevRpm, coupledRpm, clutchBlend);
        if (shiftTimer > 0f)
            targetRpm = Mathf.Max(idleRpm, targetRpm * 0.72f);

        EngineRpm = Mathf.Clamp(
            Mathf.MoveTowards(EngineRpm, targetRpm, engineRpmResponse * redlineRpm * dt),
            idleRpm, redlineRpm);

        // Preserve a stationary handbrake burnout in first gear, but do not disable
        // automatic shifting during a moving handbrake turn or drift.
        bool stationaryHandbrakeBurnout = Handbrake && speedKph < 5f;

        // Keep shift thresholds valid if the Inspector or a future garage UI
        // changes redline/shift points while the vehicle is running.
        float safeRedline = Mathf.Max(idleRpm + 100f, redlineRpm);
        float safeUpshiftRpm = Mathf.Clamp(upshiftRpm, idleRpm + 50f, safeRedline - 25f);
        float safeDownshiftRpm = Mathf.Clamp(downshiftRpm, idleRpm, safeUpshiftRpm - 50f);
        debugUpshiftThresholdRpm = safeUpshiftRpm;

        // Gear changes are based on RPM coupled to road speed and the selected
        // gear ratio, not free-rev RPM. This stops a high redline reading at launch
        // from instantly racing the gearbox through all seven ratios.
        float shiftRpm = coupledRpm;
        if (speedKph <= 2f)
        {
            debugShiftStatus = "Stationary: holding first gear";
            debugCurrentGear = CurrentGear;
            debugShiftCooldownSeconds = shiftCooldown;
            debugShiftTimerSeconds = shiftTimer;
            return;
        }
        if (!HasDriver)
        {
            debugShiftStatus = "Waiting for driver";
            return;
        }
        if (throttle < 0.10f)
        {
            // Coast-down downshifts are still allowed below; throttle should only
            // be required for an upshift.
            if (shiftTimer > 0f)
            {
                debugShiftStatus = "Shift in progress";
                return;
            }
        }
        if (stationaryHandbrakeBurnout)
        {
            debugShiftStatus = "Shift held for stationary handbrake burnout";
            return;
        }
        if (shiftTimer > 0f)
        {
            debugShiftStatus = "Shift in progress";
            return;
        }

        // Keep RPM-based shift decisions and speed fallbacks mathematically aligned.
        // Derive the expected road speed directly from the same wheel radius and
        // drivetrain ratios used by coupledRpm. A maxSpeedKph-based estimate can
        // conflict with the physical RPM estimate, causing 1st/2nd gear hunting.
        float currentGearRatio = Mathf.Max(0.1f, forwardGearRatios[CurrentGear - 1]);
        float currentTotalRatio = currentGearRatio * Mathf.Max(0.1f, finalDriveRatio);
        float speedAtUpshiftRpm = safeUpshiftRpm *
            circumference * 3.6f / (60f * currentTotalRatio);
        float speedAtDownshiftRpm = safeDownshiftRpm *
            circumference * 3.6f / (60f * currentTotalRatio);

        bool speedRequestsUpshift = speedKph >= speedAtUpshiftRpm;
        bool speedRequestsDownshift = speedKph <= speedAtDownshiftRpm;

        debugShiftStatus = "RPM " + shiftRpm.ToString("0") +
            " | up " + speedAtUpshiftRpm.ToString("0.0") +
            " / down " + speedAtDownshiftRpm.ToString("0.0") + " km/h";

        // An upshift requires driver throttle. A downshift is allowed while coasting.
        // RPM and speed fallback now describe the same physical shift point.
        bool forcedRedlineUpshift = throttle >= 0.10f &&
            CurrentGear < gearCount &&
            (shiftRpm >= safeRedline - 10f || speedRequestsUpshift);
        if (shiftCooldown > 0f && !forcedRedlineUpshift)
        {
            bool urgentDownshift = CurrentGear > 1 &&
                (shiftRpm <= safeDownshiftRpm * 0.65f || speedRequestsDownshift);
            if (!urgentDownshift)
            {
                debugShiftStatus = "Shift cooldown";
                return;
            }
        }

        if (throttle >= 0.10f && CurrentGear < gearCount &&
            (shiftRpm >= safeUpshiftRpm || speedRequestsUpshift))
        {
            debugShiftStatus = speedRequestsUpshift && shiftRpm < safeUpshiftRpm
                ? "Upshifting from ratio-based speed fallback"
                : forcedRedlineUpshift
                    ? "Upshifting at redline (cooldown override)"
                    : "Upshifting";
            BeginShift(CurrentGear + 1);
            debugCurrentGear = CurrentGear;
            debugShiftCooldownSeconds = shiftCooldown;
            debugShiftTimerSeconds = shiftTimer;
        }
        else if (CurrentGear > 1 && speedKph > 2f &&
            (shiftRpm <= safeDownshiftRpm || speedRequestsDownshift))
        {
            debugShiftStatus = speedRequestsDownshift && shiftRpm > safeDownshiftRpm
                ? "Downshifting from ratio-based speed fallback"
                : "Downshifting";
            BeginShift(CurrentGear - 1);
            debugCurrentGear = CurrentGear;
            debugShiftCooldownSeconds = shiftCooldown;
            debugShiftTimerSeconds = shiftTimer;
        }
        else if (CurrentGear >= gearCount && shiftRpm >= safeUpshiftRpm)
        {
            debugShiftStatus = "At highest configured gear";
        }
        else if (CurrentGear >= gearCount)
        {
            debugShiftStatus = "At highest configured gear";
        }
        else
        {
            debugShiftStatus = throttle >= 0.10f
                ? "Waiting for RPM/speed upshift threshold"
                : "Coasting: waiting for downshift threshold";
        }
    }

    private void BeginShift(int nextGear)
    {
        CurrentGear = Mathf.Clamp(nextGear, 1, Mathf.Max(1, forwardGearRatios.Length));
        shiftTimer = gearShiftTime;
        shiftCooldown = gearShiftTime + 0.35f;
    }

    private float CalculateDriveTorque(float throttle)
    {
        if (Mathf.Abs(throttle) < 0.01f)
            return 0f;
        if (throttle < 0f)
            return throttle * motorTorque * 0.55f * ExternalEnginePowerMultiplier;

        float rpm01 = Mathf.InverseLerp(idleRpm, redlineRpm, EngineRpm);
        float torqueCurve = rpm01 < 0.55f
            ? Mathf.Lerp(0.78f, 1f, rpm01 / 0.55f)
            : Mathf.Lerp(1f, 0.72f, Mathf.InverseLerp(0.55f, 1f, rpm01));
        float ratio = forwardGearRatios != null && forwardGearRatios.Length > 0
            ? forwardGearRatios[Mathf.Clamp(CurrentGear - 1, 0, forwardGearRatios.Length - 1)]
            : 1f;
        float gearboxMultiplier = ratio * finalDriveRatio;
        float shiftCut = shiftTimer > 0f ? 0.18f : 1f;
        float lowSpeedClutch = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(Speed / Mathf.Max(0.1f, clutchEngagementSpeed)));
        float wheelTorque = throttle * motorTorque * torqueCurve * gearboxMultiplier * 0.22f * shiftCut * lowSpeedClutch;

        // Limit delivered wheel torque at very low speed, then blend the cap out
        // as the clutch engages. Read every physics step for live tuning/upgrades.
        if (maxMotorTorqueAtLowSpeed > 0f)
        {
            float lowSpeedBlend = 1f - Mathf.Clamp01(Speed / Mathf.Max(0.1f, clutchEngagementSpeed));
            float lowSpeedCap = Mathf.Lerp(Mathf.Abs(wheelTorque), maxMotorTorqueAtLowSpeed, lowSpeedBlend);
            wheelTorque = Mathf.Sign(wheelTorque) * Mathf.Min(Mathf.Abs(wheelTorque), lowSpeedCap);
        }

        return wheelTorque * ExternalEnginePowerMultiplier;
    }

    private void DriveFallback()
    {
        // Keeps the prototype driveable until WheelColliders have been added and assigned.
        if (!warnedNoWheels)
        {
            Debug.LogWarning($"{name}: no WheelColliders assigned. Using temporary fallback driving. Add and assign four WheelColliders for wheel-based physics.", this);
            warnedNoWheels = true;
        }

        float dt = Time.fixedDeltaTime;
        Vector3 velocity = GetVelocity();
        float forwardSpeed = Vector3.Dot(velocity, transform.forward);
        float lateralSpeed = Vector3.Dot(velocity, transform.right);
        float throttle = HasDriver && ExternalEngineEnabled ? Throttle : 0f;
        bool serviceBraking = HasDriver && (ServiceBrake > 0.01f || (throttle < -0.05f && forwardSpeed > 0.5f));
        bool opposingInput = Mathf.Abs(forwardSpeed) > 0.5f &&
                             Mathf.Abs(throttle) > 0.05f &&
                             Mathf.Sign(throttle) != Mathf.Sign(forwardSpeed);
        IsBraking = HasDriver && (Handbrake || serviceBraking || opposingInput);

        if (HasDriver && Handbrake)
        {
            forwardSpeed = Mathf.MoveTowards(forwardSpeed, 0f, fallbackBrakeDeceleration * dt);
        }
        else if (serviceBraking || opposingInput)
        {
            float brakeInput = Mathf.Max(ServiceBrake, serviceBraking ? Mathf.Abs(throttle) : 0f);
            forwardSpeed = Mathf.MoveTowards(forwardSpeed, 0f, fallbackBrakeDeceleration * Mathf.Max(0.01f, brakeInput) * dt);
            if (Mathf.Abs(forwardSpeed) < 0.05f && throttle < -0.05f && ServiceBrake <= 0.01f)
                forwardSpeed = -0.1f;
        }
        else if (Mathf.Abs(throttle) > 0.01f)
        {
            float limit = throttle > 0f ? fallbackMaxSpeed : fallbackMaxReverseSpeed;
            float room = 1f - Mathf.Clamp01(Mathf.Abs(forwardSpeed) / limit);
            forwardSpeed += throttle * fallbackAcceleration * room * dt;
            forwardSpeed = Mathf.Clamp(forwardSpeed, -fallbackMaxReverseSpeed, fallbackMaxSpeed);
        }
        else
            forwardSpeed = Mathf.MoveTowards(forwardSpeed, 0f, 1.2f * dt);

        lateralSpeed = Mathf.MoveTowards(lateralSpeed, 0f, fallbackSideGrip * Mathf.Max(1f, Mathf.Abs(forwardSpeed)) * dt);
        SetVelocity(transform.forward * forwardSpeed + transform.right * lateralSpeed + Vector3.up * velocity.y);

        if (HasDriver)
        {
            float speedFactor = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / 8f);
            float targetYaw = Steer * 75f * Mathf.Deg2Rad * speedFactor * (forwardSpeed < -0.1f ? -1f : 1f);
            Vector3 angular = rb.angularVelocity;
            angular.y = Mathf.Lerp(angular.y, targetYaw, 1f - Mathf.Exp(-5f * dt));
            rb.angularVelocity = angular;
        }

        debugGrounded = true;
    }

    private bool AnyWheelGrounded()
    {
        foreach (WheelCollider wheel in validWheels)
        {
            if (wheel != null && wheel.isGrounded)
                return true;
        }
        return false;
    }

    private void UpdateWheelVisuals()
    {
        if (!updateWheelVisuals || wheelVisuals == null)
            return;

        int count = Mathf.Min(wheelColliders.Length, wheelVisuals.Length);
        for (int i = 0; i < count; i++)
        {
            WheelCollider wheel = wheelColliders[i];
            Transform visual = wheelVisuals[i];
            if (wheel == null || visual == null)
                continue;

            wheel.GetWorldPose(out Vector3 position, out Quaternion rotation);

            // Apply the WheelCollider's world pose plus the visual's cached model
            // alignment. This prevents imported wheel meshes with different local
            // axes/offsets from snapping away from the chassis at runtime.
            if (wheelVisualPositionOffsets != null && i < wheelVisualPositionOffsets.Length)
                position += rotation * wheelVisualPositionOffsets[i];

            if (wheelVisualRotationOffsets != null && i < wheelVisualRotationOffsets.Length)
                rotation *= wheelVisualRotationOffsets[i];

            visual.SetPositionAndRotation(position, rotation);
        }
    }

    private bool ContainsIndex(int[] indexes, int value)
    {
        if (indexes == null)
            return false;

        for (int i = 0; i < indexes.Length; i++)
        {
            if (indexes[i] == value)
                return true;
        }
        return false;
    }

    private void StopWheelTorque()
    {
        if (wheelColliders == null)
            return;

        foreach (WheelCollider wheel in wheelColliders)
        {
            if (wheel == null)
                continue;

            wheel.motorTorque = 0f;
            wheel.brakeTorque = 0f;
            wheel.steerAngle = 0f;
        }
    }

    private Vector3 GetVelocity()
    {
#if UNITY_6000_0_OR_NEWER
        return rb.linearVelocity;
#else
        return rb.velocity;
#endif
    }

    private void SetVelocity(Vector3 value)
    {
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = value;
#else
        rb.velocity = value;
#endif
    }

    private void OnDestroy()
    {
        StopWheelTorque();
    }
}
