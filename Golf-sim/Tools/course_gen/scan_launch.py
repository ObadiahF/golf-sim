#!/usr/bin/env python3
"""The playability scan under its old name (it began as the tee-shot check): see scan_playability.py.

  python scan_launch.py <holes_root> [--json]
"""
from scan_playability import generator_version, green_profile, launch_profile, main, scan, scan_package  # noqa: F401

if __name__ == "__main__":
    main()
