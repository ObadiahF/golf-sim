"""objects.bin: every tree, shrub and rock of a hole (contract: Docs/hole-format/README.md, section 5)."""
from __future__ import annotations

from pathlib import Path

import numpy as np

FILE_NAME = "objects.bin"
MAGIC = b"GOBJ"
VERSION = 1

# Append-only: never renumber or reuse a value.
KINDS = ("conifer", "deciduous", "palm", "cactus", "shrub", "boulder", "rock")
KIND_CODE = {k: i for i, k in enumerate(KINDS)}

HEADER = np.dtype([("magic", "S4"), ("version", "<u2"), ("record_size", "<u2"), ("count", "<u4"), ("reserved", "<u4")])
RECORD = np.dtype([("kind", "u1"), ("variant", "u1"), ("rotation", "u1"), ("reserved", "u1"),
                   ("x", "<u2"), ("y", "<u2"), ("height", "<u2"), ("radius", "<u2")])

# Decoded form used by producers and tests: meters, degrees, variant 0..1.
PLACED = np.dtype([("kind", "u1"), ("x", "f8"), ("y", "f8"), ("height", "f4"), ("radius", "f4"),
                   ("rotation", "f4"), ("variant", "f4")])


def empty() -> np.ndarray:
    return np.zeros(0, PLACED)


def encode(objects: np.ndarray, size: float) -> np.ndarray:
    """Quantise decoded objects into sorted records (kind, then y, then x), so equal holes give equal bytes."""
    rec = np.zeros(len(objects), RECORD)
    rec["kind"] = objects["kind"]
    rec["variant"] = np.clip(objects["variant"] * 256, 0, 255).astype(np.uint8)
    rec["rotation"] = (np.round(objects["rotation"] % 360 / 360 * 256) % 256).astype(np.uint8)
    rec["x"] = np.round(np.clip(objects["x"] / size, 0, 1) * 65535).astype(np.uint16)
    rec["y"] = np.round(np.clip(objects["y"] / size, 0, 1) * 65535).astype(np.uint16)
    rec["height"] = np.clip(np.round(objects["height"] * 100), 1, 65535).astype(np.uint16)
    rec["radius"] = np.clip(np.round(objects["radius"] * 100), 1, 65535).astype(np.uint16)
    return rec[np.lexsort((rec["x"], rec["y"], rec["kind"]))]


def decode(records: np.ndarray, size: float) -> np.ndarray:
    out = np.zeros(len(records), PLACED)
    out["kind"] = records["kind"]
    out["variant"] = records["variant"].astype(np.float32) / 256
    out["rotation"] = records["rotation"].astype(np.float32) * 360 / 256
    out["x"] = records["x"].astype(np.float64) / 65535 * size
    out["y"] = records["y"].astype(np.float64) / 65535 * size
    out["height"] = records["height"] / 100
    out["radius"] = records["radius"] / 100
    return out


def write(path: Path, objects: np.ndarray, size: float) -> int:
    """Writes objects.bin; returns the record count."""
    records = encode(objects, size)
    header = np.array([(MAGIC, VERSION, RECORD.itemsize, len(records), 0)], HEADER)
    with open(path, "wb") as f:
        f.write(header.tobytes())
        f.write(records.tobytes())
    return len(records)


def read_records(path: Path) -> np.ndarray:
    data = Path(path).read_bytes()
    if len(data) < HEADER.itemsize:
        raise ValueError(f"{path}: shorter than the header")
    header = np.frombuffer(data, HEADER, count=1)[0]
    if header["magic"] != MAGIC:
        raise ValueError(f"{path}: bad magic {header['magic']!r}")
    if header["version"] != VERSION:
        raise ValueError(f"{path}: objects.bin version {header['version']}, expected {VERSION}")
    size, count = int(header["record_size"]), int(header["count"])
    if size < RECORD.itemsize or len(data) != HEADER.itemsize + size * count:
        raise ValueError(f"{path}: {len(data)} bytes doesn't match {count} records of {size} bytes")
    raw = np.frombuffer(data, np.uint8, offset=HEADER.itemsize).reshape(count, size)[:, :RECORD.itemsize]
    return np.ascontiguousarray(raw).view(RECORD).reshape(count)


def read(path: Path, size: float) -> np.ndarray:
    return decode(read_records(path), size)
