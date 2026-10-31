using UnityEngine;

/// <summary>
/// Detects a vehicle striking the on-foot player and hands the collision to
/// VehicleEjectionRagdoll. Place this beside the player's root Rigidbody.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(VehicleEjectionRagdoll))]
public class PlayerVehicleImpactRagdoll : MonoBehaviour
{
    [Header("Impact Detection")]
    [Min(0f)] public float minimumRelativeImpactSpeed = 5.5f;
    [Min(0f)] public float minimumCollisionImpulse = 35f;
    [Tooltip("Ignore repeat collision callbacks for this long after an impact.")]
    [Min(0f)] public float impactCooldown = 1.5f;
    [Tooltip("Optional layer filter. Leave Everything to detect all vehicle layers.")]
    public LayerMask vehicleLayers = ~0;

    private VehicleEjectionRagdoll ragdoll;
    private Rigidbody playerBody;
    private float nextImpactTime;

    void Awake()
    {
        ragdoll = GetComponent<VehicleEjectionRagdoll>();
        playerBody = GetComponent<Rigidbody>();
    }

    void OnCollisionEnter(Collision collision)
    {
        if (ragdoll == null || ragdoll.IsRagdolled || !ragdoll.IsConfigured)
            return;
        if (Time.time < nextImpactTime || collision == null || collision.collider == null)
            return;

        Vehicle vehicle = collision.collider.GetComponentInParent<Vehicle>();
        if (vehicle == null)
            return;

        int otherLayer = collision.collider.gameObject.layer;
        if ((vehicleLayers.value & (1 << otherLayer)) == 0)
            return;

        float impactSpeed = collision.relativeVelocity.magnitude;
        float collisionImpulse = collision.impulse.magnitude;
        if (impactSpeed < minimumRelativeImpactSpeed ||
            collisionImpulse < minimumCollisionImpulse)
            return;

        nextImpactTime = Time.time + impactCooldown;

        Vector3 vehicleVelocity = Vector3.zero;
        Rigidbody vehicleBody = collision.rigidbody;
        if (vehicleBody != null)
        {
#if UNITY_6000_0_OR_NEWER
            vehicleVelocity = vehicleBody.linearVelocity;
#else
            vehicleVelocity = vehicleBody.velocity;
#endif
        }

        Vector3 impulse = collision.impulse;
        if (impulse.sqrMagnitude < 0.01f)
        {
            Vector3 awayFromVehicle = (transform.position - vehicle.transform.position).normalized;
            impulse = awayFromVehicle * impactSpeed * Mathf.Max(1f, playerBody != null ? playerBody.mass : 1f);
        }

        Vector3 impactPoint = collision.contactCount > 0
            ? collision.GetContact(0).point
            : transform.position;

        ragdoll.RagdollFromImpact(vehicleVelocity, impulse, impactPoint);
    }
}
