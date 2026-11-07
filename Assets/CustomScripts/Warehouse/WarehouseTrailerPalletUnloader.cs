using System.Collections.Generic;
using UnityEngine;
using WSMGameStudio.HeavyMachinery;

/// <summary>
/// Releases one trailer pallet when the forklift forks are inserted underneath
/// it and the operator starts lifting. Add this component to the forklift root.
/// </summary>
[DisallowMultipleComponent]
public sealed class WarehouseTrailerPalletUnloader : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private WarehouseLorryArrival lorryArrival;
    [SerializeField] private ForkliftController forklift;

    [Header("Fork engagement detection")]
    [Tooltip("Overlap volume around the forks. Increase depth if the fork tips are long.")]
    [SerializeField] private Vector3 detectionBoxSize = new Vector3(1.6f, 0.8f, 2.2f);
    [Tooltip("Moves the detection volume forward from the fork roots.")]
    [SerializeField] private float detectionForwardOffset = 0.9f;
    [Tooltip("Minimum increase in fork lift input before a pallet is released.")]
    [Range(0.001f, 0.1f)]
    [SerializeField] private float liftInputThreshold = 0.005f;
    [SerializeField] private float liftReleaseCooldown = 0.5f;

    private float previousLiftInput;
    private float nextReleaseTime;
    private readonly Collider[] overlapResults = new Collider[64];
    private readonly HashSet<int> releasedPallets = new HashSet<int>();

    private void Awake()
    {
        if (forklift == null)
            forklift = GetComponent<ForkliftController>();

        if (lorryArrival == null)
        {
#if UNITY_2023_1_OR_NEWER
            lorryArrival = FindFirstObjectByType<WarehouseLorryArrival>();
#else
            lorryArrival = FindObjectOfType<WarehouseLorryArrival>();
#endif
        }

        if (forklift == null)
            Debug.LogError("WarehouseTrailerPalletUnloader: ForkliftController is missing.", this);

        if (lorryArrival == null)
            Debug.LogError("WarehouseTrailerPalletUnloader: WarehouseLorryArrival was not found. Assign it in the Inspector.", this);

        if (forklift != null)
            previousLiftInput = forklift.ForksVertical;
    }

    private void Update()
    {
        if (forklift == null || lorryArrival == null || !lorryArrival.HasArrived)
            return;

        // The WSM player-input component is enabled only while someone is
        // operating the forklift through ForkliftPlayerInteractor.
        ForkliftPlayerInput playerInput = GetComponent<ForkliftPlayerInput>();
        if (playerInput != null && !playerInput.enablePlayerInput)
        {
            previousLiftInput = forklift.ForksVertical;
            return;
        }

        float liftInput = forklift.ForksVertical;
        bool lifting = liftInput > previousLiftInput + liftInputThreshold;
        previousLiftInput = liftInput;

        if (!lifting || Time.time < nextReleaseTime)
            return;

        TryReleasePallet();
    }

    private void TryReleasePallet()
    {
        MovingMechanicalPart left = forklift.fork_L;
        MovingMechanicalPart right = forklift.fork_R;
        if (left == null || right == null)
            return;

        Vector3 centre = (left.transform.position + right.transform.position) * 0.5f;
        centre += transform.forward * detectionForwardOffset;

        int count = Physics.OverlapBoxNonAlloc(
            centre,
            detectionBoxSize * 0.5f,
            overlapResults,
            transform.rotation,
            ~0,
            QueryTriggerInteraction.Ignore);

        Transform trailer = FindTrailer();
        if (trailer == null)
            return;

        Rigidbody candidate = null;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Collider hit = overlapResults[i];
            if (hit == null)
                continue;

            Rigidbody body = hit.attachedRigidbody;
            if (body == null || body.isKinematic || !body.transform.IsChildOf(trailer))
                continue;

            if (releasedPallets.Contains(body.GetInstanceID()))
                continue;

            float distance = Vector3.Distance(body.worldCenterOfMass, centre);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                candidate = body;
            }
        }

        if (candidate == null)
            return;

        // Detach while preserving the exact world pose so the pallet can leave
        // the stationary trailer without inheriting its transform hierarchy.
        candidate.transform.SetParent(null, true);
        candidate.velocity = Vector3.zero;
        candidate.angularVelocity = Vector3.zero;
        candidate.useGravity = true;
        candidate.isKinematic = false;
        candidate.interpolation = RigidbodyInterpolation.Interpolate;
        candidate.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        candidate.WakeUp();

        releasedPallets.Add(candidate.GetInstanceID());
        nextReleaseTime = Time.time + liftReleaseCooldown;

        Debug.Log("WarehouseTrailerPalletUnloader: Released pallet " + candidate.name + " for forklift unloading.", candidate);
    }

    private Transform FindTrailer()
    {
        GameObject lorry = lorryArrival != null ? lorryArrival.SpawnedLorry : null;
        if (lorry == null)
            return null;

        foreach (Transform child in lorry.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "BigTruck0102Trailer")
                return child;
        }

        return null;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 centre = transform.position + transform.forward * detectionForwardOffset;
        ForkliftController controller = forklift != null ? forklift : GetComponent<ForkliftController>();
        if (controller != null && controller.fork_L != null && controller.fork_R != null)
            centre = (controller.fork_L.transform.position + controller.fork_R.transform.position) * 0.5f
                     + transform.forward * detectionForwardOffset;

        Gizmos.color = Color.cyan;
        Gizmos.matrix = Matrix4x4.TRS(centre, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, detectionBoxSize);
    }
}
