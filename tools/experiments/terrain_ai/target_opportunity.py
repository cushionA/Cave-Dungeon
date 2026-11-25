"""Can a companion land its first hit before a stunned enemy recovers? Run with --output PATH for evidence."""
import argparse
import json
from dataclasses import asdict, dataclass, replace
from pathlib import Path


NEAR = "actor:A#1"
FAR = "actor:B#1"
PRIORITY_LIMIT_CM = 500
ATTACK_REACH_CM = 100
MOVE_CM_PER_SECOND = 400


@dataclass(frozen=True)
class World:
    candidate_distance_cm: int = 500
    candidate_act_state: str = "Stunned"
    stun_remaining_ms: int = 2000
    current_lock_ms: int = 200
    approach_detour_cm: int = 0
    attack_startup_ms: int = 100


@dataclass(frozen=True)
class ActorSample:
    ref: str
    distance_cm: int
    act_state: str


BASE = World()
JUST_IN_TIME = replace(BASE, stun_remaining_ms=1301)
# Handwritten selection oracles. Only initial conditions, not results of a selector, define each world.
CASES = {
    "at_distance_limit": (BASE, FAR),
    "inside_distance_limit": (replace(BASE, candidate_distance_cm=499), FAR),
    "outside_distance_limit": (replace(BASE, candidate_distance_cm=501), NEAR),
    "hit_one_ms_before_expiry": (JUST_IN_TIME, FAR),
    "hit_at_expiry": (replace(BASE, stun_remaining_ms=1300), NEAR),
    "hit_one_ms_after_expiry": (replace(BASE, stun_remaining_ms=1299), NEAR),
    "one_more_ms_locked": (replace(JUST_IN_TIME, current_lock_ms=201), NEAR),
    "one_cm_detour": (replace(JUST_IN_TIME, approach_detour_cm=1), NEAR),
    "one_more_ms_startup": (replace(JUST_IN_TIME, attack_startup_ms=101), NEAR),
    "stun_with_neutral_act_state": (replace(BASE, candidate_act_state="Neutral"), FAR),
    "no_stun_with_neutral_act_state": (replace(BASE, candidate_act_state="Neutral", stun_remaining_ms=0), NEAR),
}

# Input, same raw actor snapshot, different expected choice. The input name and both booleans are independent oracles.
PAIRS = (
    ("inside_distance_limit", "at_distance_limit", "candidate_distance_cm", False, False),
    ("at_distance_limit", "outside_distance_limit", "candidate_distance_cm", False, True),
    ("hit_one_ms_before_expiry", "hit_at_expiry", "stun_remaining_ms", True, True),
    ("hit_at_expiry", "hit_one_ms_after_expiry", "stun_remaining_ms", True, False),
    ("hit_one_ms_before_expiry", "one_more_ms_locked", "current_lock_ms", True, True),
    ("hit_one_ms_before_expiry", "one_cm_detour", "approach_detour_cm", True, True),
    ("hit_one_ms_before_expiry", "one_more_ms_startup", "attack_startup_ms", True, True),
    ("at_distance_limit", "stun_with_neutral_act_state", "candidate_act_state", False, False),
    ("stun_with_neutral_act_state", "no_stun_with_neutral_act_state", "stun_remaining_ms", True, True),
)


def evaluate_world(world, reverse=False):
    candidates = [ActorSample(NEAR, 100, "Neutral"), ActorSample(FAR, world.candidate_distance_cm, world.candidate_act_state)]
    if reverse:
        candidates.reverse()
    closest = min(candidates, key=lambda actor: (actor.distance_cm, actor.ref))

    def prioritize(eligible):
        return min(eligible, key=lambda actor: (actor.distance_cm, actor.ref), default=closest).ref

    active_ms = {NEAR: 0, FAR: world.stun_remaining_ms}
    elapsed_to_hit = {}
    for actor in candidates:
        distance_to_range = max(0, actor.distance_cm - ATTACK_REACH_CM)
        if actor.ref == FAR:
            distance_to_range += world.approach_detour_cm
        # Integer ceiling keeps a one-centimetre detour and exact expiry deterministic.
        travel_ms = (distance_to_range * 1000 + MOVE_CM_PER_SECOND - 1) // MOVE_CM_PER_SECOND
        elapsed_to_hit[actor.ref] = world.current_lock_ms + travel_ms + world.attack_startup_ms

    in_range = [actor for actor in candidates if actor.distance_cm <= PRIORITY_LIMIT_CM]
    chosen = {
        "nearest": closest.ref,
        "act_state_and_distance": prioritize(actor for actor in in_range if actor.act_state == "Stunned"),
        "active_stun_and_distance": prioritize(actor for actor in in_range if active_ms[actor.ref] > 0),
        "stun_lasts_to_first_hit": prioritize(actor for actor in in_range if elapsed_to_hit[actor.ref] < active_ms[actor.ref]),
    }
    # Positions uniquely determine distance on this fixed straight 1D fixture. All other sampled values are constant.
    actor_snapshot = {"owner_act_state": "AttackRecovery",
                      "candidates": [asdict(actor) for actor in sorted(candidates, key=lambda x: x.ref)]}
    return {"actor_snapshot": actor_snapshot, "time_to_first_hit_ms": elapsed_to_hit, "chosen": chosen}


def evaluate():
    results = {}
    misses = {}
    for name, (world, expected) in CASES.items():
        outcome = evaluate_world(world)
        assert outcome == evaluate_world(world, reverse=True), name
        assert outcome["chosen"]["stun_lasts_to_first_hit"] == expected, name
        for policy, actual in outcome["chosen"].items():
            misses.setdefault(policy, [])
            if actual != expected:
                misses[policy].append(name)
        results[name] = {"world": asdict(world), "expected": expected, **outcome}

    pairs = []
    for left, right, field, expected_same_snapshot, expected_switch in PAIRS:
        a, b = results[left], results[right]
        changes = [k for k, v in a["world"].items() if v != b["world"][k]]
        same_snapshot = a["actor_snapshot"] == b["actor_snapshot"]
        switch = a["expected"] != b["expected"]
        assert (changes, same_snapshot, switch) == ([field], expected_same_snapshot, expected_switch), (left, right)
        pairs.append({"cases": [left, right], "changed_world_input": field, "same_actor_snapshot": same_snapshot,
                      "different_expected_choice": switch})
    return {
        "model": "target_opportunity", "case_count": len(results), "pair_count": len(pairs),
        "same_snapshot_different_choice_pairs": sum(p["same_actor_snapshot"] and p["different_expected_choice"] for p in pairs),
        "assumptions": {"distance_limit_cm_inclusive": PRIORITY_LIMIT_CM, "attack_reach_cm": ATTACK_REACH_CM,
                        "travel_speed_cm_per_second": MOVE_CM_PER_SECOND, "first_hit": "lock + time to range + startup",
                        "stun_interval": "[0, remaining_ms)", "targets": "stationary, same physics world, both hostile",
                        "fallback": "nearest live enemy"},
        "projection": {
            "actor_snapshot": "Distances are derived from observed positions. Candidate flags include ActState. SelfActState reads the owner.",
            "active_stun_and_distance": "Hypothetical access to candidate StatusEffectId.Stun presence only.",
            "stun_lasts_to_first_hit": "Also uses remaining effect time, current lock, approach path and attack startup.",
        },
        "misses": misses, "cases": results, "pairs": pairs,
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = evaluate()
    if args.output:
        args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"{result['case_count']} cases, {result['pair_count']} one-input pairs, "
          f"{result['same_snapshot_different_choice_pairs']} pairs with identical actor snapshots but different expected choices")
    print("Mismatches against the handwritten choices:", {k: len(v) for k, v in result["misses"].items()})
