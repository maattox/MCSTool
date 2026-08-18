"""Unit tests for the $1 Function event policy and lock JSON (no OCI / FDK)."""

import datetime
import json
import os
import unittest

import func


RESET = (
    b'{"data":{"stateChange":{"current":{"triggeredAlertType":"RESET"}}}}'
)
ACTUAL = (
    b'{"data":{"stateChange":{"current":{"triggeredAlertType":"ACTUAL"}}}}'
)
FORECAST = (
    b'{"data":{"stateChange":{"current":{"triggeredAlertType":"FORECAST"}}}}'
)


class EventPolicyTests(unittest.TestCase):
    def test_reset_is_skipped(self):
        alert = func.parse_triggered_alert_type(RESET)
        self.assertEqual("RESET", alert)
        self.assertTrue(func.is_reset_alert(alert))

    def test_actual_is_not_skipped(self):
        alert = func.parse_triggered_alert_type(ACTUAL)
        self.assertEqual("ACTUAL", alert)
        self.assertFalse(func.is_reset_alert(alert))

    def test_forecast_is_not_skipped(self):
        alert = func.parse_triggered_alert_type(FORECAST)
        self.assertEqual("FORECAST", alert)
        self.assertFalse(func.is_reset_alert(alert))

    def test_empty_and_malformed_are_not_reset(self):
        self.assertIsNone(func.parse_triggered_alert_type(None))
        self.assertIsNone(func.parse_triggered_alert_type(b""))
        with self.assertRaises(json.JSONDecodeError):
            func.parse_triggered_alert_type(b"not-json")
        self.assertFalse(func.is_reset_alert(None))

    def test_lock_json_matches_v1_contract(self):
        now = datetime.datetime(2026, 8, 17, 21, 0, 0, tzinfo=datetime.timezone.utc)
        doc = func.build_lock_document("ACTUAL", now_utc=now)
        self.assertEqual(
            {
                "version": 1,
                "triggered_at": "2026-08-17T21:00:00Z",
                "updated_at": "2026-08-17T21:00:00Z",
                "source": "budget_function",
                "reason": "compartment_budget_threshold",
                "alert_type": "ACTUAL",
            },
            doc,
        )

    def test_lock_omits_alert_type_when_missing(self):
        doc = func.build_lock_document(None)
        self.assertNotIn("alert_type", doc)
        self.assertEqual("budget_function", doc["source"])

    def test_reset_must_not_build_a_cleared_status(self):
        # Unlocked = object absent. Function never writes status=cleared.
        doc = func.build_lock_document("ACTUAL")
        self.assertNotIn("status", doc)


class ConfigResolveTests(unittest.TestCase):
    def test_env_instance_ocids_win(self):
        env = {"INSTANCE_OCIDS": "ocid1.instance.oc1..vm1, ocid1.instance.oc1..extra"}
        self.assertEqual(
            ["ocid1.instance.oc1..vm1", "ocid1.instance.oc1..extra"],
            func.resolve_instance_ocids(env=env, baked=["<VM1_INSTANCE_OCID>"]),
        )

    def test_placeholders_are_not_stopped(self):
        self.assertEqual(
            [],
            func.resolve_instance_ocids(
                env={}, baked=["<VM1_INSTANCE_OCID>", "<VM2_INSTANCE_OCID>"]
            ),
        )

    def test_os_config_defaults_lock_key(self):
        ns, bucket, obj = func.resolve_os_config(
            env={"OS_NAMESPACE": "ns", "OS_BUCKET": "mcmgr-shared-data"}
        )
        self.assertEqual("ns", ns)
        self.assertEqual("mcmgr-shared-data", bucket)
        self.assertEqual("meta/spend-brake-triggered.json", obj)

    def test_placeholder_os_env_is_unset(self):
        ns, bucket, obj = func.resolve_os_config(
            env={
                "OS_NAMESPACE": "<OBJECT_STORAGE_NAMESPACE>",
                "OS_BUCKET": "<OBJECT_STORAGE_BUCKET>",
            }
        )
        self.assertEqual("", ns)
        self.assertEqual("", bucket)
        self.assertEqual("meta/spend-brake-triggered.json", obj)


if __name__ == "__main__":
    unittest.main()
