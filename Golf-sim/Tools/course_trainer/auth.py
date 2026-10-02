"""Login: name + password -> a signed, HttpOnly session cookie. No sign-up (accounts come from manage.py / seed).

Cookie value: base64(json {"s": session id, "e": expiry}) + "." + HMAC-SHA256. The session id names a row in
`sessions` (users.py): logout deletes it, `passwd` deletes all of that user's, so both end the session server-side.
Anything malformed (bad signature, non-ASCII, not JSON) is simply "not logged in": 401, never 500.
Failed logins are throttled in memory per client IP and per name, independently. Behind a reverse proxy / Cloudflare Tunnel every
request arrives from the proxy, so with TRAINER_TRUST_PROXY the IP comes from CF-Connecting-IP / X-Forwarded-For.
Nothing depends on the request scheme (TLS ends at the proxy; the Secure flag is configured, not detected).
"""
from __future__ import annotations

import base64
import hashlib
import hmac
import json
import threading
import time
from dataclasses import dataclass
from typing import Callable

from fastapi import APIRouter, Depends, HTTPException, Request, Response
from pydantic import BaseModel, Field

from config import Settings
from inputs import Text
from users import User, create_session, end_session, find_user, mark_login, session_user, verify_password

COOKIE = "trainer_session"


class SessionSigner:
    def __init__(self, secret: str, days: int):
        self.key = hashlib.sha256(secret.encode()).digest()
        self.max_age = days * 86400

    def _sig(self, body: bytes) -> str:
        return base64.urlsafe_b64encode(hmac.new(self.key, body, hashlib.sha256).digest()).decode().rstrip("=")

    def make(self, session_id: str) -> str:
        body = base64.urlsafe_b64encode(json.dumps({"s": session_id, "e": int(time.time()) + self.max_age}).encode())
        return f"{body.decode()}.{self._sig(body)}"

    def read(self, token: str | None) -> str | None:
        """The session id if the signature is good and it has not expired, else None."""
        body, _, sig = (token or "").partition(".")
        if not sig or not (body + sig).isascii() or not hmac.compare_digest(sig, self._sig(body.encode())):
            return None
        try:
            data = json.loads(base64.urlsafe_b64decode(body))
        except ValueError:
            return None
        if not isinstance(data, dict) or not isinstance(data.get("s"), str) or not isinstance(data.get("e"), int):
            return None
        return data["s"] if data["e"] > time.time() else None


@dataclass
class _Failures:
    count: int = 0      # at time `last`; one failure is forgiven per whole DECAY seconds since
    last: float = 0.0


class LoginThrottle:
    """After FREE failures from one IP or for one name, each further try waits 2, 4, 8 ... s (max 15 min).
    The IP and name counters are independent and each decays by one failure every DECAY s (so FREE failures are
    forgotten after FORGET quiet seconds). A successful login clears only its name's counter: logging into your
    own account between guesses does not reset your IP's."""
    FREE, MAX_WAIT, FORGET = 5, 900.0, 900.0
    DECAY = FORGET / FREE

    def __init__(self):
        self._fails: dict[str, _Failures] = {}
        self._lock = threading.Lock()

    def retry_after(self, keys: list[str], now: float | None = None) -> float:
        now = time.time() if now is None else now
        with self._lock:
            waits = [self._wait(self._fails.get(k), now) for k in keys]
        return max(waits, default=0.0)

    def _decayed(self, f: _Failures | None, now: float) -> int:
        return 0 if f is None else max(0, f.count - int((now - f.last) / self.DECAY))

    def _wait(self, f: _Failures | None, now: float) -> float:
        if f is None or f.count < self.FREE:
            return 0.0
        return max(0.0, f.last + min(self.MAX_WAIT, 2.0 ** (f.count - self.FREE + 1)) - now)

    def failed(self, keys: list[str], now: float | None = None) -> None:
        now = time.time() if now is None else now
        with self._lock:
            for k in keys:
                self._fails[k] = _Failures(self._decayed(self._fails.get(k), now) + 1, now)
            if len(self._fails) > 10_000:  # bound memory under a spray of names
                self._fails = {k: v for k, v in self._fails.items() if self._decayed(v, now) > 0}

    def succeeded(self, key: str) -> None:
        with self._lock:
            self._fails.pop(key, None)


def client_ip(request: Request, trust_proxy: bool) -> str:
    """The visitor's IP: CF-Connecting-IP, else X-Forwarded-For's first hop (only when trusting a proxy, as anyone
    reaching the port directly could forge them), else the socket peer."""
    if trust_proxy:
        forwarded = request.headers.get("cf-connecting-ip") or request.headers.get("x-forwarded-for", "").split(",")[0]
        if forwarded.strip():
            return forwarded.strip()
    return request.client.host if request.client else "?"


class LoginRequest(BaseModel):
    name: Text = Field(min_length=1, max_length=100)
    password: Text = Field(min_length=1, max_length=200)


def auth_router(settings: Settings) -> tuple[APIRouter, Callable[[Request], User]]:
    """The /api/login, /api/logout, /api/me routes, and the `current_user` dependency for everything else."""
    signer = SessionSigner(settings.session_secret, settings.session_days)
    throttle = LoginThrottle()
    router = APIRouter(prefix="/api")
    dsn = settings.database_url

    def current_user(request: Request) -> User:
        sid = signer.read(request.cookies.get(COOKIE))
        user = session_user(dsn, sid) if sid else None
        if user is None:
            raise HTTPException(401, "Log in first")
        return user

    @router.post("/login")
    def login(req: LoginRequest, request: Request, response: Response):
        name_key = f"name:{req.name.strip().lower()}"
        keys = [f"ip:{client_ip(request, settings.trust_proxy)}", name_key]
        wait = throttle.retry_after(keys)
        if wait > 0:
            raise HTTPException(429, f"Too many failed logins: try again in {int(wait) + 1} s",
                                headers={"Retry-After": str(int(wait) + 1)})
        user = find_user(dsn, req.name.strip())
        if not verify_password(req.password, user.password_hash if user else None):
            throttle.failed(keys)
            raise HTTPException(401, "Wrong name or password")
        throttle.succeeded(name_key)
        mark_login(dsn, user.id)
        token = signer.make(create_session(dsn, user.id, signer.max_age))
        response.set_cookie(COOKIE, token, max_age=signer.max_age, httponly=True, samesite="lax",
                            secure=settings.cookie_secure, path="/")
        return {"name": user.name}

    @router.post("/logout")
    def logout(request: Request, response: Response):
        sid = signer.read(request.cookies.get(COOKIE))
        if sid:
            end_session(dsn, sid)
        response.delete_cookie(COOKIE, path="/", httponly=True, samesite="lax", secure=settings.cookie_secure)
        return {"ok": True}

    @router.get("/me")
    def me(user: User = Depends(current_user)):
        return {"name": user.name}

    return router, current_user
