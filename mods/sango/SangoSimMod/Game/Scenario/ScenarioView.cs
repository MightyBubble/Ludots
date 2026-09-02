using System.IO;
using TKNewtonsoft.Json;
using UnityEngine;

namespace Sango.Core
{
    [JsonObject(MemberSerialization.OptIn)]
    public class ScenarioView
    {
        [JsonProperty]
        [JsonConverter(typeof(Vector3Converter))]
        public Vector3 cameraPosition;
        [JsonProperty]
        [JsonConverter(typeof(Vector3Converter))]
        public Vector3 cameraRotation;
        [JsonProperty]
        public float cameraDistance;
    }
}
