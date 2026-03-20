using System;
using System.Collections.Generic;
using UnityEngine;

namespace Theatre.Stage
{
    /// <summary>
    /// A cluster of nearby GameObjects for snapshot summaries.
    /// </summary>
    public struct ObjectCluster
    {
        /// <summary>
        /// Label for this cluster (common parent name or dominant component type).
        /// </summary>
        public string Label;

        /// <summary>Center position (average of member positions).</summary>
        public Vector3 Center;

        /// <summary>Spread — maximum distance from center to any member.</summary>
        public float Spread;

        /// <summary>Number of objects in this cluster.</summary>
        public int Count;
    }

    /// <summary>
    /// Grid-based spatial clustering for snapshot summaries.
    /// Divides space into cells and groups objects that share a cell.
    /// </summary>
    public static class Clustering
    {
        /// <summary>Minimum cell size in world units.</summary>
        public const float MinCellSize = 5f;

        /// <summary>Number of grid divisions per axis.</summary>
        public const int GridDivisions = 8;

        /// <summary>
        /// Minimum cluster size. Clusters with fewer objects are not grouped.
        /// </summary>
        public const int MinClusterSize = 2;

        /// <summary>
        /// Cluster a set of hierarchy entries by spatial proximity.
        /// </summary>
        /// <param name="entries">Objects to cluster.</param>
        /// <returns>
        /// List of clusters (only groups of MinClusterSize or more).
        /// Singleton objects are not included in clusters.
        /// </returns>
        public static List<ObjectCluster> Compute(List<HierarchyEntry> entries)
        {
            if (entries == null || entries.Count < MinClusterSize)
                return new List<ObjectCluster>();

            // Compute AABB
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            foreach (var entry in entries)
            {
                min = Vector3.Min(min, entry.Position);
                max = Vector3.Max(max, entry.Position);
            }

            var extent = max - min;
            var cellSize = new Vector3(
                Mathf.Max(extent.x / GridDivisions, MinCellSize),
                Mathf.Max(extent.y / GridDivisions, MinCellSize),
                Mathf.Max(extent.z / GridDivisions, MinCellSize)
            );

            // Assign objects to grid cells
            var cells = new Dictionary<(int, int, int), List<HierarchyEntry>>();

            foreach (var entry in entries)
            {
                var offset = entry.Position - min;
                var key = (
                    (int)(offset.x / cellSize.x),
                    (int)(offset.y / cellSize.y),
                    (int)(offset.z / cellSize.z)
                );

                if (!cells.TryGetValue(key, out var cell))
                {
                    cell = new List<HierarchyEntry>();
                    cells[key] = cell;
                }
                cell.Add(entry);
            }

            // Build clusters from cells with enough objects
            var clusters = new List<ObjectCluster>();

            foreach (var cell in cells.Values)
            {
                if (cell.Count < MinClusterSize) continue;

                var center = Vector3.zero;
                foreach (var entry in cell)
                    center += entry.Position;
                center /= cell.Count;

                float spread = 0f;
                foreach (var entry in cell)
                {
                    float d = Vector3.Distance(entry.Position, center);
                    if (d > spread) spread = d;
                }

                clusters.Add(new ObjectCluster
                {
                    Label = DeriveLabel(cell),
                    Center = center,
                    Spread = spread,
                    Count = cell.Count
                });
            }

            return clusters;
        }

        /// <summary>
        /// Determine which entries are not part of any cluster.
        /// Returns entries that are in cells with fewer than MinClusterSize objects.
        /// </summary>
        public static List<HierarchyEntry> GetUnclustered(
            List<HierarchyEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return new List<HierarchyEntry>();

            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            foreach (var entry in entries)
            {
                min = Vector3.Min(min, entry.Position);
                max = Vector3.Max(max, entry.Position);
            }

            var extent = max - min;
            var cellSize = new Vector3(
                Mathf.Max(extent.x / GridDivisions, MinCellSize),
                Mathf.Max(extent.y / GridDivisions, MinCellSize),
                Mathf.Max(extent.z / GridDivisions, MinCellSize)
            );

            var cells = new Dictionary<(int, int, int), List<HierarchyEntry>>();

            foreach (var entry in entries)
            {
                var offset = entry.Position - min;
                var key = (
                    (int)(offset.x / cellSize.x),
                    (int)(offset.y / cellSize.y),
                    (int)(offset.z / cellSize.z)
                );

                if (!cells.TryGetValue(key, out var cell))
                {
                    cell = new List<HierarchyEntry>();
                    cells[key] = cell;
                }
                cell.Add(entry);
            }

            var unclustered = new List<HierarchyEntry>();
            foreach (var cell in cells.Values)
            {
                if (cell.Count < MinClusterSize)
                    unclustered.AddRange(cell);
            }

            return unclustered;
        }

        /// <summary>
        /// Derive a human-readable label for a cluster.
        /// Strategy: find the most common parent name or most common component type.
        /// </summary>
        private static string DeriveLabel(List<HierarchyEntry> entries)
        {
            // Try to find a common parent name
            var parentCounts = new Dictionary<string, int>();
            foreach (var entry in entries)
            {
                if (entry.Path == null) continue;
                var lastSlash = entry.Path.LastIndexOf('/');
                if (lastSlash > 0)
                {
                    var parentSegment = entry.Path.Substring(0, lastSlash);
                    var parentSlash = parentSegment.LastIndexOf('/');
                    var parentName = parentSlash >= 0
                        ? parentSegment.Substring(parentSlash + 1)
                        : parentSegment;

                    if (!parentCounts.ContainsKey(parentName))
                        parentCounts[parentName] = 0;
                    parentCounts[parentName]++;
                }
            }

            // Find the most common parent
            string bestParent = null;
            int bestCount = 0;

            foreach (var kvp in parentCounts)
            {
                if (kvp.Value > bestCount)
                {
                    bestCount = kvp.Value;
                    bestParent = kvp.Key;
                }
            }

            if (bestParent != null && bestCount >= entries.Count / 2)
                return $"{bestParent} ({entries.Count})";

            // Fall back to most common non-Transform component
            var componentCounts = new Dictionary<string, int>();
            foreach (var entry in entries)
            {
                if (entry.Components == null) continue;
                foreach (var comp in entry.Components)
                {
                    if (comp == "Transform") continue;
                    if (!componentCounts.ContainsKey(comp))
                        componentCounts[comp] = 0;
                    componentCounts[comp]++;
                }
            }

            string bestComponent = null;
            bestCount = 0;
            foreach (var kvp in componentCounts)
            {
                if (kvp.Value > bestCount)
                {
                    bestCount = kvp.Value;
                    bestComponent = kvp.Key;
                }
            }

            if (bestComponent != null)
                return $"{bestComponent} ({entries.Count})";

            return $"Group ({entries.Count})";
        }
    }
}
