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

        [Tooltip("Search distance either side of each junction marker when matching a turn endpoint to its lane spline.")]
        [SerializeField, Range(0.01f, 0.4f)] private float endpointSearchWindow = 0.15f;

        [Tooltip("Reject a junction movement when either endpoint is this far or more from its connected lane spline.")]
        [SerializeField, Min(0.1f)] private float endpointWarningDistance = 3f;

        [Header("Inspector Test")]
        [SerializeField] private TrafficLaneSpline testStartLane;
        [SerializeField] private TrafficLaneSpline testDestinationLane;

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
            return TryBuildRoute(startLane, 0f, destinationLane, out route, out reason);
        }

        /// <summary>
        /// Calculates a route from a specific normalized position on the
        /// starting lane to the end of the destination lane. Traffic travels
        /// forward along each lane spline.
        /// </summary>
        public bool TryBuildRoute(
            TrafficLaneSpline startLane,
            float startT,
            TrafficLaneSpline destinationLane,
            out RoutePlan route,
            out string reason)
        {
            route = null;
            startT = Mathf.Clamp01(startT);

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

            List<GraphNode> nodes = BuildGraph(startLane, startT);

            GraphNode start = FindNode(nodes, startLane, startT);
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

        [ContextMenu("Test Assigned Route")]
        private void TestAssignedRoute()
        {
            if (testStartLane == null || testDestinationLane == null)
            {
                Debug.LogWarning(
                    "[Traffic Route Planner] Assign both Test Start Lane and Test Destination Lane in the Inspector first.",
                    this);
                return;
            }

            if (TryBuildRoute(testStartLane, testDestinationLane, out RoutePlan plan, out string reason))
            {
                Debug.Log("[Traffic Route Planner] Route found: " +
                    DescribeRoute(plan) + " | Approx. distance: " +
                    plan.TotalDistance.ToString("F1") + "m", this);

                if (TryBuildRoutePoints(plan, out List<Vector3> waypoints, out string geometryReason))
                {
                    Debug.Log("[Traffic Route Planner] Route geometry generated: " +
                        waypoints.Count + " world-space waypoints. Start: " +
                        waypoints[0].ToString("F2") + " | End: " +
                        waypoints[waypoints.Count - 1].ToString("F2"), this);
                }
                else
                {
                    Debug.LogWarning("[Traffic Route Planner] Route found, but waypoint generation failed: " +
                        geometryReason, this);
                }
            }
            else
            {
                Debug.LogWarning("[Traffic Route Planner] " + reason, this);
            }
        }

        /// <summary>
        /// Converts a calculated route into an ordered list of world-space
        /// waypoints. This is the geometry that will later be supplied to UTS.
        /// It does not modify the scene or any existing UTS path.
        /// </summary>
        public bool TryBuildRoutePoints(
            RoutePlan plan,
            out List<Vector3> waypoints,
            out string reason)
        {
            waypoints = new List<Vector3>();

            if (plan == null || plan.Elements == null || plan.Elements.Count == 0)
            {
                reason = "The route plan is empty.";
                return false;
            }

            foreach (RouteElement element in plan.Elements)
            {
                if (element == null)
                    continue;

                if (element.Type == ElementType.Lane)
                {
                    if (!IsUsableLane(element.Lane))
                    {
                        reason = "A route section contains an invalid lane.";
                        waypoints.Clear();
                        return false;
                    }

                    if (Mathf.Abs(element.EndT - element.StartT) < 0.0001f)
                        continue;

                    for (int i = 0; i <= laneDistanceSamples; i++)
                    {
                        float fraction = i / (float)laneDistanceSamples;
                        float t = Mathf.Lerp(element.StartT, element.EndT, fraction);
                        AddWaypointIfNew(
                            waypoints,
                            EvaluateLanePosition(element.Lane, t));
                    }
                }
                else if (element.Type == ElementType.JunctionMovement)
                {
                    TrafficJunctionMovement movement = element.Movement;

                    if (movement == null)
                    {
                        reason = "A route contains a missing junction movement reference.";
                        waypoints.Clear();
                        return false;
                    }

                    if (!movement.IsConfigured(out string movementReason))
                    {
                        reason = "A route contains an invalid junction movement: " + movementReason;
                        waypoints.Clear();
                        return false;
                    }

                    for (int i = 0; i <= laneDistanceSamples; i++)
                    {
                        float t = i / (float)laneDistanceSamples;
                        AddWaypointIfNew(
                            waypoints,
                            EvaluateContainerPosition(movement.MovementSpline, t));
                    }
                }
            }

            if (waypoints.Count < 2)
            {
                reason = "The generated route contains fewer than two usable waypoints.";
                waypoints.Clear();
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private void AddWaypointIfNew(List<Vector3> waypoints, Vector3 point)
        {
            if (waypoints.Count == 0 ||
                Vector3.SqrMagnitude(waypoints[waypoints.Count - 1] - point) > 0.0001f)
            {
                waypoints.Add(point);
            }
        }

        private List<GraphNode> BuildGraph(
            TrafficLaneSpline requestedStartLane,
            float requestedStartT)
        {
            List<GraphNode> nodes = new List<GraphNode>();
            Dictionary<TrafficLaneSpline, List<GraphNode>> laneNodes =
                new Dictionary<TrafficLaneSpline, List<GraphNode>>();
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

            // Insert the requested spawn position before lane edges are built.
            // Sorting below then creates a forward-only edge from this point
            // towards the next junction marker or lane endpoint.
            if (IsUsableLane(requestedStartLane) &&
                laneNodes.TryGetValue(requestedStartLane, out List<GraphNode> startLaneNodes))
            {
                bool alreadyExists = false;
                foreach (GraphNode existing in startLaneNodes)
                {
                    if (Mathf.Abs(existing.T - requestedStartT) <= 0.0001f)
                    {
                        alreadyExists = true;
                        break;
                    }
                }

                if (!alreadyExists)
                {
                    GraphNode requestedStart = CreateNode(requestedStartLane, requestedStartT);
                    startLaneNodes.Add(requestedStart);
                    nodes.Add(requestedStart);
                }
            }

            TrafficJunction[] junctions = FindObjectsOfType<TrafficJunction>(true);

            // Each movement uses a marker on BOTH connected lanes.
            // Markers constrain the endpoint search to the correct local section
            // of a lane, which is especially important around roundabouts.
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

                    if (incomingMarker == null || outgoingMarker == null)
                    {
                        Debug.LogWarning(
                            "[Traffic Route Planner] Movement '" + movement.DisplayName +
                            "' on junction '" + junction.name +
                            "' needs a marker for both its incoming and outgoing lanes.",
                            movement);
                        continue;
                    }

                    Vector3 movementStart = EvaluateContainerPosition(
                        movement.MovementSpline, 0f);
                    Vector3 movementEnd = EvaluateContainerPosition(
                        movement.MovementSpline, 1f);

                    GraphNode from = FindOrCreateLaneNode(
                        movement.IncomingLane,
                        movementStart,
                        incomingMarker.NormalizedPosition,
                        laneNodes,
                        nodes,
                        out float incomingGap);

                    GraphNode to = FindOrCreateLaneNode(
                        movement.OutgoingLane,
                        movementEnd,
                        outgoingMarker.NormalizedPosition,
                        laneNodes,
                        nodes,
                        out float outgoingGap);

                    if (from == null || to == null)
                        continue;

                    if (incomingGap > endpointWarningDistance ||
                        outgoingGap > endpointWarningDistance)
                    {
                        Debug.LogWarning(
                            "[Traffic Route Planner] Skipping movement '" + movement.DisplayName +
                            "' because a turn endpoint is too far from its marked lane. Incoming gap: " +
                            incomingGap.ToString("F2") + "m; outgoing gap: " +
                            outgoingGap.ToString("F2") + "m. Check the movement spline endpoints and junction markers.",
                            movement);
                        continue;
                    }

                    float turnDistance = EstimateSplineDistance(movement.MovementSpline);
                    from.Edges.Add(new GraphEdge
                    {
                        To = to,
                        Cost = Mathf.Max(0.1f, turnDistance),
                        Movement = movement
                    });
                }
            }

            // Add the continuous, one-way lane connections after the movement
            // endpoints have been inserted into their outgoing lane splines.
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

        /// <summary>
        /// Finds the closest point to a movement endpoint only within the
        /// local window around its junction marker, then inserts that point
        /// into the directed lane graph.
        /// </summary>
        private GraphNode FindOrCreateLaneNode(
            TrafficLaneSpline lane,
            Vector3 targetPosition,
            float expectedT,
            Dictionary<TrafficLaneSpline, List<GraphNode>> laneNodes,
            List<GraphNode> allNodes,
            out float closestGap)
        {
            closestGap = float.PositiveInfinity;

            if (!IsUsableLane(lane) ||
                !laneNodes.TryGetValue(lane, out List<GraphNode> onLane))
                return null;

            float minT = Mathf.Max(0f, expectedT - endpointSearchWindow);
            float maxT = Mathf.Min(1f, expectedT + endpointSearchWindow);

            const int searchSamples = 80;
            float bestT = expectedT;
            float bestSqrDistance = float.PositiveInfinity;

            for (int i = 0; i <= searchSamples; i++)
            {
                float fraction = i / (float)searchSamples;
                float t = Mathf.Lerp(minT, maxT, fraction);
                Vector3 candidate = EvaluateLanePosition(lane, t);
                float sqrDistance = (candidate - targetPosition).sqrMagnitude;

                if (sqrDistance < bestSqrDistance)
                {
                    bestSqrDistance = sqrDistance;
                    bestT = t;
                }
            }

            closestGap = Mathf.Sqrt(bestSqrDistance);

            // Reuse an existing node if this connection lands at the same
            // normalized position as another connection or lane endpoint.
            foreach (GraphNode existing in onLane)
            {
                if (Mathf.Abs(existing.T - bestT) <= 0.001f)
                    return existing;
            }

            GraphNode created = CreateNode(lane, bestT);
            onLane.Add(created);
            allNodes.Add(created);
            return created;
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
