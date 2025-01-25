public class EnhancedPerformanceMonitorAttribute : MethodInterceptionAspect
{
    private static readonly ConcurrentDictionary<string, EnhancedMetrics> _metrics = 
        new ConcurrentDictionary<string, EnhancedMetrics>();

    public override void OnInvoke(MethodInterceptionArgs args)
    {
        var sw = Stopwatch.StartNew();
        var memory = GC.GetTotalMemory(false);
        Exception exception = null;

        try
        {
            args.Proceed();
        }
        catch (Exception ex)
        {
            exception = ex;
            throw;
        }
        finally
        {
            sw.Stop();
            var memoryUsed = GC.GetTotalMemory(false) - memory;
            var key = $"{args.Method.DeclaringType?.Name}.{args.Method.Name}";

            _metrics.AddOrUpdate(key,
                _ => new EnhancedMetrics 
                { 
                    TotalTime = sw.ElapsedMilliseconds,
                    Count = 1,
                    MemoryUsed = memoryUsed,
                    LastExecutionTime = DateTime.UtcNow,
                    Exceptions = exception != null ? 1 : 0
                },
                (_, m) =>
                {
                    m.TotalTime += sw.ElapsedMilliseconds;
                    m.Count++;
                    m.MemoryUsed += memoryUsed;
                    m.LastExecutionTime = DateTime.UtcNow;
                    if (exception != null) m.Exceptions++;
                    m.MinExecutionTime = Math.Min(m.MinExecutionTime, sw.ElapsedMilliseconds);
                    m.MaxExecutionTime = Math.Max(m.MaxExecutionTime, sw.ElapsedMilliseconds);
                    return m;
                });
        }
    }

    private class EnhancedMetrics
    {
        public long TotalTime { get; set; }
        public int Count { get; set; }
        public long MemoryUsed { get; set; }
        public DateTime LastExecutionTime { get; set; }
        public int Exceptions { get; set; }
        public long MinExecutionTime { get; set; } = long.MaxValue;
        public long MaxExecutionTime { get; set; }
    }
}
