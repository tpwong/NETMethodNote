using Dapper;
using Npgsql;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class EarningRatingRepository
{
    private readonly string _connectionString;

    public EarningRatingRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Bulk inserts or updates ratings using Dapper and PostgreSQL's UNNEST function.
    /// This method is compatible with older Npgsql versions.
    /// </summary>
    public async Task<int> WriteToDbWithUnnest(IEnumerable<QualifyingRating> ratings)
    {
        if (ratings == null || !ratings.Any())
        {
            return 0;
        }

        // The SQL is more complex, but the C# code is simpler.
        // We pass the entire collection as a single parameter. Dapper and Npgsql
        // will automatically convert it into parallel arrays for UNNEST.
        const string mergeSql = @"
            INSERT INTO earning_qualified_ratings (
                rating_type, tran_id, acct, player_id, void_tran_id,
                gaming_dt, post_dtm, modified_dtm, rating_start_dtm, rating_end_dtm,
                theor_win, bet, casino_win, point, dollar, comp, ebonus, mcomp,
                card_tier, casino_code, dept_code, game_code, bet_type, chip_set,
                locn_code, locn_info3, locn_info4, denom_code, strat_id, strategy_code, rating_category,
                report_segment, report_group, report_area, segment, slot_location, table_location, is_void
            )
            SELECT 
                u.RatingType, u.TranId, u.Acct, u.PlayerId, u.VoidTranId,
                u.GamingDt, u.PostDtm, u.ModifiedDtm, u.RatingStartDtm, u.RatingEndDtm,
                u.TheorWin, u.Bet, u.CasinoWin, u.Point, u.Dollar, u.Comp, u.Ebonus, u.Mcomp,
                u.CardTier, u.CasinoCode, u.DeptCode, u.GameCode, u.BetType, u.ChipSet,
                u.LocnCode, u.LocnInfo3, u.LocnInfo4, u.DenomCode, u.StratId, u.StrategyCode, u.RatingCategory,
                u.ReportSegment, u.ReportGroup, u.ReportArea, u.Segment, u.SlotLocation, u.TableLocation, u.IsVoid
            FROM UNNEST(
                @RatingType, @TranId, @Acct, @PlayerId, @VoidTranId,
                @GamingDt, @PostDtm, @ModifiedDtm, @RatingStartDtm, @RatingEndDtm,
                @TheorWin, @Bet, @CasinoWin, @Point, @Dollar, @Comp, @Ebonus, @Mcomp,
                @CardTier, @CasinoCode, @DeptCode, @GameCode, @BetType, @ChipSet,
                @LocnCode, @LocnInfo3, @LocnInfo4, @DenomCode, @StratId, @StrategyCode, @RatingCategory,
                @ReportSegment, @ReportGroup, @ReportArea, @Segment, @SlotLocation, @TableLocation, @IsVoid
            ) AS u(
                RatingType, TranId, Acct, PlayerId, VoidTranId,
                GamingDt, PostDtm, ModifiedDtm, RatingStartDtm, RatingEndDtm,
                TheorWin, Bet, CasinoWin, Point, Dollar, Comp, Ebonus, Mcomp,
                CardTier, CasinoCode, DeptCode, GameCode, BetType, ChipSet,
                LocnCode, LocnInfo3, LocnInfo4, DenomCode, StratId, StrategyCode, RatingCategory,
                ReportSegment, ReportGroup, ReportArea, Segment, SlotLocation, TableLocation, IsVoid
            )
            ON CONFLICT ON CONSTRAINT earning_qualified_ratings_pkey DO UPDATE SET
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

        await using var connection = new NpgsqlConnection(_connectionString);
        var affectedRows = await connection.ExecuteAsync(mergeSql, ratings);
        
        return affectedRows;
    }
}