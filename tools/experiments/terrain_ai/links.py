"""A virtual switch, bridge and glowing tile. No Unity or external dependencies."""
from collections import Counter
from heapq import heappop, heappush
from itertools import product

# Each layout lists authored, directed links between specific scene instances.
LAYOUTS = {
    "bridge_only": {"switch": ("bridge",)},
    "fork": {"switch": ("bridge", "left"), "left": ("glow",)},
    "chain": {"switch": ("left",), "left": ("bridge", "glow")},
    "diamond": {
        "switch": ("left", "right"), "left": ("bridge", "glow"), "right": ("bridge", "glow")
    },
    "cycle": {
        "switch": ("left",), "left": ("right", "glow"), "right": ("left", "bridge")
    },
}

GROUPS = {
    "有向リンクの配置": ["layout"],
    "中継器の伝達待ち": ["relay_ticks"],
    "使用済みの仕掛け": ["spent"],
    "橋の初期状態": ["bridge_open"],
    "出口を渡るtick": ["cross_tick"],
    "足場が塞がるtick": ["block_from"],
    "光る床に味方が乗るtick": ["ally_on_glow"],
}

DOMAINS = {
    "layout": tuple(LAYOUTS),
    "relay_ticks": (1, 2),
    "spent": ("none", "left", "glow"),
    "bridge_open": (False, True),
    "cross_tick": (1, 2, 3),
    "block_from": (-1, 2, 4),  # -1 means the exit tile stays passable.
    "ally_on_glow": ("never", "at_1", "at_2", "at_3", "always"),
}

POLICY = (
    "仮想パズルで報酬スイッチを押すか判定する。スイッチはtick 0に起動し、"
    "直接の隣へ1tick後、中継器は受信してから指定tick後に全ての出力先へ伝える。"
    "各仕掛けは1回の起動につき最初の到着だけに反応する。使用済みなら受信も転送もしない。"
    "橋の仕掛けは開閉を反転し、光る床は発動tickだけ光る。同tickでは発動後に渡る。"
    "予定tickに橋が開いて出口の足場が通れ、かつ伝達終了まで一度も味方の足元を光らせない時だけ"
    "activateとする。block_from=-1は足場の閉塞なし。"
)


def activations(layout, relay_ticks, spent, direct_only=False):
    links = LAYOUTS[layout]
    pending = [(0, "switch")]
    first_tick = {}
    while pending:
        tick, node = heappop(pending)
        if node == spent or node in first_tick:
            continue
        first_tick[node] = tick
        if direct_only and node != "switch":
            continue
        # Positive delays and at most one dispatch per distinct node bound the queue,
        # even when several links reach the same node or form a cycle.
        delay = 1 if node == "switch" else relay_ticks
        for target in links.get(node, ()):
            heappush(pending, (tick + delay, target))
    return first_tick


def outcome(layout, relay_ticks, spent, bridge_open, cross_tick, block_from, ally_on_glow,
            *, direct_only=False):
    first_tick = activations(layout, relay_ticks, spent, direct_only)
    if "bridge" in first_tick and first_tick["bridge"] <= cross_tick:
        bridge_open = not bridge_open
    tile_blocked = block_from >= 0 and block_from <= cross_tick
    glow_tick = first_tick.get("glow")
    ally_hit = glow_tick is not None and ally_on_glow in ("always", f"at_{glow_tick}")
    return "activate" if bridge_open and not tile_blocked and not ally_hit else "hold"


def cases():
    for values in product(*DOMAINS.values()):
        inputs = dict(zip(DOMAINS, values))
        yield inputs, outcome(**inputs)


def sanity_checks():
    # Cross at tick 2. The fork opens the bridge at 1 and lights the floor at 2.
    base = dict(layout="fork", relay_ticks=1, spent="none", bridge_open=False,
                cross_tick=2, block_from=-1, ally_on_glow="never")

    def check(expected, **changes):
        actual = outcome(**(base | changes))
        if actual != expected:
            raise AssertionError((changes, expected, actual))

    check("activate")
    check("hold", ally_on_glow="at_2")
    check("activate", ally_on_glow="at_1")
    check("activate", ally_on_glow="at_2", direct_only=True)
    check("activate", ally_on_glow="at_2", spent="left")
    check("activate", ally_on_glow="at_2", spent="glow")
    check("activate", ally_on_glow="at_2", relay_ticks=2)
    check("hold", bridge_open=True)
    check("hold", block_from=2)
    check("activate", block_from=4)
    check("hold", cross_tick=1, ally_on_glow="at_2")  # Hazard after crossing still counts.
    check("activate", layout="chain")                 # Opens on the crossing tick itself.
    check("hold", layout="chain", direct_only=True)
    check("hold", layout="chain", spent="left")
    check("hold", layout="cycle")
    check("activate", layout="cycle", cross_tick=3)
    # Two simultaneous arrivals must toggle the bridge once, not twice.
    assert activations("diamond", 1, "none") == {
        "switch": 0, "left": 1, "right": 1, "bridge": 2, "glow": 2,
    }
    check("activate", layout="diamond", spent="left")
    assert activations("cycle", 1, "none") == {
        "switch": 0, "left": 1, "right": 2, "glow": 2, "bridge": 3,
    }


if __name__ == "__main__":
    sanity_checks()
    comparison = Counter((outcome(**inputs, direct_only=True), result) for inputs, result in cases())
    print("direct-only / full network:", sorted(comparison.items()))
