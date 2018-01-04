using System;
using LitJson;

namespace SLHBot
{
    public static class JSONUtility
    {
        public static bool TryGetValue(this JsonData json, string key, out string value)
        {
            try
            {
                value = (string)json[key];
                return true;
            }
            catch
            {
                value = default(string);
                return false;
            }
        }

        public static bool TryGetValue(this JsonData json, int index, out string value)
        {
            try
            {
                value = (string)json[index];
                return true;
            }
            catch
            {
                value = default(string);
                return false;
            }
        }

        public static bool TryGetValue(this JsonData json, string key, out int value)
        {
            try
            {
                value = (int)json[key];
                return true;
            }
            catch
            {
                value = default(int);
                return false;
            }
        }

        public static bool TryGetValue(this JsonData json, int index, out int value)
        {
            try
            {
                value = (int)json[index];
                return true;
            }
            catch
            {
                value = default(int);
                return false;
            }
        }

        public static bool TryGetValue(this JsonData json, string key, out long value)
        {
            try
            {
                value = (long)json[key];
                return true;
            }
            catch
            {
                value = default(long);
                return false;
            }
        }

        public static bool TryGetValue(this JsonData json, int index, out long value)
        {
            try
            {
                value = (long)json[index];
                return true;
            }
            catch
            {
                value = default(long);
                return false;
            }
        }

        public static bool TryGetValue(this JsonData json, string key, out float value)
        {
            try
            {
                value = (float)json[key];
                return true;
            }
            catch
            {
                value = default(float);
                return false;
            }
        }

        public static bool TryGetValue(this JsonData json, int index, out float value)
        {
            try
            {
                value = (float)json[index];
                return true;
            }
            catch
            {
                value = default(float);
                return false;
            }
        }

        public static bool TryGetValue(this JsonData json, string key, out double value)
        {
            try
            {
                value = (double)json[key];
                return true;
            }
            catch
            {
                value = default(double);
                return false;
            }
        }

        public static bool TryGetValue(this JsonData json, int index, out double value)
        {
            try
            {
                value = (double)json[index];
                return true;
            }
            catch
            {
                value = default(double);
                return false;
            }
        }

        public static bool TryGetValue(this JsonData json, string key, out JsonData value)
        {
            try
            {
                value = (JsonData)json[key];
                return true;
            }
            catch
            {
                value = default(JsonData);
                return false;
            }
        }

        public static bool TryGetValue(this JsonData json, int index, out JsonData value)
        {
            try
            {
                value = (JsonData)json[index];
                return true;
            }
            catch
            {
                value = default(JsonData);
                return false;
            }
        }
    }
}