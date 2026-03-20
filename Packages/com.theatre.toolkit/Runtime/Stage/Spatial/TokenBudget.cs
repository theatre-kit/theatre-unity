using System;
using Newtonsoft.Json.Linq;

namespace Theatre.Stage
{
    /// <summary>
    /// Token budget metadata included in budgeted responses.
    /// </summary>
    public struct BudgetInfo
    {
        /// <summary>The budget the caller requested.</summary>
        public int Requested;

        /// <summary>Estimated tokens actually used.</summary>
        public int Used;

        /// <summary>Whether the response was truncated to fit.</summary>
        public bool Truncated;

        /// <summary>Reason for truncation, if truncated.</summary>
        public string TruncationReason;

        /// <summary>Suggestion for the agent when truncated.</summary>
        public string Suggestion;
    }

    /// <summary>
    /// Token budgeting engine. Estimates response size in tokens and manages
    /// truncation. Uses a simple heuristic: 1 token ~= 4 characters of JSON.
    /// </summary>
    public sealed class TokenBudget
    {
        /// <summary>Hard cap — responses never exceed this regardless of requested budget.</summary>
        public const int HardCap = 4000;

        /// <summary>Default budget when not specified by caller.</summary>
        public const int DefaultBudget = 1500;

        private readonly int _budget;
        private int _charCount;

        /// <summary>
        /// Create a new budget tracker.
        /// </summary>
        /// <param name="requestedBudget">Requested token budget. Clamped to hard cap.</param>
        public TokenBudget(int requestedBudget)
        {
            _budget = Math.Min(Math.Max(requestedBudget, 100), HardCap);
            _charCount = 0;
        }

        /// <summary>
        /// The effective budget (clamped to hard cap).
        /// </summary>
        public int Budget => _budget;

        /// <summary>
        /// Estimated tokens consumed so far.
        /// </summary>
        public int EstimatedTokens => _charCount / 4;

        /// <summary>
        /// Remaining token capacity.
        /// </summary>
        public int Remaining => Math.Max(0, _budget - EstimatedTokens);

        /// <summary>
        /// Whether the budget has been exhausted.
        /// </summary>
        public bool IsExhausted => EstimatedTokens >= _budget;

        /// <summary>
        /// Check if adding content of the given character count would
        /// exceed the budget.
        /// </summary>
        /// <param name="additionalChars">Number of JSON characters to add.</param>
        /// <returns>True if adding this content would exceed the budget.</returns>
        public bool WouldExceed(int additionalChars)
        {
            return (_charCount + additionalChars) / 4 >= _budget;
        }

        /// <summary>
        /// Record that characters were added to the response.
        /// </summary>
        /// <param name="chars">Character count added.</param>
        public void Add(int chars)
        {
            _charCount += chars;
        }

        /// <summary>
        /// Estimate the token cost of a JSON string.
        /// </summary>
        public static int EstimateTokens(string json)
        {
            return (json?.Length ?? 0) / 4;
        }

        /// <summary>
        /// Estimate the token cost of a single HierarchyEntry when serialized.
        /// This is an approximation — actual size depends on component count,
        /// path length, etc. Used for pre-flight budget checks.
        /// </summary>
        /// <param name="entry">The entry to estimate.</param>
        /// <returns>Estimated token count.</returns>
        public static int EstimateEntryTokens(HierarchyEntry entry)
        {
            // Base: {"path":"...","instance_id":...,"position":[...],...} ~= 120 chars
            int chars = 120;
            chars += entry.Path?.Length ?? 0;
            chars += entry.Name?.Length ?? 0;
            if (entry.Components != null)
            {
                // Component array: ["Type1","Type2"] ~= 12 chars per component
                chars += entry.Components.Length * 12;
            }
            return chars / 4;
        }

        /// <summary>
        /// Build the budget metadata for inclusion in the response.
        /// </summary>
        /// <param name="wasTruncated">Whether content was omitted.</param>
        /// <param name="reason">Truncation reason if truncated.</param>
        /// <param name="suggestion">Recovery suggestion if truncated.</param>
        /// <returns>BudgetInfo to serialize into the response.</returns>
        public BudgetInfo ToBudgetInfo(
            bool wasTruncated = false,
            string reason = null,
            string suggestion = null)
        {
            return new BudgetInfo
            {
                Requested = _budget,
                Used = EstimatedTokens,
                Truncated = wasTruncated,
                TruncationReason = reason,
                Suggestion = suggestion
            };
        }

        /// <summary>
        /// Build a JObject containing budget metadata.
        /// </summary>
        public JObject ToBudgetJObject(
            bool truncated = false,
            string reason = null,
            string suggestion = null)
        {
            var obj = new JObject();
            obj["requested"] = _budget;
            obj["used"] = EstimatedTokens;
            obj["truncated"] = truncated;
            if (truncated)
            {
                if (reason != null)
                    obj["truncation_reason"] = reason;
                if (suggestion != null)
                    obj["suggestion"] = suggestion;
            }
            return obj;
        }
    }
}
