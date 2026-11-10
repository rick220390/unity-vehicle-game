using System.Collections.Generic;
using UnityEngine;

public class BusinessManager : MonoBehaviour
{
    [Header("Business Management")]

    [SerializeField] private List<BusinessInstance> ownedBusinesses = new List<BusinessInstance>();

    public IReadOnlyList<BusinessInstance> OwnedBusinesses => ownedBusinesses;

    public BusinessInstance PurchaseBusiness(BusinessDefinition definition)
    {
        if (definition == null)
        {
            Debug.LogWarning("Cannot purchase business: Definition is missing.");
            return null;
        }
        if (!definition.AvailableForPurchase)
        {
            Debug.LogWarning("This Business is Not Available for Purchase");
            return null;
        }
       PlayerEconomy playerEconomy = PlayerEconomy.Instance;

       if (playerEconomy == null)
        {
            Debug.LogError("Connot Purchase business: Player economy is missing.");
            return null;
        }
        if (definition.PurchasePrice > 0f && !playerEconomy.TrySpendMoney(definition.PurchasePrice, "Purchased Business: " + definition.DisplayName))
        {
            Debug.LogWarning("Not enough money to purchase " + definition.DisplayName);
            return null;
        }
        BusinessInstance newBusiness = new BusinessInstance(definition);
       
        //register the new business 
        ownedBusinesses.Add(newBusiness);
        Debug.Log("Purchased Business: " + newBusiness.BusinessName);
        return newBusiness;
    }
    public  BusinessInstance GetBusinessById(string businessId)
    {
        foreach (BusinessInstance business in ownedBusinesses)
        {
            if (business.BusinessId == businessId)
            {
                return business;
            }
        }
        Debug.LogWarning("Business not found: " + businessId);
        return null;
    }
    

}