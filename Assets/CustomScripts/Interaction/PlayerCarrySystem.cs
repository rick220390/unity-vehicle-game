using UnityEngine;

public class PlayerCarrySystem : MonoBehaviour
{
    [Header("References")]
    public Camera playerCamera;
    public Transform carryPoint;

    [Header("Interaction Settings")]
    public float interactionDistance = 2.5f;
    public KeyCode interactKey = KeyCode.E;

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
            if (TryPlaceCarriedKeg())
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

    private bool TryPlaceCarriedKeg()
    {
        if (playerCamera == null || !TryGetLookHits(out RaycastHit[] hits))
            return false;

        PhysicalKeg physicalKeg = carriedObject.GetComponent<PhysicalKeg>();

        if (physicalKeg == null)
            return false;

        foreach (RaycastHit hit in hits)
        {
            KegPlacementPoint placementPoint =
                hit.collider.GetComponentInParent<KegPlacementPoint>();

            if (placementPoint == null)
                continue;

            if (!placementPoint.TryPlaceKeg(physicalKeg))
            {
                Debug.Log("Carry System: This keg position is occupied or not configured.");
                return true;
            }

            RestoreCarriedColliders();
            carriedObject.rb.isKinematic = true;
            carriedObject.SetPlaced(true);

            Debug.Log("Carry System: Keg placed in position. Look at its beer tap and press E to connect it.");
            ClearCarriedObject();
            return true;
        }

        return false;
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