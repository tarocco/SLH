using Newtonsoft.Json.Linq;
using OpenMetaverse;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace LibSLH
{
    internal class SLHConverter : TypeConverter
    {
        private readonly List<SLHClient> _Clients = new List<SLHClient>();

        /// <summary>
        /// Thread safe access to <see cref="_Clients"/>
        /// </summary>
        public IEnumerable<SLHClient> Clients
        {
            get
            {
                lock (_Clients)
                    return _Clients.ToArray();
            }
        }

        public void AddClient(SLHClient client)
        {
            lock (_Clients)
                _Clients.Add(client);
        }

        public bool RemoveClient(SLHClient client)
        {
            lock (_Clients)
                return _Clients.Remove(client);
        }

        protected IEnumerable<Simulator> ClientSimulators => Clients.SelectMany(c => c.Network.Simulators);

        public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
        {
            var can_convert =
                destinationType.IsEnum ||
                destinationType == typeof(Simulator) ||
                destinationType == typeof(Int32) ||
                destinationType == typeof(Int64) ||
                destinationType == typeof(UInt32) ||
                destinationType == typeof(UInt64);
            return can_convert || base.CanConvertTo(context, destinationType);
        }

        private bool TryConvertJArray(JArray j_array, out object array_object)
        {
            if (j_array.HasValues)
            {
                var element_type = j_array.First.Type;
                switch (element_type)
                {
                    default:
                    case JTokenType.None:
                    case JTokenType.Array: // TODO
                    case JTokenType.Constructor:
                    case JTokenType.Property:
                    case JTokenType.Comment:
                    case JTokenType.Undefined:
                    case JTokenType.Date: // TODO
                    case JTokenType.Raw:
                    case JTokenType.Bytes: // TODO?
                    case JTokenType.Guid: // TODO? UUID?
                    case JTokenType.Uri: // TODO?
                    case JTokenType.TimeSpan: // TODO
                        array_object = null;
                        return false;
                    case JTokenType.Object:
                    case JTokenType.Null: // object
                        array_object = j_array.Values<object>().ToArray();
                        return true;
                    case JTokenType.Integer:
                        array_object = j_array.Values<int>().ToArray();
                        return true;
                    case JTokenType.Float:
                        array_object = j_array.Values<float>().ToArray();
                        return true;
                    case JTokenType.String:
                        array_object = j_array.Values<string>().ToArray();
                        return true;
                    case JTokenType.Boolean:
                        array_object = j_array.Values<bool>().ToArray();
                        return true;
                }
            }
            array_object = new object[] { };
            return false;
        }

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            if (value is JArray j_array)
            {
                if (TryConvertJArray(j_array, out object array_object))
                {
                    return ConvertTo(context, culture, array_object, destinationType);
                }
            }
            if (destinationType.IsEnum)
            {
                if (value is string)
                    return Enum.Parse(destinationType, (string)value);
                return Enum.ToObject(destinationType, value);
            }

            unchecked
            {
                if (destinationType == typeof(Simulator) && (value is Int64 || value is UInt64))
                {
                    if (Utility.Fudge(value, out UInt64 handle))
                        return ClientSimulators.FirstOrDefault(s => s.Handle == handle);
                }

                if (value is float[] floats)
                {
                    if (destinationType == typeof(Vector3))
                        return new Vector3(floats[0], floats[1], floats[2]);
                    if (destinationType == typeof(Vector3d))
                        return new Vector3d(floats[0], floats[1], floats[2]);
                }

                if (Utility.Fudge(value, out object output, destinationType))
                    return output;
            }

            // Try it the other way around!
            var converter = TypeDescriptor.GetConverter(destinationType);
            if (converter.CanConvertFrom(value.GetType()))
                return converter.ConvertFrom(value);

            return value;

            //return base.ConvertTo(context, culture, value, destinationType);
        }
    }
}