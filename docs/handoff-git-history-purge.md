# Task: purge the leaked Firebase API key from git history

Self-contained handoff. Everything needed is below — no prior context required.

## Goal

Remove the committed file that leaked a Google/Firebase API key from the **entire git
history** of this repo, then force-push the rewritten history to the public remote and
fix up the other clones. The key has **already been rotated and (should be) revoked**, so
this is a cleanup of the dead string, not an emergency — do it carefully, prioritize not
destroying uncommitted work over speed.

## Verified facts (confirmed 2026-06-09, re-verify before acting)

- **Repo**: `/home/trn/codes/mergulho-virtual`, branch `main`.
- **Leaked file** (the only blob in history containing the key):
  `src/app/MergulhoVirtual/Assets/Plugins/Android/FirebaseApp.androidlib/res/values/google-services.xml`
- That file is part of an EDM4U-generated dir — remove the **whole dir** from history:
  `src/app/MergulhoVirtual/Assets/Plugins/Android/FirebaseApp.androidlib/`
  (contains `AndroidManifest.xml`, `project.properties`, `res/values/google-services.xml`)
- **Old leaked key** (purge this): `AIzaSyDLin6vG7erKI1nNOZG8QTer8o333QLssQ`
- **New live key** (do NOT touch — it lives only in the gitignored
  `Assets/google-services.json`, never committed): `AIzaSyBRhhLvL-ciDFHTawCoKZDsd7wv_yCXEAU`
- The old key appears in history in **exactly one file path** and **two commits**:
  - `703c1e4` "ADD: Firebase app check..." — added the file (among other changes)
  - `6c15ef8` "Stop tracking EDM4U-generated FirebaseApp.androidlib..." — deleted the 3 files
  - Verified with: `git log --all -S "AIzaSyDLin6vG7erKI1nNOZG8QTer8o333QLssQ"` and
    `git grep -l "AIzaSyDLin6vG7erKI1nNOZG8QTer8o333QLssQ" $(git rev-list --all)`
- **Current HEAD = `6c15ef8`** — file already gone from the working tree / HEAD tree.
- **`main` is 1 commit AHEAD of `origin/main`.** `origin/main` is at `703c1e4`, where the
  file is still present at the tip — i.e. the **public remote still serves the key right now**.
  The removal commit `6c15ef8` has not been pushed.
- **Remotes**:
  - `origin` = `git@github.com:gfickel/mergulho-virtual-project.git` (canonical, public, LFS)
  - `oldfork` = `git@github.com:gfickel/mergulho-virtual.git` (older backup fork; also contains the leak)
- **Tooling**: `git-filter-repo` is **NOT installed**; BFG is NOT installed. Install filter-repo.
- **Git LFS** is in use (`.gitattributes`: `*.bundle *.so *.srcaar *.exe FirebaseCppApp*.dll`).
  The leaked XML is a small text file, **not** an LFS object, so path removal won't touch LFS
  pointers. Keep `git lfs` installed locally so pointer files aren't mangled on push.
- **Working tree is DIRTY**: ~8 tracked files modified (incl. `src/app/MergulhoVirtual/.gitignore`,
  several Unity assets/scripts) plus many untracked build artifacts (`Library/`, `Logs/`, etc.).
  This is the user's WIP and **must be preserved**. `git filter-repo` resets tracked files to the
  rewritten HEAD, so stash/commit the WIP first or it can be lost.
- **VM deploy**: a clone at `~/mergulho-virtual` on GCE VM `app-backend` (user `trn`) pulls `origin`
  via the repo-root `Makefile` (`make deploy`/`make ssh`). After a force-push it must be reset
  (see step 8). The VM has **no git-lfs** and doesn't need it (backend never reads `Assets/`).

## Preconditions to confirm with the user before starting

1. The **previous (leaked) key is deleted** in Google Cloud Console (Credentials, project
   `mergulho-virtual-2025`). If not yet deleted, the string is still live — deleting it is the
   actual revocation; the history rewrite is cosmetic. Confirm before relying on "it's safe."
2. No open PRs / other collaborators with unpushed work off `main` (a rewrite invalidates their
   clones). Solo repo as far as known, but confirm.

## Procedure (in-place rewrite with git-filter-repo)

### 1. Re-verify state
```bash
cd /home/trn/codes/mergulho-virtual
git rev-parse HEAD                      # expect 6c15ef8...
git status -sb                          # note dirty tracked files; expect "ahead 1"
git log --all -S "AIzaSyDLin6vG7erKI1nNOZG8QTer8o333QLssQ" --oneline   # expect 703c1e4, 6c15ef8
git remote -v
```

### 2. Backup (do not skip)
```bash
git bundle create ~/mergulho-history-backup.bundle --all
git rev-parse HEAD > ~/mergulho-head-before-rewrite.txt
```
`oldfork` on GitHub is also an untouched copy if you need to recover.

### 3. Preserve the dirty WIP
Stash only tracked changes (do NOT use `-u`; untracked build dirs are huge and unneeded):
```bash
git stash push -m "wip-before-history-rewrite"
git stash list      # confirm one entry
```
Note: `.gitignore` is among the stashed changes and already carries an ignore rule for
`FirebaseApp.androidlib/` (added this session). Make sure that rule survives (step 7 + 9).

### 4. Install git-filter-repo
```bash
pipx install git-filter-repo  ||  pip install --user git-filter-repo  ||  sudo apt-get install -y git-filter-repo
git filter-repo --version
```

### 5. Rewrite history — remove the generated dir from all commits
```bash
git filter-repo --force \
  --invert-paths \
  --path 'src/app/MergulhoVirtual/Assets/Plugins/Android/FirebaseApp.androidlib/'
```
- `--force` is required because this isn't a fresh clone.
- Expected: `703c1e4` keeps its other changes but loses the dir; `6c15ef8` (which *only* deleted
  those files) becomes empty and is **pruned** — that's correct, the net history simply never
  contains the file.
- **Optional belt-and-suspenders** (only needed if you distrust the "one file" finding): also
  redact the literal string. Create `/tmp/redact.txt` with the line
  `AIzaSyDLin6vG7erKI1nNOZG8QTer8o333QLssQ==>REDACTED` and add `--replace-text /tmp/redact.txt`
  to the command above (filter-repo accepts both in one run).

### 6. Re-add remotes (filter-repo strips `origin` as a safety)
```bash
git remote add origin  git@github.com:gfickel/mergulho-virtual-project.git
git remote add oldfork git@github.com:gfickel/mergulho-virtual.git   # optional
git remote -v
```

### 7. Restore the WIP
```bash
git stash pop
# Resolve any conflict (unlikely — the androidlib path isn't in the stash).
```

### 8. Verify the purge locally
```bash
git log --all -S "AIzaSyDLin6vG7erKI1nNOZG8QTer8o333QLssQ" --oneline   # expect EMPTY
git grep -l "AIzaSyDLin6vG7erKI1nNOZG8QTer8o333QLssQ" $(git rev-list --all) ; echo "exit=$?"  # expect no matches
```

### 9. Force-push the rewritten history
```bash
git lfs install         # ensure LFS hooks present so pointers push intact
git push origin main --force
# If other refs/tags ever existed: git push origin --force --all && git push origin --force --tags
```
LFS objects are unchanged and already on the server, so no LFS re-upload is expected.

### 10. Fix the VM clone (its history diverged)
```bash
make ssh    # or: ssh trn@<vm>  ;  repo at ~/mergulho-virtual
# on the VM:
cd ~/mergulho-virtual && git fetch origin && git reset --hard origin/main
```
Safe: the removed dir is Android-only; the backend never reads it. (`make deploy`'s plain
`git pull` would FAIL on the diverged history — use the reset above once.)

### 11. Commit the gitignore protection (if not already in history)
The whole `FirebaseApp.androidlib/` is regenerated from the gitignored
`Assets/google-services.json` on every EDM4U Android resolve. Confirm
`src/app/MergulhoVirtual/.gitignore` (committed, post-rewrite) contains:
```
/[Aa]ssets/Plugins/Android/FirebaseApp.androidlib/
```
If it's only in the stashed/WIP `.gitignore` and not yet committed, commit just that rule so a
future resolve can't re-leak the new key. (Beware: `.gitignore` may carry other unrelated WIP
edits — stage the rule narrowly, e.g. `git add -p`.)

### 12. Close out
- Mark the GitHub secret-scanning alert as **Revoked** (valid once the Console key is deleted).
- Old commit SHAs (`703c1e4`, `6c15ef8`) may stay reachable via GitHub's cache / direct SHA URL
  until GC. To force purge, open a GitHub Support request referencing the alert. Low urgency since
  the key is revoked.
- **`oldfork`** (`gfickel/mergulho-virtual`) still contains the leak. Decide: leave it (dead key,
  harmless) or repeat the rewrite/force-push there too. If it's a public fork, prefer purging or
  deleting it.
- Anyone who already cloned/forked still has the blob — irreducible; the rotation is what makes
  it harmless.

## Risks / footguns

- **filter-repo discards uncommitted tracked changes** by resetting to the rewritten HEAD — that's
  why step 3 stashes first. Don't skip it.
- **filter-repo removes the `origin` remote** after running — re-add it (step 6) or the push fails.
- **Empty-commit pruning** removes `6c15ef8`; expected, not an error.
- **Don't run `make deploy` against the VM before step 10** — the diverged `git pull` errors out.
- **Keep `git lfs` installed locally** before pushing so LFS pointer files aren't rewritten to
  full blobs.
- If anything goes wrong, recover from `~/mergulho-history-backup.bundle`
  (`git clone ~/mergulho-history-backup.bundle recovered`) or `oldfork`.

## Alternative (if you prefer not to rewrite in-place)

Do it on a throwaway `git clone --mirror git@github.com:gfickel/mergulho-virtual-project.git`,
run the same filter-repo, `git push --force`, then in the working repo
`git fetch origin && git reset --hard origin/main` (after stashing WIP). Downside: the mirror is at
`origin/main` = `703c1e4` and lacks the local-only `6c15ef8`, so you'd re-apply the removal — more
steps. In-place is simpler here because local `main` already has the removal commit.
