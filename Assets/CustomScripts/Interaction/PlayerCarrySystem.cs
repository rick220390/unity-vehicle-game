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

    private void Update()
    {
        if (Input.GetKeyDown(interactKey))
        {  Debug.Log("Carry System: E key detected!");
            if (carriedObject == null)
            {
                TryPickUp();
                
            }
            else
            {
                DropObject();
            }
        }
        if (carriedObject != null)
        {
            carriedObject.transform.position = carryPoint.position;
            carriedObject.transform.rotation = carryPoint.rotation;
        }
    }

private void TryPickUp()
{
    Debug.Log("Carry System: Attempting pickup.");

    if (playerCamera == null)
    {
        Debug.LogError("Carry System: Player Camera is missing!");
        return;
    }

    Ray ray = new Ray(
        playerCamera.transform.position,
        playerCamera.transform.forward
    );

    Debug.DrawRay(ray.origin, ray.direction * interactionDistance, Color.red, 3f);

    // Find everything the camera ray hits.
    RaycastHit[] hits = Physics.RaycastAll(
        ray,
        interactionDistance,
        Physics.DefaultRaycastLayers,
        QueryTriggerInteraction.Ignore
    );

    // Sort the hits so we check the closest object first.
    System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

    foreach (RaycastHit hit in hits)
    {
        // Ignore the player and all objects attached beneath the player.
        if (hit.transform == transform || hit.transform.IsChildOf(transform))
        {
            continue;
        }

        Debug.Log("Carry System: Raycast hit " + hit.collider.name);

        CarryableObject target =
            hit.collider.GetComponentInParent<CarryableObject>();

        if (target != null)
        {
            Debug.Log("Carry System: Found carryable object!");
            PickUpObject(target);
            return;
        }
    }

    Debug.LogWarning("Carry System: No carryable object found in front of the player.");
}

    private void PickUpObject(CarryableObject target)
    {
        carriedObject = target;

        Rigidbody rb = carriedObject.rb;

        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;

        carriedColliders = carriedObject.GetComponentsInChildren<Collider>();

        originalColliderStates = new bool[carriedColliders.Length];

        for (int i = 0; i < carriedColliders.Length; i++)
        {
            originalColliderStates[i] = carriedColliders[i].enabled;
            carriedColliders[i].enabled = false;
        }

           // Attach the keg to the carry point.
           carriedObject.transform.SetParent(carryPoint, false);

             // Reset its local position and rotation.
           carriedObject.transform.localPosition =
           carriedObject.carryPosition;

           carriedObject.transform.localRotation =
           Quaternion.Euler(carriedObject.carryRotation);
    }

    private void DropObject()
    {
        Transform objectTransform = carriedObject.transform;

        objectTransform.SetParent(null);

        Vector3 dropPosition =
            transform.position + transform.forward * 1.2f;

        objectTransform.position = dropPosition;

        for (int i = 0; i < carriedColliders.Length; i++)
        {
            carriedColliders[i].enabled = originalColliderStates[i];
        }

        carriedObject.rb.isKinematic = false;

        carriedObject.rb.WakeUp();

        carriedObject = null;
    }
}