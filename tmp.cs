看到了您的代码，我现在能更具体地理解您的问题了。在您的 `RetainServiceWorker` 类中，您使用了多个 `GetKeyedService` 调用，例如：

```csharp
if (_serviceProvider.GetKeyedService<string[]>("participants") is string[] participants)
{
    var isBlackOrWhiteList = _serviceProvider.GetKeyedService<string>("isBlackOrWhiteList");
    // ...
}
```

在单元测试中，您需要模拟这些带键的服务。基于您的代码，您应该这样设置mock：

```csharp
// 创建ServiceProvider的mock
var mockServiceProvider = new Mock<IServiceProvider>();

// 模拟"participants"键的服务
string[] mockParticipants = new[] { "participant1", "participant2" };
mockServiceProvider
    .Setup(sp => sp.GetKeyedService(typeof(string[]), "participants"))
    .Returns(mockParticipants);

// 模拟"isBlackOrWhiteList"键的服务
string mockListType = "white"; // 或 "black"，取决于您的测试需求
mockServiceProvider
    .Setup(sp => sp.GetKeyedService(typeof(string), "isBlackOrWhiteList"))
    .Returns(mockListType);
```

注意，尽管在代码中您使用了泛型方法 `GetKeyedService<T>`，但实际上在底层这会被转换为非泛型的 `GetKeyedService(Type, object)`。所以您需要模拟这个非泛型方法。

如果您使用的是.NET 8或更高版本，可能需要实现 `IKeyedServiceProvider` 接口而不是简单的 `IServiceProvider`：

```csharp
var mockServiceProvider = new Mock<IKeyedServiceProvider>();

mockServiceProvider
    .Setup(sp => sp.GetKeyedService(typeof(string[]), "participants"))
    .Returns(mockParticipants);

mockServiceProvider
    .Setup(sp => sp.GetKeyedService(typeof(string), "isBlackOrWhiteList"))
    .Returns(mockListType);
```

确保您的RetainServiceWorker类在构造函数中接收这个模拟的服务提供者，例如：

```csharp
// 在测试中实例化您的服务
var worker = new RetainServiceWorker(
    mockServiceProvider.Object, 
    mockLogger.Object, 
    mockQueryDatawarehDbContext.Object,
    mockApiClient.Object);

// 然后测试Invoke方法
await worker.Invoke();
```

这样应该能解决您的单元测试中的错误问题。