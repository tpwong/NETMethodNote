# 修正版：基於類名的 DapperLogger 數據庫標識

以下是修改後的版本，允許在 DapperLogger 特性上添加一個字符串參數，用於標識數據庫。對於名為 "DbContext" 或以 "DbContext" 結尾的類，如果沒有明確標記 DapperLogger 特性，會自動使用類名作為數據庫標識：

## 1. 修改後的 DapperLogger 特性

```csharp
using AspectCore.DynamicProxy;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
        
    // SQL 語句的最大長度
    private const int MaxSqlLength = 2048;
    
    // 數據庫標識
    private readonly string _databaseIdentifier;
    
    // 默認構造函數
    public DapperLoggerAttribute() : this(null)
    {
    }
    
    // 帶數據庫標識的構造函數
    public DapperLoggerAttribute(string databaseIdentifier)
    {
        _databaseIdentifier = databaseIdentifier;
    }

    public async override Task Invoke(AspectContext context, AspectDelegate next)
    {
        // 獲取方法名稱和類型
        var methodName = context.ImplementationMethod.Name;
        var declaringType = context.ImplementationMethod.DeclaringType;
        var typeName = declaringType?.Name ?? "Unknown";
        
        // 確定數據庫標識
        string dbIdentifier = _databaseIdentifier;
        
        // 如果沒有指定數據庫標識且類名是 DbContext 或以 DbContext 結尾，則使用類名作為標識
        if (string.IsNullOrEmpty(dbIdentifier))
        {
            if (typeName.Equals("DbContext", StringComparison.OrdinalIgnoreCase) || 
                typeName.EndsWith("DbContext", StringComparison.OrdinalIgnoreCase))
            {
                dbIdentifier = typeName;
            }
        }
        
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
                object parameters = ExtractAndSanitizeParameters(context.Parameters);
                
                // 構建數據庫標識字符串
                string dbInfo = !string.IsNullOrEmpty(dbIdentifier) 
                    ? $"[{dbIdentifier}] " 
                    : string.Empty;
                
                // 根據是否有異常決定日誌級別和內容
                if (exception != null)
                {
                    Log.Error(
                        exception,
                        "Error executing Dapper in {ElapsedMilliseconds}ms. {DbInfo}Method: {Method}, SQL: {Sql}, Parameters: {@Parameters}",
                        elapsedMs,
                        dbInfo,
                        $"{typeName}.{methodName}",
                        sql,
                        parameters);
                }
                else
                {
                    // 統一使用 Info 級別
                    Log.Information(
                        "Dapper executed in {ElapsedMilliseconds}ms. {DbInfo}Method: {Method}, SQL: {Sql}, Parameters: {@Parameters}",
                        elapsedMs,
                        dbInfo,
                        $"{typeName}.{methodName}",
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
                // 檢查長度並截斷
                if (arg.Length > MaxSqlLength)
                {
                    return arg.Substring(0, MaxSqlLength) + "... [TRUNCATED]";
                }
                return arg;
            }
        }
        return "Unknown SQL";
    }

    // 提取並清理參數
    private object ExtractAndSanitizeParameters(object[] parameters)
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
                return SanitizeObject(parameters[i]);
            }
        }
        return null;
    }
    
    // 清理對象，隱藏敏感信息
    private object SanitizeObject(object obj)
    {
        if (obj == null || obj.GetType().IsPrimitive || obj is string || obj is DateTime || obj is decimal)
        {
            return obj;
        }
        
        var properties = obj.GetType().GetProperties();
        var sanitizedObject = new Dictionary<string, object>();
        
        foreach (var prop in properties)
        {
            var propName = prop.Name.ToLowerInvariant();
            var value = prop.GetValue(obj);
            
            if (propName.Contains("password") || propName.Contains("secret") || 
                propName.Contains("key") || propName.Contains("token"))
            {
                sanitizedObject[prop.Name] = "***REDACTED***";
            }
            else
            {
                sanitizedObject[prop.Name] = value;
            }
        }
        
        return sanitizedObject;
    }
}
```

## 2. 更新 AspectCore 擴展方法以處理 DbContext 特例

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
            // 攔截 DbContext、帶有 DapperLogger 特性的類，以及名為 DbContext 的類
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
        var typeName = declaringType.Name;
        
        // 檢查是否存在 DapperLogger 特性
        var hasAttribute = declaringType.GetCustomAttributes(typeof(DapperLoggerAttribute), true).Any() ||
                          declaringType.GetInterfaces()
                              .Any(i => i.GetCustomAttributes(typeof(DapperLoggerAttribute), true).Any());
        
        // 檢查是否是 DbContext 子類
        var isDbContext = declaringType.IsSubclassOf(typeof(DbContext));
        
        // 檢查類名是否是 DbContext 或以 DbContext 結尾
        var isDbContextName = typeName.Equals("DbContext", StringComparison.OrdinalIgnoreCase) || 
                             typeName.EndsWith("DbContext", StringComparison.OrdinalIgnoreCase);
        
        return hasAttribute || isDbContext || isDbContextName;
    }
}
```

## 3. 使用範例

### 3.1 具有明確數據庫標識的儲存庫

```csharp
// 使用 DapperLogger 特性並指定數據庫標識
[DapperLogger("OracleDB")]
public class ProductRepository : IProductRepository
{
    private readonly AppDbContext _dbContext;
    
    public ProductRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }
    
    // 實現方法...
}
```

### 3.2 使用默認數據庫標識的儲存庫

```csharp
// 使用 DapperLogger 特性但不指定數據庫標識
[DapperLogger]
public class CustomerRepository : ICustomerRepository
{
    private readonly AppDbContext _dbContext;
    
    public CustomerRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }
    
    // 實現方法...
}
```

### 3.3 自動使用類名作為標識的 DbContext 類

```csharp
// 不需要顯式添加 DapperLogger 特性，將自動使用類名作為數據庫標識
public class SalesDbContext : DbContext
{
    private readonly IDbConnection _connection;
    
    public SalesDbContext(DbContextOptions options, IDbConnection connection) 
        : base(options)
    {
        _connection = connection;
    }
    
    // 實現方法...
}
```

### 3.4 帶有明確數據庫標識的 DbContext 類

```csharp
// 明確指定數據庫標識
[DapperLogger("MainDatabase")]
public class AppDbContext : DbContext
{
    private readonly IDbConnection _connection;
    
    public AppDbContext(DbContextOptions<AppDbContext> options, IDbConnection connection) 
        : base(options)
    {
        _connection = connection;
    }
    
    // 實現方法...
}
```

## 4. 日誌輸出示例

1. **帶有指定數據庫標識的日誌**：
   ```
   [10:15:30 INF] Dapper executed in 45ms. [OracleDB] Method: ProductRepository.GetAllAsync, SQL: SELECT * FROM Products, Parameters: {}
   ```

2. **使用類名作為數據庫標識的日誌**：
   ```
   [10:16:05 INF] Dapper executed in 32ms. [SalesDbContext] Method: SalesDbContext.QueryAsync, SQL: SELECT * FROM Sales WHERE Date > @StartDate, Parameters: { "StartDate": "2023-01-01" }
   ```

3. **帶有明確數據庫標識的 DbContext 日誌**：
   ```
   [10:17:12 INF] Dapper executed in 28ms. [MainDatabase] Method: AppDbContext.QueryAsync, SQL: SELECT * FROM Customers WHERE Id = @Id, Parameters: { "Id": 123 }
   ```

## 5. 總結

這個實現提供了靈活的數據庫標識機制：

1. **明確標識**：可以通過 `[DapperLogger("DatabaseName")]` 明確指定數據庫標識
2. **自動標識**：對於名為 "DbContext" 或以 "DbContext" 結尾的類，自動使用類名作為標識
3. **標準輸出格式**：在日誌中顯示為 `[DatabaseName]`，使日誌更清晰易讀
4. **敏感數據保護**：自動隱藏參數中的敏感信息（密碼、密鑰等）
5. **SQL 長度限制**：確保 SQL 語句不會超過 2048 個字符，避免日誌過大

這個設計既簡單又靈活，適用於多數據庫環境，特別是在微服務架構或需要連接多個數據源的應用程序中。