using UnityEngine;

/// <summary>
/// Add to the same Player root as PlayerVehicleInteractor.
/// While driving a Vehicle, press E near a compatible trailer to couple or
/// uncouple it. The trailer root must have WarehousePlayerTrailerCoupling.
/// </summary>
[DisallowMultipleComponent]
public sealed class WarehousePlayerTrailerInteraction : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private KeyCode couplingKey = KeyCode.E;

    [Header("Detection")]
    [SerializeField, Min(0.5f)] private float trailerDetectionRadius = 3.5f;
    [SerializeField] private string fifthWheelPivotName = "FithWheelPivot";
    [SerializeField] private LayerMask detectionMask = ~0;

    private WarehousePlayerTrailerCoupling nearbyTrailer;
    private WarehousePlayerTrailerCoupling attachedTrailer;
    private Vehicle drivenVehicle;
    private string promptMessage;
    private float promptTimer;

    private readonly Collider[] overlapResults = new Collider[64];

    private void Update()
    {
        drivenVehicle = FindDrivenVehicle();

        if (drivenVehicle == null)
        {
            nearbyTrailer = null;
            attachedTrailer = null;
            return;
        }

        if (attachedTrailer != null && !attachedTrailer.IsCoupled)
            attachedTrailer = null;

        nearbyTrailer = attachedTrailer != null
            ? attachedTrailer
            : FindNearbyTrailer();

        if (Input.GetKeyDown(couplingKey))
            HandleCouplingInput();

        if (promptTimer > 0f)
            promptTimer -= Time.deltaTime;
    }

    private void HandleCouplingInput()
    {
        if (drivenVehicle == null)
            return;

        Rigidbody tractorBody = drivenVehicle.GetComponent<Rigidbody>();
        Transform fifthWheelPivot = FindChild(drivenVehicle.transform, fifthWheelPivotName);

        if (tractorBody == null || fifthWheelPivot == null)
        {
            Say("This vehicle has no Rigidbody or " + fifthWheelPivotName + ".");
            return;
        }

        if (attachedTrailer != null && attachedTrailer.IsCoupled)
        {
            if (attachedTrailer.TryUncouple(out string uncoupleReason))
            {
                Say(uncoupleReason);
                attachedTrailer = null;
            }
            else
            {
                Say(uncoupleReason);
            }
            return;
        }

        if (nearbyTrailer == null)
        {
            Say("No trailer close enough to couple.");
            return;
        }

        if (nearbyTrailer.TryCouple(tractorBody, fifthWheelPivot, out string coupleReason))
        {
            attachedTrailer = nearbyTrailer;
            Say(coupleReason);
        }
        else
        {
            Say(coupleReason);
        }
    }

    private WarehousePlayerTrailerCoupling FindNearbyTrailer()
    {
        Vector3 origin = transform.position;
        int count = Physics.OverlapSphereNonAlloc(
            origin, trailerDetectionRadius, overlapResults, detectionMask,
            QueryTriggerInteraction.Ignore);

        WarehousePlayerTrailerCoupling best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Collider hit = overlapResults[i];
            if (hit == null) continue;

            WarehousePlayerTrailerCoupling candidate =
                hit.GetComponentInParent<WarehousePlayerTrailerCoupling>();

            if (candidate == null || candidate.IsCoupled)
                continue;

            float distance = Vector3.Distance(origin, candidate.transform.position);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static Vehicle FindDrivenVehicle()
    {
        for (int i = 0; i < Vehicle.All.Count; i++)
        {
            Vehicle candidate = Vehicle.All[i];
            if (candidate != null && candidate.isActiveAndEnabled && candidate.HasDriver)
                return candidate;
        }

        return null;
    }

    private static Transform FindChild(Transform root, string exactName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == exactName)
                return child;

        return null;
    }

    private void Say(string text)
    {
        promptMessage = text;
        promptTimer = 2.5f;
        Debug.Log("WarehousePlayerTrailerInteraction: " + text, this);
    }

    private void OnGUI()
    {
        string text = null;

        if (promptTimer > 0f)
            text = promptMessage;
        else if (drivenVehicle != null && attachedTrailer != null && attachedTrailer.IsCoupled)
            text = "[" + couplingKey + "] Uncouple trailer";
        else if (drivenVehicle != null && nearbyTrailer != null)
            text = "[" + couplingKey + "] Couple trailer";

        if (string.IsNullOrEmpty(text))
            return;

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 20,
            fontStyle = FontStyle.Bold
        };
        style.normal.textColor = Color.white;
        GUI.Label(new Rect(0, Screen.height - 165, Screen.width, 36), text, style);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, trailerDetectionRadius);
    }
}
