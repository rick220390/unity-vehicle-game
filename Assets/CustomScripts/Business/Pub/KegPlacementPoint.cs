using UnityEngine;

/// <summary>
/// A fixed position where a physical keg can be installed.
/// It implements the shared interaction contract so the player can later
/// display prompts and highlighting without keg-specific UI code.
/// </summary>
public class KegPlacementPoint : MonoBehaviour, IPlayerInteractable
{
    [Header("Keg Position")]
    [SerializeField] private BeerTap targetTap;
    [SerializeField] private Transform placementAnchor;

    private PhysicalKeg installedKeg;

    public bool IsOccupied => installedKeg != null;
    public BeerTap TargetTap => targetTap;
    public PhysicalKeg InstalledKeg => installedKeg;

    public bool CanInteract(PlayerCarrySystem player)
    {
        if (player == null || player.CarriedPhysicalKeg == null || IsOccupied || targetTap == null)
            return false;

        // A tap that already has a keg loaded cannot accept another one.
        return !targetTap.HasKeg;
    }

    public string GetInteractionPrompt(PlayerCarrySystem player)
    {
        return "E - Place keg";
    }

    public void Interact(PlayerCarrySystem player)
    {
        if (!CanInteract(player))
            return;

        player.TryPlaceCarriedKegAt(this);
    }

    public bool TryPlaceKeg(PhysicalKeg keg)
    {
        if (keg == null || keg.Drink == null || targetTap == null || IsOccupied)
        {
            Debug.LogWarning("Keg Placement: Missing keg/drink/tap, or this position is already occupied.", this);
            return false;
        }

        if (!targetTap.LoadKeg(keg.Drink))
        {
            Debug.LogWarning("Keg Placement: The beer tap rejected this keg.", this);
            return false;
        }

        Transform anchor = placementAnchor != null ? placementAnchor : transform;

        Rigidbody kegBody = keg.GetComponent<Rigidbody>();
        if (kegBody == null)
        {
            CarryableObject carryable = keg.GetComponent<CarryableObject>();
            if (carryable != null)
                kegBody = carryable.rb;
        }

        if (kegBody != null)
        {
            // A carried keg is already kinematic, so only clear velocity while physics is active.
            if (!kegBody.isKinematic)
            {
                kegBody.velocity = Vector3.zero;
                kegBody.angularVelocity = Vector3.zero;
            }

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
