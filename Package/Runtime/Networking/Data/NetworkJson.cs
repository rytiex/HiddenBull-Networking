using Newtonsoft.Json;

namespace HiddenBull.Networking.Data
{
    /// <summary>
    /// JSON for persisted config/moderation data. Mirrors the project's previous serializer
    /// settings so existing files round-trip; replaces the XUtils dependency with Newtonsoft directly.
    /// </summary>
    internal static class NetworkJson
    {
        private static readonly JsonSerializerSettings Settings = new()
        {
            TypeNameHandling = TypeNameHandling.Auto,
            NullValueHandling = NullValueHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        };

        public static string ToJson(object obj) => JsonConvert.SerializeObject(obj, Formatting.Indented, Settings);

        public static T FromJson<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return default;
            try { return JsonConvert.DeserializeObject<T>(json, Settings); }
            catch (JsonException ex)
            {
                UnityEngine.Debug.LogError($"[NetworkJson] Deserialize<{typeof(T).Name}> failed: {ex.Message}");
                return default;
            }
        }
    }
}