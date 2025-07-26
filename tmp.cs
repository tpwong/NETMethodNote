# Complete Implementation: DapperLogger with Enhanced Method Detection

Here's the complete implementation with a comprehensive list of Dapper-related method names for more accurate method detection:

## 1. DapperLoggerOptions Configuration Class

```csharp
using System;

public class DapperLoggerOptions
{
    /// <summary>
    /// Minimum execution time (milliseconds) to log. Queries faster than this will not be logged.
    /// Default value: 200 milliseconds
    /// </summary>
    public int MinimumLoggingThresholdMs { get; set; } = 200;
    
    /// <summary>
    /// Execution time threshold (milliseconds), exceeding this value will log a warning
    /// Default value: 2000 milliseconds (2 seconds)
    /// </summary>
    public int SlowExecutionThresholdMs { get; set; } = 2000;
    
    /// <summary>
    /// Maximum length of SQL statements
    /// Default value: 2048 characters
    /// </summary>
    public int MaxSqlLength { get; set; } = 2048;
    
    /// <summary>
    /// Enable parameter sensitive information filtering
    /// Default value: true
    /// </summary>
    public bool EnableParameterSanitization { get; set; } = true;
    
    /// <summary>
    /// Keywords for parameter names that should be hidden
    /// </summary>
    public string[] SensitiveParameterKeywords { get; set; } = new string[] 
    {
        "password", "secret", "key", "token", "credential", "auth"
    };
}
```

## 2. DapperLogger Attribute Implementation with Comprehensive Method Detection

```csharp
using AspectCore.DynamicProxy;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Provides automatic logging for Dapper database operations using AOP (Aspect-Oriented Programming).
/// This attribute intercepts method calls to Dapper's data access methods and records detailed execution
/// information with minimal performance impact.
/// </summary>
/// <remarks>
/// <para>
/// The attribute captures and logs essential information about database operations:
/// - SQL statements being executed
/// - Parameter values (with sensitive data protection)
/// - Execution time
/// - Database identifier for multi-database environments
/// - Errors and exceptions
/// </para>
/// <para>
/// Key features include:
/// - Performance-based logging with configurable thresholds for minimum logging and slow query warnings
/// - Smart method detection for Dapper operations through extensive method name matching
/// - Database identification through attribute parameter or automatic class name detection for DbContext
/// - Privacy protection through parameter sanitization of sensitive information
/// - Minimal overhead using ThreadLocal resources and conditional logging
/// </para>
/// <para>
/// This attribute can be applied to classes or interfaces that use Dapper for data access.
/// If applied to a class, all Dapper methods in that class will be logged.
/// If applied to an interface, all implementations of that interface will have their Dapper methods logged.
/// </para>
/// <para>
/// For DbContext classes or classes with names ending in "DbContext", the class name will automatically
/// be used as the database identifier unless explicitly specified.
/// </para>
/// </remarks>
/// <example>
/// Basic usage with explicit database name:
/// <code>
/// [DapperLogger("OrdersDatabase")]
/// public class OrderRepository
/// {
///     // Dapper methods will be automatically logged
/// }
/// </code>
/// 
/// Automatic database identification with DbContext:
/// <code>
/// // Class name will be used as database identifier
/// public class ProductDbContext : DbContext
/// {
///     // Dapper methods will be automatically logged
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface)]
public class DapperLoggerAttribute : AbstractInterceptorAttribute
{
    // Comprehensive list of Dapper method names
    private static readonly HashSet<string> _dapperMethods = new(StringComparer.Ordinal)
    {
        // Core Query Methods - Standard Dapper
        "Query", "QueryFirst", "QueryFirstOrDefault", "QuerySingle", "QuerySingleOrDefault", "QueryMultiple",
        "QueryAsync", "QueryFirstAsync", "QueryFirstOrDefaultAsync", "QuerySingleAsync", "QuerySingleOrDefaultAsync", "QueryMultipleAsync",
        
        // Query Methods with Type Parameters
        "Query<T>", "QueryFirst<T>", "QueryFirstOrDefault<T>", "QuerySingle<T>", "QuerySingleOrDefault<T>",
        "QueryAsync<T>", "QueryFirstAsync<T>", "QueryFirstOrDefaultAsync<T>", "QuerySingleAsync<T>", "QuerySingleOrDefaultAsync<T>",
        
        // Execute Methods - Standard Dapper
        "Execute", "ExecuteScalar", "ExecuteReader",
        "ExecuteAsync", "ExecuteScalarAsync", "ExecuteReaderAsync",
        
        // Execute Methods with Type Parameters
        "ExecuteScalar<T>", "ExecuteScalarAsync<T>",
        
        // Dapper.Contrib Methods
        "Get", "GetAll", "GetAsync", "GetAllAsync",
        "Insert", "Update", "Delete", "DeleteAll",
        "InsertAsync", "UpdateAsync", "DeleteAsync", "DeleteAllAsync",
        
        // Dapper.SimpleCRUD Methods
        "Get<T>", "GetList<T>", "GetListPaged<T>", "GetListFiltered<T>",
        "GetAsync<T>", "GetListAsync<T>", "GetListPagedAsync<T>", "GetListFilteredAsync<T>",
        "Insert<T>", "Update<T>", "Delete<T>", "DeleteList<T>", "RecordCount<T>",
        "InsertAsync<T>", "UpdateAsync<T>", "DeleteAsync<T>", "DeleteListAsync<T>", "RecordCountAsync<T>",
        
        // Common Repository Pattern Method Names
        "Find", "FindAll", "FindById", "FindByIds", "FindByName", "FindByQuery",
        "FindAsync", "FindAllAsync", "FindByIdAsync", "FindByIdsAsync", "FindByNameAsync", "FindByQueryAsync",
        
        // Common CRUD Operations in Repositories
        "Add", "AddRange", "Create", "CreateBulk", "Save", "SaveAll", "SaveChanges",
        "AddAsync", "AddRangeAsync", "CreateAsync", "CreateBulkAsync", "SaveAsync", "SaveAllAsync", "SaveChangesAsync",
        "Modify", "ModifyAsync", "UpdateById", "UpdateByIdAsync", "UpdateRange", "UpdateRangeAsync",
        "Remove", "RemoveAll", "RemoveById", "RemoveByIds", "RemoveRange",
        "RemoveAsync", "RemoveAllAsync", "RemoveByIdAsync", "RemoveByIdsAsync", "RemoveRangeAsync",
        
        // Bulk Operations
        "BulkInsert", "BulkUpdate", "BulkDelete", "BulkMerge",
        "BulkInsertAsync", "BulkUpdateAsync", "BulkDeleteAsync", "BulkMergeAsync",
        
        // Stored Procedure Related
        "ExecuteProcedure", "ExecuteProcedureAsync", "ExecuteStoredProcedure", "ExecuteStoredProcedureAsync",
        "ExecuteProc", "ExecuteProcAsync", "ExecProc", "ExecProcAsync",
        
        // Transaction Related
        "ExecuteInTransaction", "ExecuteInTransactionAsync",
        
        // Count Operations
        "Count", "CountAsync", "CountAll", "CountAllAsync", "GetCount", "GetCountAsync",
        
        // Exists Operations
        "Exists", "ExistsAsync", "Any", "AnyAsync"
    };

    // Use ThreadLocal to avoid Stopwatch creation
    private static readonly ThreadLocal<Stopwatch> _stopwatch = 
        new(() => new Stopwatch());
    
    // Configuration options
    private static DapperLoggerOptions _options = new DapperLoggerOptions();
    
    // Database identifier
    private readonly string _databaseIdentifier;
    
    // Default constructor
    public DapperLoggerAttribute() : this(null)
    {
    }
    
    // Constructor with database identifier
    public DapperLoggerAttribute(string databaseIdentifier)
    {
        _databaseIdentifier = databaseIdentifier;
    }
    
    // Static method to set configuration options
    internal static void SetOptions(DapperLoggerOptions options)
    {
        _options = options ?? new DapperLoggerOptions();
    }

    public async override Task Invoke(AspectContext context, AspectDelegate next)
    {
        // Get method name and type
        var methodName = context.ImplementationMethod.Name;
        var declaringType = context.ImplementationMethod.DeclaringType;
        var typeName = declaringType?.Name ?? "Unknown";
        
        // Determine database identifier
        string dbIdentifier = _databaseIdentifier;
        
        // If no database identifier is specified and class name is DbContext or ends with DbContext, use class name as identifier
        if (string.IsNullOrEmpty(dbIdentifier))
        {
            if (typeName.Equals("DbContext", StringComparison.OrdinalIgnoreCase) || 
                typeName.EndsWith("DbContext", StringComparison.OrdinalIgnoreCase))
            {
                dbIdentifier = typeName;
            }
        }
        
        // Check if it's a Dapper method
        bool isDapperMethod = _dapperMethods.Contains(methodName) || 
            _dapperMethods.Any(prefix => methodName.StartsWith(prefix, StringComparison.Ordinal));
        
        // Also check for methods in repositories or services that might use Dapper internally
        if (!isDapperMethod && (
            declaringType.Name.EndsWith("Repository", StringComparison.OrdinalIgnoreCase) ||
            declaringType.Name.EndsWith("DataAccess", StringComparison.OrdinalIgnoreCase) ||
            declaringType.Name.EndsWith("DataService", StringComparison.OrdinalIgnoreCase) ||
            declaringType.Name.EndsWith("DbService", StringComparison.OrdinalIgnoreCase)))
        {
            // Look for method names that imply database operations
            if (methodName.StartsWith("Get", StringComparison.OrdinalIgnoreCase) ||
                methodName.StartsWith("Find", StringComparison.OrdinalIgnoreCase) ||
                methodName.StartsWith("Load", StringComparison.OrdinalIgnoreCase) ||
                methodName.StartsWith("Fetch", StringComparison.OrdinalIgnoreCase) ||
                methodName.StartsWith("Select", StringComparison.OrdinalIgnoreCase) ||
                methodName.StartsWith("Insert", StringComparison.OrdinalIgnoreCase) ||
                methodName.StartsWith("Update", StringComparison.OrdinalIgnoreCase) ||
                methodName.StartsWith("Delete", StringComparison.OrdinalIgnoreCase) ||
                methodName.StartsWith("Save", StringComparison.OrdinalIgnoreCase) ||
                methodName.StartsWith("Create", StringComparison.OrdinalIgnoreCase) ||
                methodName.StartsWith("Modify", StringComparison.OrdinalIgnoreCase) ||
                methodName.StartsWith("Remove", StringComparison.OrdinalIgnoreCase) ||
                methodName.StartsWith("Execute", StringComparison.OrdinalIgnoreCase) ||
                methodName.StartsWith("Count", StringComparison.OrdinalIgnoreCase) ||
                methodName.StartsWith("Exists", StringComparison.OrdinalIgnoreCase) ||
                methodName.StartsWith("Any", StringComparison.OrdinalIgnoreCase))
            {
                isDapperMethod = true;
            }
        }
        
        if (!isDapperMethod)
        {
            await next(context);
            return;
        }

        // Get Stopwatch
        var stopwatch = _stopwatch.Value;
        stopwatch.Reset();
        stopwatch.Start();
        
        Exception exception = null;
        
        try
        {
            // Execute original method
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
            
            // Skip logging if execution time is below the minimum threshold and there's no exception
            if (elapsedMs < _options.MinimumLoggingThresholdMs && exception == null)
            {
                return;
            }
            
            try
            {
                // Extract SQL and parameters
                string sql = ExtractAndTruncateSql(context.Parameters);
                object parameters = ExtractAndSanitizeParameters(context.Parameters);
                
                // Build database identifier string
                string dbInfo = !string.IsNullOrEmpty(dbIdentifier) 
                    ? $"[{dbIdentifier}] " 
                    : string.Empty;
                
                // Determine log level based on execution time and exceptions
                LogEventLevel logLevel = exception != null 
                    ? LogEventLevel.Error 
                    : (elapsedMs > _options.SlowExecutionThresholdMs 
                        ? LogEventLevel.Warning 
                        : LogEventLevel.Information);
                
                // Build log message
                string logMessage = logLevel == LogEventLevel.Warning
                    ? "Slow Dapper execution detected! Executed in {ElapsedMilliseconds}ms (threshold: {ThresholdMs}ms). {DbInfo}Method: {Method}, SQL: {Sql}, Parameters: {@Parameters}"
                    : "Dapper executed in {ElapsedMilliseconds}ms. {DbInfo}Method: {Method}, SQL: {Sql}, Parameters: {@Parameters}";
                
                // Log based on log level
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
                else if (logLevel == LogEventLevel.Warning)
                {
                    Log.Warning(
                        logMessage,
                        elapsedMs,
                        _options.SlowExecutionThresholdMs,
                        dbInfo,
                        $"{typeName}.{methodName}",
                        sql,
                        parameters);
                }
                else
                {
                    Log.Information(
                        logMessage,
                        elapsedMs,
                        _options.SlowExecutionThresholdMs,
                        dbInfo,
                        $"{typeName}.{methodName}",
                        sql,
                        parameters);
                }
            }
            catch (Exception ex)
            {
                // Log errors in the logging process itself, avoid affecting the main business logic
                Log.Error(ex, "Error while logging Dapper execution");
            }
        }
    }

    // Extract and truncate SQL statement
    private string ExtractAndTruncateSql(object[] parameters)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i] is string arg && !string.IsNullOrWhiteSpace(arg))
            {
                // Check if this parameter looks like SQL
                if (IsSqlStatement(arg))
                {
                    // Check length and truncate
                    if (arg.Length > _options.MaxSqlLength)
                    {
                        return arg.Substring(0, _options.MaxSqlLength) + "... [TRUNCATED]";
                    }
                    return arg;
                }
            }
        }
        return "Unknown SQL";
    }
    
    // Simple check to see if a string looks like SQL
    private bool IsSqlStatement(string text)
    {
        string upperText = text.ToUpperInvariant().TrimStart();
        
        return upperText.StartsWith("SELECT ") ||
               upperText.StartsWith("INSERT ") ||
               upperText.StartsWith("UPDATE ") ||
               upperText.StartsWith("DELETE ") ||
               upperText.StartsWith("WITH ") ||
               upperText.StartsWith("MERGE ") ||
               upperText.StartsWith("CREATE ") ||
               upperText.StartsWith("ALTER ") ||
               upperText.StartsWith("DROP ") ||
               upperText.StartsWith("EXEC ") ||
               upperText.StartsWith("EXECUTE ") ||
               upperText.StartsWith("CALL ") ||
               upperText.StartsWith("DECLARE ") ||
               upperText.StartsWith("BEGIN ") ||
               upperText.StartsWith("SET ");
    }

    // Extract and sanitize parameters
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
                return _options.EnableParameterSanitization 
                    ? SanitizeObject(parameters[i]) 
                    : parameters[i];
            }
        }
        return null;
    }
    
    // Sanitize object, hide sensitive information
    private object SanitizeObject(object obj)
    {
        if (obj == null)
        {
            return null;
        }
        
        // Handle simple types directly
        if (obj.GetType().IsPrimitive || 
            obj is string || 
            obj is DateTime || 
            obj is DateTimeOffset || 
            obj is TimeSpan || 
            obj is decimal || 
            obj is Guid)
        {
            return obj;
        }
        
        // Handle dictionaries
        if (obj is IDictionary<string, object> dictionary)
        {
            var sanitizedDict = new Dictionary<string, object>();
            
            foreach (var pair in dictionary)
            {
                bool isSensitive = _options.SensitiveParameterKeywords.Any(keyword => 
                    pair.Key.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                    
                sanitizedDict[pair.Key] = isSensitive ? "***REDACTED***" : pair.Value;
            }
            
            return sanitizedDict;
        }
        
        // Handle anonymous types and other objects with properties
        var properties = obj.GetType().GetProperties();
        var sanitizedObject = new Dictionary<string, object>();
        
        foreach (var prop in properties)
        {
            var propName = prop.Name;
            var value = prop.GetValue(obj);
            
            bool isSensitive = _options.SensitiveParameterKeywords.Any(keyword => 
                propName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                
            if (isSensitive)
            {
                sanitizedObject[propName] = "***REDACTED***";
            }
            else
            {
                sanitizedObject[propName] = value;
            }
        }
        
        return sanitizedObject;
    }
}
```

## 3. AspectCore Extensions for DapperLogger

```csharp
using AspectCore.Configuration;
using AspectCore.DynamicProxy;
using AspectCore.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Reflection;

public static class AspectCoreExtensions
{
    public static IServiceCollection AddDapperWithLogging(this IServiceCollection services)
    {
        return AddDapperWithLogging(services, options => {});
    }
    
    public static IServiceCollection AddDapperWithLogging(this IServiceCollection services, 
        Action<DapperLoggerOptions> configureOptions)
    {
        // Add configuration options
        services.Configure<DapperLoggerOptions>(configureOptions);
        
        // Register configuration options setup
        services.AddSingleton<IStartupFilter, DapperLoggerStartupFilter>();
        
        // Configure AspectCore
        services.ConfigureDynamicProxy(config =>
        {
            // Intercept DbContext, classes with DapperLogger attribute, and classes named DbContext
            config.Interceptors.AddTyped<DapperLoggerAttribute>(method => 
                ShouldInterceptMethod(method));
            
            // Configure non-proxy methods
            config.NonAspectPredicates.AddNamespace("System.*");
            config.NonAspectPredicates.AddNamespace("Microsoft.*");
            config.NonAspectPredicates.AddNamespace("AspectCore.*");
        });
        
        return services;
    }
    
    // Check if method should be intercepted
    private static bool ShouldInterceptMethod(MethodInfo method)
    {
        if (method?.DeclaringType == null)
            return false;
        
        var declaringType = method.DeclaringType;
        var typeName = declaringType.Name;
        
        // Check if DapperLogger attribute exists
        var hasAttribute = declaringType.GetCustomAttributes(typeof(DapperLoggerAttribute), true).Any() ||
                          declaringType.GetInterfaces()
                              .Any(i => i.GetCustomAttributes(typeof(DapperLoggerAttribute), true).Any());
        
        // Check if it's a DbContext subclass
        var isDbContext = declaringType.IsSubclassOf(typeof(DbContext));
        
        // Check if class name is DbContext or ends with DbContext
        var isDbContextName = typeName.Equals("DbContext", StringComparison.OrdinalIgnoreCase) || 
                             typeName.EndsWith("DbContext", StringComparison.OrdinalIgnoreCase);
        
        // Check if class name ends with Repository, DataAccess, etc.
        var isRepositoryOrDataAccess = typeName.EndsWith("Repository", StringComparison.OrdinalIgnoreCase) ||
                                     typeName.EndsWith("DataAccess", StringComparison.OrdinalIgnoreCase) ||
                                     typeName.EndsWith("DataService", StringComparison.OrdinalIgnoreCase) ||
                                     typeName.EndsWith("DbService", StringComparison.OrdinalIgnoreCase);
                                     
        return hasAttribute || isDbContext || isDbContextName || isRepositoryOrDataAccess;
    }
    
    // StartupFilter to configure DapperLogger options at startup
    private class DapperLoggerStartupFilter : IStartupFilter
    {
        private readonly IOptions<DapperLoggerOptions> _options;
        
        public DapperLoggerStartupFilter(IOptions<DapperLoggerOptions> options)
        {
            _options = options;
        }
        
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            // Set static configuration for DapperLogger
            DapperLoggerAttribute.SetOptions(_options.Value);
            
            return next;
        }
    }
}

// Add IStartupFilter interface
public interface IStartupFilter
{
    Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next);
}

public interface IApplicationBuilder
{
    IServiceProvider ApplicationServices { get; }
}
```

## 4. Startup Class Configuration

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

public class Startup
{
    private readonly IConfiguration _configuration;
    
    public Startup(IConfiguration configuration)
    {
        _configuration = configuration;
    }
    
    public void ConfigureServices(IServiceCollection services)
    {
        // Add DapperLogger and configure options
        services.AddDapperWithLogging(options => 
        {
            // Load settings from configuration file
            _configuration.GetSection("DapperLogger").Bind(options);
        });
        
        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(_configuration)
            .CreateLogger();
        
        // Other service configurations...
    }
    
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        // Application configurations...
    }
}
```

## 5. Configuration File (appsettings.json)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/log-.txt",
          "rollingInterval": "Day",
          "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
        }
      }
    ],
    "Enrich": [ "FromLogContext", "WithMachineName", "WithThreadId" ]
  },
  "DapperLogger": {
    "MinimumLoggingThresholdMs": 200,
    "SlowExecutionThresholdMs": 2000,
    "MaxSqlLength": 4096,
    "EnableParameterSanitization": true,
    "SensitiveParameterKeywords": [
      "password",
      "secret",
      "key",
      "token",
      "credential",
      "auth"
    ]
  }
}
```

## 6. Sample Repository Implementations

### 6.1 Basic Repository with Dapper

```csharp
using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

[DapperLogger("ProductDatabase")]
public class ProductRepository : IProductRepository
{
    private readonly IDbConnection _connection;
    
    public ProductRepository(IDbConnection connection)
    {
        _connection = connection;
    }
    
    public async Task<IEnumerable<Product>> GetAllProductsAsync()
    {
        return await _connection.QueryAsync<Product>("SELECT * FROM Products");
    }
    
    public async Task<Product> GetProductByIdAsync(int id)
    {
        return await _connection.QueryFirstOrDefaultAsync<Product>(
            "SELECT * FROM Products WHERE Id = @Id", 
            new { Id = id });
    }
    
    public async Task<IEnumerable<Product>> GetProductsByCategoryAsync(int categoryId)
    {
        return await _connection.QueryAsync<Product>(
            "SELECT * FROM Products WHERE CategoryId = @CategoryId",
            new { CategoryId = categoryId });
    }
    
    public async Task<int> CreateProductAsync(Product product)
    {
        const string sql = @"
            INSERT INTO Products (Name, Description, Price, CategoryId, StockQuantity, CreatedAt)
            VALUES (@Name, @Description, @Price, @CategoryId, @StockQuantity, @CreatedAt);
            SELECT CAST(SCOPE_IDENTITY() as int)";
            
        return await _connection.QuerySingleAsync<int>(sql, product);
    }
    
    public async Task<bool> UpdateProductAsync(Product product)
    {
        const string sql = @"
            UPDATE Products 
            SET Name = @Name, 
                Description = @Description, 
                Price = @Price, 
                CategoryId = @CategoryId, 
                StockQuantity = @StockQuantity,
                UpdatedAt = @UpdatedAt
            WHERE Id = @Id";
            
        int affectedRows = await _connection.ExecuteAsync(sql, product);
        return affectedRows > 0;
    }
    
    public async Task<bool> DeleteProductAsync(int id)
    {
        const string sql = "DELETE FROM Products WHERE Id = @Id";
        int affectedRows = await _connection.ExecuteAsync(sql, new { Id = id });
        return affectedRows > 0;
    }
    
    public async Task<int> GetProductCountAsync()
    {
        const string sql = "SELECT COUNT(*) FROM Products";
        return await _connection.ExecuteScalarAsync<int>(sql);
    }
    
    public async Task<bool> ProductExistsAsync(int id)
    {
        const string sql = "SELECT COUNT(1) FROM Products WHERE Id = @Id";
        int count = await _connection.ExecuteScalarAsync<int>(sql, new { Id = id });
        return count > 0;
    }
    
    public async Task<IEnumerable<ProductSummary>> GetProductSummariesAsync()
    {
        const string sql = @"
            SELECT p.Id, p.Name, p.Price, c.Name as CategoryName, 
                   (SELECT COUNT(*) FROM OrderItems oi WHERE oi.ProductId = p.Id) as OrderCount
            FROM Products p
            JOIN Categories c ON p.CategoryId = c.Id";
            
        return await _connection.QueryAsync<ProductSummary>(sql);
    }
    
    public async Task<IEnumerable<Product>> SearchProductsAsync(string searchTerm, decimal? minPrice, decimal? maxPrice)
    {
        string sql = "SELECT * FROM Products WHERE 1=1";
        var parameters = new DynamicParameters();
        
        if (!string.IsNullOrEmpty(searchTerm))
        {
            sql += " AND (Name LIKE @SearchTerm OR Description LIKE @SearchTerm)";
            parameters.Add("SearchTerm", $"%{searchTerm}%");
        }
        
        if (minPrice.HasValue)
        {
            sql += " AND Price >= @MinPrice";
            parameters.Add("MinPrice", minPrice.Value);
        }
        
        if (maxPrice.HasValue)
        {
            sql += " AND Price <= @MaxPrice";
            parameters.Add("MaxPrice", maxPrice.Value);
        }
        
        return await _connection.QueryAsync<Product>(sql, parameters);
    }
    
    public async Task<int> BulkInsertProductsAsync(IEnumerable<Product> products)
    {
        const string sql = @"
            INSERT INTO Products (Name, Description, Price, CategoryId, StockQuantity, CreatedAt)
            VALUES (@Name, @Description, @Price, @CategoryId, @StockQuantity, @CreatedAt)";
            
        return await _connection.ExecuteAsync(sql, products);
    }
    
    public async Task<IEnumerable<ProductSales>> GetTopSellingProductsAsync(int count)
    {
        const string sql = @"
            SELECT TOP(@Count) p.Id, p.Name, SUM(oi.Quantity) as TotalSold, SUM(oi.Quantity * oi.UnitPrice) as Revenue
            FROM Products p
            JOIN OrderItems oi ON p.Id = oi.ProductId
            GROUP BY p.Id, p.Name
            ORDER BY TotalSold DESC";
            
        return await _connection.QueryAsync<ProductSales>(sql, new { Count = count });
    }
    
    public async Task<IEnumerable<Product>> GetProductsWithPaginationAsync(int pageNumber, int pageSize)
    {
        const string sql = @"
            SELECT * FROM Products
            ORDER BY Id
            OFFSET @Offset ROWS
            FETCH NEXT @PageSize ROWS ONLY";
            
        int offset = (pageNumber - 1) * pageSize;
        return await _connection.QueryAsync<Product>(sql, new { Offset = offset, PageSize = pageSize });
    }
}

public interface IProductRepository
{
    Task<IEnumerable<Product>> GetAllProductsAsync();
    Task<Product> GetProductByIdAsync(int id);
    Task<IEnumerable<Product>> GetProductsByCategoryAsync(int categoryId);
    Task<int> CreateProductAsync(Product product);
    Task<bool> UpdateProductAsync(Product product);
    Task<bool> DeleteProductAsync(int id);
    Task<int> GetProductCountAsync();
    Task<bool> ProductExistsAsync(int id);
    Task<IEnumerable<ProductSummary>> GetProductSummariesAsync();
    Task<IEnumerable<Product>> SearchProductsAsync(string searchTerm, decimal? minPrice, decimal? maxPrice);
    Task<int> BulkInsertProductsAsync(IEnumerable<Product> products);
    Task<IEnumerable<ProductSales>> GetTopSellingProductsAsync(int count);
    Task<IEnumerable<Product>> GetProductsWithPaginationAsync(int pageNumber, int pageSize);
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public decimal Price { get; set; }
    public int CategoryId { get; set; }
    public int StockQuantity { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class ProductSummary
{
    public int Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public string CategoryName { get; set; }
    public int OrderCount { get; set; }
}

public class ProductSales
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int TotalSold { get; set; }
    public decimal Revenue { get; set; }
}
```

### 6.2 DbContext with Dapper Integration

```csharp
using Dapper;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

// No need to explicitly add DapperLogger attribute, will automatically use class name as identifier
public class ApplicationDbContext : DbContext
{
    private readonly IDbConnection _connection;
    
    // Standard DbSet properties for EF Core
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<Customer> Customers { get; set; }
    
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IDbConnection connection) 
        : base(options)
    {
        _connection = connection;
    }
    
    // Dapper methods for optimized queries
    public async Task<IEnumerable<OrderSummary>> GetOrderSummariesAsync(DateTime startDate, DateTime endDate)
    {
        const string sql = @"
            SELECT o.Id, o.OrderDate, c.Name as CustomerName, 
                   COUNT(oi.Id) as ItemCount, 
                   SUM(oi.Quantity * oi.UnitPrice) as TotalAmount
            FROM Orders o
            JOIN Customers c ON o.CustomerId = c.Id
            JOIN OrderItems oi ON o.Id = oi.OrderId
            WHERE o.OrderDate BETWEEN @StartDate AND @EndDate
            GROUP BY o.Id, o.OrderDate, c.Name
            ORDER BY o.OrderDate DESC";
            
        return await _connection.QueryAsync<OrderSummary>(
            sql, 
            new { StartDate = startDate, EndDate = endDate });
    }
    
    public async Task<OrderDetails> GetOrderDetailsAsync(int orderId)
    {
        const string orderSql = @"
            SELECT o.Id, o.OrderDate, o.ShippedDate, o.Status,
                   c.Id as CustomerId, c.Name as CustomerName, c.Email, c.Phone
            FROM Orders o
            JOIN Customers c ON o.CustomerId = c.Id
            WHERE o.Id = @OrderId";
            
        const string itemsSql = @"
            SELECT oi.Id, oi.ProductId, p.Name as ProductName, 
                   oi.Quantity, oi.UnitPrice, (oi.Quantity * oi.UnitPrice) as TotalPrice
            FROM OrderItems oi
            JOIN Products p ON oi.ProductId = p.Id
            WHERE oi.OrderId = @OrderId";
            
        // Execute multiple queries in one connection
        using (var multi = await _connection.QueryMultipleAsync(orderSql + ";" + itemsSql, new { OrderId = orderId }))
        {
            var order = await multi.ReadFirstOrDefaultAsync<OrderDetails>();
            if (order != null)
            {
                order.Items = (await multi.ReadAsync<OrderItemDetails>()).ToList();
            }
            return order;
        }
    }
    
    public async Task<IEnumerable<CustomerActivity>> GetTopCustomersAsync(int count)
    {
        const string sql = @"
            SELECT TOP(@Count) c.Id, c.Name, c.Email,
                   COUNT(DISTINCT o.Id) as OrderCount,
                   SUM(oi.Quantity * oi.UnitPrice) as TotalSpent,
                   MAX(o.OrderDate) as LastOrderDate
            FROM Customers c
            JOIN Orders o ON c.Id = o.CustomerId
            JOIN OrderItems oi ON o.Id = oi.OrderId
            GROUP BY c.Id, c.Name, c.Email
            ORDER BY TotalSpent DESC";
            
        return await _connection.QueryAsync<CustomerActivity>(sql, new { Count = count });
    }
    
    public async Task<IDictionary<string, int>> GetOrderStatusDistributionAsync()
    {
        const string sql = @"
            SELECT Status, COUNT(*) as Count
            FROM Orders
            GROUP BY Status";
            
        var results = await _connection.QueryAsync<StatusCount>(sql);
        return results.ToDictionary(x => x.Status, x => x.Count);
    }
    
    public async Task<bool> CreateOrderAsync(Order order, IEnumerable<OrderItem> items)
    {
        // Use Dapper transaction for better performance in bulk operations
        using (var transaction = _connection.BeginTransaction())
        {
            try
            {
                // Insert order
                const string orderSql = @"
                    INSERT INTO Orders (CustomerId, OrderDate, Status)
                    VALUES (@CustomerId, @OrderDate, @Status);
                    SELECT CAST(SCOPE_IDENTITY() as int)";
                    
                int orderId = await _connection.QuerySingleAsync<int>(orderSql, order, transaction);
                
                // Insert order items
                const string itemSql = @"
                    INSERT INTO OrderItems (OrderId, ProductId, Quantity, UnitPrice)
                    VALUES (@OrderId, @ProductId, @Quantity, @UnitPrice)";
                    
                foreach (var item in items)
                {
                    item.OrderId = orderId;
                }
                
                await _connection.ExecuteAsync(itemSql, items, transaction);
                
                // Update product stock
                const string stockSql = @"
                    UPDATE Products
                    SET StockQuantity = StockQuantity - @Quantity
                    WHERE Id = @ProductId";
                    
                await _connection.ExecuteAsync(stockSql, items, transaction);
                
                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
    
    public async Task<IEnumerable<SalesReport>> GetMonthlySalesReportAsync(int year)
    {
        const string sql = @"
            SELECT 
                MONTH(o.OrderDate) as Month,
                SUM(oi.Quantity * oi.UnitPrice) as Revenue,
                COUNT(DISTINCT o.Id) as OrderCount,
                COUNT(DISTINCT o.CustomerId) as CustomerCount,
                AVG(oi.Quantity * oi.UnitPrice) as AverageOrderValue
            FROM Orders o
            JOIN OrderItems oi ON o.Id = oi.OrderId
            WHERE YEAR(o.OrderDate) = @Year
            GROUP BY MONTH(o.OrderDate)
            ORDER BY Month";
            
        return await _connection.QueryAsync<SalesReport>(sql, new { Year = year });
    }
    
    public async Task<DashboardStats> GetDashboardStatsAsync()
    {
        const string sql = @"
            SELECT 
                (SELECT COUNT(*) FROM Orders WHERE Status = 'Pending') as PendingOrders,
                (SELECT COUNT(*) FROM Orders WHERE OrderDate >= DATEADD(day, -30, GETDATE())) as RecentOrders,
                (SELECT COUNT(*) FROM Customers) as TotalCustomers,
                (SELECT COUNT(*) FROM Products WHERE StockQuantity < 10) as LowStockProducts,
                (SELECT SUM(oi.Quantity * oi.UnitPrice) FROM OrderItems oi JOIN Orders o ON oi.OrderId = o.Id WHERE o.OrderDate >= DATEADD(day, -30, GETDATE())) as MonthlyRevenue,
                (SELECT COUNT(*) FROM Products) as TotalProducts";
                
        return await _connection.QuerySingleAsync<DashboardStats>(sql);
    }
}

public class OrderSummary
{
    public int Id { get; set; }
    public DateTime OrderDate { get; set; }
    public string CustomerName { get; set; }
    public int ItemCount { get; set; }
    public decimal TotalAmount { get; set; }
}

public class OrderDetails
{
    public int Id { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime? ShippedDate { get; set; }
    public string Status { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public List<OrderItemDetails> Items { get; set; }
}

public class OrderItemDetails
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
}

public class CustomerActivity
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public int OrderCount { get; set; }
    public decimal TotalSpent { get; set; }
    public DateTime LastOrderDate { get; set; }
}

public class StatusCount
{
    public string Status { get; set; }
    public int Count { get; set; }
}

public class SalesReport
{
    public int Month { get; set; }
    public decimal Revenue { get; set; }
    public int OrderCount { get; set; }
    public int CustomerCount { get; set; }
    public decimal AverageOrderValue { get; set; }
}

public class DashboardStats
{
    public int PendingOrders { get; set; }
    public int RecentOrders { get; set; }
    public int TotalCustomers { get; set; }
    public int LowStockProducts { get; set; }
    public decimal MonthlyRevenue { get; set; }
    public int TotalProducts { get; set; }
}

public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime? ShippedDate { get; set; }
    public string Status { get; set; }
    
    // Navigation properties
    public Customer Customer { get; set; }
    public ICollection<OrderItem> Items { get; set; }
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    
    // Navigation properties
    public Order Order { get; set; }
}

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public string Address { get; set; }
    
    // Navigation properties
    public ICollection<Order> Orders { get; set; }
}
```

## 7. Summary and Key Features

This comprehensive implementation provides a robust solution for logging Dapper database operations with the following features:

1. **Extensive Method Detection**:
   - Comprehensive list of Dapper method names for better detection
   - Support for repository pattern method naming conventions
   - Automatic detection of database-related methods in repository classes
   - SQL statement validation to ensure we're logging actual SQL queries

2. **Smart Logging Behavior**:
   - Skip logging queries faster than 200ms (configurable) to reduce noise
   - Log slow queries (> 2000ms) as warnings for easy identification
   - Always log errors regardless of execution time
   - Clear log messages with execution time, method name, database identifier, and SQL

3. **Enhanced Parameter Handling**:
   - Better detection of parameter objects
   - Support for dictionaries, anonymous types, and complex objects
   - Proper sanitization of sensitive information based on configurable keywords
   - Protection against logging issues affecting main application flow

4. **Flexible Configuration**:
   - Configurable minimum logging threshold (default 200ms)
   - Configurable slow execution threshold (default 2000ms)
   - Configurable SQL length limit (default 4096 characters)
   - Customizable sensitive parameter keywords

5. **Database Identification**:
   - Explicit via attribute: `[DapperLogger("DatabaseName")]`
   - Automatic for DbContext classes
   - Clear display in logs: `[DatabaseName] Method: Class.Method`

This implementation provides comprehensive coverage of Dapper-related methods while maintaining good performance by avoiding excessive logging. It's ideal for monitoring database performance in production environments and quickly identifying slow queries or problematic operations.
