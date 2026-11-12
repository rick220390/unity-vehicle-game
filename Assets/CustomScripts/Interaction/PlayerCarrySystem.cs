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
    [Tooltip("How close the player must be to a keg placement point. Placement no longer requires precise camera aiming.")]
    public float kegPlacementDetectionRadius = 2.25f;

    private CarryableObject carriedObject;
    private Collider[] carriedColliders;
    private bool[] originalColliderStates;
    private bool originalIsKinematic;

    private void Update()
    {
        if (Input.GetKeyDown(interactKey))
            HandleInteraction();

        if (carriedObject != null && carryPoint != null)
        {
            carriedObject.transform.position = carryPoint.position;
            carriedObject.transform.rotation = carryPoint.rotation;
        }
    }

    private void HandleInteraction()
    {
        if (carriedObject != null)
        {
            // Kegs are placed with a proximity check. Pressing E while carrying
            // a keg will not accidentally drop it when no placement point is nearby.
            PhysicalKeg carriedKeg = GetCarriedPhysicalKeg();
            if (carriedKeg != null)
            {
                TryPlaceCarriedKeg(carriedKeg);
                return;
            }

            if (TryPlaceCarriedKeg(null))
                return;

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

    private PhysicalKeg GetCarriedPhysicalKeg()
    {
        if (carriedObject == null)
            return null;

        PhysicalKeg keg = carriedObject.GetComponent<PhysicalKeg>();
        if (keg == null)
            keg = carriedObject.GetComponentInChildren<PhysicalKeg>();

        return keg;
    }

    private bool TryPlaceCarriedKeg(PhysicalKeg physicalKeg)
    {
        if (carriedObject == null)
            return false;

        if (physicalKeg == null)
            physicalKeg = GetCarriedPhysicalKeg();

        // Keep the old method available for any future non-keg placement types.
        if (physicalKeg == null)
            return false;

        KegPlacementPoint placementPoint = FindNearbyKegPlacementPoint();
        if (placementPoint == null)
        {
            Debug.Log("Carry System: No keg placement point nearby. Move closer to the anchor.");
            return false;
        }

        if (!placementPoint.TryPlaceKeg(physicalKeg))
        {
            Debug.Log("Carry System: Keg placement failed. The keg is still being carried.");
            return false;
        }

        RestoreCarriedColliders();

        if (carriedObject.rb != null)
            carriedObject.rb.isKinematic = true;

        carriedObject.SetPlaced(true);

        Debug.Log("Carry System: Keg placed successfully. Look at its beer tap and press E to connect it.");
        ClearCarriedObject();
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
            if (point == null || point.IsOccupied || !checkedPoints.Add(point))
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

        // Ignore colliders belonging to the player.
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