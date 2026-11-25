"""Operating a scene switch after arriving: compare increasingly complete timing policies."""
from itertools import product

POLICY = '最速で操作するAIを仮定する。現在の行動が解除されたら移動し、操作の予備動作を終えて起動する。受付最終tickを含み、効果時に敵が区画内かつ味方が区画外なら採用する。'
GROUPS = {
    '現在行動の解除まで': ['locked'],
    '操作地点までの移動': ['travel'],
    '操作の予備動作': ['windup'],
    '起動後の遅延': ['delay'],
    '起動受付の最終tick': ['last_start'],
    '敵の滞在区間': ['enemy_enters'],
    '味方の進入時刻': ['ally_enters'],
}
BASE = ((0, 2), (0, 2, 4), (0, 1), (0, 2), (2, 5, 8), (0, 2, 4, 6), (-1, 3, 6))
HOLDOUT = ((1, 3), (1, 3, 5), (0, 2), (1, 3), (3, 6, 9), (1, 3, 5, 7), (-1, 4, 7))
KEYS = tuple(next(iter(group)) for group in GROUPS.values())


def safe_effect(s, tick):
    target_present = s['enemy_enters'] <= tick < s['enemy_enters'] + 3
    ally_present = s['ally_enters'] >= 0 and tick >= s['ally_enters']
    return target_present and not ally_present


def replay(s):
    # The oracle advances the executor a tick at a time, then observes who was hit.
    stages = [s['locked'], s['travel'], s['windup']]
    delay = s['delay']
    fired = False
    for tick in range(max(s['last_start'], s['enemy_enters']) + delay + 4):
        if not fired:
            if tick > s['last_start']:
                return 'hold'
            while stages and stages[0] == 0:
                stages.pop(0)
            if stages:
                stages[0] -= 1
                continue
            fired = True
        if delay == 0:
            return 'use' if safe_effect(s, tick) else 'hold'
        delay -= 1
    raise AssertionError(s)


def decisions(s):
    def predict(t, delay):
        effect = t + delay
        return 'use' if t <= s['last_start'] and safe_effect(s, effect) else 'hold'

    optimistic_start = s['travel'] + s['windup']
    return (predict(0, 0), predict(optimistic_start, s['delay']),
            predict(s['locked'] + optimistic_start, s['delay']))


def cases(domains=BASE):
    for values in product(*domains):
        inputs = dict(zip(KEYS, values))
        yield inputs, replay(inputs)


def progression(domains):
    errors = [{'false_use': 0, 'missed_use': 0} for _ in range(3)]
    total = 0
    for context, expected in cases(domains):
        total += 1
        for prediction, counter in zip(decisions(context), errors):
            if prediction != expected:
                counter['false_use' if prediction == 'use' else 'missed_use'] += 1
    return {'cases': total, 'errors_by_policy': errors}


def sanity():
    s = dict(locked=2, travel=2, windup=1, delay=2, last_start=5, enemy_enters=6, ally_enters=-1)
    assert replay(s) == 'use'                    # accepted on the last tick, hits on tick 7
    assert replay(dict(s, last_start=4)) == 'hold'
    assert replay(dict(s, ally_enters=7)) == 'hold'
    assert replay(dict(s, enemy_enters=4)) == 'hold'   # just after the target leaves
    assert replay(dict(s, locked=0)) == 'hold'
    assert progression(HOLDOUT)['errors_by_policy'][-1] == {'false_use': 0, 'missed_use': 0}


if __name__ == "__main__":
    sanity()
    print(progression(BASE), progression(HOLDOUT))
