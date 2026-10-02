"""Fake sim / remote for the golf game server WebSocket (/ws).

Prints every message it receives. Messages to send come from --send (scripted) or stdin (interactive),
either as raw JSON or as shorthands:

  remote: nav up|down|left|right|select|back   club 7I   aim -2   aimreset   mulligan   skip
          shot <speed m/s> <launch> <azimuth> <back rpm> <side rpm> [club]
  sim:    state <screen> [player hole par strokes]   turn <player> <hole> [strokes]
          result <player> <carry yd> <total yd> <lie> [holed] [strokes]
          score <gameId> <player> <hole> <par> <strokes>
  any:    ping
"""
import argparse
import asyncio
import json
import os
import sys
import urllib.parse

import websockets


def num(s):
    return float(s) if "." in s else int(s)


def shorthand(line):
    """Turns a shorthand command into a message dict (raw JSON passes through)."""
    if line.startswith("{"):
        return json.loads(line)
    w = line.split()
    cmd, a = w[0].lower(), w[1:]
    if cmd == "nav":
        return {"type": "nav", "key": a[0]}
    if cmd == "club":
        return {"type": "club", "club": a[0]}
    if cmd == "aim":
        return {"type": "aim", "delta": num(a[0])}
    if cmd in ("aimreset", "mulligan", "skip", "ping"):
        return {"type": {"aimreset": "aimReset"}.get(cmd, cmd)}
    if cmd == "shot":
        msg = dict(zip(["speed", "launch", "azimuth", "back", "side"], map(num, a[:5])), type="shot")
        return {**msg, "club": a[5]} if len(a) > 5 else msg
    if cmd == "state":
        msg = {"type": "state", "screen": a[0]}
        if len(a) >= 4:
            msg.update(currentPlayer=a[1], hole=int(a[2]), par=int(a[3]))
        if len(a) >= 5:
            msg["strokes"] = int(a[4])
        return msg
    if cmd == "turn":
        msg = {"type": "turn", "player": a[0], "hole": int(a[1])}
        return {**msg, "strokes": int(a[2])} if len(a) > 2 else msg
    if cmd == "result":
        return {"type": "shotResult", "player": a[0], "carry": num(a[1]), "total": num(a[2]), "lie": a[3],
                "holed": len(a) > 4 and a[4].lower() in ("holed", "true", "1"),
                "strokes": int(a[5]) if len(a) > 5 else 1}
    if cmd == "score":
        return {"type": "holeScore", "gameId": int(a[0]), "player": a[1], "hole": int(a[2]),
                "par": int(a[3]), "strokes": int(a[4])}
    raise ValueError(f"unknown command '{cmd}'")


async def send(ws, line):
    try:
        msg = shorthand(line.strip())
    except (ValueError, IndexError, json.JSONDecodeError) as e:
        print(f"!! {e}", flush=True)
        return
    text = json.dumps(msg)
    print(f">> {text}", flush=True)
    await ws.send(text)


async def receive(ws):
    async for text in ws:
        print(f"<< {text}", flush=True)


async def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--role", choices=["sim", "remote"], default="remote")
    p.add_argument("--name", default=None, help="device name (default: fake-<role>)")
    p.add_argument("--url", default=os.environ.get("WS_URL", "ws://localhost:8080/ws"))
    p.add_argument("--token", default=os.environ.get("GOLF_API_TOKEN", "golf-sim-dev-token"))
    p.add_argument("--send", action="append", default=[], help="message to send (repeatable), then exit after --wait")
    p.add_argument("--delay", type=float, default=0.3, help="seconds between --send messages")
    p.add_argument("--wait", type=float, default=None, help="listen this many seconds, then exit (no stdin)")
    args = p.parse_args()

    query = urllib.parse.urlencode({"token": args.token, "role": args.role, "name": args.name or f"fake-{args.role}"})
    async with websockets.connect(f"{args.url}?{query}") as ws:
        print(f"connected to {args.url} as {args.role}", flush=True)
        reader = asyncio.create_task(receive(ws))
        await asyncio.sleep(args.delay)
        for line in args.send:
            await send(ws, line)
            await asyncio.sleep(args.delay)
        if args.send or args.wait is not None:
            await asyncio.sleep(args.wait or 1)
        else:
            loop = asyncio.get_running_loop()
            while (line := await loop.run_in_executor(None, sys.stdin.readline)):
                if line.strip():
                    await send(ws, line)
        reader.cancel()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
    except (OSError, websockets.exceptions.WebSocketException) as e:
        sys.exit(f"connection failed: {e}")
