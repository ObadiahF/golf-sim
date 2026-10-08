-- Course types for the game (GET /api/game/presets, random-holes?preset=): each pool hole's preset, and stock
-- batches. A stock batch (pool_worker.py) generates holes of one preset that is short of TRAINER_POOL_MIN_STOCK
-- playable holes; a preference batch (preset NULL) lets the model pick the preset, as before.

ALTER TABLE batches ADD COLUMN preset text;      -- NULL: preference batch; else a stock batch of this preset

ALTER TABLE pool_holes ADD COLUMN preset text;
-- Existing holes: the id starts with the preset (generate.hole_id: <preset>_<seed>_<digest>; no preset has a '_').
UPDATE pool_holes SET preset = split_part(hole_id, '_', 1);
ALTER TABLE pool_holes ALTER COLUMN preset SET NOT NULL;
