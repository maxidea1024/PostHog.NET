using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PostHog.Model
{
    public class Batch
    {
        [JsonPropertyName(name: "batch")]
        public List<BaseAction> Actions { get; set; }

        // TODO: 따로 넣어주는게 좋을듯한데?
        [JsonPropertyName(name: "api_key")]
        public string ApiKey { get; set; }

        public Batch(List<BaseAction> actions, string apiKey)
        {
            Actions = actions;
            ApiKey = apiKey;
        }
    }
}
