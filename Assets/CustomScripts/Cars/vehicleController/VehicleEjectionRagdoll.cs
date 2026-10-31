using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Optional player-side ragdoll for high-speed vehicle ejections.
/// Assign every ragdoll bone Rigidbody and Collider (including the pelvis).
/// The bones should already be connected with CharacterJoints/ConfigurableJoints.
/// </summary>
[DisallowMultipleComponent]
public class VehicleEjectionRagdoll : MonoBehaviour
{
    [Header("Ragdoll Setup")]
    [Tooltip("All bone rigidbodies in the ragdoll hierarchy, including the pelvis.")]
    public Rigidbody[] ragdollBodies = new Rigidbody[0];
    [Tooltip("All ragdoll bone colliders. Do not include the player root capsule.")]
    public Collider[] ragdollColliders = new Collider[0];
    [Tooltip("Pelvis/hips transform used to place the player root when recovering")]
    public Transform pelvis;
    [Tooltip("Animator on the character. If empty, searches this hierarchy.")]
    public Animator characterAnimator;
    [Tooltip("Optional Invector input/controller/motor components to disable during ragdoll. If empty, standard Invector components are found automatically.")]
    public MonoBehaviour[] disableDuringRagdoll;

    [Header("Ejection")]
    [Min(0f)] public float outwardImpulse = 5f;
    [Min(0f)] public float upwardImpulse = 2.2f;
    [Min(0f)] public float tumbleImpulse = 3f;
    [Tooltip("Small random variation added to the tumble for less repeated-looking falls.")]
    [Min(0f)] public float tumbleVariation = 1.5f;
    [Min(0f)] public float recoveryDelay = 6f;
    public string recoveryAnimatorState = "Locomotion";

    private Rigidbody rootBody;
    private Collider[] rootColliders;
    private readonly List<BehaviourState> savedBehaviours = new List<BehaviourState>();
    private readonly List<bool> savedBodyKinematic = new List<bool>();
    private readonly List<bool> savedColliderEnabled = new List<bool>();
    private bool[] savedRootColliderEnabled;
    private bool savedAnimatorEnabled;
    private Vector3 pelvisRootLocalPosition;
    private bool savedRootBodyKinematic;
    private bool ragdolled;
    private Coroutine recoveryRoutine;

    public bool IsConfigured => ragdollBodies != null && ragdollBodies.Length > 0 &&
        ragdollColliders != null && ragdollColliders.Length > 0;

    private struct BehaviourState
    {
        public MonoBehaviour behaviour;
        public bool enabled;
        public BehaviourState(MonoBehaviour behaviour)
        {
            this.behaviour = behaviour;
            enabled = behaviour != null && behaviour.enabled;
        }
    }

    void Awake()
    {
        rootBody = GetComponent<Rigidbody>();
        rootColliders = GetComponents<Collider>();
        if (characterAnimator == null)
            characterAnimator = GetComponentInChildren<Animator>(true);

        if (ragdollBodies == null) ragdollBodies = new Rigidbody[0];
        if (ragdollColliders == null) ragdollColliders = new Collider[0];
        if (pelvis != null)
            pelvisRootLocalPosition = transform.InverseTransformPoint(pelvis.position);

        CacheOriginalStates();
        SetRagdollActive(false);
    }

    void CacheOriginalStates()
    {
        savedBehaviours.Clear();
        savedBodyKinematic.Clear();
        savedColliderEnabled.Clear();

        foreach (Rigidbody body in ragdollBodies)
            savedBodyKinematic.Add(body != null && body.isKinematic);
        foreach (Collider collider in ragdollColliders)
            savedColliderEnabled.Add(collider != null && collider.enabled);

        savedRootColliderEnabled = new bool[rootColliders.Length];
        for (int i = 0; i < rootColliders.Length; i++)
            savedRootColliderEnabled[i] = rootColliders[i] != null && rootColliders[i].enabled;

        savedRootBodyKinematic = rootBody != null && rootBody.isKinematic;
        savedAnimatorEnabled = characterAnimator != null && characterAnimator.enabled;
    }

    /// <summary>Called by PlayerVehicleInteractor after the door has opened.</summary>
    public void Eject(Vector3 vehicleVelocity, Vector3 exitPosition, Quaternion exitRotation, Vector3 outwardDirection)
    {
        if (ragdolled || ragdollBodies == null || ragdollBodies.Length == 0)
        {
            Debug.LogWarning("VehicleEjectionRagdoll: Assign the ragdoll bone rigidbodies before enabling ejection.", this);
            return;
        }

        if (recoveryRoutine != null)
            StopCoroutine(recoveryRoutine);

        transform.SetPositionAndRotation(exitPosition, exitRotation);
        transform.SetParent(null, true);

        if (characterAnimator != null)
            characterAnimator.enabled = false;

        DisableMovementBehaviours();

        // The root capsule/body must not fight the bone-level ragdoll physics.
        foreach (Collider collider in rootColliders)
            if (collider != null)
                collider.enabled = false;

        if (rootBody != null)
        {
            rootBody.isKinematic = true;
            SetBodyVelocity(rootBody, Vector3.zero);
            rootBody.angularVelocity = Vector3.zero;
        }

        SetRagdollActive(true);

        Vector3 direction = Vector3.ProjectOnPlane(outwardDirection, Vector3.up).normalized;
        if (direction.sqrMagnitude < 0.001f)
            direction = transform.forward;

        Rigidbody pelvisBody = FindPelvisBody();
        foreach (Rigidbody body in ragdollBodies)
        {
            if (body == null)
                continue;

            SetBodyVelocity(body, vehicleVelocity);
            body.angularVelocity = Random.insideUnitSphere * tumbleVariation;
        }

        if (pelvisBody != null)
        {
            pelvisBody.AddForce(
                direction * outwardImpulse + Vector3.up * upwardImpulse,
                ForceMode.VelocityChange
            );
            pelvisBody.AddTorque(
                Random.onUnitSphere * tumbleImpulse,
                ForceMode.VelocityChange
            );
        }

        ragdolled = true;
        if (recoveryDelay > 0f)
            recoveryRoutine = StartCoroutine(RecoverAfterDelay());
    }

    void DisableMovementBehaviours()
    {
        savedBehaviours.Clear();

        if (disableDuringRagdoll != null && disableDuringRagdoll.Length > 0)
        {
            foreach (MonoBehaviour behaviour in disableDuringRagdoll)
                DisableAndRemember(behaviour);
        }
        else
        {
            foreach (MonoBehaviour behaviour in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == this || behaviour == characterAnimator)
                    continue;

                string typeName = behaviour.GetType().Name;
                if (typeName == "vThirdPersonInput" ||
                    typeName == "vThirdPersonController" ||
                    typeName == "vThirdPersonMotor")
                    DisableAndRemember(behaviour);
            }
        }
    }

    void DisableAndRemember(MonoBehaviour behaviour)
    {
        if (behaviour == null || behaviour == this)
            return;

        foreach (BehaviourState state in savedBehaviours)
            if (state.behaviour == behaviour)
                return;

        savedBehaviours.Add(new BehaviourState(behaviour));
        behaviour.enabled = false;
    }

    IEnumerator RecoverAfterDelay()
    {
        yield return new WaitForSeconds(recoveryDelay);
        Recover();
    }

    public void Recover()
    {
        if (!ragdolled)
            return;

        if (recoveryRoutine != null)
        {
            StopCoroutine(recoveryRoutine);
            recoveryRoutine = null;
        }

        Vector3 recoveryPosition = transform.position;
        Quaternion recoveryRotation = transform.rotation;
        if (pelvis != null)
        {
            Vector3 forward = Vector3.ProjectOnPlane(pelvis.forward, Vector3.up);
            if (forward.sqrMagnitude > 0.001f)
                recoveryRotation = Quaternion.LookRotation(forward, Vector3.up);
            recoveryPosition = pelvis.position - recoveryRotation * pelvisRootLocalPosition;
        }

        SetRagdollActive(false);
        transform.SetPositionAndRotation(recoveryPosition, recoveryRotation);

        if (rootBody != null)
        {
            rootBody.isKinematic = savedRootBodyKinematic;
            SetBodyVelocity(rootBody, Vector3.zero);
            rootBody.angularVelocity = Vector3.zero;
        }

        for (int i = 0; i < rootColliders.Length; i++)
            if (rootColliders[i] != null)
                rootColliders[i].enabled = savedRootColliderEnabled[i];

        if (characterAnimator != null)
        {
            characterAnimator.enabled = savedAnimatorEnabled;
            if (characterAnimator.enabled && !string.IsNullOrEmpty(recoveryAnimatorState))
                characterAnimator.Play(recoveryAnimatorState, 0, 0f);
        }

        foreach (BehaviourState state in savedBehaviours)
            if (state.behaviour != null)
                state.behaviour.enabled = state.enabled;

        savedBehaviours.Clear();
        ragdolled = false;
    }

    void SetRagdollActive(bool active)
    {
        for (int i = 0; i < ragdollBodies.Length; i++)
        {
            Rigidbody body = ragdollBodies[i];
            if (body != null)
            {
                body.isKinematic = active ? false : savedBodyKinematic[i];
                if (!active)
                {
                    SetBodyVelocity(body, Vector3.zero);
                    body.angularVelocity = Vector3.zero;
                }
            }
        }

        for (int i = 0; i < ragdollColliders.Length; i++)
        {
            Collider collider = ragdollColliders[i];
            if (collider != null)
                collider.enabled = active ? true : savedColliderEnabled[i];
        }
    }

    Rigidbody FindPelvisBody()
    {
        if (pelvis != null)
        {
            Rigidbody pelvisRigidbody = pelvis.GetComponent<Rigidbody>();
            if (pelvisRigidbody != null)
                return pelvisRigidbody;
        }

        return ragdollBodies != null && ragdollBodies.Length > 0
            ? ragdollBodies[0]
            : null;
    }

    static void SetBodyVelocity(Rigidbody body, Vector3 velocity)
    {
#if UNITY_6000_0_OR_NEWER
        body.linearVelocity = velocity;
#else
        body.velocity = velocity;
#endif
    }
}
