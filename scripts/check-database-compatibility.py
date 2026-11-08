"""Check a published Windows single-file CLI using isolated SQLite snapshots.

Usage: python scripts/check-database-compatibility.py <exe> <min> <max>
Example: python scripts/check-database-compatibility.py skills/rimsearcher/bin/rimsearcher.exe 3.1.5 3.3.0
"""

from contextlib import closing
import hashlib
import json
from pathlib import Path
import shutil
import sqlite3
import subprocess
import sys
import tempfile


def encode(text):
    major, minor, patch = map(int, text.split("."))
    return major * 10000 + minor * 100 + patch


source = Path(sys.argv[1]).resolve()
lower, upper = map(encode, sys.argv[2:4])
assert 0 < lower <= upper

with tempfile.TemporaryDirectory(prefix="rimsearcher-compatibility-") as directory:
    root = Path(directory)
    executable = root / "rimsearcher.exe"
    shutil.copy2(source, executable)
    database = root / "defs.db"
    version = subprocess.run([str(executable), "--version"], capture_output=True, text=True, check=True).stdout.strip()
    accepted = {lower, upper, (lower + upper) // 2}
    rejected = {0, lower - 1, upper + 1}
    for marker in sorted(accepted | rejected):
        database.unlink(missing_ok=True)
        with closing(sqlite3.connect(database)) as connection:
            connection.execute("CREATE TABLE defs (def_type TEXT NOT NULL)")
            connection.executemany("INSERT INTO defs VALUES (?)", [("ThingDef",), ("JobDef",)])
            connection.execute(f"PRAGMA user_version = {marker}")
            connection.commit()
        before = hashlib.sha256(database.read_bytes()).digest()
        result = subprocess.run([str(executable), "types"], cwd=root, capture_output=True, text=True, encoding="utf-8", timeout=30)
        if marker in accepted:
            assert result.returncode == 0, result.stderr
            counts = sorted((row["def_type"], row["count"]) for row in json.loads(result.stdout))
            assert counts == [("JobDef", 1), ("ThingDef", 1)], counts
            assert result.stderr == "", result.stderr
        else:
            assert result.returncode == 1, (marker, result.returncode, result.stderr)
            assert result.stdout == "", result.stdout
        assert hashlib.sha256(database.read_bytes()).digest() == before, "CLI modified the snapshot"
        print(f"CLI {version}, database marker {marker}: {'accepted' if marker in accepted else 'rejected'}, unchanged")
