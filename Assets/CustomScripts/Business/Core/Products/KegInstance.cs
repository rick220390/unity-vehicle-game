using System;
using UnityEngine;

/// <summary>
/// Represents one physical keg owned by a pub.
/// The DrinkDefinition stores the product settings;
/// this class stores the changing contents of this individual keg.
/// </summary>
[Serializable]
public class KegInstance
{
    [Header("Keg Information")]
    [SerializeField] private string kegId;
    [SerializeField] private DrinkDefinition drink;
    
    [Header("Keg Contents")]
    [SerializeField, Min(0f)] private float remainingLitres;

    [Header("Connection")]
    [SerializeField] private bool isConnected;

    public string KegId => kegId;
    public DrinkDefinition Drink => drink;
    public float RemainingLitres => remainingLitres;
    public float OriginalCapacityLitres => drink != null ? drink.KegCapacityLitres : 0f;
    public bool IsConnected => isConnected;
    public bool IsEmpty => remainingLitres <= 0.001f;

    public float RemainingPercentage
    {
        get
        {
            if (OriginalCapacityLitres <= 0f)
                return 0f;

            return remainingLitres / OriginalCapacityLitres * 100f;
        }
    }

    public KegInstance(DrinkDefinition drinkDefinition)
    {
        if (drinkDefinition == null)
        {
            Debug.LogError("KegInstance: Cannot create a keg without a drink definition.");
            return;
        }

        kegId = Guid.NewGuid().ToString();
        drink = drinkDefinition;
        remainingLitres = drinkDefinition.KegCapacityLitres;
        isConnected = false;
    }

    /// <summary>
    /// Dispenses beer from this keg.
    /// Returns true only when the requested amount was dispensed.
    /// </summary>
    public bool TryDispense(float litres)
    {
        if (litres <= 0f)
        {
            Debug.LogWarning("KegInstance: Dispense amount must be greater than zero.");
            return false;
        }

        if (!isConnected)
        {
            Debug.LogWarning("KegInstance: This keg is not connected to a beer line.");
            return false;
        }

        if (IsEmpty || remainingLitres < litres)
        {
            Debug.LogWarning("KegInstance: Not enough beer remains in this keg.");
            return false;
        }

        remainingLitres -= litres;

        if (remainingLitres < 0.001f)
            remainingLitres = 0f;

        return true;
    }

    public bool Connect()
    {
        if (drink == null || IsEmpty)
        {
            Debug.LogWarning("KegInstance: An empty or invalid keg cannot be connected.");
            return false;
        }

        if (isConnected)
            return false;

        isConnected = true;
        return true;
    }

    public void Disconnect()
    {
        isConnected = false;
    }
}
