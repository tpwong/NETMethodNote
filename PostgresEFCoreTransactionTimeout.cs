public class PostgresEfService
{
    private readonly YourDbContext _context;
    private readonly ILogger<PostgresEfService> _logger;

    public PostgresEfService(
        YourDbContext context,
        ILogger<PostgresEfService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task ExecuteWithEf()
    {
          using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
          
          // 設定命令超時
          _context.Database.SetCommandTimeout(TimeSpan.FromSeconds(30));

          // 設定 PostgreSQL 特定的超時
          await _context.Database.ExecuteSqlRawAsync(
              "SET lock_timeout = '5s'; SET statement_timeout = '30s';",
              cts.Token);

          await using var transaction = await _context.Database
              .BeginTransactionAsync(cts.Token);

          try
          {
              // 你的 EF Core 操作
              await _context.SaveChangesAsync(cts.Token);
              
              await transaction.CommitAsync(cts.Token);
          }
          catch (Exception)
          {
              await transaction.RollbackAsync(cts.Token);
              throw;
          }
      });
  }
