"""Fixture-only 1-D encounter. Both object sockets accept the same device type."""
from itertools import product

POLICY = (
    "直線通路の観測者を0、同種装置のソケットを2・4、護衛対象を4に固定。"
    "選択中の敵31/46は位置3/5にいる。観測発行・視距離・遮蔽から可視ソケットを決め、"
    "両方を観測して初めて周辺の実数を確定する。作動中かつ標的から1以内が利用候補。"
    "同距離は小さい物体IDを選ぶ。標的を変えたら旧候補は使わない。回避は自分の進路を読み、"
    "護衛は味方を巻き込む装置を避け、見えている作動装置のそばでは護衛位置を守る。"
)
GROUPS = {
    "行動モード": ["mode"],
    "観測を発行したか": ["queried"],
    "観測者の視距離": ["view_range"],
    "遮蔽位置": ["wall_x"],
    "候補を採取した標的": ["sample_target"],
    "判定時の標的": ["target"],
    "同型物体の識別子": ["near_id", "far_id"],
    "物体の作動状態": ["near_armed", "far_armed"],
}

TARGET_X = {31: 3, 46: 5}
SOCKETS = (("near", 2), ("far", 4))
WARD_X = 4


def observe(fields):
    """None means an unobserved count; an individually seen disabled device has armed=False."""
    records = []
    surveyed = 0
    for side, x in SOCKETS:
        visible = (fields["queried"] and x <= fields["view_range"]
                   and (fields["wall_x"] == 0 or x < fields["wall_x"]))
        if not visible:
            continue
        surveyed += 1
        identity = fields[side + "_id"]
        if identity:
            records.append((x, identity, fields[side + "_armed"]))
    count = len(records) if surveyed == len(SOCKETS) else None
    return records, count


def choose(fields, records, count):
    mode = fields["mode"]
    if mode == "avoid":
        # This rule reads the owner's next step (x=2), independent of the selected enemy.
        if any(armed and x == 2 for x, _, armed in records):
            return "sidestep"
        return "advance" if count is not None else "scan"

    if fields["sample_target"] != fields["target"]:
        return "refresh-target-candidates"
    target_x = TARGET_X[fields["sample_target"]]
    usable = [(abs(x - target_x), object_id) for x, object_id, armed in records
              if armed and abs(x - target_x) <= 1
              and (mode != "guard" or abs(x - WARD_X) > 1)]
    if usable:
        return "use:" + str(min(usable)[1])
    if mode == "guard" and any(armed and abs(x - WARD_X) <= 1 for x, _, armed in records):
        return "hold-guard-position"
    if count is None:
        return "scan"
    if count == 0 and mode == "guard":
        return "follow-ward"
    return "intercept" if mode == "guard" else "pursue"


def cases():
    # None denotes an empty socket with no device state. IDs cannot occupy both sockets.
    for near, far in product((0, 17, 41), repeat=2):
        if near and near == far:
            continue
        for near_armed, far_armed in product((False, True) if near else (None,),
                                             (False, True) if far else (None,)):
            for mode, queried, radius, wall, sampled, target in product(
                    ("pursuit", "guard", "avoid"), (False, True),
                    (2, 4), (0, 1, 3), TARGET_X, TARGET_X):
                fields = dict(mode=mode, queried=queried, view_range=radius, wall_x=wall,
                              sample_target=sampled, target=target, near_id=near, far_id=far,
                              near_armed=near_armed, far_armed=far_armed)
                records, count = observe(fields)
                yield fields, choose(fields, records, count)


def sanity():
    full = dict(mode="pursuit", queried=True, view_range=4, wall_x=0,
                sample_target=31, target=31, near_id=41, far_id=17,
                near_armed=True, far_armed=True)

    def outcome(**updates):
        fields = dict(full, **updates)
        return choose(fields, *observe(fields))

    # Equal distance; stable concrete ID bounds the result even if the enumeration is reversed.
    records, count = observe(full)
    assert choose(full, records, count) == choose(full, list(reversed(records)), count) == "use:17"
    assert outcome(near_id=17, far_id=41) == "use:17"
    assert outcome(near_armed=False, far_armed=False) == "pursue"
    assert outcome(mode="guard") == "use:41"
    assert outcome(mode="avoid") == "sidestep"
    # Positive evidence from a partially surveyed area is still actionable.
    assert outcome(view_range=2) == outcome(wall_x=3) == "use:41"
    assert outcome(wall_x=1) == outcome(queried=False) == "scan"
    assert outcome(target=46) == "refresh-target-candidates"
    assert outcome(target=46, sample_target=46) == "use:17"
    assert outcome(mode="avoid", target=46) == "sidestep"

    # An unqueried/partially queried count, a fully observed 0, and a known false are distinct.
    empty = dict(full, mode="guard", near_id=0, far_id=0, near_armed=None, far_armed=None)
    assert observe(empty) == ([], 0) and choose(empty, *observe(empty)) == "follow-ward"
    for changed in (dict(queried=False), dict(view_range=2), dict(wall_x=3)):
        partial = dict(empty, **changed)
        assert observe(partial) == ([], None) and choose(partial, *observe(partial)) == "scan"
    spent = dict(empty, near_id=17, near_armed=False)
    assert observe(spent) == ([(2, 17, False)], 1) and choose(spent, *observe(spent)) == "intercept"


if __name__ == "__main__":
    from collections import Counter

    sanity()
    outputs = Counter()
    seen = set()
    for fields, result in cases():
        key = tuple(fields.items())
        assert key not in seen
        seen.add(key)
        assert result == choose(fields, list(reversed(observe(fields)[0])), observe(fields)[1])
        outputs[result] += 1
    print(len(seen), sorted(outputs.items()))
