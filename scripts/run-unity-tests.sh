#!/usr/bin/env bash
set +x
set -euo pipefail
umask 077

if [[ $# -gt 2 ]]; then
    printf 'Usage: %s [EditMode|PlayMode|All] [project-path]\n' "$0" >&2
    exit 2
fi
mode="${1:-All}"
case "${mode,,}" in
    editmode) modes=(EditMode) ;;
    playmode) modes=(PlayMode) ;;
    all) modes=(EditMode PlayMode) ;;
    *) printf 'Test mode must be EditMode, PlayMode, or All.\n' >&2; exit 2 ;;
esac
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${2:-${UNITY_PROJECT_PATH:-$script_dir/..}}" && pwd)"
cli_home="${UNITY_CLI_HOME:-$HOME/.unity}"
export PATH="$cli_home/bin:$HOME/.local/bin:$HOME/.dotnet/tools:$PATH"
export UNITY_NO_CRASH_REPORT=1 UNITY_NO_CLI_INVOKED_TELEMETRY=1 DO_NOT_TRACK=1

bash "$script_dir/setup-codex-unity.sh" --check "$project_root"
editor_version="$(python3 - "$project_root/ProjectSettings/ProjectVersion.txt" <<'PY'
import pathlib, re, sys
print(re.search(r'^m_EditorVersion:\s*(\S+)', pathlib.Path(sys.argv[1]).read_text(), re.M).group(1))
PY
)"
timeout_seconds="$(python3 - "${UNITY_TEST_TIMEOUT_SECONDS:-1800}" <<'PY'
import sys
value = sys.argv[1]
if not value.isascii() or not value.isdigit() or not 1 <= int(value) <= 86400:
    raise SystemExit('UNITY_TEST_TIMEOUT_SECONDS must be a whole number from 1 to 86400.')
print(int(value))
PY
)"
results_root="${UNITY_TEST_RESULTS_DIR:-${TMPDIR:-/tmp}/sistergame-unity-tests}"
mkdir -p -- "$results_root"
results_root="$(cd -- "$results_root" && pwd)"
status=0

for test_mode in "${modes[@]}"; do
    run_dir="$(mktemp -d "$results_root/$test_mode.XXXXXXXX")"
    xml="$run_dir/results.xml"
    editor_log="$run_dir/editor.log"
    command_args=(unity test "$project_root" --editor-version "$editor_version" --mode "$test_mode"
        --output "$xml" --timeout "$timeout_seconds" --non-interactive --format json)
    if [[ -n "${UNITY_TEST_FILTER:-}" ]]; then command_args+=(--filter "$UNITY_TEST_FILTER"); fi
    command_args+=(-- -logFile "$editor_log")
    if [[ -z "${DISPLAY:-}" ]]; then command_args=(xvfb-run --auto-servernum "${command_args[@]}"); fi
    command_status=0
    timeout --signal=TERM --kill-after=30s "$((timeout_seconds + 60))s" "${command_args[@]}" \
        >"$run_dir/cli.json" 2>"$run_dir/cli.stderr.log" || command_status=$?

    report_status=0
    python3 - "$xml" "$test_mode" <<'PY' || report_status=$?
import pathlib, sys, xml.etree.ElementTree as ET
path = pathlib.Path(sys.argv[1])
if not path.is_file():
    raise SystemExit(sys.argv[2] + ': no NUnit XML was produced.')
try:
    root = ET.parse(path).getroot()
except ET.ParseError:
    raise SystemExit(sys.argv[2] + ': NUnit XML is incomplete or invalid.')
cases = list(root.iter('test-case'))
passed = sum(case.get('result') == 'Passed' for case in cases)
failed = sum(case.get('result') == 'Failed' for case in cases)
if root.tag != 'test-run' or not cases or passed + failed == 0:
    raise SystemExit(sys.argv[2] + ': no tests actually executed.')
print(f'{sys.argv[2]}: {len(cases)} cases, {passed} passed, {failed} failed, result={root.get("result")}')
if root.get('result') != 'Passed' or failed or int(root.get('failed', '0')):
    raise SystemExit(1)
PY
    if (( command_status != 0 || report_status != 0 )); then
        printf '[unity-test] %s failed (command exit %s). Artifacts: %s\n' "$test_mode" "$command_status" "$run_dir" >&2
        status=1
    else
        printf '[unity-test] %s passed. Artifacts: %s\n' "$test_mode" "$run_dir"
    fi
done

exit "$status"
