import threading

import pool
from conftest import fill_pool, generate, login, next_hole
from test_votes import rows


def batches(dsn):
    return [(r["id"], r["status"]) for r in rows(dsn, "SELECT id, status FROM batches ORDER BY id")]


def test_batch_creation_is_idempotent_and_race_safe(make_client, clean_db):
    results = []
    threads = [threading.Thread(target=lambda: results.append(pool.ensure_batch(clean_db, 4))) for _ in range(8)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()
    assert len({b.id for b, _ in results}) == 1 and sum(created for _, created in results) == 1
    client, _ = make_client()
    with client:  # app startup: a batch already exists, so none is added
        pass
    assert batches(clean_db) == [(1, "generating")]


def test_startup_starts_the_first_batch(make_client, clean_db):
    client, _ = make_client()
    with client:
        assert batches(clean_db) == [(1, "generating")]
        assert len(fill_pool(client)) == 4
    assert batches(clean_db) == [(1, "ready")]
    trained = rows(clean_db, "SELECT trained_on_votes FROM batches")[0]["trained_on_votes"]
    assert trained == 0  # no votes yet: nothing to train on


def rate(client, hole_id, rating="up"):
    r = client.post("/api/rate", json={"id": hole_id, "rating": rating})
    assert r.status_code == 200, r.text


def serve_and_rate(client, n):
    for _ in range(n):
        rate(client, next_hole(client)["hole"]["id"])


def test_next_never_repeats_across_users_and_batches(make_client, clean_db):
    obi = login(make_client()[0])
    sam = login(make_client()[0], "sam")
    waiting = next_hole(obi)
    assert waiting["state"] == "generating" and waiting["hole"] is None
    assert waiting["pool"] == {"batch": 1, "size": 4, "status": "generating", "ready": 0, "seen": 0,
                               "capped": False}
    first = fill_pool(obi)
    assert len(first) == 4

    obi_seen = [next_hole(obi)["hole"]["id"] for _ in range(4)]
    sam_seen = [next_hole(sam)["hole"]["id"] for _ in range(4)]
    assert sorted(obi_seen) == sorted(sam_seen) == first
    assert obi_seen != sam_seen  # per-user shuffle: friends start on different holes
    done = next_hole(obi)
    assert done["state"] == "generating" and done["pool"]["batch"] == 2  # obi saw all of batch 1: refilled

    second = sorted(set(fill_pool(obi)) - set(first))
    assert len(second) == 4
    more = [next_hole(obi)["hole"]["id"] for _ in range(4)]
    assert sorted(more) == second  # the new batch, never a repeat
    assert next_hole(obi)["state"] == "generating"
    assert len(set(obi_seen + more)) == 8


def test_newest_batch_first_and_old_unseen_holes_while_it_generates(make_client):
    obi = login(make_client()[0])
    sam = login(make_client()[0], "sam")
    next_hole(obi)
    first = fill_pool(obi)
    serve_and_rate(obi, 2)  # obi rated 50%: batch 2 starts generating
    assert next_hole(sam)["hole"]["id"] in first  # nothing new ready yet: sam still gets batch 1
    second = set(fill_pool(obi)) - set(first)
    assert next_hole(sam)["hole"]["id"] in second  # newest batch first


def test_refill_counts_ratings_not_skips_and_fires_once_per_batch(make_client, clean_db):
    obi = login(make_client()[0])
    sam = login(make_client()[0], "sam")
    next_hole(obi)
    fill_pool(obi)
    next_hole(obi), next_hole(obi), next_hole(obi)
    assert len(batches(clean_db)) == 1  # 3 of 4 skipped: skips don't count until the batch is used up
    serve_and_rate(sam, 1)
    assert len(batches(clean_db)) == 1  # 1 of 4 rated
    serve_and_rate(sam, 1)
    assert len(batches(clean_db)) == 2  # 2 of 4 = 50% rated
    serve_and_rate(sam, 2)
    next_hole(obi)  # both used up batch 1 now: still no third batch, and none while batch 2 generates
    assert batches(clean_db) == [(1, "ready"), (2, "generating")]
    assert pool.maybe_refill(clean_db, 4, 0.5, 100) is None
    fill_pool(obi)
    serve_and_rate(sam, 2)
    assert len(batches(clean_db)) == 3  # 50% of batch 2


def test_skip_spam_is_bounded_by_the_unrated_cap(make_client, clean_db):
    """T-12: one user skipping forever gets at most one batch per finished batch, and none past the cap."""
    obi = login(make_client(pool_max_unrated=8)[0])
    next_hole(obi)
    for _ in range(10):
        fill_pool(obi)
        for _ in range(6):
            next_hole(obi)
    assert batches(clean_db) == [(1, "ready"), (2, "ready")]  # 8 unrated holes: capped
    capped = next_hole(obi)
    assert capped["state"] == "generating" and capped["pool"]["capped"]
    rate(obi, pool.hole_ids(clean_db).pop())  # a vote brings the pool under the cap: the next batch starts
    assert len(batches(clean_db)) == 3


def test_an_empty_finished_batch_does_not_wedge_the_refill(make_client, clean_db):
    obi = login(make_client()[0])
    next_hole(obi)
    pool.mark_ready(clean_db, 1)  # every generation failed: ready with 0 holes
    assert next_hole(obi)["state"] == "generating" and len(batches(clean_db)) == 1  # not straight away
    with pool.connect(clean_db) as conn:
        conn.execute("UPDATE batches SET created_at = now() - interval '1 hour'")
    next_hole(obi)
    assert batches(clean_db) == [(1, "ready"), (2, "generating")]


def test_peeking_does_not_count_as_seen_but_opening_does(make_client):
    obi = login(make_client()[0])
    next_hole(obi)
    a, b, *_ = fill_pool(obi)
    assert obi.get(f"/api/holes/{a}/hole.json?peek=true").status_code == 200
    assert obi.get(f"/api/holes/{b}/hole.json").status_code == 200  # opened for real: seen
    assert obi.get(f"/api/holes/{a}").status_code == 200  # the summary never marks it seen (T-4)
    served = [next_hole(obi)["hole"]["id"] for _ in range(3)]
    assert a in served and b not in served


def test_pool_holes_survive_pruning(make_client):
    obi = login(make_client(keep_unrated=1, view_grace_hours=0)[0])
    next_hole(obi)
    pooled = fill_pool(obi)
    pruned = generate(obi, seed=41)["pruned"] + generate(obi, seed=42)["pruned"]
    assert pruned and not set(pruned) & set(pooled)
    for hole_id in pooled:
        assert obi.get(f"/api/holes/{hole_id}").status_code == 200


def test_peeked_next_hole_is_not_seen_until_taken(make_client, clean_db):
    """Prefetch: `?peek=true` names the next hole without using it up; `?take=` serves exactly that hole once."""
    obi = login(make_client()[0])
    next_hole(obi)
    holes = fill_pool(obi)
    peek = lambda: obi.get("/api/next?peek=true").json()  # noqa: E731
    first = peek()["hole"]["id"]
    assert peek()["hole"]["id"] == first and not rows(clean_db, "SELECT * FROM hole_views")  # nothing marked seen
    assert obi.get(f"/api/next?take={first}").json()["hole"]["id"] == first
    second = peek()["hole"]["id"]
    assert second != first
    # Taking a hole already seen (another tab took it) never repeats it: the next unseen hole comes instead.
    assert obi.get(f"/api/next?take={first}").json()["hole"]["id"] == second
    # Taking an id that is not an unseen pool hole falls back to the usual order.
    rest = [obi.get("/api/next?take=nope").json()["hole"]["id"] for _ in range(2)]
    assert sorted([first, second, *rest]) == holes  # each hole served exactly once
    assert peek()["state"] == "generating" and peek()["hole"] is None
    assert obi.get("/api/next?take=" + "x" * 300).status_code == 422


def test_unseen_buffer_starts_batches_early_but_one_at_a_time_and_capped(make_client, clean_db):
    """TRAINER_POOL_MIN_UNSEEN: a user low on unseen holes starts the next batch without rating anything, yet skipping
    still can't stack batches (one generates at a time) or pass the unrated cap."""
    obi = login(make_client(pool_min_unseen=3, pool_max_unrated=8)[0])
    next_hole(obi)
    fill_pool(obi)
    next_hole(obi)  # 3 unseen left: not low yet
    assert batches(clean_db) == [(1, "ready")]
    next_hole(obi)  # 2 unseen left: batch 2 starts, though nobody has rated anything
    assert batches(clean_db) == [(1, "ready"), (2, "generating")]
    for _ in range(5):  # skip-spam while it generates: still one batch
        next_hole(obi)
    assert batches(clean_db) == [(1, "ready"), (2, "generating")]
    fill_pool(obi)
    for _ in range(6):  # 8 holes nobody voted on: capped, however low obi runs
        next_hole(obi)
    assert batches(clean_db) == [(1, "ready"), (2, "ready")]
    assert next_hole(obi)["pool"]["capped"]
