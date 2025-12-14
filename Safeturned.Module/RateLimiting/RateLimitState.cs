namespace Safeturned.Module.RateLimiting;

public class RateLimitState
{
    public int Limit { get; set; }
    public long ResetUnixSeconds { get; set; }
    public int BucketTokens { get; set; }
    public long BucketUpdatedAtUnixSeconds { get; set; }
    public long WindowSeconds { get; set; } = 3600;
}
