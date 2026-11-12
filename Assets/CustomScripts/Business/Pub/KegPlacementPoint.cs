using UnityEngine;

/// <summary>
/// A fixed position behind the bar where a physical keg can be installed.
/// The placement point registers the keg with its beer tap and secures its physics.
/// </summary>
public class KegPlacementPoint : MonoBehaviour
{
    [Header("Keg Position")]
    [SerializeField] private BeerTap targetTap;
    [SerializeField] private Transform placementAnchor;

    private PhysicalKeg installedKeg;

    public bool IsOccupied => installedKeg != null;
    public BeerTap TargetTap => targetTap;

    public bool TryPlaceKeg(PhysicalKeg keg)
    {
        if (keg == null || keg.Drink == null || targetTap == null || IsOccupied)
        {
            Debug.LogWarning("Keg Placement: Missing keg/drink/tap, or this position is already occupied.", this);
            return false;
        }

        // Register the drink before changing the physical keg's state.
        if (!targetTap.LoadKeg(keg.Drink))
        {
            Debug.LogWarning("Keg Placement: The beer tap rejected this keg.", this);
            return false;
        }

        Transform anchor = placementAnchor != null ? placementAnchor : transform;

        // Stop physics before parenting and snapping the keg into its final position.
        Rigidbody kegBody = keg.GetComponent<Rigidbody>();
        if (kegBody == null)
        {
            CarryableObject carryable = keg.GetComponent<CarryableObject>();
            if (carryable != null)
                kegBody = carryable.rb;
        }

        if (kegBody != null)
        {
            kegBody.velocity = Vector3.zero;
            kegBody.angularVelocity = Vector3.zero;
            kegBody.isKinematic = true;
        }

        keg.transform.SetParent(anchor, false);
        keg.transform.localPosition = Vector3.zero;
        keg.transform.localRotation = Quaternion.identity;

        keg.SetInstalled(true);
        installedKeg = keg;

        if (kegBody != null)
            kegBody.Sleep();

        Physics.SyncTransforms();

        Debug.Log("Keg Placement: " + keg.name + " successfully installed at " + name + ".", this);
        return true;
    }
}