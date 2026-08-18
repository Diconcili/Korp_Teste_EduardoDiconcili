using System.Collections.Concurrent;

namespace FaturamentoService.Services;

public sealed class AuthenticationAttemptGuard
{
    const int MaximumFailures = 5;
    static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);
    readonly ConcurrentDictionary<string, AttemptState> attempts = new(StringComparer.Ordinal);

    public bool IsBlocked(string key)
    {
        if (!attempts.TryGetValue(key, out var state)) return false;
        lock (state)
        {
            if (state.BlockedUntil > DateTime.UtcNow) return true;
            if (state.BlockedUntil != default) attempts.TryRemove(key, out _);
            return false;
        }
    }

    public bool RegisterFailure(string key)
    {
        var state = attempts.GetOrAdd(key, _ => new AttemptState());
        lock (state)
        {
            state.Failures++;
            if (state.Failures < MaximumFailures) return false;
            state.BlockedUntil = DateTime.UtcNow.Add(LockDuration);
            return true;
        }
    }

    public void Clear(string key) => attempts.TryRemove(key, out _);

    sealed class AttemptState
    {
        public int Failures { get; set; }
        public DateTime BlockedUntil { get; set; }
    }
}
