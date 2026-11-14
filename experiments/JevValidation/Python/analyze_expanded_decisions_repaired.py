"""Analyze prespecified, paired synthetic decisions without pooling translations.

The primary comparison is Hindsight minus TinyJev on one Japanese case per
semantic world. Families define the fixed benchmark mix. Family-block resampling
is a sensitivity analysis, not evidence of generalization to real games.
"""
import argparse
from collections import Counter, defaultdict
import hashlib
import json
import math
from pathlib import Path
import random
import statistics


def _integer(value, name):
    if type(value) is not int or value < 0:
        raise ValueError(name + " must be a nonnegative integer")


def binomial_pmf(k, n, probability):
    if not 0 <= probability <= 1:
        raise ValueError("Invalid binomial probability")
    if k < 0 or k > n:
        return 0.0
    if probability == 0:
        return float(k == 0)
    if probability == 1:
        return float(k == n)
    return math.exp(math.lgamma(n + 1) - math.lgamma(k + 1) - math.lgamma(n - k + 1)
                    + k * math.log(probability) + (n - k) * math.log1p(-probability))


def binomial_cdf(k, n, probability):
    if k < 0:
        return 0.0
    if k >= n:
        return 1.0
    return min(1.0, max(0.0, math.fsum(binomial_pmf(i, n, probability) for i in range(k + 1))))


def exact_mcnemar(first_only, second_only):
    """Two-sided exact conditional binomial test of discordant paired outcomes."""
    _integer(first_only, "first_only")
    _integer(second_only, "second_only")
    discordant = first_only + second_only
    return min(1.0, 2 * binomial_cdf(min(first_only, second_only), discordant, 0.5))


def holm_adjust(p_values):
    if any(not math.isfinite(p) or not 0 <= p <= 1 for p in p_values):
        raise ValueError("Invalid p-value")
    order = sorted(range(len(p_values)), key=lambda index: p_values[index])
    result, previous = [0.0] * len(order), 0.0
    for rank, index in enumerate(order):
        previous = max(previous, min(1.0, (len(order) - rank) * p_values[index]))
        result[index] = previous
    return result


def _counts(successes, total):
    _integer(successes, "successes")
    _integer(total, "total")
    if total < 1 or successes > total:
        raise ValueError("A rate needs 0 <= successes <= positive total")


def wilson_interval(successes, total, confidence=0.95):
    _counts(successes, total)
    if not 0 < confidence < 1:
        raise ValueError("Invalid confidence level")
    z = statistics.NormalDist().inv_cdf((1 + confidence) / 2)
    observed = successes / total
    denominator = 1 + z * z / total
    center = (observed + z * z / (2 * total)) / denominator
    radius = z * math.sqrt(observed * (1 - observed) / total + z * z / (4 * total * total)) / denominator
    return [0.0 if successes == 0 else max(0.0, center - radius),
            1.0 if successes == total else min(1.0, center + radius)]


def _cdf_root(k, n, target):
    # For fixed k<n, binomial CDF decreases monotonically with probability.
    low, high = 0.0, 1.0
    for _ in range(64):
        middle = (low + high) / 2
        if binomial_cdf(k, n, middle) > target:
            low = middle
        else:
            high = middle
    return (low + high) / 2


def clopper_pearson(successes, total, confidence=0.95):
    _counts(successes, total)
    if not 0 < confidence < 1:
        raise ValueError("Invalid confidence level")
    tail = (1 - confidence) / 2
    return [0.0 if successes == 0 else _cdf_root(successes - 1, total, 1 - tail),
            1.0 if successes == total else _cdf_root(successes, total, tail)]


def one_sided_upper(successes, total, confidence=0.95):
    _counts(successes, total)
    if not 0 < confidence < 1:
        raise ValueError("Invalid confidence level")
    if successes == total:
        return 1.0
    if successes == 0:
        return 1 - (1 - confidence) ** (1 / total)
    return _cdf_root(successes, total, 1 - confidence)


def conservative_paired_interval(first_only, second_only, total, confidence=0.95):
    """Boundary-safe conservative CI for delta=q*(2*theta-1).

    q is the discordance probability and theta the first model's conditional win
    probability. Two Clopper-Pearson intervals each at 1-alpha/2 give at least
    1-alpha joint coverage by the union bound. Project their rectangle to delta.
    This intentionally complements, rather than replaces, bootstrap sensitivity.
    """
    _integer(first_only, "first_only")
    _integer(second_only, "second_only")
    discordant = first_only + second_only
    _counts(discordant, total)
    component_confidence = 1 - (1 - confidence) / 2
    q = clopper_pearson(discordant, total, component_confidence)
    theta = clopper_pearson(first_only, discordant, component_confidence) if discordant else [0.0, 1.0]
    corners = [rate * (2 * fraction - 1) for rate in q for fraction in theta]
    return [max(-1.0, min(corners)), min(1.0, max(corners))]


def exact_mcnemar_power(total, discordance=0.3, difference=0.1, alpha=0.025):
    """Unconditional power under a prespecified paired multinomial alternative."""
    _integer(total, "total")
    if total < 1 or not 0 < discordance < 1 or not abs(difference) <= discordance or not 0 < alpha < 1:
        raise ValueError("Invalid paired power assumptions")
    conditional_win = (discordance + difference) / (2 * discordance)
    power = 0.0
    for count in range(total + 1):
        critical, cumulative = -1, 0.0
        for wins in range(count // 2 + 1):
            cumulative += binomial_pmf(wins, count, 0.5)
            if 2 * cumulative <= alpha + 1e-14:
                critical = wins
            else:
                break
        if critical >= 0:
            conditional_power = (binomial_cdf(critical, count, conditional_win)
                                 + binomial_cdf(critical, count, 1 - conditional_win))
            power += binomial_pmf(count, total, discordance) * conditional_power
    return min(1.0, power)


def load_cases(path):
    value = json.loads(Path(path).read_text())
    return value["cases"] if isinstance(value, dict) else value


def load_records(path):
    text = Path(path).read_text()
    try:
        value = json.loads(text)
    except json.JSONDecodeError:
        value = [json.loads(line) for line in text.splitlines() if line.strip()]
    if isinstance(value, dict):
        if "results" in value:
            if value.get("status", "complete") != "complete":
                raise ValueError("An unfinished native report cannot be analyzed as complete")
            value = value["results"]
        else:
            value = [value]
    if not isinstance(value, list) or not value:
        raise ValueError("Prediction records must be a nonempty list")
    return value


def _typed_equal(left, right):
    return type(left) is type(right) and left == right


def _row_failed(row):
    status = row.get("status", "complete")
    if status in {"failed", "error", "timeout", "crashed", "invalid", "exception"}:
        return True
    if status not in {"complete", "completed", "success", "ok", "passed"}:
        raise ValueError("A prediction record has an unfinished or unknown status")
    return bool(row.get("failure") or row.get("error"))


def _validate_probability(value):
    if type(value) not in (int, float) or not math.isfinite(value) or not 0 <= value <= 1:
        raise ValueError("A native probability is invalid")
    return float(value)


def _native_predictions(rows, field):
    result = {}
    for question_id, details in rows.items():
        if not isinstance(details, dict) or field not in details:
            raise ValueError("Native question lacks its selected prediction")
        selected = details[field]
        probability = details.get("p_true")
        distribution = details.get("probabilities")
        if isinstance(distribution, dict):
            values = {key: _validate_probability(value) for key, value in distribution.items()}
            if not values or abs(sum(values.values()) - 1) > 1e-5:
                raise ValueError("Native distribution is not normalized")
        if details.get("type") in {"boolean", "noul"}:
            if probability is None and isinstance(distribution, dict):
                probability = distribution.get("true")
            if probability is not None:
                if not _typed_equal(selected, _validate_probability(probability) >= 0.5):
                    raise ValueError("Native boolean prediction disagrees with the fixed 0.5 threshold")
        elif isinstance(distribution, dict):
            values = {key: _validate_probability(value) for key, value in distribution.items()}
            if selected not in values or values[selected] < max(values.values()) - 1e-6:
                raise ValueError("Native choice does not maximize its reported probabilities")
        result[question_id] = selected
    return result


def _tiny_native_predictions(native, case_id):
    """Decode official TinyJev outer probabilities, never its rounded noul field."""
    result = {}
    prefix = case_id + ":"
    for key, details in native.items():
        if not key.startswith(prefix):
            raise ValueError("TinyJev native result belongs to a different case")
        candidates, probabilities = details.get("candidate_ids"), details.get("probabilities")
        if (not isinstance(candidates, list) or not candidates or len(set(candidates)) != len(candidates)
                or any(not isinstance(value, str) for value in candidates)
                or not isinstance(probabilities, list) or len(probabilities) != len(candidates)):
            raise ValueError("TinyJev native candidate/probability contract changed")
        values = [_validate_probability(value) for value in probabilities]
        if abs(sum(values) - 1) > 1e-5:
            raise ValueError("TinyJev native probabilities are not normalized")
        if details.get("type") in {"boolean", "noul"}:
            if set(candidates) != {"false", "true"}:
                raise ValueError("TinyJev boolean candidate names changed")
            selected = values[candidates.index("true")] >= 0.5
        else:
            selected = candidates[max(range(len(values)), key=lambda index: values[index])]
        result[key[len(prefix):]] = selected
    return result


def _predictions(row):
    alternatives = []
    if isinstance(row.get("predictions"), dict):
        alternatives.append(row["predictions"])
    if isinstance(row.get("details"), dict):
        alternatives.append(_native_predictions(row["details"], "selected"))
    if isinstance(row.get("questions"), dict):
        alternatives.append(_native_predictions(row["questions"], "prediction"))
    if isinstance(row.get("native"), dict):
        native = row["native"]
        if isinstance(native.get("details"), dict):
            alternatives.append(_native_predictions(native["details"], "selected"))
        elif native:
            alternatives.append(_tiny_native_predictions(native, row["case_id"]))
    if not alternatives:
        raise ValueError("Successful row has no independently scoreable predictions")
    if any(json.dumps(value, sort_keys=True) != json.dumps(alternatives[0], sort_keys=True)
           for value in alternatives[1:]):
        raise ValueError("Canonical and native predictions disagree")
    return alternatives[0]


def score_records(cases, records):
    expected_ids = [case["case_id"] for case in cases]
    if len(set(expected_ids)) != len(expected_ids):
        raise ValueError("Duplicate case IDs in corpus")
    by_id = {}
    for row in records:
        case_id = row.get("case_id")
        if case_id in by_id or case_id not in expected_ids:
            raise ValueError("Duplicate or foreign prediction case ID")
        by_id[case_id] = row
    if set(by_id) != set(expected_ids):
        raise ValueError("Missing predictions must be completed or explicitly recorded as failures")
    scored = {}
    for case in cases:
        expected = case.get("expected")
        if not isinstance(expected, dict) or not expected or any(type(v) not in (bool, str) for v in expected.values()):
            raise ValueError("Decision gold must be a separate boolean/string mapping")
        row = by_id[case["case_id"]]
        failed = _row_failed(row)
        predictions = {} if failed else _predictions(row)
        if not failed and set(predictions) != set(expected):
            raise ValueError("Prediction question coverage differs from frozen gold")
        scored[case["case_id"]] = {"correct": not failed and all(
            _typed_equal(predictions[q], value) for q, value in expected.items()),
            "failed": failed, "predictions": predictions}
    return scored


def percentile(values, probability):
    values = sorted(values)
    position = (len(values) - 1) * probability
    lower = math.floor(position)
    upper = math.ceil(position)
    return values[lower] + (values[upper] - values[lower]) * (position - lower)


def bootstrap_intervals(rows, repetitions=10000, seed=20261106):
    if repetitions < 1 or not rows:
        raise ValueError("Bootstrap needs data and positive repetitions")
    families = defaultdict(list)
    for row in rows:
        families[row["family_id"]].append(row["difference"])
    ordered = [families[key] for key in sorted(families)]
    random_worlds, random_families = random.Random(seed), random.Random(seed + 1)
    world_values, family_values = [], []
    family_means = [statistics.mean(values) for values in ordered]
    for _ in range(repetitions):
        # Paired model outcomes, not separate model observations, are sampled.
        world_values.append(sum(sum(random_worlds.choices(values, k=len(values)))
                                for values in ordered) / len(rows))
        family_values.append(statistics.mean(random_families.choices(family_means, k=len(family_means))))
    result = {"world_stratified_95": [percentile(world_values, 0.025), percentile(world_values, 0.975)],
            "family_block_95": [percentile(family_values, 0.025), percentile(family_values, 0.975)],
            "repetitions": repetitions, "seed": seed,
            "scope": "World resampling conditions on the fixed family mix; family-block resampling is a family-sensitivity diagnostic. Bootstrap can degenerate at boundaries; use the conservative paired interval too."}
    if any("mechanism_group" in row for row in rows):
        mechanisms = defaultdict(list)
        for row in rows:
            if not isinstance(row.get("mechanism_group"), str) or not row["mechanism_group"]:
                raise ValueError("Mechanism metadata must cover every row if provided")
            mechanisms[row["mechanism_group"]].append(row["difference"])
        blocks = [mechanisms[key] for key in sorted(mechanisms)]
        generator, values = random.Random(seed + 2), []
        for _ in range(repetitions):
            sampled = generator.choices(blocks, k=len(blocks))
            values.append(sum(sum(block) for block in sampled) / sum(len(block) for block in sampled))
        result["mechanism_block_95"] = [percentile(values, 0.025), percentile(values, 0.975)]
        result["mechanism_groups"] = len(blocks)
    return result


def leave_family_out(rows, group_key="family_id"):
    families = sorted({row[group_key] for row in rows})
    values = {family: statistics.mean(row["difference"] for row in rows if row[group_key] != family)
              for family in families if any(row[group_key] != family for row in rows)}
    return {"differences": values, "minimum_difference": min(values.values()) if values else None,
            "maximum_difference": max(values.values()) if values else None}


def _task(case):
    return case.get("suite", case.get("task"))


def validate_design(cases, expected_primary_worlds, expected_families, expected_english_worlds):
    if not isinstance(cases, list) or not cases:
        raise ValueError("Empty corpus")
    for task in ("memory", "commander"):
        selected = [case for case in cases if _task(case) == task]
        by_language = defaultdict(list)
        seen = set()
        for case in selected:
            if not all(isinstance(case.get(key), str) and case[key] for key in
                       ("case_id", "world_id", "family_id", "language")):
                raise ValueError("Cases need explicit semantic world, family and language identifiers")
            identity = (case["world_id"], case["language"])
            if identity in seen or case["language"] not in {"ja", "en"}:
                raise ValueError("Only one primary case per Japanese world and one paired English case are allowed")
            seen.add(identity)
            by_language[case["language"]].append(case)
        japanese, english = by_language["ja"], by_language["en"]
        if len(japanese) != expected_primary_worlds or len(english) != expected_english_worlds:
            raise ValueError("Corpus size differs from the frozen primary/secondary design")
        primary_worlds = {case["world_id"]: case for case in japanese}
        family_counts = Counter(case["family_id"] for case in japanese)
        if len(family_counts) != expected_families or len(set(family_counts.values())) != 1:
            raise ValueError("Primary family allocation differs from the balanced frozen design")
        for case in english:
            original = primary_worlds.get(case["world_id"])
            if original is None or case["family_id"] != original["family_id"] or case["expected"] != original["expected"]:
                raise ValueError("English cases must be labeled translations of primary Japanese worlds")
        if english:
            english_counts = Counter(case["family_id"] for case in english)
            if set(english_counts) != set(family_counts) or len(set(english_counts.values())) != 1:
                raise ValueError("Secondary English subset must cover the fixed families equally")
    if any(_task(case) not in {"memory", "commander"} for case in cases):
        raise ValueError("Primary decision corpus contains an unexpected task")


def _group(cases, first, second, repetitions, seed):
    rows = [{"case_id": case["case_id"], "family_id": case["family_id"],
             "world_id": case["world_id"], "difference": int(first[case["case_id"]]["correct"])
             - int(second[case["case_id"]]["correct"])} for case in cases]
    if any("mechanism_group" in case for case in cases):
        for case, row in zip(cases, rows):
            row["mechanism_group"] = case.get("mechanism_group")
    total = len(rows)
    wins, losses = sum(row["difference"] == 1 for row in rows), sum(row["difference"] == -1 for row in rows)
    result = {"worlds": total, "families": len({row["family_id"] for row in rows}),
              "difference_first_minus_second": statistics.mean(row["difference"] for row in rows),
              "discordant_first_only": wins, "discordant_second_only": losses,
              "conservative_paired_difference_95": conservative_paired_interval(wins, losses, total),
              "exact_mcnemar_two_sided_p": exact_mcnemar(wins, losses),
              "bootstrap": bootstrap_intervals(rows, repetitions, seed),
              "leave_one_family_out": leave_family_out(rows)}
    if any("mechanism_group" in row for row in rows):
        result["leave_one_mechanism_out"] = leave_family_out(rows, "mechanism_group")
    for key, scored in (("first", first), ("second", second)):
        correct = sum(scored[case["case_id"]]["correct"] for case in cases)
        result[key] = {"correct": correct, "total": total, "accuracy": correct / total,
                       "wilson_95": wilson_interval(correct, total),
                       "failures_scored_incorrect": sum(scored[case["case_id"]]["failed"] for case in cases),
                       "incorrect_case_ids": [case["case_id"] for case in cases if not scored[case["case_id"]]["correct"]]}
        confusion = Counter()
        for case in cases:
            expected = case["expected"]
            predictions = scored[case["case_id"]]["predictions"]
            if len(expected) == 1 and type(next(iter(expected.values()))) is bool:
                question_id, gold = next(iter(expected.items()))
                prediction = predictions.get(question_id)
                label = ("invalid_true" if gold else "invalid_false") if type(prediction) is not bool else (
                    "true_positive" if gold and prediction else "false_negative" if gold else
                    "false_positive" if prediction else "true_negative")
                confusion[label] += 1
        if confusion:
            result[key]["boolean_confusion"] = dict(confusion)
    result["by_family"] = {family: {
        "worlds": sum(case["family_id"] == family for case in cases),
        "first_correct": sum(first[case["case_id"]]["correct"] for case in cases if case["family_id"] == family),
        "second_correct": sum(second[case["case_id"]]["correct"] for case in cases if case["family_id"] == family)}
        for family in sorted({row["family_id"] for row in rows})}
    if any(_task(case) == "commander" for case in cases):
        if not all(_task(case) == "commander" and isinstance(case.get("audit"), dict) for case in cases):
            raise ValueError("Plan audit metadata must cover the whole group")
        losses = {}
        for model_key, scored in (("first", first), ("second", second)):
            diagnostic, losses[model_key] = commander_diagnostics(cases, scored)
            result[model_key]["commander_secondary"] = diagnostic
        loss_rows = [{**row, "difference": losses["first"][row["case_id"]] - losses["second"][row["case_id"]]}
                     for row in rows]
        result["ordinal_rank_loss_secondary"] = {
            "mean_difference_first_minus_second": statistics.mean(row["difference"] for row in loss_rows),
            "bootstrap": bootstrap_intervals(loss_rows, repetitions, seed + 1000),
            "interpretation": "Lower loss is better. This is normalized ordinal rank among supplied plans, not numerical game utility or combat regret. Unknown/failed plans receive loss one. No additional confirmatory p-value."}
    return result


def commander_diagnostics(cases, scored):
    infeasible, invalid, losses = 0, 0, {}
    for case in cases:
        audit = case["audit"]
        evaluations, regret = audit.get("plan_evaluations"), audit.get("normalized_regret")
        if not isinstance(evaluations, dict) or not evaluations or not isinstance(regret, dict) or set(evaluations) != set(regret):
            raise ValueError("Commander plan audit has incomplete candidate coverage")
        if len(case["expected"]) != 1:
            raise ValueError("Commander diagnostics require one selected complete plan")
        gold = next(iter(case["expected"].values()))
        if gold not in evaluations or evaluations[gold].get("feasible") is not True or regret[gold] != 0:
            raise ValueError("Commander gold disagrees with its plan oracle")
        if any(type(value) not in (int, float) or not math.isfinite(value) or not 0 <= value <= 1 for value in regret.values()):
            raise ValueError("Ordinal plan losses must be finite in [0,1]")
        result = scored[case["case_id"]]
        selected = next(iter(result["predictions"].values()), None)
        if result["failed"] or not isinstance(selected, str) or selected not in evaluations:
            invalid += 1
            loss = 1.0
        else:
            feasible = evaluations[selected].get("feasible")
            if type(feasible) is not bool:
                raise ValueError("Plan feasibility must be explicitly boolean")
            infeasible += not feasible
            loss = regret[selected]
        losses[case["case_id"]] = float(loss)
    total = len(cases)
    return {"total": total, "known_infeasible_plan_count": infeasible,
            "unknown_or_failed_plan_count": invalid, "unsafe_or_invalid_count": infeasible + invalid,
            "unsafe_or_invalid_rate": (infeasible + invalid) / total,
            "unsafe_or_invalid_wilson_95": wilson_interval(infeasible + invalid, total),
            "mean_normalized_ordinal_plan_rank_loss": statistics.mean(losses.values()),
            "interpretation": "All planned worlds remain in the denominator; known infeasibility and invalid/failed selection are separate. Ordinal rank loss is not numerical game utility."}, losses


def _canonical(value):
    return json.dumps(value, sort_keys=True, ensure_ascii=False, separators=(",", ":"), allow_nan=False)


def _command_gold(case):
    expected = case.get("expected_calls")
    if expected is None and "expected_call" in case:
        expected = [case["expected_call"]]
    if not isinstance(expected, list) or len(expected) != 1 or not isinstance(expected[0], dict):
        raise ValueError("Needle cases require one separate expected_call")
    if set(expected[0]) != {"name", "arguments"} or not isinstance(expected[0]["name"], str) or not isinstance(expected[0]["arguments"], dict):
        raise ValueError("Invalid frozen command gold")
    return expected


def _needle_row(case, row):
    failed = _row_failed(row)
    valid = row.get("response_valid") is True and not failed
    accepted = row.get("accepted_calls", [])
    if not isinstance(accepted, list) or len(accepted) > 1:
        raise ValueError("Accepted calls violate the frozen single-action contract")
    if accepted and not valid:
        raise ValueError("An invalid response cannot issue an accepted command")
    raw = row.get("raw")
    if not isinstance(raw, dict):
        if not failed:
            raise ValueError("Needle normal rows require the native envelope for independent validation")
    else:
        native_success = raw.get("success") is True and raw.get("error") is None
        if valid and not native_success:
            raise ValueError("Runner validity contradicts the native response")
        native_calls = raw.get("function_calls", [])
        if not isinstance(native_calls, list):
            raise ValueError("Native function_calls must be an array")
        if any(_canonical(call) not in {_canonical(item) for item in native_calls} for call in accepted):
            raise ValueError("Suppressed or invented calls were promoted to accepted actions")
        validation = raw.get("validation", {})
        if accepted and isinstance(validation, dict) and (validation.get("ungrounded") or validation.get("negation")):
            raise ValueError("Accepted calls bypassed a native validation gate")
    expected = _command_gold(case)
    clarification = expected == [{"name": "clarify", "arguments": {}}]
    exact = valid and _canonical(expected) == _canonical(accepted)
    issued = any(isinstance(call, dict) and call.get("name") != "clarify" for call in accepted)
    return {"case_id": case["case_id"], "world_id": case["world_id"], "family_id": case["family_id"],
            "language": case["language"], "response_valid": valid,
            "failed": failed or not valid, "expected_clarification": clarification,
            "exact": exact, "issued_action": issued,
            "safe_clarification": valid and clarification and (exact or not accepted)}


def analyze_needle(cases, records, expected_ja=96, expected_en=32,
                   expected_executable_ja=72, expected_executable_en=24):
    if not isinstance(cases, list) or not cases:
        raise ValueError("Needle corpus is empty")
    expected_ids = {case["case_id"] for case in cases}
    if len(expected_ids) != len(cases):
        raise ValueError("Duplicate Needle case IDs")
    by_id = {}
    for row in records:
        case_id = row.get("case_id")
        if case_id in by_id or case_id not in expected_ids:
            raise ValueError("Duplicate or foreign Needle output")
        by_id[case_id] = row
    if set(by_id) != expected_ids:
        raise ValueError("Missing Needle outputs are not silently excluded")
    japanese = {case["world_id"]: case for case in cases if case["language"] == "ja"}
    seen = set()
    scored = []
    for case in cases:
        if not all(isinstance(case.get(key), str) and case[key] for key in ("case_id", "world_id", "family_id", "language")):
            raise ValueError("Needle cases require semantic world metadata")
        identity = (case["world_id"], case["language"])
        if identity in seen or case["language"] not in {"ja", "en"}:
            raise ValueError("Needle worlds must not repeat within a language")
        seen.add(identity)
        if case["language"] == "en":
            original = japanese.get(case["world_id"])
            if original is None or original["family_id"] != case["family_id"] or _command_gold(original) != _command_gold(case):
                raise ValueError("English Needle commands must be paired secondary translations")
        scored.append(_needle_row(case, by_id[case["case_id"]]))
    result = {}
    for language, expected_n, expected_actions, label in (
            ("ja", expected_ja, expected_executable_ja, "primary_ja"),
            ("en", expected_en, expected_executable_en, "secondary_en")):
        rows = [row for row in scored if row["language"] == language]
        if len(rows) != expected_n:
            raise ValueError("Needle count differs from the prespecified design")
        executable = [row for row in rows if not row["expected_clarification"]]
        clarify = [row for row in rows if row["expected_clarification"]]
        if (not executable or not clarify or len(executable) != expected_actions
                or len(clarify) != expected_n - expected_actions):
            raise ValueError("Needle executable/clarify allocation differs from the frozen design")
        correct, issued = sum(row["exact"] for row in executable), sum(row["issued_action"] for row in executable)
        safe, unsafe = sum(row["safe_clarification"] for row in clarify), sum(row["issued_action"] for row in clarify)
        result[label] = {
            "worlds": len(rows), "families": len({row["family_id"] for row in rows}),
            "invalid_or_failed_responses": sum(row["failed"] for row in rows),
            "executable": {"total": len(executable), "exact_correct": correct,
                           "exact_success_rate": correct / len(executable), "exact_success_wilson_95": wilson_interval(correct, len(executable)),
                           "exact_success_one_sided_95_upper": one_sided_upper(correct, len(executable)),
                           "issued_action_count": issued, "issued_action_coverage": issued / len(executable),
                           "issued_action_coverage_wilson_95": wilson_interval(issued, len(executable)),
                           "issued_action_coverage_one_sided_95_upper": one_sided_upper(issued, len(executable)),
                           "conditional_exact_accuracy_given_action": correct / issued if issued else None},
            "clarify": {"total": len(clarify), "safe_handling_correct": safe, "unsafe_action_count": unsafe,
                        "safe_handling_wilson_95": wilson_interval(safe, len(clarify)),
                        "unsafe_action_one_sided_95_upper": one_sided_upper(unsafe, len(clarify))}}
    return {"languages": result, "interpretation": [
        "Coverage and conditional correctness use executable-command denominators; clarify cases are analyzed separately.",
        "Suppressed calls are not accepted commands. Runtime/parse/invalid responses are not successful abstentions.",
        "Fixed sample intervals condition on this synthetic command distribution. No interim stopping or threshold tuning is used."]}


def analyze(cases, first_records, second_records, bootstrap_repetitions=10000, seed=20261106,
            expected_primary_worlds=500, expected_families=25, expected_english_worlds=100,
            planned_discordance=0.5):
    validate_design(cases, expected_primary_worlds, expected_families, expected_english_worlds)
    first, second = score_records(cases, first_records), score_records(cases, second_records)
    tasks = {}
    for index, task in enumerate(("memory", "commander")):
        tasks[task] = {}
        for language, field in (("ja", "primary_ja"), ("en", "secondary_en")):
            selected = [case for case in cases if _task(case) == task and case["language"] == language]
            if selected:
                tasks[task][field] = _group(selected, first, second, bootstrap_repetitions, seed + index * 10 + (language == "en"))
    adjusted = holm_adjust([tasks[task]["primary_ja"]["exact_mcnemar_two_sided_p"] for task in tasks])
    for task, p_value in zip(tasks, adjusted):
        group = tasks[task]["primary_ja"]
        group["holm_adjusted_p_two_primary_tasks"] = p_value
        group["significant_after_holm_0_05"] = p_value < 0.05
        group["observed_difference_at_least_10_percentage_points"] = abs(group["difference_first_minus_second"]) >= 0.1
        group["conservative_lower_difference_at_least_10_percentage_points"] = group["conservative_paired_difference_95"][0] >= 0.1
    return {"schema_version": 1, "first_model": "hindsight", "second_model": "tinyjev", "tasks": tasks,
            "planning": {"primary_worlds_per_task": expected_primary_worlds,
                         "discordance_assumption": planned_discordance, "minimum_target_difference": 0.1,
                         "conservative_per_task_planning_alpha": 0.025,
                         "exact_power_under_planning_assumptions": exact_mcnemar_power(expected_primary_worlds, planned_discordance),
                         "discordance_sensitivity": {str(q): exact_mcnemar_power(expected_primary_worlds, q)
                                                     for q in (0.3, 0.5)}},
            "interpretation": [
                "Inference is conditional on the prespecified synthetic generator and fixed family mix, not real-game strategic strength.",
                "Japanese worlds are primary; paired English translations are secondary and never increase primary sample size.",
                "Exact paired McNemar tests are adjusted by Holm across the two Japanese tasks. Statistical difference and practical 10-point difference are distinct.",
                "Per-rate Wilson and conservative paired intervals preserve uncertainty at boundaries. Bootstrap intervals can degenerate and family-block intervals are robustness diagnostics.",
                "Domain-family names are not independent reasoning structures; optional mechanism-block/leave-mechanism-out summaries diagnose concentration in shared formal operators.",
                "Quality runs may share CPU resources. Their wall latencies are not a controlled performance benchmark.",
                "All planned rows are required. Explicit execution failures are incorrect; missing rows are not silently excluded."]}


def _descriptor(path):
    path = Path(path)
    return {"path": str(path.resolve()), "bytes": path.stat().st_size,
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cases", type=Path, required=True)
    parser.add_argument("--hindsight", type=Path, required=True)
    parser.add_argument("--tinyjev", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--bootstrap-repetitions", type=int, default=10000)
    parser.add_argument("--seed", type=int, default=20261106)
    parser.add_argument("--expected-primary-worlds", type=int, default=500)
    parser.add_argument("--expected-families", type=int, default=25)
    parser.add_argument("--expected-english-worlds", type=int, default=100)
    parser.add_argument("--planned-discordance", type=float, default=0.5)
    parser.add_argument("--needle-cases", type=Path)
    parser.add_argument("--needle", type=Path, nargs="+")
    args = parser.parse_args()
    result = analyze(load_cases(args.cases), load_records(args.hindsight), load_records(args.tinyjev),
                     args.bootstrap_repetitions, args.seed, args.expected_primary_worlds,
                     args.expected_families, args.expected_english_worlds, args.planned_discordance)
    result["sources"] = {name: _descriptor(path) for name, path in
                         (("cases", args.cases), ("hindsight", args.hindsight), ("tinyjev", args.tinyjev),
                          ("analysis_source", Path(__file__)))}
    if bool(args.needle_cases) != bool(args.needle):
        parser.error("--needle-cases and --needle must be provided together")
    if args.needle_cases:
        records = [row for path in args.needle for row in load_records(path)]
        result["needle"] = analyze_needle(load_cases(args.needle_cases), records)
        result["sources"]["needle_cases"] = _descriptor(args.needle_cases)
        result["sources"]["needle_runs"] = [_descriptor(path) for path in args.needle]
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2, allow_nan=False) + "\n")
    print(json.dumps({"output": str(args.output), "primary": {
        task: {key: result["tasks"][task]["primary_ja"][key] for key in
               ("worlds", "difference_first_minus_second", "holm_adjusted_p_two_primary_tasks")}
        for task in result["tasks"]}}, indent=2))


if __name__ == "__main__":
    main()
