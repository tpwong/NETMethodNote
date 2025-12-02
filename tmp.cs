下面示範在 C# + Dapper 下，如何對 PostgreSQL 的 `40001`（serialization_failure）做**自動重試 + 回滾**。你可以直接套到專案裡。

假設你用的是 `Npgsql` + Dapper。

---

## 1. 建一個通用的「帶重試的交易執行」方法

```csharp
using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Npgsql;

public static class PgSerializableHelper
{
    /// <summary>
    /// 在 SERIALIZABLE 隔離級別下執行一段交易邏輯，遇到 40001 會自動重試。
    /// </summary>
    public static async Task ExecuteSerializableAsync(
        string connectionString,
        Func<IDbConnection, IDbTransaction, Task> work,
        int maxRetries = 3,
        int baseDelayMs = 50)
    {
        if (maxRetries < 1) maxRetries = 1;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            // 明確設定隔離級別為 SERIALIZABLE
            await using var tx = await conn.BeginTransactionAsync(
                IsolationLevel.Serializable
            );

            try
            {
                await work(conn, tx);

                await tx.CommitAsync();
                return; // 成功就結束
            }
            catch (PostgresException ex) when (ex.SqlState == "40001")
            {
                // 序列化衝突：回滾並準備重試
                try { await tx.RollbackAsync(); } catch { /* ignore */ }

                if (attempt == maxRetries)
                    throw; // 已達最大重試次數，往外拋

                // 簡單的退避(backoff)避免又同時撞在一起
                var delay = baseDelayMs * attempt;
                await Task.Delay(delay);
            }
            catch
            {
                // 其他錯誤：回滾後直接拋出
                try { await tx.RollbackAsync(); } catch { /* ignore */ }
                throw;
            }
        }
    }

    /// <summary>
    /// 有回傳值的版本。
    /// </summary>
    public static async Task<TResult> ExecuteSerializableAsync<TResult>(
        string connectionString,
        Func<IDbConnection, IDbTransaction, Task<TResult>> work,
        int maxRetries = 3,
        int baseDelayMs = 50)
    {
        if (maxRetries < 1) maxRetries = 1;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var tx = await conn.BeginTransactionAsync(
                IsolationLevel.Serializable
            );

            try
            {
                var result = await work(conn, tx);
                await tx.CommitAsync();
                return result;
            }
            catch (PostgresException ex) when (ex.SqlState == "40001")
            {
                try { await tx.RollbackAsync(); } catch { /* ignore */ }

                if (attempt == maxRetries)
                    throw;

                var delay = baseDelayMs * attempt;
                await Task.Delay(delay);
            }
            catch
            {
                try { await tx.RollbackAsync(); } catch { /* ignore */ }
                throw;
            }
        }

        throw new InvalidOperationException("Unreachable");
    }
}
```

---

## 2. 在你的業務程式碼中使用

### 無回傳值例子

```csharp
public async Task AdjustBucketBalanceAsync(string connStr, int bucketId, decimal delta)
{
    await PgSerializableHelper.ExecuteSerializableAsync(
        connStr,
        async (conn, tx) =>
        {
            // 用 Dapper 查詢 / 更新
            var balance = await conn.QuerySingleAsync<decimal>(
                "SELECT balance FROM bucket_balances WHERE bucket_id = @id FOR UPDATE",
                new { id = bucketId },
                transaction: tx);

            var newBalance = balance + delta;
            if (newBalance < 0)
                throw new InvalidOperationException("餘額不足");

            await conn.ExecuteAsync(
                "UPDATE bucket_balances SET balance = @bal WHERE bucket_id = @id",
                new { bal = newBalance, id = bucketId },
                transaction: tx);
        },
        maxRetries: 3 // 可調
    );
}
```

### 有回傳值例子

```csharp
public async Task<decimal> AdjustAndReturnBalanceAsync(string connStr, int bucketId, decimal delta)
{
    return await PgSerializableHelper.ExecuteSerializableAsync(
        connStr,
        async (conn, tx) =>
        {
            var balance = await conn.QuerySingleAsync<decimal>(
                "SELECT balance FROM bucket_balances WHERE bucket_id = @id FOR UPDATE",
                new { id = bucketId },
                transaction: tx);

            var newBalance = balance + delta;
            if (newBalance < 0)
                throw new InvalidOperationException("餘額不足");

            await conn.ExecuteAsync(
                "UPDATE bucket_balances SET balance = @bal WHERE bucket_id = @id",
                new { bal = newBalance, id = bucketId },
                transaction: tx);

            return newBalance;
        }
    );
}
```

---

## 3. 要點整理

1. **隔離級別**：`BeginTransaction(IsolationLevel.Serializable)` 必須指定；否則只是預設 `Read Committed`。
2. **只捕捉 40001**：`PostgresException.SqlState == "40001"`；其他錯誤要直接拋出。
3. **一定要 rollback**：一旦例外發生，先 `RollbackAsync()`，再決定是否重試或向上拋。
4. **退避策略**：簡單 `Task.Delay(baseDelayMs * attempt)` 就能大幅降低同時又撞在一起的機率。

如果你貼出目前 Dapper 交易的實作，我可以幫你直接改成帶 40001 重試版。