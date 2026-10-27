using UnityEngine;

/// <summary>
/// Reusable, game-editable vehicle setup. Create one with Assets > Create > Vehicles >
/// Tuning Profile, then assign it to a VehicleTuningRuntime component or apply it from a garage UI.
/// </summary>
[CreateAssetMenu(fileName = "VehicleTuningProfile", menuName = "Vehicles/Tuning Profile")]
public class VehicleTuningProfile : ScriptableObject
{
    [Header("Engine and transmission")]
    [Min(100f)] public float motorTorque = 2600f;
    [Min(1f)] public float maxSpeedKph = 150f;
    [Min(1f)] public float maxReverseSpeedKph = 30f;
    [Min(1000f)] public float redlineRpm = 6500f;
    [Min(100f)] public float upshiftRpm = 5700f;
    [Min(100f)] public float downshiftRpm = 1800f;
    [Min(0.1f)] public float gearShiftTime = 0.28f;
    [Min(1f)] public float finalDriveRatio = 3.42f;
    public float[] forwardGearRatios = { 3.20f, 2.10f, 1.45f, 1.00f, 0.78f };
    [Min(0f)] public float engineBrakeTorque;

    [Header("Tyres and handling")]
    [Min(0.1f)] public float wheelRadius = 0.34f;
    [Min(0.05f)] public float suspensionDistance = 0.22f;
    [Min(1000f)] public float suspensionSpring = 35000f;
    [Min(100f)] public float suspensionDamper = 4500f;
    [Range(0.1f, 5f)] public float forwardGrip = 1.15f;
    [Range(0.1f, 5f)] public float frontSidewaysGrip = 1.5f;
    [Range(0.1f, 5f)] public float rearSidewaysGrip = 0.55f;
    [Range(5f, 55f)] public float maxSteerAngle = 45f;
    [Range(5f, 30f)] public float highSpeedSteerAngle = 16f;

    public void ApplyTo(Vehicle vehicle)
    {
        if (vehicle == null) return;
        vehicle.motorTorque = Mathf.Max(100f, motorTorque);
        vehicle.maxSpeedKph = Mathf.Max(1f, maxSpeedKph);
        vehicle.maxReverseSpeedKph = Mathf.Max(1f, maxReverseSpeedKph);
        vehicle.redlineRpm = Mathf.Max(vehicle.idleRpm + 100f, redlineRpm);
        vehicle.upshiftRpm = Mathf.Clamp(upshiftRpm, vehicle.idleRpm + 50f, vehicle.redlineRpm - 25f);
        vehicle.downshiftRpm = Mathf.Clamp(downshiftRpm, vehicle.idleRpm, vehicle.upshiftRpm - 50f);
        vehicle.gearShiftTime = Mathf.Max(0.1f, gearShiftTime);
        vehicle.finalDriveRatio = Mathf.Max(1f, finalDriveRatio);
        vehicle.forwardGearRatios = forwardGearRatios != null && forwardGearRatios.Length > 0
            ? (float[])forwardGearRatios.Clone() : new float[] { 3.2f, 2.1f, 1.45f, 1f, 0.78f };
        vehicle.engineBrakeTorque = Mathf.Max(0f, engineBrakeTorque);
        vehicle.wheelRadius = Mathf.Max(0.1f, wheelRadius);
        vehicle.suspensionDistance = Mathf.Max(0.05f, suspensionDistance);
        vehicle.suspensionSpring = Mathf.Max(1000f, suspensionSpring);
        vehicle.suspensionDamper = Mathf.Max(100f, suspensionDamper);
        vehicle.forwardGrip = Mathf.Clamp(forwardGrip, 0.1f, 5f);
        vehicle.frontSidewaysGrip = Mathf.Clamp(frontSidewaysGrip, 0.1f, 5f);
        vehicle.rearSidewaysGrip = Mathf.Clamp(rearSidewaysGrip, 0.1f, 5f);
        vehicle.maxSteerAngle = Mathf.Clamp(maxSteerAngle, 5f, 55f);
        vehicle.highSpeedSteerAngle = Mathf.Clamp(highSpeedSteerAngle, 5f, 30f);
    }
}
