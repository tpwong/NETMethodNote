public class MemberDataManager
{
    // 使用不可变数据结构保证线程安全
    private ImmutableDictionary<long, string> _accountIdDict = ImmutableDictionary<long, string>.Empty;
    private ImmutableDictionary<long, string> _memberIdDict = ImmutableDictionary<long, string>.Empty;
    
    // 字符串缓存使用分代设计
    private ImmutableDictionary<string, string> _currentCardTierCache = ImmutableDictionary<string, string>.Empty;
    private ImmutableDictionary<string, string> _previousCardTierCache = ImmutableDictionary<string, string>.Empty;

    public string GetCardTierByAccountId(long accountId)
    {
        // 完全无锁读取
        return _accountIdDict.TryGetValue(accountId, out var ct) ? ct : null;
    }

    private void RefreshData()
    {
        var sw = Stopwatch.StartNew();
        
        // 阶段1：构建新数据集
        var (newAccountDict, newMemberDict, newCache) = BuildNewDataSet();
        
        // 阶段2：原子替换引用
        ImmutableInterlocked.Update(ref _previousCardTierCache, _ => _currentCardTierCache);
        ImmutableInterlocked.Update(ref _currentCardTierCache, _ => newCache);
        ImmutableInterlocked.Update(ref _accountIdDict, _ => newAccountDict);
        ImmutableInterlocked.Update(ref _memberIdDict, _ => newMemberDict);

        // 阶段3：延迟清理旧缓存
        Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ => {
            _previousCardTierCache = ImmutableDictionary<string, string>.Empty;
        });
        
        Console.WriteLine($"Refresh completed in {sw.ElapsedMilliseconds}ms");
    }

    private (ImmutableDictionary<long, string>, ImmutableDictionary<long, string>, ImmutableDictionary<string, string>) BuildNewDataSet()
    {
        var accountBuilder = ImmutableDictionary.CreateBuilder<long, string>();
        var memberBuilder = ImmutableDictionary.CreateBuilder<long, string>();
        var cacheBuilder = ImmutableDictionary.CreateBuilder<string, string>();

        using var connection = new SqlConnection(_connectionString);
        connection.Open();
        
        using var command = new SqlCommand("SELECT AccountID, MemberID, CardTier FROM MemberTable", connection);
        using var reader = command.ExecuteReader();
        
        while (reader.Read())
        {
            var accountId = reader.GetInt64(0);
            var memberId = reader.GetInt64(1);
            var cardTier = reader.GetString(2);

            // 三级缓存查询策略
            if (!_currentCardTierCache.TryGetValue(cardTier, out var cached) &&
                !_previousCardTierCache.TryGetValue(cardTier, out cached))
            {
                cached = cardTier;
            }

            cacheBuilder[cached] = cached; // 维持引用
            accountBuilder[accountId] = cached;
            memberBuilder[memberId] = cached;
        }

        return (
            accountBuilder.ToImmutable(),
            memberBuilder.ToImmutable(),
            cacheBuilder.ToImmutable()
        );
    }
}
