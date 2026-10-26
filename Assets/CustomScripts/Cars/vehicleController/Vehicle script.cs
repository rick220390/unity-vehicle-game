using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Vehicle : MonoBehaviour
{
    public static readonly List<Vehicle> All = new List<Vehicle>();

    [Header("Vehicle points")]
    public Transform seatPoint;
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
    [Min(100f)] public float motorTorque = 2600f;
    [Min(500f)] public float maxMotorTorqueAtLowSpeed = 2200f;
    [Min(1f)] public float maxSpeedKph = 150f;
    [Min(1f)] public float maxReverseSpeedKph = 30f;
    [Min(100f)] public float serviceBrakeTorque = 1800f;
    [Min(100f)] public float handbrakeTorque = 4500f;
    [Min(0f)] public float engineBrakeTorque = 80f;
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

    public bool HasDriver { get; private set; }
    public float Throttle { get; private set; }
    public float Steer { get; private set; }
    public bool Handbrake { get; private set; }
    public float Speed => GetVelocity().magnitude;
    public Collider BodyCollider { get; private set; }

    private Rigidbody rb;
    private float appliedThrottle;
    private float appliedBrake;
    private float appliedSteer;
    private bool warnedNoWheels;
    private readonly List<WheelCollider> validWheels = new List<WheelCollider>();
    private Vector3[] wheelVisualPositionOffsets;
    private Quaternion[] wheelVisualRotationOffsets;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        BodyCollider = GetComponent<Collider>();

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
            wheel.ConfigureVehicleSubsteps(5f, 12, 15);
            wheel.suspensionDistance = suspensionDistance;
            JointSpring spring = wheel.suspensionSpring;
            spring.spring = suspensionSpring;
            spring.damper = suspensionDamper;
            spring.targetPosition = 0.5f;
            wheel.suspensionSpring = spring;

            WheelFrictionCurve forward = wheel.forwardFriction;
            forward.stiffness = forwardGrip;
            wheel.forwardFriction = forward;

            WheelFrictionCurve sideways = wheel.sidewaysFriction;
            int wheelIndex = validWheels.IndexOf(wheel);
            bool frontWheel = ContainsIndex(steeringWheelIndexes, wheelIndex);
            sideways.stiffness = frontWheel ? frontSidewaysGrip : rearSidewaysGrip;
            wheel.sidewaysFriction = sideways;

            if (wheel.radius <= 0.01f)
                wheel.radius = wheelRadius;
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
        SetInput(0f, 0f, false);
        StopWheelTorque();
    }

    public void SetInput(float throttle, float steer, bool handbrake = false)
    {
        Throttle = Mathf.Clamp(throttle, -1f, 1f);
        Steer = Mathf.Clamp(steer, -1f, 1f);
        Handbrake = handbrake;
    }

    private void FixedUpdate()
    {
        debugThrottle = Throttle;
        debugSpeedKph = Speed * 3.6f;
        debugHasDriver = HasDriver;
        debugUsingWheelColliders = validWheels.Count > 0;

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
        float targetBrake = 0f;

        // Opposite direction input first applies brakes; reverse engages only near a stop.
        float forwardSpeed = Vector3.Dot(GetVelocity(), transform.forward);
        bool oppositeInput = Mathf.Abs(forwardSpeed) > 1.0f &&
                             Mathf.Abs(targetThrottle) > 0.05f &&
                             Mathf.Sign(targetThrottle) != Mathf.Sign(forwardSpeed);

        if (oppositeInput)
        {
            targetBrake = Mathf.Abs(targetThrottle) * serviceBrakeTorque;
            targetThrottle = 0f;
        }
        else if (Mathf.Abs(targetThrottle) < 0.05f)
        {
            targetBrake = engineBrakeTorque;
        }

        if (HasDriver && Handbrake)
            targetBrake = Mathf.Max(targetBrake, handbrakeTorque);

        appliedThrottle = Mathf.MoveTowards(
            appliedThrottle, targetThrottle, throttleResponse * dt);
        appliedBrake = Mathf.MoveTowards(
            appliedBrake, targetBrake, brakeResponse * serviceBrakeTorque * dt);
        appliedSteer = Mathf.Lerp(
            appliedSteer, HasDriver ? Steer : 0f,
            1f - Mathf.Exp(-steeringResponse * dt));

        float speedLimit = appliedThrottle < 0f ? maxReverseSpeedKph : maxSpeedKph;
        bool speedLimited = speedKph >= speedLimit && Mathf.Sign(appliedThrottle) == Mathf.Sign(forwardSpeed);
        float torque = speedLimited ? 0f : appliedThrottle * motorTorque;

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
        float throttle = HasDriver ? Throttle : 0f;

        if (HasDriver && Handbrake)
        {
            forwardSpeed = Mathf.MoveTowards(forwardSpeed, 0f, fallbackBrakeDeceleration * dt);
        }
        else if (Mathf.Abs(throttle) > 0.01f)
        {
            bool opposing = Mathf.Abs(forwardSpeed) > 0.5f && Mathf.Sign(throttle) != Mathf.Sign(forwardSpeed);
            if (opposing)
                forwardSpeed = Mathf.MoveTowards(forwardSpeed, 0f, fallbackBrakeDeceleration * Mathf.Abs(throttle) * dt);
            else
            {
                float limit = throttle > 0f ? fallbackMaxSpeed : fallbackMaxReverseSpeed;
                float room = 1f - Mathf.Clamp01(Mathf.Abs(forwardSpeed) / limit);
                forwardSpeed += throttle * fallbackAcceleration * room * dt;
                forwardSpeed = Mathf.Clamp(forwardSpeed, -fallbackMaxReverseSpeed, fallbackMaxSpeed);
            }
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
