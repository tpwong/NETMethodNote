This is a very practical scenario. When adding a Timeout within a Handler, the key lies in **correctly merging** the "external Token (User Cancel)" with the "internal Timeout Token".

We use `CancellationTokenSource.CreateLinkedTokenSource` to achieve this.

Here is the complete implementation integrating **Keyed Services** + **SemaphoreSlim** + **30-second Timeout**:

### Complete Implementation (SharedConcurrencyHandler.cs)

```csharp
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

public class SharedConcurrencyHandler : DelegatingHandler
{
    private readonly SemaphoreSlim _semaphore;

    // Inject Keyed Semaphore
    public SharedConcurrencyHandler([FromKeyedServices("GlobalHttpLock")] SemaphoreSlim semaphore)
    {
        _semaphore = semaphore;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // 1. Set up a CTS with a 30-second timeout
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // 2. Merge the "external token" with the "timeout token"
        // This ensures cancellation triggers whether the user cancels OR the time runs out
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            // 3. Wait for the lock (using linkedToken)
            // If queuing takes longer than 30s, this throws OperationCanceledException
            await _semaphore.WaitAsync(linkedCts.Token);

            try
            {
                // 4. Execute the request (using linkedToken)
                // If the request takes too long and total time exceeds 30s, this throws
                return await base.SendAsync(request, linkedCts.Token);
            }
            finally
            {
                // 5. Always release the lock
                _semaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Determine if it was a Timeout or a User Cancel
            if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // This was caused by the 30s timeout
                throw new TimeoutException("Request timed out after 30 seconds (Wait + Execution).");
            }
            
            // Otherwise, it was cancelled by the user; rethrow strictly
            throw;
        }
    }
}
```

### Key Logic Explanation

1.  **`CreateLinkedTokenSource` (Crucial)**:
    *   You cannot rely solely on `timeoutCts.Token`. If the user cancels the operation at the 2nd second, the application must stop immediately rather than waiting for the full 30 seconds.
    *   The `LinkedToken` ensures that **(User Cancel OR Timeout)** triggers the cancellation immediately if either event occurs.

2.  **Scope**:
    *   This Timeout covers **"Time spent waiting for the lock" + "Time spent on the actual HTTP request"**.
    *   If the Semaphore is full and queuing alone takes 30 seconds, the code throws an exception and aborts before sending the request.

3.  **`using` Statement**:
    *   `CancellationTokenSource` implements `IDisposable`. Always use `using` to ensure resources are released correctly.

4.  **Exception Handling**:
    *   Standard `HttpClient` timeouts usually throw a `TaskCanceledException` (a subclass of `OperationCanceledException`).
    *   I added logic in the `catch` block to distinguish between a "Real Timeout" and a "User Cancellation," which is helpful for logging and error handling.

### Program.cs (Remains Unchanged)

The registration part remains simple and does not need modification:

```csharp
builder.Services.AddKeyedSingleton("GlobalHttpLock", (sp, key) => new SemaphoreSlim(3, 3));
builder.Services.AddTransient<SharedConcurrencyHandler>();
builder.Services.AddHttpClient("MyClient")
    .AddHttpMessageHandler<SharedConcurrencyHandler>();
```