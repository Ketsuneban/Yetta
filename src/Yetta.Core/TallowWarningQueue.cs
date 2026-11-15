namespace Yetta.Core;

/// <summary>
/// Tallow's admission policy for non-modal warning windows.
/// No queued warning is ever implicitly approved; only an explicit UI decision can approve.
/// Caller owns UI objects and must call Close for windows that were dismissed.
/// </summary>
public sealed class TallowWarningQueue<T> where T : notnull
{
    public const int MaximumVisibleWarnings = 5;
    public enum Outcome { Show, Queued, Suppressed, Fatal, BlockedByFatal }
    public sealed record Ticket(Guid Id, string Fingerprint, T Payload);
    public sealed record Admission(Outcome Result, Ticket? Item);

    private readonly Dictionary<Guid, Ticket> _visible = [];
    private readonly List<Ticket> _pending = [];
    private readonly Dictionary<string, DateTimeOffset> _recent = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeSpan _cooldown;
    private bool _fatalVisible;

    public TallowWarningQueue(Func<DateTimeOffset>? now = null, TimeSpan? duplicateCooldown = null)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _cooldown = duplicateCooldown ?? TimeSpan.FromSeconds(5);
    }

    public int VisibleCount => _visible.Count;
    public int PendingCount => _pending.Count;
    public bool FatalVisible => _fatalVisible;
    public IReadOnlyList<Ticket> Pending => _pending.ToArray();

    public Admission Submit(string fingerprint, T payload, bool fatal = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        if (_fatalVisible) return new Admission(Outcome.BlockedByFatal, null);

        var now = _now();
        // Keep the cooldown index bounded, including on machines that run for days.
        foreach (var key in _recent.Where(pair => now - pair.Value > _cooldown).Select(pair => pair.Key).ToArray())
            _recent.Remove(key);

        if (!fatal && (_recent.TryGetValue(fingerprint, out var last) && now - last < _cooldown ||
            _visible.Values.Any(x => x.Fingerprint == fingerprint) ||
            _pending.Any(x => x.Fingerprint == fingerprint)))
            return new Admission(Outcome.Suppressed, null);

        var ticket = new Ticket(Guid.NewGuid(), fingerprint, payload);
        _recent[fingerprint] = now;
        if (fatal)
        {
            Reset();
            _fatalVisible = true;
            _visible.Add(ticket.Id, ticket);
            return new Admission(Outcome.Fatal, ticket);
        }
        if (_visible.Count < MaximumVisibleWarnings)
        {
            _visible.Add(ticket.Id, ticket);
            return new Admission(Outcome.Show, ticket);
        }
        _pending.Add(ticket);
        return new Admission(Outcome.Queued, ticket);
    }

    /// <summary>Release one visible slot and admit at most one pending request.</summary>
    public Ticket? Close(Guid id)
    {
        if (!_visible.Remove(id)) return null;
        if (_fatalVisible) { _fatalVisible = false; return null; }
        if (_pending.Count == 0) return null;
        var next = _pending[0];
        _pending.RemoveAt(0);
        _visible.Add(next.Id, next);
        return next;
    }

    public bool RejectQueued(Guid id)
    {
        int index = _pending.FindIndex(x => x.Id == id);
        if (index < 0) return false;
        _pending.RemoveAt(index);
        return true;
    }

    /// <summary>Used for fatal preemption and app shutdown; never accepts pending items.</summary>
    public void Reset()
    {
        _visible.Clear();
        _pending.Clear();
        _recent.Clear();
        _fatalVisible = false;
    }
}
