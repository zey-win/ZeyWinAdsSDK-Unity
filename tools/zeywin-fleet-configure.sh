#!/usr/bin/env bash
set -euo pipefail

ROOT="/Volumes/Work/games"
CONFIG=""
SDK_REF="${ZEYWIN_SDK_VERSION:-v3.9.37}"
if [[ "$SDK_REF" != v* ]]; then
  SDK_REF="v$SDK_REF"
fi
CRASHGUARD_REF="2b3947155206bc445e2d6088ac51cdf2760f921d"
GLOBAL_UNITY_PATH=""
REQUIRE_UNITY6=0
DRY_RUN=0
DISCOVER_ONLY=0
SNAPSHOT_ROOT=""

usage() {
  cat <<'USAGE'
Usage:
  tools/zeywin-fleet-configure.sh --config fleet.tsv [--root /path/to/games] [--sdk-ref v3.9.37] [--unity-path /path/to/Unity] [--require-unity6] [--dry-run]
  tools/zeywin-fleet-configure.sh --discover [--root /path/to/games]

TSV columns:
  projectPath zeywinApiKey adMobAppId bannerAdUnitId interstitialAdUnitId rewardedAdUnitId

Optional columns:
  productName companyName androidPackageId androidVersionName androidVersionCode unityPath

The five monetization values are exported through environment variables before
Unity starts, so they do not appear in Unity command-line logs.

Use --unity-path with --require-unity6 when upgrading older game projects
through a Unity 6 editor instead of the editor version recorded in
ProjectSettings/ProjectVersion.txt.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --root)
      ROOT="$2"
      shift 2
      ;;
    --config)
      CONFIG="$2"
      shift 2
      ;;
    --sdk-ref)
      SDK_REF="$2"
      shift 2
      ;;
    --crashguard-ref)
      CRASHGUARD_REF="$2"
      shift 2
      ;;
    --unity-path)
      GLOBAL_UNITY_PATH="$2"
      shift 2
      ;;
    --require-unity6)
      REQUIRE_UNITY6=1
      shift
      ;;
    --snapshot-root)
      SNAPSHOT_ROOT="$2"
      shift 2
      ;;
    --dry-run)
      DRY_RUN=1
      shift
      ;;
    --discover)
      DISCOVER_ONLY=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if [[ -z "$SNAPSHOT_ROOT" ]]; then
  SNAPSHOT_ROOT="$ROOT/_zeywin_fleet_snapshots/$(date +%Y%m%d-%H%M%S)"
fi

discover_projects() {
  find "$ROOT" -path '*/ProjectSettings/ProjectVersion.txt' -print \
    | sed 's#/ProjectSettings/ProjectVersion.txt$##' \
    | sort
}

unity_for_project() {
  local project="$1"
  local explicit="${2:-}"

  if [[ -n "$explicit" ]]; then
    validate_unity_path "$explicit" "$project" && echo "$explicit"
    return
  fi

  if [[ -n "$GLOBAL_UNITY_PATH" ]]; then
    validate_unity_path "$GLOBAL_UNITY_PATH" "$project" && echo "$GLOBAL_UNITY_PATH"
    return
  fi

  if [[ -n "${UNITY_PATH:-}" ]]; then
    validate_unity_path "$UNITY_PATH" "$project" && echo "$UNITY_PATH"
    return
  fi

  local version
  version=$(sed -n 's/^m_EditorVersion: //p' "$project/ProjectSettings/ProjectVersion.txt" | head -n 1)
  local candidate="/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/MacOS/Unity"
  if [[ -x "$candidate" ]]; then
    validate_unity_path "$candidate" "$project" && echo "$candidate"
    return
  fi

  echo "Unity editor not found for $project version $version" >&2
  return 1
}

validate_unity_path() {
  local unity_path="$1"
  local project="$2"

  if [[ ! -x "$unity_path" ]]; then
    echo "Unity editor is not executable for $project: $unity_path" >&2
    return 1
  fi

  if [[ "$REQUIRE_UNITY6" -eq 1 && "$unity_path" != *"/Editor/6000."* ]]; then
    echo "Skipping $project: --require-unity6 needs a Unity 6 editor, got $unity_path" >&2
    return 1
  fi
}

pin_packages() {
  local project="$1"
  local manifest="$project/Packages/manifest.json"

  if [[ ! -f "$manifest" ]]; then
    mkdir -p "$project/Packages"
    printf '{\n  "dependencies": {}\n}\n' > "$manifest"
  fi

  ruby -rjson -e '
    path, sdk_ref, crash_ref = ARGV
    data = JSON.parse(File.read(path))
    data["dependencies"] ||= {}
    data["dependencies"]["com.zeywin.ads"] = "https://github.com/zey-win/ZeyWinAdsSDK-Unity.git##{sdk_ref}"
    data["dependencies"]["com.crashguard.sdk"] = "https://github.com/zey-win/CrashGuardSDK-Unity.git##{crash_ref}"
    File.write(path, JSON.pretty_generate(data) + "\n")
  ' "$manifest" "$SDK_REF" "$CRASHGUARD_REF"
}

snapshot_if_needed() {
  local project="$1"
  local name
  name=$(basename "$project")

  if [[ -d "$project/.git" ]]; then
    git -C "$project" status --short > "$project/Logs/zeywin-fleet-git-status.txt" 2>/dev/null || true
    return
  fi

  mkdir -p "$SNAPSHOT_ROOT"
  tar -C "$project" \
    --exclude='./Library' \
    --exclude='./Temp' \
    --exclude='./Obj' \
    --exclude='./Build' \
    --exclude='./Builds' \
    -czf "$SNAPSHOT_ROOT/$name.tgz" Assets Packages ProjectSettings 2>/dev/null
}

print_discovery() {
  printf 'projectPath\tunityVersion\tgit\n'
  while IFS= read -r project; do
    local version git_state
    version=$(sed -n 's/^m_EditorVersion: //p' "$project/ProjectSettings/ProjectVersion.txt" | head -n 1)
    if [[ -d "$project/.git" ]]; then
      git_state="git"
    else
      git_state="non-git"
    fi
    printf '%s\t%s\t%s\n' "$project" "$version" "$git_state"
  done < <(discover_projects)
}

require_value() {
  local value="$1"
  local column="$2"
  local project="$3"

  if [[ -z "$value" ]]; then
    echo "Skipping $project: missing required column $column" >&2
    return 1
  fi
}

run_row() {
  local project="$1"
  local api_key="$2"
  local app_id="$3"
  local banner_id="$4"
  local interstitial_id="$5"
  local rewarded_id="$6"
  local product_name="${7:-}"
  local company_name="${8:-}"
  local package_id="${9:-}"
  local version_name="${10:-}"
  local version_code="${11:-}"
  local unity_path="${12:-}"

  require_value "$project" "projectPath" "$project" || return 0
  require_value "$api_key" "zeywinApiKey" "$project" || return 0
  require_value "$app_id" "adMobAppId" "$project" || return 0
  require_value "$banner_id" "bannerAdUnitId" "$project" || return 0
  require_value "$interstitial_id" "interstitialAdUnitId" "$project" || return 0
  require_value "$rewarded_id" "rewardedAdUnitId" "$project" || return 0

  if [[ ! -f "$project/ProjectSettings/ProjectVersion.txt" ]]; then
    echo "Skipping $project: not a Unity project" >&2
    return 0
  fi

  if ! unity_path=$(unity_for_project "$project" "$unity_path"); then
    return 0
  fi
  mkdir -p "$project/Logs"

  echo "Configuring $project"
  if [[ "$DRY_RUN" -eq 1 ]]; then
    echo "  dry-run: unity=$unity_path sdk=$SDK_REF"
    return 0
  fi

  snapshot_if_needed "$project"
  pin_packages "$project"

  local log="$project/Logs/zeywin-fleet-configure-$(date +%Y%m%d-%H%M%S).log"
  local args=(
    -batchmode
    -quit
    -projectPath "$project"
    -executeMethod ZeyWinAds.Editor.ZeyWinAdsProjectConfigurator.ApplyFromCommandLine
  )

  [[ -n "$product_name" ]] && args+=(-productName "$product_name")
  [[ -n "$company_name" ]] && args+=(-companyName "$company_name")
  [[ -n "$package_id" ]] && args+=(-androidPackageId "$package_id")
  [[ -n "$version_name" ]] && args+=(-androidVersionName "$version_name")
  [[ -n "$version_code" ]] && args+=(-androidVersionCode "$version_code")

  ZEYWIN_API_KEY="$api_key" \
  ADMOB_APP_ID="$app_id" \
  ADMOB_BANNER_AD_UNIT_ID="$banner_id" \
  ADMOB_INTERSTITIAL_AD_UNIT_ID="$interstitial_id" \
  ADMOB_REWARDED_AD_UNIT_ID="$rewarded_id" \
    "$unity_path" "${args[@]}" > "$log" 2>&1

  echo "  log=$log"
}

if [[ "$DISCOVER_ONLY" -eq 1 ]]; then
  print_discovery
  exit 0
fi

if [[ -z "$CONFIG" ]]; then
  echo "Missing --config" >&2
  usage >&2
  exit 2
fi

if [[ ! -f "$CONFIG" ]]; then
  echo "Config file not found: $CONFIG" >&2
  exit 2
fi

{
  read -r _header || exit 0
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line//$'\t'/$'\x1f'}"
    IFS=$'\x1f' read -r \
      project_path \
      zeywin_api_key \
      admob_app_id \
      banner_ad_unit_id \
      interstitial_ad_unit_id \
      rewarded_ad_unit_id \
      product_name \
      company_name \
      android_package_id \
      android_version_name \
      android_version_code \
      unity_path \
      _extra <<< "$line"

  [[ -z "${project_path:-}" ]] && continue
  run_row \
    "$project_path" \
    "${zeywin_api_key:-}" \
    "${admob_app_id:-}" \
    "${banner_ad_unit_id:-}" \
    "${interstitial_ad_unit_id:-}" \
    "${rewarded_ad_unit_id:-}" \
    "${product_name:-}" \
    "${company_name:-}" \
    "${android_package_id:-}" \
    "${android_version_name:-}" \
    "${android_version_code:-}" \
    "${unity_path:-}"
  done
} < "$CONFIG"
