# Revised Version: DapperLogger with Database Identification Based on Class Names (with Configuration Options)

Below is a version with added configuration options that allow setting execution time thresholds and other options:

## 1. DapperLoggerOptions Configuration Class

```csharp
using System;

public class DapperLoggerOptions
{
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

## 2. Modified DapperLogger Attribute (With Configuration Support)

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
        // Query methods
        "Query", "QueryFirst", "QueryFirstOrDefault", "QuerySingle", "QuerySingleOrDefault", "QueryMultiple",
        "QueryAsync", "QueryFirstAsync", "QueryFirstOrDefaultAsync", "QuerySingleAsync", "QuerySingleOrDefaultAsync", "QueryMultipleAsync",
        "Get", "GetAll", "GetAsync", "GetAllAsync", "Find", "FindAsync",
        
        // Execute methods
        "Execute", "ExecuteScalar", "ExecuteReader",
        "ExecuteAsync", "ExecuteScalarAsync", "ExecuteReaderAsync",
        "Insert", "Update", "Delete", "InsertAsync", "UpdateAsync", "DeleteAsync"
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

## 3. Updated AspectCore Extension Methods to Handle Configuration

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

## 4. Configuration Example (appsettings.json)

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

## 5. Configure DapperLogger in Startup and Read Settings from Configuration File

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

## 6. Usage Examples

### 6.1 Using in Repositories

```csharp
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
        // This operation will be logged as a warning if execution time exceeds 2 seconds
        return await _connection.QueryAsync<Product>("SELECT * FROM Products");
    }
    
    public async Task<Product> GetProductByIdAsync(int id)
    {
        return await _connection.QueryFirstOrDefaultAsync<Product>(
            "SELECT * FROM Products WHERE Id = @Id", 
            new { Id = id });
    }
}
```

### 6.2 Using in DbContext

```csharp
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
        // This operation will be logged as a warning if execution time exceeds 2 seconds
        return await _connection.QueryAsync<Sale>(
            "SELECT * FROM Sales WHERE Date >= @StartDate", 
            new { StartDate = startDate });
    }
}
```

## 7. Log Output Examples

1. **Normal execution log**:
   ```
   [10:15:30 INF] Dapper executed in 45ms. [OracleDB] Method: ProductRepository.GetAllProductsAsync, SQL: SELECT * FROM Products, Parameters: {}
   ```

2. **Slow execution warning log**:
   ```
   [10:16:05 WRN] Slow Dapper execution detected! Executed in 3542ms (threshold: 2000ms). [SalesDbContext] Method: SalesDbContext.GetSalesAsync, SQL: SELECT * FROM Sales WHERE Date >= @StartDate, Parameters: { "StartDate": "2023-01-01" }
   ```

3. **Execution error log**:
   ```
   [10:17:12 ERR] Error executing Dapper in 123ms. [MainDatabase] Method: CustomerRepository.GetCustomerByIdAsync, SQL: SELECT * FROM Customers WHERE Id = @Id, Parameters: { "Id": 123 }
   System.Data.SqlClient.SqlException (0x80131904): Invalid column name 'InvalidColumn'.
   ```

## 8. Summary

This implementation provides the following features:

1. **Explicit database identification**: Specify database identifier through `[DapperLogger("DatabaseName")]`
2. **Automatic identification**: Automatically use class name as identifier for DbContext classes
3. **Configurable execution time threshold**: Operations exceeding specified threshold (default 2 seconds) will be logged as warnings
4. **Configurable SQL length limit**: Ensures SQL statements don't exceed specified length
5. **Configurable sensitive data protection**: Customize keywords for sensitive parameters to be hidden
6. **Load settings from configuration file**: Support loading configurations from appsettings.json

This design is both flexible and practical, suitable for multi-database environments, and can help developers identify and resolve database performance issues.
