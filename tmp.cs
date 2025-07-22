# 針對 SQL 語句長度限制的 AspectCore Dapper 記錄器

修改後的版本將 SQL 語句限制在 2048 個字符以內，以避免日誌過大。這是一個在性能和可讀性之間取得良好平衡的優化實現。

## 1. 帶有 SQL 長度限制的 DapperLogger 特性

```csharp
using AspectCore.DynamicProxy;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

// 用於標記需要攔截的類
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface)]
public class DapperLoggerAttribute : AbstractInterceptorAttribute
{
    // 預定義所有 Dapper 方法名稱
    private static readonly HashSet<string> _dapperMethods = new(StringComparer.Ordinal)
    {
        // 查詢方法
        "Query", "QueryFirst", "QueryFirstOrDefault", "QuerySingle", "QuerySingleOrDefault", "QueryMultiple",
        "QueryAsync", "QueryFirstAsync", "QueryFirstOrDefaultAsync", "QuerySingleAsync", "QuerySingleOrDefaultAsync", "QueryMultipleAsync",
        "Get", "GetAll", "GetAsync", "GetAllAsync", "Find", "FindAsync",
        
        // 執行方法
        "Execute", "ExecuteScalar", "ExecuteReader",
        "ExecuteAsync", "ExecuteScalarAsync", "ExecuteReaderAsync",
        "Insert", "Update", "Delete", "InsertAsync", "UpdateAsync", "DeleteAsync"
    };

    // 使用 ThreadLocal 避免 Stopwatch 創建
    private static readonly ThreadLocal<Stopwatch> _stopwatch = 
        new(() => new Stopwatch());
        
    // SQL 語句最大長度限制
    private const int MAX_SQL_LENGTH = 2048;

    public async override Task Invoke(AspectContext context, AspectDelegate next)
    {
        // 獲取方法名稱
        var methodName = context.ImplementationMethod.Name;
        var declaringType = context.ImplementationMethod.DeclaringType?.Name ?? "Unknown";
        
        // 檢查是否是 Dapper 方法
        bool isDapperMethod = _dapperMethods.Contains(methodName) || 
            _dapperMethods.Any(prefix => methodName.StartsWith(prefix, StringComparison.Ordinal));
        
        if (!isDapperMethod)
        {
            await next(context);
            return;
        }

        // 獲取 Stopwatch
        var stopwatch = _stopwatch.Value;
        stopwatch.Reset();
        stopwatch.Start();
        
        Exception exception = null;
        
        try
        {
            // 執行原始方法
            await next(context);
        }
        catch (Exception ex)
        {
            exception = ex;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            var elapsedMs = stopwatch.ElapsedMilliseconds;
            
            try
            {
                // 提取 SQL 和參數
                string sql = ExtractAndTruncateSql(context.Parameters);
                object parameters = ExtractParameters(context.Parameters);
                
                // 根據是否有異常決定日誌級別和內容
                if (exception != null)
                {
                    Log.Error(
                        exception,
                        "Error executing Dapper in {ElapsedMilliseconds}ms. Method: {Method}, SQL: {Sql}, Parameters: {@Parameters}",
                        elapsedMs,
                        $"{declaringType}.{methodName}",
                        sql,
                        parameters);
                }
                else
                {
                    // 統一使用 Info 級別
                    Log.Information(
                        "Dapper executed in {ElapsedMilliseconds}ms. Method: {Method}, SQL: {Sql}, Parameters: {@Parameters}",
                        elapsedMs,
                        $"{declaringType}.{methodName}",
                        sql,
                        parameters);
                }
            }
            catch (Exception ex)
            {
                // 記錄日誌記錄本身的錯誤，避免影響主要業務邏輯
                Log.Error(ex, "Error while logging Dapper execution");
            }
        }
    }

    // 提取並截斷 SQL 語句
    private string ExtractAndTruncateSql(object[] parameters)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i] is string arg && !string.IsNullOrWhiteSpace(arg))
            {
                // 檢查 SQL 長度並截斷
                if (arg.Length > MAX_SQL_LENGTH)
                {
                    return arg.Substring(0, MAX_SQL_LENGTH) + "... [TRUNCATED]";
                }
                return arg;
            }
        }
        return "Unknown SQL";
    }

    // 提取參數
    private object ExtractParameters(object[] parameters)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i] != null &&
                !(parameters[i] is string) &&
                !(parameters[i] is IDbConnection) &&
                !(parameters[i] is IDbTransaction) &&
                !(parameters[i] is int?) &&
                !(parameters[i] is CommandType?) &&
                !(parameters[i] is bool))
            {
                return parameters[i];
            }
        }
        return null;
    }
}
```

## 2. 簡化的 AspectCore 擴展方法

```csharp
using AspectCore.Configuration;
using AspectCore.DynamicProxy;
using AspectCore.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Reflection;

public static class AspectCoreExtensions
{
    public static IServiceCollection AddDapperWithLogging(this IServiceCollection services)
    {
        // 配置 AspectCore
        services.ConfigureDynamicProxy(config =>
        {
            // 攔截 DbContext 或帶有 DapperLogger 特性的類
            config.Interceptors.AddTyped<DapperLoggerAttribute>(method => 
                ShouldInterceptMethod(method));
            
            // 配置非代理方法
            config.NonAspectPredicates.AddNamespace("System.*");
            config.NonAspectPredicates.AddNamespace("Microsoft.*");
            config.NonAspectPredicates.AddNamespace("AspectCore.*");
        });
        
        return services;
    }
    
    // 檢查是否應該攔截方法
    private static bool ShouldInterceptMethod(MethodInfo method)
    {
        if (method?.DeclaringType == null)
            return false;
        
        var declaringType = method.DeclaringType;
        
        // 檢查類型是否是 DbContext 或帶有 DapperLogger 特性
        bool shouldIntercept = declaringType.IsSubclassOf(typeof(DbContext)) ||
                              declaringType.GetCustomAttributes(typeof(DapperLoggerAttribute), true).Any();
        
        return shouldIntercept;
    }
}
```

## 3. 帶參數化的 DbContext 實現

此 DbContext 實現提供了對 Dapper 方法的良好封裝，同時加強了對超長 SQL 語句的處理：

```csharp
using Dapper;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

public class AppDbContext : DbContext
{
    private readonly IDbConnection _connection;
    private readonly AsyncLocal<IDbTransaction> _currentTransaction = new();
    
    // SQL 語句最大長度限制
    private const int MAX_SQL_LENGTH = 2048;
    
    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        IDbConnection connection) : base(options)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }
    
    public IDbConnection Connection
    {
        get
        {
            // 確保連接打開
            if (_connection.State != ConnectionState.Open)
            {
                _connection.Open();
            }
            
            return _connection;
        }
    }
    
    // 獲取或開始事務
    public IDbTransaction GetOrBeginTransaction()
    {
        if (_currentTransaction.Value != null)
        {
            return _currentTransaction.Value;
        }
        
        var efTransaction = Database.BeginTransaction();
        var dbTransaction = efTransaction.GetDbTransaction();
        _currentTransaction.Value = dbTransaction;
        
        return dbTransaction;
    }
    
    // 獲取當前事務
    public IDbTransaction GetCurrentTransaction() => _currentTransaction.Value;
    
    // 安全執行 Dapper 查詢，處理 SQL 長度
    public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object param = null)
    {
        // 檢查 SQL 長度，如果超出限制則發出警告
        if (sql?.Length > MAX_SQL_LENGTH)
        {
            Log.Warning("Long SQL query detected ({Length} chars). Consider optimizing.", sql.Length);
        }
        
        return await Connection.QueryAsync<T>(
            sql, 
            param, 
            transaction: GetCurrentTransaction());
    }
    
    // 安全執行 Dapper 查詢單個結果
    public async Task<T> QueryFirstOrDefaultAsync<T>(string sql, object param = null)
    {
        return await Connection.QueryFirstOrDefaultAsync<T>(
            sql, 
            param, 
            transaction: GetCurrentTransaction());
    }
    
    // 安全執行 Dapper 命令
    public async Task<int> ExecuteAsync(string sql, object param = null)
    {
        return await Connection.ExecuteAsync(
            sql, 
            param, 
            transaction: GetCurrentTransaction());
    }
    
    // 資源清理
    public override void Dispose()
    {
        _currentTransaction.Value = null;
        base.Dispose();
    }
}
```

## 4. 示例儲存庫實現

此實現展示了如何處理可能很長的 SQL 語句：

```csharp
using Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// 使用 DapperLogger 特性標記需要攔截的類
[DapperLogger]
public class UserRepository : IUserRepository
{
    private readonly AppDbContext _dbContext;
    
    // 簡單 SQL 常量
    private const string GET_BY_ID_SQL = "SELECT * FROM Users WHERE Id = @Id";
    private const string DELETE_SQL = "DELETE FROM Users WHERE Id = @Id";
    
    public UserRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }
    
    public async Task<IEnumerable<User>> GetAllAsync()
    {
        // 使用 SQL 構建器處理可能複雜的查詢
        var sql = new StringBuilder();
        sql.Append("SELECT u.*, ");
        sql.Append("p.Address, p.Phone, p.Bio, ");
        sql.Append("r.RoleName, r.Permissions ");
        sql.Append("FROM Users u ");
        sql.Append("LEFT JOIN UserProfiles p ON u.Id = p.UserId ");
        sql.Append("LEFT JOIN UserRoles ur ON u.Id =