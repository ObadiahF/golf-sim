-- The one global ball-physics profile: overrides of the sim's built-in ground response, one row per surface and
-- field. A surface/field without a row uses the game's built-in value; deleting every row resets to defaults.
-- Names and ranges are checked by the server (physics/PhysicsField.java, PhysicsProfile.SURFACES).
create table physics_overrides (
    surface    varchar(16)      not null,
    field      varchar(16)      not null,
    value      double precision not null,
    updated_at timestamptz      not null default now(),
    primary key (surface, field)
);
