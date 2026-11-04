using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

[ExecuteAlways]
[RequireComponent(typeof(SplineContainer))]
public class SplineRoadDeformer : MonoBehaviour
{
public enum RoadAxis { X, Z }


[Header("Source Road")]
public MeshFilter sourceRoad;
public RoadAxis lengthAxis = RoadAxis.Z;

[Header("Road Orientation")]
[Tooltip("Rotate the original FBX around its length axis.")]
public float rotationOffset = 90f;

[Tooltip("Flip the road sideways.")]
public bool flipRoad = false;

[Header("Road Position")]
public float heightOffset = 0f;

[Header("Generation")]
public bool updateInEditor = true;
public bool generateCollider = true;
public bool recalculateNormals = true;

private GameObject generatedObject;
private Mesh generatedMesh;
private int previousHash;

private void OnEnable()
{
    GenerateRoad();
}

private void OnValidate()
{
    if (isActiveAndEnabled)
        GenerateRoad();
}

private void Update()
{
    if (!updateInEditor)
        return;

    int hash = CalculateHash();

    if (hash != previousHash)
        GenerateRoad();
}

private int CalculateHash()
{
    unchecked
    {
        int hash = 17;

        var container = GetComponent<SplineContainer>();

        hash = hash * 31 + rotationOffset.GetHashCode();
        hash = hash * 31 + heightOffset.GetHashCode();
        hash = hash * 31 + flipRoad.GetHashCode();
        hash = hash * 31 + lengthAxis.GetHashCode();

        if (container == null)
            return hash;

        for (int i = 0; i <= 30; i++)
        {
            float3 p = container.EvaluatePosition(i / 30f);

            hash = hash * 31 + p.x.GetHashCode();
            hash = hash * 31 + p.y.GetHashCode();
            hash = hash * 31 + p.z.GetHashCode();
        }

        if (sourceRoad != null && sourceRoad.sharedMesh != null)
            hash = hash * 31 + sourceRoad.sharedMesh.GetInstanceID();

        return hash;
    }
}

[ContextMenu("Generate Road")]
public void GenerateRoad()
{
    if (sourceRoad == null || sourceRoad.sharedMesh == null)
        return;

    SplineContainer container = GetComponent<SplineContainer>();

    if (container == null || container.Spline.Count < 2)
        return;

    Mesh original = sourceRoad.sharedMesh;
    Bounds bounds = original.bounds;

    float min = lengthAxis == RoadAxis.Z
        ? bounds.min.z
        : bounds.min.x;

    float max = lengthAxis == RoadAxis.Z
        ? bounds.max.z
        : bounds.max.x;

    float sourceLength = max - min;

    if (sourceLength <= 0.001f)
        return;

    const int samples = 500;

    Vector3[] positions = new Vector3[samples + 1];
    Vector3[] rights = new Vector3[samples + 1];

    float[] distances = new float[samples + 1];

    for (int i = 0; i <= samples; i++)
    {
        float t = (float)i / samples;

        float3 p = container.EvaluatePosition(t);
        float3 tangent = math.normalize(
            container.EvaluateTangent(t));

        Vector3 forward = new Vector3(
            tangent.x, tangent.y, tangent.z).normalized;

        positions[i] = transform.InverseTransformPoint(
            new Vector3(p.x, p.y, p.z));

        Vector3 right = Vector3.Cross(
            Vector3.up, forward).normalized;

        Quaternion rotation = Quaternion.AngleAxis(
            rotationOffset, forward);

        right = rotation * right;

        if (flipRoad)
            right = -right;

        rights[i] = right;

        if (i > 0)
        {
            distances[i] = distances[i - 1] +
                Vector3.Distance(positions[i - 1], positions[i]);
        }
    }

    float totalLength = distances[samples];

    if (totalLength <= 0.001f)
        return;

    Vector3[] originalVertices = original.vertices;
    Vector3[] newVertices = new Vector3[originalVertices.Length];

    for (int i = 0; i < originalVertices.Length; i++)
    {
        Vector3 vertex = originalVertices[i];

        float longitudinal = lengthAxis == RoadAxis.Z
            ? vertex.z - min
            : vertex.x - min;

        float normalizedDistance = Mathf.Clamp01(
            longitudinal / sourceLength);

        float targetDistance = normalizedDistance * totalLength;

        int index = 0;

        while (index < samples - 1 &&
               distances[index + 1] < targetDistance)
        {
            index++;
        }

        float segmentLength =
            distances[index + 1] - distances[index];

        float blend = segmentLength > 0.0001f
            ? (targetDistance - distances[index]) / segmentLength
            : 0f;

        Vector3 center = Vector3.Lerp(
            positions[index],
            positions[index + 1],
            blend);

        Vector3 right = Vector3.Slerp(
            rights[index],
            rights[index + 1],
            blend).normalized;

        float lateral = lengthAxis == RoadAxis.Z
            ? vertex.x
            : vertex.z;

        float height = vertex.y + heightOffset;

        newVertices[i] = center +
            right * lateral +
            Vector3.up * height;
    }

    if (generatedMesh == null)
    {
        generatedMesh = new Mesh();
        generatedMesh.name = "Deformed Road Mesh";
    }

    generatedMesh.Clear();

    generatedMesh.vertices = newVertices;

    generatedMesh.subMeshCount = original.subMeshCount;

    for (int i = 0; i < original.subMeshCount; i++)
    {
        generatedMesh.SetTriangles(
            original.GetTriangles(i), i);
    }

    generatedMesh.uv = original.uv;
    generatedMesh.uv2 = original.uv2;
    generatedMesh.uv3 = original.uv3;
    generatedMesh.uv4 = original.uv4;
    generatedMesh.colors = original.colors;

    if (recalculateNormals)
        generatedMesh.RecalculateNormals();
    else
        generatedMesh.normals = original.normals;

    generatedMesh.RecalculateBounds();

    if (generatedObject == null)
    {
        Transform existing = transform.Find("DeformedRoad");

        if (existing != null)
            generatedObject = existing.gameObject;
        else
        {
            generatedObject = new GameObject("DeformedRoad");
            generatedObject.transform.SetParent(transform, false);
        }
    }

    generatedObject.transform.localPosition = Vector3.zero;
    generatedObject.transform.localRotation = Quaternion.identity;
    generatedObject.transform.localScale = Vector3.one;

    MeshFilter filter = generatedObject.GetComponent<MeshFilter>();

    if (filter == null)
        filter = generatedObject.AddComponent<MeshFilter>();

    filter.sharedMesh = generatedMesh;

    MeshRenderer renderer =
        generatedObject.GetComponent<MeshRenderer>();

    if (renderer == null)
        renderer = generatedObject.AddComponent<MeshRenderer>();

    MeshRenderer originalRenderer =
        sourceRoad.GetComponent<MeshRenderer>();

    if (originalRenderer != null)
        renderer.sharedMaterials = originalRenderer.sharedMaterials;

    if (generateCollider)
    {
        MeshCollider collider =
            generatedObject.GetComponent<MeshCollider>();

        if (collider == null)
            collider = generatedObject.AddComponent<MeshCollider>();

        collider.sharedMesh = null;
        collider.sharedMesh = generatedMesh;
    }

    previousHash = CalculateHash();
}

private void OnDisable()
{
    // Keep generated road visible in edit mode.
}


}
