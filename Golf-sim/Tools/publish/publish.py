#!/usr/bin/env python3
"""Builds the game and publishes it as a self-update release on the game server (Game-server/docs/UPDATES.md).

    Tools/publish/publish.sh windows "Faster putting green"     # build, upload what changed, release
    Tools/publish/publish.sh macos --server http://localhost:8080
    Tools/publish/publish.sh windows --skip-build              # publish the build already in Builds/

The admin token is GOLF_UPDATE_TOKEN, else Tools/publish/update_token.txt (gitignored); the server is --server,
else GOLF_UPDATE_SERVER, else the hosted one. Only files the server doesn't have yet are uploaded, in parts of at most
50 MB. Windows also refreshes Builds/GolfSim-Windows.zip for a manual install."""
import argparse
import datetime
import json
import os
import subprocess
import sys
import zipfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from update_api import SKIPPED_FOLDERS, ApiError, UpdateApi, manifest, mb  # noqa: E402
import unity_build  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
DEFAULT_SERVER = "https://golf-server.obadiahfusco.xyz"
PLATFORMS = {
    # name: (update channel, build folder, what the manifest describes, record x bits)
    "windows": ("windows-x64", "Builds/Windows", "Builds/Windows", False),
    "macos": ("macos", "Builds/macOS", "Builds/macOS/GolfSim.app", True),
}


def token():
    value = os.environ.get("GOLF_UPDATE_TOKEN", "").strip()
    path = os.path.join(HERE, "update_token.txt")
    if not value and os.path.exists(path):
        value = open(path).read().strip()
    if not value:
        sys.exit(f"No admin token: set GOLF_UPDATE_TOKEN or put it in {path}")
    return value


def git(*args):
    return subprocess.run(["git", *args], cwd=PROJECT, capture_output=True, text=True).stdout.strip()


def new_version():
    """2026.10.02-1754-2135252 (UTC date and time, git commit; -dirty with uncommitted changes) and its build number."""
    now = datetime.datetime.now(datetime.timezone.utc)
    sha = git("rev-parse", "--short", "HEAD") or "nogit"
    dirty = "-dirty" if git("status", "--porcelain", "--", ".") else ""
    return f"{now:%Y.%m.%d-%H%M}-{sha}{dirty}", int(f"{now:%Y%m%d%H%M%S}")


def zip_windows(folder, target):
    """The manual-install zip: the build in a GolfSim/ folder, without Unity's backup folders."""
    temp = target + ".tmp"
    with zipfile.ZipFile(temp, "w", zipfile.ZIP_DEFLATED) as z:
        for root, dirs, names in os.walk(folder):
            dirs[:] = sorted(d for d in dirs if not d.endswith(SKIPPED_FOLDERS))
            for name in sorted(names):
                if name != ".DS_Store":
                    full = os.path.join(root, name)
                    z.write(full, os.path.join("GolfSim", os.path.relpath(full, folder)))
    os.replace(temp, target)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("platform", choices=sorted(PLATFORMS))
    parser.add_argument("note", nargs="?", default="", help="shown to the players with the update")
    parser.add_argument("--server", default=os.environ.get("GOLF_UPDATE_SERVER", DEFAULT_SERVER))
    parser.add_argument("--skip-build", action="store_true", help="publish the build already in Builds/")
    parser.add_argument("--part-mb", type=int, default=50, help="upload part size (Cloudflare allows < 100 MB)")
    args = parser.parse_args()
    channel, folder, root, executables = PLATFORMS[args.platform]
    api = UpdateApi(args.server, token(), args.part_mb * 1024 * 1024)

    try:
        api.missing([])  # the token and the server work, before a long build
    except ApiError as e:
        sys.exit(f"Can't publish to {args.server}: {e}")

    if not args.skip_build:
        version, number = new_version()
        print(unity_build.build(PROJECT, args.platform, version, number))
    with open(os.path.join(PROJECT, folder + ".build.json")) as f:
        info = json.load(f)
    if info["platform"] != channel:
        sys.exit(f"{folder}.build.json is for {info['platform']}, not {channel}")

    print(f"Hashing {root}...", flush=True)
    files = manifest(os.path.join(PROJECT, root), executables)
    total = sum(f["size"] for f in files)
    by_sha = {f["sha256"]: f for f in files}
    missing = api.missing(sorted(by_sha))
    to_send = sum(by_sha[s]["size"] for s in missing)
    print(f"{len(files)} files, {mb(total)}; the server needs {len(missing)} of them ({mb(to_send)})")

    sent = 0
    for i, sha in enumerate(missing, 1):
        f = by_sha[sha]
        def progress(n, base=sent):
            print(f"\r  [{i}/{len(missing)}] {mb(base + n)} of {mb(to_send)}  {f['path'][-60:]:<60}", end="", flush=True)
        api.upload(os.path.join(PROJECT, root, f["path"]), sha, f["size"], progress)
        sent += f["size"]
    if missing:
        print()

    release = api.publish({"platform": channel, "version": info["version"], "build": info["build"],
                           "note": args.note, "files": files})
    print(f"Published {release['platform']} {release['version']} (build {release['build']}): "
          f"{release['fileCount']} files, {mb(release['totalSize'])} on {args.server}")

    if args.platform == "windows":
        target = os.path.join(PROJECT, "Builds", "GolfSim-Windows.zip")
        zip_windows(os.path.join(PROJECT, folder), target)
        print(f"Manual install zip: {target} ({mb(os.path.getsize(target))})")


if __name__ == "__main__":
    try:
        main()
    except (ApiError, RuntimeError) as e:
        sys.exit(f"\n{e}")
    except KeyboardInterrupt:
        sys.exit("\nStopped (files already uploaded are skipped next time)")
