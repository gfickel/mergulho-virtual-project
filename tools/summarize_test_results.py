#!/usr/bin/env python3
"""Summarise a Unity NUnit3 test-results XML.

WHY THIS EXISTS — do not replace it with a grep.

The obvious one-liner is wrong in a way that looks right:

    grep -oE 'result="[A-Za-z]+"' ds-test-editmode.xml | sort | uniq -c

`result=` is carried by every <test-case>, by every <test-suite> rollup AND by
the root <test-run>. So that pipeline reports 749 for a run of 683 tests
(683 cases + 65 suites + 1 run) — inflating the count by roughly 10%, silently,
in the direction that looks like good news. That number has been quoted as the
test count more than once, including into project documentation.

Counting <test-case> elements is the only census that means anything.

Stdlib only, no venv — same rule as tools/generate_firestore_indexes.py.

Usage:
    python3 tools/summarize_test_results.py <results.xml> [--quiet]

Exits 1 if any test failed (or the file is unreadable/absent), else 0, so it
can drive a Makefile recipe on its own.
"""

import sys
import xml.etree.ElementTree as ET
from collections import Counter

MAX_FAILURES_SHOWN = 25
MAX_MESSAGE_LINES = 6


def main(argv):
    args = [a for a in argv[1:] if not a.startswith("--")]
    quiet = "--quiet" in argv[1:]
    if len(args) != 1:
        print(__doc__.strip(), file=sys.stderr)
        return 2

    path = args[0]
    try:
        root = ET.parse(path).getroot()
    except FileNotFoundError:
        print(f"no results file at {path} — did the run get far enough to write one?",
              file=sys.stderr)
        return 1
    except ET.ParseError as exc:
        # A run killed mid-write leaves a truncated file; say so rather than
        # reporting "0 tests", which reads like a clean run.
        print(f"{path} is not parseable XML ({exc}) — the run was probably "
              f"interrupted before the results were flushed.", file=sys.stderr)
        return 1

    cases = list(root.iter("test-case"))
    by_result = Counter(c.get("result", "Unknown") for c in cases)
    failed = [c for c in cases if c.get("result") == "Failed"]

    # Per-assembly breakdown: the assembly is the outermost test-suite of
    # type="Assembly" above each case. Walking parents is not supported by
    # ElementTree, so map child->parent once.
    parents = {child: parent for parent in root.iter() for child in parent}

    def assembly_of(case):
        node = parents.get(case)
        while node is not None:
            if node.get("type") == "Assembly":
                return node.get("name", "?")
            node = parents.get(node)
        return "?"

    per_assembly = Counter(assembly_of(c) for c in cases)

    total = len(cases)
    passed = by_result.get("Passed", 0)
    other = {k: v for k, v in by_result.items() if k not in ("Passed", "Failed")}

    if not quiet or failed:
        parts = [f"{passed}/{total} passed"]
        if failed:
            parts.append(f"{len(failed)} FAILED")
        for name, count in sorted(other.items()):
            parts.append(f"{count} {name.lower()}")
        print("  " + ", ".join(parts))
        for name, count in sorted(per_assembly.items()):
            print(f"    {count:>5}  {name}")

    for case in failed[:MAX_FAILURES_SHOWN]:
        print(f"\n  FAILED  {case.get('fullname', case.get('name', '?'))}")
        message = case.find(".//message")
        if message is not None and message.text:
            for line in message.text.strip().splitlines()[:MAX_MESSAGE_LINES]:
                print(f"          {line}")
    if len(failed) > MAX_FAILURES_SHOWN:
        print(f"\n  … and {len(failed) - MAX_FAILURES_SHOWN} more failures "
              f"(full detail in {path})")

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
