# Golf Sim Game Server: Self-updates

The sim updates itself from the game server, like a game launcher: the Mac publishes a build as a **release**, the
laptop's main menu sees that a newer one exists, downloads only the files that changed, and restarts as the new
version. Code: server `update/` (`UpdateService`, `BlobStore`, `ManifestRules`), `api/UpdateController.java`,
`api/UpdatePublishController.java`; sim `Golf-sim/Assets/GolfSim/Net/Runtime/Update/` and `Game/Runtime/UpdateFlow.cs`;
publish tool `Golf-sim/Tools/publish/`; build recipe `Golf-sim/Assets/GolfSim/Editor/BuildPlayers.cs`.

## Model

- **Platforms** (update channels): `windows-x64` (the gaming laptop) and `macos` (for testing on the Mac). Any
  `[a-z0-9][a-z0-9-]{0,31}` name works.
- **Release**: one build of the game for one platform: `version` (e.g. `2026.10.02-1754-2135252`: UTC date and time,
  git commit, `-dirty` with uncommitted changes), `build` (monotonic number, UTC `yyyyMMddHHmmss`), an optional
  `note`, and a **manifest**: every file of the build as `{path, size, sha256, executable}`. Windows paths are
  relative to the folder with `GolfSim.exe`; macOS paths to the `GolfSim.app` bundle (`Contents/...`).
- **Latest** is the release with the highest `build`; a new release must have a higher build than the latest and a
  version not used before (409 otherwise). To roll back, publish the old commit again (it gets a new build number).
- **Blobs**: file contents, stored once by sha256 under `updates.dir` (`/data/updates/blobs`, docker volume
  `updates`). A file that doesn't change between releases is never uploaded or downloaded again.
- **Retention**: the newest 5 releases per platform (`updates.keep-releases`); after each publish, older releases are
  deleted and blobs no release refers to are garbage-collected (only once unused for 1 h, `updates.gc-grace`, so a
  publish in progress never loses its uploads). Unfinished uploads are deleted after 24 h.

## Security

- **Reading** (`/api/updates/latest`, `/releases`, `/blobs/...`) takes the shared `GOLF_API_TOKEN`, like the rest of
  `/api`.
- **Publishing** (everything under `/api/updates/publish/`) takes a **separate admin token**, `GOLF_UPDATE_TOKEN`
  (`Authorization: Bearer <token>`). The shared token ships inside the phone app and the sim, so it must never be able
  to push code to the laptop: it gets 401 there, and the admin token gets 401 everywhere else. Without
  `GOLF_UPDATE_TOKEN` publishing is switched off (404). The check is in `auth/ApiTokenFilter.java`, on the normalised
  path, so path tricks (`/api/updates/%70ublish`, `;`, `..`) can't get around it.
- **Manifests can't escape the game folder.** The server refuses (400, `fieldErrors["files[i].path"]`) absolute paths,
  drive letters, `\`, `.`/`..`/empty segments, `: * ? " < > |` and control characters, segments ending in a dot or
  space, reserved Windows names (`CON`, `NUL`, `COM1`...), paths that differ only in case and a path that is also a
  folder of another. The sim checks every path again, and the updater scripts once more, before touching a file.
- **Integrity**: the server verifies size and sha256 of every upload before storing it; the sim verifies the sha256 of
  every download before using it. The updater only deletes files inside folders the release owns (`GolfSim_Data/`,
  `MonoBleedingEdge/`, `D3D12/`, the bundle's `Contents/`), never loose files next to `GolfSim.exe` and never the
  player data (`%USERPROFILE%\AppData\LocalLow\DefaultCompany\Golf-sim`).

## REST

Errors use the usual `ErrorResponse` body (`PROTOCOL.md`). sha256s are 64 lowercase hex characters.

#### `GET /api/updates/latest?platform=windows-x64` (shared token)

`200` with the latest release, or `204 No Content` when the platform has none.

```json
{
  "platform": "windows-x64", "version": "2026.10.02-1754-2135252", "build": 20261002175412,
  "note": "Faster putting green", "createdAt": "2026-10-02T17:56:03Z", "totalSize": 578813952, "fileCount": 216,
  "files": [
    { "path": "GolfSim.exe", "size": 667648, "sha256": "9f2c...", "executable": false },
    { "path": "GolfSim_Data/Managed/Assembly-CSharp.dll", "size": 1843200, "sha256": "41ab...", "executable": false }
  ]
}
```

#### `GET /api/updates/releases?platform=windows-x64` (shared token)

The kept releases, newest first, without `files`.

#### `GET /api/updates/blobs/{sha256}` (shared token)

The file, `application/octet-stream`, with `Content-Length`, `ETag: "<sha256>"`, `Accept-Ranges: bytes` and
`Cache-Control: private, max-age=31536000, immutable`. **Range** requests resume a download:
`Range: bytes=1048576-` answers `206` with `Content-Range: bytes 1048576-9999999/10000000`; a range past the end
answers `416` with `Content-Range: bytes */10000000`. `404` for an unknown blob.

#### `POST /api/updates/publish/blobs/missing` (admin token)

`{"sha256": ["...", "..."]}` (up to 20 000) → `{"missing": ["..."]}`: the ones the server doesn't have, in order,
without duplicates. A blob reported as present is protected from garbage collection for the grace period.

#### `PUT /api/updates/publish/blobs/{sha256}/parts/{n}` (admin token)

One part (`n` from 0) of a file as the raw request body (any content type). At most 50 MiB
(`updates.max-part-bytes`; Cloudflare refuses request bodies over 100 MB), else `413`. Sending a part again replaces
it. Streamed to disk, never held in memory. → `{"sha256": "...", "part": 0, "size": 52428800}`.

#### `POST /api/updates/publish/blobs/{sha256}/complete` (admin token)

`{"size": 123456789, "parts": 3}`: joins parts 0..2, checks the size and the sha256 and stores the blob
(`200 {"sha256", "size"}`). `400` when a part is missing ("Part 1 has not been uploaded"), or when the size or the
sha256 doesn't match: the parts are then deleted, upload the file again. Completing a stored blob is a no-op.
Files may be up to 4 GiB (`updates.max-blob-bytes`).

#### `POST /api/updates/publish/releases` (admin token)

```json
{ "platform": "windows-x64", "version": "2026.10.02-1754-2135252", "build": 20261002175412,
  "note": "Faster putting green", "files": [ { "path": "GolfSim.exe", "size": 667648, "sha256": "9f2c...", "executable": false } ] }
```

`201` with the release (as `latest`). `400` for a bad manifest, or a file that isn't uploaded
(`fieldErrors["files[3].sha256"]: "not uploaded"`) or whose upload has another size. `409` when `build` isn't higher
than the latest's or `version` exists. Up to 20 000 files, `note` up to 500 characters.

## On the laptop (the sim)

1. Every player build embeds its version: `BuildPlayers.cs` writes `Resources/BuildInfo.json`
   (`{version, build, platform}`) just before the build and deletes it after. The Editor and older builds have none
   and never update.
2. When the main menu opens (at startup and on every return from a round, at most once a minute) the sim asks
   `GET /api/updates/latest` in the background. No server, no release or nothing newer: nothing happens. A newer
   `build`: the installed files are hashed on a worker thread (only files whose size matches; hashes are cached by
   size and modification time in `<persistentDataPath>/updates/hashes.json`, so later checks take milliseconds), and
   an **Update available** card joins the menu row (keyboard, gamepad and the phone's D-pad, like every card) with the
   new version, the note and the download size. The top bar shows the running version.
3. **Play** on the card (never during a round: the menu only exists between rounds) downloads each blob whose sha256
   differs into `<persistentDataPath>/updates/blobs/` with a progress overlay (MB and %; the phones see `loading`).
   Downloads resume with `Range` after a failure, a stall (30 s) or Back (cancel); every file's sha256 is verified.
4. **Apply**: a running exe can't be overwritten, so the sim writes `updates/apply/plan.txt` and the updater script
   (`Net/Resources/Updater/ApplyWindows.txt` as `apply.ps1`, run by Windows PowerShell 5.1 with
   `-ExecutionPolicy Bypass`; `ApplyMac.txt` as `apply.sh` on macOS), starts it and quits. The script waits for the
   game (and Unity's crash handler) to exit, backs up every file it replaces or deletes, copies the staged files in
   (retrying locked files for ~10 s), deletes the files the release no longer has, and starts the game again with the
   same command line. On any failure it restores the backup, so the old version still runs. It logs to
   `updates/update.log` and leaves `ok`/`failed` in `updates/result.txt`, which the next start shows as a toast
   ("Updated to ..." / "The update failed ...").

Paths: Windows `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Golf-sim\updates\`, macOS
`~/Library/Application Support/<bundle id>/updates/` (today `com.Unity-Technologies.com.unity.template.hdrp-blank`). Windows paths must stay under 260 characters
(Windows PowerShell 5.1 has no long-path support): a normal install location is far from that.

Point a build at another server without rebuilding: `GolfSim.exe -server-url http://192.168.1.20:8080` (macOS:
`open GolfSim.app --args -server-url http://localhost:8080`). The relaunch after an update keeps the argument.

## Publishing (on the Mac)

```bash
cd Golf-sim
Tools/publish/publish.sh windows "Faster putting green"   # build, upload what changed, release, refresh the zip
Tools/publish/publish.sh macos --server http://localhost:8080
Tools/publish/publish.sh windows --skip-build             # publish what is already in Builds/Windows
```

The admin token comes from `GOLF_UPDATE_TOKEN` or `Golf-sim/Tools/publish/update_token.txt` (gitignored), the
server from `--server`, `GOLF_UPDATE_SERVER` or `https://golf-server.obadiahfusco.xyz`. The tool checks the token
first, builds through the open Unity Editor (`unity command run_script ... BuildPlayers.Build`, bringing Unity to the
front; the Editor must not be in Play mode) and puts back the settings Unity rewrites during a build, hashes the
build (skipping `*_BackUpThisFolder_ButDontShipItWithYourGame`, `*_BurstDebugInformation_DoNotShip` and
`.DS_Store`), asks which blobs are missing, uploads those in 50 MB parts with progress, and creates the release.
`windows` also writes `Builds/GolfSim-Windows.zip` for a manual install. Python 3, standard library only.

Server setup: add `GOLF_UPDATE_TOKEN=<openssl rand -hex 32>` to the server's `.env` (never the same as
`GOLF_API_TOKEN`) and redeploy; compose mounts the `updates` volume at `/data/updates`.
