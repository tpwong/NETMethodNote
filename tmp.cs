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
            WITH input_rows AS (
                SELECT 
                    a.acct, 
                    a.bucket_name, 
                    a.bucket_type, 
                    a.expiry_date, 
                    a.amount
                FROM unnest(
                    @Accts,
                    @BucketNames,
                    @BucketTypes,
                    @ExpiryDates,
                    @Amounts
                ) AS a(acct, bucket_name, bucket_type, expiry_date, amount)
            )
            INSERT INTO bucket_balances as b (acct, bucket_name, bucket_type, expiry_date, total)
            SELECT 
                i.acct, 
                i.bucket_name, 
                i.bucket_type, 
                i.expiry_date, 
                i.amount
            FROM input_rows i
            ON CONFLICT ON CONSTRAINT u_bucket_balances
            DO UPDATE SET
                total = b.total + excluded.total,
                last_modified_date = CURRENT_TIMESTAMP
            RETURNING 
                b.acct, 
                b.bucket_type, 
                b.expiry_date,
                b.total - (SELECT amount FROM input_rows WHERE 
                          input_rows.acct = b.acct AND 
                          input_rows.bucket_name = b.bucket_name AND 
                          input_rows.bucket_type = b.bucket_type AND 
                          input_rows.expiry_date = b.expiry_date) as before_total, 
                (SELECT amount FROM input_rows WHERE 
                          input_rows.acct = b.acct AND 
                          input_rows.bucket_name = b.bucket_name AND 
                          input_rows.bucket_type = b.bucket_type AND 
                          input_rows.expiry_date = b.expiry_date) as sum_with,
                b.total";

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




// 收集数据
var accts = adjustTransactionExpiries.Select(b => b.Acct).ToArray();
var gamingDts = adjustTransactionExpiries.Select(b => b.GamingDt).ToArray();
var bucketNames = adjustTransactionExpiries.Select(b => b.BucketName).ToArray();
var bucketTypes = adjustTransactionExpiries.Select(b => b.BucketType).ToArray();
var amounts = adjustTransactionExpiries.Select(b => b.Amount).ToArray();
var totals = audits.Select(a => a?.Total ?? 0M).ToArray(); // 假设audits和adjustTransactionExpiries有相同的顺序
var relatedIds = adjustTransactionExpiries.Select(b => b.RelatedId).ToArray();
var remarks = adjustTransactionExpiries.Select(b => b.Remark).ToArray();

// 定义批量插入SQL
const string insertStmt = @"
-- insert adjust bucket transaction
INSERT INTO bucket_adjust_transactions (acct, gaming_dt, bucket_name, bucket_type, amount, after_adjust_amount, related_id, remark)
SELECT 
    a.acct, 
    a.gaming_dt, 
    a.bucket_name, 
    a.bucket_type, 
    a.amount, 
    a.after_adjust_amount, 
    a.related_id, 
    a.remark
FROM unnest(
    @Accts,
    @GamingDts,
    @BucketNames,
    @BucketTypes,
    @Amounts,
    @Totals,
    @RelatedIds,
    @Remarks
) AS a(acct, gaming_dt, bucket_name, bucket_type, amount, after_adjust_amount, related_id, remark)
RETURNING id, acct, gaming_dt, bucket_name, bucket_type, amount, after_adjust_amount, post_dtm, related_id, remark, is_void";

// 执行批量插入
var adjustTransactions = await dbConnection.QueryAsync<AdjustTransaction>(
    insertStmt, 
    new {
        Accts = accts,
        GamingDts = gamingDts,
        BucketNames = bucketNames,
        BucketTypes = bucketTypes,
        Amounts = amounts,
        Totals = totals, // 使用从audits获取的Total作为after_adjust_amount
        RelatedIds = relatedIds,
        Remarks = remarks
    }, 
    transaction);

// 如果只需要第一个结果
var adjustTransaction = adjustTransactions.FirstOrDefault();


