

using System.Data.SqlTypes;
using System.Runtime.Serialization;
using UnityEngine;

public class BusinessPurchaseTester : MonoBehaviour
{
    [Header("Purchase Test References")]
    [SerializeField] private BusinessManager businessManager;
    [SerializeField] private BusinessDefinition businessDefinition;
    [SerializeField] private ProductDefinition testProduct;
    [SerializeField] private int testStockAmount = 20;
    [SerializeField] private int testDeliveryAmount = 90;

    [ContextMenu("Test Business Purchase")]
    public void TestPurchase()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("Enter Play Mode Before Testing.");
            return;
        }

        if (businessManager == null || businessDefinition == null)
        {
            Debug.LogError("Missing Business Manager or Business Definition.");
            return;
        }

        BusinessInstance purchasedBusiness = businessManager.PurchaseBusiness(businessDefinition);

        if (purchasedBusiness != null)
        {
            Debug.Log("Business Purchased: " + purchasedBusiness.BusinessName);

            purchasedBusiness.AddStock(testProduct, testStockAmount);
            purchasedBusiness.AddStock(testProduct, testDeliveryAmount);
            ProductStock deliveryStock = purchasedBusiness.GetStock(testProduct);
            
            
            if (deliveryStock != null)
            {
                Debug.Log ("Stock After Delivery Attempt: " + deliveryStock.Quantity);
                Debug.Log (" Remaining Storage: " + purchasedBusiness.AvailableCapacity);
                Debug.Log("StorageState Used: " + purchasedBusiness.StoragePercentage + "%");
            }

            ProductStock stock = purchasedBusiness.GetStock(testProduct);

            if (stock != null)
            {
                Debug.Log("Product: " + stock.Product.ProductName);
                Debug.Log("Stock Quantity: " + stock.Quantity);
                purchasedBusiness.RemoveStock(testProduct, 5);
                ProductStock updatedStock = purchasedBusiness.GetStock(testProduct);
                if (updatedStock != null)
                {
                    Debug.Log("Stock After Removal " + updatedStock.Quantity);
                }
            
            }
            bool hasStock = purchasedBusiness.HasStock(testProduct, 10);
            Debug.Log("Has At Least 10 units: " + hasStock);

            bool hasEnoughStock = purchasedBusiness.HasStock(testProduct, 20);
            Debug.Log(" Has At lLeast 20 units: "+ hasEnoughStock);

            Debug.Log("Business Balance: £" + purchasedBusiness.Finances.Balance);
            Debug.Log("Total Businesses Owned: " + businessManager.OwnedBusinesses.Count);
        }
        else
        {
            Debug.LogWarning("Business Purchase Failed.");
        }
    }
}