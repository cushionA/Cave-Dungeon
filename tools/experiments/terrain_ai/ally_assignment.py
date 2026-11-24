"""Replay ally action reports while choosing whether to interrupt a threat to a protected game actor."""
import argparse
import json
from collections import Counter, defaultdict
from dataclasses import asdict, dataclass, replace
from pathlib import Path

# Actor references include lifetime. Positions, the companion's combat target, and its ward stay fixed.
SISTER, PARTNER, A, C = "sister@1", "partner@2", "ward-A@1", "ward-C@1"
B, D = "caster-B@1", "caster-D@1"
POSITIONS = {SISTER: (0, 0), PARTNER: (1, 0), A: (3, 0), C: (3, 2), B: (5, 0), D: (5, 2)}
DEADLINE, WINDUP = 6, 2
NORMAL = ((B, A), (D, C))
SWAPPED = ((B, C), (D, A))


@dataclass(frozen=True)
class Report:
    delivery: int
    action: int       # Increasing per ally lifetime.
    revision: int     # Increasing within the action; full replacement state.
    state: str
    eta: int = 5
    opponent: str = B
    cast: int = 7     # The hostile cast instance this ally is trying to stop.
    ally: str = PARTNER


ASSIGNED = Report(1, 10, 1, "assigned")
ACCEPTED = Report(2, 10, 2, "accepted")
MISSED = Report(3, 10, 3, "confirmed_miss")
COMPLETED = Report(3, 10, 3, "completed")
NEW = Report(2, 11, 1, "accepted")
OLD_CANCEL = Report(3, 10, 8, "canceled")


def coverage(tick, opponent, current_cast, last):
    if not last or last.opponent != opponent or last.cast != current_cast:
        return "no_matching_assignment"
    if last.state in ("accepted", "completed"):
        # Completion of the ally's action does not settle the separate effect on the opponent.
        if tick > last.eta:
            return "impact_outcome_unknown_after_eta"
        return "effect_pending" if last.eta <= DEADLINE else "effect_too_late"
    return last.state


def choose(tick, opponent, switch_delay, last, status):
    if status == "confirmed_interrupt":
        return ("resume_combat", A, B, tick, None)
    if status == "effect_pending":
        return ("monitor_ally", A, opponent, tick, last.eta)
    start = tick + switch_delay
    if start + WINDUP <= DEADLINE:
        return ("interrupt", A, opponent, start, start + WINDUP)
    return ("guard_ward", A, opponent, tick, None)


def observed_counts(targets):
    # The companion and partner keep B as their combat target even if the protection action aims at D.
    return dict(sorted(Counter([B, B] + [ward for _, ward in targets]).items()))


@dataclass(frozen=True)
class Scene:
    name: str
    reports: tuple
    expected: tuple
    targets: tuple = NORMAL
    current_cast: int = 7
    switch_delay: int = 0


# Each expected row gives the sample tick and (verb, protected ActorRef, opponent ActorRef, start, impact/ETA).
SCENES = (
    Scene("same_counts_B_threatens_A", (), ((2, ("interrupt", A, B, 2, 4)),)),
    Scene("same_counts_D_threatens_A", (), ((2, ("interrupt", A, D, 2, 4)),), targets=SWAPPED),
    Scene("assigned_only", (ASSIGNED,), ((2, ("interrupt", A, B, 2, 4)),)),
    Scene("accepted_in_time", (ASSIGNED, ACCEPTED), ((2, ("monitor_ally", A, B, 2, 5)),)),
    Scene("accepted_exact_deadline", (replace(ACCEPTED, eta=6),), ((2, ("monitor_ally", A, B, 2, 6)),)),
    Scene("accepted_one_tick_late", (replace(ACCEPTED, eta=7),), ((2, ("interrupt", A, B, 2, 4)),)),
    Scene("ally_committed_to_other_caster", (replace(ACCEPTED, opponent=D),), ((2, ("interrupt", A, B, 2, 4)),)),
    Scene("miss_releases_assignment", (ACCEPTED, MISSED), (
        (2, ("monitor_ally", A, B, 2, 5)), (3, ("interrupt", A, B, 3, 5)))),
    Scene("resume_at_last_start", (ACCEPTED, MISSED), ((3, ("interrupt", A, B, 4, 6)),), switch_delay=1),
    Scene("resume_one_tick_too_late", (ACCEPTED, MISSED), ((3, ("guard_ward", A, B, 3, None)),), switch_delay=2),
    Scene("ally_itself_interrupted", (ACCEPTED, replace(MISSED, state="interrupted")),
          ((3, ("interrupt", A, B, 3, 5)),)),
    Scene("canceled_then_old_accept_replayed", (ACCEPTED, replace(MISSED, state="canceled"),
          replace(ACCEPTED, delivery=4)), ((3, ("interrupt", A, B, 3, 5)), (4, ("interrupt", A, B, 4, 6)))),
    Scene("old_cancel_and_duplicate_after_new_accept", (ACCEPTED, NEW, OLD_CANCEL, replace(OLD_CANCEL, delivery=4)),
          ((3, ("monitor_ally", A, B, 3, 5)), (4, ("monitor_ally", A, B, 4, 5)))),
    Scene("current_cancel_after_new_accept", (ACCEPTED, NEW, replace(OLD_CANCEL, action=11)),
          ((3, ("interrupt", A, B, 3, 5)),)),
    Scene("confirmed_interrupt", (ACCEPTED, replace(MISSED, state="confirmed_interrupt")),
          ((3, ("resume_combat", A, B, 3, None)),)),
    Scene("confirmation_is_for_prior_cast", (ACCEPTED, replace(MISSED, state="confirmed_interrupt")),
          ((3, ("interrupt", A, B, 3, 5)),), current_cast=8),
    Scene("confirmation_is_from_recycled_ally", (ACCEPTED, replace(MISSED, state="confirmed_interrupt", ally="partner@1")),
          ((3, ("monitor_ally", A, B, 3, 5)),)),
    Scene("completed_while_delayed_effect_pending", (ACCEPTED, COMPLETED), (
        (3, ("monitor_ally", A, B, 3, 5)), (5, ("monitor_ally", A, B, 5, 5)),
        (6, ("guard_ward", A, B, 6, None)))),
    Scene("completed_then_observed_miss", (ACCEPTED, COMPLETED, replace(MISSED, delivery=4, revision=4)),
          ((3, ("monitor_ally", A, B, 3, 5)), (4, ("interrupt", A, B, 4, 6)))),
    Scene("eta_elapsed_without_confirmation", (replace(ACCEPTED, eta=3),),
          ((3, ("monitor_ally", A, B, 3, 3)), (4, ("interrupt", A, B, 4, 6)))),
)


def replay(scene, reverse_relations):
    targets = scene.targets[::-1] if reverse_relations else scene.targets
    opponent = next(caster for caster, ward in targets if ward == A)
    reports = sorted(scene.reports, key=lambda report: report.delivery)
    cursor = 0
    last = None
    rows = []
    for tick, expected in scene.expected:
        deliveries = []
        while cursor < len(reports) and reports[cursor].delivery <= tick:
            report = reports[cursor]
            cursor += 1
            fresh = report.ally == PARTNER and (last is None or
                    (report.action, report.revision) > (last.action, last.revision))
            if fresh:
                last = report
            deliveries.append({"report": asdict(report), "applied": fresh})
        status = coverage(tick, opponent, scene.current_cast, last)
        actual = choose(tick, opponent, scene.switch_delay, last, status)
        if actual != expected:
            raise AssertionError((scene.name, tick, actual, expected))
        rows.append({"tick": tick, "received": deliveries, "current_report": asdict(last) if last else None,
                     "coverage": status, "decision": actual})
    return {"scene": scene.name, "targets": targets, "targeting_count": observed_counts(targets),
            "cast": scene.current_cast, "switch_delay": scene.switch_delay, "steps": rows}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, help="Write inputs, report delivery and verified decisions to JSON.")
    args = parser.parse_args()
    traces = [replay(scene, reverse) for scene in SCENES for reverse in (False, True)]
    # At a given tick, positions and combat target are identical in every fixture.
    physical = defaultdict(set)
    for trace in traces:
        for step in trace["steps"]:
            physical[(step["tick"], tuple(trace["targeting_count"].items()))].add(step["decision"])
    summary = {"scenes": len(SCENES), "relation_orders": 2, "replays_passed": len(traces),
               "decisions_checked": sum(len(t["steps"]) for t in traces),
               "same_visible_snapshot_at_tick_with_conflicting_decisions": sum(len(v) > 1 for v in physical.values())}
    if args.output:
        result = {"summary": summary, "positions": POSITIONS, "current_combat_target": B,
                  "ward": A, "deadline_inclusive": DEADLINE, "windup": WINDUP,
                  "decision_fields": ["action", "protected_ref", "opponent_ref", "start_tick", "impact_or_ally_eta"],
                  "traces": traces}
        args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(summary))


if __name__ == "__main__":
    main()
