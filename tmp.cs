var jitter = Random.Shared.Next(0, baseDelayMs);
var delay = (int)(baseDelayMs * Math.Pow(2, attempt - 1)) + jitter;
await Task.Delay(delay);