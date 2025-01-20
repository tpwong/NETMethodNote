public static class IdempotencyChecker
{
    private static readonly ConcurrentDictionary<string, DateTime> _requests = new();
    private static readonly TimeSpan _expirationTime = TimeSpan.FromSeconds(5);

    public static bool IsNewRequest(string acct, string bucketType, decimal amount)
    {
        var requestId = $"{acct}:{bucketType}:{amount}";
        var now = DateTime.UtcNow;

        foreach (var key in _requests.Keys)
        {
            if (_requests.TryGetValue(key, out var time) && 
                (now - time) > _expirationTime)
            {
                _requests.TryRemove(key, out _);
            }
        }

        return _requests.TryAdd(requestId, now);
    }
}
