import pytest

import users
from auth import COOKIE, LoginThrottle, SessionSigner
from conftest import PASSWORDS, login


def test_password_hashing():
    stored = users.hash_password("hunter2")
    assert stored.startswith("scrypt$") and "hunter2" not in stored
    assert users.verify_password("hunter2", stored)
    assert not users.verify_password("hunter3", stored)
    assert not users.verify_password("hunter2", None)
    assert not users.verify_password("hunter2", "garbage")


def test_login_sets_a_safe_cookie_and_me_works(make_client):
    client, _ = make_client()
    r = client.post("/api/login", json={"name": "OBI", "password": PASSWORDS["obi"]})  # names are case-insensitive
    assert r.status_code == 200 and r.json() == {"name": "obi"}
    cookie = r.headers["set-cookie"]
    assert f"{COOKIE}=" in cookie and "HttpOnly" in cookie and "SameSite=lax" in cookie
    assert "Max-Age=2592000" in cookie and "Secure" not in cookie
    assert client.get("/api/me").json() == {"name": "obi"}


def test_secure_cookie_when_configured(make_client):
    client, _ = make_client(cookie_secure=True)
    r = client.post("/api/login", json={"name": "obi", "password": PASSWORDS["obi"]})
    assert "Secure" in r.headers["set-cookie"]


def test_bad_password_and_unknown_user(make_client):
    client, _ = make_client()
    assert client.post("/api/login", json={"name": "obi", "password": "nope"}).status_code == 401
    assert client.post("/api/login", json={"name": "nobody", "password": "x"}).status_code == 401
    assert client.post("/api/login", json={"name": "legacy", "password": ""}).status_code == 422
    assert client.get("/api/me").status_code == 401


@pytest.mark.parametrize("path", ["/api/presets", "/api/status", "/api/holes", "/api/holes/x/hole.json",
                                  "/api/export/votes.jsonl"])
def test_api_needs_the_cookie(make_client, path):
    client, _ = make_client()
    assert client.get(path).status_code == 401
    assert client.post("/api/generate", json={"preset": "links"}).status_code == 401
    assert client.post("/api/rate", json={"id": "x", "rating": "up"}).status_code == 401
    client.cookies.set(COOKIE, "forged.signature")
    assert client.get(path).status_code == 401


def test_logout(client):
    assert client.get("/api/presets").status_code == 200
    r = client.post("/api/logout")
    assert r.status_code == 200 and f'{COOKIE}=""' in r.headers["set-cookie"]
    assert client.get("/api/presets").status_code == 401


def test_password_change_ends_old_sessions(client, clean_db):
    assert client.get("/api/me").status_code == 200
    users.set_password(clean_db, "obi", "new-pw")
    assert client.get("/api/me").status_code == 401
    assert login(client, "obi", "new-pw").get("/api/me").status_code == 200


def test_signer_rejects_tampering_and_expiry():
    signer = SessionSigner("secret", days=30)
    user = users.User(1, "obi", "hash")
    token = signer.make(user)
    assert signer.read(token)["u"] == 1
    assert signer.read(token[:-2] + "xx") is None
    assert SessionSigner("other", 30).read(token) is None
    assert SessionSigner("secret", days=-1).read(SessionSigner("secret", days=-1).make(user)) is None


def test_throttle_backs_off_then_forgets():
    t = LoginThrottle()
    keys = ["ip:1.2.3.4"]
    for _ in range(LoginThrottle.FREE - 1):
        t.failed(keys, now=100)
    assert t.retry_after(keys, now=100) == 0
    t.failed(keys, now=100)
    assert t.retry_after(keys, now=100) == 2
    t.failed(keys, now=100)
    assert t.retry_after(keys, now=101) == 3
    assert t.retry_after(keys, now=100 + LoginThrottle.FORGET + 1) == 0
    t.succeeded(keys)
    assert t.retry_after(keys, now=100) == 0


def test_repeated_failures_lock_the_name(make_client):
    client, _ = make_client()
    for _ in range(LoginThrottle.FREE):
        assert client.post("/api/login", json={"name": "sam", "password": "wrong"}).status_code == 401
    r = client.post("/api/login", json={"name": "sam", "password": PASSWORDS["sam"]})
    assert r.status_code == 429 and "Retry-After" in r.headers


def test_seed_is_idempotent_and_legacy_cannot_log_in(clean_db):
    assert users.parse_seed_users(" ann:pw1 , bob:p:w ") == [("ann", "pw1"), ("bob", "p:w")]
    with pytest.raises(ValueError):
        users.parse_seed_users("ann")
    assert users.seed(clean_db, "ann:pw1,bob:pw2") == ["created user ann", "created user bob"]
    users.set_password(clean_db, "ann", "changed")
    assert users.seed(clean_db, "ann:pw1,bob:pw2") == []
    ann = users.find_user(clean_db, "ANN")
    assert users.verify_password("changed", ann.password_hash)
    assert users.find_user(clean_db, "legacy").password_hash is None
    with pytest.raises(ValueError):
        users.create_user(clean_db, "Ann", "x")


def test_client_ip_trusts_proxy_headers_only_when_configured(make_client):
    from starlette.requests import Request

    from auth import client_ip
    headers = [(b"cf-connecting-ip", b"203.0.113.9"), (b"x-forwarded-for", b"198.51.100.1, 10.0.0.2")]
    req = Request({"type": "http", "headers": headers, "client": ("172.18.0.5", 4000)})
    assert client_ip(req, trust_proxy=True) == "203.0.113.9"
    assert client_ip(req, trust_proxy=False) == "172.18.0.5"
    xff = Request({"type": "http", "headers": headers[1:], "client": ("172.18.0.5", 4000)})
    assert client_ip(xff, trust_proxy=True) == "198.51.100.1"

    # Behind the tunnel everyone shares cloudflared's IP: one attacker must not lock out the others.
    client, _ = make_client(trust_proxy=True)
    for _ in range(LoginThrottle.FREE):
        client.post("/api/login", json={"name": "sam", "password": "x"}, headers={"CF-Connecting-IP": "203.0.113.9"})
    ok = client.post("/api/login", json={"name": "obi", "password": PASSWORDS["obi"]},
                     headers={"CF-Connecting-IP": "198.51.100.7"})
    assert ok.status_code == 200
