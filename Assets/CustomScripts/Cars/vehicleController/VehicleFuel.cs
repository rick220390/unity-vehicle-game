using System;
using UnityEngine;

/// <summary>
/// Fuel tank and consumption for a drivable Vehicle.
/// Attach this beside Vehicle on the vehicle root. PetrolStation can add fuel
/// through AddFuel without directly changing drivetrain or engine-audio logic.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Vehicle))]
[DefaultExecutionOrder(100)]
public class VehicleFuel : MonoBehaviour
{
    [Header("Fuel tank")]
    [Min(1f)] public float tankCapacityLitres = 60f;
    [Tooltip("When enabled, this overrides Current Fuel Litres and fills the tank at scene start. Disable it to use Current Fuel Litres as the starting amount.")]
    public bool startWithFullTank = false;
    [Min(0f)] public float currentFuelLitres = 60f;

    [Header("Fuel consumption")]
    [Tooltip("Base consumption at a steady 100 km/h, before the throttle-load factor.")]
    [Min(0.1f)] public float litresPer100Km = 10f;
    [Tooltip("Fuel used per hour while the engine is idling or the vehicle is stationary.")]
    [Min(0f)] public float idleLitresPerHour = 0.8f;
    [Tooltip("Additional litres per hour at full throttle while stationary (for example, revving the engine).")]
    [Min(0f)] public float fullThrottleStationaryLitresPerHour = 24f;
    [Tooltip("Consumption multiplier at a steady speed when throttle is zero, such as coasting.")]
    [Range(0f, 2f)] public float coastingConsumptionFactor = 0.6f;
    [Tooltip("Additional consumption factor at full throttle while moving.")]
    [Range(0f, 2f)] public float fullThrottleConsumptionFactor = 0.8f;

    [Header("Engine behaviour")]
    [Tooltip("At this remaining amount or less, the fuel tank is treated as empty.")]
    [Min(0f)] public float emptyTankThresholdLitres = 0.001f;

    [Header("Debug")]
    public float debugLastConsumptionLitresPerHour;
    public bool debugEngineAllowedToRun;

    public Vehicle Vehicle => vehicle;
    public float FuelCapacityLitres => Mathf.Max(1f, tankCapacityLitres);
    public float FuelRemainingLitres => Mathf.Clamp(currentFuelLitres, 0f, FuelCapacityLitres);
    public float FuelRemaining01 => FuelCapacityLitres > 0f
        ? Mathf.Clamp01(FuelRemainingLitres / FuelCapacityLitres)
        : 0f;
    public bool HasFuel => FuelRemainingLitres > Mathf.Max(0f, emptyTankThresholdLitres);
    public bool IsFull => FuelRemainingLitres >= FuelCapacityLitres - 0.01f;

    /// <summary>Invoked after fuel level changes. Arguments: litres remaining, capacity.</summary>
    public event Action<float, float> FuelChanged;
    public event Action FuelDepleted;
    public event Action FuelRestored;

    private Vehicle vehicle;
    private bool wasOutOfFuel;

    private void Awake()
    {
        vehicle = GetComponent<Vehicle>();
        tankCapacityLitres = Mathf.Max(1f, tankCapacityLitres);

        if (startWithFullTank)
            currentFuelLitres = tankCapacityLitres;
        else
            currentFuelLitres = Mathf.Clamp(currentFuelLitres, 0f, tankCapacityLitres);

        wasOutOfFuel = !HasFuel;
        ApplyEngineAvailability();
    }

    private void OnValidate()
    {
        tankCapacityLitres = Mathf.Max(1f, tankCapacityLitres);
        currentFuelLitres = Mathf.Clamp(currentFuelLitres, 0f, tankCapacityLitres);
        litresPer100Km = Mathf.Max(0.1f, litresPer100Km);
        idleLitresPerHour = Mathf.Max(0f, idleLitresPerHour);
        fullThrottleStationaryLitresPerHour = Mathf.Max(0f, fullThrottleStationaryLitresPerHour);
        emptyTankThresholdLitres = Mathf.Max(0f, emptyTankThresholdLitres);
    }

    private void FixedUpdate()
    {
        if (vehicle == null)
            return;

        if (HasFuel && vehicle.HasDriver)
            ConsumeFuel(Time.fixedDeltaTime);

        ApplyEngineAvailability();
    }

    private void ConsumeFuel(float deltaTime)
    {
        float speedKph = vehicle.Speed * 3.6f;
        float throttle = vehicle.HasDriver ? Mathf.Abs(vehicle.Throttle) : 0f;
        float litresPerHour;

        if (speedKph < 2f)
        {
            // At a standstill, throttle represents engine revving rather than distance travelled.
            litresPerHour = idleLitresPerHour +
                throttle * fullThrottleStationaryLitresPerHour;
        }
        else
        {
            // L/100 km multiplied by speed yields a litres-per-hour rate.
            float movingLoad = Mathf.Max(0f, coastingConsumptionFactor) +
                throttle * Mathf.Max(0f, fullThrottleConsumptionFactor);
            litresPerHour = (speedKph * litresPer100Km / 100f) * movingLoad;
        }

        debugLastConsumptionLitresPerHour = Mathf.Max(0f, litresPerHour);
        float amount = debugLastConsumptionLitresPerHour * deltaTime / 3600f;
        if (amount <= 0f)
            return;

        float previous = currentFuelLitres;
        currentFuelLitres = Mathf.Max(0f, currentFuelLitres - amount);

        if (!Mathf.Approximately(previous, currentFuelLitres))
            FuelChanged?.Invoke(FuelRemainingLitres, FuelCapacityLitres);

        if (previous > emptyTankThresholdLitres &&
            currentFuelLitres <= emptyTankThresholdLitres)
        {
            currentFuelLitres = 0f;
            wasOutOfFuel = true;
            FuelDepleted?.Invoke();
            FuelChanged?.Invoke(0f, FuelCapacityLitres);
        }
    }

    private void ApplyEngineAvailability()
    {
        if (vehicle == null)
            return;

        bool engineAllowed = HasFuel;
        vehicle.SetExternalEngineState(engineAllowed, engineAllowed ? 1f : 0f);
        debugEngineAllowedToRun = engineAllowed;
    }

    /// <summary>
    /// Adds fuel and returns the amount actually accepted by the tank.
    /// Stations and future UI should use this method instead of writing the field.
    /// </summary>
    public float AddFuel(float litres)
    {
        if (litres <= 0f)
            return 0f;

        float previous = FuelRemainingLitres;
        currentFuelLitres = Mathf.Clamp(previous + litres, 0f, FuelCapacityLitres);
        float added = currentFuelLitres - previous;

        if (added > 0f)
        {
            if (!HasFuel)
                currentFuelLitres = Mathf.Max(currentFuelLitres, emptyTankThresholdLitres + 0.01f);

            bool fuelRestoredNow = wasOutOfFuel && HasFuel;
            wasOutOfFuel = !HasFuel;
            ApplyEngineAvailability();

            FuelChanged?.Invoke(FuelRemainingLitres, FuelCapacityLitres);
            if (fuelRestoredNow)
                FuelRestored?.Invoke();
        }

        return added;
    }

    /// <summary>Fills the tank completely. Returns the litres added.</summary>
    public float FillTank()
    {
        return AddFuel(FuelCapacityLitres - FuelRemainingLitres);
    }

    /// <summary>Called by save/load or a future garage UI to set an exact fuel amount.</summary>
    public void SetFuelLevel(float litres)
    {
        bool previouslyHadFuel = HasFuel;
        currentFuelLitres = Mathf.Clamp(litres, 0f, FuelCapacityLitres);
        bool nowHasFuel = HasFuel;
        wasOutOfFuel = !nowHasFuel;
        ApplyEngineAvailability();
        FuelChanged?.Invoke(FuelRemainingLitres, FuelCapacityLitres);

        if (previouslyHadFuel && !nowHasFuel)
            FuelDepleted?.Invoke();
        else if (!previouslyHadFuel && nowHasFuel)
            FuelRestored?.Invoke();
    }
}
