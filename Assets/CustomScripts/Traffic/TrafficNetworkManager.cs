using System.Collections.Generic;
using UnityEngine;

namespace CustomTraffic
{
    /// <summary>
    /// Collects directed lane connections and identifies which connections
    /// can be taken next from a given traffic node.
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

            foreach (TrafficConnection connection in connections)
            {
                if (connection == null || !connection.IsConfigured)
                {
                    Debug.LogWarning(
                        "[Traffic Network] Found a connection with missing references.",
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
                $"[Traffic Network] Built network with {ConnectionCount} valid lane connections " +
                $"and {outgoingConnections.Count} departure nodes.",
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