# OCI API usage (developers & agents)

**Audience:** anyone writing Manager / Setup code that calls Oracle Cloud Infrastructure APIs (.NET OCI SDK, or raw REST).  
**Authority:** Oracle’s [REST APIs / Using the API](https://docs.oracle.com/en-us/iaas/Content/API/Concepts/usingapi.htm) (read that page; this file is a product-oriented digest + Always Free constraints).  
**Cost rule:** stay inside **Always Free–eligible** usage. Chatty or naïve polling can burn the Object Storage **~50,000 API requests/month** free allowance and trigger **429** throttling on other services.

Lab copy (same guidance): sibling `OCI-mc-server-manager/docs/OCI-API-Usage.md`. Product intent / sync intervals: lab `PRODUCT-IDEAS.md`.

---

## Basics (from Oracle)

| Rule | Detail |
|------|--------|
| **HTTPS + TLS 1.2** | All API calls must use HTTPS / TLS 1.2. |
| **Request signing** | Every request must be signed. Prefer the **official .NET OCI SDK** in `McManager.Core` so signing is correct. Credentials: `%USERPROFILE%\.oci\config` + PEM (`LocalConfigStore` / `OciSession`). |
| **API version in path** | Regional endpoints include a dated API version (e.g. `/20160918/...`). SDKs handle this; do not invent paths. |
| **JSON Content-Type** | POST/PUT with a JSON body must set `Content-Type: application/json`. |
| **Clock skew** | If the client clock is skewed **> 5 minutes** from OCI, expect **401 NotAuthenticated**. Fix OS time sync before debugging keys. Check server time: `curl -s --head https://iaas.<region>.oraclecloud.com \| grep Date`. |
| **opc-request-id** | Every response includes `opc-request-id`. Log it on failures; Oracle support needs it. |
| **Errors** | 4xx client / 5xx server; body JSON has `code` + `message`. See [API Errors](https://docs.oracle.com/en-us/iaas/Content/API/References/apierrors.htm). |
| **Breaking changes** | Oracle gives **12 months** notice before removing/changing a deployed API in a way that requires code updates. |

Tenancy OCID is required for signing and some IAM ops. Keep it in gitignored `data/config.local.json` (`oci.tenancy_id`).

---

## Request throttling (429)

| Item | Guidance |
|------|----------|
| **HTTP** | **429** |
| **Body** | `"code": "TooManyRequests"`, message like `User-rate limit exceeded.` |
| **Retry** | Oracle recommends **exponential back-off**, starting from a **few seconds**, up to a **maximum of 60 seconds**. |
| **SDK** | Prefer SDK **retry** configuration that retries 429 with backoff/jitter. Do not tight-loop the same call. |
| **UI** | Surface “OCI rate limit — retrying…”; never hammer from a refresh button or timer. |

Agents: on 429, **backoff** — do not add parallel OCI storms.

---

## Polling for resource status (lifecycles)

After Start/Stop/Create, wait until `lifecycleState` (or equivalent) reaches the desired value, or time out.

Oracle **SDK waiters** default strategy (match this in Core helpers):

1. **Exponential back-off** between polls: a **few seconds** → max **30 seconds**.  
2. Poll up to **~20 minutes**, then fail clearly.

**Do not** poll `GetInstance` every 1–2 seconds for long periods.

### Product polling intervals (Always Free–aware)

| Situation | Preferred approach |
|-----------|-------------------|
| Wait for VM1 `RUNNING` / `STOPPED` | Waiter / exponential backoff (few sec → ≤30s); timeout ~20 min |
| Top-bar status while focused | **15–60s**; slow or pause when minimized / unfocused |
| Usage tab open | ~**2 min**; refresh on open; stop when leaving |
| Setup capacity wait | **5 min** auto-retry with consent; **`CreateComputeCapacityReport` before apply** (not a 1s loop); persist resume |
| Door-aware power | Prefer door HTTP for play path; OCI GetInstance as secondary — don’t double-poll aggressively |
| Security List after sync | One write (+ optional single re-GET); no tight list loop |
| Object Storage flags / ledger | On demand / on open / slow timer |

**Prefer Get-by-OCID** from `config.local.json` / `meta/infra.json` over repeated **List*** discovery.

---

## List pagination

- Follow **`opc-next-page`** until absent. Empty pages can still mean more results.  
- Object Storage **ListObjects** uses **`nextStartWith`** + **`start`** (not `opc-next-page`).  
- Respect **`limit`**; never assume a single page lists all backups.

---

## Retry tokens & ETags

| Mechanism | Use |
|-----------|-----|
| **`opc-retry-token`** | Supported creates: avoid double-create on timeout/retry (token ~24h). Important for Setup. |
| **ETags / `if-match`** | Optimistic concurrency on update/delete (e.g. Security List) when the SDK exposes it. |

Optional string params: prefer **omit/null** over `""` (empty string still validates and often fails min length).

---

## Always Free / $0 constraints

1. Object Storage **~50k API requests/month** — design for dirty flags / versions, not constant GetObject.  
2. Soft-cap **~9.5 GiB** backups (capacity) — separate from request count.  
3. No chatty background OCI when the UI is idle.  
4. Cache OCIDs locally; auto-detect is **button-gated**.  
5. Security List updates: **one** full desired-state write (preserve non-owned rules).  
6. Do not add paid monitoring just to debug API calls.

---

## Implementation checklist (`McManager.Core`)

- [ ] SDK session from `~/.oci` via local config  
- [ ] Retry **429** with exponential backoff (≤60s)  
- [ ] Lifecycle waits: waiter-style (≤30s between polls, ~20 min timeout)  
- [ ] List pagination complete  
- [ ] `opc-retry-token` on sensitive creates where supported  
- [x] ETag / if-match when concurrent updates matter (Manager budget, meta, allowlist — V1 Step 7.4)  
- [ ] Log **`opc-request-id`** on failures  
- [ ] UI poll intervals match the table above  

---

## References

- [Using the API (REST APIs)](https://docs.oracle.com/en-us/iaas/Content/API/Concepts/usingapi.htm)  
- [API Errors](https://docs.oracle.com/en-us/iaas/Content/API/References/apierrors.htm)  
- [API Reference and Endpoints](https://docs.oracle.com/en-us/iaas/api/)  
- [Request Signatures](https://docs.oracle.com/en-us/iaas/Content/API/Concepts/signingrequests.htm)  

---

## Changelog

| Date | Note |
|------|------|
| 2026-08-18 | Manager Object Storage writes for `budget/config.json`, `meta/infra.json`, `meta/flags.json` (those publishes), and `ip/allowlist.json` send `If-Match`. 412 → refresh-and-retry. |
| 2026-08-11 | Initial guide aligned with lab doc + Oracle Using the API. |
