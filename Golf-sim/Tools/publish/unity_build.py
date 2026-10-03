"""Builds a player through the open Unity Editor (the Unity CLI's run_script job), with the recipe in
Assets/GolfSim/Editor/BuildPlayers.cs. The Editor only works while it is frontmost, so it is brought forward."""
import json
import os
import subprocess
import time

UNITY_BIN = os.path.expanduser("~/.unity/bin")
BUILD_SCRIPT = "Assets/GolfSim/Editor/BuildPlayers.cs"
# Settings Unity rewrites during a build; put back afterwards unless they already had changes of someone's.
CHURN = ["Assets/Settings/URP", "ProjectSettings/ProjectSettings.asset", "ProjectSettings/UnityConnectSettings.asset"]


def _unity(project, *args):
    env = dict(os.environ, PATH=UNITY_BIN + os.pathsep + os.environ.get("PATH", ""))
    out = subprocess.run(["unity", "--format", "json", *args], cwd=project, env=env, capture_output=True, text=True)
    try:
        return json.loads(out.stdout)
    except ValueError:
        raise RuntimeError(f"unity {' '.join(args)}: {out.stdout or out.stderr}") from None


def _activate():
    subprocess.run(["osascript", "-e", 'tell application "Unity" to activate'], capture_output=True)


def _clean(project, path):
    return subprocess.run(["git", "diff", "--quiet", "--", path], cwd=project).returncode == 0


def _run_job(project, entry, args, wait_message=None):
    """Runs a static entry of BuildPlayers.cs as a detached Editor job and returns its result."""
    started = _unity(project, "command", "run_script", "--file", BUILD_SCRIPT, "--entry", entry,
                     "--args", json.dumps(args), "--detach")
    job = (started.get("data") or {}).get("jobId")
    if not job:
        raise RuntimeError(f"Couldn't start {entry}: {started}")
    if wait_message:
        print(f"{wait_message} (job {job})...", flush=True)
    began = time.time()
    try:
        while True:
            data = _unity(project, "job", "status", job).get("data") or {}
            state = data.get("state")
            if state == "queued":
                _activate()  # the job starts on the Editor's next tick
            if state in ("completed", "failed", "cancelled"):
                result = data.get("result") or {}
                if state != "completed" or not result.get("success"):
                    raise RuntimeError(f"{entry} failed: {data.get('error') or result.get('errorDetails') or result.get('error') or result}")
                return result.get("result")
            if wait_message:
                print(f"\r  {state}, {int(time.time() - began)} s", end="", flush=True)
            time.sleep(5 if wait_message else 2)
    finally:
        if wait_message:
            print()


def back_to_mac(project):
    """The Editor ends on macOS (it finishes a deferred platform switch after a Windows build first)."""
    for _ in range(30):
        if _run_job(project, "BuildPlayers.BackToMac", []) == "StandaloneOSX":
            return
        _activate()
        time.sleep(5)
    print("Warning: the Unity Editor is not back on macOS; File > Build Profiles to switch it", flush=True)


def build(project, platform, version, build_number):
    """Runs BuildPlayers.Build in the Editor and waits for it; returns its summary line."""
    status = _unity(project, "command", "editor_status", "--result-only")
    if status.get("playMode", "stopped") != "stopped":
        raise RuntimeError("The Unity Editor is in Play mode: stop it first")
    clean = [p for p in CHURN if _clean(project, p)]
    try:
        return _run_job(project, "BuildPlayers.Build", [platform, version, str(build_number)],
                        f"Building {platform} {version} in the Unity Editor")
    finally:
        back_to_mac(project)
        for path in clean:
            subprocess.run(["git", "checkout", "--", path], cwd=project, capture_output=True)
