using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Optional, non-invasive collision damage for a vehicle.
/// Attach to the same root GameObject as Vehicle and a Rigidbody.
/// Static MeshFilter meshes can be deformed around impact points at runtime.
/// For the cleanest results, assign only exterior body/panel meshes in the Inspector.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class VehicleDamage : MonoBehaviour
{
    [Header("Impact thresholds")]
    [Tooltip("Collisions below this relative speed do not cause visible damage.")]
    [Min(0f)] public float minimumImpactSpeedKph = 12f;
    [Tooltip("At or above this speed, an impact uses the maximum configured dent size.")]
    [Min(1f)] public float maximumImpactSpeedKph = 80f;
    [Tooltip("Minimum time between damage events, preventing a single scrape from damaging every physics frame.")]
    [Min(0f)] public float impactCooldown = 0.25f;

    [Header("Mesh deformation")]
    [Tooltip("Assign the exterior body/panel MeshFilters here for precise control. If empty, eligible child MeshFilters are discovered automatically.")]
    public MeshFilter[] deformableMeshes;
    [Tooltip("Automatically discover static child meshes when Deformable Meshes is empty.")]
    public bool autoFindBodyMeshes = true;
    [Tooltip("Approximate radius of the dent around the collision point, in world units.")]
    [Min(0.05f)] public float deformationRadius = 0.85f;
    [Tooltip("Smallest indentation for a collision that just exceeds the minimum impact speed.")]
    [Min(0f)] public float minimumDentDepth = 0.008f;
    [Tooltip("Largest indentation applied by one strong impact, in world units.")]
    [Min(0f)] public float maximumDentDepthPerImpact = 0.12f;
    [Tooltip("Maximum accumulated distance any single vertex can move away from its original position.")]
    [Min(0.01f)] public float maximumVertexDisplacement = 0.28f;
    [Tooltip("Scale damage amounts for the overall car. Useful for tuning different vehicle sizes.")]
    [Range(0.1f, 3f)] public float damageScale = 1f;

    [Header("Damage state")]
    [Range(0f, 1f)] public float currentDamage;
    [Tooltip("Increase in the damage meter from a maximum-severity impact.")]
    [Range(0.01f, 1f)] public float damagePerMaximumImpact = 0.25f;

    [Header("Debug (read during Play mode)")]
    public float lastImpactSpeedKph;
    [Range(0f, 1f)] public float lastImpactSeverity;
    public float lastAppliedDentDepth;
    public Vector3 lastImpactPoint;
    public int lastImpactDeformedVertexCount;

    public event Action<float, Vector3> Damaged;

    private Rigidbody body;
    private float nextImpactAllowedTime;
    private readonly List<RuntimeMesh> runtimeMeshes = new List<RuntimeMesh>();

    public float DamagePercent => currentDamage * 100f;
    public bool IsHeavilyDamaged => currentDamage >= 0.75f;

    private sealed class RuntimeMesh
    {
        public MeshFilter filter;
        public Mesh instance;
        public Vector3[] originalVertices;
        public Vector3[] vertices;
        public Vector3[] accumulatedOffsets;
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        CacheDeformableMeshes();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null || collision.contactCount == 0)
            return;

        float impactSpeed = collision.relativeVelocity.magnitude * 3.6f;
        lastImpactSpeedKph = impactSpeed;

        if (impactSpeed < minimumImpactSpeedKph || Time.time < nextImpactAllowedTime)
            return;

        nextImpactAllowedTime = Time.time + impactCooldown;
        float denominator = Mathf.Max(1f, maximumImpactSpeedKph - minimumImpactSpeedKph);
        float severity = Mathf.Clamp01((impactSpeed - minimumImpactSpeedKph) / denominator);
        // A gentle curve gives medium-speed bumps visible dents without making
        // the first threshold-crossing collision look like a major crash.
        severity = severity * severity * (3f - 2f * severity);

        ContactPoint contact = collision.GetContact(0);
        Vector3 impactPoint = contact.point;
        Vector3 outwardFromObstacle = contact.normal;

        // Prefer the contact whose normal best opposes the vehicle's approach.
        float mostOpposed = float.PositiveInfinity;
        Vector3 approach = collision.relativeVelocity.sqrMagnitude > 0.001f
            ? collision.relativeVelocity.normalized
            : -transform.forward;

        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint candidate = collision.GetContact(i);
            float score = Vector3.Dot(approach, candidate.normal);
            if (score < mostOpposed)
            {
                mostOpposed = score;
                impactPoint = candidate.point;
                outwardFromObstacle = candidate.normal;
            }
        }

        float dentDepth = Mathf.Lerp(minimumDentDepth, maximumDentDepthPerImpact, severity)
            * Mathf.Max(0.1f, damageScale);

        lastImpactSeverity = severity;
        lastImpactPoint = impactPoint;
        lastAppliedDentDepth = dentDepth;
        lastImpactDeformedVertexCount = DeformAtImpact(
            impactPoint, -outwardFromObstacle.normalized, dentDepth);

        currentDamage = Mathf.Clamp01(
            currentDamage + Mathf.Max(0f, severity) * damagePerMaximumImpact);

        Damaged?.Invoke(impactSpeed, impactPoint);
    }

    private void CacheDeformableMeshes()
    {
        runtimeMeshes.Clear();

        List<MeshFilter> candidates = new List<MeshFilter>();
        if (deformableMeshes != null)
        {
            foreach (MeshFilter filter in deformableMeshes)
            {
                if (filter != null && !candidates.Contains(filter))
                    candidates.Add(filter);
            }
        }

        if (candidates.Count == 0 && autoFindBodyMeshes)
        {
            MeshFilter[] children = GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter filter in children)
            {
                if (filter == null || IsLikelyNonBodyMesh(filter.transform) ||
                    filter.sharedMesh == null || filter.sharedMesh.vertexCount < 12 ||
                    filter.GetComponent<MeshRenderer>() == null)
                    continue;

                candidates.Add(filter);
            }
        }

        foreach (MeshFilter filter in candidates)
        {
            if (filter == null || filter.sharedMesh == null ||
                filter.GetComponent<MeshRenderer>() == null)
                continue;

            // Never edit an imported/shared asset mesh: make a private runtime copy.
            Mesh source = filter.sharedMesh;
            Mesh instance = Instantiate(source);
            instance.name = source.name + " (Runtime Deformed)";
            instance.MarkDynamic();

            RuntimeMesh runtime = new RuntimeMesh
            {
                filter = filter,
                instance = instance,
                originalVertices = source.vertices,
                vertices = instance.vertices,
                accumulatedOffsets = new Vector3[source.vertexCount]
            };

            filter.sharedMesh = instance;
            runtimeMeshes.Add(runtime);
        }
    }

    private bool IsLikelyNonBodyMesh(Transform candidate)
    {
        string n = candidate.name.ToLowerInvariant();
        string[] excludedWords =
        {
            "wheel", "tyre", "tire", "rim", "alloy", "brake", "caliper",
            "disc", "rotor", "glass", "window", "windscreen", "windshield",
            "light", "lamp", "led", "mirror", "seat", "interior", "steering",
            "exhaust", "pipe", "wiper", "numberplate", "licenseplate"
        };

        foreach (string word in excludedWords)
        {
            if (n.Contains(word))
                return true;
        }

        return false;
    }

    private int DeformAtImpact(Vector3 worldPoint, Vector3 inwardWorldDirection, float requestedDepth)
    {
        int deformedCount = 0;
        float radius = Mathf.Max(0.05f, deformationRadius);
        float radiusSqr = radius * radius;

        foreach (RuntimeMesh runtime in runtimeMeshes)
        {
            if (runtime == null || runtime.filter == null || runtime.instance == null ||
                !runtime.filter.gameObject.activeInHierarchy)
                continue;

            Transform meshTransform = runtime.filter.transform;
            Vector3 inwardLocalDirection =
                meshTransform.InverseTransformDirection(inwardWorldDirection).normalized;
            bool changed = false;

            for (int i = 0; i < runtime.vertices.Length; i++)
            {
                Vector3 vertexWorld = meshTransform.TransformPoint(runtime.vertices[i]);
                Vector3 fromImpact = vertexWorld - worldPoint;
                float distanceSqr = fromImpact.sqrMagnitude;

                if (distanceSqr > radiusSqr)
                    continue;

                float distance = Mathf.Sqrt(distanceSqr);
                float falloff = 1f - Mathf.Clamp01(distance / radius);
                // Smooth falloff avoids a hard circular dent edge.
                falloff = falloff * falloff * (3f - 2f * falloff);
                if (falloff <= 0.001f)
                    continue;

                Vector3 proposedOffset = runtime.accumulatedOffsets[i] +
                    inwardLocalDirection * (requestedDepth * falloff);
                float maxDisplacement = Mathf.Max(0.01f, maximumVertexDisplacement);
                if (proposedOffset.magnitude > maxDisplacement)
                    proposedOffset = proposedOffset.normalized * maxDisplacement;

                Vector3 offsetDelta = proposedOffset - runtime.accumulatedOffsets[i];
                if (offsetDelta.sqrMagnitude <= 0.0000001f)
                    continue;

                runtime.accumulatedOffsets[i] = proposedOffset;
                runtime.vertices[i] = runtime.originalVertices[i] + proposedOffset;
                deformedCount++;
                changed = true;
            }

            if (changed)
            {
                runtime.instance.vertices = runtime.vertices;
                runtime.instance.RecalculateBounds();
                // Recalculating normals gives more believable lighting on dents.
                runtime.instance.RecalculateNormals();
            }
        }

        return deformedCount;
    }

    /// <summary>
    /// Restores the runtime meshes and clears the damage meter. Useful for a future
    /// repair interaction at a garage or service station.
    /// </summary>
    public void RepairFully()
    {
        foreach (RuntimeMesh runtime in runtimeMeshes)
        {
            if (runtime == null || runtime.instance == null)
                continue;

            runtime.vertices = (Vector3[])runtime.originalVertices.Clone();
            runtime.accumulatedOffsets = new Vector3[runtime.originalVertices.Length];
            runtime.instance.vertices = runtime.vertices;
            runtime.instance.RecalculateBounds();
            runtime.instance.RecalculateNormals();
        }

        currentDamage = 0f;
        lastImpactSeverity = 0f;
        lastAppliedDentDepth = 0f;
        lastImpactDeformedVertexCount = 0;
    }

    private void OnDestroy()
    {
        foreach (RuntimeMesh runtime in runtimeMeshes)
        {
            if (runtime == null || runtime.instance == null)
                continue;

            // Restore the original shared mesh reference if this filter still exists.
            if (runtime.filter != null && runtime.filter.sharedMesh == runtime.instance)
                runtime.filter.sharedMesh = runtime.originalVertices != null ? runtime.filter.sharedMesh : null;

            if (Application.isPlaying)
                Destroy(runtime.instance);
            else
                DestroyImmediate(runtime.instance);
        }
    }
}
