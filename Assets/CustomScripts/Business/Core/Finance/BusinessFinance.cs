using System;
using UnityEngine;

[Serializable]
public class BusinessFinance
{
    [Header("Business Account")]

    [SerializeField] private float balance;

    [SerializeField] private float totalIncome;

    [SerializeField] private float totalExpenses;

    public float Balance => balance;

    public float TotalIncome => totalIncome;
    public float TotalExpenses => totalExpenses;

    public BusinessFinance(float startingBalance)
    {
        balance = Mathf.Max(0f, startingBalance);
        totalIncome = 0f;
        totalExpenses = 0f;
    }
    

    public bool Deposit(float amount)
    {
        if (amount <= 0f)
        {
            Debug.LogWarning("Cannot deposit an invaild amount");
            return false;
        
        }
        balance += amount;
        totalIncome += amount;

        return true;
    }
    public bool Withdraw(float amount)
    {
        if (amount <= 0f)
        {
            Debug.LogWarning("cannot withdraw an invaild amount");
            return false;
        }
        if (amount > balance)
        {
            Debug.LogWarning("Insuficient Business Funds");
            return false;
            
             
        }
        balance -= amount;

        totalExpenses += amount;
        return true;
    }
}
