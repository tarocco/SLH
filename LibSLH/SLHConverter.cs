using Newtonsoft.Json;
using OpenMetaverse;
using System;

namespace LibSLH
{
    /// <summary>
    /// To address issues with automatic Int64 deserialization -- see https://stackoverflow.com/a/9444519/1037948
    /// </summary>
    public class SLHConverter : JsonConverter
    {
        #region Overrides of JsonConverter

        /// <summary>
        /// Only want to deserialize
        /// </summary>
        public override bool CanWrite { get { return true; } }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            // Unused if CanWrite == false
            writer.WriteValue(value.ToString());
        }

        /// <summary>
        /// Reads the JSON representation of the object.
        /// </summary>
        /// <param name="reader">The <see cref="T:Newtonsoft.Json.JsonReader"/> to read from.</param><param name="objectType">Type of the object.</param><param name="existingValue">The existing value of object being read.</param><param name="serializer">The calling serializer.</param>
        /// <returns>
        /// The object value.
        /// </returns>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Integer)
                return Convert.ToInt32(reader.Value);     // convert to Int32 instead of Int64
            if (reader.TokenType == JsonToken.String)
            {
                {
                    if (UUID.TryParse((string)reader.Value, out UUID result))
                        return result;
                }
                {
                    if (Vector3.TryParse((string)reader.Value, out Vector3 result))
                        return result;
                }
            }
            return serializer.Deserialize(reader);   // default to regular deserialization
        }

        /// <summary>
        /// Determines whether this instance can convert the specified object type.
        /// </summary>
        /// <param name="objectType">Type of the object.</param>
        /// <returns>
        /// <c>true</c> if this instance can convert the specified object type; otherwise, <c>false</c>.
        /// </returns>
        public override bool CanConvert(Type objectType)
        {
            var non_converting =
                objectType.IsSubclassOf(typeof(GridClient)) ||
                objectType == typeof(AgentManager);

            var converting =
                objectType == typeof(Int32) ||
                objectType == typeof(Int64) ||
                objectType == typeof(Single) ||
                objectType == typeof(Double) ||
                objectType == typeof(int) ||
                // Custom types
                objectType == typeof(UUID) ||
                objectType == typeof(Vector3) ||
                objectType.IsSubclassOf(typeof(Primitive)) ||
                // need this last one in case we "weren't given" the type
                // and this will be accounted for by `ReadJson` checking tokentype
                objectType == typeof(object);

            return !non_converting && converting;
        }

        #endregion Overrides of JsonConverter
    }
}