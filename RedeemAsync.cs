所以我需要一個account_balances, earning transactions和redeem transactions的表, 如果需要earn分的話就對account_balances和earning transactions表一起做事務操作.

那如果我現在有一個event promotion, 在promotion period期間可以賺取優惠券, 當然優惠券是有過期時間的, 如果要redeem的時候就當然先扣優惠券的金額, 再扣帳號的金額, 那該如何設計呢



1. 数据库并发处理能力: PostgreSQL 支持高并发的事务处理，但其并发能力取决于以下因素：
- 最大连接数（max_connections）：
a. PostgreSQL 默认的最大连接数为 100。
b. 您需要将 max_connections 参数调整为至少 500，以支持 500 个并发连接。
c. 需要注意的是，过多的连接数会消耗更多的内存资源，每个连接大约消耗几 MB 的内存。
- 连接池的使用：
a. 建议在应用程序层使用连接池（如 Npgsql 的连接池）。
b. 通过连接池，应用程序可以复用数据库连接，减少连接建立和关闭的开销。
c. 限制每个应用实例的最大连接数，例如，每个实例最多 50 个连接。


CREATE TABLE IF NOT EXISTS account_balances (
	-- 主键已经在 account_id 上创建了唯一索引
    account_id BIGINT PRIMARY KEY,
    balance NUMERIC NOT NULL DEFAULT 0,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);


CREATE TABLE IF NOT EXISTS coupon_types (
	-- 主键已经在 coupon_type_id 上创建了唯一索引
    coupon_type_id SERIAL PRIMARY KEY,
    coupon_name VARCHAR(100) NOT NULL,
    value NUMERIC NOT NULL,
    validity_days INTEGER NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);


CREATE TABLE IF NOT EXISTS user_coupons (
    user_coupon_id BIGSERIAL PRIMARY KEY,
    account_id BIGINT NOT NULL,
    coupon_type_id INTEGER NOT NULL,
    acquired_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    expired_at TIMESTAMP WITH TIME ZONE NOT NULL,
    is_used BOOLEAN NOT NULL DEFAULT FALSE,
    CONSTRAINT fk_account
        FOREIGN KEY(account_id)
            REFERENCES account_balances(account_id),
    CONSTRAINT fk_coupon_type
        FOREIGN KEY(coupon_type_id)
            REFERENCES coupon_types(coupon_type_id)
);
-- 添加索引以提高查询性能
CREATE INDEX IF NOT EXISTS idx_user_coupons_account_id ON user_coupons (account_id);
-- 添加组合索引以优化查询可用优惠券
CREATE INDEX IF NOT EXISTS idx_user_coupons_account_isused_expired ON user_coupons (account_id, is_used, expired_at);


CREATE TABLE IF NOT EXISTS redemption_records (
	-- 主键已经在 redemption_id 上创建了唯一索引
    redemption_id BIGSERIAL PRIMARY KEY,
    account_id BIGINT NOT NULL,
    total_amount NUMERIC NOT NULL,
    coupon_amount NUMERIC NOT NULL,
    balance_amount NUMERIC NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT fk_account
        FOREIGN KEY(account_id)
            REFERENCES account_balances(account_id)
);


CREATE TABLE IF NOT EXISTS redemption_coupons (
    redemption_id BIGINT NOT NULL,
    user_coupon_id BIGINT NOT NULL,
    coupon_value NUMERIC NOT NULL, -- 本次兑换中该优惠券使用的金额
    PRIMARY KEY (redemption_id, user_coupon_id),
    CONSTRAINT fk_redemption
        FOREIGN KEY(redemption_id)
            REFERENCES redemption_records(redemption_id),
    CONSTRAINT fk_user_coupon
        FOREIGN KEY(user_coupon_id)
            REFERENCES user_coupons(user_coupon_id)
);



您可以通过在连接字符串中添加参数，对连接池的行为进行配置：
- Pooling：启用或禁用连接池。默认值为 true。
- MinPoolSize：连接池保持的最小连接数。默认值为 0。
- MaxPoolSize：连接池允许的最大连接数。默认值为 100。
// 设置连接池的最大和最小连接数
string connectionString = "Host=localhost;Port=5432;Username=your_username;Password=your_password;Database=your_database;MinPoolSize=10;MaxPoolSize=100";



using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Npgsql;

namespace RedemptionExample
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // 数据库连接字符串
            string connectionString = "Host=localhost;Port=5432;Username=your_username;Password=your_password;Database=your_database";

            // 示例数据
            long accountId = 1;
            decimal redeemAmount = 120; // 需要兑换的总金额

            try
            {
                bool success = await RedeemAsync(connectionString, accountId, redeemAmount);

                if (success)
                {
                    Console.WriteLine("兑换成功！");
                }
                else
                {
                    Console.WriteLine("兑换失败：余额或优惠券金额不足。");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"兑换过程中出现错误：{ex.Message}");
            }
        }

        public static async Task<bool> RedeemAsync(string connectionString, long accountId, decimal redeemAmount)
        {
            using (var conn = new NpgsqlConnection(connectionString))
            {
                await conn.OpenAsync();

                using (var transaction = await conn.BeginTransactionAsync())
                {
                    try
                    {
                        // Step 1: 查询可用的优惠券
                        string selectCouponsSql = @"
                            SELECT uc.user_coupon_id, ct.value
                            FROM user_coupons uc
                            JOIN coupon_types ct ON uc.coupon_type_id = ct.coupon_type_id
							// 组合索引 idx_user_coupons_account_isused_expired
                            WHERE uc.account_id = @account_id 
                              AND uc.is_used = FALSE
                              AND uc.expired_at > NOW()
                            ORDER BY uc.expired_at ASC
                        ";

                        var couponIdsToUse = new List<long>();
                        var couponValuesToUse = new List<decimal>(); // 对应每张优惠券的面值
                        decimal totalCouponValue = 0;

                        using (var cmd = new NpgsqlCommand(selectCouponsSql, conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("account_id", accountId);

                            using (var reader = await cmd.ExecuteReaderAsync())
                            {
                                while (await reader.ReadAsync())
                                {
                                    long userCouponId = reader.GetInt64(0);
                                    decimal couponValue = reader.GetDecimal(1);

                                    couponIdsToUse.Add(userCouponId);
                                    couponValuesToUse.Add(couponValue);
                                    totalCouponValue += couponValue;

                                    if (totalCouponValue >= redeemAmount)
                                    {
                                        break;
                                    }
                                }
                            }
                        }

                        decimal couponAmountToUse = Math.Min(totalCouponValue, redeemAmount);
                        decimal balanceAmountToUse = redeemAmount - couponAmountToUse;

                        // 计算每张优惠券实际使用的金额
                        var couponUsageList = new List<(long UserCouponId, decimal CouponValueUsed)>();
                        decimal remainingAmount = couponAmountToUse;

                        for (int i = 0; i < couponIdsToUse.Count; i++)
                        {
                            long userCouponId = couponIdsToUse[i];
                            decimal couponValue = couponValuesToUse[i];

                            decimal valueToUse = Math.Min(couponValue, remainingAmount);

                            couponUsageList.Add((userCouponId, valueToUse));
                            remainingAmount -= valueToUse;

                            if (remainingAmount <= 0)
                            {
                                break;
                            }
                        }

                        // Step 2: 如果需要，从账户余额扣除
                        if (balanceAmountToUse > 0)
                        {
                            // 查询账户余额并锁定账户记录
							// account_id 是主键，查询速度非常快
                            string selectBalanceSql = @"
                                SELECT balance
                                FROM account_balances
                                WHERE account_id = @account_id
                                FOR UPDATE
                            ";

                            decimal balance = 0;

                            using (var cmd = new NpgsqlCommand(selectBalanceSql, conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("account_id", accountId);

                                var result = await cmd.ExecuteScalarAsync();
                                if (result == null)
                                {
                                    // 账户不存在
                                    await transaction.RollbackAsync();
                                    return false;
                                }
                                else
                                {
                                    balance = Convert.ToDecimal(result);
                                }
                            }

                            if (balance < balanceAmountToUse)
                            {
                                // 余额不足
                                await transaction.RollbackAsync();
                                return false;
                            }

                            // 扣除余额
                            string updateBalanceSql = @"
                                UPDATE account_balances
                                SET balance = balance - @amount,
                                    updated_at = NOW()
                                WHERE account_id = @account_id
                            ";

                            using (var cmd = new NpgsqlCommand(updateBalanceSql, conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("amount", balanceAmountToUse);
                                cmd.Parameters.AddWithValue("account_id", accountId);

                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        // Step 3: 标记优惠券为已使用
                        if (couponAmountToUse > 0 && couponIdsToUse.Count > 0)
                        {
							// user_coupon_id 是主键，更新操作能快速找到需要更新的行
                            string updateCouponsSql = @"
                                UPDATE user_coupons
                                SET is_used = TRUE
                                WHERE user_coupon_id = ANY(@coupon_ids)
                            ";

                            using (var cmd = new NpgsqlCommand(updateCouponsSql, conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("coupon_ids", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Bigint, couponIdsToUse.ToArray());

                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        // Step 4: 插入兑换记录，并获取生成的 redemption_id
                        string insertRedemptionSql = @"
                            INSERT INTO redemption_records (account_id, total_amount, coupon_amount, balance_amount)
                            VALUES (@account_id, @total_amount, @coupon_amount, @balance_amount)
                            RETURNING redemption_id
                        ";

                        long redemptionId;

                        using (var cmd = new NpgsqlCommand(insertRedemptionSql, conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("account_id", accountId);
                            cmd.Parameters.AddWithValue("total_amount", redeemAmount);
                            cmd.Parameters.AddWithValue("coupon_amount", couponAmountToUse);
                            cmd.Parameters.AddWithValue("balance_amount", balanceAmountToUse);

                            var result = await cmd.ExecuteScalarAsync();
                            redemptionId = Convert.ToInt64(result);
                        }

                        // Step 5: 插入 redemption_coupons 记录
                        if (couponUsageList.Count > 0)
                        {
                            string insertRedemptionCouponsSql = @"
                                INSERT INTO redemption_coupons (redemption_id, user_coupon_id, coupon_value)
                                VALUES 
                            ";

                            var parameters = new List<NpgsqlParameter>();
                            for (int i = 0; i < couponUsageList.Count; i++)
                            {
                                insertRedemptionCouponsSql += $"(@redemption_id, @user_coupon_id_{i}, @coupon_value_{i})";

                                if (i < couponUsageList.Count - 1)
                                    insertRedemptionCouponsSql += ", ";
                                else
                                    insertRedemptionCouponsSql += ";";

                                parameters.Add(new NpgsqlParameter($"@user_coupon_id_{i}", couponUsageList[i].UserCouponId));
                                parameters.Add(new NpgsqlParameter($"@coupon_value_{i}", couponUsageList[i].CouponValueUsed));
                            }

                            using (var cmd = new NpgsqlCommand(insertRedemptionCouponsSql, conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("redemption_id", redemptionId);
                                cmd.Parameters.AddRange(parameters.ToArray());

                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        // 提交事务
                        await transaction.CommitAsync();

                        return true;
                    }
                    catch (Exception)
                    {
                        // 发生异常，回滚事务
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
            }
        }
    }
}
