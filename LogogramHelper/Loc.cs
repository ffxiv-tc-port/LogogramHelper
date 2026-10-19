using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;

namespace LogogramHelper
{
    public static class Loc
    {
        private static readonly Dictionary<string, string> Dict = Load();

        private static Dictionary<string, string> Load()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("LogogramHelper.loc.zh_TW.json");
            if (stream == null) return new Dictionary<string, string>();
            using var reader = new StreamReader(stream);
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(reader.ReadToEnd()) ?? new Dictionary<string, string>();
        }

        public static string T(string fallback) => Dict.TryGetValue(fallback, out var value) ? value : fallback;
    }
}
