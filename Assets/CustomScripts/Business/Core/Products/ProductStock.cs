using UnityEngine;
using System;


[Serializable]

public class ProductStock
{
    [Header("Product Stock")]

    [SerializeField] private ProductDefinition product;
    [SerializeField] private int quantity;

    public ProductDefinition Product => product;
    public int Quantity => quantity;
    public ProductStock(ProductDefinition product, int quantity)
    {
        this.product = product;
        this.quantity = quantity;
    }

    public void  AddStock(int amount)
    {
        if (amount <= 0)
        {
            Debug.LogWarning("ProductStock: Amount must be greater than 0");
            return;
        }
        quantity += amount;
    }

    public void RemoveStock(int amount)
    {
        if (amount <= 0)
        {
            Debug.LogWarning("ProductStock: Amount must eb greater than Zero.");
            return;
        }
        if  (quantity < amount)
        {
            Debug.LogWarning("ProduckStock: Not enough stock avalible,");
            return;

        }
        quantity -= amount;
    }
}



