using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CanEmAllEditor.Core;

/// <summary>
/// The game writes its files with Unity's JsonUtility (pretty print, 4 spaces). This keeps that format:
/// key order and unknown keys are preserved, float fields stay floats ("100.0"), int fields stay ints.
/// </summary>
public sealed class UnityJson
{
    public JsonObject Root { get; }
    readonly HashSet<string> floatKeys;

    UnityJson(JsonObject root, HashSet<string> floatKeys)
    {
        Root = root;
        this.floatKeys = floatKeys;
    }

    public static UnityJson Parse(string text, IEnumerable<string>? knownFloatKeys = null)
    {
        var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })
                   as JsonObject ?? throw new FormatException("JSON root is not an object");
        var floats = new HashSet<string>(knownFloatKeys ?? []);
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        foreach (var p in doc.RootElement.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.Number && p.Value.GetRawText().IndexOfAny(['.', 'e', 'E']) >= 0)
                floats.Add(p.Name);
        return new UnityJson(node, floats);
    }

    public static UnityJson Empty(IEnumerable<string>? knownFloatKeys = null) => new(new JsonObject(), new HashSet<string>(knownFloatKeys ?? []));

    // ---- typed accessors ----
    public long GetLong(string key, long fallback = 0)
    {
        if (Root[key] is JsonValue v)
        {
            if (v.TryGetValue(out long l)) return l;
            if (v.TryGetValue(out double d)) return (long)d;
        }
        return fallback;
    }

    public double GetDouble(string key, double fallback = 0) =>
        Root[key] is JsonValue v && v.TryGetValue(out double d) ? d : fallback;

    public bool GetBool(string key, bool fallback = false) =>
        Root[key] is JsonValue v && v.TryGetValue(out bool b) ? b : fallback;

    public void SetLong(string key, long value) => Root[key] = JsonValue.Create(value);
    public void SetDouble(string key, double value) { Root[key] = JsonValue.Create(value); floatKeys.Add(key); }
    public void SetBool(string key, bool value) => Root[key] = JsonValue.Create(value);

    public List<int> GetIntList(string key)
    {
        var list = new List<int>();
        if (Root[key] is JsonArray arr)
            foreach (var n in arr)
                if (n is JsonValue v && v.TryGetValue(out int i)) list.Add(i);
        return list;
    }

    public void SetIntList(string key, IEnumerable<int> values)
    {
        var arr = new JsonArray();
        foreach (int v in values) arr.Add(JsonValue.Create(v));
        Root[key] = arr;
    }

    /// <summary>Nested object (e.g. "lifetimeStats"); created when missing.</summary>
    public JsonObject Child(string key)
    {
        if (Root[key] is JsonObject o) return o;
        var n = new JsonObject();
        Root[key] = n;
        return n;
    }

    /// <summary>Array of objects (e.g. "mails"); created when missing.</summary>
    public JsonArray Array(string key)
    {
        if (Root[key] is JsonArray a) return a;
        var n = new JsonArray();
        Root[key] = n;
        return n;
    }

    /// <summary>Integer by path: "money" or "lifetimeStats.totalKilled".</summary>
    public long GetPath(string path, long fallback = 0)
    {
        int dot = path.IndexOf('.');
        if (dot < 0) return GetLong(path, fallback);
        return Root[path[..dot]] is JsonObject o && o[path[(dot + 1)..]] is JsonValue v && v.TryGetValue(out long l) ? l : fallback;
    }

    public void SetPath(string path, long value)
    {
        int dot = path.IndexOf('.');
        if (dot < 0) SetLong(path, value);
        else Child(path[..dot])[path[(dot + 1)..]] = JsonValue.Create(value);
    }

    public static long Long(JsonNode? n, string key, long fallback = 0)
    {
        if (n is not JsonObject o || o[key] is not JsonValue v) return fallback;
        if (v.TryGetValue(out long l)) return l;
        if (v.TryGetValue(out int i)) return i;
        return v.TryGetValue(out double d) ? (long)d : fallback;
    }

    public static bool Bool(JsonNode? n, string key, bool fallback = false) =>
        n is JsonObject o && o[key] is JsonValue v && v.TryGetValue(out bool b) ? b : fallback;

    // ---- writer ----
    public override string ToString()
    {
        var sb = new StringBuilder();
        WriteNode(sb, Root, 0, null);
        return sb.ToString();
    }

    void WriteNode(StringBuilder sb, JsonNode? node, int depth, string? key)
    {
        string pad = new(' ', (depth + 1) * 4), padEnd = new(' ', depth * 4);
        switch (node)
        {
            case JsonObject obj:
                if (obj.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                int i = 0;
                foreach (var (k, v) in obj)
                {
                    sb.Append(pad).Append(JsonSerializer.Serialize(k)).Append(": ");
                    WriteNode(sb, v, depth + 1, depth == 0 ? k : null);
                    sb.Append(++i < obj.Count ? ",\n" : "\n");
                }
                sb.Append(padEnd).Append('}');
                return;
            case JsonArray arr:
                if (arr.Count == 0) { sb.Append("[]"); return; }
                sb.Append("[\n");
                for (int j = 0; j < arr.Count; j++)
                {
                    sb.Append(pad);
                    WriteNode(sb, arr[j], depth + 1, key);
                    sb.Append(j + 1 < arr.Count ? ",\n" : "\n");
                }
                sb.Append(padEnd).Append(']');
                return;
            case JsonValue val:
                sb.Append(FormatValue(val, key));
                return;
            default:
                sb.Append("null");
                return;
        }
    }

    string FormatValue(JsonValue val, string? key)
    {
        string raw = val.ToJsonString();
        if (val.GetValueKind() != JsonValueKind.Number) return raw;
        if (key is not null && floatKeys.Contains(key))
        {
            double d = double.Parse(raw, CultureInfo.InvariantCulture);
            string s = d.ToString("R", CultureInfo.InvariantCulture);
            return s.IndexOfAny(['.', 'E', 'e']) >= 0 ? s : s + ".0";
        }
        return raw;
    }
}
