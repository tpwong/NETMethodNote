# Complete Implementation: DapperLogger with Database Identification and Configurable Thresholds

Below is the complete implementation of the DapperLogger with all features, including the minimum execution time threshold to avoid logging fast queries:

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

## 2. DapperLogger Attribute Implementation

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

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface)]
public class DapperLoggerAttribute : AbstractInterceptorAttribute
{
    // Predefined Dapper method names
    private static readonly HashSet<string> _dapperMethods = new(StringComparer.Ordinal)
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
                // Check length and truncate
                if (arg.Length > _options.MaxSqlLength)
                {
                    return arg.Substring(0, _options.MaxSqlLength) + "... [TRUNCATED]";
                }
                return arg;
            }
        }
        return "Unknown SQL";
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
            
            bool isSensitive = _options.SensitiveParameterKeywords.Any(keyword => 
                propName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                
            if (isSensitive)
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
        
        return hasAttribute || isDbContext || isDbContextName;
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

## 6. Usage Examples

### 6.1 Repository with Explicit Database Identifier

```csharp
using Dapper;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

// Explicitly specify database identifier
[DapperLogger("OracleDB")]
public class ProductRepository : IProductRepository
{
    private readonly IDbConnection _connection;
    
    public ProductRepository(IDbConnection connection)
    {
        _connection = connection;
    }
    
    public async Task<IEnumerable<Product>> GetAllProductsAsync()
    {
        // Queries that take less than MinimumLoggingThresholdMs (200ms) will not be logged
        // Queries that take more than SlowExecutionThresholdMs (2000ms) will be logged as warnings
        return await _connection.QueryAsync<Product>("SELECT * FROM Products");
    }
    
    public async Task<Product> GetProductByIdAsync(int id)
    {
        return await _connection.QueryFirstOrDefaultAsync<Product>(
            "SELECT * FROM Products WHERE Id = @Id", 
            new { Id = id });
    }
    
    public async Task<int> CreateProductAsync(Product product)
    {
        return await _connection.ExecuteAsync(
            "INSERT INTO Products (Name, Price, CategoryId) VALUES (@Name, @Price, @CategoryId)",
            product);
    }
}
```

### 6.2 DbContext with Automatic Database Identification

```csharp
using Dapper;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

// No need to explicitly add DapperLogger attribute, will automatically use class name as identifier
public class SalesDbContext : DbContext
{
    private readonly IDbConnection _connection;
    
    public SalesDbContext(DbContextOptions options, IDbConnection connection) 
        : base(options)
    {
        _connection = connection;
    }
    
    public async Task<IEnumerable<Sale>> GetSalesAsync(DateTime startDate)
    {
        // Queries that take less than MinimumLoggingThresholdMs (200ms) will not be logged
        // Queries that take more than SlowExecutionThresholdMs (2000ms) will be logged as warnings
        return await _connection.QueryAsync<Sale>(
            "SELECT * FROM Sales WHERE Date >= @StartDate", 
            new { StartDate = startDate });
    }
    
    public async Task<int> CreateSaleAsync(Sale sale)
    {
        return await _connection.ExecuteAsync(
            "INSERT INTO Sales (Date, Amount, CustomerId, ProductId) VALUES (@Date, @Amount, @CustomerId, @ProductId)",
            sale);
    }
    
    public async Task<IEnumerable<SalesSummary>> GetSalesSummaryAsync(DateTime startDate, DateTime endDate)
    {
        return await _connection.QueryAsync<SalesSummary>(
            @"SELECT 
                ProductId, 
                SUM(Amount) AS TotalAmount, 
                COUNT(*) AS Count 
              FROM Sales 
              WHERE Date BETWEEN @StartDate AND @EndDate 
              GROUP BY ProductId",
            new { StartDate = startDate, EndDate = endDate });
    }
}
```

### 6.3 Service with Database Interface Injection

```csharp
using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

public interface ICustomerDatabaseService
{
    Task<Customer> GetCustomerByIdAsync(int id);
    Task<IEnumerable<Customer>> FindCustomersByNameAsync(string namePattern);
    Task<int> UpdateCustomerAsync(Customer customer);
}

[DapperLogger("CustomerDB")]
public class CustomerDatabaseService : ICustomerDatabaseService
{
    private readonly IDbConnection _connection;
    
    public CustomerDatabaseService(IDbConnection connection)
    {
        _connection = connection;
    }
    
    public async Task<Customer> GetCustomerByIdAsync(int id)
    {
        return await _connection.QueryFirstOrDefaultAsync<Customer>(
            "SELECT * FROM Customers WHERE Id = @Id",
            new { Id = id });
    }
    
    public async Task<IEnumerable<Customer>> FindCustomersByNameAsync(string namePattern)
    {
        return await _connection.QueryAsync<Customer>(
            "SELECT * FROM Customers WHERE Name LIKE @NamePattern",
            new { NamePattern = $"%{namePattern}%" });
    }
    
    public async Task<int> UpdateCustomerAsync(Customer customer)
    {
        return await _connection.ExecuteAsync(
            @"UPDATE Customers 
              SET Name = @Name, Email = @Email, Address = @Address, Phone = @Phone
              WHERE Id = @Id",
            customer);
    }
}
```

## 7. Log Output Examples

With the minimum logging threshold set to 200ms, the logging behavior will be:

1. **Fast Queries (< 200ms)**:
   - Not logged at all (unless an exception occurs)

2. **Normal Queries (200ms - 2000ms)**:
   ```
   [10:15:30 INF] Dapper executed in 450ms. [OracleDB] Method: ProductRepository.GetAllProductsAsync, SQL: SELECT * FROM Products, Parameters: {}
   ```

3. **Slow Queries (> 2000ms)**:
   ```
   [10:16:05 WRN] Slow Dapper execution detected! Executed in 3542ms (threshold: 2000ms). [SalesDbContext] Method: SalesDbContext.GetSalesSummaryAsync, SQL: SELECT ProductId, SUM(Amount) AS TotalAmount, COUNT(*) AS Count FROM Sales WHERE Date BETWEEN @StartDate AND @EndDate GROUP BY ProductId, Parameters: { "StartDate": "2023-01-01", "EndDate": "2023-12-31" }
   ```

4. **Error Cases (any duration)**:
   ```
   [10:17:12 ERR] Error executing Dapper in 123ms. [CustomerDB] Method: CustomerDatabaseService.GetCustomerByIdAsync, SQL: SELECT * FROM Customers WHERE Id = @Id, Parameters: { "Id": 123 }
   System.Data.SqlClient.SqlException (0x80131904): Invalid column name 'InvalidColumn'.
   ```

## 8. Summary and Key Features

This implementation provides a comprehensive solution for logging Dapper database operations with the following key features:

1. **Performance-Based Logging**:
   - Queries faster than 200ms are not logged (configurable)
   - Slow queries (> 2000ms) are logged as warnings
   - All errors are logged regardless of execution time

2. **Database Identification**:
   - Explicit identification via attribute parameter: `[DapperLogger("DatabaseName")]`
   - Automatic identification for DbContext classes using class name

3. **Privacy and Security**:
   - Sensitive parameters are automatically redacted
   - SQL statements are truncated to avoid excessive log size

4. **Configurability**:
   - All thresholds and settings are configurable via appsettings.json
   - Customizable list of sensitive parameter keywords

5. **Integration**:
   - Seamlessly integrates with AspectCore for AOP
   - Works with both EF Core DbContext and standard Dapper usage

This implementation strikes a balance between providing enough information for monitoring and troubleshooting while avoiding excessive logging that could impact performance or storage.
