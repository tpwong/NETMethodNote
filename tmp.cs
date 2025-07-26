*Thinking...*

> **Comprehending the Goal**
> 
> I'm focusing on grasping the core requirement: generating the full, clean C# code for the DapperLogger implementation. The user has specified .NET 8 as the target framework and insists on removing all prior comments. I'm starting to outline the key components to ensure a comprehensive and accurate response.
> 
> 
> **Structuring the Code**
> 
> I'm now organizing the code components to build the final `DapperLogger` solution. I'm reviewing `DapperLoggerOptions.cs` to incorporate `EnableHeuristicMethodDetection`. The core logic is in `DapperLoggerAttribute.cs`, where I'm integrating the `MethodNameTrie` and property caching. I'm ensuring proper documentation and eliminating all previously included comments.
> 
> 
> **Constructing the Components**
> 
> I'm now in the process of piecing together the code components for the final solution. I'm double-checking the static constructor in `DapperLoggerAttribute.cs` to make sure that the `dapperMethodPrefixes` are all properly capitalized to match the Trie case-insensitivity requirement, as well as constructing the trie. Also, I'm confirming that all references to comments like `// **OPTIMIZED**` have been removed.
> 
> 
> **Detailing Attribute Implementation**
> 
> I'm now focused on the intricacies of `DapperLoggerAttribute.cs`, specifically the static constructor and the `ShouldLogMethod` logic. I'm rigorously ensuring that the static constructor correctly capitalizes the dapper method prefixes for case-insensitive matching. Then, I'm thoroughly reviewing the implementation for the trie's prefix matching. Also, I'm adding proper XML documentation.
> 
> 
> **Implementing the Attribute**
> 
> I'm now concentrating on the `DapperLoggerAttribute` and `MethodNameTrie` to ensure both logic and documentation are perfect. I'm reviewing all methods, including `ShouldLogMethod`, to ensure proper functionality, and I'm updating the static constructor, meticulously implementing the case-insensitive prefix handling using the Trie, and removing all comments from prior versions.
> 
> 
> **Consolidating Implementation Steps**
> 
> I'm now compiling the comprehensive solution, addressing the core request by delivering clean, final C# code. I'm meticulously reviewing the individual components, from `DapperLoggerOptions.cs` to the sample repositories, ensuring all comments are expunged and documentation is properly presented. The focus now is on the final output, ready for deployment in a .NET 8 environment.
> 
> 
好的，完全理解。

這就為您提供一份適用於 .NET 8 的最終、乾淨且不含任何註解標籤的完整程式碼。此版本整合了所有討論過的優化，包括使用 `Trie` 結構來簡化和加速方法偵測、反射快取等，並移除了所有臨時性的註解。

---

### 1. DapperLoggerOptions.cs

```csharp
using System;

/// <summary>
/// Provides configuration options for the DapperLogger.
/// </summary>
public class DapperLoggerOptions
{
    /// <summary>
    /// Gets or sets the minimum execution time in milliseconds to log a Dapper command.
    /// Commands faster than this threshold will not be logged, unless an error occurs.
    /// Default is 200ms.
    /// </summary>
    public int MinimumLoggingThresholdMs { get; set; } = 200;
    
    /// <summary>
    /// Gets or sets the execution time threshold in milliseconds, above which a Dapper command is logged as a warning.
    /// Default is 2000ms (2 seconds).
    /// </summary>
    public int SlowExecutionThresholdMs { get; set; } = 2000;
    
    /// <summary>
    /// Gets or sets the maximum length of the SQL statement to be logged. Longer statements will be truncated.
    /// Default is 2048 characters.
    /// </summary>
    public int MaxSqlLength { get; set; } = 2048;
    
    /// <summary>
    /// Gets or sets a value indicating whether parameter sanitization is enabled to hide sensitive data.
    /// Default is true.
    /// </summary>
    public bool EnableParameterSanitization { get; set; } = true;
    
    /// <summary>
    /// Gets or sets the keywords used to identify sensitive parameters that should be redacted from logs.
    /// The matching is case-insensitive.
    /// </summary>
    public string[] SensitiveParameterKeywords { get; set; } = new string[] 
    {
        "password", "secret", "key", "token", "credential", "auth"
    };

    /// <summary>
    /// Gets or sets a value indicating whether to enable heuristic method detection for classes with specific suffixes
    /// like "Repository", "DataService", or "DbContext".
    /// When enabled, it attempts to log methods that match known data access prefixes (e.g., "Get", "Update").
    /// Default is true.
    /// </summary>
    public bool EnableHeuristicMethodDetection { get; set; } = true;
}
```

### 2. MethodNameTrie.cs (內部輔助類別)

```csharp
using System.Collections.Generic;

/// <summary>
/// An internal helper class for high-performance method name matching using a Trie (prefix tree).
/// This is significantly faster than using string operations on a collection for prefix matching.
/// </summary>
internal class MethodNameTrie
{
    private class TrieNode
    {
        public readonly Dictionary<char, TrieNode> Children = new();
        public bool IsEndOfWord { get; set; }
    }

    private readonly TrieNode _root = new();

    /// <summary>
    /// Inserts a word into the Trie. The word is converted to uppercase for case-insensitive matching.
    /// </summary>
    public void Insert(string word)
    {
        var node = _root;
        foreach (char c in word.ToUpperInvariant())
        {
            if (!node.Children.TryGetValue(c, out var nextNode))
            {
                nextNode = new TrieNode();
                node.Children[c] = nextNode;
            }
            node = nextNode;
        }
        node.IsEndOfWord = true;
    }

    /// <summary>
    /// Checks if the given text starts with any of the prefixes stored in the Trie.
    /// The check is case-insensitive.
    /// </summary>
    public bool StartsWithPrefix(string text)
    {
        var node = _root;
        foreach (char c in text.ToUpperInvariant())
        {
            if (node.IsEndOfWord)
            {
                return true;
            }
            
            if (!node.Children.TryGetValue(c, out node))
            {
                return false;
            }
        }
        return node.IsEndOfWord;
    }
}
```

### 3. DapperLoggerAttribute.cs (核心實作)

```csharp
using AspectCore.DynamicProxy;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// An interceptor attribute that provides automatic logging for Dapper database operations using AOP.
/// It captures SQL, parameters, execution time, and errors for methods in decorated classes.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface)]
public class DapperLoggerAttribute : AbstractInterceptorAttribute
{
    private static readonly MethodNameTrie _dapperMethodsTrie = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _propertyCache = new();

    static DapperLoggerAttribute()
    {
        var dapperMethodPrefixes = new List<string>
        {
            "Query", "Execute", "Get", "Insert", "Update", "Delete", "Find", "Add", 
            "Create", "Save", "Modify", "Remove", "BulkInsert", "BulkUpdate", "BulkDelete", 
            "BulkMerge", "ExecuteProcedure", "ExecuteStoredProcedure", "ExecProc", 
            "ExecuteInTransaction", "Count", "Exists", "Any", "Fetch", "Load", "Select"
        };
        
        foreach (var prefix in dapperMethodPrefixes)
        {
            _dapperMethodsTrie.Insert(prefix);
        }
    }

    private static readonly ThreadLocal<Stopwatch> _stopwatch = new(() => new Stopwatch());
    private static DapperLoggerOptions _options = new();
    private readonly string _databaseIdentifier;

    public DapperLoggerAttribute() : this(null) { }
    
    public DapperLoggerAttribute(string databaseIdentifier)
    {
        _databaseIdentifier = databaseIdentifier;
    }
    
    internal static void SetOptions(DapperLoggerOptions options)
    {
        _options = options ?? new DapperLoggerOptions();
    }

    public async override Task Invoke(AspectContext context, AspectDelegate next)
    {
        var method = context.ImplementationMethod;
        if (!ShouldLogMethod(method.Name, method.DeclaringType))
        {
            await next(context);
            return;
        }

        var stopwatch = _stopwatch.Value;
        stopwatch.Restart();
        
        Exception exception = null;
        try
        {
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
            
            if (elapsedMs < _options.MinimumLoggingThresholdMs && exception == null)
            {
                return;
            }
            
            LogDapperExecution(context, elapsedMs, exception);
        }
    }

    private bool ShouldLogMethod(string methodName, Type declaringType)
    {
        if (_dapperMethodsTrie.StartsWithPrefix(methodName))
        {
            return true;
        }

        if (_options.EnableHeuristicMethodDetection)
        {
            var typeName = declaringType?.Name ?? "";
            if (typeName.EndsWith("Repository", StringComparison.OrdinalIgnoreCase) ||
                typeName.EndsWith("DataAccess", StringComparison.OrdinalIgnoreCase) ||
                typeName.EndsWith("DataService", StringComparison.OrdinalIgnoreCase) ||
                typeName.EndsWith("DbService", StringComparison.OrdinalIgnoreCase) ||
                typeName.EndsWith("DbContext", StringComparison.OrdinalIgnoreCase))
            {
                return _dapperMethodsTrie.StartsWithPrefix(methodName);
            }
        }
        
        return false;
    }

    private void LogDapperExecution(AspectContext context, long elapsedMs, Exception exception)
    {
        try
        {
            var method = context.ImplementationMethod;
            var typeName = method.DeclaringType?.Name ?? "UnknownType";

            string dbIdentifier = _databaseIdentifier;
            if (string.IsNullOrEmpty(dbIdentifier) && typeName.EndsWith("DbContext", StringComparison.OrdinalIgnoreCase))
            {
                dbIdentifier = typeName;
            }

            string sql = ExtractAndTruncateSql(context.Parameters);
            object parameters = ExtractAndSanitizeParameters(context.Parameters);
            
            string dbInfo = !string.IsNullOrEmpty(dbIdentifier) ? $"[{dbIdentifier}] " : string.Empty;
            
            LogEventLevel logLevel = exception != null ? LogEventLevel.Error : 
                                     (elapsedMs > _options.SlowExecutionThresholdMs ? LogEventLevel.Warning : 
                                     LogEventLevel.Information);
            
            if (exception != null)
            {
                Log.Error(exception, "Error executing Dapper in {ElapsedMilliseconds}ms. {DbInfo}Method: {Method}, SQL: {Sql}, Parameters: {@Parameters}",
                    elapsedMs, dbInfo, $"{typeName}.{method.Name}", sql, parameters);
            }
            else if (logLevel == LogEventLevel.Warning)
            {
                Log.Warning("Slow Dapper execution detected! Executed in {ElapsedMilliseconds}ms (threshold: {ThresholdMs}ms). {DbInfo}Method: {Method}, SQL: {Sql}, Parameters: {@Parameters}",
                    elapsedMs, _options.SlowExecutionThresholdMs, dbInfo, $"{typeName}.{method.Name}", sql, parameters);
            }
            else
            {
                Log.Information("Dapper executed in {ElapsedMilliseconds}ms. {DbInfo}Method: {Method}, SQL: {Sql}, Parameters: {@Parameters}",
                    elapsedMs, dbInfo, $"{typeName}.{method.Name}", sql, parameters);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while logging Dapper execution");
        }
    }

    private string ExtractAndTruncateSql(object[] parameters)
    {
        var sql = parameters.OfType<string>().FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && IsSqlStatement(p));
        
        if (sql == null) return "SQL Not Found";

        return sql.Length > _options.MaxSqlLength
            ? sql.Substring(0, _options.MaxSqlLength) + "... [TRUNCATED]"
            : sql;
    }
    
    private bool IsSqlStatement(string text)
    {
        string upperText = text.TrimStart().ToUpperInvariant();
        var keywords = new[] { "SELECT", "INSERT", "UPDATE", "DELETE", "WITH", "MERGE", "CREATE", "ALTER", "DROP", "EXEC", "EXECUTE", "CALL", "DECLARE", "BEGIN", "SET" };
        return keywords.Any(k => upperText.StartsWith(k));
    }

    private object ExtractAndSanitizeParameters(object[] parameters)
    {
        var paramObject = parameters.FirstOrDefault(p =>
            p != null &&
            !(p is string) &&
            !(p is IDbConnection) &&
            !(p is IDbTransaction) &&
            !p.GetType().IsPrimitive &&
            !(p is CommandType?) &&
            !(p is CancellationToken)
        );

        return _options.EnableParameterSanitization ? SanitizeObject(paramObject) : paramObject;
    }
    
    private object SanitizeObject(object obj)
    {
        if (obj == null) return null;

        var type = obj.GetType();
        if (type.IsPrimitive || obj is string || obj is decimal || obj is Guid || obj is DateTime || obj is DateTimeOffset || obj is TimeSpan)
        {
            return obj;
        }
        
        if (obj is IDictionary<string, object> dictionary)
        {
            return dictionary.ToDictionary(
                kvp => kvp.Key,
                kvp => _options.SensitiveParameterKeywords.Any(k => kvp.Key.Contains(k, StringComparison.OrdinalIgnoreCase))
                    ? "***REDACTED***"
                    : kvp.Value
            );
        }
        
        var properties = _propertyCache.GetOrAdd(type, t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance));
        var sanitizedDict = new Dictionary<string, object>(properties.Length);
        
        foreach (var prop in properties)
        {
            var propName = prop.Name;
            var value = prop.GetValue(obj);
            
            bool isSensitive = _options.SensitiveParameterKeywords.Any(keyword => 
                propName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                
            sanitizedDict[propName] = isSensitive ? "***REDACTED***" : value;
        }
        
        return sanitizedDict;
    }
}
```

### 4. AspectCoreExtensions.cs (DI 註冊)

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
        return AddDapperWithLogging(services, _ => {});
    }
    
    public static IServiceCollection AddDapperWithLogging(this IServiceCollection services, 
        Action<DapperLoggerOptions> configureOptions)
    {
        services.Configure(configureOptions);
        services.AddSingleton<IStartupFilter, DapperLoggerStartupFilter>();
        
        services.ConfigureDynamicProxy(config =>
        {
            config.Interceptors.AddTyped<DapperLoggerAttribute>(method => 
                ShouldInterceptMethod(method));
            
            config.NonAspectPredicates.AddNamespace("System.*");
            config.NonAspectPredicates.AddNamespace("Microsoft.*");
            config.NonAspectPredicates.AddNamespace("AspectCore.*");
        });
        
        return services;
    }
    
    private static bool ShouldInterceptMethod(MethodInfo method)
    {
        if (method?.DeclaringType == null)
            return false;
        
        var declaringType = method.DeclaringType;
        var typeName = declaringType.Name;
        
        var hasAttribute = declaringType.IsDefined(typeof(DapperLoggerAttribute), true) ||
                           declaringType.GetInterfaces().Any(i => i.IsDefined(typeof(DapperLoggerAttribute), true));
        
        var isDbContext = typeof(DbContext).IsAssignableFrom(declaringType);
        
        var isRepositoryOrDataAccess = typeName.EndsWith("Repository", StringComparison.OrdinalIgnoreCase) ||
                                       typeName.EndsWith("DataAccess", StringComparison.OrdinalIgnoreCase) ||
                                       typeName.EndsWith("DataService", StringComparison.OrdinalIgnoreCase) ||
                                       typeName.EndsWith("DbService", StringComparison.OrdinalIgnoreCase);
                                     
        return hasAttribute || isDbContext || isRepositoryOrDataAccess;
    }
    
    private class DapperLoggerStartupFilter : IStartupFilter
    {
        private readonly IOptions<DapperLoggerOptions> _options;
        
        public DapperLoggerStartupFilter(IOptions<DapperLoggerOptions> options)
        {
            _options = options;
        }
        
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            DapperLoggerAttribute.SetOptions(_options.Value);
            return next;
        }
    }
}

// These interfaces are typically part of ASP.NET Core but are included here for completeness
// if you were to use this outside of a standard web project.
public interface IStartupFilter
{
    Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next);
}

public interface IApplicationBuilder
{
    IServiceProvider ApplicationServices { get; }
}
```

### 5. appsettings.json (組態檔)

```json
{
  "DapperLogger": {
    "MinimumLoggingThresholdMs": 100,
    "SlowExecutionThresholdMs": 1500,
    "MaxSqlLength": 4096,
    "EnableParameterSanitization": true,
    "SensitiveParameterKeywords": [
      "password",
      "secret",
      "key",
      "token",
      "credential",
      "auth"
    ],
    "EnableHeuristicMethodDetection": true
  }
}
```

### 6. Startup.cs (啟動組態) 與範例程式碼

您的 `Startup.cs`、`ProductRepository`、`ApplicationDbContext` 以及所有相關的 Model 類別**無需任何變更**，可以直接與上述優化後的 Logger 一起使用。只需確保在 `ConfigureServices` 方法中呼叫 `services.AddDapperWithLogging(...)` 即可。
