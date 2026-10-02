-- Players are identified only by name; names are unique ignoring case.
create table players (
    id         bigserial primary key,
    name       varchar(40) not null,
    created_at timestamptz not null default now()
);
create unique index players_name_ci_uk on players (lower(name));

create table games (
    id          bigserial primary key,
    status      varchar(16) not null check (status in ('IN_PROGRESS', 'FINISHED', 'ABANDONED')),
    holes_count integer     not null default 9 check (holes_count between 1 and 18),
    course_name varchar(100),
    created_at  timestamptz not null default now(),
    finished_at timestamptz
);
-- At most one game can be in progress at a time.
create unique index games_single_in_progress_uk on games (status) where status = 'IN_PROGRESS';
create index games_created_at_idx on games (created_at desc);

create table game_players (
    game_id    bigint  not null references games (id) on delete cascade,
    player_id  bigint  not null references players (id),
    turn_order integer not null,
    primary key (game_id, player_id),
    unique (game_id, turn_order)
);

create table hole_scores (
    id          bigserial primary key,
    game_id     bigint      not null,
    player_id   bigint      not null,
    hole_number integer     not null check (hole_number >= 1),
    par         integer     not null check (par between 1 and 10),
    strokes     integer     not null check (strokes between 1 and 99),
    created_at  timestamptz not null default now(),
    updated_at  timestamptz not null default now(),
    unique (game_id, player_id, hole_number),
    foreign key (game_id, player_id) references game_players (game_id, player_id) on delete cascade
);
