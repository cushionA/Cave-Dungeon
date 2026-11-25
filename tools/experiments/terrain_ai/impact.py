"""Offline, one-lane game fixture. Outputs are decisions of the stated test policy."""
from collections import Counter
from itertools import product

GROUPS = {
    "発動方式": ["kind"],
    "効果の横幅": ["half_width"],
    "発動待ち": ["windup_ticks"],
    "効果の持続": ["active_ticks"],
    "敵の開始位置": ["enemy_x"],
    "敵の移動": ["enemy_dx"],
    "敵の消失": ["enemy_gone_at"],
    "巻き込み候補の開始位置": ["other_x"],
    "巻き込み候補の移動": ["other_dx"],
    "巻き込み候補の関係": ["other_role"],
    "味方への命中可否": ["can_hit_ally"],
    "自分への命中可否": ["can_hit_self"],
}
POLICY = (
    "仮想1次元レーンの点キャラと、両端を含む効果区間を毎tick照合する。"
    "接触板は誰の接触でも0～3tickの最初の一回だけ作動し、範囲物は指示時に起動する。"
    "落下物は待ち時間後に高さ3から各tickで下向き速度を1ずつ増して着地した時に効果が始まる。"
    "消失tick以降の敵は判定対象外。持続中に敵へ一度以上命中し、命中設定の対象である味方・自分を"
    "一度も巻き込まない場合だけactivateとする。各初期値・移動は仮想の格子単位。"
)

DOMAINS = {
    "kind": ("contact", "area", "drop"),
    "half_width": (1, 2),
    "windup_ticks": (0, 1),
    "active_ticks": (1, 3),
    "enemy_x": (-2, 0, 2),
    "enemy_dx": (-1, 0, 1),
    "enemy_gone_at": (-1, 2),  # -1 leaves the enemy present for the whole experiment.
    "other_x": (-2, 0, 2),
    "other_dx": (-1, 0, 1),
    "other_role": ("ally", "self"),
    "can_hit_ally": (False, True),
    "can_hit_self": (False, True),
}

LAST_PRESS = 3
DROP_HEIGHT = 3


def outcome(kind, half_width, windup_ticks, active_ticks, enemy_x, enemy_dx,
            enemy_gone_at, other_x, other_dx, other_role, can_hit_ally, can_hit_self):
    # Actors are sampled at their current positions before moving to the next grid tick.
    # A pressure plate responds to occupancy even when the occupant is immune to the effect.
    effect_start = windup_ticks if kind == "area" else None
    drop_y, drop_speed = DROP_HEIGHT, 0
    other_vulnerable = can_hit_ally if other_role == "ally" else can_hit_self
    enemy_hit = False
    # This covers the last possible contact, the full delay and the entire active period.
    # DROP_HEIGHT bounds falling time when at least one unit of descent occurs per tick.
    last_tick = max(LAST_PRESS, DROP_HEIGHT) + windup_ticks + active_ticks - 1
    for tick in range(last_tick + 1):
        enemy_present = enemy_gone_at < 0 or tick < enemy_gone_at
        enemy_inside = enemy_present and abs(enemy_x) <= half_width
        other_inside = abs(other_x) <= half_width
        if effect_start is None:
            if kind == "contact" and tick <= LAST_PRESS and (enemy_inside or other_inside):
                effect_start = tick + windup_ticks
            elif kind == "drop" and tick > windup_ticks:
                drop_speed += 1
                drop_y -= drop_speed
                if drop_y <= 0:
                    effect_start = tick

        if effect_start is not None and effect_start <= tick < effect_start + active_ticks:
            if other_inside and other_vulnerable:
                return "hold"
            enemy_hit |= enemy_inside

        enemy_x += enemy_dx
        other_x += other_dx

    return "activate" if enemy_hit else "hold"


def cases():
    for values in product(*DOMAINS.values()):
        inputs = dict(zip(DOMAINS, values))
        yield inputs, outcome(**inputs)


def sanity_checks():
    simple = dict(kind="area", half_width=1, windup_ticks=0, active_ticks=1,
                  enemy_x=2, enemy_dx=0, enemy_gone_at=-1, other_x=2, other_dx=0,
                  other_role="ally", can_hit_ally=False, can_hit_self=False)

    def check(expected, **changes):
        actual = outcome(**(simple | changes))
        if actual != expected:
            raise AssertionError((changes, expected, actual))

    check("hold")                                # One grid unit outside.
    check("activate", half_width=2)              # Exactly on the included edge.
    check("hold", enemy_x=-2, enemy_dx=1)
    check("activate", kind="drop", enemy_x=-2, enemy_dx=1)
    check("activate", enemy_x=0, enemy_dx=1)
    check("hold", kind="drop", enemy_x=0, enemy_dx=1)
    check("hold", kind="drop", enemy_x=-2, enemy_dx=1, enemy_gone_at=2)
    # A friendly can expend the plate early even if immune. At tick 0, the enemy is still outside.
    check("hold", kind="contact", enemy_dx=-1, other_x=0)
    check("activate", kind="contact", enemy_dx=-1)
    # At the delayed tick or during a persistent effect, a moving ally first touches the edge.
    check("activate", enemy_x=0, other_dx=-1, can_hit_ally=True)
    check("hold", enemy_x=0, other_dx=-1, can_hit_ally=True, windup_ticks=1)
    check("hold", enemy_x=0, other_dx=-1, can_hit_ally=True, active_ticks=3)
    check("activate", enemy_x=0, other_x=0, other_role="self", can_hit_ally=True)
    check("hold", enemy_x=0, other_x=0, other_role="self", can_hit_self=True)


if __name__ == "__main__":
    sanity_checks()
    print(dict(Counter(result for _, result in cases())))
