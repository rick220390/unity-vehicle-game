using UnityEngine;

public class CarryableObject : MonoBehaviour
{
    [Header("Object Properties")]
    public float weightKg = 50f;

    [Header("Carrying Settings")]
    public Vector3 carryPosition;
    public Vector3 carryRotation;

    public Rigidbody rb;

    private void Awake()
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
        }
    }
}