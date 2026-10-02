-- Green-slope playability (Q5-1): pond banks tilted some greens to 15 % at the pin before generator v5. Each check
-- now records which rules judged it (hole_checks.CHECK_VERSION; rows from before this migration are version 1, the
-- tee shot only), and the startup backfill re-checks every hole judged by older rules.

ALTER TABLE hole_checks
    ADD COLUMN check_version   integer NOT NULL DEFAULT 1,
    ADD COLUMN green_pin_slope real,             -- steepest grade within 2 m of the pin (0.04 = 4 %)
    ADD COLUMN green_max_slope real;             -- steepest grade on the putting surface (recorded, not judged)
