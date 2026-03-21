using System;
using System.Linq;

namespace Theatre.Stage
{
    /// <summary>
    /// Builds filter predicates for spatial index queries.
    /// Centralizes the include_components / exclude_tags filtering logic
    /// shared by nearest and radius query handlers.
    /// </summary>
    public static class SpatialEntryFilter
    {
        /// <summary>
        /// Build a filter predicate for <see cref="SpatialEntry"/> values.
        /// Returns null if both arguments are null (no filtering required).
        /// The predicate returns false for any entry that should be excluded.
        /// </summary>
        /// <param name="includeComponents">
        /// Component type names that must all be present on the entry (case-insensitive).
        /// Pass null to skip this check.
        /// </param>
        /// <param name="excludeTags">
        /// Tag names that must NOT be present on the entry (case-insensitive).
        /// Pass null to skip this check.
        /// </param>
        public static Func<SpatialEntry, bool> Build(
            string[] includeComponents, string[] excludeTags)
        {
            if (includeComponents == null && excludeTags == null)
                return null;

            return entry =>
            {
                if (excludeTags != null)
                {
                    if (excludeTags.Any(tag => string.Equals(entry.Tag, tag,
                        StringComparison.OrdinalIgnoreCase)))
                        return false;
                }
                if (includeComponents != null)
                {
                    foreach (var required in includeComponents)
                    {
                        if (entry.Components == null ||
                            !entry.Components.Any(comp => string.Equals(comp, required,
                                StringComparison.OrdinalIgnoreCase)))
                            return false;
                    }
                }
                return true;
            };
        }
    }
}
