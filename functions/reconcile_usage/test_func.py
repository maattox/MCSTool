"""Unit tests for Usage API 48h ledger reconcile (no OCI / FDK)."""

import datetime
import json
import unittest

import func

NOW = datetime.datetime(2026, 8, 18, 18, 0, 0, tzinfo=datetime.timezone.utc)
# 48h before NOW is 2026-08-16 18:00 → last fully-old day ends 2026-08-16 00:00 (the 15th).
DAY_OLD = "2026-08-15"
DAY_RECENT = "2026-08-17"

A1_OCPU = {
    "time_usage_started": "2026-08-15T00:00:00Z",
    "time_usage_ended": "2026-08-16T00:00:00Z",
    "sku_name": "Oracle Cloud Infrastructure - Compute - Ampere A1 - OCPU",
    "sku_part_number": "B93113",
    "unit": "OCPU Hours",
    "shape": "VM.Standard.A1.Flex",
    "resource_id": "ocid1.instance.oc1..vm1",
    "computed_quantity": 8.0,
    "is_forecast": False,
}
A1_MEMORY = {
    "time_usage_started": "2026-08-15T00:00:00Z",
    "time_usage_ended": "2026-08-16T00:00:00Z",
    "sku_name": "Oracle Cloud Infrastructure - Compute - Ampere A1 - Memory",
    "sku_part_number": "B93114",
    "unit": "GB Hours",
    "shape": "VM.Standard.A1.Flex",
    "resource_id": "ocid1.instance.oc1..vm1",
    "computed_quantity": 48.0,
    "is_forecast": False,
}
AMD_MICRO = {
    "time_usage_started": "2026-08-15T00:00:00Z",
    "sku_name": "Oracle Cloud Infrastructure - Compute - Standard - E2 Micro",
    "sku_part_number": "B88317",
    "unit": "OCPU Hours",
    "shape": "VM.Standard.E2.1.Micro",
    "resource_id": "ocid1.instance.oc1..door",
    "computed_quantity": 24.0,
    "is_forecast": False,
}


def _ledger(**kwargs):
    doc = {
        "version": 2,
        "revision": 10,
        "intervals": [
            {
                "id": "11111111-1111-1111-1111-111111111111",
                "started_at": "2026-08-15T00:00:00Z",
                "stopped_at": "2026-08-15T02:00:00Z",
                "ocpus": 4.0,
                "memory_gb": 24.0,
                "source": "boot",
                "stop_source": "idle_or_budget_stop",
                "stop_uncertain": False,
                "uncertain_reason": "keep-me",
            }
        ],
        "daily_overrides": {},
        "idle_since": None,
        "last_budget_warn_at": None,
    }
    doc.update(kwargs)
    return doc


class SkuClassifyTests(unittest.TestCase):
    def test_ampere_a1_ocpu_and_memory(self):
        self.assertEqual("ocpu", func.classify_usage_row(A1_OCPU))
        self.assertEqual("memory", func.classify_usage_row(A1_MEMORY))

    def test_rejects_amd_micro_and_object_storage(self):
        self.assertIsNone(func.classify_usage_row(AMD_MICRO))
        self.assertIsNone(
            func.classify_usage_row(
                {
                    "sku_name": "Oracle Cloud Infrastructure - Object Storage - Storage",
                    "computed_quantity": 1.0,
                }
            )
        )


class EligibilityTests(unittest.TestCase):
    def test_last_eligible_end_is_midnight_before_cutoff(self):
        end = func.last_eligible_day_end(NOW, age_hours=48)
        self.assertEqual(
            datetime.datetime(2026, 8, 16, 0, 0, tzinfo=datetime.timezone.utc),
            end,
        )

    def test_exact_midnight_cutoff_includes_that_day_end(self):
        now = datetime.datetime(2026, 8, 17, 0, 0, tzinfo=datetime.timezone.utc)
        # 48h earlier is 2026-08-15 00:00 exactly → last end is that stamp (the 14th).
        end = func.last_eligible_day_end(now, age_hours=48)
        self.assertEqual(now - datetime.timedelta(hours=48), end)

    def test_eligible_keys_include_old_not_recent(self):
        keys, _, last_end = func.eligible_day_keys(NOW, age_hours=48, lookback_days=4)
        self.assertIn(DAY_OLD, keys)
        self.assertNotIn(DAY_RECENT, keys)
        self.assertNotIn("2026-08-16", keys)
        self.assertEqual(
            datetime.datetime(2026, 8, 16, 0, 0, tzinfo=datetime.timezone.utc),
            last_end,
        )


class FoldItemsTests(unittest.TestCase):
    def test_sums_a1_and_skips_micro_forecast_and_other_vm(self):
        forecast = dict(A1_OCPU)
        forecast["is_forecast"] = True
        other_vm = dict(A1_OCPU)
        other_vm["resource_id"] = "ocid1.instance.oc1..other"
        other_vm["computed_quantity"] = 99.0
        folded = func.fold_usage_items(
            [A1_OCPU, A1_MEMORY, AMD_MICRO, forecast, other_vm],
            vm1_instance_ocid="ocid1.instance.oc1..vm1",
        )
        self.assertEqual(
            {DAY_OLD: {"ocpu_hours": 8.0, "gb_hours": 48.0}},
            folded,
        )

    def test_keeps_rows_without_resource_id_when_filter_set(self):
        row = dict(A1_OCPU)
        row["resource_id"] = ""
        folded = func.fold_usage_items(
            [row], vm1_instance_ocid="ocid1.instance.oc1..vm1"
        )
        self.assertEqual(8.0, folded[DAY_OLD]["ocpu_hours"])


class ReconcileTests(unittest.TestCase):
    def test_writes_override_when_api_differs_and_bumps_revision(self):
        ledger = _ledger()
        api = {DAY_OLD: {"ocpu_hours": 10.0, "gb_hours": 60.0}}
        updated, changes, wrote = func.apply_reconcile(ledger, api, NOW)
        self.assertTrue(wrote)
        self.assertEqual(11, updated["revision"])
        ov = updated["daily_overrides"][DAY_OLD]
        self.assertEqual("usage_api_reconcile", ov["note"])
        self.assertEqual(10.0, ov["ocpu_hours"])
        self.assertEqual(60.0, ov["gb_hours"])
        self.assertAlmostEqual(2.5, ov["uptime_hours"])
        self.assertEqual("wrote", changes[0]["action"])
        # Intervals and extra fields stay put.
        self.assertEqual("keep-me", updated["intervals"][0]["uncertain_reason"])
        self.assertEqual(ledger["intervals"], updated["intervals"])

    def test_skips_when_api_matches_intervals(self):
        ledger = _ledger()
        # Interval is 2h * 4 OCPU = 8, 2h * 24 GB = 48 — same as API.
        api = {DAY_OLD: {"ocpu_hours": 8.0, "gb_hours": 48.0}}
        # Wait: the test above writes because... 2h*4=8, 2h*24=48. That MATCHES.
        # The first test expected a write. I need a mismatch for write.
        # Keep this as the match case; fix the first test to use different API numbers.
        updated, changes, wrote = func.apply_reconcile(ledger, api, NOW)
        self.assertFalse(wrote)
        self.assertEqual(10, updated["revision"])
        self.assertEqual({}, updated["daily_overrides"])
        self.assertEqual("matched_intervals", changes[0]["action"])

    def test_writes_when_api_finds_hours_intervals_missed(self):
        ledger = _ledger(intervals=[])
        api = {DAY_OLD: {"ocpu_hours": 4.0, "gb_hours": 24.0}}
        updated, changes, wrote = func.apply_reconcile(ledger, api, NOW)
        self.assertTrue(wrote)
        self.assertEqual("wrote", changes[0]["action"])
        self.assertEqual(4.0, updated["daily_overrides"][DAY_OLD]["ocpu_hours"])

    def test_does_not_zero_out_interval_hours_when_api_missing(self):
        ledger = _ledger()
        updated, changes, wrote = func.apply_reconcile(
            ledger, {DAY_OLD: {"ocpu_hours": 0.0, "gb_hours": 0.0}}, NOW
        )
        self.assertFalse(wrote)
        self.assertEqual({}, updated["daily_overrides"])
        self.assertEqual("skipped_no_api", changes[0]["action"])

    def test_preserves_manual_override(self):
        ledger = _ledger(
            daily_overrides={
                DAY_OLD: {
                    "uptime_hours": 1.5,
                    "ocpu_hours": 6.0,
                    "gb_hours": 36.0,
                    "note": "manual correction",
                }
            }
        )
        api = {DAY_OLD: {"ocpu_hours": 99.0, "gb_hours": 99.0}}
        updated, changes, wrote = func.apply_reconcile(ledger, api, NOW)
        self.assertFalse(wrote)
        self.assertEqual("preserved_manual", changes[0]["action"])
        self.assertEqual(6.0, updated["daily_overrides"][DAY_OLD]["ocpu_hours"])

    def test_refreshes_previous_usage_api_override(self):
        ledger = _ledger(
            daily_overrides={
                DAY_OLD: {
                    "uptime_hours": 1.0,
                    "ocpu_hours": 4.0,
                    "gb_hours": 24.0,
                    "note": "usage_api_reconcile",
                    "updated_at": "2026-08-16T00:00:00Z",
                }
            }
        )
        api = {DAY_OLD: {"ocpu_hours": 12.0, "gb_hours": 72.0}}
        updated, changes, wrote = func.apply_reconcile(ledger, api, NOW)
        self.assertTrue(wrote)
        self.assertEqual(12.0, updated["daily_overrides"][DAY_OLD]["ocpu_hours"])
        self.assertEqual("usage_api_reconcile", updated["daily_overrides"][DAY_OLD]["note"])

    def test_skips_days_newer_than_48h(self):
        ledger = _ledger()
        api = {
            DAY_RECENT: {"ocpu_hours": 4.0, "gb_hours": 24.0},
        }
        updated, changes, wrote = func.apply_reconcile(ledger, api, NOW)
        self.assertFalse(wrote)
        self.assertEqual("skipped_too_recent", changes[0]["action"])
        self.assertEqual({}, updated["daily_overrides"])


class FlagsAndConfigTests(unittest.TestCase):
    def test_dirties_all_ledger_consumers_and_keeps_other_categories(self):
        flags = func.empty_flags(NOW)
        flags["categories"]["budget"]["door"] = True
        flags["help"] = "keep"
        out = func.dirty_ledger_consumers(flags, NOW)
        self.assertTrue(out["categories"]["ledger"]["manager"])
        self.assertTrue(out["categories"]["ledger"]["door"])
        self.assertTrue(out["categories"]["ledger"]["vm1"])
        self.assertTrue(out["categories"]["budget"]["door"])
        self.assertFalse(out["categories"]["budget"]["manager"])
        self.assertEqual("keep", out["help"])

    def test_placeholder_env_is_unset(self):
        ns, bucket, ledger, flags = func.resolve_os_config(
            env={
                "OS_NAMESPACE": "<OBJECT_STORAGE_NAMESPACE>",
                "OS_BUCKET": "<OBJECT_STORAGE_BUCKET>",
            }
        )
        self.assertEqual("", ns)
        self.assertEqual("", bucket)
        self.assertEqual("ledger/usage.json", ledger)
        self.assertEqual("meta/flags.json", flags)

    def test_env_ocids_win(self):
        tenancy, compartment, vm1, age = func.resolve_usage_config(
            env={
                "TENANCY_OCID": "ocid1.tenancy.oc1..test",
                "COMPARTMENT_OCID": "ocid1.compartment.oc1..mcmgr",
                "VM1_INSTANCE_OCID": "ocid1.instance.oc1..vm1",
                "AGE_HOURS": "72",
            }
        )
        self.assertEqual("ocid1.tenancy.oc1..test", tenancy)
        self.assertEqual("ocid1.compartment.oc1..mcmgr", compartment)
        self.assertEqual("ocid1.instance.oc1..vm1", vm1)
        self.assertEqual(72.0, age)

    def test_dry_run_body(self):
        self.assertTrue(func.parse_invoke_body(b'{"dry_run": true}')["dry_run"])
        self.assertFalse(func.parse_invoke_body(b"{}")["dry_run"])
        self.assertFalse(func.parse_invoke_body(None)["dry_run"])


class IntervalTotalsTests(unittest.TestCase):
    def test_clips_open_interval_to_the_utc_day(self):
        ledger = _ledger(
            intervals=[
                {
                    "id": "open",
                    "started_at": "2026-08-15T22:00:00Z",
                    "stopped_at": None,
                    "ocpus": 4.0,
                    "memory_gb": 24.0,
                    "source": "boot",
                }
            ]
        )
        tot = func.interval_day_totals(ledger, DAY_OLD, NOW)
        self.assertAlmostEqual(2.0, tot["uptime_hours"])
        self.assertAlmostEqual(8.0, tot["ocpu_hours"])


if __name__ == "__main__":
    unittest.main()
