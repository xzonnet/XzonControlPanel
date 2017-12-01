using System;
using System.Configuration;

namespace XzonControlPanel.Config
{
    public static class ConfigExtensionMethods
    {
        public static bool? ToBool(this string input)
        {
            bool output;
            return bool.TryParse(input, out output) ? output : (bool?)null;
        }

        public static int? ToInt(this string input)
        {
            int output;
            return int.TryParse(input, out output) ? output : (int?)null;
        }
        public static double? ToDouble(this string input)
        {
            double output;
            return double.TryParse(input, out output) ? output : (double?)null;
        }
        public static decimal? ToDecimal(this string input)
        {
            decimal output;
            return decimal.TryParse(input, out output) ? output : (decimal?)null;
        }
        public static string FromConfig(this string propertyName, bool isRequired = false)
        {
            var result = ConfigurationManager.AppSettings[propertyName];
            if (result != null || !isRequired)
            {
                return result;
            }

            throw new ArgumentException($"Config value must be set {propertyName}", propertyName);
        }
    }
}
