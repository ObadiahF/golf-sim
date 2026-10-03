-- Self-update releases of the sim (update/UpdateService.java). A release is one build of the game for one platform:
-- its manifest lists every file with its size and sha256. File contents live on disk in a content-addressed blob store
-- (update/BlobStore.java, /data/updates/blobs), so a file unchanged between releases is stored once.
-- "Latest" is the release with the highest build number; only the newest few per platform are kept.
create table update_releases (
    id          bigserial    primary key,
    platform    varchar(32)  not null,
    version     varchar(64)  not null,
    build       bigint       not null,
    note        varchar(500),
    total_size  bigint       not null,
    created_at  timestamptz  not null default now(),
    unique (platform, version),
    unique (platform, build)
);

create table update_files (
    release_id  bigint       not null references update_releases (id) on delete cascade,
    path        varchar(400) not null,
    size        bigint       not null,
    sha256      char(64)     not null,
    executable  boolean      not null default false,
    primary key (release_id, path)
);

create index update_files_sha256 on update_files (sha256);
