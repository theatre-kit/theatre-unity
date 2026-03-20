using System.Collections.Generic;

namespace Theatre
{
    /// <summary>
    /// Server configuration. Expanded in Phase 1 with tool group toggles.
    /// </summary>
    public static class TheatreConfig
    {
        /// <summary>
        /// HTTP server port. Default 9078.
        /// Read from SessionState on domain reload, falls back to default.
        /// </summary>
        public static int Port
        {
#if UNITY_EDITOR
            get => UnityEditor.SessionState.GetInt("Theatre.Port", DefaultPort);
            set => UnityEditor.SessionState.SetInt("Theatre.Port", value);
#else
            get => DefaultPort;
            set { }
#endif
        }

        public const int DefaultPort = 9078;

        /// <summary>
        /// Prefix for HttpListener. Derived from Port.
        /// </summary>
        public static string HttpPrefix => $"http://localhost:{Port}/";

        /// <summary>
        /// Which tool groups are enabled. Default: GameObjectProject
        /// (all Stage + all Director, no ECS).
        /// </summary>
        public static ToolGroup EnabledGroups
        {
#if UNITY_EDITOR
            get => (ToolGroup)UnityEditor.SessionState.GetInt(
                "Theatre.EnabledGroups", (int)DefaultEnabledGroups);
            set => UnityEditor.SessionState.SetInt(
                "Theatre.EnabledGroups", (int)value);
#else
            get => DefaultEnabledGroups;
            set { }
#endif
        }

        public const ToolGroup DefaultEnabledGroups = ToolGroup.GameObjectProject;

        /// <summary>
        /// Individually disabled tool names. Tools in this set are hidden
        /// even if their group is enabled.
        /// </summary>
        public static HashSet<string> DisabledTools { get; set; } = new();

        /// <summary>
        /// MCP protocol version we support.
        /// </summary>
        public const string ProtocolVersion = "2025-03-26";

        /// <summary>
        /// Server version string.
        /// </summary>
        public const string ServerVersion = "0.0.1";
    }
}
