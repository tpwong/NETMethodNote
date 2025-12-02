UPDATE earning_qualified_ratings t
SET acct      = @newAcct,
    player_id = @newPlayerId,
    updated_at = CURRENT_TIMESTAMP
WHERE t.acct = @oldAcct
  AND NOT EXISTS (
      SELECT 1
      FROM earning_qualified_ratings x
      WHERE x.acct        = @newAcct
        AND x.gaming_dt   = t.gaming_dt
        AND x.casino_code = t.casino_code
        AND x.dept_code   = t.dept_code
        AND x.tran_id     = t.tran_id
  );