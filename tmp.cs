UPDATE earning_qualified_ratings t
SET acct      = @newAcct,
    player_id = @newPlayerId,
    updated_at = CURRENT_TIMESTAMP
WHERE acct = @oldAcct
  AND NOT EXISTS (
      SELECT 1
      FROM earning_qualified_ratings x
      WHERE x.acct = @newAcct           -- 這裡用跟 PK/unique 同一組 key
  );