"""Count information loss on six finite game fixtures, one hidden input group at a time."""
import argparse
from collections import Counter
from operator import itemgetter
import json
from pathlib import Path
from importlib import import_module

FAMILIES = ("impact", "routes", "perception", "lifecycle", "links", "handoff")
CHECKS = {
    "impact": "sanity_checks", "routes": "check_examples", "perception": "sanity",
    "lifecycle": "sanity", "links": "sanity_checks", "handoff": "sanity",
}


def grade_projection(cases, keys, hidden):
    visible = [k for k in keys if k not in hidden]
    by_visible = {}
    first_case = {}
    witness = None
    project = itemgetter(*visible)
    for row, expected in cases:
        key = project(row)
        counts = by_visible.get(key)
        if counts is None:
            by_visible[key] = Counter({expected: 1})
            first_case[key] = (row, expected)
        else:
            counts[expected] += 1
            if witness is None and len(counts) > 1:
                before, other = first_case[key]
                assert other != expected
                witness = {
                    "shared": {k: row[k] for k in visible},
                    "alternatives": [
                        {"hidden": {k: x[k] for k in hidden}, "outcome": y}
                        for x, y in ((before, other), (row, expected))
                    ],
                }

    mixed = [counts for counts in by_visible.values() if len(counts) > 1]
    return {
        "hidden": hidden,
        "visible_keys": len(by_visible),
        "contradictory_keys": len(mixed),
        "rows_in_contradictions": sum(sum(c.values()) for c in mixed),
        "minimum_mistakes": sum(sum(c.values()) - max(c.values()) for c in mixed),
        "witness": witness,
    }


def evaluate(module, name):
    getattr(module, CHECKS[name])()
    cases = list(module.cases())
    assert cases
    keys = sorted(cases[0][0])
    assert {k for group in module.GROUPS.values() for k in group} == set(keys)
    seen = set()
    take = itemgetter(*keys)
    for row, expected in cases:
        assert set(row) == set(keys) and isinstance(expected, str)
        assert all(isinstance(v, (str, int, float, bool)) or v is None for v in row.values())
        full = take(row)
        assert full not in seen, (name, row)
        seen.add(full)

    result = {
        "policy": module.POLICY,
        "cases": len(cases),
        "outcomes": dict(sorted(Counter(y for _, y in cases).items())),
        "projections": {
            group: grade_projection(cases, keys, hidden)
            for group, hidden in module.GROUPS.items()
        },
    }
    if name == "links":
        compared = Counter((module.outcome(**x, direct_only=True), y) for x, y in cases)
        result["direct_only_vs_network"] = [
            {"direct_only": direct, "network": network, "cases": n}
            for (direct, network), n in sorted(compared.items())
        ]
    if name == "handoff":
        # Record signed mistakes for each preset formula on each domain.
        base_keys = {take(x) for x, _ in cases}
        holdout_keys = {take(x) for x, _ in module.cases(module.HOLDOUT)}
        assert base_keys.isdisjoint(holdout_keys)
        result["timing_progression"] = {
            "policies": ["immediate", "travel_windup_delay", "lock_travel_windup_delay"],
            "base": module.progression(module.BASE),
            "holdout": module.progression(module.HOLDOUT),
        }
    return result


def matrix():
    families = {name: evaluate(import_module(name), name) for name in FAMILIES}
    projections = [p for f in families.values() for p in f["projections"].values()]
    return {
        "method": "Within each fixture and each hidden group, equally weight distinct enumerated inputs. "
                  "A visible key is contradictory if at least two expected outcomes occur. "
                  "Minimum mistakes is the sum over visible keys of (row count - majority outcome count). "
                  "Each input occurs in multiple projections; their mistake totals are not additive.",
        "summary": {
            "base_cases": sum(f["cases"] for f in families.values()),
            "additional_handoff_holdout_cases": families["handoff"]["timing_progression"]["holdout"]["cases"],
            "groups": len(projections),
            "groups_with_contradiction": sum(p["contradictory_keys"] > 0 for p in projections),
        },
        "families": families,
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, help="Write all per-family counts and counterexamples to this JSON file.")
    args = parser.parse_args()
    result = matrix()
    if args.output:
        args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(result["summary"])
    for name, family in result["families"].items():
        print(name, family["cases"], [(group, p["contradictory_keys"], p["minimum_mistakes"])
                                      for group, p in family["projections"].items()])
