namespace Theatre
{
    /// <summary>
    /// Server configuration. Phase 0: port only.
    /// Later phases add ToolGroup flags, disabled tools, recording settings.
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
    }
}
