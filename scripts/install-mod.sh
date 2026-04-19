#!/usr/bin/env bash
set -euo pipefail

REFERENCE_DIR=""
GAME_ROOT=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --reference-dir)
      REFERENCE_DIR="${2:-}"
      shift 2
      ;;
    --game-root)
      GAME_ROOT="${2:-}"
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
PROJECT_PATH="${REPO_ROOT}/dg_lab_socket_spire2.csproj"
OUTPUT_DIR="${REPO_ROOT}/bin/Release/net9.0"
OUTPUT_DLL="${OUTPUT_DIR}/dglab_socket_spire2.dll"
DOTNET_DOWNLOAD_URL="https://aka.ms/dotnet-download"

test_sts2_reference_dir() {
  local path="$1"
  [[ -f "${path}/sts2.dll" && -f "${path}/GodotSharp.dll" && -f "${path}/0Harmony.dll" ]]
}

resolve_reference_dir() {
  local candidate

  for candidate in \
    "${REFERENCE_DIR}" \
    "${STS2_REF_DIR:-}" \
    "${REPO_ROOT}/refs/sts2" \
    "${GameDir:-}" \
    "${HOME}/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/data_sts2_macos" \
    "${HOME}/.local/share/Steam/steamapps/common/Slay the Spire 2/data_sts2_linux_x86_64" \
    "${HOME}/.steam/steam/steamapps/common/Slay the Spire 2/data_sts2_linux_x86_64"
  do
    if [[ -n "${candidate}" && -d "${candidate}" ]] && test_sts2_reference_dir "${candidate}"; then
      printf '%s\n' "${candidate}"
      return 0
    fi
  done

  echo "Could not find STS2 reference assemblies. Pass --reference-dir, set STS2_REF_DIR, or place refs under refs/sts2." >&2
  exit 1
}

read_library_roots() {
  local steam_root="$1"
  local library_vdf="${steam_root}/steamapps/libraryfolders.vdf"

  printf '%s\n' "${steam_root}"
  if [[ -f "${library_vdf}" ]]; then
    sed -nE 's/.*"path"[[:space:]]*"([^"]+)".*/\1/p; s/^[[:space:]]*"[0-9]+"[[:space:]]*"([^"]+)".*/\1/p' "${library_vdf}" \
      | sed 's#\\\\#/#g'
  fi
}

resolve_game_root_from_steam() {
  local steam_root
  local library_root
  local candidate

  for steam_root in \
    "${STEAM_DIR:-}" \
    "${HOME}/Library/Application Support/Steam" \
    "${HOME}/.local/share/Steam" \
    "${HOME}/.steam/steam" \
    "${HOME}/.var/app/com.valvesoftware.Steam/.local/share/Steam"
  do
    [[ -n "${steam_root}" && -d "${steam_root}" ]] || continue

    while IFS= read -r library_root; do
      [[ -n "${library_root}" ]] || continue
      candidate="${library_root}/steamapps/common/Slay the Spire 2"
      if [[ -d "${candidate}" ]]; then
        printf '%s\n' "${candidate}"
        return 0
      fi
    done < <(read_library_roots "${steam_root}" | awk '!seen[$0]++')
  done

  return 1
}

resolve_game_root() {
  local candidate

  for candidate in \
    "${GAME_ROOT}" \
    "${STS2_GAME_DIR:-}" \
    "${GameRoot:-}"
  do
    if [[ -n "${candidate}" && -d "${candidate}" ]]; then
      printf '%s\n' "${candidate}"
      return 0
    fi
  done

  if candidate="$(resolve_game_root_from_steam)"; then
    printf '%s\n' "${candidate}"
    return 0
  fi

  while true; do
    read -r -p "Could not locate Slay the Spire 2 automatically. Enter the game install directory: " candidate
    if [[ -n "${candidate}" && -d "${candidate}" ]]; then
      printf '%s\n' "${candidate}"
      return 0
    fi

    echo "Directory not found: ${candidate}" >&2
  done
}

configure_dotnet_environment() {
  export DOTNET_CLI_HOME="${REPO_ROOT}/.tools/dotnet-cli"
  export NUGET_PACKAGES="${REPO_ROOT}/.tools/nuget-packages"
  export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
  export DOTNET_CLI_TELEMETRY_OPTOUT=1
  export DOTNET_NOLOGO=1

  mkdir -p "${DOTNET_CLI_HOME}" "${NUGET_PACKAGES}"
}

get_required_sdk_major() {
  sed -nE 's#.*<TargetFramework>net([0-9]+)\.0</TargetFramework>.*#\1#p' "${PROJECT_PATH}" | head -n 1
}

assert_dotnet_sdk() {
  local required_major
  local sdk_list
  local installed_major

  if ! command -v dotnet >/dev/null 2>&1; then
    echo "未检测到 dotnet 命令。构建此项目需要安装 .NET SDK 9.0 或更高版本。" >&2
    echo "下载地址：${DOTNET_DOWNLOAD_URL}" >&2
    exit 1
  fi

  if ! sdk_list="$(dotnet --list-sdks)"; then
    echo "无法查询当前已安装的 .NET SDK。请安装 .NET SDK 9.0 或更高版本后重试。" >&2
    echo "下载地址：${DOTNET_DOWNLOAD_URL}" >&2
    exit 1
  fi

  required_major="$(get_required_sdk_major)"
  installed_major="$(
    printf '%s\n' "${sdk_list}" \
      | sed -nE 's/^([0-9]+)\..*/\1/p' \
      | sort -n \
      | tail -n 1
  )"

  if [[ -n "${required_major}" ]] && [[ -z "${installed_major}" || "${installed_major}" -lt "${required_major}" ]]; then
    if [[ -n "${sdk_list//[[:space:]]/}" ]]; then
      echo "此项目目标框架为 net${required_major}.0，需要 .NET SDK ${required_major}.0 或更高版本。" >&2
      echo "当前已安装 SDK：${sdk_list//$'\n'/, }" >&2
    else
      echo "此项目目标框架为 net${required_major}.0，需要 .NET SDK ${required_major}.0 或更高版本。" >&2
      echo "当前未检测到任何 .NET SDK。" >&2
    fi
    echo "下载地址：${DOTNET_DOWNLOAD_URL}" >&2
    exit 1
  fi
}

REFERENCE_DIR_RESOLVED="$(resolve_reference_dir)"
echo "Using STS2 reference directory: ${REFERENCE_DIR_RESOLVED}"
configure_dotnet_environment
assert_dotnet_sdk
dotnet build "${PROJECT_PATH}" -c Release "/p:Sts2ReferenceDir=${REFERENCE_DIR_RESOLVED}"

GAME_ROOT_RESOLVED="$(resolve_game_root)"
MOD_ROOT="${GAME_ROOT_RESOLVED}/mods/dglab_socket_spire2"

if [[ ! -f "${OUTPUT_DLL}" ]]; then
  echo "Could not find build output '${OUTPUT_DLL}'. The build step did not produce the mod assembly." >&2
  exit 1
fi

mkdir -p "${MOD_ROOT}/waves"
cp -f "${OUTPUT_DLL}" "${MOD_ROOT}/"
cp -f "${REPO_ROOT}/manifest.json" "${MOD_ROOT}/"
cp -f "${REPO_ROOT}/data/official_waves.json" "${MOD_ROOT}/official_waves.json"

if [[ -f "${MOD_ROOT}/config.json" ]]; then
  if [[ ! -f "${MOD_ROOT}/dglab_socket_spire2.cfg" ]]; then
    mv -f "${MOD_ROOT}/config.json" "${MOD_ROOT}/dglab_socket_spire2.cfg"
  else
    rm -f "${MOD_ROOT}/config.json"
  fi
fi

echo "Installed to ${MOD_ROOT}"
echo "macOS / Linux / SteamOS support is currently experimental."
