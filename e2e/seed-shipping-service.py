#!/usr/bin/env python3
"""Emit a populated service fixture for the disposable shipping quickstart."""

from pathlib import Path
import sys


def fixture_sql(server_source: Path) -> str:
    seed = (server_source / "tests/seed/base-schema.sql").read_text()
    start = seed.index("CREATE OR REPLACE FUNCTION honua.seed_metadata_v2_compat_snapshot()")
    end = seed.index("\n$$;", start) + len("\n$$;")
    fixture = Path(__file__).with_name("shipping-service.sql").read_text()
    folders = Path(__file__).with_name("shipping-folder.sql").read_text()
    # Use the selected server's compatibility compiler rather than copying its schema.
    return fixture + "\n" + seed[start:end] + "\nSELECT honua.seed_metadata_v2_compat_snapshot();\n" + folders


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: seed-shipping-service.py SERVER_SOURCE")
    sys.stdout.write(fixture_sql(Path(sys.argv[1])))
