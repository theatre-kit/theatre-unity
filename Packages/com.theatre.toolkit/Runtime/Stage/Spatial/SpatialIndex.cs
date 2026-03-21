using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Theatre.Stage
{
    /// <summary>
    /// Entry in the spatial index. Stores the minimum data needed for
    /// spatial queries — position, path, instance_id, and component list.
    /// </summary>
    public struct SpatialEntry
    {
        /// <summary>World position of the object.</summary>
        public Vector3 Position;

        /// <summary>Hierarchy path.</summary>
        public string Path;

        /// <summary>Unity InstanceID.</summary>
        public int InstanceId;

        /// <summary>GameObject name.</summary>
        public string Name;

        /// <summary>Component type names.</summary>
        public string[] Components;

        /// <summary>Tag.</summary>
        public string Tag;

        /// <summary>Layer name.</summary>
        public string Layer;

        /// <summary>Whether the object is active.</summary>
        public bool Active;
    }

    /// <summary>
    /// Result of a spatial query — an entry with its computed distance.
    /// </summary>
    public struct SpatialResult
    {
        /// <summary>The matched entry.</summary>
        public SpatialEntry Entry;

        /// <summary>Distance from the query origin.</summary>
        public float Distance;
    }

    /// <summary>
    /// Spatial index for transform-based queries. Uses a flat list with
    /// brute-force distance computation for scenes up to ~10K objects.
    /// For larger scenes, a grid-based spatial hash partitions the space.
    ///
    /// Built lazily from live scene state. Invalidated on scene changes.
    /// </summary>
    public sealed class SpatialIndex
    {
        private List<SpatialEntry> _entries;
        private bool _dirty = true;
        private string _sceneName;
        private int _lastBuildFrame;

        /// <summary>
        /// How often to rebuild in play mode (frames). 0 = never auto-rebuild.
        /// </summary>
        public int PlayModeRebuildInterval { get; set; } = 30;

        /// <summary>Number of entries in the index.</summary>
        public int Count => _entries?.Count ?? 0;

        /// <summary>Mark the index as needing a rebuild.</summary>
        public void Invalidate()
        {
            _dirty = true;
        }

        /// <summary>
        /// Ensure the index is up-to-date. Rebuilds if dirty or stale.
        /// </summary>
        public void EnsureFresh()
        {
            var sceneName = SceneManager.GetActiveScene().name;
            if (sceneName != _sceneName)
            {
                _dirty = true;
                _sceneName = sceneName;
            }

            if (Application.isPlaying && PlayModeRebuildInterval > 0)
            {
                if (Time.frameCount - _lastBuildFrame >= PlayModeRebuildInterval)
                    _dirty = true;
            }

            if (_dirty)
                Rebuild();
        }

        /// <summary>
        /// Find the N closest entries to a point.
        /// </summary>
        /// <param name="origin">Query center.</param>
        /// <param name="count">Maximum results.</param>
        /// <param name="maxDistance">Distance cutoff (0 = no limit).</param>
        /// <param name="filter">Optional filter predicate.</param>
        /// <returns>Results sorted by distance ascending.</returns>
        public List<SpatialResult> Nearest(
            Vector3 origin,
            int count,
            float maxDistance = 0f,
            Func<SpatialEntry, bool> filter = null)
        {
            EnsureFresh();

            var results = new List<SpatialResult>();
            if (_entries == null) return results;

            foreach (var entry in _entries)
            {
                if (filter != null && !filter(entry))
                    continue;

                float dist = Vector3.Distance(entry.Position, origin);
                if (maxDistance > 0f && dist > maxDistance)
                    continue;

                results.Add(new SpatialResult
                {
                    Entry = entry,
                    Distance = dist
                });
            }

            results.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            if (results.Count > count)
                results.RemoveRange(count, results.Count - count);

            return results;
        }

        /// <summary>
        /// Find all entries within a radius of a point.
        /// </summary>
        /// <param name="origin">Sphere center.</param>
        /// <param name="radius">Search radius.</param>
        /// <param name="filter">Optional filter predicate.</param>
        /// <param name="sortBy">Sort mode: "distance" or "name".</param>
        /// <returns>Results sorted by the specified mode.</returns>
        public List<SpatialResult> Radius(
            Vector3 origin,
            float radius,
            Func<SpatialEntry, bool> filter = null,
            string sortBy = "distance")
        {
            EnsureFresh();

            var results = new List<SpatialResult>();
            if (_entries == null) return results;

            float radiusSq = radius * radius;

            foreach (var entry in _entries)
            {
                if (filter != null && !filter(entry))
                    continue;

                float distSq = (entry.Position - origin).sqrMagnitude;
                if (distSq > radiusSq)
                    continue;

                results.Add(new SpatialResult
                {
                    Entry = entry,
                    Distance = Mathf.Sqrt(distSq)
                });
            }

            if (sortBy == "name")
                results.Sort((a, b) =>
                    string.Compare(a.Entry.Name, b.Entry.Name,
                        StringComparison.Ordinal));
            else
                results.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            return results;
        }

        /// <summary>
        /// Rebuild the index from current scene state.
        /// </summary>
        private void Rebuild()
        {
            _entries = new List<SpatialEntry>();
            var roots = ObjectResolver.GetAllRoots();

            foreach (var root in roots)
            {
                CollectRecursive(root.transform);
            }

            _dirty = false;
            _lastBuildFrame = Time.frameCount;
        }

        /// <summary>
        /// Recursively collect all active GameObjects into the index.
        /// </summary>
        private void CollectRecursive(Transform current)
        {
            if (!current.gameObject.activeInHierarchy)
                return;

            var components = current.GetComponents<Component>();
            var typeNames = new string[components.Length];
            for (int i = 0; i < components.Length; i++)
            {
                typeNames[i] = components[i] != null
                    ? components[i].GetType().Name
                    : "Missing";
            }

            #pragma warning disable CS0618
            _entries.Add(new SpatialEntry
            {
                Position = current.position,
                Path = ResponseHelpers.GetHierarchyPath(current),
                InstanceId = current.gameObject.GetInstanceID(),
                Name = current.name,
                Components = typeNames,
                Tag = current.gameObject.tag,
                Layer = LayerMask.LayerToName(current.gameObject.layer),
                Active = true
            });
            #pragma warning restore CS0618

            for (int i = 0; i < current.childCount; i++)
            {
                CollectRecursive(current.GetChild(i));
            }
        }
    }
}
