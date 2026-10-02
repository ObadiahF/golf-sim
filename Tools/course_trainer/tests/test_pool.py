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


def test_next_never_repeats_across_users_and_batches(make_client, clean_db):
    obi = login(make_client()[0])
    sam = login(make_client()[0], "sam")
    waiting = next_hole(obi)
    assert waiting["state"] == "generating" and waiting["hole"] is None
    assert waiting["pool"] == {"batch": 1, "size": 4, "status": "generating", "ready": 0, "seen": 0}
    first = fill_pool(obi)
    assert len(first) == 4

    obi_seen = [next_hole(obi)["hole"]["id"] for _ in range(4)]
    sam_seen = [next_hole(sam)["hole"]["id"] for _ in range(4)]
    assert sorted(obi_seen) == sorted(sam_seen) == first
    assert obi_seen != sam_seen  # per-user shuffle: friends start on different holes
    done = next_hole(obi)
    assert done["state"] == "generating" and done["pool"]["batch"] == 2  # refilled at 50%, still generating

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
    next_hole(obi), next_hole(obi)  # obi reaches 50%: batch 2 starts generating
    assert next_hole(sam)["hole"]["id"] in first  # nothing new ready yet: sam still gets batch 1
    second = set(fill_pool(obi)) - set(first)
    assert next_hole(sam)["hole"]["id"] in second  # newest batch first


def test_refill_fires_exactly_once_per_batch(make_client, clean_db):
    obi = login(make_client()[0])
    sam = login(make_client()[0], "sam")
    next_hole(obi)
    fill_pool(obi)
    next_hole(obi)
    assert len(batches(clean_db)) == 1  # 1 of 4 seen
    next_hole(obi)
    assert len(batches(clean_db)) == 2  # 2 of 4 = 50%
    for _ in range(4):
        next_hole(obi), next_hole(sam)  # both pass 50% of batch 1 now: no third batch
    assert batches(clean_db) == [(1, "ready"), (2, "generating")]
    assert pool.maybe_refill(clean_db, 4, 0.5) is None
    fill_pool(obi)
    next_hole(sam), next_hole(sam)
    assert len(batches(clean_db)) == 3  # 50% of batch 2


def test_peeking_does_not_count_as_seen_but_opening_does(make_client):
    obi = login(make_client()[0])
    next_hole(obi)
    a, b, *_ = fill_pool(obi)
    assert obi.get(f"/api/holes/{a}/hole.json?peek=true").status_code == 200
    assert obi.get(f"/api/holes/{b}/hole.json").status_code == 200  # a shared link: seen
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
