"""Request input hygiene: text fields Postgres and the filesystem can take, and error responses that never 500.

- `Text`: a str field that rejects NUL (Postgres text cannot hold it) and lone surrogates (not valid UTF-8): 422.
- Every JSON response, errors included, is rendered ASCII-only (`\\uXXXX` escapes), so echoing a bad value back in
  an error message can never raise UnicodeEncodeError. 422 bodies leave out the offending `input` (it may be a
  password).
"""
from __future__ import annotations

import json
from typing import Annotated, Any

from fastapi import FastAPI, Request
from fastapi.encoders import jsonable_encoder
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from pydantic import AfterValidator, Field
from starlette.exceptions import HTTPException

MAX_ID = 200  # hole ids are ~30 characters; anything near the 255-byte filename limit is not one


def clean_text(value: str) -> str:
    if "\x00" in value:
        raise ValueError("must not contain NUL characters")
    try:
        value.encode("utf-8")
    except UnicodeEncodeError:
        raise ValueError("must be valid Unicode (no lone surrogates)") from None
    return value


Text = Annotated[str, AfterValidator(clean_text)]
HoleId = Annotated[Text, Field(min_length=1, max_length=MAX_ID)]
FiniteFloat = Annotated[float, Field(allow_inf_nan=False)]


class AsciiJSONResponse(JSONResponse):
    def render(self, content: Any) -> bytes:
        return json.dumps(content, ensure_ascii=True, allow_nan=False, separators=(",", ":")).encode("ascii")


def install(app: FastAPI) -> None:
    """ASCII-safe handlers for HTTP errors (Starlette's too: unknown routes) and validation errors (the defaults
    encode UTF-8 and can 500)."""

    @app.exception_handler(HTTPException)
    async def http_error(_request: Request, exc: HTTPException):
        return AsciiJSONResponse({"detail": exc.detail}, status_code=exc.status_code, headers=exc.headers)

    @app.exception_handler(RequestValidationError)
    async def validation_error(_request: Request, exc: RequestValidationError):
        errors = [{k: v for k, v in e.items() if k != "input"} for e in exc.errors()]
        return AsciiJSONResponse({"detail": jsonable_encoder(errors)}, status_code=422)
