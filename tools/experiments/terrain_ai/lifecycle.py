"""Small event-delivery model for an AI-selected game prop. No Unity runtime involved."""
from dataclasses import dataclass
from itertools import permutations, product

POLICY = (
    "全員が同じ時点の使用可能な環境物を観測し、行動選択時に標的と操作物の参照を別々に固定する。"
    "受付が予約を取り、効果発生時に物体・標的の世代、取消と判定変更を再確認する。"
    "観測tickは受付まで照合し、受付後の通常のtick更新だけでは予約を取り消さない。"
    "競合・失効した要求は通常行動に戻して再評価し、同じ要求や効果の再送には副作用を認めない。"
    "使い切りは発動時に消費し、再使用型は発動からモデル内の2ステップ後に利用可と仮定する。"
)

GROUPS = {
    "利用方式": ["usage"],
    "受付と効果の配信順": ["delivery_order"],
    "実行前後の変更": ["world_change"],
    "変更のタイミング": ["change_after"],
    "操作対象と戦闘標的の区別": ["a_recipient"],
    "既配信の要求や効果の再送": ["replay"],
    "新しい要求までの経過時間": ["fresh_delay"],
}

# A and B denote the actors. A/a (B/b) are that actor's commit/effect delivery.
ORDERS = tuple("".join(p) for p in permutations("AaBb") if p.index("A") < p.index("a") and p.index("B") < p.index("b"))
CHANGES = ("none", "remove-prop", "replace-prop", "replace-target", "revise-A", "advance-snapshot", "cancel-A")
REPLAYS = ("none", "old-commits", "old-effects", "old-commits-and-effects")


@dataclass(frozen=True)
class Ref:
    kind: str
    number: int
    generation: int


@dataclass
class Prop:
    ref: Ref
    reserved_by: str = ""
    consumed: bool = False
    ready_at: int = 0


@dataclass(frozen=True)
class Decision:
    actor: str
    target: Ref
    recipient: Ref
    tick: int
    revision: int


class World:
    def __init__(self, usage):
        self.usage = usage
        # The same number in the two namespaces makes an accidental cross-target write observable.
        self.prop = Prop(Ref("prop", 7, 1))
        self.target = Ref("actor", 7, 1)
        self.revisions = {"A": 1, "B": 1}
        self.snapshot = 1
        self.now = 0
        self.decisions = {}
        self.states = {}
        self.accepted = []
        self.fired = []
        self.fallback = []

    def observe_and_select(self, request, actor, recipient):
        if self.prop is None or self.prop.consumed or self.prop.ready_at > self.now or self.prop.reserved_by:
            self.states[request] = "fallback"
            self.fallback.append(request)
            return
        selected_recipient = self.target if recipient == "actor" else self.prop.ref
        self.decisions[request] = Decision(actor, self.target, selected_recipient, self.snapshot, self.revisions[actor])
        self.states[request] = "selected"

    def current(self, d):
        return (self.prop is not None and self.prop.ref == d.recipient and self.target == d.target
                and self.revisions[d.actor] == d.revision)

    def abandon(self, request):
        if self.states.get(request) not in ("selected", "accepted"):
            return
        if self.prop is not None and self.prop.reserved_by == request:
            self.prop.reserved_by = ""
        self.states[request] = "fallback"
        self.fallback.append(request)

    def commit(self, request):
        if self.states.get(request) != "selected":
            return
        d = self.decisions[request]
        if (not self.current(d) or self.snapshot != d.tick or self.prop.reserved_by
                or self.prop.consumed or self.prop.ready_at > self.now):
            self.abandon(request)
            return
        self.prop.reserved_by = request
        self.states[request] = "accepted"
        self.accepted.append(request)

    def effect(self, request):
        if self.states.get(request) != "accepted":
            return
        if not self.current(self.decisions[request]) or self.prop.reserved_by != request:
            self.abandon(request)
            return
        self.prop.reserved_by = ""
        if self.usage == "single":
            self.prop.consumed = True
        else:
            self.prop.ready_at = self.now + 2
        self.states[request] = "spent"
        self.fired.append(request)

    def change(self, change):
        if change == "remove-prop":
            self.prop = None
        elif change == "replace-prop":
            self.prop = Prop(Ref("prop", 7, 2))
        elif change == "replace-target":
            self.target = Ref("actor", 7, 2)
        elif change == "revise-A":
            self.revisions["A"] += 1
        elif change == "advance-snapshot":
            self.snapshot += 1
        elif change == "cancel-A":
            self.abandon("A")


def simulate(inputs):
    w = World(inputs["usage"])
    w.observe_and_select("A", "A", inputs["a_recipient"])
    w.observe_and_select("B", "B", "prop")
    sequence = inputs["delivery_order"]
    for delivered in range(len(sequence) + 1):
        if delivered == inputs["change_after"]:
            w.change(inputs["world_change"])
        if delivered < len(sequence):
            packet = sequence[delivered]
            if packet.isupper():
                w.commit(packet)
            else:
                w.effect(packet.upper())

    delay = inputs["fresh_delay"]
    if delay >= 0:
        w.now += delay

    # Replay *old* request IDs even after rearming, before any new request can reserve the object.
    replay = inputs["replay"]
    for request in ("A", "B"):
        if replay in ("old-commits", "old-commits-and-effects"):
            w.commit(request)
        if replay in ("old-effects", "old-commits-and-effects"):
            w.effect(request)

    if delay >= 0:
        w.snapshot += 1
        w.observe_and_select("C", "A", "prop")  # C is a new request from the same actor as A.
        w.commit("C")
        w.effect("C")

    # Report actual lifecycle decisions, without encoding IDs/generations of unobserved world changes.
    return "受付={};効果={};再評価={}".format("".join(w.accepted) or "-", "".join(w.fired) or "-",
                                          "".join(sorted(w.fallback)) or "-")


def cases():
    for change in CHANGES:
        times = (-1,) if change == "none" else range(5)
        for timing, usage, order, recipient, replay, delay in product(
                times, ("single", "cooldown"), ORDERS, ("prop", "actor"), REPLAYS, (-1, 0, 1, 2)):
            inputs = dict(usage=usage, delivery_order=order, world_change=change, change_after=timing,
                          a_recipient=recipient, replay=replay, fresh_delay=delay)
            yield inputs, simulate(inputs)


def sanity():
    reference = dict(usage="single", delivery_order="ABab", world_change="none", change_after=-1,
                     a_recipient="prop", replay="none", fresh_delay=-1)
    def check(expected, **changes):
        actual = simulate({**reference, **changes})
        assert actual == expected, (changes, actual, expected)
    check("受付=A;効果=A;再評価=B")
    check("受付=B;効果=B;再評価=A", delivery_order="BAba")
    check("受付=B;効果=B;再評価=A", a_recipient="actor")
    check("受付=-;効果=-;再評価=AB", world_change="remove-prop", change_after=0)
    check("受付=A;効果=-;再評価=AB", world_change="replace-target", change_after=1)
    check("受付=AC;効果=C;再評価=AB", world_change="replace-prop", change_after=1, fresh_delay=0)
    check("受付=AB;効果=B;再評価=A", world_change="cancel-A", change_after=1)
    check("受付=A;効果=A;再評価=B", world_change="advance-snapshot", change_after=1)
    check("受付=-;効果=-;再評価=AB", world_change="advance-snapshot", change_after=0)
    check("受付=A;効果=A;再評価=BC", usage="cooldown", fresh_delay=1)
    check("受付=AC;効果=AC;再評価=B", usage="cooldown", fresh_delay=2, replay="old-commits-and-effects")
    check("受付=A;効果=A;再評価=BC", fresh_delay=2, replay="old-commits-and-effects")


if __name__ == "__main__":
    sanity()
    from collections import Counter
    outcomes = Counter(outcome for _, outcome in cases())
    print(sum(outcomes.values()), "unique input combinations,", len(outcomes), "distinct outcomes; sanity passed")
