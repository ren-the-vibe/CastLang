#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cast.Lang;

// A binding cell. Eager bindings hold a CastValue. Lazy bindings hold a thunk that
// re-evaluates on read. Alias bindings share a cell (=&), so several names point
// at one CastCell instance.
public sealed class CastCell
{
    public CastValue? Eager;
    public Func<CastValue>? Lazy;     // for => bindings
    public bool IsLazy => Lazy is not null;

    public CastValue Read() => IsLazy ? Lazy!() : (Eager ?? CastValue.Null);
    public void Write(CastValue v) { Eager = v; Lazy = null; }
}

// A lexical frame: function-local or session-local. Names resolve outward through
// parent frames. =& aliases install the SAME CastCell under two names (possibly across
// the arg/param duality within one frame).
public sealed class CastFrame
{
    private readonly Dictionary<string, CastCell> _cells = new();
    public CastFrame? Parent { get; }

    public CastFrame(CastFrame? parent = null) => Parent = parent;

    public CastCell? Find(string name)
    {
        for (var f = this; f is not null; f = f.Parent)
            if (f._cells.TryGetValue(name, out var c)) return c;
        return null;
    }

    public CastCell Declare(string name)
    {
        if (!_cells.TryGetValue(name, out var c)) { c = new CastCell(); _cells[name] = c; }
        return c;
    }

    // Bind a name to an existing cell (used by =& alias).
    public void BindCell(string name, CastCell cell) => _cells[name] = cell;

    public bool HasLocal(string name) => _cells.ContainsKey(name);
    public IEnumerable<string> LocalNames => _cells.Keys;
}

// The @v persistent registry: a flat dictionary keyed by namespace-structured
// names (colon-joined). Language-owned, not host-provided. Strict on read,
// tolerant on write (writing creates the slot).
public sealed class CastRegistry
{
    private readonly Dictionary<string, CastValue> _slots = new();

    public bool Has(string key) => _slots.ContainsKey(key);

    public CastValue Read(string key) =>
        _slots.TryGetValue(key, out var v) ? v : throw new CastRuntimeException($"@v:{key} is unset");

    public CastValue ReadOr(string key, CastValue fallback) =>
        _slots.TryGetValue(key, out var v) ? v : fallback;

    public void Write(string key, CastValue v)
    {
        if (v is NullValue) _slots.Remove(key);   // writing null clears the slot
        else _slots[key] = v;
    }

    public int Count => _slots.Count;
    public IReadOnlyDictionary<string, CastValue> Snapshot() => _slots;

    // Query: keys matching a glob (e.g. 'score:**'). '**' matches any run.
    public IEnumerable<KeyValuePair<string, CastValue>> Query(string glob)
    {
        var rx = "^" + System.Text.RegularExpressions.Regex.Escape(glob)
                     .Replace("\\*\\*", ".*") + "$";
        var re = new System.Text.RegularExpressions.Regex(rx);
        return _slots.Where(kv => re.IsMatch(kv.Key));
    }
}

public sealed class CastRuntimeException : Exception
{
    public CastRuntimeException(string message) : base(message) { }
}
