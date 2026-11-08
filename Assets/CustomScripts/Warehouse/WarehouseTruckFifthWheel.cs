using UnityEngine;

/// <summary>
/// Explicit reference to the tractor's authored fifth-wheel pivot.
/// Add this to the same root GameObject as the tractor Rigidbody and assign
/// the FithWheelPivot empty Transform in the Inspector.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class WarehouseTruckFifthWheel : MonoBehaviour
{
    [Tooltip("Drag the tractor's FithWheelPivot empty Transform here.")]
    [SerializeField] private Transform pivot;

    public Transform Pivot => pivot;

    private void Awake()
    {
        if (pivot == null)
            Debug.LogError("WarehouseTruckFifthWheel: Assign the FithWheelPivot Transform in the Inspector.", this);
        else if (pivot.name != "FithWheelPivot")
            Debug.LogWarning("WarehouseTruckFifthWheel: Assigned pivot is named '" + pivot.name + "'; expected 'FithWheelPivot'.", pivot);
    }
}
