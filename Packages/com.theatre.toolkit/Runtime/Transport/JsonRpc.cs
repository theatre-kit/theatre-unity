using System.Text.Json;
using System.Text.Json.Serialization;

namespace Theatre.Transport
{
    /// <summary>
    /// Parsed JSON-RPC 2.0 message. Can be a request (has id + method),
    /// notification (has method, no id), or response (has id + result/error).
    /// </summary>
    public sealed class JsonRpcMessage
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonPropertyName("id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonElement? Id { get; set; }

        [JsonPropertyName("method")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Method { get; set; }

        [JsonPropertyName("params")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonElement? Params { get; set; }

        [JsonPropertyName("result")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonElement? Result { get; set; }

        [JsonPropertyName("error")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonRpcError Error { get; set; }

        /// <summary>True if this message has an id (request or response).</summary>
        public bool IsRequest => Id.HasValue && Method != null;

        /// <summary>True if this is a notification (method but no id).</summary>
        public bool IsNotification => !Id.HasValue && Method != null;
    }

    /// <summary>
    /// JSON-RPC 2.0 error object.
    /// </summary>
    public sealed class JsonRpcError
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonElement? Data { get; set; }
    }

    /// <summary>
    /// Factory methods for creating JSON-RPC responses.
    /// </summary>
    public static class JsonRpcResponse
    {
        /// <summary>Create a success response with a pre-serialized result.</summary>
        public static JsonRpcMessage Success(JsonElement? id, JsonElement result)
        {
            return new JsonRpcMessage
            {
                Id = id,
                Result = result
            };
        }

        /// <summary>Create an error response.</summary>
        public static JsonRpcMessage ErrorResponse(
            JsonElement? id, int code, string message, JsonElement? data = null)
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
        public static JsonRpcMessage Notification(string method, JsonElement? @params = null)
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
