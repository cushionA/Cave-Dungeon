"""Compare a 2D ground-targeted game ability's plan, release and impact in paired toy scenes."""
from __future__ import annotations

import argparse
import json
from dataclasses import asdict, dataclass, replace
from itertools import product
from math import dist
from pathlib import Path

LEFT, RIGHT = (-3, 0), (3, 0)
FLOOR = (LEFT, RIGHT)
ORIGIN = (0, 0)
RADIUS = 1


@dataclass(frozen=True)
class Actor:
    ref: tuple  # (hash, generation)
    team: str
    parts: tuple  # point colliders in two dimensions


def score(point, actors):
    """Predict affected actors from observed bodies. Each body contributes at most once."""
    enemies, friends = set(), set()
    occupied = False
    for actor in actors:
        for x, y in actor.parts:
            dx, dy = x - point[0], y - point[1]
            occupied |= dx == 0 and dy == 0
            if dx * dx + dy * dy <= RADIUS * RADIUS:
                (enemies if actor.team == "enemy" else friends).add(actor.ref)
    return occupied, tuple(sorted(enemies)), tuple(sorted(friends))


def propose(floor, actors):
    candidates = []
    for point in floor:
        occupied, enemies, friends = score(point, actors)
        if not occupied and enemies and not friends:
            candidates.append((-len(enemies), dist(ORIGIN, point), point, enemies))
    if not candidates:
        return None, ()
    _, _, point, enemies = min(candidates)  # coordinates break equal-score ties
    return point, enemies


def impact(point, actors):
    """Resolve point-collider contacts afresh at impact, including friendly hits."""
    hits = {"enemy": set(), "friendly": set()}
    if point is not None:
        for actor in actors:
            if any(dist(center, point) <= RADIUS for center in actor.parts):
                hits[actor.team].add(actor.ref)
    return tuple(sorted(hits["enemy"])), tuple(sorted(hits["friendly"]))


ENEMIES = tuple(Actor((i, 1), "enemy", ((x, 0),)) for i, x in enumerate((-4, -2, 2, 4), 1))
ALLY = Actor((9, 1), "friendly", ((0, 3),))
BASE = ENEMIES + (ALLY,)
ALLY_ENTERS = ENEMIES + (replace(ALLY, parts=((-3, 1),)),)
BOSS = (replace(ENEMIES[0], parts=((-4, 0), (-2, 0))),) + BASE[2:]
DEPARTS = (BASE[0], replace(BASE[1], parts=((-2, 2),))) + BASE[2:]
REUSED = (BASE[0], replace(BASE[1], ref=(2, 2))) + BASE[2:]
OCCUPIED = (replace(BASE[0], parts=(LEFT,)),) + BASE[1:]
LEFT_REFS, RIGHT_REFS = ((1, 1), (2, 1)), ((3, 1), (4, 1))


@dataclass(frozen=True)
class Scenario:
    name: str
    floor: tuple = FLOOR
    observed: tuple = BASE
    at_release: tuple = BASE
    at_impact: tuple = BASE
    aim: tuple | None = LEFT
    predicted: tuple = LEFT_REFS
    released: bool = True
    hit_enemies: tuple = LEFT_REFS
    hit_friends: tuple = ()


SCENARIOS = (
    Scenario("two_enemies_per_cluster"),
    Scenario("same_parts_one_boss", observed=BOSS, at_release=BOSS, at_impact=BOSS,
             aim=RIGHT, predicted=RIGHT_REFS, hit_enemies=RIGHT_REFS),
    Scenario("only_left_floor_two_enemies", floor=(LEFT,)),
    Scenario("only_left_floor_one_boss", floor=(LEFT,), observed=BOSS, at_release=BOSS, at_impact=BOSS,
             predicted=((1, 1),), hit_enemies=((1, 1),)),
    Scenario("missing_left_floor", floor=(RIGHT,), aim=RIGHT, predicted=RIGHT_REFS, hit_enemies=RIGHT_REFS),
    Scenario("occupied_left_floor", observed=OCCUPIED, at_release=OCCUPIED, at_impact=OCCUPIED,
             aim=RIGHT, predicted=RIGHT_REFS, hit_enemies=RIGHT_REFS),
    Scenario("ally_at_left_before_selection", observed=ALLY_ENTERS, at_release=ALLY_ENTERS, at_impact=ALLY_ENTERS,
             aim=RIGHT, predicted=RIGHT_REFS, hit_enemies=RIGHT_REFS),
    Scenario("ally_enters_before_release", at_release=ALLY_ENTERS, at_impact=ALLY_ENTERS,
             released=False, hit_enemies=()),
    Scenario("ally_enters_after_release", at_impact=ALLY_ENTERS, hit_friends=((9, 1),)),
    Scenario("enemy_leaves_after_release", at_impact=DEPARTS, hit_enemies=((1, 1),)),
    Scenario("enemy_reused_after_release", at_impact=REUSED, hit_enemies=((1, 1), (2, 2))),
    Scenario("only_floor_is_unsafe", floor=(LEFT,), observed=ALLY_ENTERS, at_release=ALLY_ENTERS,
             at_impact=ALLY_ENTERS, aim=None, predicted=(), released=False, hit_enemies=()),
)


def run(case, reverse_floor, reverse_actors, reverse_parts):
    def ordered(actors):
        return tuple(replace(a, parts=a.parts[::-1] if reverse_parts else a.parts)
                     for a in (actors[::-1] if reverse_actors else actors))

    observed = ordered(case.observed)
    aim, predicted = propose(case.floor[::-1] if reverse_floor else case.floor, observed)
    released = False
    if aim is not None:
        occupied, enemies, friends = score(aim, ordered(case.at_release))
        released = not occupied and bool(enemies) and not friends
    hit_enemies, hit_friends = impact(aim if released else None, ordered(case.at_impact))
    actual = (aim, predicted, released, hit_enemies, hit_friends)
    expected = (case.aim, case.predicted, case.released, case.hit_enemies, case.hit_friends)
    if actual != expected:
        raise AssertionError(f"{case.name}: expected {expected}, got {actual}")
    direction = None if aim is None else tuple((x - o) / dist(ORIGIN, aim) for x, o in zip(aim, ORIGIN))
    return {"scenario": case.name, "reverse_floor": reverse_floor, "reverse_actors": reverse_actors,
            "reverse_parts": reverse_parts, "floor": case.floor, "aim": aim, "direction": direction,
            "predicted_enemies": predicted, "released": released, "impact_enemies": hit_enemies,
            "impact_friends": hit_friends}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, help="Write per-case traces to JSON.")
    args = parser.parse_args()
    traces = [run(case, *order) for case in SCENARIOS for order in product((False, True), repeat=3)]
    summary = {"scenarios": len(SCENARIOS), "order_variants": 8, "passed": len(traces),
               "released": sum(trace["released"] for trace in traces),
               "impact_differs_from_prediction_after_release": sum(
                   trace["released"] and (trace["predicted_enemies"] != trace["impact_enemies"]
                                          or bool(trace["impact_friends"])) for trace in traces)}
    if args.output:
        result = {"summary": summary, "radius": RADIUS, "origin": ORIGIN,
                  "scenarios": [asdict(case) for case in SCENARIOS], "traces": traces}
        args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(summary))


if __name__ == "__main__":
    main()
