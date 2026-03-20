using Newtonsoft.Json;

namespace Theatre.Transport
{
    /// <summary>
    /// Parsed JSON-RPC 2.0 message. Can be a request (has id + method),
    /// notification (has method, no id), or response (has id + result/error).
    /// </summary>
    public sealed class JsonRpcMessage
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public Newtonsoft.Json.Linq.JToken Id { get; set; }

        [JsonProperty("method", NullValueHandling = NullValueHandling.Ignore)]
        public string Method { get; set; }

        [JsonProperty("params", NullValueHandling = NullValueHandling.Ignore)]
        public Newtonsoft.Json.Linq.JToken Params { get; set; }

        [JsonProperty("result", NullValueHandling = NullValueHandling.Ignore)]
        public Newtonsoft.Json.Linq.JToken Result { get; set; }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public JsonRpcError Error { get; set; }

        /// <summary>True if this message has an id (request or response).</summary>
        public bool IsRequest => Id != null && Method != null;

        /// <summary>True if this is a notification (method but no id).</summary>
        public bool IsNotification => Id == null && Method != null;
    }

    /// <summary>
    /// JSON-RPC 2.0 error object.
    /// </summary>
    public sealed class JsonRpcError
    {
        [JsonProperty("code")]
        public int Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public Newtonsoft.Json.Linq.JToken Data { get; set; }
    }

    /// <summary>
    /// Factory methods for creating JSON-RPC responses.
    /// </summary>
    public static class JsonRpcResponse
    {
        /// <summary>Create a success response with a pre-serialized result.</summary>
        public static JsonRpcMessage Success(
            Newtonsoft.Json.Linq.JToken id, Newtonsoft.Json.Linq.JToken result)
        {
            return new JsonRpcMessage
            {
                Id = id,
                Result = result
            };
        }

        /// <summary>Create an error response.</summary>
        public static JsonRpcMessage ErrorResponse(
            Newtonsoft.Json.Linq.JToken id, int code, string message,
            Newtonsoft.Json.Linq.JToken data = null)
        {
            return new JsonRpcMessage
            {
                Id = id,
                Error = new JsonRpcError
                {
                    Code = code,
                    Message = message,
                    Data = data
                }
            };
        }

        /// <summary>Create a notification (no id).</summary>
        public static JsonRpcMessage Notification(
            string method, Newtonsoft.Json.Linq.JToken @params = null)
        {
            return new JsonRpcMessage
            {
                Method = method,
                Params = @params
            };
        }

        // Standard JSON-RPC error codes
        public const int ParseError = -32700;
        public const int InvalidRequest = -32600;
        public const int MethodNotFound = -32601;
        public const int InvalidParams = -32602;
        public const int InternalError = -32603;
    }
}
