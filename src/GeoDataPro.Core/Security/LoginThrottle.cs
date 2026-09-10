using System;
using System.Collections.Generic;

namespace GeoDataPro.Core.Security;

public interface ILoginThrottle
{
    bool TryEnter(string key, out TimeSpan retryAfter);
    void OnFailure(string key);
    void OnSuccess(string key);
}

public sealed class LoginThrottle : ILoginThrottle
{
    public const int WindowFailures = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(30);

    sealed class Entry
    {
        public int Failures;
        public DateTimeOffset FirstFailure;
        public DateTimeOffset BlockedUntil;
    }

    readonly IClock _clock;
    readonly object _gate = new();
    readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public LoginThrottle(IClock? clock = null) => _clock = clock ?? SystemClock.Instance;

    public bool TryEnter(string key, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(key)) return true;

        lock (_gate)
        {
            Prune();
            if (!_entries.TryGetValue(key, out var entry)) return true;

            var now = _clock.UtcNow;
            if (entry.BlockedUntil > now)
            {
                retryAfter = entry.BlockedUntil - now;
                return false;
            }

            return true;
        }
    }

    public void OnFailure(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        lock (_gate)
        {
            var now = _clock.UtcNow;
            if (!_entries.TryGetValue(key, out var entry))
            {
                entry = new Entry { FirstFailure = now };
                _entries[key] = entry;
            }

            if (now - entry.FirstFailure > Window && entry.BlockedUntil <= now)
            {
                entry.Failures = 0;
                entry.FirstFailure = now;
            }

            entry.Failures++;

            if (entry.Failures >= WindowFailures)
            {
                var over = entry.Failures - WindowFailures;
                var factor = Math.Min(over, 8);
                var delay = TimeSpan.FromTicks(BaseDelay.Ticks * (1L << factor));
                if (delay > MaxDelay) delay = MaxDelay;
                entry.BlockedUntil = now + delay;
            }
        }
    }

    public void OnSuccess(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        lock (_gate) _entries.Remove(key);
    }

    void Prune()
    {
        var now = _clock.UtcNow;
        if (_entries.Count < 512) return;

        var stale = new List<string>();
        foreach (var pair in _entries)
            if (pair.Value.BlockedUntil <= now && now - pair.Value.FirstFailure > Window)
                stale.Add(pair.Key);

        foreach (var key in stale) _entries.Remove(key);
    }
}
