using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace Hepta
{
    internal static class Locale
    {
        static readonly Dictionary<string, string> Turkish = Map("tr.json"), English = Map("en.json");
        internal static string Resource(string name)
        {
            using (var s = typeof(Locale).Assembly.GetManifestResourceStream(name))
            using (var r = new StreamReader(s))
                return r.ReadToEnd();
        }

        internal static Dictionary<string, string> Map(string name)
        {
            return new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(Resource(name));
        }

        public static string Get(bool english, string key)
        {
            string s;
            if ((english ? English : Turkish).TryGetValue(key, out s))
                return s;
            throw new InvalidDataException("Missing translation: " + key);
        }
    }
}
