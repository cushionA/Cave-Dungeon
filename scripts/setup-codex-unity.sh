#!/usr/bin/env bash
set +x
set -euo pipefail
umask 077

check_only=false
if [[ "${1:-}" == "--check" ]]; then
    check_only=true
    shift
fi
if [[ $# -gt 2 ]]; then
    printf 'Usage: %s [--check] [project-path] [private-assets.tar.gz]\n' "$0" >&2
    exit 2
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${1:-${UNITY_PROJECT_PATH:-$script_dir/..}}" && pwd)"
assets_archive="${2:-${UNITY_PRIVATE_ASSETS_ARCHIVE:-}}"
cli_home="${UNITY_CLI_HOME:-$HOME/.unity}"
export PATH="$cli_home/bin:$HOME/.local/bin:$HOME/.dotnet/tools:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export UNITY_NO_CRASH_REPORT=1 UNITY_NO_CLI_INVOKED_TELEMETRY=1 DO_NOT_TRACK=1

fail() { printf '[unity-setup] %s\n' "$*" >&2; exit 1; }
[[ "$(uname -s)" == "Linux" && "$(uname -m)" == "x86_64" ]] || fail 'An x86_64 Linux environment is required.'
[[ -f "$project_root/ProjectSettings/ProjectVersion.txt" ]] || fail 'ProjectVersion.txt is missing.'

if ! "$check_only"; then
    packages=(ca-certificates curl python3 tar git xvfb xauth libnss3 libxss1 libgbm1 libglu1-mesa libgl1-mesa-dri libglx-mesa0)
    if apt-cache show libgtk-3-0t64 >/dev/null 2>&1; then packages+=(libgtk-3-0t64); else packages+=(libgtk-3-0); fi
    if ! command -v dotnet >/dev/null 2>&1; then packages+=(dotnet-sdk-8.0); fi
    if apt-cache show libasound2t64 >/dev/null 2>&1; then packages+=(libasound2t64); else packages+=(libasound2); fi
    if [[ "$(id -u)" == 0 ]]; then
        apt=(apt-get)
    elif command -v sudo >/dev/null 2>&1 && sudo -n true 2>/dev/null; then
        apt=(sudo -n apt-get)
    else
        fail 'Installing Linux dependencies requires root or passwordless sudo.'
    fi
    export DEBIAN_FRONTEND=noninteractive
    "${apt[@]}" update
    "${apt[@]}" install --no-install-recommends -y "${packages[@]}"
fi

for executable in python3 curl git tar timeout xvfb-run xauth; do
    command -v "$executable" >/dev/null 2>&1 || fail "Missing prerequisite: $executable. Run setup without --check."
done

editor_version="$(python3 - "$project_root/ProjectSettings/ProjectVersion.txt" <<'PY'
import pathlib, re, sys
match = re.search(r'^m_EditorVersion:\s*(\d+\.\d+\.\d+[abfp]\d+)\s*$', pathlib.Path(sys.argv[1]).read_text(), re.M)
if not match:
    raise SystemExit('Invalid Unity Editor version in ProjectVersion.txt.')
print(match.group(1))
PY
)"

if [[ -n "$assets_archive" && "$check_only" == false ]]; then
    [[ -f "$assets_archive" ]] || fail 'The injected private asset archive is not a readable file.'
    python3 - "$project_root" "$assets_archive" <<'PY'
import pathlib, shutil, sys, tarfile
root = pathlib.Path(sys.argv[1]).resolve()
allowed = tuple(pathlib.PurePosixPath(value) for value in (
    'Assets/Plugins/Sirenix', 'Assets/ThirdParty/Plugins/Sirenix',
    'Assets/Plugins/DarkTonic', 'Assets/Plugins/Pixel Crushers',
    'Assets/AnyPortrait', 'Assets/SensorToolkit', 'Assets/ProCamera2D', 'Assets/Packages'))
allowed_meta = {pathlib.PurePosixPath(str(directory) + '.meta')
                for base in allowed for directory in (base, *base.parents) if directory.parts}
with tarfile.open(sys.argv[2], 'r:*') as archive:
    entries = []
    for member in archive.getmembers():
        path = pathlib.PurePosixPath(member.name)
        if path.is_absolute() or '..' in path.parts or '\\' in member.name:
            raise SystemExit('Unsafe path in private asset archive.')
        inside = any(path == base or base in path.parents for base in allowed)
        parent = member.isdir() and any(path in base.parents for base in allowed)
        metadata = member.isfile() and path in allowed_meta
        if not (inside or parent or metadata) or not (member.isdir() or member.isfile()):
            raise SystemExit('Archive contains a link, special file, or a path outside the permitted asset folders.')
        destination = root.joinpath(*path.parts)
        current = root
        for part in path.parts:
            current = current / part
            if current.is_symlink():
                raise SystemExit('Archive destination contains an existing symbolic link.')
        if not destination.resolve().is_relative_to(root):
            raise SystemExit('Archive destination escapes the project.')
        entries.append((member, destination))
    for member, destination in entries:
        if member.isdir():
            destination.mkdir(parents=True, exist_ok=True)
        else:
            destination.parent.mkdir(parents=True, exist_ok=True)
            with archive.extractfile(member) as source, destination.open('wb') as output:
                shutil.copyfileobj(source, output)
print('[unity-setup] Restored the injected local asset archive.')
PY
fi

python3 - "$project_root" <<'PY'
import pathlib, sys
root = pathlib.Path(sys.argv[1])
sirenix_assemblies = tuple(root / path / 'Assemblies' for path in (
    'Assets/Plugins/Sirenix', 'Assets/ThirdParty/Plugins/Sirenix'))
def has_sirenix_assembly(name):
    return any((directory / name).is_file() for directory in sirenix_assemblies)
required = {
    'Odin Inspector attributes': has_sirenix_assembly('Sirenix.OdinInspector.Attributes.dll'),
    'Odin Inspector Editor': has_sirenix_assembly('Sirenix.OdinInspector.Editor.dll'),
    'Odin Validator Editor': has_sirenix_assembly('Sirenix.OdinValidator.Editor.dll'),
    'AnyPortrait': (root / 'Assets/AnyPortrait/Assets/Scripts/AnyPortrait_ASMDEF.asmdef').is_file(),
    'SensorToolkit': (root / 'Assets/SensorToolkit/Sensors/Micosmo.SensorToolkit.asmdef').is_file(),
    'ProCamera2D': (root / 'Assets/ProCamera2D/Runtime/_ProCamera2D.Runtime.asmdef').is_file(),
    'MasterAudio': (root / 'Assets/Plugins/DarkTonic/MasterAudio/Scripts/Singleton/MasterAudio.cs').is_file(),
    'Pixel Crushers Common': (root / 'Assets/Plugins/Pixel Crushers/Common/Scripts/Message System/MessageSystem.cs').is_file(),
    'Dialogue System': (root / 'Assets/Plugins/Pixel Crushers/Dialogue System/Scripts/Manager/DialogueManager.cs').is_file(),
    'Quest Machine': (root / 'Assets/Plugins/Pixel Crushers/Quest Machine/Scripts/Quest Machine/QuestMachine.cs').is_file(),
    'LoveHate': all((root / path).is_file() for path in (
        'Assets/Plugins/Pixel Crushers/LoveHate/Scripts/Core/Faction Members/FactionManager.cs',
        'Assets/Plugins/Pixel Crushers/LoveHate/Scripts/Interaction/Deeds/DeedReporter.cs')),
}
missing = [name for name, present in required.items() if not present]
if missing:
    raise SystemExit('Missing private assets: ' + ', '.join(missing) + '. Inject an authorized local tar archive with UNITY_PRIVATE_ASSETS_ARCHIVE and run setup.')
PY

scratch="$(mktemp -d "${TMPDIR:-/tmp}/sistergame-unity-setup.XXXXXXXX")"
trap 'rm -rf -- "$scratch"' EXIT

unity_json() {
    unity "$@" --non-interactive --format json >"$scratch/cli.json" 2>"$scratch/cli.stderr" || return $?
    python3 - "$scratch/cli.json" <<'PY'
import json, sys
payload = json.load(open(sys.argv[1]))
for notification in payload.get('notifications', []):
    code = notification.get('code', '')
    if isinstance(code, str) and code.replace('_', '').isalnum():
        print('[unity-setup] Unity CLI notification: ' + code, file=sys.stderr)
if payload.get('success') is not True:
    raise SystemExit(1)
PY
}

if ! command -v unity >/dev/null 2>&1; then
    "$check_only" && fail 'Unity CLI is missing. Run setup without --check.'
    curl --fail --silent --show-error --location --proto '=https' --proto-redir '=https' \
        https://unity.com/install.sh --output "$scratch/install-unity.sh"
    UNITY_CLI_HOME="$cli_home" bash "$scratch/install-unity.sh"
    hash -r
fi
command -v unity >/dev/null 2>&1 || fail 'The official Unity CLI installer did not provide a runnable unity command.'
if ! "$check_only"; then
    unity_json self-update || fail 'Unity CLI update failed.'
fi

if ! unity_json editors path "$editor_version"; then
    "$check_only" && fail "Unity Editor $editor_version is not installed. Run setup without --check."
    [[ "${UNITY_ACCEPT_EDITOR_TERMS:-}" == 1 ]] || fail 'Review https://unity.com/legal/editor-terms-of-service/software and set UNITY_ACCEPT_EDITOR_TERMS=1 to accept Editor terms for this cloud machine.'
    unity_json install "$editor_version" --yes --accept-eula --timeout 3600 || fail 'Unity Editor installation failed.'
fi
unity_json editors verify "$editor_version" || fail 'The installed Unity Editor is incomplete.'

if [[ "${UNITY_ACCEPT_EDITOR_TERMS:-}" == 1 && "$check_only" == false ]]; then
    unity_json terms accept "$editor_version" --yes || fail 'Unity Editor terms acceptance failed.'
else
    unity_json terms status || fail 'Cannot read Unity Editor terms status.'
    python3 - "$scratch/cli.json" "$editor_version" <<'PY'
import json, sys
items = json.load(open(sys.argv[1])).get('data', {}).get('items', [])
matching = [item for item in items if item.get('version') == sys.argv[2]]
if not matching or any(item.get('status') not in ('accepted', 'no-terms-file') for item in matching):
    raise SystemExit('Unity Editor terms are not confirmed for this version. Review the terms and run setup with UNITY_ACCEPT_EDITOR_TERMS=1.')
PY
fi

license_mode="${UNITY_LICENSE_MODE:-existing}"
if [[ -n "${UNITY_LICENSE_FILE:-}" && -z "${UNITY_LICENSE_MODE:-}" ]]; then license_mode=file; fi
case "$license_mode" in existing|floating|file) ;; *) fail 'UNITY_LICENSE_MODE must be existing, floating, or file.' ;; esac
if ! unity_json license status; then
    "$check_only" && fail 'No usable Unity license on this cloud machine. Configure a permitted cloud-machine license and rerun setup.'
    case "$license_mode" in
        floating) unity_json license activate --floating || fail 'The configured floating license could not be activated.' ;;
        file)
            [[ -n "${UNITY_LICENSE_FILE:-}" && -f "$UNITY_LICENSE_FILE" ]] || fail 'UNITY_LICENSE_FILE must identify a license issued for this cloud machine.'
            unity_json license activate --file "$UNITY_LICENSE_FILE" || fail 'The cloud-machine license file could not be activated.'
            ;;
        existing) fail 'No usable cloud-machine Unity license. Activate one on this machine or explicitly configure floating/file mode.' ;;
    esac
    unity_json license status || fail 'Unity license validation failed after activation.'
fi
python3 - "$scratch/cli.json" <<'PY'
import json, sys
data = json.load(open(sys.argv[1])).get('data', {})
if data.get('active') is not True and data.get('floatingServerHealthy') is not True:
    raise SystemExit('Unity did not confirm an active license or a healthy configured floating server.')
PY

nuget_ready() {
    python3 - "$project_root" <<'PY'
import pathlib, sys, xml.etree.ElementTree as ET
root = pathlib.Path(sys.argv[1])
config = root / 'Assets/packages.config'
if not config.exists():
    raise SystemExit(0)
missing = any(not any((root / 'Assets/Packages' / (item.attrib['id'] + '.' + item.attrib['version'])).rglob('*.dll'))
              for item in ET.parse(config).getroot().findall('package'))
raise SystemExit(1 if missing else 0)
PY
}
if ! nuget_ready; then
    "$check_only" && fail 'NuGet packages are missing. Run setup without --check to restore Assets/packages.config.'
    command -v dotnet >/dev/null 2>&1 || fail 'The .NET 8 SDK is required for NuGetForUnity restore.'
    nuget_tools="$HOME/.local/share/sistergame/nuget-tools"
    if [[ ! -x "$nuget_tools/nugetforunity" ]]; then
        dotnet tool install --tool-path "$nuget_tools" --version 4.5.0 NuGetForUnity.Cli >"$scratch/nuget-install.log" 2>&1 \
            || fail 'NuGetForUnity CLI installation failed.'
    fi
    "$nuget_tools/nugetforunity" restore "$project_root" >"$scratch/nuget-restore.log" 2>&1 \
        || fail 'NuGet package restore failed.'
    nuget_ready || fail 'NuGet restore did not produce the required package assemblies.'
fi

printf '[unity-setup] Ready: Unity %s, cloud-machine license, private assets, and NuGet dependencies.\n' "$editor_version"
