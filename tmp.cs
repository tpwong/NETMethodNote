CREATE INDEX idx_rating_dead_letters_gaming_dt
ON earning.rating_dead_letters ((message_value->'GamingDt')::jsonb::text::date)
WHERE message_value ? 'GamingDt';