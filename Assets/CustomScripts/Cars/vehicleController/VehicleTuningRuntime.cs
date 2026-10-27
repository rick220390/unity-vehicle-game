using UnityEngine;

/// <summary>
/// Runtime bridge between a vehicle and garage/customisation UI.
/// UI buttons can call ApplyProfile, UpgradeEngine, UpgradeTransmission or SetTyreGrip.
/// Values are applied to the live Vehicle component, so no scene restart is needed.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Vehicle))]
public class VehicleTuningRuntime : MonoBehaviour
{
    public VehicleTuningProfile startingProfile;
    public bool applyProfileOnAwake = true;

    private Vehicle vehicle;

    private void Awake()
    {
        vehicle = GetComponent<Vehicle>();
        if (applyProfileOnAwake && startingProfile != null)
            ApplyProfile(startingProfile);
    }

    public void ApplyProfile(VehicleTuningProfile profile)
    {
        if (profile == null) return;
        EnsureVehicle();
        profile.ApplyTo(vehicle);
        startingProfile = profile;
    }

    /// <summary>Apply an engine upgrade multiplier, e.g. 1.1 for +10% torque.</summary>
    public void UpgradeEngine(float torqueMultiplier, float redlineIncreaseRpm = 0f)
    {
        EnsureVehicle();
        vehicle.motorTorque = Mathf.Max(100f, vehicle.motorTorque * Mathf.Max(0.1f, torqueMultiplier));
        vehicle.redlineRpm = Mathf.Max(vehicle.idleRpm + 100f, vehicle.redlineRpm + redlineIncreaseRpm);
        vehicle.upshiftRpm = Mathf.Clamp(vehicle.upshiftRpm, vehicle.idleRpm + 50f, vehicle.redlineRpm - 25f);
    }

    /// <summary>Apply a transmission/final-drive upgrade. A shorter final drive improves acceleration but lowers road speed at a given RPM.</summary>
    public void UpgradeTransmission(float finalDriveMultiplier, float shiftTimeMultiplier = 1f)
    {
        EnsureVehicle();
        vehicle.finalDriveRatio = Mathf.Clamp(vehicle.finalDriveRatio * Mathf.Max(0.1f, finalDriveMultiplier), 1f, 12f);
        vehicle.gearShiftTime = Mathf.Clamp(vehicle.gearShiftTime * Mathf.Max(0.1f, shiftTimeMultiplier), 0.1f, 2f);
    }

    /// <summary>Set live tyre grip values from a tyre purchase or setup screen.</summary>
    public void SetTyreGrip(float longitudinalGrip, float frontLateralGrip, float rearLateralGrip)
    {
        EnsureVehicle();
        vehicle.forwardGrip = Mathf.Clamp(longitudinalGrip, 0.1f, 5f);
        vehicle.frontSidewaysGrip = Mathf.Clamp(frontLateralGrip, 0.1f, 5f);
        vehicle.rearSidewaysGrip = Mathf.Clamp(rearLateralGrip, 0.1f, 5f);
    }

    /// <summary>Set a live speed limiter from a gameplay setting or vehicle class.</summary>
    public void SetSpeedLimit(float forwardKph, float reverseKph)
    {
        EnsureVehicle();
        vehicle.maxSpeedKph = Mathf.Max(1f, forwardKph);
        vehicle.maxReverseSpeedKph = Mathf.Max(1f, reverseKph);
    }

    private void EnsureVehicle()
    {
        if (vehicle == null) vehicle = GetComponent<Vehicle>();
    }
}
