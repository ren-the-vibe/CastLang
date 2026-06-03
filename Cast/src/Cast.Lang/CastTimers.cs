#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cast.Lang;

// The @t timer/counter registry. Each slot (indexed @t[N] or named @t{'name'}) is
// a number that ticks. Sign is the user's intent: negative = timer (counts up
// toward 0), zero-or-positive = counter (counts up). A slot nulls itself when its
// value transitions from negative to 0-or-above, regardless of cause — tick,
// arithmetic, or direct assignment. State is host-free and fully testable by
// driving Tick() manually.
public sealed class CastTimers
{
    private sealed class Slot
    {
        public double CastValue;
        public bool Paused;
        public double Speed = 1.0;
    }

    private readonly Dictionary<string, Slot> _slots = new();
    private bool _globalPaused;
    private double _globalSpeed = 1.0;

    private static string Key(CastValue index) => index switch
    {
        NumberValue n => "#" + n.N.ToString(System.Globalization.CultureInfo.InvariantCulture),
        StringValue s => "$" + s.S,
        _ => index.ToString() ?? ""
    };

    public bool Has(CastValue index) => _slots.ContainsKey(Key(index));

    // Read a slot; returns null CastValue if unset/expired.
    public CastValue Read(CastValue index) =>
        _slots.TryGetValue(Key(index), out var s) ? new NumberValue(s.CastValue) : CastValue.Null;

    // Assign a slot. Honors the null-on-crossing rule: if the old value was
    // negative and the new value is >= 0, the slot nulls instead of storing.
    public void Set(CastValue index, double value)
    {
        string k = Key(index);
        _slots.TryGetValue(k, out var existing);
        double old = existing?.CastValue ?? double.NegativeInfinity; // unset acts like "fresh"

        if (existing is not null && old < 0 && value >= 0)
        {
            // crossing from negative to 0-or-above nulls the slot
            _slots.Remove(k);
            return;
        }
        if (existing is null)
        {
            // first assignment: store as-is (a fresh positive is a counter; fresh
            // negative is a timer). No crossing happened.
            _slots[k] = new Slot { CastValue = value };
            return;
        }
        existing.CastValue = value;
    }

    public void Clear(CastValue index) => _slots.Remove(Key(index));

    // Arithmetic that may cross zero from below also nulls (same rule).
    public void Add(CastValue index, double delta)
    {
        string k = Key(index);
        if (!_slots.TryGetValue(k, out var s)) return; // adding to unset is a no-op
        double old = s.CastValue;
        double next = old + delta;
        if (old < 0 && next >= 0) { _slots.Remove(k); return; }
        s.CastValue = next;
    }

    // Per-slot controls
    public void Pause(CastValue index) { if (_slots.TryGetValue(Key(index), out var s)) s.Paused = true; }
    public void Resume(CastValue index) { if (_slots.TryGetValue(Key(index), out var s)) s.Paused = false; }
    public void SetSpeed(CastValue index, double speed) { if (_slots.TryGetValue(Key(index), out var s)) s.Speed = speed; }

    // Global controls (apply across all slots)
    public void PauseAll() => _globalPaused = true;
    public void ResumeAll() => _globalPaused = false;
    public void SetGlobalSpeed(double speed) => _globalSpeed = speed;

    // Advance all slots by `seconds` of wall time, scaled per-slot and globally.
    // CastTimers (negative) and counters (zero-or-positive) both increase. A slot that
    // crosses from negative to >= 0 by ticking nulls itself.
    public void Tick(double seconds)
    {
        if (_globalPaused) return;
        var toNull = new List<string>();
        foreach (var (k, s) in _slots)
        {
            if (s.Paused) continue;
            double old = s.CastValue;
            double next = old + seconds * s.Speed * _globalSpeed;
            if (old < 0 && next >= 0) { toNull.Add(k); continue; }
            s.CastValue = next;
        }
        foreach (var k in toNull) _slots.Remove(k);
    }

    public int Count => _slots.Count;
}
