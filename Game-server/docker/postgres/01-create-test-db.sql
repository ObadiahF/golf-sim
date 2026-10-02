-- Runs once when the postgres volume is first created: a separate database for `docker compose run --rm test`.
create database golf_test owner golf;
