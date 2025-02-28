using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

// 任务优先级枚举
public enum TaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

// 任务项接口
public interface ITaskItem
{
    TaskPriority Priority { get; }
    string Name { get; }
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}

// 任务项实现
public class TaskItem : ITaskItem
{
    public TaskPriority Priority { get; }
    public string Name { get; }
    private readonly Func<CancellationToken, Task> _action;

    public TaskItem(string name, TaskPriority priority, Func<CancellationToken, Task> action)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Priority = priority;
        _action = action ?? throw new ArgumentNullException(nameof(action));
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await _action(cancellationToken);
    }
}

// 优先级任务调度器接口
public interface IPriorityTaskScheduler : IDisposable, IAsyncDisposable
{
    ValueTask EnqueueTaskAsync(ITaskItem task, CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}

// 结合PriorityQueue和Channel的任务调度器
public class PriorityChannelTaskScheduler : IPriorityTaskScheduler
{
    // 任务输入Channel
    private readonly Channel<ITaskItem> _inputChannel;
    
    // 用于处理任务的优先级队列 - 使用.NET 6的PriorityQueue
    private readonly PriorityQueue<ITaskItem, int> _priorityQueue;
    
    // 用于保护优先级队列的锁
    private readonly SemaphoreSlim _queueLock = new SemaphoreSlim(1, 1);
    
    // 最大并发任务数限制
    private readonly SemaphoreSlim _concurrencyLimiter;
    
    // 用于通知有新任务添加的信号
    private readonly SemaphoreSlim _taskAvailableSignal = new SemaphoreSlim(0);
    
    private CancellationTokenSource _cts;
    private Task _processingTask;
    private readonly int _maxConcurrentTasks;
    private bool _isDisposed;
    private readonly int _boundedCapacity;

    public PriorityChannelTaskScheduler(int maxConcurrentTasks = 4, int boundedCapacity = 100)
    {
        if (maxConcurrentTasks < 1)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentTasks), "Must be at least 1");

        _maxConcurrentTasks = maxConcurrentTasks;
        _boundedCapacity = boundedCapacity;
        _concurrencyLimiter = new SemaphoreSlim(maxConcurrentTasks);
        
        // 创建输入Channel
        var options = new BoundedChannelOptions(boundedCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };
        _inputChannel = Channel.CreateBounded<ITaskItem>(options);
        
        // 初始化优先级队列
        // 使用负值作为优先级，这样高优先级(数值大)的任务将首先出队
        _priorityQueue = new PriorityQueue<ITaskItem, int>(_boundedCapacity);
    }

    // 异步将任务放入输入Channel
    public async ValueTask EnqueueTaskAsync(ITaskItem task, CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(PriorityChannelTaskScheduler));
            
        if (task == null)
            throw new ArgumentNullException(nameof(task));

        // 将任务写入输入Channel
        await _inputChannel.Writer.WriteAsync(task, cancellationToken);
    }

    // 启动后台处理任务
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(PriorityChannelTaskScheduler));
            
        if (_processingTask != null)
            return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        // 启动Channel读取任务
        Task channelReaderTask = Task.Run(() => ReadFromChannelAsync(_cts.Token));
        
        // 启动任务处理
        _processingTask = Task.Run(() => ProcessTasksAsync(_cts.Token));
        
        return Task.CompletedTask;
    }

    // 从输入Channel读取任务并放入优先级队列
    private async Task ReadFromChannelAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var task in _inputChannel.Reader.ReadAllAsync(cancellationToken))
            {
                await _queueLock.WaitAsync(cancellationToken);
                try
                {
                    // 根据优先级插入队列 (PriorityQueue以小值优先，所以使用负值转换)
                    _priorityQueue.Enqueue(task, -1 * (int)task.Priority);
                }
                finally
                {
                    _queueLock.Release();
                }
                
                // 发出有任务可用的信号
                _taskAvailableSignal.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 预期的取消
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading from channel: {ex.Message}");
            throw;
        }
    }

    // 从优先级队列中处理任务
    private async Task ProcessTasksAsync(CancellationToken cancellationToken)
    {
        // 追踪正在执行的任务
        var executingTasks = new List<Task>(_maxConcurrentTasks);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // 清理已完成的任务
                executingTasks.RemoveAll(t => t.IsCompleted);
                
                // 尝试从优先级队列获取任务
                ITaskItem task = null;
                
                // 获取队列锁
                await _queueLock.WaitAsync(cancellationToken);
                try
                {
                    if (_priorityQueue.Count > 0)
                    {
                        task = _priorityQueue.Dequeue();
                    }
                }
                finally
                {
                    _queueLock.Release();
                }
                
                if (task != null)
                {
                    // 等待可用的执行插槽
                    await _concurrencyLimiter.WaitAsync(cancellationToken);
                    
                    // 启动任务执行
                    var executionTask = ExecuteTaskWithReleaseAsync(task, cancellationToken);
                    executingTasks.Add(executionTask);
                }
                else
                {
                    // 如果没有任务，等待新任务信号或取消
                    try
                    {
                        await _taskAvailableSignal.WaitAsync(cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break; // 退出循环
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 预期的取消，退出循环
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected error in task processing: {ex}");
            throw;
        }
        finally
        {
            // 等待所有执行中的任务完成
            if (executingTasks.Count > 0)
            {
                try
                {
                    await Task.WhenAll(executingTasks);
                }
                catch
                {
                    // 忽略任务执行中的异常
                }
            }
        }
    }

    private async Task ExecuteTaskWithReleaseAsync(ITaskItem task, CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine($"执行任务: {task.Name} (优先级: {task.Priority})");
            await task.ExecuteAsync(cancellationToken);
            Console.WriteLine($"任务完成: {task.Name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"任务 {task.Name} 失败: {ex.Message}");
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    // 停止处理并等待所有任务完成
    public async Task StopAsync()
    {
        if (_processingTask == null || _cts == null)
            return;

        // 关闭输入Channel
        _inputChannel.Writer.Complete();
        
        // 取消处理任务
        _cts.Cancel();
        
        try
        {
            // 等待处理任务完成
            await _processingTask;
        }
        catch (OperationCanceledException)
        {
            // 预期中的取消异常
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _processingTask = null;
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_isDisposed)
        {
            await StopAsync();
            
            _queueLock.Dispose();
            _concurrencyLimiter.Dispose();
            _taskAvailableSignal.Dispose();
            _cts?.Dispose();
            
            _isDisposed = true;
        }
    }
}

// 使用示例
public class PriorityTaskSchedulerExample
{
    public static async Task Main()
    {
        await using var scheduler = new PriorityChannelTaskScheduler(maxConcurrentTasks: 2);
        
        // 启动调度器
        await scheduler.StartAsync();
        
        // 创建并添加各种优先级的任务
        Console.WriteLine("添加各种优先级的任务...");
        
        // 按照随机顺序添加任务来测试优先级是否生效
        await scheduler.EnqueueTaskAsync(new TaskItem("库存检查", TaskPriority.Normal, async (ct) => 
        {
            Console.WriteLine("开始检查库存...");
            await Task.Delay(1500, ct);
            Console.WriteLine("库存检查完成");
        }));
        
        await scheduler.EnqueueTaskAsync(new TaskItem("数据分析", TaskPriority.Low, async (ct) => 
        {
            Console.WriteLine("开始数据分析...");
            await Task.Delay(3000, ct);
            Console.WriteLine("数据分析完成");
        }));
        
        await scheduler.EnqueueTaskAsync(new TaskItem("付款处理", TaskPriority.Critical, async (ct) => 
        {
            Console.WriteLine("开始处理付款...");
            await Task.Delay(1000, ct);
            Console.WriteLine("付款处理完成");
        }));
        
        await scheduler.EnqueueTaskAsync(new TaskItem("订单确认", TaskPriority.High, async (ct) => 
        {
            Console.WriteLine("开始确认订单...");
            await Task.Delay(2000, ct);
            Console.WriteLine("订单确认完成");
        }));
        
        // 添加更多任务测试并发和优先级
        Console.WriteLine("添加更多任务...");
        for (int i = 0; i < 5; i++)
        {
            var priority = i % 4 switch
            {
                0 => TaskPriority.Critical,
                1 => TaskPriority.High,
                2 => TaskPriority.Normal,
                _ => TaskPriority.Low
            };
            
            await scheduler.EnqueueTaskAsync(new TaskItem(
                $"附加任务 {i+1}", 
                priority, 
                async (ct) => 
                {
                    Console.WriteLine($"开始执行附加任务 {i+1} (优先级: {priority})...");
                    await Task.Delay(1000, ct);
                    Console.WriteLine($"附加任务 {i+1} 完成");
                }
            ));
        }
        
        Console.WriteLine("等待任务完成...");
        await Task.Delay(15000);
        
        Console.WriteLine("停止调度器...");
        await scheduler.StopAsync();
        Console.WriteLine("调度器已停止");
    }
}
