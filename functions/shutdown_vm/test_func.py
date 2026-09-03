"""Unit tests for the $1 Function event policy and lock JSON (no OCI / FDK)."""

import datetime
import json
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

# Oracle Events reference payload (docs.oracle.com Events producers).
OFFICIAL_TRIGGERED_ALERT = json.dumps(
    {
        "eventType": "com.oraclecloud.budgets.createtriggeredalert",
        "cloudEventsVersion": "0.1",
        "eventTypeVersion": "2.0",
        "source": "budgets",
        "eventID": "unique-id",
        "eventTime": "2026-09-01T00:55:00.000Z",
        "contentType": "application/json",
        "data": {
            "eventName": "CreateTriggeredAlert",
            "compartmentId": "ocid1.tenancy.oc1..example",
            "compartmentName": "example_compartment",
            "resourceId": "ocid1.triggeredalert.oc1.phx.example",
            "availabilityDomain": "availability_domain",
            "additionalDetails": {
                "budgetId": "ocid1.budget.oc1.phx.examplebudgetid",
                "alertRuleId": "ocid1.alertrule.oc1.phx.examplealert",
            },
        },
        "extensions": {"compartmentId": "ocid1.tenancy.oc1..example"},
    }
).encode("utf-8")


class EventPolicyTests(unittest.TestCase):
    def test_reset_is_skipped(self):
        alert = func.parse_triggered_alert_type(RESET)
        self.assertEqual("RESET", alert)
        self.assertTrue(func.is_reset_alert(alert))
        self.assertEqual(
            ("SKIP", "alert_type=RESET"),
            func.decide_spend_brake_action(alert, None),
        )

    def test_actual_acts_when_spend_unknown(self):
        alert = func.parse_triggered_alert_type(ACTUAL)
        self.assertEqual("ACTUAL", alert)
        self.assertFalse(func.is_reset_alert(alert))
        self.assertEqual(
            ("ACT", "actual_alert_spend_unknown"),
            func.decide_spend_brake_action(alert, None),
        )

    def test_forecast_is_skipped(self):
        alert = func.parse_triggered_alert_type(FORECAST)
        self.assertEqual("FORECAST", alert)
        self.assertEqual(
            ("SKIP", "alert_type=FORECAST"),
            func.decide_spend_brake_action(alert, True),
        )

    def test_official_reset_payload_has_no_alert_type(self):
        parsed = func.parse_event(OFFICIAL_TRIGGERED_ALERT)
        self.assertIsNone(parsed["alert_type"])
        self.assertTrue(parsed["budget_id"].startswith("ocid1.budget."))
        self.assertEqual(
            ("SKIP", "actual_spend_below_threshold"),
            func.decide_spend_brake_action(parsed["alert_type"], False),
        )
        self.assertEqual(
            ("SKIP", "unconfirmed_alert"),
            func.decide_spend_brake_action(parsed["alert_type"], None),
        )
        self.assertEqual(
            ("ACT", "actual_spend_reached_threshold"),
            func.decide_spend_brake_action(parsed["alert_type"], True),
        )

    def test_empty_and_malformed_are_unconfirmed(self):
        self.assertIsNone(func.parse_triggered_alert_type(None))
        self.assertIsNone(func.parse_triggered_alert_type(b""))
        with self.assertRaises(json.JSONDecodeError):
            func.parse_triggered_alert_type(b"not-json")
        self.assertFalse(func.is_reset_alert(None))
        self.assertEqual(
            ("SKIP", "unconfirmed_alert"),
            func.decide_spend_brake_action(None, None),
        )

    def test_spend_gate_numeric(self):
        self.assertIs(True, func.spend_reached_threshold(1.0, 1.0))
        self.assertIs(True, func.spend_reached_threshold(1.2, 1.0))
        self.assertIs(False, func.spend_reached_threshold(0.0, 1.0))
        self.assertIs(False, func.spend_reached_threshold(0.99, 1.0))
        self.assertIsNone(func.spend_reached_threshold(None, 1.0))
        self.assertIsNone(func.spend_reached_threshold(0.0, None))

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

    def test_budget_id_prefers_event_then_env(self):
        parsed = "ocid1.budget.oc1.phx.fromevent"
        self.assertEqual(
            parsed,
            func.resolve_budget_id(
                env={"BUDGET_ID": "ocid1.budget.oc1.phx.fromenv"},
                parsed_id=parsed,
            ),
        )
        self.assertEqual(
            "ocid1.budget.oc1.phx.fromenv",
            func.resolve_budget_id(
                env={"BUDGET_ID": "ocid1.budget.oc1.phx.fromenv"},
                parsed_id=None,
            ),
        )
        self.assertEqual(
            "",
            func.resolve_budget_id(env={"BUDGET_ID": "<BUDGET_OCID>"}, parsed_id=None),
        )


if __name__ == "__main__":
    unittest.main()
