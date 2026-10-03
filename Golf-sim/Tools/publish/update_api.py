"""The game server's update API, publisher side (Game-server/docs/UPDATES.md): which blobs are missing, chunked
uploads (each request under Cloudflare's 100 MB body limit) and creating a release. Standard library only."""
import hashlib
import json
import os
import sys
import time
import urllib.error
import urllib.request

PUBLISH = "/api/updates/publish"


class ApiError(Exception):
    pass


class UpdateApi:
    def __init__(self, server, token, part_bytes=50 * 1024 * 1024, timeout=600):
        self.server = server.rstrip("/")
        self.token = token
        self.part_bytes = part_bytes
        self.timeout = timeout

    def _request(self, method, path, body=None, content_type="application/json"):
        data = json.dumps(body).encode() if content_type == "application/json" and body is not None else body
        request = urllib.request.Request(self.server + path, data=data, method=method)
        request.add_header("Authorization", "Bearer " + self.token)
        if data is not None:
            request.add_header("Content-Type", content_type)
        try:
            with urllib.request.urlopen(request, timeout=self.timeout) as response:
                text = response.read().decode()
                return json.loads(text) if text else None
        except urllib.error.HTTPError as e:
            detail = e.read().decode(errors="replace")
            try:
                parsed = json.loads(detail)
                detail = parsed.get("message", detail) + (f" {parsed['fieldErrors']}" if parsed.get("fieldErrors") else "")
            except ValueError:
                pass
            raise ApiError(f"{method} {path}: HTTP {e.code}: {detail}") from None
        except urllib.error.URLError as e:
            raise ApiError(f"{method} {path}: {e.reason}") from None

    def missing(self, shas):
        """The sha256s the server doesn't have yet."""
        missing = []
        for start in range(0, len(shas), 10000):
            missing += self._request("POST", PUBLISH + "/blobs/missing", {"sha256": shas[start:start + 10000]})["missing"]
        return missing

    def upload(self, path, sha, size, progress):
        """Uploads one file in parts, retrying each part a few times; progress(bytes sent of this file)."""
        parts = max(1, -(-size // self.part_bytes))
        with open(path, "rb") as f:
            for part in range(parts):
                chunk = f.read(self.part_bytes)
                for attempt in range(1, 5):
                    try:
                        self._request("PUT", f"{PUBLISH}/blobs/{sha}/parts/{part}", chunk, "application/octet-stream")
                        break
                    except ApiError as e:
                        if attempt == 4:
                            raise
                        print(f"\n  part {part} failed ({e}); retrying", file=sys.stderr)
                        time.sleep(2 * attempt)
                progress(min(size, (part + 1) * self.part_bytes))
        self._request("POST", f"{PUBLISH}/blobs/{sha}/complete", {"size": size, "parts": parts})

    def publish(self, release):
        return self._request("POST", PUBLISH + "/releases", release)


SKIPPED_FOLDERS = ("_BackUpThisFolder_ButDontShipItWithYourGame", "_BurstDebugInformation_DoNotShip")


def manifest(root, executables):
    """Every shipped file under root: {path, size, sha256, executable}, paths with / and sorted. Unity's backup and
    Burst debug folders and .DS_Store files are not shipped. executables: record the x bit (macOS)."""
    files = []
    for folder, dirs, names in os.walk(root):
        dirs[:] = sorted(d for d in dirs if not d.endswith(SKIPPED_FOLDERS))
        for name in sorted(names):
            if name == ".DS_Store":
                continue
            full = os.path.join(folder, name)
            relative = os.path.relpath(full, root).replace(os.sep, "/")
            digest = hashlib.sha256()
            with open(full, "rb") as f:
                for block in iter(lambda: f.read(1 << 20), b""):
                    digest.update(block)
            files.append({"path": relative, "size": os.path.getsize(full), "sha256": digest.hexdigest(),
                          "executable": executables and os.access(full, os.X_OK)})
    return sorted(files, key=lambda f: f["path"])


def mb(n):
    return f"{n / 1048576:.1f} MB"
