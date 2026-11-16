using UnityEngine;
using UnityEngine.Splines;

namespace CustomTraffic.UTS
{
    /// <summary>
    /// Presents a TrafficLaneSpline to the existing UTS MovePath/CarAIController
    /// as a sampled, one-way WalkPath. This is an isolated bridge; it does not
    /// modify any original UTS scripts.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TrafficLaneSpline))]
    public class TrafficSplineWalkPath : WalkPath
    {
        [Header("Spline Sampling")]
        [SerializeField, Min(8)] private int sampleCount = 80;

        public override void DrawCurved(bool withDraw, EnumDir direct = EnumDir.Forward)
        {
            TrafficLaneSpline lane = GetComponent<TrafficLaneSpline>();
            SplineContainer container = lane != null ? lane.SplineContainer : null;

            if (container == null || container.Spline == null || container.Spline.Count < 2)
            {
                Debug.LogError("[UTS Traffic Bridge] A valid TrafficLaneSpline/SplineContainer is required.", this);
                return;
            }

            // UTS uses duplicate padding points at indices 0 and the final index.
            numberOfWays = 1;
            loopPath = true; // Keeps the UTS controller from requesting an unconfigured SpawnPoint at the end.
            _forward = new bool[1] { true };
            pointLength = new int[2];
            pointLength[0] = sampleCount + 2;
            points = new Vector3[1, sampleCount + 2];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)(sampleCount - 1);
                Unity.Mathematics.float3 evaluated = SplineUtility.EvaluatePosition(container.Spline, t);
                Vector3 local = new Vector3(evaluated.x, evaluated.y, evaluated.z);
                points[0, i + 1] = container.transform.TransformPoint(local);
            }

            points[0, 0] = points[0, 1];
            points[0, sampleCount + 1] = points[0, sampleCount];

            if (withDraw)
            {
                for (int i = 1; i < sampleCount; i++)
                    Debug.DrawLine(points[0, i], points[0, i + 1], Color.cyan);
            }
        }
    }
}