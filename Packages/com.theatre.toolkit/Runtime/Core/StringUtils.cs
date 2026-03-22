namespace Theatre
{
    /// <summary>
    /// Shared string utilities for the Theatre runtime.
    /// </summary>
    public static class StringUtils
    {
        /// <summary>
        /// Convert a snake_case string to PascalCase.
        /// E.g. "is_kinematic" -&gt; "IsKinematic".
        /// </summary>
        public static string ToPascalCase(string snakeCase)
        {
            if (string.IsNullOrEmpty(snakeCase)) return snakeCase;
            var parts = snakeCase.Split('_');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                    parts[i] = char.ToUpperInvariant(parts[i][0])
                             + parts[i].Substring(1);
            }
            return string.Join("", parts);
        }

        /// <summary>
        /// Generate Unity SerializedProperty name candidates for a snake_case input.
        /// Unity uses m_PascalCase internally, but MCP sends snake_case.
        /// </summary>
        public static string[] GetPropertyNameCandidates(string snakeCaseName)
        {
            var pascal = ToPascalCase(snakeCaseName);
            return new[]
            {
                snakeCaseName,           // exact match
                "m_" + pascal,           // Unity internal (m_IsKinematic)
                pascal,                  // PascalCase (IsKinematic)
                "m_" + snakeCaseName     // m_ + original (m_mass)
            };
        }
    }
}
