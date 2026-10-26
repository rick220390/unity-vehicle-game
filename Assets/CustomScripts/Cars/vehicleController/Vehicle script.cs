using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Vehicle : MonoBehaviour
{
    // Registry so the player can find cars without layers or physics queries
    public static readonly List<Vehicle> All = new List<Vehicle>();

    [Header("Points")]
    public Transform seatPoint;
    public Transform cameraPoint;          // unused for now (future first-person view)
    public Transform[] exitPoints;         // driver side first

    [Header("Arcade driving (m/s^2, m/s)")]
    public float acceleration = 12f;
    public float maxSpeed = 25f;
    public float maxReverseSpeed = 8f;
    public float brakeDeceleration = 20f;      // throttle opposite to motion
    public float coastDeceleration = 3f;       // no input / no driver
    public float handbrakeDeceleration = 25f;
    public float maxSteerRate = 100f;          // degrees per second
    public float steerFullSpeed = 6f;          // speed at which steering is at full strength
    public float sideGrip = 6f;                // higher = less sliding sideways
    public float groundCheckDistance = 0.4f;   // extra reach below the collider's bottom

    [Header("Physics")]
    public Vector3 centerOfMassOffset = new Vector3(0f, -0.4f, 0f);
    public bool autoSlipperyMaterial = true;   // box has no wheels, so remove ground friction

    [Header("Debug (read only, watch while playing)")]
    public bool debugGrounded;
    public float debugThrottle;
    public float debugSpeed;
    public bool debugHasDriver;

    public bool HasDriver { get; private set; }
    public float Throttle { get; private set; }   // -1..1
    public float Steer { get; private set; }      // -1..1
    public bool Handbrake { get; private set; }
    public float Speed => Velocity.magnitude;
    public Collider BodyCollider { get; private set; }

    Rigidbody rb;

    Vector3 Velocity
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

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        BodyCollider = GetComponent<Collider>();

        rb.ResetCenterOfMass();
        rb.centerOfMass += centerOfMassOffset;

        if (autoSlipperyMaterial && BodyCollider != null)
        {
#if UNITY_6000_0_OR_NEWER
            var mat = new PhysicsMaterial("CarSlide")
            {
                dynamicFriction = 0.05f,
                staticFriction = 0.05f,
                frictionCombine = PhysicsMaterialCombine.Minimum
            };
#else
            var mat = new PhysicMaterial("CarSlide")
            {
                dynamicFriction = 0.05f,
                staticFriction = 0.05f,
                frictionCombine = PhysicMaterialCombine.Minimum
            };
#endif
            BodyCollider.sharedMaterial = mat;
        }
    }

    void OnEnable()  { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // Called by the player when entering / exiting
    public bool TryReserve()
    {
        if (HasDriver) return false;
        HasDriver = true;
        return true;
    }

    public void Release()
    {
        HasDriver = false;
        SetInput(0f, 0f, false);
    }

    // The ONLY way anything controls the car
    public void SetInput(float throttle, float steer, bool handbrake = false)
    {
        Throttle = Mathf.Clamp(throttle, -1f, 1f);
        Steer = Mathf.Clamp(steer, -1f, 1f);
        Handbrake = handbrake;
    }

    void FixedUpdate()
    {
        // Only drive / grip while touching ground
        Vector3 origin = BodyCollider != null ? BodyCollider.bounds.center : transform.position + Vector3.up * 0.5f;
        float reach = (BodyCollider != null ? BodyCollider.bounds.extents.y : 0.5f) + groundCheckDistance;
        bool grounded = Physics.Raycast(origin, Vector3.down, reach, ~0, QueryTriggerInteraction.Ignore);

        debugGrounded = grounded;
        debugThrottle = Throttle;
        debugSpeed = Speed;
        debugHasDriver = HasDriver;

        if (!grounded) return;

        float dt = Time.fixedDeltaTime;
        Vector3 vel = Velocity;
        float fwd = Vector3.Dot(vel, transform.forward);
        float lateral = Vector3.Dot(vel, transform.right);

        float throttle = HasDriver ? Throttle : 0f;
        float change;   // desired change in forward speed this step

        if (HasDriver && Handbrake)
        {
            change = -Mathf.Sign(fwd) * Mathf.Min(handbrakeDeceleration * dt, Mathf.Abs(fwd));
        }
        else if (Mathf.Abs(throttle) > 0.01f)
        {
            bool opposing = Mathf.Abs(fwd) > 0.5f && Mathf.Sign(throttle) != Mathf.Sign(fwd);
            if (opposing)
            {
                change = Mathf.Sign(throttle) * brakeDeceleration * dt;
            }
            else
            {
                float limit = throttle > 0f ? maxSpeed : maxReverseSpeed;
                float room = 1f - Mathf.Clamp01(Mathf.Abs(fwd) / limit);
                change = throttle * acceleration * room * dt;
            }
        }
        else
        {
            change = -Mathf.Sign(fwd) * Mathf.Min(coastDeceleration * dt, Mathf.Abs(fwd));
        }

        // Forward speed change + cancel sideways slide (cheap tire grip)
        Vector3 dv = transform.forward * change
                   - transform.right * lateral * Mathf.Clamp01(sideGrip * dt);
        rb.AddForce(dv, ForceMode.VelocityChange);

        // Steering: yaw rate scales with speed, flips when reversing
        if (HasDriver)
        {
            float steerFactor = Mathf.Clamp(fwd / steerFullSpeed, -1f, 1f);
            float targetYaw = Steer * maxSteerRate * Mathf.Deg2Rad * steerFactor;
            Vector3 av = rb.angularVelocity;
            av.y = Mathf.Lerp(av.y, targetYaw, 1f - Mathf.Exp(-10f * dt));
            rb.angularVelocity = av;
        }
    }
}