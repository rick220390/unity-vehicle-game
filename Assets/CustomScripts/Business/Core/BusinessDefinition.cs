
using UnityEngine;

/// <summary>
/// A ScriptableObject describing a type of business
/// that can exist in the game.
///
/// This is a definition/template, not an individual
/// owned business. Runtime ownership and finances
/// are handled by separate scripts.
/// </summary>
[CreateAssetMenu(
    fileName = "NewBusinessDefinition",
    menuName = "Game/Businesses/Business Definition"
)]
public sealed class BusinessDefinition : ScriptableObject
{
    public enum BusinessType
    {
        Pub,
        Shop,
        Restaurant,
        Garage,
        Other
    }

    [Header("Business Identity")]

    [Tooltip("Unique identifier, for example: small_pub")]
    [SerializeField] private string definitionId = "small_pub";

    [SerializeField] private string displayName = "Small Pub";

    [TextArea(3, 6)]
    [SerializeField] private string description;

    [SerializeField] private BusinessType businessType = BusinessType.Pub;

    [Header("Storage Settings")]
    [Tooltip("Maximum number of Units this business can store")]
    [SerializeField, Min(1)] private int storageCapacity = 100;

    [Header("Purchase Settings")]

    [Tooltip("Price the player must pay to purchase this business.")]
    [SerializeField, Min(0f)] private float purchasePrice = 20000f;

    [Tooltip("Money placed into the business account when it is first purchased.")]
    [SerializeField, Min(0f)] private float startingBusinessBalance = 500f;

    [Tooltip("Whether this business can currently be purchased.")]
    [SerializeField] private bool availableForPurchase = true;

    // Public read-only access for other game systems.

    public string DefinitionId => definitionId;
    public string DisplayName => displayName;
    public string Description => description;
    public BusinessType Type => businessType;

    public float PurchasePrice => purchasePrice;
    public float StartingBusinessBalance => startingBusinessBalance;

    public bool AvailableForPurchase => availableForPurchase;
    public int StorageCapacity  => storageCapacity;

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            definitionId = "new_business";

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = "New Business";

        purchasePrice = Mathf.Max(0f, purchasePrice);
        startingBusinessBalance = Mathf.Max(0f, startingBusinessBalance);
        storageCapacity = Mathf.Max(1, storageCapacity);
    }
}