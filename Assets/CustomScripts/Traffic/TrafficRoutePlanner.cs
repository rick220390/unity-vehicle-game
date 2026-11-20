using System;
using System.Collections.Generic;
using UnityEngine;

namespace CustomTraffic
{
    /// <summary>
    /// Finds a directed route through the continuous road-lane splines and
    /// the hand-authored junction movement splines.
    ///
    /// A graph node represents a position on a lane: either a lane endpoint
    /// or a junction marker. Lane edges always travel in increasing spline
    /// order. Movement edges connect the incoming marker to the outgoing
    /// marker through a configured junction turn.
    /// </summary>
    [DisallowMultipleComponent]
    public class TrafficRoutePlanner : MonoBehaviour
    {
        [Header("Route Search")]
        [SerializeField, Min(2)] private int laneDistanceSamples = 16;

        public enum ElementType
        {
            Lane,
            JunctionMovement
        }

        [Serializable]
        public class RouteElement
        {
            public ElementType Type;
            public TrafficLaneSpline Lane;
            public float StartT;
            public float EndT;
            public TrafficJunctionMovement Movement;
        }

        public class RoutePlan
        {
            public readonly List<RouteElement> Elements = new List<RouteElement>();
            public float TotalDistance { get; internal set; }
            public TrafficLaneSpline StartLane { get; internal set; }
            public TrafficLaneSpline DestinationLane { get; internal set; }
        }

        private class GraphNode
        {
            public TrafficLaneSpline Lane;
            public float T;
            public Vector3 Position;
            public readonly List<GraphEdge> Edges = new List<GraphEdge>();
        }

        private class GraphEdge
        {
            public GraphNode To;
            public float Cost;
            public TrafficJunctionMovement Movement;
        }

        /// <summary>
        /// Calculates a route from the beginning of startLane to the end of
        /// destinationLane. Returns lane sections and junction turns in order.
        /// This method only reads the scene; it does not move or modify objects.
        /// </summary>
        public bool TryBuildRoute(
            TrafficLaneSpline startLane,
            TrafficLaneSpline destinationLane,
            out RoutePlan route,
            out string reason)
        {
            route = null;

            if (!IsUsableLane(startLane))
            {
                reason = "The starting lane does not have a valid spline.";
                return false;
            }

            if (!IsUsableLane(destinationLane))
            {
                reason = "The destination lane does not have a valid spline.";
                return false;
            }

            List<GraphNode> nodes = BuildGraph();

            GraphNode start = FindNode(nodes, startLane, 0f);
            GraphNode destination = FindNode(nodes, destinationLane, 1f);

            if (start == null || destination == null)
            {
                reason = "Could not find the start or destination lane endpoint in the navigation graph.";
                return false;
            }

            Dictionary<GraphNode, float> distances = new Dictionary<GraphNode, float>();
            Dictionary<GraphNode, GraphNode> previousNodes = new Dictionary<GraphNode, GraphNode>();
            Dictionary<GraphNode, GraphEdge> previousEdges = new Dictionary<GraphNode, GraphEdge>();
            HashSet<GraphNode> unvisited = new HashSet<GraphNode>();

            foreach (GraphNode node in nodes)
            {
                distances[node] = float.PositiveInfinity;
                unvisited.Add(node);
            }

            distances[start] = 0f;

            while (unvisited.Count > 0)
            {
                GraphNode current = null;
                float bestDistance = float.PositiveInfinity;

                foreach (GraphNode candidate in unvisited)
                {
                    float candidateDistance = distances[candidate];
                    if (candidateDistance < bestDistance)
                    {
                        bestDistance = candidateDistance;
                        current = candidate;
                    }
                }

                if (current == null || float.IsPositiveInfinity(bestDistance))
                    break;

                if (current == destination)
                    break;

                unvisited.Remove(current);

                foreach (GraphEdge edge in current.Edges)
                {
                    if (!unvisited.Contains(edge.To))
                        continue;

                    float newDistance = bestDistance + edge.Cost;
                    if (newDistance < distances[edge.To])
                    {
                        distances[edge.To] = newDistance;
                        previousNodes[edge.To] = current;
                        previousEdges[edge.To] = edge;
                    }
                }
            }

            if (float.IsPositiveInfinity(distances[destination]))
            {
                reason = "No valid route connects '" + startLane.LaneDisplayName +
                    "' to '" + destinationLane.LaneDisplayName + "'. Check the junction markers and movement connections.";
                return false;
            }

            List<GraphEdge> routeEdges = new List<GraphEdge>();
            GraphNode cursor = destination;

            while (cursor != start)
            {
                if (!previousEdges.TryGetValue(cursor, out GraphEdge edge) ||
                    !previousNodes.TryGetValue(cursor, out GraphNode previous))
                {
                    reason = "The route search could not reconstruct the selected path.";
                    return false;
                }

                routeEdges.Add(edge);
                cursor = previous;
            }

            routeEdges.Reverse();

            RoutePlan result = new RoutePlan
            {
                StartLane = startLane,
                DestinationLane = destinationLane,
                TotalDistance = distances[destination]
            };

            GraphNode edgeStart = start;

            foreach (GraphEdge edge in routeEdges)
            {
                if (edge.Movement != null)
                {
                    AddLaneElement(result, edgeStart.Lane, edgeStart.T, edgeStart.T);
                    result.Elements.Add(new RouteElement
                    {
                        Type = ElementType.JunctionMovement,
                        Movement = edge.Movement
                    });
                }
                else
                {
                    AddLaneElement(result, edgeStart.Lane, edgeStart.T, edge.To.T);
                }

                edgeStart = edge.To;
            }

            route = result;
            reason = string.Empty;
            return true;
        }

        [ContextMenu("Test Route Planner (First Two Lanes)")]
        private void TestFirstTwoLanes()
        {
            TrafficLaneSpline[] lanes = FindObjectsOfType<TrafficLaneSpline>(true);

            if (lanes.Length < 2)
            {
                Debug.LogWarning("[Traffic Route Planner] At least two lane splines are needed for this test.", this);
                return;
            }

            if (TryBuildRoute(lanes[0], lanes[1], out RoutePlan plan, out string reason))
            {
                Debug.Log("[Traffic Route Planner] Route found: " +
                    DescribeRoute(plan) + " | Approx. distance: " +
                    plan.TotalDistance.ToString("F1") + "m", this);
            }
            else
            {
                Debug.LogWarning("[Traffic Route Planner] " + reason, this);
            }
        }

        private List<GraphNode> BuildGraph()
        {
            List<GraphNode> nodes = new List<GraphNode>();
            Dictionary<TrafficLaneSpline, List<GraphNode>> laneNodes =
                new Dictionary<TrafficLaneSpline, List<GraphNode>>();
            Dictionary<TrafficJunctionMarker, GraphNode> markerNodes =
                new Dictionary<TrafficJunctionMarker, GraphNode>();

            TrafficLaneSpline[] lanes = FindObjectsOfType<TrafficLaneSpline>(true);

            foreach (TrafficLaneSpline lane in lanes)
            {
                if (!IsUsableLane(lane))
                    continue;

                List<GraphNode> onLane = new List<GraphNode>();
                onLane.Add(CreateNode(lane, 0f));
                onLane.Add(CreateNode(lane, 1f));

                laneNodes[lane] = onLane;
                nodes.AddRange(onLane);
            }

            TrafficJunction[] junctions = FindObjectsOfType<TrafficJunction>(true);

            foreach (TrafficJunction junction in junctions)
            {
                foreach (TrafficJunctionMarker marker in junction.GetMarkers())
                {
                    if (marker == null || !marker.IsConfigured(out _) ||
                        !laneNodes.TryGetValue(marker.Lane, out List<GraphNode> onLane))
                        continue;

                    GraphNode markerNode = CreateNode(marker.Lane, marker.NormalizedPosition);
                    onLane.Add(markerNode);
                    nodes.Add(markerNode);
                    markerNodes[marker] = markerNode;
                }
            }

            // A lane is continuous and directional: traffic only advances
            // from its first spline point towards its last spline point.
            foreach (KeyValuePair<TrafficLaneSpline, List<GraphNode>> entry in laneNodes)
            {
                List<GraphNode> onLane = entry.Value;
                onLane.Sort((a, b) => a.T.CompareTo(b.T));

                for (int i = 0; i < onLane.Count - 1; i++)
                {
                    GraphNode from = onLane[i];
                    GraphNode to = onLane[i + 1];

                    if (to.T <= from.T)
                        continue;

                    float distance = EstimateLaneDistance(entry.Key, from.T, to.T);
                    from.Edges.Add(new GraphEdge { To = to, Cost = distance });
                }
            }

            // A junction movement is a directed connection from one lane
            // marker to another. The movement spline supplies its route cost.
            foreach (TrafficJunction junction in junctions)
            {
                foreach (TrafficJunctionMovement movement in junction.GetMovements())
                {
                    if (movement == null || !movement.IsConfigured(out _))
                        continue;

                    TrafficJunctionMarker incomingMarker =
                        FindMarker(junction, movement.IncomingLane);
                    TrafficJunctionMarker outgoingMarker =
                        FindMarker(junction, movement.OutgoingLane);

                    if (incomingMarker == null || outgoingMarker == null ||
                        !markerNodes.TryGetValue(incomingMarker, out GraphNode from) ||
                        !markerNodes.TryGetValue(outgoingMarker, out GraphNode to))
                        continue;

                    float turnDistance = EstimateSplineDistance(movement.MovementSpline);
                    from.Edges.Add(new GraphEdge
                    {
                        To = to,
                        Cost = Mathf.Max(0.1f, turnDistance),
                        Movement = movement
                    });
                }
            }

            return nodes;
        }

        private TrafficJunctionMarker FindMarker(
            TrafficJunction junction,
            TrafficLaneSpline lane)
        {
            foreach (TrafficJunctionMarker marker in junction.GetMarkers())
            {
                if (marker != null && marker.Lane == lane &&
                    marker.IsConfigured(out _))
                    return marker;
            }

            return null;
        }

        private GraphNode CreateNode(TrafficLaneSpline lane, float t)
        {
            Vector3 position = EvaluateLanePosition(lane, t);
            return new GraphNode { Lane = lane, T = t, Position = position };
        }

        private GraphNode FindNode(
            List<GraphNode> nodes,
            TrafficLaneSpline lane,
            float t)
        {
            foreach (GraphNode node in nodes)
            {
                if (node.Lane == lane && Mathf.Abs(node.T - t) < 0.0001f)
                    return node;
            }

            return null;
        }

        private bool IsUsableLane(TrafficLaneSpline lane)
        {
            return lane != null &&
                lane.SplineContainer != null &&
                lane.SplineContainer.Spline != null &&
                lane.SplineContainer.Spline.Count >= 2;
        }

        private Vector3 EvaluateLanePosition(TrafficLaneSpline lane, float t)
        {
            var p = UnityEngine.Splines.SplineUtility.EvaluatePosition(
                lane.SplineContainer.Spline, t);
            return lane.SplineContainer.transform.TransformPoint(
                new Vector3(p.x, p.y, p.z));
        }

        private float EstimateLaneDistance(
            TrafficLaneSpline lane,
            float startT,
            float endT)
        {
            float distance = 0f;
            Vector3 previous = EvaluateLanePosition(lane, startT);

            for (int i = 1; i <= laneDistanceSamples; i++)
            {
                float t = Mathf.Lerp(startT, endT, i / (float)laneDistanceSamples);
                Vector3 current = EvaluateLanePosition(lane, t);
                distance += Vector3.Distance(previous, current);
                previous = current;
            }

            return distance;
        }

        private float EstimateSplineDistance(
            UnityEngine.Splines.SplineContainer container)
        {
            if (container == null || container.Spline == null)
                return 0f;

            float distance = 0f;
            Vector3 previous = EvaluateContainerPosition(container, 0f);

            for (int i = 1; i <= laneDistanceSamples; i++)
            {
                float t = i / (float)laneDistanceSamples;
                Vector3 current = EvaluateContainerPosition(container, t);
                distance += Vector3.Distance(previous, current);
                previous = current;
            }

            return distance;
        }

        private Vector3 EvaluateContainerPosition(
            UnityEngine.Splines.SplineContainer container,
            float t)
        {
            var p = UnityEngine.Splines.SplineUtility.EvaluatePosition(
                container.Spline, t);
            return container.transform.TransformPoint(
                new Vector3(p.x, p.y, p.z));
        }

        private void AddLaneElement(
            RoutePlan plan,
            TrafficLaneSpline lane,
            float startT,
            float endT)
        {
            if (plan.Elements.Count > 0)
            {
                RouteElement previous = plan.Elements[plan.Elements.Count - 1];

                if (previous.Type == ElementType.Lane &&
                    previous.Lane == lane &&
                    Mathf.Abs(previous.EndT - startT) < 0.0001f)
                {
                    previous.EndT = endT;
                    return;
                }
            }

            plan.Elements.Add(new RouteElement
            {
                Type = ElementType.Lane,
                Lane = lane,
                StartT = startT,
                EndT = endT
            });
        }

        private string DescribeRoute(RoutePlan plan)
        {
            List<string> names = new List<string>();

            foreach (RouteElement element in plan.Elements)
            {
                if (element.Type == ElementType.Lane && element.Lane != null)
                    names.Add(element.Lane.LaneDisplayName);
                else if (element.Movement != null)
                    names.Add("[" + element.Movement.DisplayName + "]");
            }

            return string.Join(" -> ", names);
        }
    }
}
