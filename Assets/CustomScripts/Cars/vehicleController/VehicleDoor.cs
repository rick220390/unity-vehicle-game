using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Optional interaction marker for a vehicle door.
/// Attach this to the door object or a dedicated interaction-point object under a Vehicle.
/// This is the foundation for door-based entry; it does not animate the door yet.
/// </summary>
[DisallowMultipleComponent]
public class VehicleDoor : MonoBehaviour
{
    public enum DoorRole
    {
        Driver,
        FrontPassenger,
        RearLeftPassenger,
        RearRightPassenger,
        Other
    }

    public static readonly List<VehicleDoor> All = new List<VehicleDoor>();

    [Header("Door Connection")]
    [Tooltip("The root vehicle this door belongs to. It is found from the parent hierarchy when possible.")]
    public Vehicle vehicle;

    [Tooltip("World-space point used to detect when the player is near this door. If empty, this object's position is used.")]
    public Transform interactionPoint;

    [Tooltip("Optional seat position used when entering through this door. If empty, Vehicle.seatPoint is used.")]
    public Transform seatPointOverride;

    [Header("Door Role")]
    public DoorRole role = DoorRole.Driver;

    /// <summary>The configured interaction point, or this object's transform.</summary>
    public Transform InteractionTransform =>
        interactionPoint != null ? interactionPoint : transform;

    /// <summary>True when this door is configured as an active driver entry point.</summary>
    public bool CanEnterAsDriver =>
        isActiveAndEnabled &&
        vehicle != null &&
        vehicle.isActiveAndEnabled &&
        !vehicle.HasDriver &&
        role == DoorRole.Driver;

    private void Reset()
    {
        FindVehicleIfMissing();
    }

    private void OnValidate()
    {
        FindVehicleIfMissing();
    }

    private void OnEnable()
    {
        FindVehicleIfMissing();

        if (!All.Contains(this))
            All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    private void FindVehicleIfMissing()
    {
        if (vehicle == null)
            vehicle = GetComponentInParent<Vehicle>();
    }

    public float DistanceToPoint(Vector3 point)
    {
        return Vector3.Distance(InteractionTransform.position, point);
    }

    /// <summary>
    /// Lets legacy vehicles keep body-based entry while a vehicle has no door
    /// interaction markers. Once doors are configured, entry is intentionally
    /// limited to their interaction points rather than anywhere around the car.
    /// </summary>
    public static bool HasAnyDoorFor(Vehicle targetVehicle)
    {
        if (targetVehicle == null)
            return false;

        foreach (VehicleDoor door in All)
        {
            if (door != null &&
                door.isActiveAndEnabled &&
                door.vehicle == targetVehicle)
                return true;
        }

        return false;
    }
}
