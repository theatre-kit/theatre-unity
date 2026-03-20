using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Theatre.Stage
{
    /// <summary>
    /// Entry for a single GameObject in hierarchy traversal results.
    /// </summary>
    public struct HierarchyEntry
    {
        /// <summary>Full hierarchy path.</summary>
        public string Path;

        /// <summary>Unity InstanceID.</summary>
        public int InstanceId;

        /// <summary>GameObject name.</summary>
        public string Name;

        /// <summary>Number of direct children.</summary>
        public int ChildrenCount;

        /// <summary>Whether the object is active in hierarchy.</summary>
        public bool Active;

        /// <summary>Component type names on this object.</summary>
        public string[] Components;

        /// <summary>World position (if Transform present).</summary>
        public Vector3 Position;

        /// <summary>Distance from a reference point (optional).</summary>
        public float Distance;
    }

    /// <summary>
    /// Filter criteria for hierarchy traversal.
    /// </summary>
    public sealed class HierarchyFilter
    {
        /// <summary>Name pattern for find operations (glob: "*Door*").</summary>
        public string NamePattern;

        /// <summary>Required component type names.</summary>
        public string[] RequiredComponents;

        /// <summary>Required tag.</summary>
        public string Tag;

        /// <summary>Required layer name.</summary>
        public string Layer;

        /// <summary>Include inactive GameObjects.</summary>
        public bool IncludeInactive;

        /// <summary>Maximum hierarchy depth to traverse.</summary>
        public int MaxDepth = int.MaxValue;
    }

    /// <summary>
    /// Walks the Unity Transform hierarchy with filtering, depth limits,
    /// and pagination support.
    /// </summary>
    public static class HierarchyWalker
    {
        /// <summary>
        /// List direct children of a path (or scene roots if path is null).
        /// </summary>
        /// <param name="parentPath">
        /// Parent path to list children of. Null for root level.
        /// </param>
        /// <param name="includeInactive">Include disabled GameObjects.</param>
        /// <param name="offset">Pagination offset (skip this many).</param>
        /// <param name="limit">Maximum results to return.</param>
        /// <returns>List of child entries and total count.</returns>
        public static (List<HierarchyEntry> Entries, int Total) ListChildren(
            string parentPath,
            bool includeInactive = false,
            int offset = 0,
            int limit = 50)
        {
            var entries = new List<HierarchyEntry>();

            if (parentPath == null)
            {
                // Root level — list loaded scenes (or prefab root)
                return ListSceneRoots(includeInactive, offset, limit);
            }

            var resolved = ObjectResolver.Resolve(path: parentPath);
            if (!resolved.Success)
                return (entries, 0);

            var parent = resolved.GameObject.transform;
            int total = parent.childCount;
            int end = Math.Min(offset + limit, total);

            for (int i = offset; i < end; i++)
            {
                var child = parent.GetChild(i);
                if (!includeInactive && !child.gameObject.activeInHierarchy)
                    continue;
                entries.Add(BuildEntry(child));
            }

            return (entries, total);
        }

        /// <summary>
        /// Find GameObjects by name pattern (glob matching).
        /// Searches the entire hierarchy or a subtree.
        /// </summary>
        /// <param name="pattern">
        /// Glob pattern: "*" matches any characters, "?" matches one character.
        /// Examples: "Scout*", "*Door*", "Enemy_??"
        /// </param>
        /// <param name="root">Optional subtree root to limit search.</param>
        /// <param name="includeInactive">Include disabled objects.</param>
        /// <param name="maxResults">Maximum results to return.</param>
        /// <returns>Matching entries.</returns>
        public static List<HierarchyEntry> Find(
            string pattern,
            string root = null,
            bool includeInactive = false,
            int maxResults = 50)
        {
            var regex = GlobToRegex(pattern);
            var results = new List<HierarchyEntry>();
            var searchRoots = GetSearchRoots(root);

            foreach (var searchRoot in searchRoots)
            {
                FindRecursive(searchRoot, regex, includeInactive,
                    maxResults, results);
                if (results.Count >= maxResults) break;
            }

            return results;
        }

        /// <summary>
        /// Search GameObjects by component type, tag, or layer.
        /// </summary>
        /// <param name="filter">Search criteria.</param>
        /// <param name="root">Optional subtree root.</param>
        /// <param name="maxResults">Maximum results to return.</param>
        /// <returns>Matching entries.</returns>
        public static List<HierarchyEntry> Search(
            HierarchyFilter filter,
            string root = null,
            int maxResults = 50)
        {
            var results = new List<HierarchyEntry>();
            var searchRoots = GetSearchRoots(root);

            foreach (var searchRoot in searchRoots)
            {
                SearchRecursive(searchRoot, filter, maxResults, results);
                if (results.Count >= maxResults) break;
            }

            return results;
        }

        /// <summary>
        /// Walk the hierarchy from a root, collecting entries up to a depth limit.
        /// Used by scene_snapshot to gather objects for budgeted output.
        /// </summary>
        /// <param name="roots">Root transforms to walk from.</param>
        /// <param name="maxDepth">Maximum depth to traverse.</param>
        /// <param name="excludeInactive">Skip inactive objects.</param>
        /// <param name="focus">Optional focus point for distance calculation.</param>
        /// <param name="radius">Optional radius limit from focus.</param>
        /// <returns>All entries within the constraints.</returns>
        public static List<HierarchyEntry> Walk(
            List<Transform> roots,
            int maxDepth = 3,
            bool excludeInactive = true,
            Vector3? focus = null,
            float? radius = null)
        {
            var entries = new List<HierarchyEntry>();

            foreach (var root in roots)
            {
                WalkRecursive(root, 0, maxDepth, excludeInactive,
                    focus, radius, entries);
            }

            return entries;
        }

        // --- Private helpers ---

        private static (List<HierarchyEntry>, int) ListSceneRoots(
            bool includeInactive, int offset, int limit)
        {
            var allRoots = ObjectResolver.GetAllRoots();
            var entries = new List<HierarchyEntry>();
            int total = allRoots.Count;
            int end = Math.Min(offset + limit, total);

            for (int i = offset; i < end; i++)
            {
                if (!includeInactive && !allRoots[i].activeInHierarchy)
                    continue;
                entries.Add(BuildEntry(allRoots[i].transform));
            }

            return (entries, total);
        }

        private static HierarchyEntry BuildEntry(Transform t)
        {
            var components = t.GetComponents<Component>();
            var typeNames = new string[components.Length];
            for (int i = 0; i < components.Length; i++)
            {
                typeNames[i] = components[i] != null
                    ? components[i].GetType().Name
                    : "Missing";
            }

            return new HierarchyEntry
            {
                Path = ResponseHelpers.GetHierarchyPath(t),
                InstanceId = t.gameObject.GetEntityId(),
                Name = t.name,
                ChildrenCount = t.childCount,
                Active = t.gameObject.activeInHierarchy,
                Components = typeNames,
                Position = t.position
            };
        }

        private static void WalkRecursive(
            Transform current, int depth, int maxDepth,
            bool excludeInactive, Vector3? focus, float? radius,
            List<HierarchyEntry> entries)
        {
            if (excludeInactive && !current.gameObject.activeInHierarchy)
                return;

            float distance = 0f;
            if (focus.HasValue)
            {
                distance = Vector3.Distance(current.position, focus.Value);
                if (radius.HasValue && distance > radius.Value)
                    return;
            }

            var entry = BuildEntry(current);
            entry.Distance = distance;
            entries.Add(entry);

            if (depth < maxDepth)
            {
                for (int i = 0; i < current.childCount; i++)
                {
                    WalkRecursive(current.GetChild(i), depth + 1, maxDepth,
                        excludeInactive, focus, radius, entries);
                }
            }
        }

        private static void FindRecursive(
            Transform current, Regex pattern, bool includeInactive,
            int maxResults, List<HierarchyEntry> results)
        {
            if (results.Count >= maxResults) return;
            if (!includeInactive && !current.gameObject.activeInHierarchy)
                return;

            if (pattern.IsMatch(current.name))
                results.Add(BuildEntry(current));

            for (int i = 0; i < current.childCount; i++)
            {
                FindRecursive(current.GetChild(i), pattern,
                    includeInactive, maxResults, results);
            }
        }

        private static void SearchRecursive(
            Transform current, HierarchyFilter filter,
            int maxResults, List<HierarchyEntry> results)
        {
            if (results.Count >= maxResults) return;
            if (!filter.IncludeInactive && !current.gameObject.activeInHierarchy)
                return;

            bool matches = true;
            var go = current.gameObject;

            if (filter.Tag != null && !go.CompareTag(filter.Tag))
                matches = false;

            if (matches && filter.Layer != null
                && LayerMask.LayerToName(go.layer) != filter.Layer)
                matches = false;

            if (matches && filter.RequiredComponents != null)
            {
                foreach (var typeName in filter.RequiredComponents)
                {
                    if (go.GetComponent(typeName) == null)
                    {
                        matches = false;
                        break;
                    }
                }
            }

            if (matches)
                results.Add(BuildEntry(current));

            for (int i = 0; i < current.childCount; i++)
            {
                SearchRecursive(current.GetChild(i), filter,
                    maxResults, results);
            }
        }

        private static List<Transform> GetSearchRoots(string root)
        {
            var roots = new List<Transform>();
            if (root != null)
            {
                var resolved = ObjectResolver.Resolve(path: root);
                if (resolved.Success)
                    roots.Add(resolved.GameObject.transform);
            }
            else
            {
                foreach (var go in ObjectResolver.GetAllRoots())
                    roots.Add(go.transform);
            }
            return roots;
        }

        /// <summary>
        /// Convert a glob pattern to a Regex.
        /// "*" becomes ".*", "?" becomes ".".
        /// </summary>
        private static Regex GlobToRegex(string pattern)
        {
            var escaped = Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".");
            return new Regex("^" + escaped + "$", RegexOptions.IgnoreCase);
        }
    }
}
