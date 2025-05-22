using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using NpgsqlTypes;

public class BucketBalance
{
    public string Acct { get; set; }
    public string BucketName { get; set; }
    public string BucketType { get; set; }
    public DateTime ExpiryDate { get; set; }
    public decimal Amount { get; set; }
}

public class BalanceResult
{
    public string Acct { get; set; }
    public string BucketType { get; set; }
    public DateTime ExpiryDate { get; set; }
    public decimal BeforeTotal { get; set; }
    public decimal SumWith { get; set; }
    public decimal Total { get; set; }
}

public class BucketBalanceRepository
{
    private readonly string _connectionString;

    public BucketBalanceRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IEnumerable<BalanceResult>> BulkUpsertBalancesAsync(List<BucketBalance> balances)
    {
        // 准备数组参数
        var accts = balances.Select(b => b.Acct).ToArray();
        var bucketNames = balances.Select(b => b.BucketName).ToArray();
        var bucketTypes = balances.Select(b => b.BucketType).ToArray();
        var expiryDates = balances.Select(b => b.ExpiryDate).ToArray();
        var amounts = balances.Select(b => b.Amount).ToArray();

        const string sql = @"
            INSERT INTO bucket_balances as b (acct, bucket_name, bucket_type, expiry_date, total)
            SELECT 
                a.acct, 
                a.bucket_name, 
                a.bucket_type, 
                a.expiry_date, 
                a.amount
            FROM unnest(
                @Accts,      -- acct 数组
                @BucketNames,      -- bucket_name 数组
                @BucketTypes,      -- bucket_type 数组
                @ExpiryDates, -- expiry_date 数组
                @Amounts    -- amount 数组
            ) AS a(acct, bucket_name, bucket_type, expiry_date, amount)
            ON CONFLICT ON CONSTRAINT u_bucket_balances
            DO UPDATE SET
                total = b.total + excluded.total,
                last_modified_date = CURRENT_TIMESTAMP
            RETURNING 
                acct, 
                bucket_type, 
                expiry_date,
                total - excluded.total as before_total, 
                excluded.total as sum_with,
                total";

        using (var connection = new NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            
            // 使用Dapper执行查询
            var parameters = new
            {
                Accts = accts,
                BucketNames = bucketNames,
                BucketTypes = bucketTypes,
                ExpiryDates = expiryDates,
                Amounts = amounts
            };

            var results = await connection.QueryAsync<BalanceResult>(sql, parameters);
            return results;
        }
    }
}