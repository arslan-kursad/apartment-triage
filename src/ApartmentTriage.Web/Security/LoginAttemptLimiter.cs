using System.Collections.Concurrent;

namespace ApartmentTriage.Web.Security;

public sealed class LoginAttemptLimiter
{
    private sealed record AttemptWindow(DateTimeOffset WindowStart, int Count);

    private readonly ConcurrentDictionary<string, AttemptWindow> _attempts = new();
    private readonly TimeSpan _window = TimeSpan.FromHours(1);
    private const int MaxAttempts = 5;

    public bool IsAllowed(string key, DateTimeOffset now)
    {
        var current = _attempts.GetOrAdd(key, _ => new AttemptWindow(now, 0));
        return now - current.WindowStart >= _window || current.Count < MaxAttempts;
    }

    public int RegisterFailure(string key, DateTimeOffset now)
    {
        var updated = _attempts.AddOrUpdate(
            key,
            _ => new AttemptWindow(now, 1),
            (_, current) => now - current.WindowStart >= _window
                ? new AttemptWindow(now, 1)
                : current with { Count = current.Count + 1 });

        return Math.Max(0, MaxAttempts - updated.Count);
    }

    public void Reset(string key)
        => _attempts.TryRemove(key, out _);
}
