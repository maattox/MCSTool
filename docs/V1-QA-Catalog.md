# V1 QA catalog — pre-packaging

**Status:** Living test catalog for [Phase 8.5](V1-Implementation-Plan.md#phase-85--pre-packaging-qa).  
**Parent:** `[V1-Implementation-Plan.md](V1-Implementation-Plan.md)`.  
**Results:** fill `[V1-QA-Pass-N-Results.md](V1-QA-Pass-3-Results.md)` for the current pass. Pass 1 and Pass 2 are historical. Do **not** edit expected steps in this catalog just to record an outcome. Product changes (Steps 8.4, **8.7**, **8.8**) **may** update expected (S4-02, S3-01, S6-01, S6-02, S4-01, S3-07, plus 8.7/8.8 chrome).  
**Pass 2 execution:** [`archive/V1-QA-Pass-2-Scope.md`](archive/V1-QA-Pass-2-Scope.md) (**closed early**).  
**Pass 3 execution:** [`V1-QA-Pass-3-Scope.md`](V1-QA-Pass-3-Scope.md) (**blocked** until Step **8.10** completes and the operator starts Pass 3). Do not re-run the full catalog.  
**Fix work:** after triage, an agent writes `[V1-Bug-Fix-Plan-Pass-N.md](V1-Bug-Fix-Plan-TEMPLATE.md)` from the filled results. Agents implement **that** plan, not this catalog.

This catalog is `dotnet run` **+ TESTING**. Pass 1 used the existing Vanilla stack. Pass 2 is Delete + greenfield **Modded** (**closed early**). Pass 3 is gap-close + Steps **8.4 / 8.7 / 8.8 / 8.9 / 8.10** tests on that stack. It is not the PRODUCT-IDEAS clean-room (new account + installer + real $1 budget fire). Do not start [Step 8.6.1](V1-Implementation-Plan.md#step-861--ci-built-arm-image--setup-copy-into-ocir) or [Step 9.1](V1-Implementation-Plan.md#step-91--windows-installer) from this file.

**Cost:** $0 (Always Free–eligible). Never open `0.0.0.0/0` on Minecraft, SSH, or door admin.

---



## How to use this file

1. **Pass 1 (DONE):** full S0–S7 on Vanilla (S7-04 Skipped). Bug-fix P1–P8 DONE.
2. **Pass 2 (DONE, closed early):** follow [`V1-QA-Pass-2-Scope.md`](V1-QA-Pass-2-Scope.md) was Phase A + join only. Filled [`V1-QA-Pass-2-Results.md`](V1-QA-Pass-2-Results.md). Do **not** re-run it.
3. **Pass 3 (blocked):** follow [`V1-QA-Pass-3-Scope.md`](V1-QA-Pass-3-Scope.md) after Step **8.9**. Fill [`V1-QA-Pass-3-Results.md`](V1-QA-Pass-3-Results.md).
4. **One agent chat owns the TESTING stack at a time.** Two chats shortening idle and invoking the spend-brake Function will collide. Greenfield destroy/apply was Pass 2 Phase A only.
5. Fill the pass results file as you go. Do not wait until the end of a three-hour session.
6. Later passes = **failed tests from the last pass + smoke ([S0-01](#s0-01--core-unit-tests), [S1-03](#s1-03--stack-snapshot), [S2-08](#s2-08--wake-from-stopped-unlocked), [S2-09](#s2-09--idle-softstop-short-timeout), [S2-17](#s2-17--invoke-function-with-fake-actual-alert), [S3-01](#s3-01--spend-brake-overlay--typed-confirm), [S4-01](#s4-01--novice-chrome)) + tests for files that changed** + gaps the last pass never covered. Full catalog re-run only before declaring Phase 8.5 done.
7. Agents: read **the current pass scope + named catalog IDs**. Do not load the Minecraft blueprint, PRODUCT-IDEAS, or the whole V1 plan.
8. The operator **may pause a pass after a suite** for a Blocker (write/run [`V1-Bug-Fix-Plan-Pass-N.md`](V1-Bug-Fix-Plan-TEMPLATE.md), then a delta). Do not start 8.6.1 or 9.1. Living product work: Step **8.9** [`V1-Pack-Import-Assisted-Review-Plan.md`](V1-Pack-Import-Assisted-Review-Plan.md). Historical: Step **8.7** [`V1-Modpack-Test-Follow-On-Plan.md`](V1-Modpack-Test-Follow-On-Plan.md), Step **8.8** [`V1-Operator-Notes-Follow-On-Plan.md`](V1-Operator-Notes-Follow-On-Plan.md), Pass 2 pause [`V1-Pass-2-Follow-On-Plan.md`](V1-Pass-2-Follow-On-Plan.md) (**DONE**).



### Operator prompt (copy-paste)

**Current follow-on (Step 8.9) — not a QA pass:**

Canonical text: [`V1-Pack-Import-Assisted-Review-Plan.md`](V1-Pack-Import-Assisted-Review-Plan.md) → Operator entry.

**Pass 3 (blocked until 8.9):** [`V1-QA-Pass-3-Scope.md`](V1-QA-Pass-3-Scope.md).

**Historical Pass 2 / Pass 1:** filled results files. Do not use Pass 2 Phase A (tofu destroy) again.

---



## Runners


| Runner        | Who                                                                  | Typical tools                                                                |
| ------------- | -------------------------------------------------------------------- | ---------------------------------------------------------------------------- |
| `dotnet-test` | Agent or operator                                                    | `dotnet test` — do not re-do as a click path                                 |
| `agent`       | Agent alone                                                          | OCI CLI/SDK profile `TESTING`, SSH both test VMs, `fn` for product Functions |
| `hybrid`      | Agent stages/verifies; operator uses Hybrid UI or a Minecraft client | Same cloud access as `agent`                                                 |
| `operator`    | Operator at Hybrid / Minecraft                                       | Eyes on layout, copy, overlays, drag-drop                                    |


Agents **cannot** drive the WPF/Blazor window. Do not spend the pass inventing UI automation.

---



## Result schema (every test)

In the pass results file, each ID gets:


| Field                            | Values                                                                                   |
| -------------------------------- | ---------------------------------------------------------------------------------------- |
| **Result**                       | `Pass` / `Fail` / `Blocked` / `Skipped` / `Known`                                        |
| **Severity** (operator, on Fail) | `Blocker` / `Major` / `Minor` / `Nit` / `After-v1` / `Won't-fix`                         |
| **Notes**                        | Required on anything other than Pass. On Fail: expected vs actual + enough to reproduce. |


`Known` must cite an `Issues.md` id (example: `DOOR-ISSUE-1`). Do not open a new bug for a parked known issue unless it **regressed** (worse than documented).

Optional on Fail: screenshot path, `journalctl` snippet, approximate timestamp (UTC). **Do not paste live OCIDs, Auth Tokens, or RCON passwords into the results file.**

---



## Efficiency (do this, not a tab-order marathon)

1. **S0 first.** If Core tests fail, stop and fix or file before burning VM time.
2. **Order by VM state**, not by Manager tab. Suggested S2 flow: preflight → inspect while RUNNING → wake/idle cycle **once** → spend-brake Function → restore.
3. **Short idle for most waits.** For idle SoftStop tests, set timeout to **2 minutes**, then restore the saved value. Run **one** default-timeout confirmation only if a 2-minute pass already worked (S2-09b, optional).
4. **Do not fire the real $1 budget.** Invoke the Function with a **synthetic** Events body, or PUT the lock object. A Console/$1 actual-spend alert can bill ~$1–$2 — that is the post-packaging clean-room test, not this catalog.
5. **Fixture the lock for UI.** S2 proves door + Function. S3-01 is the overlay. Do not wait for a real alert.
6. **MOTD:** agent checks door state / wake-refuse / journal strings. Operator opens Minecraft **once** in S5 for the human-visible MOTD/kick.
7. **Greenfield last** (S7). Do not destroy the stack you need for S2–S6.
8. **Screenshots on Fail only.**
9. **Pass 2 is a delta**, not a new encyclopedia.

---



## Out of this catalog


| Item                                    | Why                                                                                      |
| --------------------------------------- | ---------------------------------------------------------------------------------------- |
| Archived Python Manager                 | Product under test is `McManager.Hybrid`                                                 |
| DEBUG Advanced probes as a suite        | Optional while diagnosing; not v1 user paths                                             |
| Core unit tests as manual steps         | S0 cites them                                                                            |
| Real Oracle **$1 budget fire**          | Clean-room / accepted spend; not TESTING day-to-day                                      |
| Windows installer / GitHub update check | V1 Phase 9 — **after** Phase 8.6 (CI Function image)                                     |
| Docker Desktop on the admin PC to install the spend-brake Function | **Rejected** for the product path (V1 Step **8.6.1**). TESTING `fn`/`docker` remains an agent fill-in until then. |
| After-v1 PRODUCT-IDEAS                  | Players tab, pack-replace **light swap**, PTY, paid mode, … **Change pack (full re-setup)** is v1 in Step 8.4 |
| In-app pack browser / public Minecraft  | **Rejected**                                                                             |
| Live Forge lab (`DEFAULT` profile)      | Forbidden                                                                                |
| `tofu apply` / `tofu destroy`           | Still **operator-authorized per session** — Pass 2 **Phase A** authorizes TESTING destroy-then-apply for S7-04. Other chats: skip unless the operator says so |


---



## Agent protocol (TESTING)

Same stack as the V1 plan [Test stack access](V1-Implementation-Plan.md#test-stack-access-oci--ssh):


| Item          | Value                                                                                                                                                                                                                            |
| ------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| OCI profile   | `TESTING` **only** — never `DEFAULT`                                                                                                                                                                                             |
| SSH           | `ubuntu` + `%USERPROFILE%\.ssh\mcmgr_ed25519_20260817_125552`                                                                                                                                                                    |
| Hosts / OCIDs | gitignored `data/config.local.json` — do not copy into this catalog or chat dumps                                                                                                                                                |
| Idle          | If you START VM1 or Minecraft: **disable idle**, then **re-enable when the session ends**. Minecraft boot force-enables idle (OS-ISSUE-7) — disable again after a game start. Exception: tests whose **point** is idle SoftStop. |
| `ubuntu`      | `Permission denied` → `sudo` (`[Agent-Deploy-Pitfalls.md](Agent-Deploy-Pitfalls.md)`)                                                                                                                                            |
| OCI waits     | `[OCI-API-Usage.md](OCI-API-Usage.md)` — 429 backoff, waiter-style polls (not 1s loops), modest Object Storage                                                                                                                   |




### Product Functions (blanket, $0)

This blanket is **TESTING / until V1 Step 8.6.1**. The **product** path is a CI-built ARM image copied into the user’s OCIR (no Docker on the admin PC). `fn`/`docker` here does not define the installer story.

Agents **may** without asking, on **TESTING** only:

- `fn build` / `fn deploy` / `fn push` of **product** Functions: `functions/shutdown_vm/`, `functions/reconcile_usage/`
- `fn invoke` / `oci fn function invoke` with **synthetic** payloads
- `docker login` + push **only** as part of those Function images onto the **existing** TESTING OCIR repo / Function application

**$0 constraints (non-negotiable):**

- Do **not** create a real compartment budget alert or drive actual spend to $1.  
- SoftStop **VM1** via the Function is OK (Always Free A1 hours).  
- **Do not SoftStop the door Micro.** If an old image (0.0.11) still stops both, **START the door immediately**, then push v1 (`0.0.12`, VM1 only + lock PUT).  
- Do not add paid Function memory, extra OCIR repos, extra Functions applications, or other billable resources. Replace the existing image in place.  
- After lock tests: **DELETE** `meta/spend-brake-triggered.json` unless the next hybrid test needs it.  
- Never print Auth Tokens. Prefer Windows Credential Manager `McManager/ocir` when Setup already stored one.  
- **Never** `fn push` / invoke against `DEFAULT` / the live Forge lab.

`tofu apply` / `tofu plan` / `tofu destroy` / deleting the compartment remain **operator-authorized**.

### Restore (every mutating session)

Before you stop:

1. DELETE spend-brake lock if you created it (404 = OK).
2. Restore idle **timeout** if you shortened it.
3. Re-enable idle timer + `idle_agent_enabled=true` unless the operator is about to click through Hybrid with idle off — then say so in the results file.
4. Do not leave VM1 RUNNING overnight without asking.

---



## Known issues (pre-seeded)

Treat as `Known`, not a new Fail, unless worse than documented. Living list: [`Issues.md`](Issues.md).


| ID                           | Summary                                                              | Catalog                                                     |
| ---------------------------- | -------------------------------------------------------------------- | ----------------------------------------------------------- |
| **DOOR-ISSUE-1**             | First client connect sometimes misses custom “starting” kick         | S5-02                                                       |
| **OS-ISSUE-3**               | Force Start can dual-write ledger intervals                          | S4-22 (prefer door Start)                                   |
| **OS-ISSUE-7**               | Idle disable does not survive boot / Minecraft start (**by design**) | S2-28, S4-18                                                |
| **FN-ISSUE-1**               | Old Function image SoftStops the door                                | S2-16 / S2-17 — **gone on TESTING** (P3); Forge lab may still be 0.0.11 |
| **OS-ISSUE-6** (backup skip) | Function/Console SoftStop skips world backup (MVP deferred)          | S2-17 — note only                                           |
| **OS-ISSUE-9**               | Guest ACPI SoftStop stuck OCI **STOPPING** — **fixed** (firewalld/cloud-init/dbus; P1) | S2-05 / S2-08 on **greenfield** (Pass 2 Phase B) |


---



## QA exit (Phase 8.5 done)

- No open **Blocker** or **Major** on the latest pass (or parked with operator OK).  
- Smoke: S0-01, S1-03, S2-08, S2-09, S2-17, S3-01, S4-01 all `Pass` (or S3-01/S4-01 Pass on the operator pass).  
- Remaining items are `Known` / `After-v1` / `Won't-fix` with ids.  
- Then V1 **NEXT** may move to Step **8.6.1** (not 9.1). Operator asks.

---



# Suites

Each test: **ID**, **Runner**, **Duration**, **State** (starting VM/game), **Steps**, **Expected**, **Restore**.

Duration: `quick` · `wait` · `destructive`.  
IDs are **stable** (do not renumber). Gaps are intentional.

---



## S0 — Automated (already covered)

Run from `OCI-mc-server`. Record overall Pass/Fail; paste the failing test name on Fail.

### S0-01 — Core unit tests

**Runner:** `dotnet-test` · **Duration:** `quick` · **State:** n/a

**Steps**

```powershell
dotnet test src\McManager.slnx
```

**Expected:** All tests pass (planner, spend-brake DTO/UX, pack analyzers, Connect-existing, wipe paths, etc.).

**Restore:** none.

### S0-02 — Spend-brake Function unit tests

**Runner:** `dotnet-test` (Python unittest) · **Duration:** `quick`

**Steps**

```powershell
python -m unittest test_func.py
```

Working directory: `functions/shutdown_vm/`.

**Expected:** Event policy (RESET skip, ACTUAL/FORECAST SoftStop+lock) passes with no OCI.

**Restore:** none.

### S0-03 — `reconcile_usage` unit tests

**Runner:** `dotnet-test` · **Duration:** `quick`

**Steps:** Run the Function’s documented unittest (see `functions/reconcile_usage/`). If none exist, **Skipped** with note.

**Expected:** Mocked Usage payload tests pass. No live Usage API required.

**Restore:** none.

### S0-04 — OpenTofu validate

**Runner:** `agent` · **Duration:** `quick`

**Steps:** `tofu validate` in `infra/` (no apply).

**Expected:** Success.

**Restore:** none.

### S0-05 — Door `make test` (optional)

**Runner:** `agent` · **Duration:** `quick`

**Steps:** If gcc/WSL is available, `make test` in `door_vm/`. Else **Skipped**.

**Expected:** MOTD/state tests include `SPEND_BRAKE` vs daily budget strings.

**Restore:** none.

---



## S1 — Agent preflight



### S1-01 — TESTING profile

**Runner:** `agent` · **Duration:** `quick` · **State:** n/a

**Steps:** `oci iam region list --profile TESTING` (or `oci os ns get --profile TESTING`). Confirm the profile is not `DEFAULT`.

**Expected:** Auth works. Region matches `config.local.json`.

**Restore:** none.

### S1-02 — SSH both VMs

**Runner:** `agent` · **Duration:** `quick`

**Steps:** If VM1 is STOPPED, START it (Always Free) and wait RUNNING. SSH `ubuntu@` both hosts with the named key: `hostname` and `sudo -n true`.

**Expected:** Both sessions work. `sudo -n` may fail on passwordless — then `sudo true` with the usual key setup, or note it. Recurring `Permission denied` on `/etc/mcmgr` as `ubuntu` is **not** a Fail.

**Restore:** Leave VM1 up for S1–S2 inspect; disable idle (S1-04).

### S1-03 — Stack snapshot

**Runner:** `agent` · **Duration:** `quick`

**Steps:** Record into the results file (**no OCIDs**): VM1/door lifecycle; who holds the reserved play IP (primary vs secondary VNIC); spend-brake lock present/absent; `minecraft.service` active?; idle timer enabled/active; door `/api/status` or `systemctl is-active mccontrol`; Security List: any 25565 `0.0.0.0/0`? (must be **no**).

**Expected:** Door RUNNING. Minecraft 25565 is **not** world-open. Snapshot is enough for later diffs.

**Restore:** none.

### S1-04 — Disable idle for inspect session

**Runner:** `agent` · **Duration:** `quick` · **State:** VM1 RUNNING

**Steps:** Use the V1 plan disable snippet (`idle_agent_enabled=false`, stop+disable `mc-idle-watch.timer`). If you will run S2-09 in the **same** session, skip this until after S2-08 or re-enable only for S2-09.

**Expected:** Timer inactive so inspect tests are not SoftStopped mid-SSH.

**Restore:** Re-enable at session end (S1-05) unless S2-09 still needs it on.

### S1-05 — Session restore

**Runner:** `agent` · **Duration:** `quick`

**Steps:** Run the restore list in [Agent protocol](#restore-every-mutating-session).

**Expected:** Lock gone; idle timeout original; idle on unless operator asked to leave it off.

**Restore:** this **is** restore.

---



## S2 — Agent on-box / cloud

Do **S2-08 → S2-11 → S2-16 → S2-18** as one story when possible. Inspect tests (S2-01–S2-07) while VM1 is already RUNNING after S1.

### S2-01 — Game tree contract

**Runner:** `agent` · **Duration:** `quick` · **State:** VM1 RUNNING

**Steps:** SSH VM1. `namei -l /opt/mcmgr /opt/mcmgr/server`. `systemctl cat minecraft` — `User=` and `WorkingDirectory=`. Do **not** chmod 0777.

**Expected:** `User=mcmgr` (not `ubuntu`). WorkingDirectory is under `/opt/mcmgr/server`. Layout is `root:mcmgr` / `mcmgr:mcmgr` as in `onbox/mcmgr` (SETUP-ISSUE-4 must not recur).

**Restore:** none.

### S2-02 — Game manifest

**Runner:** `agent` · **Duration:** `quick`

**Steps:** `sudo cat /etc/mcmgr/game-manifest.json` (or documented path). Note `distribution` / `loader` / `java_major` in results (**no secrets**).

**Expected:** Valid JSON. Matches what this test stack was deployed as (Vanilla/Paper/modded).

**Restore:** none.

### S2-03 — RCON localhost only

**Runner:** `agent` · **Duration:** `quick`

**Steps:** On VM1: `ss -lntp | grep 25575` (or `ncat`). Confirm listeners. Get Security List: no 25575 ingress.

**Expected:** RCON bound to localhost (or equivalent not public). SL has no 25575.

**Restore:** none.

### S2-04 — In-game whitelist off

**Runner:** `agent` · **Duration:** `quick`

**Steps:** `sudo grep -E '^white-list' /opt/mcmgr/server/server.properties`

**Expected:** `white-list=false` (SETUP-ISSUE-3). Join is OCI allowlist, not Minecraft whitelist.

**Restore:** none.

### S2-05 — firewalld 25565 after boot

**Runner:** `agent` · **Duration:** `quick`

**Steps:** `sudo firewall-cmd --list-all` (and/or rich rules). Confirm 25565 tcp/udp. `systemctl is-enabled netfilter-persistent` — product wants it **masked** (SETUP-ISSUE-7).

**Expected:** 25565 allowed in firewalld. `netfilter-persistent` not fighting firewalld after SoftStop reboot.

**Restore:** none.

### S2-06 — Security List private

**Runner:** `agent` · **Duration:** `quick`

**Steps:** `GetSecurityList` for the product list. List 25565 sources.

**Expected:** No Minecraft `0.0.0.0/0`. SSH not world-open. ICMP / non-owned rules still present.

**Restore:** none.

### S2-07 — Door control plane up

**Runner:** `agent` · **Duration:** `quick` · **State:** door RUNNING

**Steps:** SSH door. `systemctl is-active mccontrol`. `curl -sS` door status API if documented (`/api/status`). Confirm secondary play IP / netplan exists (`99-mcmgr-play` or equivalent).

**Expected:** `mccontrol` active. Status JSON parseable. Guest has the play secondary.

**Restore:** none.

### S2-08 — Wake from STOPPED (unlocked)

**Runner:** `agent` · **Duration:** `wait` · **State:** VM1 STOPPED, lock **absent**, idle off until Minecraft is up if you will inspect

**Steps**

1. Confirm lock object 404.
2. SoftStop/stop VM1 if needed; wait **STOPPED** (not STOPPING).
3. Confirm play IP is on the **door**.
4. `POST /api/wake` (or documented door wake). Wait VM1 **RUNNING** with waiter backoff.
5. Confirm play IP moved to VM1. Probe TCP 25565 on play IP (from this PC if allowlisted, or from door).
6. `wait_forge` / `minecraft.service` active — do not fail solely on first-kick MOTD (DOOR-ISSUE-1).

**Expected:** Wake succeeds. Door does not stay DEGRADED. Game port eventually listens. Start-on-already-RUNNING is a no-op (DOOR-ISSUE-6 must not 409-loop).

**Restore:** Disable idle if it force-enabled (OS-ISSUE-7) before other inspect work.

### S2-09 — Idle SoftStop (short timeout)

**Runner:** `agent` · **Duration:** `wait` · **State:** VM1 RUNNING, Minecraft **empty or inactive**

**Steps**

1. Save current `idle_timeout_minutes` (or equivalent) from `/etc/mc-manager/config.json`.
2. Set timeout to **2**. `idle_agent_enabled=true`. `enable --now mc-idle-watch.timer`.
3. Ensure no players (RCON `list` over SSH localhost). Minecraft not running **or** running empty — both should SoftStop after the same timeout (first tick starts the clock only; do not SoftStop on the first tick of a normal start).
4. Wait with Compute GET backoff until VM1 **STOPPED** (cap ~15 min for a 2 min timeout + SoftStop).
5. Confirm play IP on **door**; door `mccontrol` listening; door Micro still RUNNING.

**Expected:** VM1 SoftStops. IP handback works (DOOR-ISSUE-7). World backup on the **idle** path (not on Function SoftStop). Ledger: open interval closed or heal path eligible when STOPPED.

**Restore:** Put original timeout back. Leave VM1 STOPPED or START+disable idle for later tests.

### S2-09b — Idle SoftStop default timeout (optional)

**Runner:** `agent` · **Duration:** `wait`

**Steps:** Same as S2-09 with **default 15 minutes**, only if S2-09 passed and you want one real-clock confirmation.

**Expected:** Same as S2-09.

**Restore:** same.

### S2-10 — Heal when STOPPED

**Runner:** `agent` · **Duration:** `quick` · **State:** VM1 **STOPPED**

**Steps:** On door, run documented `heal_os_ledger.sh` (sudo; `HOME` set). Must skip if lifecycle is STOPPING (OS-ISSUE-6 heal race).

**Expected:** Heal OK or skip with a clear reason. No `HOME: unbound`. Does not run heal while STOPPING.

**Restore:** none.

### S2-11 — Lock fixture refuses wake (no Function)

**Runner:** `agent` · **Duration:** `wait` · **State:** VM1 STOPPED

**Steps**

1. PUT a valid v1 `meta/spend-brake-triggered.json` ([contract](Contracts-Object-Storage.md)).
2. `POST /api/wake`.
3. Confirm VM1 **stays STOPPED**. Door stays RUNNING.
4. Door state / journal / kick path mentions **MONTHLY SPEND BRAKE FIRED**, not daily budget copy.
5. DELETE the object. `/api/os-refresh` or `pull_os_budget.sh --force`. Confirm `SPEND_BRAKE_LOCK=0`.

**Expected:** Downstream lock works even if Function image is old. Wake does not START VM1 while locked.

**Restore:** Object deleted.

### S2-12 — Fail-closed malformed lock (optional)

**Runner:** `agent` · **Duration:** `quick`

**Steps:** PUT malformed JSON at the lock key. Wake. DELETE after.

**Expected:** Treated as locked (fail closed). Then delete.

**Restore:** DELETE.

### S2-16 — Deploy v1 `shutdown_vm` on TESTING

**Runner:** `agent` · **Duration:** `wait`

**Steps:** Compare live Function image vs `functions/shutdown_vm/` (`func.yaml` **0.0.12**, VM1 only + lock PUT). If live is missing / **0.0.11** / stops both VMs: until Step **8.6.1** ships, `fn build` + `fn push` / interim Setup publisher onto **TESTING** OCIR + Function app. After 8.6.1, use the product copy path (no Docker required). Do not touch `DEFAULT`. Stay on existing app/repo.

**Expected:** TESTING Function runs v1 source. Door is not on the stop list.

**Restore:** none (image stays). If push fails on Auth Token, **Blocked** — operator stores token; do not invent spend.

### S2-17 — Invoke Function with fake **ACTUAL** alert

**Runner:** `agent` · **Duration:** `wait` · **State:** VM1 RUNNING (so SoftStop is observable), door RUNNING, lock absent, idle irrelevant

**Steps**

1. Do **not** create a Budget alert.
2. Invoke with synthetic body (same shape as `functions/shutdown_vm/test_func.py` `ACTUAL`):
  `{"data":{"stateChange":{"current":{"triggeredAlertType":"ACTUAL"}}}}`
3. Wait VM1 **STOPPED** / STOPPING→STOPPED.
4. GET lock object — v1 JSON, `source=budget_function`.
5. GET door instance — **RUNNING**.
6. Confirm play IP can be reconciled (door up). Optional: park path.
7. DELETE lock when done (unless handing off to S3-01).

**Expected:** VM1 SoftStopped. Lock written. **Door not SoftStopped.** This is the $0 stand-in for “the spend-limit Function stops VM1 and sets flags.”

**Restore:** DELETE lock. START door if an old image took it down. Do not leave the lock in place overnight.

### S2-18 — Invoke Function with **RESET**

**Runner:** `agent` · **Duration:** `quick` · **State:** VM1 RUNNING, lock **absent**

**Steps:** Invoke `triggeredAlertType=RESET`. Wait ~30s. VM1 still RUNNING. Lock still absent.

**Expected:** `SKIPPED`. No SoftStop. No lock PUT. No lock DELETE (if a lock had been present, RESET must not clear it — optional extra: PUT lock, RESET, lock still present, then DELETE).

**Restore:** none.

### S2-19 — Daily budget MOTD ≠ spend-brake (optional)

**Runner:** `agent` · **Duration:** `wait`

**Steps:** If you can set a test daily cap via Object Storage budget config **without** stranding the operator, exhaust or fixture daily refuse. Compare MOTD/journal to S2-11. Restore budget config immediately.

**Expected:** Daily copy is not `MONTHLY SPEND BRAKE FIRED`. If too risky for the shared stack, **Skipped**.

**Restore:** Original budget config.

### S2-20 — Raw Compute start does not move play IP

**Runner:** `agent` · **Duration:** `wait` · **State:** VM1 STOPPED, IP on door

**Steps:** `oci compute instance action --action START` on VM1 (**not** door wake). Wait RUNNING. See who holds the reserved IP.

**Expected:** IP **stays on the door**. Friends would not follow. Documents why Advanced break-glass ≠ top-bar Start.

**Restore:** Either wake (move IP) or SoftStop VM1 again so the doorbell matches. Disable idle.

### S2-21 — Localhost RCON over SSH

**Runner:** `agent` · **Duration:** `quick` · **State:** Minecraft running

**Steps:** SSH tunnel or `sudo` RCON client on VM1 to `127.0.0.1:25575` — `list`. Do not open 25575 on SL.

**Expected:** Response. No SL change.

**Restore:** none.

### S2-22 — Lease heartbeat (Phase 5)

**Runner:** `agent` · **Duration:** `quick` · **State:** VM1 RUNNING with idle agent having ticked

**Steps:** `sudo cat /var/lib/mc-manager/lease.json` (path may match product).

**Expected:** Recent heartbeat while up. Used by door heal bound.

**Restore:** none.

### S2-28 — Minecraft start force-enables idle (by design)

**Runner:** `agent` · **Duration:** `quick`

**Steps:** Disable idle. `sudo systemctl start minecraft` (or restart). Recheck timer + config flag.

**Expected:** Idle **on** again (OS-ISSUE-7). **Pass** = documented safety, not a bug.

**Restore:** Disable idle again if more inspect tests remain.

### S2-26 — `reconcile_usage` dry-run (optional)

**Runner:** `agent` · **Duration:** `wait`

**Steps:** Prefer invoke `{"dry_run": true}` if the Function is deployed on TESTING. Do **not** run against Forge lab. Live PUT is optional; if you PUT, note ledger `revision` bump.

**Expected:** Dry-run returns a plan. Usage API is Always Free–eligible; still keep chatter modest.

**Restore:** If you wrote the ledger, say so in notes (no silent rewrite).

---



## S3 — Hybrid (agent stages, operator clicks)

Agent: put the fixture, watch OCI/SSH. Operator: Hybrid or Minecraft. Fill **both** notes.

### S3-01 — Spend-brake overlay + typed confirm

**Runner:** `hybrid` · **Duration:** `wait` · **State:** lock **present** (agent PUT or leftover from S2-17)

**Operator**

1. Open Manager (`dotnet run` Hybrid).
2. Confirm **full-window** warning (not a small banner). Start blocked.
3. Copy/paste the exact sentence (copy button OK):
  `I confirm that we have entered a new calendar month and that my free monthly usage limits have been reset. I understand that if I ignore these warnings and turn on my server before a new month has started, the card I created my Oracle Cloud account with will automatically be charged for the excess usage.`
4. Confirm (**Clear lock** — this does **not** Start). Overlay dismisses. Watch that VM1 stays down until you click top-bar **Start**.

**Agent**

- Before: lock GET exists.  
- After confirm: lock **DELETE**d (404). Play IP parked/reconciled. Door OS-refresh. VM1 **not** woken by the overlay. Idle/daily/monthly gates still apply (top-bar Start still refuses if those gates are exhausted).  
- Fail-closed: optional — break Get (hard); skip unless easy.

**Expected:** Overlay copy matches PRODUCT-IDEAS except confirm is **unlock only** (no overlay Start — follow-on P1; PRODUCT-IDEAS may still say overlay Start). Manager is the only clearer. No auto-clear at month rollover (do not wait a month — just confirm code/docs; UX has no “it’s a new month so skip typing”).

**Restore:** Lock absent. Idle as agreed.

### S3-02 — Top-bar Start (doorbell)

**Runner:** `hybrid` · **Duration:** `wait` · **State:** VM1 STOPPED, unlocked

**Operator:** Click **Start**. Status should show in-flight (Starting…) then **Running** when joinable.

**Agent:** VM1 RUNNING; play IP on VM1; 25565 listens. Start while already on stays disabled (E2E F5).

**Expected:** Door-aware Start, not raw Compute.

**Restore:** Disable idle after Minecraft start.

### S3-03 — Top-bar Stop

**Runner:** `hybrid` · **Duration:** `wait`

**Operator:** **Stop**. Must not hang forever on `POST /api/idle-empty` (DOOR-ISSUE-9).

**Agent:** VM1 STOPPED; IP on door; door listening.

**Expected:** Doorbell-aware Stop.

**Restore:** none.

### S3-04 — Allowlist Save → Security List

**Runner:** `hybrid` · **Duration:** `quick`

**Operator:** Whitelist add a **test** `/32` you control (or a harmless extra). **Save changes**. Then revert.

**Agent:** GetSecurityList — new 25565 rule with **name as description**; SSH rule only if Admin. No wipe of ICMP. Then confirm revert.

**Expected:** SL matches Desired List. CIDR on Minecraft only if you used Advanced prefix (see S4-08).

**Restore:** Original friends/SL.

### S3-05 — One real join

**Runner:** `hybrid` · **Duration:** `wait`

**Operator:** Minecraft Java, matching version/pack, play IP. Join once.

**Agent:** RCON `list` / `journalctl -u minecraft` shows the player. Do not open extra ports.

**Expected:** Join works when Status is Running. Modded: vanilla client must **fail** (that is Pass for “pack required”).

**Restore:** none.

### S3-06 — Oversized-world bell fixture

**Runner:** `hybrid` · **Duration:** `quick`

**Agent:** PUT DEBUG fixture `meta/oversized-world-backup.json` (or Advanced probe).

**Operator:** Bell warns. Server Management latest download copy mentions SSH live world, not OS PUT.

**Agent:** DELETE fixture after.

**Expected:** Bell + copy. No 9.5 GB upload attempted.

**Restore:** DELETE fixture.

### S3-07 — Wipe world (destructive to **live save only**)

**Runner:** `hybrid` · **Duration:** `wait` · **State:** VM1 RUNNING

**Operator:** Download a backup first if you care about the world. Server Management **Wipe world** + confirm.

**Agent:** Minecraft stopped then **started again**. `world` dir gone or empty under `/opt/mcmgr/server/<world>` only. `mods/`, `server.properties`, Object Storage `backups/` untouched.

**Expected:** Path guard holds. Minecraft is **running** after wipe; next join is a fresh world. If Wipe is used while the game VM is not RUNNING, the warning appears in the compact lower-right toast (**X** to dismiss), not only as grey tab text.

*(Pass 1 recorded leave-stopped. Operator 2026-08-19 overrode — [`V1-Bug-Fix-Plan-Pass-1.md`](V1-Bug-Fix-Plan-Pass-1.md) **P8**. PRODUCT-IDEAS Wipe world step 4 may still say next-Start.)*

**Restore:** Operator may restore from backup if they want the old world. Minecraft is **running** after wipe; Stop from Manager / idle when the session ends.

---



## S4 — Operator Manager UI

`dotnet run` Hybrid. Idle **off** if VM1 is up so the session is not stolen. Agent does not need to watch unless noted.

For each: click the path, then Result Pass or Fail with what you saw.

### S4-01 — Novice chrome

**Runner:** `operator` · **Duration:** `quick`

**Steps:** Top bar **Status** is Running/Stopped (not a raw OCI lifecycle dump). **Play IP** visible + copy. **Players** pin is `0` when Stopped and the RCON `list` count (`X / Y`) when Running. Native Windows title bar (no custom caption). No mini-terminal. Button results on manage tabs (Whitelist, Server Management, Advanced, Troubleshooting) appear in a dismissible **compact toast at the lower-right**, not only as grey text at the bottom of a scrolled tab.

**Expected:** Matches operator UI notes. Wipe-while-stopped warning is readable without scrolling Server Management. **X** dismisses the toast; short success fades. Setup wizard may keep an on-screen footer status.

### S4-02 — Tabs exist and split

**Runner:** `operator` · **Duration:** `quick`

**Steps:** Open Whitelist, Usage, Server Management, Console, Troubleshooting, Advanced. There is no separate **Danger Zone** tab.

**Expected:** Idle **enable/disable**, idle **timeout**, shape scale, and **Delete infrastructure** are under the **Advanced → Danger Zone** heading (typed-`confirm` still required for Delete). Troubleshooting is its own tab. Each tab remembers its vertical scroll when you switch away and back; unopened tabs start at the top.

### S4-03 — Whitelist add /32

**Runner:** `operator` · **Duration:** `quick`

**Steps:** Add a named `/32`. Save. Friend cannot join without an entry (private only). No public toggle, no blacklist panel.

**Expected:** Save updates cloud (or error is clear). No `ip/mode.json` public mode.

### S4-08 — CIDR prefix

**Runner:** `operator` · **Duration:** `quick`

**Steps:** Add IP → Advanced → a **tight** test prefix (not `/0`–`/8`). Confirm warning if wider than `/32`. Reject `/0`–`/8`.

**Expected:** Minecraft SL uses the CIDR; SSH/door stay `/32` except own admin row. IPv4 only.

**Restore:** Remove the test prefix.

### S4-09 — Usage tab

**Runner:** `operator` · **Duration:** `quick`

**Steps:** Refresh. Pinned cards vs tab. Expand **Detailed usage** (closed by default): one row per UTC day through today; day hours should match the month/today heroes well enough to trust. **Still on** appears on today when the server is running. 2/12 vs 4/24 copy: smaller shape calmer; 4/24 remaining-hours language. Publish/save if you change budgets — ETag conflict should tell you to refresh, not silently overwrite.

**Expected:** Hours display; no paid-mode UI; detailed day table matches heroes.

### S4-10 — Server Management backups

**Runner:** `operator` · **Duration:** `wait`

**Steps:** List backups. Download one small zip if any exist. Do not replace world unless you mean S7.

**Expected:** Download works. Soft cap copy exists.

### S4-11 — Modding panel

**Runner:** `operator` · **Duration:** `quick`

**Steps:** Vanilla/Paper: empty “not modded” note; **Change pack** is still offered. Modded: inspect `mods/` when VM1 up; **Download pack** is the **original** `data/imported-packs/` archive, not a zip of live `mods/`. Missing local archive disables download with reconstruct warning. **Change pack**: VM1 must be RUNNING; pick/drop `.mrpack` or server-pack zip; Setup analyze + two client-pack checkboxes; world kept unless wipe is checked; confirm reinstalls Minecraft. Not a catalog / per-mod IDE.

**Expected:** Matches Guide. Change pack Start-first when VM1 is down. Wipe optional; friends still need the new client pack. **Install** / **Cancel** and job progress (elapsed; indeterminate bar) live in the window-locked bottom dock, not only in the scrolling panel. Compact toasts stay for success/error, not the running Change pack job. Unacknowledged crash-quarantined mods (exactly one loader-blamed jar, moved to `mods.quarantined`) show **Keep excluded** / **Put back** on this panel.

### S4-12 — Name / icon / messages

**Runner:** `operator` · **Duration:** `quick`

**Steps:** Set a harmless description. Save. Restart Minecraft to apply the in-game name/icon. Optional PNG (any size; Manager fits 64×64). Setup also has a **Name and icon** page that seeds the same `messages/chat.json` (default icon if none chosen). Save also refreshes doorbell favicons (offline / starting / unavailable).

**Expected:** MOTD/list name and color icon update after restart while the game holds the play IP. Door-off list ping shows the greyscale+overlay idle (or starting/unavailable) favicon, not a solid color. Setup defaults are Vanilla/Paper/Modded Server (no Oracle™). Manager remains the day-2 editor.

**Restore:** Put the old name back if you care.

### S4-13 — Console tab

**Runner:** `operator` · **Duration:** `quick` · **State:** Minecraft running

**Steps:** Refresh logs. Send `list` (leading `/` optional). Not a PTY.

**Expected:** Default **Simple** view is readable (chat, joins, command transcript, spawn progress, errors) without RCON, modloader boot, or mixin refmap spam. **Full** shows unfiltered `journalctl` including those lines. Send `list` (leading `/` optional). RCON not on SL. Not a PTY.

### S4-14 — Troubleshooting one-shots

**Runner:** `operator` · **Duration:** `wait`

**Steps:** Each control asks confirm and shows a pasteable log. Prefer **Idle timer status** (read-only) first. **Park play IP** / **Refresh OS budget** are safe-ish. Skip **Reset door** / **Unstick** unless actually stuck. Skip **Heal ledger** unless VM1 is STOPPED.

**Expected:** Confirm gates. Logs useful. Do not disable $0 brakes from this tab.

### S4-15 — Advanced technical status

**Runner:** `operator` · **Duration:** `quick`

**Steps:** VM/door technical state visible here, not on novice Status. Deploy/repair + Auto-detect **button-gated** (no silent OCI probe on launch). Break-glass VM power labelled as not moving play IP.

**Expected:** Auto-detect does not run at every startup.

### S4-16 — Settings gear + overflow

**Runner:** `operator` · **Duration:** `quick`

**Steps:** Gear: paths + update-check **toggle** (check may be placeholder until 9.2). Menu: About, GitHub.

**Expected:** Native chrome. Toggle saves.

### S4-17 — Bell / notifications

**Runner:** `operator` · **Duration:** `quick`

**Steps:** Open bell. Empty state OK. Dismiss if an item exists.

**Expected:** List + dismiss. Session-only is OK.

### S4-18 — Danger Zone idle disable

**Runner:** `operator` · **Duration:** `quick`

**Steps:** On **Advanced → Danger Zone**, disable idle with strong confirm. Idle timeout is on that same heading (not higher on Advanced).

**Expected:** Strong warning. Boot/Minecraft start will turn it back on (tell testers).

**Restore:** Re-enable, or leave off for the rest of the UI session and say so.

### S4-19 — Shape scale UI (no apply required)

**Runner:** `operator` · **Duration:** `quick`

**Steps:** Danger Zone size picker. With VM1 **RUNNING**, control disabled. Do **not** apply a live resize unless doing S7-02.

**Expected:** STOPPED gate; 2/12 vs 4/24 only; playtime preview copy.

### S4-20 — Delete infrastructure dialog (do **not** delete)

**Runner:** `operator` · **Duration:** `quick`

**Steps:** Open dialog. Type `confirm` enables Delete. **Cancel / close without deleting.**

**Expected:** Typed confirm. Window would stay open until tofu finished **if** you deleted — you must not.

### S4-21 — No public / blacklist leftovers

**Runner:** `operator` · **Duration:** `quick`

**Steps:** Search UI for public server, blacklist, `0.0.0.0/0` as a user option.

**Expected:** None.

### S4-22 — Advanced raw start vs Start

**Runner:** `operator` · **Duration:** `quick`

**Steps:** Read break-glass labels. Do not prefer raw start for play (OS-ISSUE-3 / doorbell).

**Expected:** Copy warns play IP does not follow.

---



## S5 — Operator play path (Minecraft client)

Use the play IP. Matching version/pack. One wake-from-client if S2-08 already proved API wake.

### S5-01 — MOTD when VM1 stopped

**Runner:** `operator` · **Duration:** `quick` · **State:** IP on door, unlocked

**Steps:** Add server in Minecraft. Read MOTD (idle / always-on-capable copy if 2/12).

**Expected:** Door MOTD, not a generic timeout only.

### S5-02 — Wake from client connect

**Runner:** `operator` · **Duration:** `wait`

**Steps:** Connect while stopped. First attempt may miss custom kick (**DOOR-ISSUE-1** → `Known`). Second should show starting / try again, then join when Running.

**Expected:** Wake starts. Join when Status Running.

### S5-03 — Player present: idle does not SoftStop

**Runner:** `operator` · **Duration:** `wait`

**Steps:** Stay online > idle timeout (use **2 minute** timeout if agent set it; say which). Server stays up.

**Expected:** Occupied server is not SoftStopped.

**Restore:** Leave or Stop from Manager.

### S5-04 — Empty then idle (if not already S2-09)

**Runner:** `operator` · **Duration:** `wait`

**Steps:** Disconnect everyone. Wait timeout. Status Stopped. Next ping is door MOTD.

**Expected:** Same as S2-09 from the player’s view. Skip if S2-09 already Pass and you trust it.

### S5-05 — Daily exhausted copy (optional)

**Runner:** `operator` · **Duration:** `wait`

**Steps:** Only if you temporarily lower daily cap. Kick/MOTD is daily, **not** spend-brake. Restore cap.

**Expected:** Distinct strings. Skip if you do not want to touch budgets.

---



## S6 — Setup / Connect-existing

Do **not** `tofu apply` from these tests unless the operator authorizes it in that session. Prefer dry-run and Connect-existing.

### S6-01 — First-run / Setup pages (no Deploy)

**Runner:** `operator` · **Duration:** `wait`

**Steps:** If you can open Setup without destroying manage config: walk Always Free checkboxes ($1 residual honesty), profile picker, game Vanilla vs Modded, Paper vs Default Vanilla, **Name and icon** (defaults Vanilla/Paper/Modded Server; no Oracle™; optional 64×64 PNG), EULA link, Auth Token skip copy, shape 2/12 vs 4/24, admin `/32`. **No Compartment page** — Setup auto-names `mcmgr` (or `mcmgr-2` if taken). **Do not click Deploy** unless S7. If Setup already finished, reopen the last step from **Advanced → Deploy / repair** (no second Deploy) to check the finish page.

**Expected:** Copy matches Guide. Pages are short; extra Always Free / EULA / pack copy is on info-icon hover. Name and icon sits after Minecraft; changing game type updates the default name until you edit it. Back/Deploy lock behavior is described (cannot verify without Deploy). While Deploy runs, percent, elapsed time, and a **humanized** status live in the **bottom dock** (same bar as Back/Deploy/Close) — never a raw `> rm -rf …` line. The detailed log stays on the page and is the tall viewport (review form and plan hidden). Modded: file picker/drop only; client-pack checkboxes; Quilt cannot continue; CurseForge **client** export refused. After a successful Deploy (or resume of a finished wizard): heading **Deployment Complete**, reserved play IP with **Copy**, Close to continue to Manager; deploy log may be collapsed below.

### S6-02 — Modded analyze (local file)

**Runner:** `operator` · **Duration:** `quick`

**Steps:** Use gitignored `data/sample-packs/` or fixtures. Analyze `.mrpack` and a server-pack zip. Drag-and-drop if you can.

**Expected:** Summary (name, MC, loader, Java, strip counts). No catalog search. User-made / jar-root / filled Server Files zips with jars that have no side metadata: **continue** with a summary warning (jars kept after exclude lists). **Jar-root / unstructured zips** also show **editable** Minecraft version, loader, loader version, and Java; after you confirm, Manager writes a derived archive for install and **Download pack** (original upload file is not overwritten). Modrinth **`.mrpack`** with unclear `env.server` still **blocked**. CurseForge client export / jar-less / mixed ID-only still **blocked**.

### S6-03 — Connect-existing / version skew

**Runner:** `operator` · **Duration:** `quick`

**Steps:** Advanced Auto-detect (button). Read schema/version warnings. Do not need a second PC — read the confirm summary.

**Expected:** Newer stack than app **refuses**. Older/legacy extra-confirms. No silent probe on launch.

### S6-04 — Deploy / repair resume (no second apply)

**Runner:** `operator` · **Duration:** `quick`

**Steps:** If `apply_stage=tofu_applied`, Advanced **Deploy / repair** is the resume path (SETUP-ISSUE-5). Do not start a second Deploy on a finished page.

**Expected:** Resume does not `tofu apply` again when apply already succeeded.

### S6-05 — Wizard dry-run (agent OK)

**Runner:** `agent` · **Duration:** `quick`

**Steps:** `MCMANAGER_TOFU_DRY_RUN=1` wizard Deploy if documented. No live apply.

**Expected:** Fake runner; both vanilla flavors appear in plan if that path exists.

---



## S7 — Destructive (last)

Skip any row you are not willing to restore. **S7-04 requires an explicit operator “you may tofu destroy/apply” in that chat** even after Function blanket permission.

### S7-02 — VM1 shape scale live (optional, $0)

**Runner:** `hybrid` · **Duration:** `wait` · **State:** VM1 **STOPPED**

**Steps:** Danger Zone switch 4/24 ↔ 2/12. Agent confirms instance shape + local config + budget/meta. Then **switch back** to the original.

**Expected:** Always Free Flex only. Ledger **past** intervals keep old shape. MOTD copy follows 7.2 (calmer on 2/12).

**Restore:** Original shape.

### S7-03 — World replace from backup (optional)

**Runner:** `operator` · **Duration:** `wait`

**Steps:** Upload/replace live world from a zip you own. VM1 running; Minecraft stopped during replace as designed.

**Expected:** World replaced; not a full wipe of mods.

### S7-04 — Delete + greenfield (optional, operator-authorized tofu)

**Runner:** `operator` · **Duration:** `destructive`

**Steps:** Only with explicit **tofu destroy + apply** in the session (Pass 2 Phase A prompt is that authorization). Danger Zone Delete (`confirm`). Then Setup Deploy on TESTING — **Modded**, one sample pack ([`V1-QA-Pass-2-Scope.md`](V1-QA-Pass-2-Scope.md)). Never `DEFAULT` / Forge lab. Never a second A1 that would exceed Always Free. Destroy **first**.

**Expected:** Same bar as MVP 7.2 E2E: playable doorbell, idle, private SL. New ledger starts at zero; Oracle monthly hours do **not** reset (Guide).

**Restore:** This **is** a new stack. Do not run this to “save time.”

---



## S8 — Known-issue checks

Mark `Known` or `Pass` (fixed/not seen). Do not file duplicates.


| ID    | Check                                                                   |
| ----- | ----------------------------------------------------------------------- |
| S8-01 | DOOR-ISSUE-1 first-kick still parked?                                   |
| S8-02 | After S2-16/17, FN-ISSUE-1 gone on **TESTING**?                         |
| S8-03 | OS-ISSUE-7 documented in Danger Zone / Guide?                           |
| S8-04 | SETUP-ISSUE-7 firewalld still OK after a SoftStop reboot (S2-05/S2-09)? |


---



## Additional problems (pass results file)

The results file ends with a freeform section. Operators should add anything **not** tied to an ID: performance, confusing copy, “I clicked X and Y happened,” questions about intended behavior. Agents must **not** treat questions as bugs until triage.