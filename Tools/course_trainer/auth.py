"""Login: name + password -> a signed, HttpOnly session cookie. No sign-up (accounts come from manage.py / seed).

Cookie value: base64(json {"u": user id, "e": expiry, "p": password fingerprint}) + "." + HMAC-SHA256. The
fingerprint ties a session to the password it was made with, so `passwd` logs that user out everywhere.
Failed logins are throttled in memory per client IP and per name. Behind a reverse proxy / Cloudflare Tunnel every
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
from users import User, find_user, mark_login, verify_password

COOKIE = "trainer_session"


class SessionSigner:
    def __init__(self, secret: str, days: int):
        self.key = hashlib.sha256(secret.encode()).digest()
        self.max_age = days * 86400

    def _sig(self, body: bytes) -> str:
        return base64.urlsafe_b64encode(hmac.new(self.key, body, hashlib.sha256).digest()).decode().rstrip("=")

    def fingerprint(self, user: User) -> str:
        return self._sig((user.password_hash or "").encode())[:12]

    def make(self, user: User) -> str:
        body = base64.urlsafe_b64encode(json.dumps(
            {"u": user.id, "e": int(time.time()) + self.max_age, "p": self.fingerprint(user)}).encode())
        return f"{body.decode()}.{self._sig(body)}"

    def read(self, token: str | None) -> dict | None:
        """The payload if the signature is good and it has not expired, else None."""
        body, _, sig = (token or "").partition(".")
        if not sig or not hmac.compare_digest(sig, self._sig(body.encode())):
            return None
        try:
            data = json.loads(base64.urlsafe_b64decode(body))
        except ValueError:
            return None
        return data if data.get("e", 0) > time.time() else None


@dataclass
class _Failures:
    count: int = 0
    last: float = 0.0


class LoginThrottle:
    """After FREE failures from one IP or for one name, each further try waits 2, 4, 8 ... s (max 15 min).
    Entries are forgotten after 15 quiet minutes, or on a successful login."""
    FREE, MAX_WAIT, FORGET = 5, 900.0, 900.0

    def __init__(self):
        self._fails: dict[str, _Failures] = {}
        self._lock = threading.Lock()

    def retry_after(self, keys: list[str], now: float | None = None) -> float:
        now = now or time.time()
        with self._lock:
            waits = [self._wait(self._fails.get(k), now) for k in keys]
        return max(waits, default=0.0)

    def _wait(self, f: _Failures | None, now: float) -> float:
        if f is None or f.count < self.FREE or now - f.last > self.FORGET:
            return 0.0
        return max(0.0, f.last + min(self.MAX_WAIT, 2.0 ** (f.count - self.FREE + 1)) - now)

    def failed(self, keys: list[str], now: float | None = None) -> None:
        now = now or time.time()
        with self._lock:
            for k in keys:
                f = self._fails.get(k)
                if f is None or now - f.last > self.FORGET:
                    f = self._fails[k] = _Failures()
                f.count, f.last = f.count + 1, now
            if len(self._fails) > 10_000:  # bound memory under a spray of names
                self._fails = {k: v for k, v in self._fails.items() if now - v.last <= self.FORGET}

    def succeeded(self, keys: list[str]) -> None:
        with self._lock:
            for k in keys:
                self._fails.pop(k, None)


def client_ip(request: Request, trust_proxy: bool) -> str:
    """The visitor's IP: CF-Connecting-IP, else X-Forwarded-For's first hop (only when trusting a proxy, as anyone
    reaching the port directly could forge them), else the socket peer."""
    if trust_proxy:
        forwarded = request.headers.get("cf-connecting-ip") or request.headers.get("x-forwarded-for", "").split(",")[0]
        if forwarded.strip():
            return forwarded.strip()
    return request.client.host if request.client else "?"


class LoginRequest(BaseModel):
    name: str = Field(min_length=1, max_length=100)
    password: str = Field(min_length=1, max_length=200)


def auth_router(settings: Settings) -> tuple[APIRouter, Callable[[Request], User]]:
    """The /api/login, /api/logout, /api/me routes, and the `current_user` dependency for everything else."""
    signer = SessionSigner(settings.session_secret, settings.session_days)
    throttle = LoginThrottle()
    router = APIRouter(prefix="/api")
    dsn = settings.database_url

    def current_user(request: Request) -> User:
        data = signer.read(request.cookies.get(COOKIE))
        user = find_user(dsn, user_id=data["u"]) if data else None
        if user is None or not hmac.compare_digest(data.get("p", ""), signer.fingerprint(user)):
            raise HTTPException(401, "Log in first")
        return user

    @router.post("/login")
    def login(req: LoginRequest, request: Request, response: Response):
        keys = [f"ip:{client_ip(request, settings.trust_proxy)}", f"name:{req.name.strip().lower()}"]
        wait = throttle.retry_after(keys)
        if wait > 0:
            raise HTTPException(429, f"Too many failed logins: try again in {int(wait) + 1} s",
                                headers={"Retry-After": str(int(wait) + 1)})
        user = find_user(dsn, req.name.strip())
        if not verify_password(req.password, user.password_hash if user else None):
            throttle.failed(keys)
            raise HTTPException(401, "Wrong name or password")
        throttle.succeeded(keys)
        mark_login(dsn, user.id)
        response.set_cookie(COOKIE, signer.make(user), max_age=signer.max_age, httponly=True, samesite="lax",
                            secure=settings.cookie_secure, path="/")
        return {"name": user.name}

    @router.post("/logout")
    def logout(response: Response):
        response.delete_cookie(COOKIE, path="/", httponly=True, samesite="lax", secure=settings.cookie_secure)
        return {"ok": True}

    @router.get("/me")
    def me(user: User = Depends(current_user)):
        return {"name": user.name}

    return router, current_user
