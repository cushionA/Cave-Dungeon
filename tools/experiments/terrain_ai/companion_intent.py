"""Four multi-tick companion policies. Run directly, or pass --output PATH for all traces."""
import argparse
import json
from dataclasses import asdict, dataclass
from pathlib import Path

SISTER = "actor:sister#1"
PLAYER = "actor:player#1"
WOUNDED = "actor:fleeing#3"
CASTER = "actor:caster#2"
SHIELD = "actor:shield#4"
LURED = "actor:pursuer#5"
MISSING = "actor:scout#7"
TRAP = "device:floor-trap#2"

# Seconds are simulation time. Movement durations and mission deadlines are fixture parameters.
END = 6
LURE_LIMIT = 5
SEARCH_LIMIT = 4


@dataclass(frozen=True)
class Command:
    verb: str
    actor: str = SISTER
    target: str = ""
    point: str = ""
    route: str = ""


@dataclass
class Intent:
    goal: str = ""
    protected_actor: str = PLAYER
    opponent: str = ""
    device: str = ""
    search_point: str = ""
    last_seen_point: str = ""
    last_seen_at: int | None = None
    deadline: int | None = None


@dataclass
class World:
    scenario: str
    settings: dict
    place: str = "near"
    command: Command | None = None
    started_at: int = 0
    trap_used: bool = False

    def observe(self, t):
        # Body updates rely on the command that actually started, including its start time.
        verb = self.command.verb if self.command else ""
        elapsed = t - self.started_at
        if verb == "approach_takeoff" and elapsed >= 2:
            self.place = "takeoff"
        elif verb == "jump_gap":
            self.place = "landing" if elapsed >= 2 else "air"
        elif verb == "return_to_player" and elapsed >= 2:
            self.place = "beside_player"
        elif verb == "climb_side_ramp" and elapsed >= 3:
            self.place = "flank"
        elif verb == "reach_last_seen_corner" and elapsed >= 1:
            self.place = "corner"

        p = self.settings
        if self.scenario == "pursuit":
            threatened = p["cast_at"] is not None and t >= p["cast_at"]
            return {"place": self.place, "weak_enemy": WOUNDED, "beyond_gap": True,
                    "caster": CASTER if threatened else None, "cast_target": PLAYER if threatened else None}
        if self.scenario == "flank":
            released = p["release_at"] is not None and t >= p["release_at"]
            turned = released or (p["turn_at"] is not None and t >= p["turn_at"])
            return {"place": self.place, "player_near": True, "shield_enemy": SHIELD,
                    "player_engaging": None if released else SHIELD,
                    "enemy_facing": SISTER if turned else PLAYER}
        if self.scenario == "lure":
            enemy_inside = p["enemy_enters"] is not None and t >= p["enemy_enters"]
            player_inside = p["player_exits"] is None or t < p["player_exits"]
            return {"device": TRAP, "ready": not self.trap_used, "can_operate": True,
                    "enemy": LURED, "enemy_inside": enemy_inside, "player_inside": player_inside}
        # Occluded actors have no position in the observation. A sound identifies a direction, not an actor.
        if t == 0:
            visible_at = "point:corner"
        elif p["emerges_at"] is not None and t >= p["emerges_at"]:
            visible_at = "point:opening-" + p["hidden_side"]
        else:
            visible_at = None
        return {"place": self.place, "seen": MISSING if visible_at else None, "seen_at": visible_at,
                "footstep_direction": p["sound_side"] if t == 3 else None}

    def start(self, command, t):
        self.command = command
        self.started_at = t
        if command.verb == "activate_trap":
            assert not self.trap_used and command.target == TRAP
            self.trap_used = True


class Companion:
    def __init__(self):
        self.intent = Intent()
        self.previous = {}
        self.command = None
        self.review = None
        self.start = None

    def decide(self, scenario, t, facts):
        self.review = self.start = None
        getattr(self, scenario)(t, facts)
        self.previous = facts
        return self.review, self.start

    def begin(self, verb, *, target="", point="", route=""):
        command = Command(verb, target=target, point=point, route=route)
        if command != self.command:
            self.command = self.start = command

    def pursuit(self, t, f):
        state = self.intent
        if not state.goal and f["weak_enemy"] and f["beyond_gap"]:
            self.review = "weak_enemy_across_gap"
            state.goal, state.opponent = "pursue", f["weak_enemy"]
            self.begin("approach_takeoff", point="point:takeoff", route="route:gap")
        if f["cast_target"] == state.protected_actor and state.goal == "pursue":
            self.review = "cast_threatens_player"
            state.goal, state.opponent = "protect", f["caster"]
            if f["place"] != "air":
                self.begin("return_to_player", target=state.protected_actor, route="route:return")
        elif state.goal == "pursue" and f["place"] == "takeoff" and self.command.verb == "approach_takeoff":
            self.review = "takeoff_reached"
            self.begin("jump_gap", point="point:landing", route="route:gap")
        elif self.command.verb == "jump_gap" and f["place"] == "landing":
            self.review = "landed"
            if state.goal == "protect":
                self.begin("return_to_player", target=state.protected_actor, route="route:return")
            else:
                self.begin("resume_pursuit", target=state.opponent)
        elif self.command.verb == "return_to_player" and f["place"] == "beside_player":
            self.review = "rejoined_player"
            self.begin("guard_player", target=state.protected_actor)

    def flank(self, t, f):
        state = self.intent
        pinned = f["player_engaging"] == f["shield_enemy"] and f["enemy_facing"] == PLAYER
        if not state.goal or (state.goal == "flank" and not pinned):
            self.review = "front_held" if pinned else "front_not_held"
            state.goal, state.opponent = ("flank" if pinned else "support"), f["shield_enemy"]
            if pinned:
                self.begin("climb_side_ramp", point="point:shield_side", route="route:side_ramp")
            else:
                self.begin("cover_player", target=state.protected_actor)
        elif state.goal == "flank" and f["place"] == "flank" and self.command.verb == "climb_side_ramp":
            self.review = "side_reached"
            self.begin("strike_side", target=state.opponent, point="point:shield_side")

    def lure(self, t, f):
        state = self.intent
        if not state.goal and f["ready"] and f["enemy"]:
            self.review = "armed_trap_seen"
            state.goal, state.opponent, state.device = "lure", f["enemy"], f["device"]
            state.deadline = t + LURE_LIMIT
            self.begin("lure_enemy", target=state.opponent, point="point:trap_zone")
        elif state.goal == "lure":
            if t >= state.deadline:
                self.review = "lure_expired"
                state.goal, state.device, state.deadline = "protect", "", None
                self.begin("guard_player", target=state.protected_actor)
            elif f["ready"] and f["can_operate"] and f["enemy_inside"] and not f["player_inside"]:
                self.review = "enemy_in_player_clear"
                state.goal = "activated"
                self.begin("activate_trap", target=state.device)
            elif f["enemy_inside"] and self.command.verb == "lure_enemy":
                self.review = "enemy_in_player_exposed"
                self.begin("hold_trigger", target=state.device)
            elif self.command.verb == "lure_enemy" and not f["player_inside"] and self.previous["player_inside"]:
                self.review = "player_clear_enemy_outside"
        elif state.goal == "activated" and not f["ready"]:
            self.review = "activation_confirmed"
            state.goal, state.device, state.deadline = "protect", "", None
            self.begin("guard_player", target=state.protected_actor)

    def search(self, t, f):
        state = self.intent
        if f["seen"]:
            if state.goal != "track" or state.opponent != f["seen"]:
                self.review = "visual_reacquired" if state.last_seen_at is not None else "visual_contact"
                self.begin("aim_at_visible_enemy", target=f["seen"], point=f["seen_at"])
            state.goal, state.opponent, state.search_point = "track", f["seen"], ""
            state.last_seen_point, state.last_seen_at, state.deadline = f["seen_at"], t, None
        elif state.goal == "track":
            self.review = "lost_visual"
            state.goal, state.deadline = "search", t + SEARCH_LIMIT
            state.search_point = state.last_seen_point
            self.begin("reach_last_seen_corner", point=state.search_point, route="route:corner")
        elif state.goal == "search":
            if t >= state.deadline:
                self.review = "search_expired"
                state.goal = "protect"
                state.opponent, state.search_point, state.deadline = "", "", None
                self.begin("guard_player", target=state.protected_actor)
            elif f["place"] == "corner" and f["footstep_direction"]:
                self.review = "unidentified_footstep"
                state.search_point = "point:" + f["footstep_direction"]
                self.begin("check_sound", point=state.search_point)
            elif f["place"] == "corner" and self.command.verb == "reach_last_seen_corner":
                self.review = "corner_reached"
                self.begin("inspect_corner", point=state.last_seen_point)


def run(scenario, settings):
    world, brain = World(scenario, settings), Companion()
    previous = {}
    rows = []
    for t in range(END + 1):
        facts = world.observe(t)
        review, start = brain.decide(scenario, t, facts)
        if start:
            world.start(start, t)
        rows.append({"t": t, "observed": facts, "changed": {k: v for k, v in facts.items()
                     if k not in previous or previous[k] != v}, "review": review, "intent": asdict(brain.intent),
                     "begin": asdict(start) if start else None, "executing": asdict(brain.command)})
        previous = facts
    return rows


@dataclass(frozen=True)
class Fixture:
    scenario: str
    settings: dict
    observed_changes: tuple
    reviews: str
    starts: str


# Independent oracles. Empty seconds are also asserted: in particular, a decision to return
# during a jump may not start a return command before landing.
FIXTURES = {
    "pursue_without_threat": Fixture("pursuit", {"cast_at": None}, (0, 2, 3, 4),
        "0:weak_enemy_across_gap 2:takeoff_reached 4:landed",
        "0:approach_takeoff 2:jump_gap 4:resume_pursuit"),
    "threat_before_takeoff": Fixture("pursuit", {"cast_at": 1}, (0, 1, 3),
        "0:weak_enemy_across_gap 1:cast_threatens_player 3:rejoined_player",
        "0:approach_takeoff 1:return_to_player 3:guard_player"),
    "threat_at_takeoff": Fixture("pursuit", {"cast_at": 2}, (0, 2, 4),
        "0:weak_enemy_across_gap 2:cast_threatens_player 4:rejoined_player",
        "0:approach_takeoff 2:return_to_player 4:guard_player"),
    "threat_midair": Fixture("pursuit", {"cast_at": 3}, (0, 2, 3, 4, 6),
        "0:weak_enemy_across_gap 2:takeoff_reached 3:cast_threatens_player 4:landed 6:rejoined_player",
        "0:approach_takeoff 2:jump_gap 4:return_to_player 6:guard_player"),
    "shield_pinned": Fixture("flank", {"release_at": None, "turn_at": None}, (0, 3),
        "0:front_held 3:side_reached", "0:climb_side_ramp 3:strike_side"),
    "front_released": Fixture("flank", {"release_at": 2, "turn_at": None}, (0, 2),
        "0:front_held 2:front_not_held", "0:climb_side_ramp 2:cover_player"),
    "near_but_unengaged": Fixture("flank", {"release_at": 0, "turn_at": None}, (0,),
        "0:front_not_held", "0:cover_player"),
    "shield_turns_while_engaged": Fixture("flank", {"release_at": None, "turn_at": 2}, (0, 2),
        "0:front_held 2:front_not_held", "0:climb_side_ramp 2:cover_player"),
    "trap_clears": Fixture("lure", {"enemy_enters": 2, "player_exits": 3}, (0, 2, 3, 4),
        "0:armed_trap_seen 2:enemy_in_player_exposed 3:enemy_in_player_clear 4:activation_confirmed",
        "0:lure_enemy 2:hold_trigger 3:activate_trap 4:guard_player"),
    "player_stays_exposed": Fixture("lure", {"enemy_enters": 2, "player_exits": None}, (0, 2),
        "0:armed_trap_seen 2:enemy_in_player_exposed 5:lure_expired",
        "0:lure_enemy 2:hold_trigger 5:guard_player"),
    "enemy_never_arrives": Fixture("lure", {"enemy_enters": None, "player_exits": 3}, (0, 3),
        "0:armed_trap_seen 3:player_clear_enemy_outside 5:lure_expired", "0:lure_enemy 5:guard_player"),
    "player_clears_first": Fixture("lure", {"enemy_enters": 2, "player_exits": 1}, (0, 1, 2, 3),
        "0:armed_trap_seen 1:player_clear_enemy_outside 2:enemy_in_player_clear 3:activation_confirmed",
        "0:lure_enemy 2:activate_trap 3:guard_player"),
}

SEARCH_START = "0:aim_at_visible_enemy 1:reach_last_seen_corner 2:inspect_corner"
SEARCH_REVIEW = "0:visual_contact 1:lost_visual 2:corner_reached"

for name, hidden_side, sound_side, emerges_at, changed, reviews, starts in (
    ("hidden_left", "left", "left", None, (0, 1, 2, 3, 4), "3:unidentified_footstep 5:search_expired", "3:check_sound 5:guard_player"),
    ("hidden_right", "right", "left", None, (0, 1, 2, 3, 4), "3:unidentified_footstep 5:search_expired", "3:check_sound 5:guard_player"),
    ("sound_on_right", "right", "right", None, (0, 1, 2, 3, 4), "3:unidentified_footstep 5:search_expired", "3:check_sound 5:guard_player"),
    ("silent_corner", "right", None, None, (0, 1, 2), "5:search_expired", "5:guard_player"),
    ("visual_reacquisition", "right", "left", 4, (0, 1, 2, 3, 4), "3:unidentified_footstep 4:visual_reacquired", "3:check_sound 4:aim_at_visible_enemy"),
):
    FIXTURES[name] = Fixture("search", {"hidden_side": hidden_side, "sound_side": sound_side, "emerges_at": emerges_at},
                             changed, SEARCH_REVIEW + " " + reviews, SEARCH_START + " " + starts)


# Each tuple names the only world input that may differ and the first predicted difference in
# (observations, reconsideration, physically executing command). Identical hidden worlds stay indistinguishable.
PAIRS = (
    ("pursue_without_threat", "threat_before_takeoff", "cast_at", (1, 1, 1)),
    ("threat_before_takeoff", "threat_midair", "cast_at", (1, 1, 1)),
    ("pursue_without_threat", "threat_midair", "cast_at", (3, 3, 4)),
    ("threat_at_takeoff", "threat_midair", "cast_at", (2, 2, 2)),
    ("shield_pinned", "front_released", "release_at", (2, 2, 2)),
    ("shield_pinned", "near_but_unengaged", "release_at", (0, 0, 0)),
    ("shield_pinned", "shield_turns_while_engaged", "turn_at", (2, 2, 2)),
    ("trap_clears", "player_stays_exposed", "player_exits", (3, 3, 3)),
    ("trap_clears", "enemy_never_arrives", "enemy_enters", (2, 2, 2)),
    ("trap_clears", "player_clears_first", "player_exits", (1, 1, 2)),
    ("hidden_left", "hidden_right", "hidden_side", (None, None, None)),
    ("hidden_right", "sound_on_right", "sound_side", (3, None, 3)),
    ("hidden_right", "silent_corner", "sound_side", (3, 3, 3)),
    ("hidden_right", "visual_reacquisition", "emerges_at", (4, 4, 4)),
)


def named_events(rows, key):
    return " ".join(f"{r['t']}:{r[key]['verb'] if key == 'begin' else r[key]}" for r in rows if r[key])


def validate_roles(rows, fixture):
    for r in rows:
        f, command, intent = r["observed"], r["executing"], r["intent"]
        verb = command["verb"]
        assert command["actor"] == SISTER and intent["protected_actor"] == PLAYER
        if verb in ("return_to_player", "guard_player", "cover_player"):
            assert command["target"] == PLAYER
        if verb == "jump_gap":
            assert command["point"] == "point:landing" and command["route"] == "route:gap"
        if fixture.scenario == "pursuit" and f["cast_target"] == PLAYER:
            assert intent["goal"] == "protect" and intent["opponent"] == CASTER
        if verb == "climb_side_ramp":
            assert command["route"] == "route:side_ramp" and command["point"] == "point:shield_side"
        if verb == "strike_side":
            assert command["target"] == SHIELD and command["point"] == "point:shield_side"
        if fixture.scenario == "lure":
            if intent["goal"] in ("lure", "activated"):
                assert intent["device"] == TRAP and intent["opponent"] == LURED
            if r["begin"] and verb == "activate_trap":
                assert command["target"] == TRAP and f["enemy_inside"] and not f["player_inside"] and f["ready"]
        if fixture.scenario == "search":
            if verb == "aim_at_visible_enemy":
                assert f["seen"] == MISSING and command["target"] == MISSING and command["point"] == f["seen_at"]
            if verb in ("reach_last_seen_corner", "inspect_corner"):
                assert command["point"] == "point:corner" and not command["target"]
            if verb == "check_sound":
                assert command["point"] == "point:" + fixture.settings["sound_side"] and not command["target"]
            if intent["goal"] == "search":
                assert intent["last_seen_at"] == 0 and intent["last_seen_point"] == "point:corner"


def evaluate():
    traces = {}
    for name, fixture in FIXTURES.items():
        rows = run(fixture.scenario, fixture.settings)
        assert rows == run(fixture.scenario, fixture.settings), name
        assert tuple(r["t"] for r in rows if r["changed"]) == fixture.observed_changes, name
        assert named_events(rows, "review") == fixture.reviews, (name, named_events(rows, "review"))
        assert named_events(rows, "begin") == fixture.starts, (name, named_events(rows, "begin"))
        validate_roles(rows, fixture)
        traces[name] = rows

    pairs = []
    for a, b, field, expected in PAIRS:
        left, right = FIXTURES[a], FIXTURES[b]
        assert left.scenario == right.scenario and left.settings.keys() == right.settings.keys()
        assert [k for k in left.settings if left.settings[k] != right.settings[k]] == [field]
        diffs = {key: [u["t"] for u, v in zip(traces[a], traces[b]) if u[key] != v[key]]
                 for key in ("observed", "review", "intent", "begin", "executing")}
        first = tuple(next(iter(diffs[key]), None) for key in ("observed", "review", "executing"))
        assert first == expected, (a, b, first, expected)
        if expected == (None, None, None):
            assert traces[a] == traces[b], "Hidden positions must not leak through any field"
        pairs.append({"cases": [a, b], "changed_world_input": field, "differing_ticks": diffs})
    return {
        "model": "companion_intent", "tick_seconds": 1, "ticks_per_case": END + 1,
        "case_count": len(FIXTURES), "pair_count": len(PAIRS),
        "assumptions": {"takeoff_after": 2, "flight_duration": 2, "return_walk": 2, "ramp_duration": 3,
                        "lure_timeout": LURE_LIMIT, "search_timeout_after_loss": SEARCH_LIMIT,
                        "corner_walk": 1, "clock": "simulation seconds"},
        "cases": {name: {"scenario": f.scenario, "world": f.settings, "expected_observation_change_ticks": f.observed_changes,
                         "expected_reviews": f.reviews, "expected_commands_started": f.starts, "trace": traces[name]}
                  for name, f in FIXTURES.items()},
        "pairs": pairs,
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", "--json", dest="output", type=Path, help="Write complete inputs, expected events and validated per-second traces")
    args = parser.parse_args()
    result = evaluate()
    if args.output:
        args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"{result['case_count']} cases, {result['pair_count']} one-input pairs, {result['ticks_per_case']} ticks per case, all expectations passed")
