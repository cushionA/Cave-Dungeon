"""Prespecified command-resolution worlds for the expanded Needle evaluation.

Each primary question is a Japanese semantic world. English rows translate a
fixed subset, and do not increase the primary sample size. The formal command
AST and oracle stay in ``audit``; only ``model_input`` is eligible for inference.
These cases evaluate a fixed synthetic distribution, not unrestricted speech.
"""
import copy
import hashlib
import json
import random
from collections import Counter, defaultdict

from decision_diagnostic_cases import COMMAND_POLICY, entity_catalog, tool_catalog


DEFAULT_SEED = 2026110603
FAMILIES = (
    "follow_reference", "hold_until_ready", "attack_reference", "defend_reference",
    "retreat_reference", "interact_reference", "scout_reference", "negated_target",
    "quoted_order", "pronoun_binding", "known_conditional", "latest_order",
    "ambiguous_reference", "unsupported_action", "multiple_actions", "unknown_condition",
)
EXECUTABLE_FAMILIES = frozenset(FAMILIES[:12])
LABEL_SCOPE = (
    "Formal single-command resolution under explicit facts and a fixed tool catalog. "
    "Japanese worlds are primary; translated English rows are secondary. "
    "No natural-language population or gameplay success claim."
)
_TARGETS = {
    "follow": ["player", "ally_mira", "ally_nera"],
    "attack": ["enemy_raider", "enemy_archer"],
    "defend_ally": ["ally_mira", "ally_nera"],
    "retreat": ["zone_north_exit", "zone_east_balcony"],
    "interact": ["object_lever", "object_gate"],
    "scout": ["zone_north_exit", "zone_east_balcony"],
}
_EXTRA_POLICY = {
    "en": (
        "Query situation facts are authoritative. Resolve a stated minimum, maximum, code, or binding only among "
        "the listed candidates. Evaluate stated conditions from those facts; an unknown condition with different "
        "actions requires clarify. A later timestamped player order supersedes earlier player orders. "
        "Simultaneous different commands require clarify."
    ),
    "ja": (
        "各質問の状況説明を確定した事実として使う。最小・最大・コード・指示対象の対応は、質問に記載された候補だけで判定する。"
        "条件はその事実から判定し、条件が不明で行動が分かれるときはclarifyを選ぶ。時刻付きのプレイヤー指示は、"
        "新しいものが古いものを更新する。異なる行動の同時実行要求ではclarifyを選ぶ。"
    ),
}
_FIELDS = {
    "distance": ("distance", "距離"), "priority": ("priority", "優先値"),
    "tag": ("mark", "印"), "status": ("state", "状態"),
    "signal_time": ("signal time", "合図の時刻"), "code": ("code", "コード"),
    "elapsed": ("elapsed seconds", "経過秒数"), "health": ("health", "体力"),
    "signal": ("signal value", "合図の値"), "supply": ("supplies", "補給数"),
    "assembled": ("assembled units", "集合済み人数"), "progress": ("repair progress", "修理進捗"),
    "marked_target": ("the explicitly pointed-at entity", "明示的に指し示された対象"),
}
_VALUES = {"marked": ("marked", "あり"), "plain": ("unmarked", "なし"),
           "active": ("active", "活動中"), "standby": ("standby", "待機中")}


def _json(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def semantic_fingerprint(world):
    def normalize(value):
        if isinstance(value, dict):
            result = {key: normalize(item) for key, item in value.items()}
            if "candidates" in result:
                result["candidates"] = sorted(result["candidates"])
            if result.get("kind") == "latest":
                result["orders"] = sorted(result["orders"], key=lambda item: _json(item))
            if result.get("kind") == "simultaneous":
                result["commands"] = sorted(result["commands"], key=_json)
            return result
        return [normalize(item) for item in value] if isinstance(value, list) else value
    return hashlib.sha256(_json(normalize(world)).encode("utf-8")).hexdigest()


def _rng(seed, family, index):
    digest = hashlib.sha256(f"{seed}:{family}:{index}".encode()).digest()
    return random.Random(int.from_bytes(digest[:16], "big"))


def _call(name, reference=None):
    result = {"kind": "call", "name": name}
    if reference is not None:
        result["reference"] = reference
    return result


def _literal(name, rng):
    return _call(name, {"kind": "entity", "id": rng.choice(_TARGETS[name])}) if name in _TARGETS else _call(name)


def _resolve(reference, facts):
    kind = reference["kind"]
    if kind == "entity":
        return reference["id"]
    if kind == "binding":
        return facts.get(reference["key"])
    candidates = reference["candidates"]
    if kind == "exclude":
        matches = [entity for entity in candidates if entity != reference["excluded"]]
    elif kind == "extreme":
        values = [facts[entity][reference["field"]] for entity in candidates]
        wanted = min(values) if reference["operator"] == "min" else max(values)
        matches = [entity for entity in candidates if facts[entity][reference["field"]] == wanted]
    elif kind == "match":
        matches = [entity for entity in candidates if facts[entity][reference["field"]] == reference["value"]]
    else:
        raise ValueError("Unknown formal reference")
    return matches[0] if len(matches) == 1 else None


def _truth(condition, facts):
    value = facts.get(condition["key"])
    if value is None:
        return None
    if condition["op"] == "lt":
        return value < condition["value"]
    if condition["op"] == "ge":
        return value >= condition["value"]
    if condition["op"] == "eq":
        return value == condition["value"]
    raise ValueError("Unknown formal condition")


def oracle(world):
    """Evaluate a formal instruction without reading its rendered natural text."""
    facts = world["facts"]
    clarify = {"name": "clarify", "arguments": {}}

    def evaluate(command):
        kind = command["kind"]
        if kind == "call":
            name = command["name"]
            if name in ("hold_position", "clarify"):
                return {"name": name, "arguments": {}}
            if name not in _TARGETS:
                raise ValueError("Unknown implemented tool in formal call")
            target = _resolve(command["reference"], facts)
            return {"name": name, "arguments": {"target_id": target}} if target in _TARGETS[name] else clarify
        if kind == "if":
            truth = _truth(command["condition"], facts)
            if truth is None:
                yes, no = evaluate(command["then"]), evaluate(command["else"])
                return yes if yes == no else clarify
            return evaluate(command["then"] if truth else command["else"])
        if kind == "quoted":
            return evaluate(command["actual"])
        if kind == "latest":
            latest = max(order["time"] for order in command["orders"])
            matches = [order for order in command["orders"] if order["time"] == latest]
            return evaluate(matches[0]["command"]) if len(matches) == 1 else clarify
        if kind == "simultaneous":
            calls = [evaluate(item) for item in command["commands"]]
            return calls[0] if calls and all(item == calls[0] for item in calls) else clarify
        if kind == "unsupported":
            return clarify
        raise ValueError("Unknown formal command")

    return evaluate(world["command"])


def _code_rank(seed, family, count):
    if count == 3:
        return 1  # Include a non-extreme identifier, not only min/max matches.
    families = ["attack_reference", "defend_reference", "retreat_reference",
                "interact_reference", "scout_reference", "quoted_order"]
    ranks = [0, 0, 0, 1, 1, 1]
    _rng(seed, "code-rank", "allocation").shuffle(ranks)
    return ranks[families.index(family)]


def _selector(name, index, rng, ambiguous=False, code_rank=0):
    candidates = list(_TARGETS[name])
    rng.shuffle(candidates)
    field, kind = [("distance", "min"), ("priority", "max"), ("tag", "match"),
                   ("status", "match"), ("signal_time", "max"), ("code", "match")][index % 6]
    facts = {}
    selected = candidates[0]
    if kind in ("min", "max"):
        values = rng.sample(range(3, 96), len(candidates))
        values.sort(reverse=kind == "max")
        if ambiguous:
            values[1] = values[0]
        for entity, value in zip(candidates, values):
            facts[entity] = {field: value}
        reference = {"kind": "extreme", "operator": kind, "field": field, "candidates": candidates}
    else:
        if field == "code":
            numbers = rng.sample(range(10, 90), len(candidates))
            wanted = sorted(numbers)[code_rank]
            distractors = [number for number in numbers if number != wanted]
            rng.shuffle(distractors)
            facts[selected] = {field: wanted}
            for entity, number in zip(candidates[1:], distractors):
                facts[entity] = {field: number}
        else:
            wanted = {"tag": "marked", "status": "active"}[field]
            other = {"tag": "plain", "status": "standby"}[field]
            for entity in candidates:
                facts[entity] = {field: wanted if entity == selected else other}
        if ambiguous:
            facts[candidates[1]][field] = wanted
        reference = {"kind": "match", "field": field, "value": wanted, "candidates": candidates}
    # Semantic values determine the answer before presentation is chosen. A
    # minimum/maximum/match answer must not always occupy the first fact row.
    # Six worlds balance positions 3/3 for two candidates and 2/2/2 for three.
    presentation = [entity for entity in candidates if entity != selected]
    rng.shuffle(presentation)
    presentation.insert(index % len(candidates), selected)
    facts = {entity: facts[entity] for entity in presentation}
    reference["candidates"] = presentation
    return _call(name, reference), facts


def _condition(index, rng, truth, unknown=False):
    key = ["elapsed", "health", "signal", "supply", "assembled", "progress"][index % 6]
    op = ("lt", "ge", "eq")[index % 3]
    threshold = rng.randrange(5, 86)
    gap = rng.randrange(1, 10)
    if op == "lt":
        value = threshold - gap if truth else threshold + gap
    elif op == "ge":
        value = threshold + gap if truth else threshold - gap
    else:
        value = threshold if truth else threshold + gap
    return {"key": key, "op": op, "value": threshold}, {key: None if unknown else value}


def _world(seed, family, index):
    rng = _rng(seed, family, index)
    facts = {}
    basics = {"follow_reference": "follow", "attack_reference": "attack", "defend_reference": "defend_ally",
              "retreat_reference": "retreat", "interact_reference": "interact", "scout_reference": "scout"}
    if family in basics:
        name = basics[family]
        command, facts = _selector(name, index, rng, code_rank=_code_rank(seed, family, len(_TARGETS[name])))
    elif family == "hold_until_ready":
        condition, facts = _condition(index, rng, False)
        command = {"kind": "if", "condition": condition, "then": _literal("scout", rng), "else": _call("hold_position")}
    elif family == "negated_target":
        name = ("attack", "defend_ally", "retreat")[index % 3]
        candidates = list(_TARGETS[name])
        excluded = candidates[(index // 3) % len(candidates)]
        rng.shuffle(candidates)
        command = _call(name, {"kind": "exclude", "candidates": candidates, "excluded": excluded})
    elif family == "quoted_order":
        name = ("follow", "attack", "defend_ally", "retreat", "interact", "scout")[index % 6]
        actual, facts = _selector(name, index, rng, code_rank=_code_rank(seed, family, len(_TARGETS[name])))
        command = {"kind": "quoted", "quoted": _literal("attack" if name != "attack" else "retreat", rng), "actual": actual}
    elif family == "pronoun_binding":
        name = ("attack", "defend_ally", "follow", "interact", "retreat", "scout")[index % 6]
        facts = {"marked_target": rng.choice(_TARGETS[name])}
        command = _call(name, {"kind": "binding", "key": "marked_target"})
    elif family in ("known_conditional", "unknown_condition"):
        condition, facts = _condition(index, rng, index % 2 == 0, family == "unknown_condition")
        pair = [("attack", "hold_position"), ("defend_ally", "retreat"), ("follow", "scout"),
                ("interact", "hold_position"), ("scout", "retreat"), ("retreat", "attack")][index % 6]
        command = {"kind": "if", "condition": condition, "then": _literal(pair[0], rng), "else": _literal(pair[1], rng)}
    elif family == "latest_order":
        tools = [("attack", "defend_ally"), ("follow", "hold_position"), ("retreat", "scout"),
                 ("interact", "attack"), ("defend_ally", "hold_position"), ("scout", "follow")][index % 6]
        times = rng.sample(range(10, 100), 2)
        orders = [{"time": at, "command": _literal(name, rng)} for at, name in zip(times, tools)]
        rng.shuffle(orders)
        command = {"kind": "latest", "orders": orders}
    elif family == "ambiguous_reference":
        name = ("attack", "defend_ally", "retreat", "interact", "follow", "scout")[index % 6]
        command, facts = _selector(name, index, rng, ambiguous=True)
    elif family == "unsupported_action":
        action = ("heal", "craft", "trade", "teleport", "revive", "summon")[index % 6]
        command = {"kind": "unsupported", "action": action, "amount": rng.randrange(2, 15),
                   "target": rng.choice(["ally_mira", "ally_nera"])}
    elif family == "multiple_actions":
        pairs = [("attack", "defend_ally"), ("follow", "scout"), ("retreat", "interact"),
                 ("defend_ally", "scout"), ("attack", "retreat"), ("follow", "interact")]
        command = {"kind": "simultaneous", "commands": [_literal(name, rng) for name in pairs[index % 6]]}
    else:
        raise ValueError("Unknown command family")
    return {"family": family, "facts": facts, "command": command}


def counterfactual(world):
    """Change a relevant fact or semantic operator; this is not another scored row."""
    changed = copy.deepcopy(world)
    command = changed["command"]
    while command["kind"] == "quoted":
        command = command["actual"]
    if command["kind"] == "call":
        reference = command.get("reference")
        if reference is None:
            raise ValueError("Fixed zero-argument calls need a surrounding condition")
        if reference["kind"] == "binding":
            previous = changed["facts"][reference["key"]]
            changed["facts"][reference["key"]] = next(item for item in _TARGETS[command["name"]] if item != previous)
        elif reference["kind"] == "exclude":
            reference["excluded"] = next(item for item in reference["candidates"] if item != reference["excluded"])
        elif reference["kind"] == "entity":
            reference["id"] = next(item for item in _TARGETS[command["name"]] if item != reference["id"])
        else:
            candidates, field = reference["candidates"], reference["field"]
            chosen = _resolve(reference, changed["facts"])
            if chosen is None:
                reference.clear()
                reference.update({"kind": "entity", "id": candidates[0]})
            else:
                other = next(item for item in candidates if item != chosen)
                changed["facts"][chosen][field], changed["facts"][other][field] = changed["facts"][other][field], changed["facts"][chosen][field]
    elif command["kind"] == "if":
        condition = command["condition"]
        truth = _truth(condition, changed["facts"])
        threshold = condition["value"]
        changed["facts"][condition["key"]] = {"lt": threshold - 1 if truth is not True else threshold + 1,
                                                  "ge": threshold + 1 if truth is not True else threshold - 1,
                                                  "eq": threshold if truth is not True else threshold + 1}[condition["op"]]
    elif command["kind"] == "latest":
        command["orders"][0]["time"], command["orders"][1]["time"] = command["orders"][1]["time"], command["orders"][0]["time"]
    elif command["kind"] == "simultaneous":
        changed["command"] = copy.deepcopy(command["commands"][0])
    elif command["kind"] == "unsupported":
        changed["command"] = _call("hold_position")
    else:
        raise ValueError("No semantic counterfactual for command")
    return changed


def _word(value, language):
    if value is None:
        return "unknown" if language == "en" else "不明"
    if value in _VALUES:
        return _VALUES[value][language == "ja"]
    return str(value)


def _name(entity, language):
    names = {item["id"]: item["description"] for item in entity_catalog(language)}
    return names.get(entity, entity)


def _reference_text(reference, language):
    en = language == "en"
    kind = reference["kind"]
    if kind == "entity":
        return _name(reference["id"], language)
    if kind == "binding":
        return "the explicitly pointed-at one" if en else "明示的に指し示した、その対象"
    if kind == "exclude":
        excluded = _name(reference["excluded"], language)
        choices = ", ".join(_name(entity, language) for entity in reference["candidates"])
        return f"the candidate other than {excluded} among [{choices}]" if en else f"候補［{choices}］のうち、{excluded}ではない方"
    field = _FIELDS[reference["field"]][not en]
    if kind == "extreme":
        adjective = ("smallest" if reference["operator"] == "min" else "largest") if en else ("最小" if reference["operator"] == "min" else "最大")
        return f"the listed candidate with the {adjective} {field}" if en else f"記載された候補で{field}が{adjective}の対象"
    value = _word(reference["value"], language)
    return f"the listed candidate whose {field} is {value}" if en else f"記載された候補で{field}が「{value}」の対象"


def _condition_text(condition, language):
    key = _FIELDS[condition["key"]][language == "ja"]
    operator = {"lt": ("less than", "未満"), "ge": ("at least", "以上"), "eq": ("equal to", "と等しい")}[condition["op"]][language == "ja"]
    return f"{key} is {operator} {condition['value']}" if language == "en" else f"{key}が{condition['value']}{operator}"


def _render_command(command, language):
    en = language == "en"
    kind = command["kind"]
    if kind == "call":
        name = command["name"]
        if name == "hold_position":
            return "stay at the current position" if en else "現在の場所で待機して"
        if name == "clarify":
            return "ask for clarification" if en else "指示の確認を求めて"
        target = _reference_text(command["reference"], language)
        actions = {"follow": (f"follow {target}", f"{target}について行って"),
                   "attack": (f"attack {target}", f"{target}を攻撃して"),
                   "defend_ally": (f"protect {target}", f"{target}を守って"),
                   "retreat": (f"retreat to {target}", f"{target}へ撤退して"),
                   "interact": (f"operate {target}", f"{target}を操作して"),
                   "scout": (f"inspect {target} and report back", f"{target}を偵察して報告して")}
        return actions[name][not en]
    if kind == "if":
        condition = _condition_text(command["condition"], language)
        yes, no = _render_command(command["then"], language), _render_command(command["else"], language)
        return f"if {condition}, {yes}; otherwise, {no}" if en else f"{condition}なら{yes}。そうでなければ{no}"
    if kind == "quoted":
        quoted, actual = _render_command(command["quoted"], language), _render_command(command["actual"], language)
        return f"a discarded note says '{quoted}'. Ignore the note. My actual instruction is: {actual}" if en else f"捨てられたメモには『{quoted}』とある。そのメモは無視して。私からの実際の指示は、{actual}"
    if kind == "latest":
        orders = [f"at {item['time']}: '{_render_command(item['command'], language)}'" if en else f"時刻{item['time']}に『{_render_command(item['command'], language)}』"
                  for item in command["orders"]]
        return ("these are timestamped orders from the player; apply the latest: " + "; ".join(orders)) if en else ("これはプレイヤーからの時刻付き指示。最新の指示を使って：" + "、".join(orders))
    if kind == "simultaneous":
        calls = [_render_command(item, language) for item in command["commands"]]
        return f"do both at the same time: {calls[0]} AND {calls[1]}" if en else f"次の両方を同時に実行して：{calls[0]}。それと{calls[1]}"
    if kind == "unsupported":
        target = _name(command["target"], language)
        amount = command["amount"]
        forms = {"heal": (f"restore {amount} health to {target} with a healing spell", f"回復魔法で{target}の体力を{amount}回復して"),
                 "craft": (f"craft {amount} new shields for {target}", f"{target}用の新しい盾を{amount}個製作して"),
                 "trade": (f"sell {amount} gems to {target}", f"{target}に宝石を{amount}個売って"),
                 "teleport": (f"teleport {target} directly out of the dungeon", f"{target}をダンジョンの外へ瞬間移動させて"),
                 "revive": (f"resurrect {target} from death", f"死亡した{target}を生き返らせて"),
                 "summon": (f"magically summon {amount} new soldiers beside {target}", f"{target}の横に新しい兵士を{amount}人、魔法で召喚して")}
        return forms[command["action"]][not en]
    raise ValueError("Unknown rendered command")


def _render(world, language):
    en = language == "en"
    facts = []
    for key, value in world["facts"].items():
        if isinstance(value, dict):
            properties = ", ".join(f"{_FIELDS[field][not en]}={_word(number, language)}" for field, number in value.items())
            facts.append(f"{key} ({_name(key, language)}): {properties}")
        else:
            label = _FIELDS[key][not en]
            displayed = f"{value} ({_name(value, language)})" if key == "marked_target" else _word(value, language)
            facts.append(f"{label}={displayed}")
    situation = "; ".join(facts)
    instruction = _render_command(world["command"], language)
    query = ((f"Situation: {situation}.\n" if facts else "") + f"Player instruction: {instruction}.") if en else ((f"状況：{situation}。\n" if facts else "") + f"プレイヤーの指示：{instruction}。")
    return {"policy": COMMAND_POLICY[language] + " " + _EXTRA_POLICY[language],
            "instruction": query, "entities": entity_catalog(language), "tools": tool_catalog(language)}


def model_input(case):
    return copy.deepcopy(case["input"])


def generate_cases(seed=DEFAULT_SEED, worlds_per_family=6, english_per_family=2):
    if (type(seed) is not int or type(worlds_per_family) is not int or not 1 <= worlds_per_family <= 6
            or type(english_per_family) is not int or not 0 <= english_per_family <= worlds_per_family):
        raise ValueError("Use 1..6 distinct worlds per family and an English subset no larger than that")
    rows = []
    for family in FAMILIES:
        translated = set(_rng(seed, family, "english-subset").sample(range(worlds_per_family), english_per_family))
        for index in range(worlds_per_family):
            world = _world(seed, family, index)
            expected = oracle(world)
            scenario = f"command_{family}_{index:03d}"
            for language in (["ja", "en"] if index in translated else ["ja"]):
                rows.append({"case_id": f"{scenario}_{language}", "scenario_id": scenario, "world_id": scenario,
                             "pair_id": scenario, "family": family, "family_id": family,
                             "suite": "commands", "language": language, "category": family,
                             "input": _render(world, language), "expected": copy.deepcopy(expected),
                             "executable": expected["name"] != "clarify",
                             "safe_handling": "exact_call" if expected["name"] != "clarify" else "clarify_or_no_calls",
                             "audit": {"world": copy.deepcopy(world), "semantic_sha256": semantic_fingerprint(world),
                                       "label_scope": LABEL_SCOPE}})
    _rng(seed, "all", "row-order").shuffle(rows)
    validate_cases(rows)
    return rows


def validate_cases(rows):
    if not isinstance(rows, list) or not rows:
        raise ValueError("Command corpus must be a nonempty list")
    ids, worlds, primary_signatures = set(), defaultdict(list), set()
    shared = {}
    for row in rows:
        if row.get("case_id") in ids or row.get("language") not in ("ja", "en") or row.get("family") not in FAMILIES:
            raise ValueError("Invalid or duplicate command case identity")
        ids.add(row["case_id"])
        if (row["world_id"] != row["scenario_id"] or row["pair_id"] != row["scenario_id"]
                or row["family_id"] != row["family"] or row["suite"] != "commands"):
            raise ValueError("Inconsistent command cluster metadata")
        world = row["audit"]["world"]
        fingerprint = semantic_fingerprint(world)
        expected = oracle(world)
        executable = row["family"] in EXECUTABLE_FAMILIES
        if (world["family"] != row["family"] or row["audit"]["semantic_sha256"] != fingerprint
                or row["expected"] != expected or row["executable"] != executable
                or executable != (expected["name"] != "clarify")):
            raise ValueError("Command label or formal world changed")
        if row["input"] != _render(world, row["language"]):
            raise ValueError("Rendered input changed its formal evidence")
        fixed = {key: row["input"][key] for key in ("policy", "entities", "tools")}
        shared.setdefault(row["language"], fixed)
        if shared[row["language"]] != fixed:
            raise ValueError("Command system facts or schemas differ within a language")
        if row["language"] == "ja":
            if fingerprint in primary_signatures:
                raise ValueError("Repeated semantic world in primary questions")
            primary_signatures.add(fingerprint)
        worlds[row["scenario_id"]].append(row)
    for group in worlds.values():
        if (sum(row["language"] == "ja" for row in group) != 1 or len(group) > 2
                or len({row["language"] for row in group}) != len(group)
                or any(row["expected"] != group[0]["expected"] or row["audit"]["world"] != group[0]["audit"]["world"] for row in group)):
            raise ValueError("Translation rows changed labels or duplicated primary worlds")
    return True


if __name__ == "__main__":
    cases = generate_cases()
    print(_json({"cases": len(cases), "languages": dict(Counter(row["language"] for row in cases)),
                 "ja_executable": sum(row["language"] == "ja" and row["executable"] for row in cases),
                 "sha256": hashlib.sha256(_json(cases).encode()).hexdigest()}))
