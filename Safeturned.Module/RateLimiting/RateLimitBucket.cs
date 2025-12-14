using System;

namespace Safeturned.Module.RateLimiting;

public class RateLimitBucket
{
    private readonly object _lock = new();
    private readonly Func<long> _utcNowSeconds;

    public RateLimitBucket(Func<long> utcNowSeconds = null)
    {
        _utcNowSeconds = utcNowSeconds ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public RateLimitState State { get; private set; } = new();

    public void SeedFromHeaders(int limit, int remaining, long resetUnixSeconds)
    {
        lock (_lock)
        {
            var now = _utcNowSeconds();
            if (resetUnixSeconds > now && State.ResetUnixSeconds > 0)
            {
                State.WindowSeconds = resetUnixSeconds - now + (now - State.BucketUpdatedAtUnixSeconds);
            }
            State.Limit = limit;
            State.ResetUnixSeconds = resetUnixSeconds;
            State.BucketTokens = remaining;
            State.BucketUpdatedAtUnixSeconds = now;
        }
    }

    public bool TryConsume(out int tokensLeft)
    {
        lock (_lock)
        {
            if (State.Limit <= 0)
            {
                tokensLeft = int.MaxValue;
                return true;
            }
            RefillIfNeeded();
            if (State.BucketTokens <= 0)
            {
                tokensLeft = 0;
                return false;
            }
            State.BucketTokens--;
            tokensLeft = State.BucketTokens;
            return true;
        }
    }

    private void RefillIfNeeded()
    {
        var now = _utcNowSeconds();
        if (State.ResetUnixSeconds == 0 || State.Limit == 0)
        {
            return;
        }

        if (now >= State.ResetUnixSeconds)
        {
            State.BucketTokens = State.Limit;
            State.ResetUnixSeconds = now + State.WindowSeconds;
            State.BucketUpdatedAtUnixSeconds = now;
        }
    }
}
