const doorEl = document.getElementById("door");
const remainingEl = document.getElementById("remaining");
const usedEl = document.getElementById("used");
const resetEl = document.getElementById("reset");
const keepaliveEl = document.getElementById("keepalive");
const bannerEl = document.getElementById("exhausted-banner");
const wakeBtn = document.getElementById("wake-btn");
const actionMsg = document.getElementById("action-msg");
const idleForm = document.getElementById("idle-form");
const idleInput = document.getElementById("idle-minutes");

async function api(path, options = {}) {
  const res = await fetch(path, {
    headers: { "Content-Type": "application/json" },
    ...options,
  });
  const text = await res.text();
  let body = {};
  try {
    body = text ? JSON.parse(text) : {};
  } catch (_) {
    body = { raw: text };
  }
  return { ok: res.ok, status: res.status, body };
}

function formatReset(iso) {
  if (!iso) return "—";
  try {
    const d = new Date(iso);
    return d.toLocaleString(undefined, { timeZoneName: "short" });
  } catch (_) {
    return iso;
  }
}

function renderStatus(s) {
  doorEl.textContent = s.door || "—";
  const rem = s.remaining_ocpu_hours ?? 0;
  const used = s.used_ocpu_hours ?? 0;
  const limit = s.daily_limit_ocpu_hours ?? 45;
  remainingEl.textContent = `${rem.toFixed(1)} OCPU-h`;
  usedEl.textContent = `${used.toFixed(1)} / ${limit.toFixed(0)} OCPU-h used`;
  resetEl.textContent = formatReset(s.reset_at_utc);
  const kaOn = s.keepalive_enabled ? "enabled" : "disabled";
  const lastKa = s.last_keepalive_at ? formatReset(s.last_keepalive_at) : "never";
  const nextKa = s.next_keepalive_at ? formatReset(s.next_keepalive_at) : "—";
  keepaliveEl.textContent = `${kaOn}; last ${lastKa}; next ${nextKa}`;

  const spendBrake = s.door === "SPEND_BRAKE";
  const exhausted = s.door === "BUDGET_EXHAUSTED" || rem <= 0.0001;
  bannerEl.classList.toggle("hidden", !(exhausted || spendBrake));
  bannerEl.textContent = spendBrake
    ? "Monthly spend brake fired — wake blocked until the admin uses Manager after a new calendar month."
    : "Daily budget exhausted — wake blocked until reset.";
  const waking = s.door === "STARTING" || s.wake_in_progress;
  wakeBtn.disabled = exhausted || spendBrake || waking || s.door === "PLAYABLE";
}

async function refresh() {
  try {
    const { ok, body } = await api("/api/status");
    if (!ok) throw new Error(body.error || "status failed");
    renderStatus(body);
    if (body.idle_timeout_minutes) {
      idleInput.value = body.idle_timeout_minutes;
    }
  } catch (err) {
    actionMsg.textContent = String(err);
  }
}

wakeBtn.addEventListener("click", async () => {
  actionMsg.textContent = "Starting wake…";
  const { ok, status, body } = await api("/api/wake", { method: "POST", body: "{}" });
  actionMsg.textContent = ok
    ? "Wake accepted — polling status…"
    : `Wake failed (${status}): ${body.error || "unknown"}`;
  await refresh();
});

idleForm.addEventListener("submit", async (e) => {
  e.preventDefault();
  const minutes = Number(idleInput.value);
  const { ok, body } = await api("/api/config/idle", {
    method: "POST",
    body: JSON.stringify({ idle_timeout_minutes: minutes }),
  });
  actionMsg.textContent = ok ? `Idle timeout set to ${minutes} min` : (body.error || "save failed");
  await refresh();
});

refresh();
setInterval(refresh, 5000);
