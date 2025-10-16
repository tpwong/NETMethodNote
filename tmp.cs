*Thinking...*

> **Exploring the Core Task**
> 
> I'm currently focused on the user's need to transform an `INSERT ... ON CONFLICT DO UPDATE` statement. I see the main goal is to migrate this to an efficient bulk copy implementation using `Npgsql` with Dapper. I'm starting to think about the best approach to translate the conflict handling into the bulk copy context.
> 
> 
> **Decomposing the Conflict Strategy**
> 
> I'm now dissecting the `ON CONFLICT DO UPDATE` part. The challenge is mapping this upsert logic into a bulk copy workflow. Thinking about how to determine which records will cause a conflict. Then, I'll analyze how to efficiently represent this information in the bulk copy process to ensure correct updates.
> 
> 
> **Formulating a Bulk Strategy**
> 
> I'm now strategizing around the `ON CONFLICT` aspect. I'll need to integrate `NpgsqlBinaryImporter` for fast inserts, while also handling potential conflicts. Thinking about creating a staging table or using a temporary table to buffer the data first, then merging. This allows for conflict resolution.
> 
> 
> **Crafting a Combined Approach**
> 
> I'm now consolidating the staging table strategy. I'll insert into a temp table using the `NpgsqlBinaryImporter`. Then, I'll write an `UPDATE` statement that uses `MERGE` or `INSERT ... ON CONFLICT DO UPDATE` from the temporary to the target table to handle the upsert logic. This approach balances performance and conflict resolution.
> 
> 
> **Refining Staging and Merging**
> 
> I'm now honing in on the staging table's role in upsert logic. My plan involves using `NpgsqlBinaryImporter` to efficiently populate a temp table with all incoming data. Then, a single, carefully crafted SQL `MERGE` statement will perform the upserts into the target table, ensuring all conflict resolution happens within the database. I'm focusing on the performance and elegance of the `MERGE`.
> 
> 
> **Fine-Tuning Type Mapping**
> 
> I'm now focusing on precise type mapping within the `NpgsqlBinaryImporter`. I'll carefully map C# properties to the corresponding `NpgsqlDbType` values. For date/time, I'll use `TimestampTz`, for integers, `Integer`, and for monetary values like `point` and `dollar`, I'll lean toward `Numeric` for the most accurate representation. I will cover edge cases for potential `NULL` values too.
> 
> 
> **Mapping and Type Selection**
> 
> I'm now focused on the `NpgsqlDbType` mapping. Since this is a bulk copy, proper type selection is critical for performance and data integrity. Considering `TimestampTz` for timestamps with timezone, `Integer` for integers, and `Numeric` for `point` and `dollar` monetary types to ensure precision. I'll also cover null value handling to prevent potential errors.
> 
> 
> **Evaluating the Upsert Strategy**
> 
> I'm now pondering the best `ON CONFLICT` strategy. Since I'm leaning towards the temp table approach, the key is the final `MERGE` or the `INSERT ... ON CONFLICT DO UPDATE` statement. I'll prioritize the latter since it's more PostgreSQL-idiomatic. I'll analyze how to best translate the original upsert logic into this statement.
> 
> 
> **Outlining the Implementation**
> 
> I'm now designing the C# method, keeping the existing Dapper code signature. I'm now thinking about the database connection, temp table creation, `NpgsqlBinaryImporter` implementation, the upsert statement, and the transaction management. I'm focused on ensuring everything integrates seamlessly and efficiently within a transaction.
> 
> 
好的，這是一個非常經典的效能優化場景。您目前使用 Dapper 的方式，雖然語法簡潔，但它實際上是在迴圈中逐一執行 `INSERT` 命令。當 `ratings` 陣列很大時，這會導致大量的資料庫來回通訊，效能較差。

將其改為 PostgreSQL 的 Bulk Copy (批量複製) 是正確的解決方案。這利用了 PostgreSQL 底層高效的 `COPY` 命令，可以一次性將大量資料從客戶端傳輸到資料庫，速度快非常多。

不過，這裡有一個關鍵點：**原生的 Bulk Copy (`COPY` 命令) 只支援 `INSERT`，不支援 `ON CONFLICT DO UPDATE` (Upsert) 邏輯。**

因此，最標準、最高效的作法是「**臨時表 + Bulk Copy + Upsert**」策略。步驟如下：

1.  **建立一個臨時表 (Temporary Table)**，其結構與您的目標表 `earning_qualified_ratings` 完全相同。
2.  **使用 Bulk Copy** 將 C# 中的所有 `ratings` 資料高效地寫入這個臨時表。
3.  **執行一條 SQL 命令**，將臨時表的資料合併 (Upsert) 到最終的目標表中。
4.  事務結束後，臨時表會自動被清除。

下面我將為您提供完整的 C# 程式碼，並詳細解釋每一步。

### 分析與準備

從您的 SQL 來看，您的 `QualifyingRating` 物件大概有以下屬性。我會根據欄位名稱推斷其資料類型，請根據您的實際情況調整。

```csharp
// 假設您的 QualifyingRating 類別長這樣
// 請根據您的實際定義進行調整
public class QualifyingRating
{
    public string RatingType { get; set; }
    public long TranId { get; set; }
    public string Acct { get; set; }
    public string PlayerId { get; set; }
    public long? VoidTranId { get; set; } // 可為 null
    public DateTime GamingDt { get; set; }
    public DateTime PostDtm { get; set; }
    public DateTime ModifiedDtm { get; set; }
    public DateTime RatingStartDtm { get; set; }
    public DateTime RatingEndDtm { get; set; }
    public decimal TheorWin { get; set; }
    public decimal Bet { get; set; }
    public decimal CasinoWin { get; set; }
    public decimal Point { get; set; }
    public decimal Dollar { get; set; }
    public decimal Comp { get; set; }
    public decimal Ebonus { get; set; }
    public decimal Mcomp { get; set; }
    public int CardTier { get; set; }
    public string CasinoCode { get; set; }
    public string DeptCode { get; set; }
    public string GameCode { get; set; }
    public string BetType { get; set; }
    public string ChipSet { get; set; }
    public string LocnCode { get; set; }
    public string LocnInfo3 { get; set; }
    public string LocnInfo4 { get; set; }
    public string DenomCode { get; set; }
    public string StratId { get; set; }
    public string StrategyCode { get; set; }
    public string RatingCategory { get; set; }
    public string ReportSegment { get; set; }
    public string ReportGroup { get; set; }
    public string ReportArea { get; set; }
    public string Segment { get; set; }
    public string SlotLocation { get; set; }
    public string TableLocation { get; set; }
    public bool IsVoid { get; set; }
    // 注意：updated_at 是資料庫端自動更新的，C# 物件中不需要
}
```

### 使用 Npgsql Bulk Copy 的重構程式碼

您需要確保您的專案引用了 `Npgsql` 套件。Dapper 底層也是依賴它來與 PostgreSQL 通訊的。

```csharp
// 引入 Npgsql 命名空間
using Npgsql;
using NpgsqlTypes;
using Dapper;
using System.Collections.Generic;
using System.Threading.Tasks;

public class EarningRatingRepository
{
    private readonly string _connectionString;

    public EarningRatingRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// 使用 Bulk Copy 高效地批量寫入或更新評級資料
    /// </summary>
    /// <param name="ratings">要寫入的評級資料集合</param>
    /// <returns>受影響的行數</returns>
    public async Task<int> WriteToDbBulkCopy(IEnumerable<QualifyingRating> ratings)
    {
        // 確保有資料需要處理
        if (ratings == null || !ratings.Any())
        {
            return 0;
        }

        // 1. 建立連接並開啟
        // NpgsqlConnection 是實現 Bulk Copy 的關鍵
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        // 2. 開啟一個事務，確保操作的原子性
        // 臨時表的建立、資料匯入、合併操作應該在同一個事務中完成
        await using var transaction = await connection.BeginTransactionAsync();

        // 3. 建立一個臨時表，ON COMMIT DROP 確保事務結束後自動刪除
        // 臨時表的結構應該和目標表 earning_qualified_ratings 一致
        // 使用 LIKE 語法可以快速複製表結構
        var tempTableName = "temp_earning_ratings";
        await connection.ExecuteAsync($"CREATE TEMP TABLE {tempTableName} (LIKE earning_qualified_ratings INCLUDING DEFAULTS) ON COMMIT DROP;");

        // 4. 使用 Npgsql 的二進位匯入器 (BinaryImporter) 進行 Bulk Copy
        // 這是最高效能的部分
        // 注意：欄位順序必須和下面的 importer.Write() 順序完全一致
        var copyCommand = $@"
            COPY {tempTableName} (
                rating_type, tran_id, acct, player_id, void_tran_id,
                gaming_dt, post_dtm, modified_dtm, rating_start_dtm, rating_end_dtm,
                theor_win, bet, casino_win,
                point, dollar, comp, ebonus, mcomp,
                card_tier, casino_code, dept_code, game_code, bet_type, chip_set,
                locn_code, locn_info3, locn_info4,
                denom_code, strat_id, strategy_code, rating_category,
                report_segment, report_group, report_area, segment,
                slot_location, table_location, is_void
            ) FROM STDIN (FORMAT BINARY)";
        
        await using (var importer = await connection.BeginBinaryImportAsync(copyCommand))
        {
            foreach (var rating in ratings)
            {
                await importer.StartRowAsync();

                // 按照 COPY 命令中定義的順序寫入每個欄位
                // 注意處理可為 null 的值，並指定正確的 NpgsqlDbType
                await importer.WriteAsync(rating.RatingType, NpgsqlDbType.Varchar);
                await importer.WriteAsync(rating.TranId, NpgsqlDbType.Bigint);
                await importer.WriteAsync(rating.Acct, NpgsqlDbType.Varchar);
                await importer.WriteAsync(rating.PlayerId, NpgsqlDbType.Varchar);
                await importer.WriteAsync(rating.VoidTranId, NpgsqlDbType.Bigint); // WriteAsync 會自動處理 C# null 到 DB NULL
                await importer.WriteAsync(rating.GamingDt, NpgsqlDbType.TimestampTz); // 假設是 timestamptz
                await importer.WriteAsync(rating.PostDtm, NpgsqlDbType.TimestampTz);
                await importer.WriteAsync(rating.ModifiedDtm, NpgsqlDbType.TimestampTz);
                await importer.WriteAsync(rating.RatingStartDtm, NpgsqlDbType.TimestampTz);
                await importer.WriteAsync(rating.RatingEndDtm, NpgsqlDbType.TimestampTz);
                await importer.WriteAsync(rating.TheorWin, NpgsqlDbType.Numeric);
                await importer.WriteAsync(rating.Bet, NpgsqlDbType.Numeric);
                await importer.WriteAsync(rating.CasinoWin, NpgsqlDbType.Numeric);
                await importer.WriteAsync(rating.Point, NpgsqlDbType.Numeric);
                await importer.WriteAsync(rating.Dollar, NpgsqlDbType.Numeric);
                await importer.WriteAsync(rating.Comp, NpgsqlDbType.Numeric);
                await importer.WriteAsync(rating.Ebonus, NpgsqlDbType.Numeric);
                await importer.WriteAsync(rating.Mcomp, NpgsqlDbType.Numeric);
                await importer.WriteAsync(rating.CardTier, NpgsqlDbType.Integer);
                await importer.WriteAsync(rating.CasinoCode, NpgsqlDbType.Varchar);
                await importer.WriteAsync(rating.DeptCode, NpgsqlDbType.Varchar);
                await importer.WriteAsync(rating.GameCode, NpgsqlDbType.Varchar);
                await importer.WriteAsync(rating.BetType, NpgsqlDbType.Varchar);
                await importer.WriteAsync(rating.ChipSet, NpgsqlDbType.Varchar);
                await importer.WriteAsync(rating.LocnCode, NpgsqlDbType.Varchar);
                await importer.WriteAsync(rating.LocnInfo3, NpgsqlDbType.Varchar);
                await importer.WriteAsync(rating.LocnInfo4, NpgsqlDbType.Varchar);
                await importer.WriteAsync(rating.DenomCode, NpgsqlDbType.Varchar);
                await importer.WriteAsync(rating.StratId, NpgsqlDbType.Varchar);
                await importer.WriteAsync(rating.StrategyCode, NpgsqlDbType.Varchar);
                await importer.WriteAsync(rating.RatingCategory, NpgsqlDbType.Varchar);
                await importer.WriteAsync(rating.ReportSegment, NpgsqlDbType.Varchar);
                await importer.WriteAsync(rating.ReportGroup, NpgsqlDbType.Varchar);
                await importer.WriteAsync(rating.ReportArea, NpgsqlDbType.Varchar);
                await importer.WriteAsync(rating.Segment, NpgsqlDbType.Varchar);
                await importer.WriteAsync(rating.SlotLocation, NpgsqlDbType.Varchar);
                await importer.WriteAsync(rating.TableLocation, NpgsqlDbType.Varchar);
                await importer.WriteAsync(rating.IsVoid, NpgsqlDbType.Boolean);
            }

            // 完成匯入
            await importer.CompleteAsync();
        }

        // 5. 執行合併 (Upsert) 邏輯
        // 從臨時表將資料 INSERT 或 UPDATE 到目標表
        // 這和您原來的 ON CONFLICT 邏輯幾乎一樣，只是資料來源變成了臨時表
        var mergeSql = $@"
            INSERT INTO earning_qualified_ratings (
                rating_type, tran_id, acct, player_id, void_tran_id,
                gaming_dt, post_dtm, modified_dtm, rating_start_dtm, rating_end_dtm,
                theor_win, bet, casino_win, point, dollar, comp, ebonus, mcomp,
                card_tier, casino_code, dept_code, game_code, bet_type, chip_set,
                locn_code, locn_info3, locn_info4, denom_code, strat_id, strategy_code, rating_category,
                report_segment, report_group, report_area, segment, slot_location, table_location, is_void
            )
            SELECT 
                rating_type, tran_id, acct, player_id, void_tran_id,
                gaming_dt, post_dtm, modified_dtm, rating_start_dtm, rating_end_dtm,
                theor_win, bet, casino_win, point, dollar, comp, ebonus, mcomp,
                card_tier, casino_code, dept_code, game_code, bet_type, chip_set,
                locn_code, locn_info3, locn_info4, denom_code, strat_id, strategy_code, rating_category,
                report_segment, report_group, report_area, segment, slot_location, table_location, is_void
            FROM {tempTableName}
            ON CONFLICT (earning_qualified_ratings_pkey) DO UPDATE SET
                player_id = excluded.player_id,
                void_tran_id = excluded.void_tran_id,
                post_dtm = excluded.post_dtm,
                modified_dtm = excluded.modified_dtm,
                rating_start_dtm = excluded.rating_start_dtm,
                rating_end_dtm = excluded.rating_end_dtm,
                theor_win = excluded.theor_win,
                bet = excluded.bet,
                casino_win = excluded.casino_win,
                point = excluded.point,
                dollar = excluded.dollar,
                comp = excluded.comp,
                ebonus = excluded.ebonus,
                mcomp = excluded.mcomp,
                card_tier = excluded.card_tier,
                casino_code = excluded.casino_code,
                dept_code = excluded.dept_code,
                game_code = excluded.game_code,
                bet_type = excluded.bet_type,
                chip_set = excluded.chip_set,
                locn_code = excluded.locn_code,
                locn_info3 = excluded.locn_info3,
                locn_info4 = excluded.locn_info4,
                denom_code = excluded.denom_code,
                strat_id = excluded.strat_id,
                strategy_code = excluded.strategy_code,
                rating_category = excluded.rating_category,
                report_segment = excluded.report_segment,
                report_group = excluded.report_group,
                report_area = excluded.report_area,
                segment = excluded.segment,
                slot_location = excluded.slot_location,
                table_location = excluded.table_location,
                is_void = excluded.is_void,
                updated_at = now();";

        var affectedRows = await connection.ExecuteAsync(mergeSql, transaction: transaction);

        // 6. 提交事務
        await transaction.CommitAsync();

        return affectedRows;
    }
}
```

### 程式碼解釋與注意事項

1.  **`NpgsqlConnection`**: 我們直接使用 `Npgsql` 的連接物件，而不是依賴 Dapper 的擴充方法來開啟連接，因為我們需要呼叫 `BeginBinaryImportAsync` 這個 `Npgsql` 特有的方法。
2.  **事務 (`BeginTransactionAsync`)**: 這是至關重要的。整個「建立臨時表 -> 匯入資料 -> 合併資料」的過程必須是原子的。如果中途任何一步失敗，整個事務都會回滾，不會留下髒資料或不完整的臨時表。
3.  **`CREATE TEMP TABLE ... ON COMMIT DROP`**: 這是建立臨時表的最佳實踐。`TEMP` 或 `TEMPORARY` 關鍵字表示這個表只在當前的資料庫會話中可見。`ON COMMIT DROP` 確保在事務成功提交時，這個臨時表會被自動刪除，無需手動 `DROP`。
4.  **`BeginBinaryImportAsync`**: 這是執行 Bulk Copy 的核心。它接收一個 `COPY ... FROM STDIN (FORMAT BINARY)` 命令。
    *   `COPY ... FROM STDIN`: 告訴 PostgreSQL 資料將從客戶端標準輸入流中傳入。
    *   `(FORMAT BINARY)`: 使用二進位格式傳輸資料。這比文字格式更高效，也避免了轉義、引號等複雜問題。
5.  **`importer.WriteAsync()`**: 在迴圈中，我們將每個物件的每個屬性按照 `COPY` 命令中聲明的順序寫入匯入器。
    *   **類型匹配**: `NpgsqlDbType` 必須與資料庫欄位的類型精確匹配（或可隱式轉換）。例如，`DateTime` 對應 `TimestampTz` 或 `Timestamp`，`decimal` 對應 `Numeric`，`string` 對應 `Varchar` 或 `Text`。請務必檢查您的表定義以確保類型正確。
    *   **Null 處理**: `importer.WriteAsync` 方法可以很好地處理 C# 中的 `null` 值，它會自動將其轉換為資料庫的 `NULL`。
6.  **合併 SQL (`INSERT ... ON CONFLICT`)**: 最後這一步，我們執行一條 SQL。這條 SQL 的邏輯和您原來的一樣，但它的資料來源是我們剛剛填充好的臨時表 `temp_earning_ratings`，而不是客戶端傳來的一個個參數。`excluded` 虛擬表代表了如果發生衝突時，試圖插入的那一行新資料。
7.  **效能**: 這種方法的效能遠超 Dapper 的迴圈執行。資料只在 C# 端被序列化一次，然後通過一個高效的流一次性傳輸到 PostgreSQL。所有的 `UPDATE` 和 `INSERT` 判斷都在資料庫伺服器端完成，這是最高效的方式。

將您原來的 `WriteToDb` 方法替換為這個 `WriteToDbBulkCopy` 方法，您將會看到在處理大量資料時效能的巨大提升。