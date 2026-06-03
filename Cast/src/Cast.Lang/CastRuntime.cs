#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cast.Lang;

// An active standing cast living on @w.casts. The runtime polls these each frame.
public sealed class ActiveCast
{
    public int Id;
    public CastNode Node = null!;
    public bool ReferencesSessionLocal;   // if true, excluded from save (own-token rule)

    // Edge-tracking: targets currently "inside" the trigger scope. Used to detect
    // entry (edge). Map target -> remaining edge firings for this occupancy.
    public readonly HashSet<CastTarget> Inside = new();

    // CastFrame-spread schedule (for `over N`): pending (frameDue, target) firings.
    public readonly Queue<(long frame, CastTarget? target)> Scheduled = new();

    public override string ToString() => $"cast#{Id}";
}

// Owns active casts and drives them. The host advances time by calling Tick()
// once per frame; the runtime polls each standing cast's trigger scope and fires
// as the level/edge rules dictate. Fire-and-forget casts never live here — they
// run immediately at registration.
public sealed class CastRuntime
{
    private readonly List<ActiveCast> _casts = new();
    private int _nextId = 1;
    private long _frame;

    public long CastFrame => _frame;
    public IReadOnlyList<ActiveCast> Active => _casts;

    // Register a standing cast (one with a trigger scope). Returns its id.
    public int Register(CastNode node, bool referencesSessionLocal)
    {
        var ac = new ActiveCast { Id = _nextId++, Node = node, ReferencesSessionLocal = referencesSessionLocal };
        _casts.Add(ac);
        return ac.Id;
    }

    public bool Uncast(int id)
    {
        int removed = _casts.RemoveAll(c => c.Id == id);
        return removed > 0;
    }

    public void Clear() => _casts.Clear();

    // Advance one frame. The evaluator supplies the per-cast firing logic via the
    // callback, because firing requires evaluation against the host (which the
    // runtime doesn't own). The callback resolves the trigger scope and returns the
    // current set of fulfilling targets; the runtime applies level/edge rules and
    // calls fire(target) for each firing due this frame.
    public void Tick(
        Func<ActiveCast, IReadOnlyList<CastTarget>> resolveTargets,
        Action<ActiveCast, CastTarget?> fire)
    {
        _frame++;
        // snapshot: casts may be removed during iteration (e.g. a self-uncast)
        foreach (var cast in _casts.ToList())
        {
            // First, flush any scheduled (over-spread) firings due this frame.
            while (cast.Scheduled.Count > 0 && cast.Scheduled.Peek().frame <= _frame)
            {
                var (_, tgt) = cast.Scheduled.Dequeue();
                fire(cast, tgt);
            }

            var current = resolveTargets(cast);
            var currentSet = new HashSet<CastTarget>(current);

            bool edge = cast.Node.Count is not null;       // count => edge-triggered
            int count = edge ? (int)((NumberNode)cast.Node.Count!).CastValue : 0;
            int? over = cast.Node.Over is null ? null : (int)((NumberNode)cast.Node.Over!).CastValue;

            if (!edge)
            {
                // Level-triggered: fire for everything currently fulfilling, each frame.
                foreach (var t in current) fire(cast, t);
            }
            else
            {
                // Edge-triggered: fire N times for each target that JUST entered.
                foreach (var t in current)
                {
                    if (cast.Inside.Contains(t)) continue; // already inside; no new edge
                    // new entry
                    if (over is { } frames && count > 0)
                    {
                        // spread N firings across `frames` frames (spacing frames/count)
                        for (int i = 1; i <= count; i++)
                        {
                            long due = _frame + (long)Math.Round((double)frames * i / count);
                            cast.Scheduled.Enqueue((due, t));
                        }
                    }
                    else
                    {
                        for (int i = 0; i < count; i++) fire(cast, t);
                    }
                }
            }

            // Update occupancy: things no longer present leave (so re-entry is a fresh edge).
            cast.Inside.IntersectWith(currentSet);
            foreach (var t in currentSet) cast.Inside.Add(t);
        }
    }

    // Casts that survive a save: those whose own tokens don't reference a session-local.
    public IEnumerable<ActiveCast> Saveable => _casts.Where(c => !c.ReferencesSessionLocal);
    public int DroppedOnSave => _casts.Count(c => c.ReferencesSessionLocal);
}
