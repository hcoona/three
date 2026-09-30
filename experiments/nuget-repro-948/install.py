# Assertions enforce experiment invariants; invoke Python without -O.
# Console output is retained evidence. Subprocess arguments are local recipes.
# ruff: noqa: S101, T201

"""Install a named official SDK in task-owned storage, verifying SHA-512."""

import argparse
import hashlib
import json
import ssl
import tarfile
import urllib.request
import zipfile
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument("--version", required=True)
p.add_argument("--rid", required=True, choices=["linux-x64", "win-x64"])
p.add_argument("--root", required=True, type=Path)
a = p.parse_args()
a.root.mkdir(parents=True, exist_ok=True)
context = ssl.create_default_context()
if Path("/etc/ssl/certs/ca-certificates.crt").is_file():
    context = ssl.create_default_context(
        cafile="/etc/ssl/certs/ca-certificates.crt"
    )
channel = ".".join(a.version.split(".")[:2])
url = f"https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/{channel}/releases.json"
metadata = urllib.request.urlopen(url, context=context, timeout=60).read()
(a.root / f"releases-{channel}.json").write_bytes(metadata)
sdks = [
    s
    for r in json.loads(metadata)["releases"]
    for s in r.get("sdks", [r["sdk"]])
]
sdk = next(s for s in sdks if s["version"] == a.version)
suffix = ".zip" if a.rid == "win-x64" else ".tar.gz"
artifact = next(
    f for f in sdk["files"] if f["rid"] == a.rid and f["name"].endswith(suffix)
)
archive = a.root / f"dotnet-sdk-{a.version}-{a.rid}{suffix}"
if not archive.exists():
    with (
        urllib.request.urlopen(  # noqa: S310 - official HTTPS metadata
            artifact["url"], context=context, timeout=120
        ) as response,
        archive.open("wb") as output,
    ):
        while chunk := response.read(1024 * 1024):
            output.write(chunk)
actual = hashlib.sha512(archive.read_bytes()).hexdigest()
assert actual == artifact["hash"], "Official SDK archive SHA-512 mismatch"
target = a.root / f"sdk-{a.version}"
target.mkdir(exist_ok=False)
if suffix == ".zip":
    with zipfile.ZipFile(archive) as z:
        z.extractall(target)  # noqa: S202 - hash-verified official SDK
else:
    with tarfile.open(archive) as t:
        t.extractall(target, filter="data")
(a.root / f"sdk-{a.version}-provenance.json").write_text(
    json.dumps(
        {
            "release_metadata": url,
            "version": a.version,
            "artifact": artifact,
            "verified_sha512": actual,
        },
        indent=2,
    )
    + "\n"
)
print(target)
