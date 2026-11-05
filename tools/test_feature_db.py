import contextlib
import copy
import importlib.util
import io
import json
import hashlib
from pathlib import Path
import sqlite3
import tempfile
import unittest
from unittest.mock import patch


spec = importlib.util.spec_from_file_location("feature_db", Path(__file__).with_name("feature-db.py"))
feature_db = importlib.util.module_from_spec(spec)
spec.loader.exec_module(feature_db)


class FeatureCallPathTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.original_db = feature_db.DB_PATH
        self.original_root = getattr(feature_db, "PROJECT_ROOT", None)
        feature_db.DB_PATH = str(self.root / "feature-log.db")
        feature_db.PROJECT_ROOT = self.root
        self.addCleanup(self.restore_paths)
        (self.root / "source.cs").write_text("class Source { void Run() {} }", encoding="utf-8")
        (self.root / "tests.cs").write_text("class SourceTests {}", encoding="utf-8")
        self.capture(feature_db.init_db)
        self.capture(feature_db.add_feature, "Status", impl=["source.cs"])
        self.capture(feature_db.add_feature, "Unregistered")
        self.payload = {
            "key": "hit-to-status", "title": "被弾から付与",
            "implementation_status": "implemented", "connection_status": "connected",
            "verification": "logic_tested", "notes": "純ロジックのテストを実行。Editor未実行。",
            "steps": [
                {"file": "source.cs", "symbol": "Source.Run", "kind": "entry"},
                {"file": "source.cs", "symbol": "Source.State", "kind": "write"}
            ],
            "tests": ["tests.cs"],
            "verification_hashes": {
                file: hashlib.sha256((self.root / file).read_bytes()).hexdigest()
                for file in ["source.cs", "tests.cs"]
            }
        }

    def restore_paths(self):
        feature_db.DB_PATH = self.original_db
        feature_db.PROJECT_ROOT = self.original_root

    def capture(self, function, *args, **kwargs):
        stream = io.StringIO()
        with contextlib.redirect_stdout(stream):
            function(*args, **kwargs)
        return stream.getvalue()

    def paths(self, feature="Status"):
        return json.loads(self.capture(feature_db.list_call_paths, feature))

    def test_call_path_upsert_preserves_order_and_does_not_mark_feature_complete(self):
        self.capture(feature_db.set_call_path, "Status", self.payload)
        changed = copy.deepcopy(self.payload)
        changed["title"] = "更新後"
        self.capture(feature_db.set_call_path, "Status", changed)
        paths = self.paths()
        self.assertEqual(1, len(paths))
        self.assertEqual("更新後", paths[0]["title"])
        self.assertEqual(["entry", "write"], [step["kind"] for step in paths[0]["steps"]])
        feature = json.loads(self.capture(feature_db.get_feature, "Status"))
        self.assertEqual("in_progress", feature["status"])
        self.assertEqual("registered", feature["call_path_registration"])
        self.assertEqual(paths, feature["call_paths"])

    def test_call_path_source_changes_require_new_evidence_to_refresh_verification(self):
        self.capture(feature_db.set_call_path, "Status", self.payload)
        self.assertFalse(self.paths()[0]["stale"])
        (self.root / "source.cs").write_text("class Source { void Changed() {} }", encoding="utf-8")
        stale = self.paths()[0]
        self.assertTrue(stale["stale"])
        self.assertEqual(["source.cs"], stale["changed_files"])
        self.assertFalse(stale["verification_current"])
        self.capture(feature_db.set_call_path, "Status", self.payload)
        self.assertTrue(self.paths()[0]["stale"])
        self.payload["verification_hashes"]["source.cs"] = hashlib.sha256((self.root / "source.cs").read_bytes()).hexdigest()
        self.capture(feature_db.set_call_path, "Status", self.payload)
        self.assertTrue(self.paths()[0]["verification_current"])

    def test_call_path_missing_test_file_is_stale(self):
        self.capture(feature_db.set_call_path, "Status", self.payload)
        (self.root / "tests.cs").unlink()
        self.assertEqual(["tests.cs"], self.paths()[0]["changed_files"])

    def test_call_path_invalid_payloads_do_not_change_existing_registration(self):
        self.capture(feature_db.set_call_path, "Status", self.payload)
        invalid_payloads = []
        for field, value in [("verification", "complete"), ("connection_status", "done"),
                             ("implementation_status", "done"), ("steps", []), ("tests", []),
                             ("notes", ""), ("verification", []), ("tests", {})]:
            payload = copy.deepcopy(self.payload)
            payload[field] = value
            invalid_payloads.append(payload)
        for file in ["../outside.cs", str(self.root / "source.cs"), "missing.cs"]:
            payload = copy.deepcopy(self.payload)
            payload["steps"][0]["file"] = file
            invalid_payloads.append(payload)
        payload = copy.deepcopy(self.payload)
        payload["steps"][0]["kind"] = "fiction"
        invalid_payloads.append(payload)
        payload = copy.deepcopy(self.payload)
        payload["steps"][0]["kind"] = []
        invalid_payloads.append(payload)
        for hashes in [None, {}, {"source.cs": "bad", "tests.cs": "0" * 64},
                       {"../source.cs": "0" * 64, "tests.cs": "0" * 64}]:
            payload = copy.deepcopy(self.payload)
            payload["verification_hashes"] = hashes
            invalid_payloads.append(payload)
        for payload in invalid_payloads:
            with self.subTest(payload=payload):
                with self.assertRaises(ValueError):
                    feature_db.set_call_path("Status", payload)
        self.assertEqual("被弾から付与", self.paths()[0]["title"])

    def test_call_path_unknown_feature_is_rejected(self):
        with self.assertRaises(ValueError):
            feature_db.set_call_path("Missing", self.payload)
        with self.assertRaises(ValueError):
            feature_db.list_call_paths("Missing")

    def test_call_path_unregistered_is_visible_without_inventing_a_route(self):
        feature = json.loads(self.capture(feature_db.get_feature, "Unregistered"))
        self.assertEqual("unregistered", feature["call_path_registration"])
        self.assertEqual([], feature["call_paths"])
        summary = self.capture(feature_db.summary)
        self.assertIn("0 registered", summary)
        self.assertIn("2 unregistered", summary)

    def test_call_path_existing_database_is_migrated_without_losing_records(self):
        with contextlib.closing(sqlite3.connect(feature_db.DB_PATH)) as connection:
            connection.execute("DROP TABLE IF EXISTS feature_call_paths")
            connection.commit()
        self.capture(feature_db.set_call_path, "Status", self.payload)
        self.assertEqual(1, len(self.paths()))
        self.assertEqual("Status", json.loads(self.capture(feature_db.get_feature, "Status"))["name"])

    def test_call_path_cli_imports_json_and_lists_registered_route(self):
        payload_file = self.root / "route.json"
        payload_file.write_text(json.dumps(self.payload, ensure_ascii=False), encoding="utf-8")
        with patch.object(feature_db.sys, "argv", ["feature-db.py", "set-call-path", "Status", "--file", str(payload_file)]):
            self.capture(feature_db.main)
        with patch.object(feature_db.sys, "argv", ["feature-db.py", "call-paths", "Status"]):
            result = json.loads(self.capture(feature_db.main))
        self.assertEqual("hit-to-status", result[0]["key"])
        with patch.object(feature_db.sys, "argv", ["feature-db.py", "set-call-path", "Status"]):
            with self.assertRaisesRegex(ValueError, 'Usage'):
                feature_db.main()

    def test_call_path_bulk_import_is_atomic_when_later_entry_is_invalid(self):
        self.capture(feature_db.set_call_path, "Status", self.payload)
        changed = copy.deepcopy(self.payload)
        changed["title"] = "途中更新されない"
        entries = [{"feature": "Status", "path": changed}, {"feature": "Missing", "path": self.payload}]
        with self.assertRaisesRegex(ValueError, 'Missing'):
            feature_db.import_call_paths(entries)
        self.assertEqual("被弾から付与", self.paths()[0]["title"])
        entries[1] = {"feature": "Unregistered", "path": {**self.payload, "verification": "invalid"}}
        with self.assertRaises(ValueError):
            feature_db.import_call_paths(entries)
        self.assertEqual([], self.paths("Unregistered"))
        self.assertEqual("被弾から付与", self.paths()[0]["title"])

    def test_call_path_bulk_cli_import_is_idempotent(self):
        entries = [{"feature": "Status", "path": self.payload}, {"feature": "Unregistered", "path": self.payload}]
        manifest = self.root / "call-paths.json"
        manifest.write_text(json.dumps(entries, ensure_ascii=False), encoding="utf-8")
        with patch.object(feature_db.sys, "argv", ["feature-db.py", "import-call-paths", "--file", str(manifest)]):
            self.capture(feature_db.main)
            self.capture(feature_db.main)
        self.assertEqual(1, len(self.paths("Status")))
        self.assertEqual(1, len(self.paths("Unregistered")))
        self.assertEqual("logic_tested", self.paths()[0]["verification"])

    def test_call_path_reimport_preserves_outdated_evidence_hashes(self):
        entries = [{"feature": "Status", "path": self.payload}]
        self.capture(feature_db.import_call_paths, entries)
        (self.root / "source.cs").write_text("class ChangedSource {}", encoding="utf-8")
        self.capture(feature_db.import_call_paths, entries)
        route = self.paths()[0]
        self.assertTrue(route["stale"])
        self.assertFalse(route["verification_current"])
        with contextlib.closing(sqlite3.connect(feature_db.DB_PATH)) as connection:
            hashes = json.loads(connection.execute('SELECT source_hashes_json FROM feature_call_paths').fetchone()[0])
        self.assertEqual(self.payload["verification_hashes"], hashes)

    def test_call_path_verified_payload_requires_hashes_for_all_referenced_files(self):
        payload = copy.deepcopy(self.payload)
        payload.pop("verification_hashes")
        with self.assertRaisesRegex(ValueError, 'hash'):
            feature_db.set_call_path("Status", payload)
        payload["verification_hashes"] = self.payload["verification_hashes"]
        (self.root / "additional.cs").write_text("class Extra {}", encoding="utf-8")
        payload["steps"].append({"file": "additional.cs", "symbol": "Extra", "kind": "call"})
        with self.assertRaisesRegex(ValueError, 'hash'):
            feature_db.set_call_path("Status", payload)

    def test_call_path_unverified_payload_can_omit_hash_evidence(self):
        payload = copy.deepcopy(self.payload)
        payload["verification"] = "unverified"
        payload.pop("verification_hashes")
        self.capture(feature_db.set_call_path, "Status", payload)
        self.assertFalse(self.paths()[0]["verification_current"])


if __name__ == "__main__":
    unittest.main()
