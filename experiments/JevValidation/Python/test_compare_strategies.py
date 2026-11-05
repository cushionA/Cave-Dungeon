import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

import numpy as np

from prepare import MODEL_REVISION
from prepare_strategies import STRATEGIES, _state, catalog_fingerprint, catalog_metadata, strategy_cases
from compare_strategies import HYPOTHESIS_TEMPLATE, _download_file, entailment_index, load_reference, make_pairs, rank_logits


class NliStrategyComparisonTests(unittest.TestCase):
    def cases(self):
        return [{"case_id": spec["case_id"], "state_id": spec["case_id"], "state": _state(spec),
                 "choice_ids": spec.get("candidate_order", list(STRATEGIES)),
                 "input_ids": [1] * 512, "attention_mask": [1] * 512,
                 "option_positions": list(range(10, 90, 10)), "decision_position": [511],
                 "reference_probabilities": [0.125] * 8} for spec in strategy_cases()]

    def test_EntailmentLabel_UsesExactConfigMappingAndRejectsAmbiguity(self):
        self.assertEqual(0, entailment_index({"id2label": {"0": "entailment", "1": "not_entailment"},
                                             "label2id": {"entailment": 0, "not_entailment": 1}}))
        self.assertEqual(1, entailment_index({"id2label": {"0": "contradiction", "1": "ENTAILMENT", "2": "neutral"}}))
        for config in ({"id2label": {"0": "LABEL_0", "1": "LABEL_1"}},
                       {"id2label": {"0": "entailment", "1": "ENTAILMENT"}},
                       {"id2label": {"0": "entailment", "2": "neutral"}},
                       {"id2label": {"0": "entailment", "1": "not_entailment"}, "label2id": {"entailment": 1, "not_entailment": 0}}):
            with self.subTest(config=config), self.assertRaises(ValueError):
                entailment_index(config)

    def test_Ranking_StableRelativeSoftmaxUsesEntailmentAndRejectsInvalidNumbers(self):
        logits = np.zeros((8, 3))
        logits[:, 0] = 1e6
        logits[:, 1] = np.arange(8) + 1000
        result = rank_logits(list(STRATEGIES), logits, 1, 3)
        self.assertEqual("bypass", result["selected_strategy"])
        self.assertAlmostEqual(1, sum(result["relative_scores"].values()))
        self.assertEqual(1007, result["entailment_logits"]["bypass"])
        self.assertEqual([1e6, 1007, 0], result["raw_logits"]["bypass"])
        self.assertEqual(0, result["entailment_probabilities"]["bypass"])
        self.assertEqual([1, 0, 0], result["nli_class_probabilities"]["bypass"])
        for broken in (np.full((8, 3), np.nan), np.zeros((7, 3)), np.zeros((8, 2))):
            with self.assertRaises(ValueError):
                rank_logits(list(STRATEGIES), broken, 1, 3)

    def test_CandidatePermutation_PreservesInputAndIdAlignedRankings(self):
        case = self.cases()[0]
        original = copy.deepcopy(case)
        saved_catalog = copy.deepcopy(STRATEGIES)
        pairs = make_pairs(case, saved_catalog)
        self.assertTrue(all(premise == case["state"] for premise, _ in pairs))
        self.assertEqual([HYPOTHESIS_TEMPLATE.format(strategy=STRATEGIES[key]) for key in case["choice_ids"]],
                         [hypothesis for _, hypothesis in pairs])
        canonical = {key: [0, index] for index, key in enumerate(STRATEGIES)}
        first = rank_logits(case["choice_ids"], [canonical[key] for key in case["choice_ids"]], 1, 2)
        rotated = copy.deepcopy(case)
        rotated["choice_ids"] = case["choice_ids"][4:] + case["choice_ids"][:4]
        second = rank_logits(rotated["choice_ids"], [canonical[key] for key in rotated["choice_ids"]], 1, 2)
        self.assertEqual(first["relative_scores"], second["relative_scores"])
        self.assertEqual(first["selected_strategy"], second["selected_strategy"])
        self.assertEqual(original, case)
        original_description = STRATEGIES["lure_ambush"]
        try:
            STRATEGIES["lure_ambush"] = "A replaced candidate with the same ID."
            self.assertEqual(pairs, make_pairs(case, saved_catalog))
        finally:
            STRATEGIES["lure_ambush"] = original_description

    def test_DownloadBoundary_RejectsCorruptLfsAndGitBlobsWithoutRewritingCache(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "model.onnx"
            data = b"pinned public model"
            lfs = {"size": len(data), "lfs": {"sha256": hashlib.sha256(data).hexdigest()}}
            git = {"size": len(data), "blobId": hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()}
            for descriptor in (lfs, git):
                path.write_bytes(data)
                record = _download_file("https://unused.invalid/model", path, descriptor)
                self.assertEqual(hashlib.sha256(data).hexdigest(), record["sha256"])
                corrupt = data[:-1] + b"!"
                path.write_bytes(corrupt)
                with self.assertRaises(ValueError):
                    _download_file("https://unused.invalid/model", path, descriptor)
                self.assertEqual(corrupt, path.read_bytes())

    def test_BaselineBoundary_PreservesAll18CasesAndRejectsChangedCandidates(self):
        document = {"token_count": 512, "reference_source": {"model_revision": MODEL_REVISION, **catalog_metadata()}, "cases": self.cases()}
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "reference.json"
            path.write_text(json.dumps(document), encoding="utf-8")
            original = path.read_bytes()
            self.assertEqual(document, load_reference(path))
            self.assertEqual(original, path.read_bytes())
            for change in ("missing_case", "candidate", "state", "catalog_body", "catalog_body_and_hash"):
                invalid = copy.deepcopy(document)
                if change == "missing_case":
                    invalid["cases"].pop()
                elif change == "candidate":
                    invalid["cases"][0]["choice_ids"][0] = "other_strategy"
                elif change == "state":
                    invalid["cases"][0]["state"] = " "
                else:
                    invalid["reference_source"]["strategy_catalog"]["lure_ambush"] = "A different plan under the same candidate ID."
                    if change == "catalog_body_and_hash":
                        invalid["reference_source"]["strategy_catalog_sha256"] = catalog_fingerprint(invalid["reference_source"]["strategy_catalog"])
                path.write_text(json.dumps(invalid), encoding="utf-8")
                with self.subTest(change=change), self.assertRaises(ValueError):
                    load_reference(path)


if __name__ == "__main__":
    unittest.main()
