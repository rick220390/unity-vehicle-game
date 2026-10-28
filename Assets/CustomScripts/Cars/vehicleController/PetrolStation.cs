using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A simple petrol-pump interaction zone.
/// Put this on a GameObject with a BoxCollider (Is Trigger enabled). A driven
/// vehicle must be inside the zone and nearly stationary; hold the interaction
/// key to refuel. Refuelling is free in this first version.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class PetrolStation : MonoBehaviour
{
    [Header("Station")]
    public string stationDisplayName = "Petrol station";
    [Tooltip("The collider on this GameObject must have Is Trigger enabled.")]
    public bool validateTriggerCollider = true;

    [Header("Refuelling")]
    public KeyCode refuelKey = KeyCode.E;
    [Tooltip("Litres added per second while the key is held.")]
    [Min(0.1f)] public float refuelLitresPerSecond = 10f;
    [Tooltip("Vehicle must be travelling at or below this speed before refuelling is allowed.")]
    [Min(0f)] public float maximumRefuelSpeedKph = 1.5f;

    [Header("Prompt")]
    public bool showOnScreenPrompt = true;
    [Min(12)] public int promptFontSize = 18;
    [Tooltip("Vertical offset from the bottom of the screen.")]
    [Min(0f)] public float promptBottomOffset = 110f;

    [Header("Debug")]
    public bool debugVehicleInZone;
    public bool debugCanRefuel;
    public float debugLastLitresAdded;

    private readonly Dictionary<Collider, Vehicle> vehiclesByCollider =
        new Dictionary<Collider, Vehicle>();
    private Vehicle activeVehicle;
    private VehicleFuel activeFuel;
    private Collider stationCollider;
    private GUIStyle promptStyle;

    private void Awake()
    {
        stationCollider = GetComponent<Collider>();

        if (validateTriggerCollider && stationCollider != null && !stationCollider.isTrigger)
        {
            Debug.LogWarning(
                name + ": PetrolStation needs a trigger collider. Enabling Is Trigger automatically.",
                this);
            stationCollider.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        TrackCollider(other);
    }

    private void OnTriggerStay(Collider other)
    {
        // Stay also acts as a recovery path if colliders are enabled while already overlapping.
        TrackCollider(other);
    }

    private void OnTriggerExit(Collider other)
    {
        vehiclesByCollider.Remove(other);
        RefreshActiveVehicle();
    }

    private void Update()
    {
        RefreshActiveVehicle();

        debugVehicleInZone = activeVehicle != null;
        debugCanRefuel = CanRefuel();

        if (!debugCanRefuel || activeFuel == null || !Input.GetKey(refuelKey))
        {
            debugLastLitresAdded = 0f;
            return;
        }

        debugLastLitresAdded = activeFuel.AddFuel(
            Mathf.Max(0.1f, refuelLitresPerSecond) * Time.deltaTime);
    }

    private void TrackCollider(Collider other)
    {
        if (other == null || other == stationCollider)
            return;

        Vehicle vehicle = null;
        if (other.attachedRigidbody != null)
            vehicle = other.attachedRigidbody.GetComponent<Vehicle>();

        if (vehicle == null)
            vehicle = other.GetComponentInParent<Vehicle>();

        if (vehicle != null)
            vehiclesByCollider[other] = vehicle;
    }

    private void RefreshActiveVehicle()
    {
        activeVehicle = null;
        activeFuel = null;

        // Several body and wheel colliders may overlap the trigger; they all map back
        // to the same Vehicle component. Select the first one with a seated driver.
        foreach (KeyValuePair<Collider, Vehicle> pair in vehiclesByCollider)
        {
            Collider overlap = pair.Key;
            Vehicle vehicle = pair.Value;

            if (overlap == null || !overlap.enabled || !overlap.gameObject.activeInHierarchy)
                continue;

            if (vehicle == null || !vehicle.gameObject.activeInHierarchy || !vehicle.HasDriver)
                continue;

            activeVehicle = vehicle;
            activeFuel = vehicle.GetComponent<VehicleFuel>();
            break;
        }
    }

    private bool CanRefuel()
    {
        if (activeVehicle == null || !activeVehicle.HasDriver || activeFuel == null)
            return false;

        return activeVehicle.Speed * 3.6f <= Mathf.Max(0f, maximumRefuelSpeedKph);
    }

    private void OnGUI()
    {
        if (!showOnScreenPrompt || activeVehicle == null || !activeVehicle.HasDriver)
            return;

        string message;
        if (activeFuel == null)
        {
            message = stationDisplayName + "\nThis vehicle has no VehicleFuel component.";
        }
        else if (activeVehicle.Speed * 3.6f > Mathf.Max(0f, maximumRefuelSpeedKph))
        {
            message = stationDisplayName + "\nStop the vehicle to refuel.";
        }
        else if (activeFuel.IsFull)
        {
            message = stationDisplayName + "\nTank full.";
        }
        else
        {
            message = stationDisplayName + "\nHold " + refuelKey +
                " to refuel  •  " +
                activeFuel.FuelRemainingLitres.ToString("0.0") + " / " +
                activeFuel.FuelCapacityLitres.ToString("0.0") + " L";
        }

        float width = Mathf.Min(560f, Screen.width - 32f);
        float height = 76f;
        Rect rect = new Rect(
            (Screen.width - width) * 0.5f,
            Screen.height - Mathf.Max(20f, promptBottomOffset) - height,
            width,
            height);

        if (promptStyle == null)
        {
            promptStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                fontSize = Mathf.Max(12, promptFontSize)
            };
        }
        else
        {
            promptStyle.fontSize = Mathf.Max(12, promptFontSize);
        }

        GUI.Box(rect, message, promptStyle);
    }

    private void OnDisable()
    {
        vehiclesByCollider.Clear();
        activeVehicle = null;
        activeFuel = null;
    }
}
