using UnityEngine;

/// <summary>
/// Generates a simple tapered beer volume inside a glass.
/// Place this on the BeerLiquid child object and assign an amber material
/// to its MeshRenderer. The glass model itself is not modified.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class BeerGlassLiquid : MonoBehaviour
{
    [Header("Glass Interior Dimensions (metres)")]
    [Min(0.001f)] public float bottomRadius = 0.035f;
    [Min(0.001f)] public float topRadius = 0.034f;
    [Min(0.001f)] public float maximumHeight = 0.12f;

    [Header("Fill")]
    [Range(0f, 1f)] public float fillAmount = 0f;

    [Header("Mesh")]
    [Range(8, 64)] public int radialSegments = 32;

    private Mesh liquidMesh;
    private MeshFilter meshFilter;

    private void Awake()
    {
        EnsureMesh();
        UpdateLiquidMesh();
    }

    private void OnValidate()
    {
        bottomRadius = Mathf.Max(0.001f, bottomRadius);
        topRadius = Mathf.Max(0.001f, topRadius);
        maximumHeight = Mathf.Max(0.001f, maximumHeight);
        radialSegments = Mathf.Clamp(radialSegments, 8, 64);
        fillAmount = Mathf.Clamp01(fillAmount);

        if (Application.isPlaying)
        {
            EnsureMesh();
            UpdateLiquidMesh();
        }
    }

    private void OnDestroy()
    {
        if (liquidMesh != null)
        {
            if (Application.isPlaying)
                Destroy(liquidMesh);
            else
                DestroyImmediate(liquidMesh);
        }
    }

    /// <summary>Sets the glass fill from 0 (empty) to 1 (full).</summary>
    public void SetFillAmount(float amount)
    {
        fillAmount = Mathf.Clamp01(amount);
        EnsureMesh();
        UpdateLiquidMesh();
    }

    private void EnsureMesh()
    {
        if (meshFilter == null)
            meshFilter = GetComponent<MeshFilter>();

        if (liquidMesh == null)
        {
            liquidMesh = new Mesh();
            liquidMesh.name = "Generated Beer Liquid";
            liquidMesh.MarkDynamic();
            meshFilter.sharedMesh = liquidMesh;
        }
    }

    private void UpdateLiquidMesh()
    {
        if (liquidMesh == null)
            return;

        float height = maximumHeight * fillAmount;

        if (fillAmount <= 0.001f || height <= 0.001f)
        {
            liquidMesh.Clear();
            return;
        }

        int segments = radialSegments;
        int ringVertexCount = (segments + 1) * 2;
        int vertexCount = ringVertexCount + 2;
        Vector3[] vertices = new Vector3[vertexCount];
        int[] triangles = new int[segments * 12];

        float currentTopRadius = Mathf.Lerp(bottomRadius, topRadius, fillAmount);

        for (int i = 0; i <= segments; i++)
        {
            float angle = (Mathf.PI * 2f * i) / segments;
            float x = Mathf.Cos(angle);
            float z = Mathf.Sin(angle);

            vertices[i] = new Vector3(x * bottomRadius, 0f, z * bottomRadius);
            vertices[segments + 1 + i] =
                new Vector3(x * currentTopRadius, height, z * currentTopRadius);
        }

        int bottomCenter = ringVertexCount;
        int topCenter = ringVertexCount + 1;
        vertices[bottomCenter] = Vector3.zero;
        vertices[topCenter] = new Vector3(0f, height, 0f);

        int triangleIndex = 0;

        for (int i = 0; i < segments; i++)
        {
            int bottomA = i;
            int bottomB = i + 1;
            int topA = segments + 1 + i;
            int topB = segments + 1 + i + 1;

            // Outer tapered wall.
            triangles[triangleIndex++] = bottomA;
            triangles[triangleIndex++] = topA;
            triangles[triangleIndex++] = topB;

            triangles[triangleIndex++] = bottomA;
            triangles[triangleIndex++] = topB;
            triangles[triangleIndex++] = bottomB;

            // Bottom cap, facing down.
            triangles[triangleIndex++] = bottomCenter;
            triangles[triangleIndex++] = bottomA;
            triangles[triangleIndex++] = bottomB;

            // Top surface, facing up.
            triangles[triangleIndex++] = topCenter;
            triangles[triangleIndex++] = topB;
            triangles[triangleIndex++] = topA;
        }

        liquidMesh.Clear();
        liquidMesh.vertices = vertices;
        liquidMesh.triangles = triangles;
        liquidMesh.RecalculateNormals();
        liquidMesh.RecalculateBounds();
    }
}
