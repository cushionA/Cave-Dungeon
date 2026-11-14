"""Prespecified, formally scored joint-plan problems for commander evaluation.

The domain families are fixed strata, not independent samples of all possible
gameplay. Several domains share a mathematical mechanism; ``mechanism_group``
preserves that fact for sensitivity analyses. Each Japanese row is one newly
sampled formal world. English rows are translations of a fixed subset.
"""
import copy
import hashlib
import itertools
import json
import random
from collections import Counter


DEFAULT_SEED = 2026110619
FAMILIES = (
    "role_eligibility", "exact_role_quota", "specialist_coverage", "arrival_deadline",
    "medkit_budget", "mana_budget", "stamina_threshold", "carry_capacity",
    "hazard_immunity", "stealth_budget", "ranged_cover", "unlock_skill",
    "blocker_strength", "zone_reach", "incompatible_pair", "cooperating_pair",
    "reserved_health", "role_cooldown", "ordered_prerequisites", "weather_capability",
    "robust_health", "weighted_utility", "lexicographic_priority", "dual_budget_capacity",
    "risk_then_reward",
)
ROLE_NAMES = {
    "block": ("block", "足止め"), "escort": ("escort", "護送"),
    "reserve": ("reserve", "待機"), "heal": ("heal", "治療"),
    "shield": ("shield", "結界"), "scout": ("scout", "偵察"),
    "shoot": ("ranged cover", "射撃援護"), "unlock": ("unlock", "解錠"),
    "north": ("guard north", "北側警備"), "east": ("guard east", "東側警備"),
}
ATTR_NAMES = {
    "eligible": ("qualified (1=yes,0=no)", "資格(1=有,0=無)"),
    "north_skill": ("north qualification", "北側の資格"), "east_skill": ("east qualification", "東側の資格"),
    "distance": ("distance", "距離"), "speed": ("speed", "速度"),
    "kits": ("kits consumed", "消費治療キット"), "mana": ("mana consumed", "消費マナ"),
    "stamina": ("stamina", "スタミナ"), "capacity": ("carry capacity", "運搬力"),
    "immune": ("poison immunity (1=yes,0=no)", "毒耐性(1=有,0=無)"),
    "noise": ("noise", "騒音"), "range": ("range", "射程"), "skill": ("lock skill", "解錠技量"),
    "strength": ("strength", "戦力"), "north_eta": ("north arrival time", "北側到着時間"),
    "east_eta": ("east arrival time", "東側到着時間"), "position": ("position", "位置"),
    "health": ("health", "体力"), "last_block": ("last block time", "前回足止め時刻"),
    "rainproof": ("rain protection grade", "防雨等級"),
    "heatproof": ("heat protection grade", "耐熱等級"),
    "poison_resistance": ("poison resistance", "毒耐性値"),
    "fire_resistance": ("fire resistance", "炎耐性値"),
    "health_min": ("minimum possible health", "体力の下限"),
    "health_max": ("maximum possible health", "体力の上限"),
    "power": ("power", "攻撃力"), "cost": ("cost", "費用"),
    "safety": ("safety", "安全度"), "risk": ("risk", "危険度"),
}


def _role(role, language):
    return ROLE_NAMES[role][0 if language == "en" else 1]


def _attribute(name, language):
    return ATTR_NAMES[name][0 if language == "en" else 1]


def _members(world, plan, role):
    return [unit for unit, assigned in plan["assignments"].items() if assigned == role]


def _metric(world, plan, term):
    return term.get("weight", 1) * sum(world["units"][unit][term["attribute"]]
                                      for unit in _members(world, plan, term["role"]))


def evaluate_plan(world, plan):
    """Evaluate structure, every hard rule, then the declared objective.

    This evaluator does not know a generated gold ID, candidate order, family
    name, or rendered text. Unknown/missing roles are always infeasible.
    """
    violations = []
    assignments = plan.get("assignments", {})
    if set(assignments) != set(world["units"]):
        return {"feasible": False, "violations": ["complete_assignment"], "score": None}
    if Counter(assignments.values()) != Counter(world["role_counts"]):
        violations.append("role_counts")
    for index, rule in enumerate(world["rules"]):
        op = rule["op"]
        members = _members(world, plan, rule.get("role"))
        if op in {"each_ge", "each_le", "sum_ge", "sum_le"}:
            values = [world["units"][unit][rule["attribute"]] for unit in members]
            if op == "each_ge":
                valid = all(value >= rule["value"] for value in values)
            elif op == "each_le":
                valid = all(value <= rule["value"] for value in values)
            elif op == "sum_ge":
                valid = sum(values) >= rule["value"]
            else:
                valid = sum(values) <= rule["value"]
        elif op == "arrival":
            valid = all((world["units"][unit]["distance"] + world["units"][unit]["speed"] - 1)
                        // world["units"][unit]["speed"] <= rule["value"] for unit in members)
        elif op == "combined_le":
            valid = sum(_metric(world, plan, term) for term in rule["terms"]) <= rule["value"]
        elif op == "forbidden_pairs":
            chosen = set(_members(world, plan, rule["roles"][0]) + _members(world, plan, rule["roles"][1]))
            valid = all(not set(pair).issubset(chosen) for pair in rule["pairs"])
        elif op == "within_distance":
            first = _members(world, plan, rule["roles"][0])
            second = _members(world, plan, rule["roles"][1])
            valid = all(abs(world["units"][a]["position"] - world["units"][b]["position"]) <= rule["value"]
                        for a in first for b in second)
        elif op == "reserve_low_health":
            valid = all(assignments[unit] == "reserve" for unit, values in world["units"].items()
                        if values["health"] < rule["value"])
        elif op == "cooldown":
            valid = all(rule["now"] - world["units"][unit]["last_block"] >= rule["value"] for unit in members)
        elif op == "before":
            sequence = plan.get("sequence", [])
            valid = (len(sequence) == len(set(sequence)) == len(world["sequence_roles"])
                     and set(sequence) == set(world["sequence_roles"])
                     and sequence.index(rule["first"]) < sequence.index(rule["second"]))
        elif op == "weather_eligibility":
            attribute = rule["attributes"][world["condition"]]
            valid = all(world["units"][unit][attribute] >= rule["value"] for unit in members)
        else:
            raise ValueError("Unknown formal commander rule: " + str(op))
        if not valid:
            violations.append(f"rule_{index}:{op}")
    objective = world.get("objective")
    if violations:
        score = None
    elif objective is None:
        score = [1]
    elif objective["kind"] == "weighted":
        score = [sum(_metric(world, plan, term) for term in objective["terms"])]
    elif objective["kind"] == "lexicographic":
        score = [_metric(world, plan, term) for term in objective["terms"]]
    else:
        raise ValueError("Unknown formal objective")
    return {"feasible": not violations, "violations": violations, "score": score}


def choose_plan(world, plans):
    evaluated = {key: evaluate_plan(world, plan) for key, plan in plans.items()}
    feasible = [key for key, value in evaluated.items() if value["feasible"]]
    if not feasible:
        raise ValueError("No supplied plan is feasible")
    maximum = max(evaluated[key]["score"] for key in feasible)
    winners = [key for key in feasible if evaluated[key]["score"] == maximum]
    if len(winners) != 1:
        raise ValueError("The supplied plans do not have a unique optimum")
    return winners[0]


def normalized_regret(world, plans):
    """Ordinal regret among supplied feasible plans; infeasible selections get 1.

    This is a bounded rank loss, not a calibrated difference in game success.
    """
    evaluated = {key: evaluate_plan(world, plan) for key, plan in plans.items()}
    levels = sorted({tuple(value["score"]) for value in evaluated.values() if value["feasible"]}, reverse=True)
    denominator = max(1, len(levels) - 1)
    return {key: levels.index(tuple(value["score"])) / denominator if value["feasible"] else 1.0
            for key, value in evaluated.items()}


def _numbers(rng, units, attributes):
    for attribute in attributes:
        for unit, value in zip(units.values(), rng.sample(range(2, 41), len(units))):
            unit[attribute] = value


def _only_best(world, role, attribute, direction):
    values = sorted(unit[attribute] for unit in world["units"].values())
    value = values[-1] if direction == "ge" else values[0]
    return {"op": "each_" + direction, "role": role, "attribute": attribute, "value": value}


def _make_world(rng, family, index):
    n = rng.randint(3, 5)
    ids = ["u" + str(number) for number in rng.sample(range(11, 99), n)]
    units = {unit: {} for unit in ids}
    world = {"units": units, "role_counts": {"block": 1, "escort": 1, "reserve": n - 2},
             "rules": [], "objective": None}
    mechanism = "individual_threshold"
    active = "block"
    if family == "role_eligibility":
        _numbers(rng, units, ["capacity"])
        selected = set(rng.sample(ids, rng.randint(1, n - 1)))
        for unit, values in units.items():
            values["eligible"] = int(unit in selected)
        world["rules"] = [{"op": "each_ge", "role": active, "attribute": "eligible", "value": 1},
                          {"op": "each_ge", "role": "escort", "attribute": "capacity",
                           "value": sorted(v["capacity"] for v in units.values())[n // 2]}]
        mechanism = "mixed_eligibility_capacity"
    elif family == "exact_role_quota":
        # Distinct count configurations, rather than repeated relabelings of one quota.
        options = [(count, a, b, c) for count in range(3, 6) for a in range(1, count)
                   for b in range(1, count - a + 1) for c in range(count - a - b + 1)]
        count, a, b, c = options[index % len(options)]
        ids = ["u" + str(number) for number in rng.sample(range(11, 99), count)]
        world["units"] = {unit: {} for unit in ids}
        world["role_counts"] = {"block": a, "escort": b, "scout": c, "reserve": count - a - b - c}
        mechanism = "exact_multiset"
    elif family in {"specialist_coverage", "zone_reach"}:
        world["role_counts"] = {"north": 1, "east": 1, "reserve": n - 2}
        if family == "specialist_coverage":
            _numbers(rng, units, ["north_skill", "east_skill"])
            world["rules"] = [_only_best(world, "north", "north_skill", "ge"),
                              {"op": "each_ge", "role": "east", "attribute": "east_skill",
                               "value": sorted(v["east_skill"] for v in units.values())[n // 2]}]
            mechanism = "joint_eligibility"
        else:
            _numbers(rng, units, ["north_eta", "east_eta"])
            world["rules"] = [_only_best(world, "north", "north_eta", "le"),
                              {"op": "each_le", "role": "east", "attribute": "east_eta",
                               "value": sorted(v["east_eta"] for v in units.values())[n // 2]}]
            mechanism = "joint_deadlines"
    elif family == "arrival_deadline":
        _numbers(rng, units, ["distance"])
        for values in units.values():
            values["speed"] = rng.randint(2, 7)
        times = sorted((v["distance"] + v["speed"] - 1) // v["speed"] for v in units.values())
        world["rules"] = [{"op": "arrival", "role": "block", "value": times[0]}]
        mechanism = "ratio_deadline"
    elif family in {"medkit_budget", "mana_budget", "stamina_threshold", "carry_capacity", "ranged_cover", "unlock_skill"}:
        specs = {"medkit_budget": ("heal", "kits", "le"), "mana_budget": ("shield", "mana", "le"),
                 "stamina_threshold": ("block", "stamina", "ge"), "carry_capacity": ("escort", "capacity", "ge"),
                 "ranged_cover": ("shoot", "range", "ge"), "unlock_skill": ("unlock", "skill", "ge")}
        role, attribute, direction = specs[family]
        if role not in {"block", "escort"}:
            world["role_counts"] = {role: 1, "escort": 1, "reserve": n - 2}
        _numbers(rng, units, [attribute])
        world["rules"] = [_only_best(world, role, attribute, direction)]
    elif family == "hazard_immunity":
        world["role_counts"] = {"scout": 1, "escort": 1, "reserve": n - 2}
        _numbers(rng, units, ["poison_resistance", "fire_resistance"])
        world["rules"] = [_only_best(world, "scout", "poison_resistance", "ge"),
                          {"op": "each_ge", "role": "escort", "attribute": "fire_resistance",
                           "value": sorted(v["fire_resistance"] for v in units.values())[n // 2]}]
        mechanism = "joint_eligibility"
    elif family == "stealth_budget":
        _numbers(rng, units, ["noise"])
        minimum = sum(sorted(v["noise"] for v in units.values())[:2])
        world["rules"] = [{"op": "combined_le", "terms": [
            {"role": "block", "attribute": "noise"}, {"role": "escort", "attribute": "noise"}], "value": minimum}]
        mechanism = "joint_sum_budget"
    elif family == "blocker_strength":
        if n < 4:
            return _make_world(rng, family, index)
        world["role_counts"] = {"block": 2, "escort": 1, "reserve": n - 3}
        _numbers(rng, units, ["strength"])
        limit = sum(sorted(v["strength"] for v in units.values())[-2:])
        world["rules"] = [{"op": "sum_ge", "role": "block", "attribute": "strength", "value": limit}]
        mechanism = "joint_sum_threshold"
    elif family == "incompatible_pair":
        pairs = list(itertools.combinations(ids, 2))
        forbidden = rng.sample(pairs, rng.randint(2, max(2, len(pairs) - 1)))
        world["rules"] = [{"op": "forbidden_pairs", "roles": ["block", "escort"],
                           "pairs": [list(pair) for pair in forbidden]}]
        mechanism = "pairwise_incompatibility"
    elif family == "cooperating_pair":
        _numbers(rng, units, ["position"])
        differences = sorted({abs(a["position"] - b["position"]) for a, b in itertools.combinations(units.values(), 2)})
        world["rules"] = [{"op": "within_distance", "roles": ["block", "escort"], "value": differences[0]}]
        mechanism = "pairwise_distance"
    elif family == "reserved_health":
        _numbers(rng, units, ["health"])
        limit = sorted(v["health"] for v in units.values())[1]
        world["rules"] = [{"op": "reserve_low_health", "value": limit}]
        mechanism = "conditional_reservation"
    elif family == "role_cooldown":
        _numbers(rng, units, ["last_block"])
        now = rng.randint(45, 70)
        earliest = min(v["last_block"] for v in units.values())
        world["rules"] = [{"op": "cooldown", "role": "block", "now": now, "value": now - earliest}]
        mechanism = "elapsed_time"
    elif family == "ordered_prerequisites":
        if n < 4:
            return _make_world(rng, family, index)
        world["role_counts"] = {"scout": 1, "unlock": 1, "heal": 1, "escort": 1, "reserve": n - 4}
        world["sequence_roles"] = ["scout", "unlock", "heal", "escort"]
        order = rng.sample(world["sequence_roles"], 4)
        edges = list(itertools.combinations(order, 2))
        world["rules"] = [{"op": "before", "first": first, "second": second}
                          for first, second in rng.sample(edges, rng.randint(1, 4))]
        mechanism = "temporal_order"
    elif family == "weather_capability":
        weather = rng.choice(["rain", "heat"])
        world["condition"] = weather
        attribute = "rainproof" if weather == "rain" else "heatproof"
        _numbers(rng, units, ["rainproof", "heatproof"])
        world["rules"] = [{"op": "weather_eligibility", "role": "block",
                           "attributes": {"rain": "rainproof", "heat": "heatproof"},
                           "value": max(values[attribute] for values in units.values())}]
        mechanism = "conditional_eligibility"
    elif family == "robust_health":
        _numbers(rng, units, ["health_min"])
        for values in units.values():
            values["health_max"] = values["health_min"] + rng.randint(1, 20)
        world["rules"] = [_only_best(world, "block", "health_min", "ge")]
        mechanism = "worst_case_interval"
    elif family in {"weighted_utility", "lexicographic_priority", "risk_then_reward"}:
        _numbers(rng, units, ["power", "speed", "cost", "safety"])
        if family == "lexicographic_priority":
            world["objective"] = {"kind": "lexicographic", "terms": [
                {"role": "escort", "attribute": "safety", "weight": 1},
                {"role": "block", "attribute": "power", "weight": 1},
                {"role": "escort", "attribute": "speed", "weight": 1}]}
            mechanism = "lexicographic_utility"
        else:
            world["objective"] = {"kind": "weighted", "terms": [
                {"role": "block", "attribute": "power", "weight": rng.randint(1, 4)},
                {"role": "escort", "attribute": "speed", "weight": rng.randint(1, 4)},
                {"role": "block", "attribute": "cost", "weight": -rng.randint(1, 3)}]}
            mechanism = "weighted_utility"
            if family == "risk_then_reward":
                _numbers(rng, units, ["risk"])
                risks = sorted(v["risk"] for v in units.values())
                world["rules"] = [{"op": "combined_le", "terms": [
                    {"role": "block", "attribute": "risk"}, {"role": "escort", "attribute": "risk"}],
                    "value": sum(risks[:2]) + rng.randint(2, 12)}]
                mechanism = "constraint_then_utility"
    elif family == "dual_budget_capacity":
        _numbers(rng, units, ["cost", "capacity"])
        capacities = sorted(v["capacity"] for v in units.values())
        costs = sorted(v["cost"] for v in units.values())
        world["rules"] = [{"op": "combined_le", "terms": [
            {"role": "block", "attribute": "cost"}, {"role": "escort", "attribute": "cost"}],
            "value": sum(costs[:2]) + rng.randint(3, 15)},
            {"op": "each_ge", "role": "escort", "attribute": "capacity", "value": capacities[-2]}]
        mechanism = "conjunctive_constraints"
    else:
        raise ValueError("Unknown commander family")
    world["role_counts"] = {key: value for key, value in world["role_counts"].items() if value}
    return world, mechanism


def _pool(world, family):
    units = list(world["units"])
    roles = list(world["role_counts"])
    if "reserve" not in roles:
        roles.append("reserve")
    result = []
    for assignments in itertools.product(roles, repeat=len(units)):
        if family != "exact_role_quota" and Counter(assignments) != Counter(world["role_counts"]):
            continue
        plan = {"assignments": dict(zip(units, assignments))}
        if "sequence_roles" in world:
            for sequence in itertools.permutations(world["sequence_roles"]):
                result.append({**plan, "sequence": list(sequence)})
        else:
            result.append(plan)
    return result


def _sample_world(rng, family, index):
    for _ in range(1000):
        world, mechanism = _make_world(rng, family, index)
        pool = _pool(world, family)
        good = [plan for plan in pool if evaluate_plan(world, plan)["feasible"]]
        bad = [plan for plan in pool if not evaluate_plan(world, plan)["feasible"]]
        if world["objective"] is None:
            if good and len(bad) >= 3:
                selected = [rng.choice(good)] + rng.sample(bad, 3)
            else:
                continue
        else:
            if len(good) < 4:
                continue
            selected = rng.sample(good, 4)
            if family == "risk_then_reward" and bad:
                selected[-1] = rng.choice(bad)
        labels = ["p" + str(value) for value in rng.sample(range(11, 99), 4)]
        plans = dict(zip(labels, selected))
        try:
            gold = choose_plan(world, plans)
        except ValueError:
            continue
        # No target-position shortcut: each family has exactly 3 golds at each position.
        others = [key for key in labels if key != gold]
        rng.shuffle(others)
        order = list(others)
        order.insert(index % 4, gold)
        return world, {key: plans[key] for key in order}, mechanism
    raise RuntimeError("Could not construct an unambiguous formal world: " + family)


def _rule_text(rule, language):
    en = language == "en"
    op = rule["op"]
    if op in {"each_ge", "each_le", "sum_ge", "sum_le"}:
        role, attribute = _role(rule["role"], language), _attribute(rule["attribute"], language)
        comparator = ">=" if op.endswith("ge") else "<="
        if en:
            prefix = "Every" if op.startswith("each") else "Sum over"
            return f"{prefix} {role}: {attribute} {comparator} {rule['value']}."
        prefix = "各隊員の" if op.startswith("each") else "隊員の合計"
        return f"{role}役の{prefix}{attribute}{comparator}{rule['value']}。"
    if op == "arrival":
        role = _role(rule["role"], language)
        return (f"Every {role} must arrive within {rule['value']} time units; arrival=ceil(distance/speed)." if en else
                f"{role}役は{rule['value']}時間単位以内に到着する。到着時間=距離÷速度の切り上げ。")
    if op == "combined_le":
        terms = [f"{_role(term['role'], language)}:{_attribute(term['attribute'], language)}" for term in rule["terms"]]
        return ("Sum of " if en else "次の合計") + "+".join(terms) + f" <= {rule['value']}."
    if op == "forbidden_pairs":
        pairs = ",".join("/".join(pair) for pair in rule["pairs"])
        return (f"No listed pair may jointly fill block and escort in either order: {pairs}." if en else
                f"次の組の2人を足止め役と護送役に同時に使ってはいけない(順不同):{pairs}。")
    if op == "within_distance":
        return (f"The positions of block and escort must differ by at most {rule['value']}." if en else
                f"足止め役と護送役の位置の差の絶対値は{rule['value']}以下。")
    if op == "reserve_low_health":
        return (f"Every unit with health below {rule['value']} must reserve." if en else
                f"体力が{rule['value']}未満の隊員は必ず待機。")
    if op == "cooldown":
        return (f"Current time={rule['now']}. A blocker needs current time-last block time >= {rule['value']}." if en else
                f"現在時刻={rule['now']}。足止め役は現在時刻−前回足止め時刻>={rule['value']}を満たす。")
    if op == "before":
        first, second = _role(rule["first"], language), _role(rule["second"], language)
        return f"{first} before {second}." if en else f"手順は{first}を{second}より先に行う。"
    if op == "weather_eligibility":
        return (f"If raining, each blocker needs rain protection grade>={rule['value']}; if hot, heat protection grade>={rule['value']}." if en else
                f"雨なら足止め役は全員防雨等級>={rule['value']}、高温なら全員耐熱等級>={rule['value']}であること。")
    raise ValueError("Unknown render rule")


def render_state(world, language):
    en = language == "en"
    counts = ", ".join(f"{_role(role, language)}={count}" for role, count in world["role_counts"].items())
    lines = [("Assign every unit exactly one role. Required counts: " if en else "全隊員に役割を1つずつ割り当てる。必要人数:") + counts + "."]
    if "condition" in world:
        lines.append(("Weather: " + world["condition"] + ".") if en else
                     ("天候:" + ("雨。" if world["condition"] == "rain" else "高温。")))
    for unit, attributes in world["units"].items():
        text = ",".join(f"{_attribute(key, language)}={value}" for key, value in attributes.items())
        lines.append(unit + (":" + text if text else ":" + ("available" if en else "行動可能")))
    if "sequence_roles" in world:
        roster = ",".join(_role(role, language) for role in world["sequence_roles"])
        lines.append(f"Required phases: {roster}; each exactly once." if en else f"手順対象:{roster}。各手順を必ず1回ずつ含める。")
    if any(rule.get("attribute") == "health_min" for rule in world["rules"]):
        lines.append("Health may take any value inside its bounds; the plan must be safe for all values." if en else "体力は示した上下限内の任意の値を取る。全ての値で条件を満たす必要がある。")
    lines.extend(_rule_text(rule, language) for rule in world["rules"])
    objective = world["objective"]
    if objective is None:
        lines.append("Select the only supplied plan meeting all constraints." if en else "全条件を満たす唯一の候補作戦を選ぶ。")
    else:
        terms = [f"{term['weight']}*{_role(term['role'], language)}:{_attribute(term['attribute'], language)}"
                 for term in objective["terms"]]
        if objective["kind"] == "weighted":
            lines.append(("Among feasible plans maximize the sum: " if en else "条件を満たす作戦の中で次の合計を最大化:") + "+".join(terms) + ".")
        else:
            lines.append(("Among feasible plans maximize these quantities lexicographically, first priority first: " if en else
                          "条件を満たす作戦の中で次の量を左から優先して最大化する。上位が同じ時だけ次を比較:") + ",".join(terms) + ".")
    return "\n".join(lines)


def render_plan(plan, language):
    text = ";".join(f"{unit}={_role(role, language)}" for unit, role in plan["assignments"].items())
    if "sequence" in plan:
        text += (";sequence=" if language == "en" else ";手順=") + "→".join(_role(role, language) for role in plan["sequence"])
    return text


def semantic_world_sha256(world):
    """Canonicalize arbitrary unit IDs, rule order and temporal redundancy.

    This is stronger than a raw JSON hash: pure unit renamings cannot become new
    worlds. It does not claim that distinct arithmetic inputs are new reasoning
    mechanisms; that limitation is retained in the mechanism-group metadata.
    """
    normalized = copy.deepcopy(world)
    if "sequence_roles" in normalized:
        roles = sorted(normalized["sequence_roles"])
        closure = {(rule["first"], rule["second"]) for rule in normalized["rules"] if rule["op"] == "before"}
        for pivot in roles:
            for first in roles:
                for second in roles:
                    if (first, pivot) in closure and (pivot, second) in closure:
                        closure.add((first, second))
        normalized["sequence_roles"] = roles
        normalized["rules"] = [rule for rule in normalized["rules"] if rule["op"] != "before"] + [
            {"op": "before", "first": first, "second": second} for first, second in sorted(closure)]

    def rename(value, mapping):
        if isinstance(value, dict):
            return {mapping.get(key, key): rename(item, mapping) for key, item in value.items()}
        if isinstance(value, list):
            return [rename(item, mapping) for item in value]
        return mapping.get(value, value) if isinstance(value, str) else value

    candidates = []
    units = list(normalized["units"])
    for order in itertools.permutations(units):
        mapping = {unit: f"v{index}" for index, unit in enumerate(order)}
        renamed = rename(normalized, mapping)
        for rule in renamed["rules"]:
            if "pairs" in rule:
                rule["pairs"] = sorted(sorted(pair) for pair in rule["pairs"])
        renamed["rules"] = sorted(renamed["rules"], key=lambda rule: json.dumps(rule, sort_keys=True))
        candidates.append(json.dumps(renamed, sort_keys=True, separators=(",", ":")))
    return hashlib.sha256(min(candidates).encode()).hexdigest()


def generate_cases(seed=DEFAULT_SEED, worlds_per_family=12, english_per_family=4):
    if not 1 <= worlds_per_family <= 34 or not 0 <= english_per_family <= worlds_per_family:
        raise ValueError("Invalid world/translation counts")
    cases = []
    for family in FAMILIES:
        selection_seed = hashlib.sha256(f"{seed}:{family}:translations".encode()).digest()
        english_indices = set(random.Random(selection_seed).sample(range(worlds_per_family), english_per_family))
        seen_semantic_worlds = set()
        for index in range(worlds_per_family):
            # A scenario ID always identifies the same world, independent of
            # requested corpus size and which translations are exported.
            world_seed = hashlib.sha256(f"{seed}:{family}:{index}:world".encode()).digest()
            rng = random.Random(world_seed)
            for _ in range(1000):
                world, plans, mechanism = _sample_world(rng, family, index)
                semantic_signature = semantic_world_sha256(world)
                if semantic_signature not in seen_semantic_worlds:
                    seen_semantic_worlds.add(semantic_signature)
                    break
            else:
                raise RuntimeError("Could not generate a genuinely different world: " + family)
            gold = choose_plan(world, plans)
            scenario = f"commander_{family}_{seed}_{index:03d}"
            signature = hashlib.sha256(json.dumps(world, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
            audit = {"world": world, "plans": plans, "formal_world_sha256": signature,
                     "semantic_world_sha256": semantic_signature,
                     "plan_evaluations": {key: evaluate_plan(world, plan) for key, plan in plans.items()},
                     "normalized_regret": normalized_regret(world, plans),
                     "regret_definition": "Ordinal rank among supplied feasible plans; infeasible selections receive1. Not gameplay win-rate loss."}
            for language in (["ja", "en"] if index in english_indices else ["ja"]):
                case = {"case_id": scenario + "_" + language, "scenario_id": scenario, "pair_id": scenario,
                        "family": family, "mechanism_group": mechanism, "language": language,
                        "category": family, "suite": "commander",
                        "request": {"state": render_state(world, language), "questions": {"plan": {
                            "type": "choice", "instructions": "Select one supplied complete plan using every stated rule." if language == "en" else "記載された全ての規則に従い、提示された完成済み作戦を1つ選ぶ。",
                            "criteria": {key: render_plan(plan, language) for key, plan in plans.items()}}}},
                        "expected": {"plan": gold}, "audit": copy.deepcopy(audit)}
                cases.append(case)
    return cases


if __name__ == "__main__":
    cases = generate_cases()
    print(json.dumps({"cases": len(cases), "japanese": sum(case["language"] == "ja" for case in cases),
                      "english": sum(case["language"] == "en" for case in cases), "families": len(FAMILIES)}))
