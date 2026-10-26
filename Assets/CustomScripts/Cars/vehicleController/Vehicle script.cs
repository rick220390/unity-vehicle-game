using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Vehicle : MonoBehaviour
{
    // Registry so the player can find cars without layers or physics queries.
    public static readonly List<Vehicle> All = new List<Vehicle>();

    [Header("Points")]
    public Transform seatPoint;
    public Transform cameraPoint;          // Optional first-person camera point.
    public Transform[] exitPoints;         // Driver side first.

    [Header("Driving (m/s² and m/s)")]
    [Min(0.1f)] public float acceleration = 10f;
    [Min(1f)] public float maxSpeed = 32f;
    [Min(1f)] public float maxReverseSpeed = 9f;
    [Min(1f)] public float brakeDeceleration = 18f;
    [Min(0f)] public float coastDeceleration = 1.2f;
    [Min(1f)] public float handbrakeDeceleration = 14f;

    [Header("Steering and grip")]
    [Min(1f)] public float maxSteerRate = 75f;
    [Min(1f)] public float steerFullSpeed = 8f;
    [Min(0f)] public float sideGrip = 5f;
    [Range(0f, 1f)] public float handbrakeGripMultiplier = 0.22f;
    [Min(0.1f)] public float steeringResponse = 5f;
    [Min(0f)] public float groundCheckDistance = 0.45f;

    [Header("Physics")]
    public Vector3 centerOfMassOffset = new Vector3(0f, -0.45f, 0f);
    public bool autoSlipperyMaterial = true;
    [Min(0f)] public float linearDamping = 0.03f;
    [Min(0f)] public float angularDamping = 1.5f;
    [Min(1f)] public float maxAngularVelocity = 3f;

    [Header("Debug (read only, watch while playing)")]
    public bool debugGrounded;
    public float debugThrottle;
    public float debugSpeed;
    public bool debugHasDriver;

    public bool HasDriver { get; private set; }
    public float Throttle { get; private set; }
    public float Steer { get; private set; }
    public bool Handbrake { get; private set; }
    public float Speed => Velocity.magnitude;
    public Collider BodyCollider { get; private set; }

    private Rigidbody rb;
    private readonly RaycastHit[] groundHits = new RaycastHit[16];
    private PhysicMaterial createdMaterial3D;
    private PhysicsMaterial createdMaterial6;

    private Vector3 Velocity
    {
        get
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        BodyCollider = GetComponent<Collider>();

        // Interpolation smooths rendered movement between fixed physics steps.
        // This is the main defence against visible judder when following a Rigidbody.
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.maxAngularVelocity = maxAngularVelocity;
        rb.drag = linearDamping;
        rb.angularDrag = angularDamping;
        rb.ResetCenterOfMass();
        rb.centerOfMass += centerOfMassOffset;

        if (autoSlipperyMaterial && BodyCollider != null)
        {
#if UNITY_6000_0_OR_NEWER
            createdMaterial6 = new PhysicsMaterial("CarSlide")
            {
                dynamicFriction = 0.08f,
                staticFriction = 0.08f,
                frictionCombine = PhysicsMaterialCombine.Minimum
            };
            BodyCollider.sharedMaterial = createdMaterial6;
#else
            createdMaterial3D = new PhysicMaterial("CarSlide")
            {
                dynamicFriction = 0.08f,
                staticFriction = 0.08f,
                frictionCombine = PhysicMaterialCombine.Minimum
            };
            BodyCollider.sharedMaterial = createdMaterial3D;
#endif
        }
    }

    private void OnEnable()
    {
        if (!All.Contains(this))
            All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
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
    }

    // The only way external scripts should control the car.
    public void SetInput(float throttle, float steer, bool handbrake = false)
    {
        Throttle = Mathf.Clamp(throttle, -1f, 1f);
        Steer = Mathf.Clamp(steer, -1f, 1f);
        Handbrake = handbrake;
    }

    private void FixedUpdate()
    {
        bool grounded = CheckGrounded();

        debugGrounded = grounded;
        debugThrottle = Throttle;
        debugSpeed = Speed;
        debugHasDriver = HasDriver;

        if (!grounded)
            return;

        float dt = Time.fixedDeltaTime;
        Vector3 velocity = Velocity;
        float forwardSpeed = Vector3.Dot(velocity, transform.forward);
        float lateralSpeed = Vector3.Dot(velocity, transform.right);
        float throttle = HasDriver ? Throttle : 0f;

        // Accelerate smoothly toward the speed limit. Opposite input brakes first,
        // then allows the car to move in the opposite direction without a speed snap.
        if (HasDriver && Handbrake)
        {
            forwardSpeed = Mathf.MoveTowards(
                forwardSpeed, 0f, handbrakeDeceleration * dt);
        }
        else if (Mathf.Abs(throttle) > 0.01f)
        {
            bool brakingAgainstMotion =
                Mathf.Abs(forwardSpeed) > 0.35f &&
                Mathf.Sign(throttle) != Mathf.Sign(forwardSpeed);

            if (brakingAgainstMotion)
            {
                forwardSpeed = Mathf.MoveTowards(
                    forwardSpeed, 0f, brakeDeceleration * Mathf.Abs(throttle) * dt);
            }
            else
            {
                float limit = throttle > 0f ? maxSpeed : maxReverseSpeed;
                float speedRoom = 1f - Mathf.Clamp01(Mathf.Abs(forwardSpeed) / limit);
                forwardSpeed += throttle * acceleration * speedRoom * dt;
                forwardSpeed = Mathf.Clamp(forwardSpeed, -maxReverseSpeed, maxSpeed);
            }
        }
        else
        {
            forwardSpeed = Mathf.MoveTowards(
                forwardSpeed, 0f, coastDeceleration * dt);
        }

        // Rebuild the forward component while damping sideways sliding like tire grip.
        // Holding the handbrake reduces rear grip so the car can slide into a turn.
        float grip = sideGrip * (HasDriver && Handbrake ? handbrakeGripMultiplier : 1f);
        lateralSpeed = Mathf.MoveTowards(lateralSpeed, 0f, grip * Mathf.Max(1f, Mathf.Abs(forwardSpeed)) * dt);

        Vector3 verticalVelocity = Vector3.Project(velocity, Vector3.up);
        Vector3 targetVelocity =
            transform.forward * forwardSpeed +
            transform.right * lateralSpeed +
            verticalVelocity;

        SetVelocity(targetVelocity);

        // Steering builds with speed, but is deliberately gentler at high speed.
        // Reverse steering naturally changes direction because forwardSpeed is signed.
        if (HasDriver)
        {
            float speedFactor = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / steerFullSpeed);
            float highSpeedReduction = 1f - 0.35f * Mathf.Clamp01(
                (Mathf.Abs(forwardSpeed) - steerFullSpeed) / Mathf.Max(1f, maxSpeed - steerFullSpeed));
            float direction = forwardSpeed < -0.1f ? -1f : 1f;
            float targetYawRate = Steer * maxSteerRate * Mathf.Deg2Rad *
                                  speedFactor * highSpeedReduction * direction;

            Vector3 angularVelocity = rb.angularVelocity;
            angularVelocity.y = Mathf.Lerp(
                angularVelocity.y,
                targetYawRate,
                1f - Mathf.Exp(-steeringResponse * dt));
            rb.angularVelocity = angularVelocity;
        }
        else
        {
            Vector3 angularVelocity = rb.angularVelocity;
            angularVelocity.y = Mathf.MoveTowards(angularVelocity.y, 0f, steeringResponse * dt);
            rb.angularVelocity = angularVelocity;
        }
    }

    private bool CheckGrounded()
    {
        Vector3 origin = BodyCollider != null
            ? BodyCollider.bounds.center
            : transform.position + Vector3.up * 0.5f;
        float reach = (BodyCollider != null ? BodyCollider.bounds.extents.y : 0.5f)
                      + groundCheckDistance;

        int hitCount = Physics.RaycastNonAlloc(
            origin, Vector3.down, groundHits, reach, ~0,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = groundHits[i].collider;
            if (hitCollider == null)
                continue;

            // Ignore all colliders belonging to this car.
            if (hitCollider.transform == transform ||
                hitCollider.transform.IsChildOf(transform))
                continue;

            return true;
        }

        return false;
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
        if (createdMaterial3D != null)
            Destroy(createdMaterial3D);
        if (createdMaterial6 != null)
            Destroy(createdMaterial6);
    }
}
