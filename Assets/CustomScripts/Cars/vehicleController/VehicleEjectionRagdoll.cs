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
    [Tooltip("Layers that can be used as the ground when the character recovers.")]
    public LayerMask recoveryGroundMask = ~0;
    [Min(0.5f)] public float recoveryGroundCheckHeight = 2f;
    [Min(1f)] public float recoveryGroundCheckDistance = 12f;
    [Header("Stand-up Recovery")]
    [Tooltip("Animator state name for the get-up animation. Create this state in the character Animator Controller.")]
    public string standUpAnimatorState = "StandUp";
    [Tooltip("Animator state to return to after the get-up animation completes.")]
    public string recoveryAnimatorState = "Locomotion";
    [Tooltip("Maximum time to wait for the stand-up state to finish before restoring control.")]
    [Min(1f)] public float standUpAnimationTimeout = 5f;
    [Tooltip("Enable root motion while the stand-up animation plays. Disable this for an in-place animation.")]
    public bool standUpApplyRootMotion = true;

    private Rigidbody rootBody;
    private Collider[] rootColliders;
    private readonly List<BehaviourState> savedBehaviours = new List<BehaviourState>();
    private readonly List<bool> savedBodyKinematic = new List<bool>();
    private readonly List<bool> savedColliderEnabled = new List<bool>();
    private bool[] savedRootColliderEnabled;
    private bool savedAnimatorEnabled;
    private bool savedAnimatorApplyRootMotion;
    private Vector3 pelvisRootLocalPosition;
    private bool savedRootBodyKinematic;
    private bool ragdolled;
    private bool recoveryInProgress;
    private Coroutine recoveryRoutine;

    public bool IsConfigured => ragdollBodies != null && ragdollBodies.Length > 0 &&
        ragdollColliders != null && ragdollColliders.Length > 0;
    public bool IsRagdolled => ragdolled;

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

        // The normal animated character owns the pose until ejection. Keep
        // ragdoll bones dormant regardless of their imported prefab defaults.
        foreach (Rigidbody body in ragdollBodies)
            savedBodyKinematic.Add(true);
        foreach (Collider collider in ragdollColliders)
            savedColliderEnabled.Add(false);

        savedRootColliderEnabled = new bool[rootColliders.Length];
        for (int i = 0; i < rootColliders.Length; i++)
            savedRootColliderEnabled[i] = rootColliders[i] != null && rootColliders[i].enabled;

        savedRootBodyKinematic = rootBody != null && rootBody.isKinematic;
        savedAnimatorEnabled = characterAnimator != null && characterAnimator.enabled;
        savedAnimatorApplyRootMotion = characterAnimator != null && characterAnimator.applyRootMotion;
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
        recoveryRoutine = null;
        recoveryInProgress = false;

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

    /// <summary>Activates the same ragdoll when a moving vehicle strikes the player.</summary>
    public void RagdollFromImpact(Vector3 vehicleVelocity, Vector3 impactImpulse, Vector3 impactPoint)
    {
        if (ragdolled || !IsConfigured)
            return;

        if (recoveryRoutine != null)
            StopCoroutine(recoveryRoutine);
        recoveryRoutine = null;
        recoveryInProgress = false;

        transform.SetParent(null, true);
        if (characterAnimator != null)
            characterAnimator.enabled = false;

        DisableMovementBehaviours();

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

        foreach (Rigidbody body in ragdollBodies)
        {
            if (body == null)
                continue;
            SetBodyVelocity(body, vehicleVelocity);
            body.angularVelocity = Vector3.zero;
        }

        Rigidbody pelvisBody = FindPelvisBody();
        if (pelvisBody != null)
        {
            Vector3 impulse = impactImpulse;
            if (impulse.sqrMagnitude < 0.01f)
                impulse = (vehicleVelocity - GetRootVelocity()) * pelvisBody.mass;
            pelvisBody.AddForceAtPosition(impulse, impactPoint, ForceMode.Impulse);
            pelvisBody.AddTorque(Random.onUnitSphere * tumbleImpulse, ForceMode.Impulse);
        }

        ragdolled = true;
        if (recoveryDelay > 0f)
            recoveryRoutine = StartCoroutine(RecoverAfterDelay());
    }

    Vector3 GetRootVelocity()
    {
        if (rootBody == null)
            return Vector3.zero;
#if UNITY_6000_0_OR_NEWER
        return rootBody.linearVelocity;
#else
        return rootBody.velocity;
#endif
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
        if (!ragdolled || recoveryInProgress)
            yield break;

        recoveryInProgress = true;
        yield return RecoverRoutine();
    }

    public void Recover()
    {
        if (!ragdolled || recoveryInProgress)
            return;

        recoveryInProgress = true;
        recoveryRoutine = StartCoroutine(RecoverRoutine());
    }

    IEnumerator RecoverRoutine()
    {
        if (!ragdolled)
            yield break;

        Vector3 recoveryPosition = transform.position;
        Quaternion recoveryRotation = transform.rotation;
        if (pelvis != null)
        {
            Vector3 forward = Vector3.ProjectOnPlane(pelvis.forward, Vector3.up);
            if (forward.sqrMagnitude > 0.001f)
                recoveryRotation = Quaternion.LookRotation(forward, Vector3.up);
            recoveryPosition = pelvis.position - recoveryRotation * pelvisRootLocalPosition;
        }

        // Stop the bone physics first, then restore the root collider while the
        // root Rigidbody is still kinematic. The pelvis can finish its tumble
        // below or above the floor, so its raw position is not a safe spawn point.
        SetRagdollActive(false);
        transform.SetPositionAndRotation(recoveryPosition, recoveryRotation);

        for (int i = 0; i < rootColliders.Length; i++)
            if (rootColliders[i] != null)
                rootColliders[i].enabled = savedRootColliderEnabled[i];

        Physics.SyncTransforms();
        recoveryPosition = GetGroundedRecoveryPosition(recoveryPosition);
        transform.position = recoveryPosition;
        Physics.SyncTransforms();

        if (rootBody != null)
        {
            rootBody.position = recoveryPosition;
            // Hold the root steady while the get-up animation plays. Restoring
            // a dynamic Rigidbody now would let gravity/physics fight the clip.
            rootBody.isKinematic = true;
            SetBodyVelocity(rootBody, Vector3.zero);
            rootBody.angularVelocity = Vector3.zero;
        }

        // Keep movement/controller behaviours disabled while the character
        // gets up. Otherwise Invector can apply locomotion over the recovery clip.
        if (characterAnimator != null)
        {
            characterAnimator.enabled = savedAnimatorEnabled;
            if (characterAnimator.enabled)
            {
                characterAnimator.applyRootMotion = standUpApplyRootMotion;
                bool playedStandUp = false;
                if (!string.IsNullOrEmpty(standUpAnimatorState))
                {
                    characterAnimator.Play(standUpAnimatorState, 0, 0f);
                    characterAnimator.Update(0f);
                    playedStandUp = characterAnimator.GetCurrentAnimatorStateInfo(0).IsName(standUpAnimatorState);
                }

                if (playedStandUp)
                {
                    float elapsed = 0f;
                    float timeout = Mathf.Max(1f, standUpAnimationTimeout);
                    while (elapsed < timeout)
                    {
                        AnimatorStateInfo state = characterAnimator.GetCurrentAnimatorStateInfo(0);
                        if (!characterAnimator.IsInTransition(0) &&
                            state.IsName(standUpAnimatorState) &&
                            state.normalizedTime >= 1f)
                            break;

                        elapsed += Time.deltaTime;
                        yield return null;
                    }
                }
                else if (!string.IsNullOrEmpty(standUpAnimatorState))
                {
                    Debug.LogWarning(
                        $"VehicleEjectionRagdoll: Animator state '{standUpAnimatorState}' was not found on layer 0. Check the Stand Up state name in the Inspector.",
                        this
                    );
                }

                // Return to locomotion after the get-up clip, then restore the
                // Animator's original root-motion setting.
                if (!string.IsNullOrEmpty(recoveryAnimatorState))
                    characterAnimator.Play(recoveryAnimatorState, 0, 0f);
                characterAnimator.applyRootMotion = savedAnimatorApplyRootMotion;
            }
        }

        foreach (BehaviourState state in savedBehaviours)
            if (state.behaviour != null)
                state.behaviour.enabled = state.enabled;

        savedBehaviours.Clear();
        if (rootBody != null)
        {
            rootBody.position = transform.position;
            rootBody.isKinematic = savedRootBodyKinematic;
            SetBodyVelocity(rootBody, Vector3.zero);
            rootBody.angularVelocity = Vector3.zero;
        }
        ragdolled = false;
        recoveryInProgress = false;
        recoveryRoutine = null;
    }


    Vector3 GetGroundedRecoveryPosition(Vector3 candidatePosition)
    {
        Vector3 rayOrigin = candidatePosition + Vector3.up * Mathf.Max(0.5f, recoveryGroundCheckHeight);
        RaycastHit[] hits = Physics.RaycastAll(
            rayOrigin,
            Vector3.down,
            Mathf.Max(1f, recoveryGroundCheckDistance),
            recoveryGroundMask,
            QueryTriggerInteraction.Ignore
        );

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            Collider hitCollider = hit.collider;
            if (hitCollider == null)
                continue;

            // Never treat the player's own rig or the vehicle they exited as ground.
            if (hitCollider.transform == transform || hitCollider.transform.IsChildOf(transform))
                continue;
            if (hitCollider.GetComponentInParent<Vehicle>() != null)
                continue;

            float lowestRootColliderY = float.PositiveInfinity;
            foreach (Collider rootCollider in rootColliders)
            {
                if (rootCollider != null && rootCollider.enabled)
                    lowestRootColliderY = Mathf.Min(lowestRootColliderY, rootCollider.bounds.min.y);
            }

            if (float.IsPositiveInfinity(lowestRootColliderY))
                return candidatePosition;

            float rootToColliderBottom = transform.position.y - lowestRootColliderY;
            candidatePosition.y = hit.point.y + rootToColliderBottom + 0.03f;
            return candidatePosition;
        }

        // If no ground is found (for example, while recovering over a pit),
        // preserve the physics-derived position rather than teleporting arbitrarily.
        return candidatePosition;
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
