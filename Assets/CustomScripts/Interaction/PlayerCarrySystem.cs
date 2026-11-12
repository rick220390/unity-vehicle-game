using System.Collections.Generic;
using UnityEngine;

public class PlayerCarrySystem : MonoBehaviour
{
    [Header("References")]
    public Camera playerCamera;
    public Transform carryPoint;

    [Header("Interaction Settings")]
    public float interactionDistance = 2.5f;
    public KeyCode interactKey = KeyCode.E;
    public KeyCode disconnectKey = KeyCode.R;

    [Header("Keg Placement")]
    [Tooltip("Maximum distance from the player to a keg placement point.")]
    public float kegPlacementDetectionRadius = 2.25f;

    [Header("Test Interaction UI")]
    [Tooltip("Show a simple on-screen prompt while testing interactions.")]
    public bool showTestInteractionPrompt = true;

    private CarryableObject carriedObject;
    private Collider[] carriedColliders;
    private bool[] originalColliderStates;
    private bool originalIsKinematic;

    private IPlayerInteractable currentInteractable;
    private string currentInteractionPrompt = string.Empty;
    private string currentKegInfo = string.Empty;

    public IPlayerInteractable CurrentInteractable => currentInteractable;
    public string CurrentInteractionPrompt => currentInteractionPrompt;

    public PhysicalKeg CarriedPhysicalKeg
    {
        get
        {
            if (carriedObject == null)
                return null;

            PhysicalKeg keg = carriedObject.GetComponent<PhysicalKeg>();
            return keg != null ? keg : carriedObject.GetComponentInChildren<PhysicalKeg>();
        }
    }

    private void Update()
    {
        UpdateCurrentInteractable();

        if (Input.GetKeyDown(interactKey))
            HandleInteraction();

        if (Input.GetKeyDown(disconnectKey))
            HandleDisconnectInteraction();

        if (carriedObject != null && carryPoint != null)
        {
            carriedObject.transform.position = carryPoint.position;
            carriedObject.transform.rotation = carryPoint.rotation;
        }
    }

    private void OnGUI()
    {
        if (!showTestInteractionPrompt || (string.IsNullOrEmpty(currentInteractionPrompt) && string.IsNullOrEmpty(currentKegInfo)))
            return;

        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.fontSize = 18;
        style.alignment = TextAnchor.MiddleCenter;
        style.padding = new RectOffset(16, 16, 8, 8);

        string displayText = currentInteractionPrompt;
        if (!string.IsNullOrEmpty(currentKegInfo))
            displayText = string.IsNullOrEmpty(displayText)
                ? currentKegInfo
                : currentKegInfo + "\n" + displayText;

        float width = Mathf.Max(260f, style.CalcSize(new GUIContent(displayText)).x + 32f);
        float height = string.IsNullOrEmpty(currentKegInfo) ? 42f : 86f;
        Rect promptRect = new Rect(
            (Screen.width - width) * 0.5f,
            Screen.height * 0.68f,
            width,
            height
        );

        GUI.Box(promptRect, displayText, style);
    }

    private void UpdateCurrentInteractable()
    {
        currentInteractable = null;
        currentInteractionPrompt = string.Empty;
        currentKegInfo = string.Empty;

        if (carriedObject != null)
        {
            if (CarriedPhysicalKeg != null)
            {
                KegPlacementPoint point = FindNearbyKegPlacementPoint();
                if (point != null && point.CanInteract(this))
                {
                    currentInteractable = point;
                    currentInteractionPrompt = point.GetInteractionPrompt(this);
                }
            }
            else
            {
                currentInteractionPrompt = "E - Drop " + carriedObject.name;
            }

            return;
        }

        if (!TryGetLookHits(out RaycastHit[] hits))
            return;

        foreach (RaycastHit hit in hits)
        {
            PhysicalKeg physicalKeg = hit.collider.GetComponentInParent<PhysicalKeg>();
            if (physicalKeg != null && physicalKeg.IsInstalled)
            {
                KegPlacementPoint placementPoint = physicalKeg.GetComponentInParent<KegPlacementPoint>();
                if (placementPoint != null && placementPoint.TargetTap != null &&
                    placementPoint.TargetTap.HasKeg)
                {
                    KegInstance keg = placementPoint.TargetTap.ConnectedKeg;
                    DrinkDefinition drink = keg.Drink;
                    if (drink != null)
                    {
                        float remainingPints = drink.PintSizeLitres > 0f
                            ? keg.RemainingLitres / drink.PintSizeLitres
                            : 0f;

                        currentKegInfo =
                            drink.ProductName + " Keg\n" +
                            $"{keg.RemainingLitres:0.00} L remaining / {keg.OriginalCapacityLitres:0.00} L\n" +
                            $"Approximately {remainingPints:0} pints left ({keg.RemainingPercentage:0}%)";
                    }
                }
            }

            CarryableObject target = hit.collider.GetComponentInParent<CarryableObject>();
            if (target != null && !target.IsPlaced)
            {
                currentInteractionPrompt = "E - Pick up " + target.name;
                return;
            }

            BeerTap tap = hit.collider.GetComponentInParent<BeerTap>();
            if (tap != null && tap.HasKeg)
            {
                currentInteractionPrompt = tap.ConnectedKeg.IsConnected
                    ? "E - Pour pint    |    R - Disconnect " + tap.TapName
                    : "E - Connect " + tap.TapName;
                return;
            }
        }
    }

    private void HandleInteraction()
    {
        if (carriedObject != null)
        {
            if (CarriedPhysicalKeg != null)
            {
                if (currentInteractable != null && currentInteractable.CanInteract(this))
                    currentInteractable.Interact(this);
                else
                    Debug.Log("Carry System: No valid keg placement point nearby. Move closer to an available position.");
                return;
            }

            DropObject();
            return;
        }

        if (playerCamera == null)
        {
            Debug.LogError("Carry System: Player Camera is missing!");
            return;
        }

        if (!TryGetLookHits(out RaycastHit[] hits))
            return;

        foreach (RaycastHit hit in hits)
        {
            CarryableObject target = hit.collider.GetComponentInParent<CarryableObject>();

            if (target != null && !target.IsPlaced)
            {
                PickUpObject(target);
                return;
            }

            BeerTap tap = hit.collider.GetComponentInParent<BeerTap>();

            if (tap != null && tap.HasKeg)
            {
                if (tap.ConnectedKeg.IsConnected)
                {
                    if (tap.TryPourPint(out float salePrice))
                        Debug.Log($"Carry System: Poured a pint. Sale recorded: {salePrice:0.00}.");
                    else
                        Debug.Log("Carry System: Could not pour a pint. Check that the keg is connected and contains beer.");
                }
                else if (tap.ConnectKeg())
                {
                    Debug.Log("Carry System: Connected the keg to the beer tap.");
                }

                return;
            }
        }

        Debug.Log("Carry System: Nothing interactable found.");
    }

    private void HandleDisconnectInteraction()
    {
        if (carriedObject != null || !TryGetLookHits(out RaycastHit[] hits))
            return;

        foreach (RaycastHit hit in hits)
        {
            BeerTap tap = hit.collider.GetComponentInParent<BeerTap>();
            if (tap == null || !tap.HasKeg)
                continue;

            if (tap.ConnectedKeg.IsConnected)
            {
                tap.DisconnectKeg();
                Debug.Log("Carry System: Disconnected the keg from the beer tap. The keg remains installed in its holder.");
            }
            else
            {
                Debug.Log("Carry System: This keg is already disconnected.");
            }

            return;
        }
    }

    public bool TryPlaceCarriedKegAt(KegPlacementPoint placementPoint)
    {
        PhysicalKeg keg = CarriedPhysicalKeg;
        if (carriedObject == null || keg == null || placementPoint == null ||
            !placementPoint.CanInteract(this))
            return false;

        if (!placementPoint.TryPlaceKeg(keg))
            return false;

        RestoreCarriedColliders();

        if (carriedObject.rb != null)
            carriedObject.rb.isKinematic = true;

        carriedObject.SetPlaced(true);
        Debug.Log("Carry System: Keg placed successfully. Look at its beer tap and press E to connect it.");
        ClearCarriedObject();
        currentInteractable = null;
        currentInteractionPrompt = string.Empty;
        return true;
    }

    private KegPlacementPoint FindNearbyKegPlacementPoint()
    {
        Vector3 searchCentre = transform.position + Vector3.up * 0.8f;
        Collider[] nearbyColliders = Physics.OverlapSphere(
            searchCentre,
            kegPlacementDetectionRadius,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Collide
        );

        KegPlacementPoint closestPoint = null;
        float closestDistance = float.MaxValue;
        HashSet<KegPlacementPoint> checkedPoints = new HashSet<KegPlacementPoint>();

        foreach (Collider nearbyCollider in nearbyColliders)
        {
            if (nearbyCollider == null ||
                nearbyCollider.transform == transform ||
                nearbyCollider.transform.IsChildOf(transform))
                continue;

            KegPlacementPoint point = nearbyCollider.GetComponentInParent<KegPlacementPoint>();
            if (point == null || !checkedPoints.Add(point))
                continue;

            float distance = Vector3.Distance(searchCentre, nearbyCollider.ClosestPoint(searchCentre));
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestPoint = point;
            }
        }

        return closestPoint;
    }

    private bool TryGetLookHits(out RaycastHit[] hits)
    {
        hits = null;

        if (playerCamera == null)
            return false;

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        Debug.DrawRay(ray.origin, ray.direction * interactionDistance, Color.red, 1f);

        hits = Physics.RaycastAll(
            ray,
            interactionDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore
        );

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        int validCount = 0;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].transform == transform || hits[i].transform.IsChildOf(transform))
                continue;

            hits[validCount++] = hits[i];
        }

        if (validCount != hits.Length)
            System.Array.Resize(ref hits, validCount);

        return hits.Length > 0;
    }

    private void PickUpObject(CarryableObject target)
    {
        if (target.rb == null || carryPoint == null)
        {
            Debug.LogError("Carry System: Carryable object needs a Rigidbody and the player needs a Carry Point.");
            return;
        }

        carriedObject = target;
        originalIsKinematic = carriedObject.rb.isKinematic;

        carriedObject.rb.velocity = Vector3.zero;
        carriedObject.rb.angularVelocity = Vector3.zero;
        carriedObject.rb.isKinematic = true;

        carriedColliders = carriedObject.GetComponentsInChildren<Collider>();
        originalColliderStates = new bool[carriedColliders.Length];

        for (int i = 0; i < carriedColliders.Length; i++)
        {
            originalColliderStates[i] = carriedColliders[i].enabled;
            carriedColliders[i].enabled = false;
        }

        carriedObject.transform.SetParent(carryPoint, false);
        carriedObject.transform.localPosition = carriedObject.carryPosition;
        carriedObject.transform.localRotation = Quaternion.Euler(carriedObject.carryRotation);

        Debug.Log("Carry System: Picked up " + carriedObject.name + ".");
    }

    private void DropObject()
    {
        if (carriedObject == null)
            return;

        Transform objectTransform = carriedObject.transform;
        objectTransform.SetParent(null);
        objectTransform.position = transform.position + transform.forward * 1.2f;

        RestoreCarriedColliders();

        carriedObject.rb.isKinematic = originalIsKinematic;
        carriedObject.rb.WakeUp();

        Debug.Log("Carry System: Dropped " + carriedObject.name + ".");
        ClearCarriedObject();
    }

    private void RestoreCarriedColliders()
    {
        if (carriedColliders == null || originalColliderStates == null)
            return;

        for (int i = 0; i < carriedColliders.Length; i++)
        {
            if (carriedColliders[i] != null)
                carriedColliders[i].enabled = originalColliderStates[i];
        }
    }

    private void ClearCarriedObject()
    {
        carriedObject = null;
        carriedColliders = null;
        originalColliderStates = null;
    }
}