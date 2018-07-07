using OpenMetaverse;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace LibSLH
{
    class SLHConverter : TypeConverter
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

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
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