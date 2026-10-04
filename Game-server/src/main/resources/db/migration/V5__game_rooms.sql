-- Rooms (game/Rooms.java): one sim and its phones per room. Each game belongs to the room it was started in;
-- '' is the default room, where every game before rooms lives and clients that send no room still play.
alter table games add column room varchar(8) not null default '';

-- At most one game in progress per room (was: per server).
drop index games_single_in_progress_uk;
create unique index games_in_progress_per_room_uk on games (room) where status = 'IN_PROGRESS';
