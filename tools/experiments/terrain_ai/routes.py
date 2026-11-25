"""Offline topology probe. The fixed scene geometry and timings below are fixture choices."""
from functools import lru_cache
from heapq import heappop, heappush
from itertools import product


POLICY = (
    "固定した敵が効果中心の半径1以内なら、操作場所への最短経路を探す。"
    "歩行床・片方向ジャンプ・迂回橋は別の辺。遠方から攻撃で起動するには開始地点からの射線が必要で、"
    "操作場所まで接近すれば至近距離で攻撃できる。受付は開始時刻以降、操作完了は期限を含む。"
    "1tickはこの試験の0.25秒。実ゲームの移動速度・AIの動作を表す数値ではない。"
)
GROUPS = {
    "operation_position": ["operation_x"],
    "effect_region": ["effect_center_x"],
    "target_position": ["target_x"],
    "activation_method": ["activation"],
    "line_of_sight": ["clear_from_start"],
    "floor_topology": ["crossing"],
    "jump_ability": ["can_jump"],
    "link_direction": ["jump_direction"],
    "landing_condition": ["jump_landing"],
    "alternate_route": ["bridge_open"],
    "opening": ["open_tick"],
    "deadline": ["close_tick"],
}


@lru_cache(maxsize=None)
def shortest_route(operation_x, crossing, can_jump, jump_direction, jump_landing, bridge_open):
    # Actor starts at x=-4; west/east banks at -2/+2. The bridge detours off this line.
    # Each edge contains (destination, travel ticks, traversal); all times are positive.
    graph = {
        "start": [("west", 4, "walk")],
        "west": [],
        "east": [("east_control", 4, "walk")],
        "bridge_turn": [("east", 8, "bridge")],
        "east_control": [],
    }
    if crossing == "solid":
        graph["west"].append(("east", 6, "walk"))
    if can_jump and jump_landing == "clear":
        origin, destination = ("west", "east") if jump_direction == "east" else ("east", "west")
        graph[origin].append((destination, 8, "jump"))
    if bridge_open:
        graph["west"].append(("bridge_turn", 6, "bridge"))

    goal = {-2: "west", 4: "east_control"}[operation_x]
    frontier = [(0, "start", ())]
    visited = set()
    while frontier:
        ticks, node, traversals = heappop(frontier)
        if node in visited:
            continue
        visited.add(node)
        if node == goal:
            for special in ("jump", "bridge"):
                if special in traversals:
                    return ticks, special
            return ticks, "walk"
        for target, duration, traversal in graph[node]:
            heappush(frontier, (ticks + duration, target, traversals + (traversal,)))
    return None


def outcome(row):
    if abs(row["target_x"] - row["effect_center_x"]) > 1:
        return "fallback"
    if row["activation"] == "shoot" and row["clear_from_start"]:
        arrival, route = 0, "stationary"
    else:
        path = shortest_route(row["operation_x"], row["crossing"], row["can_jump"],
                              row["jump_direction"], row["jump_landing"], row["bridge_open"])
        if path is None:
            return "fallback"
        arrival, route = path
    finish = max(arrival, row["open_tick"]) + 1
    if finish > row["close_tick"]:
        return "fallback"
    waiting = "_wait" if arrival < row["open_tick"] else ""
    return f'{route}{waiting}_{row["activation"]}'


def check_examples():
    row = dict(operation_x=4, effect_center_x=0, target_x=1, activation="touch",
               clear_from_start=False, crossing="gap", can_jump=True, jump_direction="east",
               jump_landing="clear", bridge_open=True, open_tick=0, close_tick=23)
    assert outcome(row) == "jump_touch"
    assert outcome(dict(row, jump_direction="west")) == "bridge_touch"
    assert outcome(dict(row, jump_landing="occupied")) == "bridge_touch"
    assert outcome(dict(row, jump_landing="missing", bridge_open=False)) == "fallback"
    assert outcome(dict(row, crossing="solid")) == "walk_touch"
    assert outcome(dict(row, can_jump=False, close_tick=22)) == "fallback"
    assert outcome(dict(row, close_tick=17)) == "jump_touch"  # 4+8+4 travel + 1 action
    assert outcome(dict(row, close_tick=16)) == "fallback"
    assert outcome(dict(row, open_tick=20)) == "jump_wait_touch"
    assert outcome(dict(row, activation="shoot")) == "jump_shoot"
    assert outcome(dict(row, activation="shoot", clear_from_start=True, close_tick=1)) == "stationary_shoot"
    assert outcome(dict(row, clear_from_start=True, close_tick=1)) == "fallback"
    assert outcome(dict(row, operation_x=-2, close_tick=5)) == "walk_touch"
    assert outcome(dict(row, target_x=2)) == "fallback"
    assert outcome(dict(row, effect_center_x=6)) == "fallback"


def cases():
    check_examples()
    fields = ["operation_x", "effect_center_x", "target_x", "activation", "clear_from_start",
              "crossing", "can_jump", "jump_direction", "jump_landing", "bridge_open"]
    dimensions = [(-2, 4), (0, 6), (1, 2, 6), ("touch", "shoot"), (False, True),
                  ("solid", "gap"), (False, True), ("east", "west"),
                  ("clear", "occupied", "missing"), (False, True)]
    # Paired open ticks isolate early arrival. Selected deadlines straddle the exact completion
    # ticks of stationary, nearby, uninterrupted, jumping and bridge routes.
    windows = ((0, 1), (0, 5), (0, 15), (0, 16), (0, 17), (0, 22), (0, 23), (20, 23))
    for values in product(*dimensions):
        inputs = dict(zip(fields, values))
        for opening, closing in windows:
            row = dict(inputs, open_tick=opening, close_tick=closing)
            yield row, outcome(row)
