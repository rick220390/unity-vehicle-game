using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple WheelCollider-based AI controller for the warehouse tractor unit.
/// Assign steering and drive wheels explicitly when desired; otherwise the
/// front-most axle steers and all rear axles drive automatically.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class WarehouseTruckPhysicsController : MonoBehaviour
{
    [Header("Vehicle references")]
    [SerializeField] private Rigidbody truckBody;
    [SerializeField] private WheelCollider[] steeringWheels;
    [SerializeField] private WheelCollider[] driveWheels;
    [SerializeField] private WheelCollider[] allWheels;

    [Header("AI driving")]
    [SerializeField, Min(1f)] private float maximumSpeedKph = 18f;
    [SerializeField, Min(1f)] private float maximumSteerAngle = 32f;
    [SerializeField, Min(100f)] private float motorTorquePerWheel = 1800f;
    [SerializeField, Min(100f)] private float serviceBrakeTorque = 5000f;
    [SerializeField, Min(0.1f)] private float steeringSensitivity = 1f;
    [SerializeField, Min(0.1f)] private float waypointSlowdownDistance = 5f;
    [SerializeField, Min(1f)] private float minimumCornerSpeedKph = 3f;
    [SerializeField, Range(10f, 90f)] private float fullCorneringAngle = 65f;
    [SerializeField, Min(1f)] private float steeringRateDegreesPerSecond = 55f;

    private Transform target;
    private bool reversing;
    private bool aiEnabled;
    private float requestedSpeedKph;

    public Rigidbody Body => truckBody;
    public float SpeedKph => truckBody != null ? truckBody.velocity.magnitude * 3.6f : 0f;
    public bool IsDriving => aiEnabled;

    private void Awake()
    {
        if (truckBody == null)
            truckBody = GetComponent<Rigidbody>();

        if (truckBody == null)
        {
            Debug.LogError("WarehouseTruckPhysicsController: Tractor Rigidbody is missing.", this);
            enabled = false;
            return;
        }

        truckBody.isKinematic = false;
        truckBody.useGravity = true;
        truckBody.interpolation = RigidbodyInterpolation.Interpolate;
        truckBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        ResolveWheels();
    }

    private void ResolveWheels()
    {
        if (allWheels == null || allWheels.Length == 0)
            allWheels = GetComponentsInChildren<WheelCollider>(true);

        List<WheelCollider> valid = new List<WheelCollider>();
        foreach (WheelCollider wheel in allWheels)
            if (wheel != null) valid.Add(wheel);

        if ((steeringWheels == null || steeringWheels.Length == 0) && valid.Count > 0)
        {
            float frontZ = float.NegativeInfinity;
            foreach (WheelCollider wheel in valid)
                frontZ = Mathf.Max(frontZ, transform.InverseTransformPoint(wheel.transform.position).z);

            foreach (WheelCollider wheel in valid)
            {
                float z = transform.InverseTransformPoint(wheel.transform.position).z;
                if (z >= frontZ - 0.35f)
                    AddWheel(ref steeringWheels, wheel);
            }
        }

        if ((driveWheels == null || driveWheels.Length == 0) && valid.Count > 0)
        {
            float frontZ = float.NegativeInfinity;
            foreach (WheelCollider wheel in valid)
                frontZ = Mathf.Max(frontZ, transform.InverseTransformPoint(wheel.transform.position).z);

            foreach (WheelCollider wheel in valid)
            {
                float z = transform.InverseTransformPoint(wheel.transform.position).z;
                if (z < frontZ - 0.5f)
                    AddWheel(ref driveWheels, wheel);
            }
        }

        if (steeringWheels == null || steeringWheels.Length == 0)
            Debug.LogError("WarehouseTruckPhysicsController: No steering WheelColliders found. Assign the front axle in the Inspector.", this);
        if (driveWheels == null || driveWheels.Length == 0)
            Debug.LogError("WarehouseTruckPhysicsController: No drive WheelColliders found. Assign the rear axle(s) in the Inspector.", this);
        if (valid.Count == 0)
            Debug.LogError("WarehouseTruckPhysicsController: No WheelColliders found under the tractor.", this);
    }

    private static void AddWheel(ref WheelCollider[] wheels, WheelCollider wheel)
    {
        List<WheelCollider> list = wheels != null ? new List<WheelCollider>(wheels) : new List<WheelCollider>();
        if (!list.Contains(wheel))
        {
            list.Add(wheel);
            wheels = list.ToArray();
        }
    }

    public void DriveTo(Transform destination, bool reverse, float speedKph)
    {
        target = destination;
        reversing = reverse;
        requestedSpeedKph = Mathf.Clamp(speedKph, 1f, maximumSpeedKph);
        aiEnabled = target != null;
    }

    public void StopDriving()
    {
        aiEnabled = false;
        target = null;
        ApplyBrakes();
    }

    private void FixedUpdate()
    {
        if (truckBody == null)
            return;

        if (!aiEnabled || target == null)
        {
            ApplyBrakes();
            return;
        }

        Vector3 toTarget = target.position - truckBody.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.01f)
        {
            ApplyBrakes();
            return;
        }

        // Steer toward the waypoint using a signed heading angle. Unlike the
        // previous Abs(localTarget.z) calculation, this preserves whether the
        // waypoint is ahead or behind the tractor.
        Vector3 desiredTravelDirection = reversing ? -truckBody.transform.forward : truckBody.transform.forward;
        float headingError = Vector3.SignedAngle(
            desiredTravelDirection,
            toTarget.normalized,
            Vector3.up);

        float steering = Mathf.Clamp(
            headingError * steeringSensitivity,
            -maximumSteerAngle,
            maximumSteerAngle);

        // Smooth steering changes to prevent the AI snapping to full lock.
        foreach (WheelCollider wheel in steeringWheels)
        {
            if (wheel == null) continue;
            wheel.steerAngle = Mathf.MoveTowards(
                wheel.steerAngle,
                steering,
                steeringRateDegreesPerSecond * Time.fixedDeltaTime);
        }

        float distance = toTarget.magnitude;
        float cornerFactor = Mathf.Clamp01(
            Mathf.Abs(headingError) / Mathf.Max(1f, fullCorneringAngle));
        float targetSpeed = Mathf.Lerp(requestedSpeedKph, minimumCornerSpeedKph, cornerFactor);

        // Also slow down as the tractor approaches the current waypoint.
        if (distance < waypointSlowdownDistance)
            targetSpeed *= Mathf.Clamp01(distance / waypointSlowdownDistance);

        float currentSpeed = SpeedKph * (reversing ? -1f : 1f);
        float speedError = targetSpeed - Mathf.Abs(currentSpeed);
        float torque = Mathf.Clamp(speedError * motorTorquePerWheel, 0f, motorTorquePerWheel);

        foreach (WheelCollider wheel in driveWheels)
        {
            if (wheel == null) continue;
            wheel.motorTorque = reversing ? -torque : torque;
            wheel.brakeTorque = 0f;
        }

        foreach (WheelCollider wheel in allWheels)
        {
            if (wheel == null || Contains(driveWheels, wheel)) continue;
            wheel.motorTorque = 0f;
            wheel.brakeTorque = 0f;
        }
    }

    private void ApplyBrakes()
    {
        if (allWheels == null) return;
        foreach (WheelCollider wheel in allWheels)
        {
            if (wheel == null) continue;
            wheel.motorTorque = 0f;
            wheel.brakeTorque = serviceBrakeTorque;
            wheel.steerAngle = 0f;
        }
    }

    private static bool Contains(WheelCollider[] wheels, WheelCollider targetWheel)
    {
        if (wheels == null) return false;
        foreach (WheelCollider wheel in wheels)
            if (wheel == targetWheel) return true;
        return false;
    }
}