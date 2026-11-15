using System.Collections.Generic;
using UnityEngine;

namespace CustomTraffic
{
    /// <summary>
    /// Collects directed road-lane and junction-movement connections.
    /// The end node of one connection can be the start node of the next.
    /// </summary>
    public class TrafficNetworkManager : MonoBehaviour
    {
        [Header("Network")]
        [SerializeField] private bool scanOnStart = true;

        private readonly Dictionary<TrafficNode, List<TrafficConnection>> outgoingConnections =
            new Dictionary<TrafficNode, List<TrafficConnection>>();

        public int ConnectionCount { get; private set; }

        private void Start()
        {
            if (scanOnStart)
                BuildNetwork();
        }

        [ContextMenu("Build / Validate Network")]
        public void BuildNetwork()
        {
            outgoingConnections.Clear();

            TrafficConnection[] connections = FindObjectsOfType<TrafficConnection>();
            int validCount = 0;
            int invalidCount = 0;

            foreach (TrafficConnection connection in connections)
            {
                if (connection == null)
                {
                    invalidCount++;
                    Debug.LogWarning("[Traffic Network] Found a missing connection component.");
                    continue;
                }

                if (!connection.IsValidForNetwork(out string reason))
                {
                    invalidCount++;
                    Debug.LogWarning(
                        $"[Traffic Network] Connection '{connection.DisplayName}' is invalid: {reason}",
                        connection
                    );
                    continue;
                }

                TrafficNode startNode = connection.StartNode;

                if (!outgoingConnections.TryGetValue(startNode, out List<TrafficConnection> list))
                {
                    list = new List<TrafficConnection>();
                    outgoingConnections.Add(startNode, list);
                }

                list.Add(connection);
                validCount++;
            }

            ConnectionCount = validCount;

            Debug.Log(
                $"[Traffic Network] Built network with {ConnectionCount} valid connections, " +
                $"{invalidCount} invalid connections, and {outgoingConnections.Count} departure nodes.",
                this
            );

            foreach (KeyValuePair<TrafficNode, List<TrafficConnection>> entry in outgoingConnections)
            {
                Debug.Log(
                    $"[Traffic Network] Node '{entry.Key.DisplayName}' has " +
                    $"{entry.Value.Count} outgoing connection(s).",
                    entry.Key
                );
            }
        }

        public IReadOnlyList<TrafficConnection> GetOutgoingConnections(TrafficNode node)
        {
            if (node != null && outgoingConnections.TryGetValue(node, out List<TrafficConnection> list))
                return list;

            return new List<TrafficConnection>();
        }
    }
}