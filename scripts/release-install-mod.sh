#!/usr/bin/env bash
set -euo pipefail

GAME_ROOT=""

while [[ $# -gt 0 ]]; do
  case "$1" in
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
PACKAGE_ROOT="${SCRIPT_DIR}"
MOD_SOURCE_ROOT="${PACKAGE_ROOT}/dglab_socket_spire2"

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

if [[ ! -f "${MOD_SOURCE_ROOT}/dglab_socket_spire2.dll" ]]; then
  echo "Could not find packaged mod files under '${MOD_SOURCE_ROOT}'." >&2
  exit 1
fi

GAME_ROOT_RESOLVED="$(resolve_game_root)"
MOD_ROOT="${GAME_ROOT_RESOLVED}/mods/dglab_socket_spire2"

mkdir -p "${MOD_ROOT}"
cp -fR "${MOD_SOURCE_ROOT}/." "${MOD_ROOT}/"

if [[ -f "${MOD_ROOT}/config.json" ]]; then
  if [[ ! -f "${MOD_ROOT}/dglab_socket_spire2.cfg" ]]; then
    mv -f "${MOD_ROOT}/config.json" "${MOD_ROOT}/dglab_socket_spire2.cfg"
  else
    rm -f "${MOD_ROOT}/config.json"
  fi
fi

echo "Installed to ${MOD_ROOT}"
echo "macOS / Linux / SteamOS support is currently experimental."
