using System.Collections.Generic;
using UnityEngine;
using WSMGameStudio.HeavyMachinery;

/// <summary>
/// Releases one trailer pallet when the forklift forks are inserted underneath
/// it and the operator starts lifting.
/// </summary>
[DisallowMultipleComponent]
public sealed class WarehouseTrailerPalletUnloader : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private WarehouseLorryArrival lorryArrival;
    [SerializeField] private ForkliftController forklift;

    [Header("Fork engagement detection")]
    [SerializeField] private Vector3 detectionBoxSize = new Vector3(1.6f, 0.8f, 2.2f);
    [SerializeField] private float detectionForwardOffset = 0.9f;
    [Range(0.0001f, 0.01f)]
    [Tooltip("Fork lift input changes gradually each frame, so this must be below the per-frame movement amount.")]
    [SerializeField] private float liftInputThreshold = 0.0005f;
    [SerializeField] private float liftReleaseCooldown = 0.5f;

    private float previousLiftInput;
    private float nextReleaseTime;
    private float nextStatusLogTime;
    private bool warnedInputDisabled;
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
            Debug.LogError("TrailerUnloader DIAGNOSTIC: ForkliftController is missing on this object.", this);

        if (lorryArrival == null)
            Debug.LogError("TrailerUnloader DIAGNOSTIC: WarehouseLorryArrival was not found.", this);

        if (forklift != null)
            previousLiftInput = forklift.ForksVertical;

        Debug.Log(
            "TrailerUnloader DIAGNOSTIC: Awake on '" + name +
            "'. Enabled=" + enabled +
            ", forklift=" + (forklift != null ? forklift.name : "NULL") +
            ", lorryArrival=" + (lorryArrival != null ? lorryArrival.name : "NULL") +
            ", ForksVertical=" + (forklift != null ? forklift.ForksVertical.ToString("F3") : "n/a") +
            ".", this);
    }

    private void Update()
    {
        if (forklift == null || lorryArrival == null)
            return;

        if (!lorryArrival.HasArrived)
        {
            if (Time.time >= nextStatusLogTime)
            {
                Debug.Log("TrailerUnloader DIAGNOSTIC: Waiting for lorry arrival.", this);
                nextStatusLogTime = Time.time + 3f;
            }
            return;
        }

        ForkliftPlayerInput playerInput = GetComponent<ForkliftPlayerInput>();
        if (playerInput != null && !playerInput.enablePlayerInput)
        {
            previousLiftInput = forklift.ForksVertical;
            if (!warnedInputDisabled)
            {
                Debug.Log("TrailerUnloader DIAGNOSTIC: ForkliftPlayerInput is present but enablePlayerInput is FALSE.", this);
                warnedInputDisabled = true;
            }
            return;
        }

        warnedInputDisabled = false;
        float liftInput = forklift.ForksVertical;
        bool lifting = liftInput > previousLiftInput + liftInputThreshold;

        if (Mathf.Abs(liftInput - previousLiftInput) > 0.0001f)
        {
            Debug.Log(
                "TrailerUnloader DIAGNOSTIC: ForksVertical changed from " +
                previousLiftInput.ToString("F3") + " to " + liftInput.ToString("F3") +
                "; upward trigger=" + lifting + ".", this);
        }

        previousLiftInput = liftInput;

        if (!lifting || Time.time < nextReleaseTime)
            return;

        Debug.Log("TrailerUnloader DIAGNOSTIC: Upward lift detected. Checking pallet overlap.", this);
        TryReleasePallet();
    }

    private void TryReleasePallet()
    {
        MovingMechanicalPart left = forklift.fork_L;
        MovingMechanicalPart right = forklift.fork_R;

        if (left == null || right == null)
        {
            Debug.LogError(
                "TrailerUnloader DIAGNOSTIC: Fork reference missing. Left=" +
                (left != null) + ", Right=" + (right != null) + ".", this);
            return;
        }

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
        {
            Debug.LogError(
                "TrailerUnloader DIAGNOSTIC: Could not find trailer under spawned lorry. SpawnedLorry=" +
                (lorryArrival.SpawnedLorry != null ? lorryArrival.SpawnedLorry.name : "NULL") + ".", this);
            return;
        }

        Debug.Log(
            "TrailerUnloader DIAGNOSTIC: OverlapBox found " + count +
            " collider(s). Trailer='" + GetPath(trailer) + "'. Box centre=" +
            centre.ToString("F2") + ", size=" + detectionBoxSize.ToString("F2") + ".", this);

        Rigidbody candidate = null;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Collider hit = overlapResults[i];
            if (hit == null)
                continue;

            Rigidbody hitBody = hit.attachedRigidbody;
            Transform load = FindLoadContainer(trailer);
            bool colliderInLoad = load != null && hit.transform.IsChildOf(load);
            Rigidbody body = colliderInLoad ? FindTopLevelCargoBody(hit.transform, load) : null;

            Debug.Log(
                "TrailerUnloader DIAGNOSTIC: Hit collider='" + GetPath(hit.transform) +
                "', attachedRigidbody=" + (hitBody != null ? GetPath(hitBody.transform) : "NULL") +
                ", selectedPalletBody=" + (body != null ? GetPath(body.transform) : "NULL") +
                ", colliderInLoad=" + colliderInLoad + ".", hit);

            if (body == null)
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
        {
            Debug.LogWarning(
                "TrailerUnloader DIAGNOSTIC: No unreleased trailer Rigidbody was found inside the detection box. " +
                "The overlap count above shows exactly what the box is touching.", this);
            return;
        }

        // Remove the transport joint before detaching the pallet from the load.
        FixedJoint[] cargoJoints = candidate.GetComponents<FixedJoint>();
        foreach (FixedJoint cargoJoint in cargoJoints)
        {
            if (cargoJoint == null)
                continue;

            cargoJoint.connectedBody = null;
            Destroy(cargoJoint);
        }

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

        Debug.Log("TrailerUnloader: Successfully released pallet '" + candidate.name + "'.", candidate);
    }

    // Returns the outermost Rigidbody between the hit collider and the
    // dedicated Load container, so the pallet root is selected and the
    // trailer Rigidbody can never be returned.
    private static Rigidbody FindTopLevelCargoBody(Transform hitTransform, Transform loadRoot)
    {
        Rigidbody selected = null;
        Transform current = hitTransform;

        while (current != null && current != loadRoot)
        {
            Rigidbody body = current.GetComponent<Rigidbody>();
            if (body != null)
                selected = body;

            current = current.parent;
        }

        return current == loadRoot ? selected : null;
    }

    private static Transform FindLoadContainer(Transform trailerRoot)
    {
        if (trailerRoot == null)
            return null;

        foreach (Transform child in trailerRoot.GetComponentsInChildren<Transform>(true))
            if (child.name == "Load")
                return child;

        return null;
    }

    private Transform FindTrailer()
    {
        // Tractor and trailer are now separate runtime prefabs. Use the
        // explicit trailer reference instead of searching for the old nested
        // BigTruck0102Trailer child name.
        GameObject trailer = lorryArrival != null ? lorryArrival.SpawnedTrailer : null;
        return trailer != null ? trailer.transform : null;
    }

    private static string GetPath(Transform item)
    {
        if (item == null)
            return "NULL";

        string path = item.name;
        while (item.parent != null)
        {
            item = item.parent;
            path = item.name + "/" + path;
        }
        return path;
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
