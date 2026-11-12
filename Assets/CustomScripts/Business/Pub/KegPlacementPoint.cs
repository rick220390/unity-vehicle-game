using UnityEngine;

/// <summary>
/// A fixed position behind the bar where a physical keg can be installed.
/// Assign the beer tap supplied by this keg position in the Inspector.
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
            return false;

        if (!targetTap.LoadKeg(keg.Drink))
            return false;

        installedKeg = keg;
        keg.SetInstalled(true);

        Transform anchor = placementAnchor != null ? placementAnchor : transform;
        keg.transform.SetParent(anchor, false);
        keg.transform.localPosition = Vector3.zero;
        keg.transform.localRotation = Quaternion.identity;

        return true;
    }
}