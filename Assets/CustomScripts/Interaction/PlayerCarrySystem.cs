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

    [Header("Keg Placement")]
    [Tooltip("Maximum distance from the player to a keg placement point.")]
    public float kegPlacementDetectionRadius = 2.25f;

    private CarryableObject carriedObject;
    private Collider[] carriedColliders;
    private bool[] originalColliderStates;
    private bool originalIsKinematic;

    private IPlayerInteractable currentInteractable;

    /// <summary>Currently available interaction. UI can read this later to show prompts.</summary>
    public IPlayerInteractable CurrentInteractable => currentInteractable;

    /// <summary>Text for a future on-screen interaction prompt.</summary>
    public string CurrentInteractionPrompt =>
        currentInteractable != null && currentInteractable.CanInteract(this)
            ? currentInteractable.GetInteractionPrompt(this)
            : string.Empty;

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

        if (carriedObject != null && carryPoint != null)
        {
            carriedObject.transform.position = carryPoint.position;
            carriedObject.transform.rotation = carryPoint.rotation;
        }
    }

    private void UpdateCurrentInteractable()
    {
        currentInteractable = null;

        // For now, the first implemented interactable is an available keg placement point.
        // Later, this same target can drive prompts and highlighting for every object.
        if (CarriedPhysicalKeg == null)
            return;

        KegPlacementPoint point = FindNearbyKegPlacementPoint();
        if (point != null && point.CanInteract(this))
            currentInteractable = point;
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
                    tap.DisconnectKeg();
                    Debug.Log("Carry System: Disconnected the keg from the beer tap.");
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

    /// <summary>Called by an interactable placement point after it has secured the keg.</summary>
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
