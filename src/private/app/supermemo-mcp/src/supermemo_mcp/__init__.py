"""This module provides functions to interact with SuperMemo processes & UI."""

import psutil


def find_sm_processes() -> psutil.Process | None:
    """Find the SuperMemo process (sm18.exe or sm19.exe)."""
    processes: list[psutil.Process] = []

    for proc in psutil.process_iter(["pid", "name", "exe"]):
        try:
            if proc.info["name"] and proc.info["name"].lower() in [
                "sm18.exe",
                "sm19.exe",
            ]:
                processes.append(proc)
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            continue

    if len(processes) > 1:
        raise RuntimeError("Found multiple SuperMemo processes.")

    return processes[0] if processes else None
