#!/usr/bin/env python3
"""Import the not-yet-done Trello cards into the Jira project MV.

Filtering: skips archived cards and cards whose Trello due-date checkbox is
marked complete (`dueComplete`) — that's how done work is flagged on this board.

Structure created in Jira (team-managed project):
  - one Epic per Trello list (only lists that still have open cards)
  - one Task per card, parented to its list's Epic
  - every issue gets the labels: dev, trello-import
  - card description + unchecked checklist items + a link back to the Trello
    card go into the Jira description

Usage:
  export JIRA_EMAIL="guilhermefickel@gmail.com"          # your Atlassian login
  export JIRA_API_TOKEN="..."                             # id.atlassian.com > Security > API tokens
  python3 tools/trello_to_jira.py            # dry run: prints what would be created
  python3 tools/trello_to_jira.py --apply    # actually creates the issues

Idempotent: created issues are recorded in tools/jira_import_state.json
(trello card id -> jira key), and re-runs skip anything already imported.

Stdlib only — no venv needed.
"""

import json
import os
import sys
import base64
import urllib.request
import urllib.error
from pathlib import Path

JIRA_BASE = "https://mergulhovirtual.atlassian.net"
PROJECT_KEY = "MV"
LABELS = ["dev", "trello-import"]

REPO_ROOT = Path(__file__).resolve().parent.parent
TRELLO_EXPORT = REPO_ROOT / "tools" / "trello_board.json"
STATE_FILE = REPO_ROOT / "tools" / "jira_import_state.json"

# Trello lists to skip entirely — neither the epic nor its cards are imported.
EXCLUDE_LISTS = {
    "Clima",
    "Tubarões",
    "Viagem",
    "UX",
}

# Card names to skip on top of the dueComplete filter (e.g. things already
# done in the codebase but never checked off in Trello). Edit freely.
EXCLUDE_CARDS = {
    # "Possível detectar oceano?",
    # "Funciona na praia?",
}


def api(path, method="GET", body=None):
    email = os.environ.get("JIRA_EMAIL")
    token = os.environ.get("JIRA_API_TOKEN")
    if not email or not token:
        sys.exit("Set JIRA_EMAIL and JIRA_API_TOKEN env vars first (see docstring).")
    req = urllib.request.Request(JIRA_BASE + path, method=method)
    auth = base64.b64encode(f"{email}:{token}".encode()).decode()
    req.add_header("Authorization", f"Basic {auth}")
    req.add_header("Accept", "application/json")
    if body is not None:
        req.add_header("Content-Type", "application/json")
        req.data = json.dumps(body).encode()
    try:
        with urllib.request.urlopen(req) as resp:
            return json.load(resp)
    except urllib.error.HTTPError as e:
        detail = e.read().decode(errors="replace")
        sys.exit(f"Jira API {method} {path} failed: HTTP {e.code}\n{detail}")


def adf(card, checklists):
    """Build an Atlassian Document Format description for a card."""
    content = []
    desc = (card.get("desc") or "").strip()
    if desc:
        content.append({
            "type": "paragraph",
            "content": [{"type": "text", "text": desc}],
        })
    for clid in card.get("idChecklists", []):
        cl = checklists.get(clid)
        if not cl:
            continue
        items = sorted(cl["checkItems"], key=lambda i: i["pos"])
        pending = [i["name"] for i in items if i["state"] != "complete"]
        if not pending:
            continue
        content.append({
            "type": "paragraph",
            "content": [{"type": "text", "text": cl["name"] + ":",
                         "marks": [{"type": "strong"}]}],
        })
        content.append({
            "type": "bulletList",
            "content": [
                {"type": "listItem", "content": [
                    {"type": "paragraph",
                     "content": [{"type": "text", "text": name}]}]}
                for name in pending
            ],
        })
    url = card.get("shortUrl")
    if url:
        content.append({
            "type": "paragraph",
            "content": [{"type": "text", "text": "Trello: "},
                        {"type": "text", "text": url,
                         "marks": [{"type": "link", "attrs": {"href": url}}]}],
        })
    if not content:
        content = [{"type": "paragraph", "content": [
            {"type": "text", "text": "(importado do Trello)"}]}]
    return {"type": "doc", "version": 1, "content": content}


def resolve_issue_types():
    """Find the Epic and Task issue-type ids for the project (names may be localized)."""
    data = api(f"/rest/api/3/issue/createmeta/{PROJECT_KEY}/issuetypes")
    types = data.get("issueTypes", data.get("values", []))
    epic = task = None
    for t in types:
        level = t.get("hierarchyLevel")
        name = t.get("name", "")
        if level == 1 or name in ("Epic", "Épico"):
            epic = epic or t
        elif (level == 0 or level is None) and not t.get("subtask"):
            # prefer the plain Task type over Story/Bug when several exist
            if task is None or name in ("Task", "Tarefa"):
                task = t
    if not epic or not task:
        sys.exit(f"Could not find Epic/Task issue types. Got: "
                 f"{[(t.get('name'), t.get('hierarchyLevel')) for t in types]}")
    return epic, task


def main():
    apply = "--apply" in sys.argv

    board = json.loads(TRELLO_EXPORT.read_text())
    lists_by_id = {l["id"]: l for l in board["lists"]}
    checklists = {c["id"]: c for c in board["checklists"]}
    state = json.loads(STATE_FILE.read_text()) if STATE_FILE.exists() else {}

    todo = [c for c in board["cards"]
            if not c["closed"]
            and not c.get("dueComplete")
            and not lists_by_id[c["idList"]]["closed"]
            and lists_by_id[c["idList"]]["name"].strip() not in EXCLUDE_LISTS
            and c["name"].strip() not in EXCLUDE_CARDS]
    # group by list, keeping board order
    order = {l["id"]: l["pos"] for l in board["lists"]}
    todo.sort(key=lambda c: (order[c["idList"]], c["pos"]))

    list_names = []
    for c in todo:
        n = lists_by_id[c["idList"]]["name"].strip()
        if n not in list_names:
            list_names.append(n)

    print(f"{len(todo)} cards to import into {len(list_names)} epics "
          f"({'APPLY' if apply else 'dry run'}):\n")
    for name in list_names:
        print(f"EPIC: {name}")
        for c in todo:
            if lists_by_id[c["idList"]]["name"].strip() == name:
                done = " (already imported)" if c["id"] in state else ""
                print(f"  - {c['name']}{done}")
    if not apply:
        print("\nDry run only. Re-run with --apply to create these in Jira.")
        return

    epic_type, task_type = resolve_issue_types()
    print(f"\nIssue types: epic={epic_type['name']} ({epic_type['id']}), "
          f"task={task_type['name']} ({task_type['id']})")

    def save_state():
        STATE_FILE.write_text(json.dumps(state, indent=2, ensure_ascii=False))

    epic_keys = {}  # list name -> jira key
    for name in list_names:
        state_key = f"epic:{name}"
        if state_key in state:
            epic_keys[name] = state[state_key]
            continue
        res = api("/rest/api/3/issue", "POST", {
            "fields": {
                "project": {"key": PROJECT_KEY},
                "issuetype": {"id": epic_type["id"]},
                "summary": name,
                "labels": LABELS,
            }
        })
        epic_keys[name] = res["key"]
        state[state_key] = res["key"]
        save_state()
        print(f"created epic {res['key']}: {name}")

    for c in todo:
        if c["id"] in state:
            continue
        list_name = lists_by_id[c["idList"]]["name"].strip()
        res = api("/rest/api/3/issue", "POST", {
            "fields": {
                "project": {"key": PROJECT_KEY},
                "issuetype": {"id": task_type["id"]},
                "summary": c["name"].strip(),
                "description": adf(c, checklists),
                "labels": LABELS,
                "parent": {"key": epic_keys[list_name]},
            }
        })
        state[c["id"]] = res["key"]
        save_state()
        print(f"created {res['key']}: {c['name']}")

    print("\nDone. All issues carry the labels " + ", ".join(LABELS) + ".")


if __name__ == "__main__":
    main()
