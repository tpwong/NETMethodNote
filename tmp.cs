CREATE INDEX idx_rating_dead_letters_gaming_dt 
ON earning.rating_dead_letters ((to_date(message_value->>'GamingDt', 'YYYY-MM-DD')))
WHERE message_value->>'GamingDt' IS NOT NULL;