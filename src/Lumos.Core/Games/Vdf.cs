using System.Text;

namespace Lumos.Core.Games;

/// <summary>
/// Minimal reader for Valve's text KeyValues format (libraryfolders.vdf, appmanifest_*.acf):
/// quoted or bare keys and values, <c>{ }</c> nesting and <c>//</c> comments. Keys are case-insensitive.
/// </summary>
public sealed class VdfObject
{
    private readonly Dictionary<string, object> _items = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<KeyValuePair<string, object>> Items => _items;

    public string? GetString(string key) => _items.TryGetValue(key, out var v) ? v as string : null;

    public VdfObject? GetObject(string key) => _items.TryGetValue(key, out var v) ? v as VdfObject : null;

    public IEnumerable<VdfObject> Children => _items.Values.OfType<VdfObject>();

    internal void Set(string key, object value) => _items[key] = value;

    /// <summary>Parses a document. Malformed input yields whatever was read before the error.</summary>
    public static VdfObject Parse(string text)
    {
        var pos = 0;
        var root = new VdfObject();
        ReadInto(root, text, ref pos);
        return root;
    }

    private static void ReadInto(VdfObject target, string text, ref int pos)
    {
        while (NextToken(text, ref pos, out var key, out var isBrace))
        {
            if (isBrace) return; // '}' closes this object ('{' without a key is skipped below)
            if (!NextToken(text, ref pos, out var value, out var valueIsBrace)) return;
            if (valueIsBrace && value == "{")
            {
                var child = new VdfObject();
                ReadInto(child, text, ref pos);
                target.Set(key, child);
            }
            else if (valueIsBrace)
            {
                return; // "key }" is malformed
            }
            else
            {
                target.Set(key, value);
            }
        }
    }

    /// <summary>Reads one token. Braces come back with <paramref name="isBrace"/> set and the brace as the value.</summary>
    private static bool NextToken(string text, ref int pos, out string token, out bool isBrace)
    {
        token = "";
        isBrace = false;
        while (pos < text.Length)
        {
            var c = text[pos];
            if (char.IsWhiteSpace(c)) { pos++; continue; }
            if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
            {
                while (pos < text.Length && text[pos] != '\n') pos++;
                continue;
            }
            if (c is '{' or '}')
            {
                pos++;
                token = c.ToString();
                isBrace = true;
                return true;
            }
            if (c == '"')
            {
                pos++;
                var sb = new StringBuilder();
                while (pos < text.Length && text[pos] != '"')
                {
                    if (text[pos] == '\\' && pos + 1 < text.Length)
                    {
                        pos++;
                        sb.Append(text[pos] switch { 'n' => '\n', 't' => '\t', _ => text[pos] });
                    }
                    else
                    {
                        sb.Append(text[pos]);
                    }
                    pos++;
                }
                pos++; // closing quote
                token = sb.ToString();
                return true;
            }

            var start = pos;
            while (pos < text.Length && !char.IsWhiteSpace(text[pos]) && text[pos] is not ('{' or '}' or '"')) pos++;
            token = text[start..pos];
            return true;
        }
        return false;
    }
}
