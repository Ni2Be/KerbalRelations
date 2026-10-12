using System.Collections.Generic;
using System.Globalization;

namespace KerbalRelations.Core
{
    /// <summary>
    /// Minimal name/value tree used for persistence. It mirrors the shape of KSP's ConfigNode so the
    /// plugin can convert one-to-one, while keeping the simulation free of KSP references.
    /// </summary>
    public class DataNode
    {
        public string Name;
        public readonly List<KeyValuePair<string, string>> Values = new List<KeyValuePair<string, string>>();
        public readonly List<DataNode> Nodes = new List<DataNode>();

        public DataNode(string name)
        {
            Name = name;
        }

        public void Add(string key, string value)
        {
            Values.Add(new KeyValuePair<string, string>(key, Sanitize(value)));
        }

        public void Add(string key, double value)
        {
            Values.Add(new KeyValuePair<string, string>(key, value.ToString("R", CultureInfo.InvariantCulture)));
        }

        public void Add(string key, bool value)
        {
            Values.Add(new KeyValuePair<string, string>(key, value ? "True" : "False"));
        }

        public DataNode AddNode(string name)
        {
            var node = new DataNode(name);
            Nodes.Add(node);
            return node;
        }

        public string Get(string key, string fallback = null)
        {
            for (int i = 0; i < Values.Count; ++i)
            {
                if (Values[i].Key == key)
                    return Values[i].Value;
            }
            return fallback;
        }

        public double GetDouble(string key, double fallback)
        {
            double result;
            var text = Get(key);
            if (text != null && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                return result;
            return fallback;
        }

        public bool GetBool(string key, bool fallback)
        {
            bool result;
            var text = Get(key);
            if (text != null && bool.TryParse(text, out result))
                return result;
            return fallback;
        }

        public IEnumerable<string> GetAll(string key)
        {
            for (int i = 0; i < Values.Count; ++i)
            {
                if (Values[i].Key == key)
                    yield return Values[i].Value;
            }
        }

        public IEnumerable<DataNode> GetNodes(string name)
        {
            for (int i = 0; i < Nodes.Count; ++i)
            {
                if (Nodes[i].Name == name)
                    yield return Nodes[i];
            }
        }

        // ConfigNode files are line based and treat braces and "//" specially.
        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Replace("\r", " ").Replace("\n", " ").Replace("{", "(").Replace("}", ")").Replace("//", "/");
        }
    }
}
