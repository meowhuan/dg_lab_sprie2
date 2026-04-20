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
      echo "未知参数：$1" >&2
      exit 1
      ;;
  esac
done

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
PACKAGE_ROOT="${SCRIPT_DIR}"
MOD_SOURCE_ROOT="${PACKAGE_ROOT}/dglab_socket_spire2"

print_if_dir() {
  local candidate="$1"

  if [[ -n "${candidate}" && -d "${candidate}" ]]; then
    printf '%s\n' "${candidate}"
  fi
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
    read -r -p "无法自动定位《杀戮尖塔 2》。请输入游戏安装目录: " candidate
    if [[ -n "${candidate}" && -d "${candidate}" ]]; then
      printf '%s\n' "${candidate}"
      return 0
    fi

    echo "目录不存在：${candidate}" >&2
  done
}

resolve_mods_root() {
  local game_root="$1"

  for app_bundle in \
    "${game_root}/SlayTheSpire2.app" \
    "${game_root}/Slay the Spire 2.app"
  do
    if [[ -d "${app_bundle}/Contents/MacOS" ]]; then
      printf '%s\n' "${app_bundle}/Contents/MacOS/mods"
      return 0
    fi
  done

  printf '%s\n' "${game_root}/mods"
}

get_save_appdata_roots() {
  printf '%s\n' \
    "${HOME}/Library/Application Support/SlayTheSpire2" \
    "${HOME}/.config/SlayTheSpire2" \
    "${HOME}/.local/share/SlayTheSpire2" \
    | while IFS= read -r candidate; do
      print_if_dir "${candidate}"
    done
}

get_save_storage_roots() {
  local appdata_root
  local container_name

  while IFS= read -r appdata_root; do
    [[ -n "${appdata_root}" ]] || continue

    for container_name in steam default; do
      if [[ -d "${appdata_root}/${container_name}" ]]; then
        find "${appdata_root}/${container_name}" -mindepth 1 -maxdepth 1 -type d -print 2>/dev/null
      fi
    done
  done < <(get_save_appdata_roots)
}

get_save_profile_mappings() {
  local save_root
  local profile_dir
  local profile_name
  local vanilla_save_dir
  local modded_save_dir

  while IFS= read -r save_root; do
    [[ -n "${save_root}" ]] || continue

    while IFS= read -r -d '' profile_dir; do
      profile_name="$(basename "${profile_dir}")"
      vanilla_save_dir="${profile_dir}/saves"
      [[ -d "${vanilla_save_dir}" ]] || continue

      modded_save_dir="${save_root}/modded/${profile_name}/saves"
      printf '%s|%s|%s|%s\n' "${save_root}" "${profile_name}" "${vanilla_save_dir}" "${modded_save_dir}"
    done < <(find "${save_root}" -mindepth 1 -maxdepth 1 -type d -name 'profile*' -print0 2>/dev/null)
  done < <(get_save_storage_roots | awk 'NF && !seen[$0]++')
}

directory_has_items() {
  local candidate="$1"

  if [[ ! -d "${candidate}" ]]; then
    return 1
  fi

  find "${candidate}" -mindepth 1 -maxdepth 1 -print -quit 2>/dev/null | grep -q .
}

get_save_backup_paths() {
  local steam_root
  local steam_user_root

  {
    get_save_appdata_roots
    get_save_storage_roots

    for steam_root in \
      "${STEAM_DIR:-}" \
      "${HOME}/Library/Application Support/Steam" \
      "${HOME}/.local/share/Steam" \
      "${HOME}/.steam/steam" \
      "${HOME}/.var/app/com.valvesoftware.Steam/.local/share/Steam"
    do
      [[ -n "${steam_root}" && -d "${steam_root}" ]] || continue

      if [[ -d "${steam_root}/userdata" ]]; then
        while IFS= read -r -d '' steam_user_root; do
          print_if_dir "${steam_user_root}/2868840"
        done < <(find "${steam_root}/userdata" -mindepth 1 -maxdepth 1 -type d -print0 2>/dev/null)
      fi
    done
  } | awk 'NF && !seen[$0]++'
}

get_save_backup_hints() {
  local steam_root

  {
    printf '%s\n' \
      "${HOME}/Library/Application Support/SlayTheSpire2" \
      "${HOME}/.config/SlayTheSpire2" \
      "${HOME}/.local/share/SlayTheSpire2" \
      "${HOME}/Library/Application Support/SlayTheSpire2/default/1" \
      "${HOME}/Library/Application Support/SlayTheSpire2/steam/<Steam64ID>" \
      "${HOME}/.config/SlayTheSpire2/default/1" \
      "${HOME}/.config/SlayTheSpire2/steam/<Steam64ID>" \
      "${HOME}/.local/share/SlayTheSpire2/default/1" \
      "${HOME}/.local/share/SlayTheSpire2/steam/<Steam64ID>"

    for steam_root in \
      "${STEAM_DIR:-}" \
      "${HOME}/Library/Application Support/Steam" \
      "${HOME}/.local/share/Steam" \
      "${HOME}/.steam/steam" \
      "${HOME}/.var/app/com.valvesoftware.Steam/.local/share/Steam"
    do
      [[ -n "${steam_root}" ]] || continue
      printf '%s\n' "${steam_root}/userdata/<Steam3AccountID>/2868840"
    done
  } | awk 'NF && !seen[$0]++'
}

show_install_safety_notice() {
  local found_save_paths=0

  echo
  echo "警告：《杀戮尖塔 2》的原版和 mod 模式使用不同的存档目录。"
  echo "警告：如果你第一次进入 mod 模式后发现进度像是【不见了】，通常不是丢档，而是原版存档还没有复制到 mod 存档目录。"
  echo "警告：此安装器可以帮助你准备 mod 存档目录并复制原版存档，但如果游戏之后使用了不同的存档根目录或不同的 Steam 账号，自动迁移仍可能不可用。"
  echo
  echo "继续之前："
  echo "  1. 请先彻底关闭《杀戮尖塔 2》。"
  echo "  2. 请先把存档备份到游戏目录和 Steam 目录之外的位置。"

  while IFS= read -r save_path; do
    [[ -n "${save_path}" ]] || continue

    if [[ ${found_save_paths} -eq 0 ]]; then
      echo "检测到以下存档目录，建议先备份："
      found_save_paths=1
    fi

    printf '  - %s\n' "${save_path}"
  done < <(get_save_backup_paths)

  if [[ ${found_save_paths} -eq 0 ]]; then
    echo "未检测到现有存档目录，请优先检查这些常见路径："
    while IFS= read -r hint_path; do
      [[ -n "${hint_path}" ]] || continue
      printf '  - %s\n' "${hint_path}"
    done < <(get_save_backup_hints)
  fi

  echo
}

confirm_backup_done() {
  local backup_confirmation

  read -r -p "如已关闭游戏并完成存档备份，请输入 1 继续: " backup_confirmation
  if [[ "${backup_confirmation}" != "1" ]]; then
    echo "安装已取消。请先备份存档，然后重新运行安装器。" >&2
    exit 1
  fi
}

install_packaged_mod() {
  local resolved_game_root="$1"
  local mod_root="$(resolve_mods_root "${resolved_game_root}")/dglab_socket_spire2"

  mkdir -p "${mod_root}"
  cp -fR "${MOD_SOURCE_ROOT}/." "${mod_root}/"

  if [[ -f "${mod_root}/config.json" ]]; then
    if [[ ! -f "${mod_root}/dglab_socket_spire2.cfg" ]]; then
      mv -f "${mod_root}/config.json" "${mod_root}/dglab_socket_spire2.cfg"
    else
      rm -f "${mod_root}/config.json"
    fi
  fi

  printf '%s\n' "${mod_root}"
}

test_modded_environment_enabled() {
  local save_root

  while IFS= read -r save_root; do
    [[ -n "${save_root}" ]] || continue

    if [[ -d "${save_root}/modded" ]]; then
      return 0
    fi
  done < <(get_save_storage_roots)

  return 1
}

ensure_modded_save_directories() {
  local line
  local save_root
  local profile_name
  local vanilla_save_dir
  local modded_save_dir

  while IFS= read -r line; do
    [[ -n "${line}" ]] || continue
    IFS='|' read -r save_root profile_name vanilla_save_dir modded_save_dir <<EOF
${line}
EOF
    mkdir -p "${modded_save_dir}"
  done < <(get_save_profile_mappings)
}

show_save_migration_paths() {
  local line
  local save_root
  local profile_name
  local vanilla_save_dir
  local modded_save_dir
  local found=0

  while IFS= read -r line; do
    [[ -n "${line}" ]] || continue
    IFS='|' read -r save_root profile_name vanilla_save_dir modded_save_dir <<EOF
${line}
EOF
    if [[ ${found} -eq 0 ]]; then
      echo "检测到以下 原版 -> mod 模式 存档路径："
      found=1
    fi
    echo "  - 来源：${vanilla_save_dir}"
    echo "    目标：${modded_save_dir}"
  done < <(get_save_profile_mappings)

  if [[ ${found} -eq 0 ]]; then
    echo "警告：没有在本地 SlayTheSpire2 存档根目录下检测到原版存档配置目录。" >&2
  fi
}

launch_game_for_mod_activation() {
  local resolved_game_root="$1"
  local launch_target

  for launch_target in \
    "${resolved_game_root}/SlayTheSpire2.x86_64" \
    "${resolved_game_root}/SlayTheSpire2" \
    "${resolved_game_root}/SlayTheSpire2.app" \
    "${resolved_game_root}/Slay the Spire 2.app"
  do
    if [[ -x "${launch_target}" && ! -d "${launch_target}" ]]; then
      nohup "${launch_target}" >/dev/null 2>&1 &
      return 0
    fi

    if [[ -d "${launch_target}" ]] && command -v open >/dev/null 2>&1; then
      open "${launch_target}" >/dev/null 2>&1
      return 0
    fi
  done

  return 1
}

offer_mod_environment_activation() {
  local resolved_game_root="$1"
  local activation_choice
  local ready_confirmation

  if test_modded_environment_enabled; then
    return 0
  fi

  echo
  echo "警告：目前还没有检测到 mod 模式存档目录。"
  echo "下次启动游戏时，游戏应该能在 mods 文件夹里检测到这个 mod。"
  echo "如果游戏弹出 mod 提示，请选择【Load Mods】，游戏应会重启一次并进入 mod 模式。"
  echo "安装器现在可以先为你准备对应的 mod 存档目录，并可选地帮你启动游戏。"

  show_save_migration_paths || true
  echo "如果游戏之后改用了别的存档根目录或别的 Steam 账号，自动迁移仍可能不可用。"
  echo

  read -r -p "如需现在准备 mod 存档目录并启动游戏，请输入 1；直接回车可跳过: " activation_choice
  if [[ "${activation_choice}" != "1" ]]; then
    return 0
  fi

  ensure_modded_save_directories

  if launch_game_for_mod_activation "${resolved_game_root}"; then
    echo "游戏已启动。"
    echo "如果出现提示，请选择【Load Mods】，等待游戏重启进入 mod 模式，并至少进入一次主菜单，然后关闭游戏回到安装器。"
    read -r -p "完成上述步骤后请输入 1 继续: " ready_confirmation
    if [[ "${ready_confirmation}" != "1" ]]; then
      echo "警告：未确认 mod 模式启动成功，安装器将继续后续步骤。" >&2
    fi
  else
    echo "警告：当前平台上无法自动启动游戏。" >&2
    echo "请手动启动游戏，如有提示请选择【Load Mods】，然后继续下面的存档迁移步骤。" >&2
  fi
}

offer_save_transfer() {
  local line
  local save_root
  local profile_name
  local vanilla_save_dir
  local modded_save_dir
  local has_mappings=0
  local has_candidates=0
  local copy_choice

  echo

  while IFS= read -r line; do
    [[ -n "${line}" ]] || continue
    has_mappings=1
    break
  done < <(get_save_profile_mappings)

  if [[ ${has_mappings} -eq 0 ]]; then
    echo "警告：没有检测到原版存档配置，因此安装器无法推断 mod 模式的目标存档路径。" >&2
    return 0
  fi

  echo "原版和 mod 模式的存档进度是分开保存的。"
  echo "如果你希望在 mod 模式里立刻看到原版已经解锁的内容，就需要把原版存档复制到对应的 mod 存档目录。"
  echo "推荐做法是让安装器执行复制。如果你更想手动移动，也可以按下面打印出的同一路径自行处理。"
  show_save_migration_paths

  while IFS= read -r line; do
    [[ -n "${line}" ]] || continue
    IFS='|' read -r save_root profile_name vanilla_save_dir modded_save_dir <<EOF
${line}
EOF
    if directory_has_items "${vanilla_save_dir}" && ! directory_has_items "${modded_save_dir}"; then
      has_candidates=1
      break
    fi
  done < <(get_save_profile_mappings)

  if [[ ${has_candidates} -eq 0 ]]; then
    echo "没有找到可用于自动复制的空 mod 存档目录，或缺失的 mod 存档目录。"
    echo "如果 mod 模式里仍然看不到进度，请按上面的路径手动复制或移动存档。"
    return 0
  fi

  echo
  read -r -p "如需现在自动复制原版存档到空的 mod 存档目录，请输入 1；直接回车可跳过: " copy_choice
  if [[ "${copy_choice}" != "1" ]]; then
    echo "已跳过自动复制。你仍然可以按上面的路径手动复制或移动存档。"
    return 0
  fi

  while IFS= read -r line; do
    [[ -n "${line}" ]] || continue
    IFS='|' read -r save_root profile_name vanilla_save_dir modded_save_dir <<EOF
${line}
EOF
    if directory_has_items "${vanilla_save_dir}" && ! directory_has_items "${modded_save_dir}"; then
      mkdir -p "${modded_save_dir}"
      cp -fR "${vanilla_save_dir}/." "${modded_save_dir}/"
    fi
  done < <(get_save_profile_mappings)

  echo "已将原版存档复制到检测到的空 mod 存档目录。"
}

if [[ ! -f "${MOD_SOURCE_ROOT}/dglab_socket_spire2.dll" ]]; then
  echo "在 '${MOD_SOURCE_ROOT}' 下找不到打包后的 mod 文件。" >&2
  exit 1
fi

show_install_safety_notice
confirm_backup_done

GAME_ROOT_RESOLVED="$(resolve_game_root)"
MOD_ROOT="$(install_packaged_mod "${GAME_ROOT_RESOLVED}")"

offer_mod_environment_activation "${GAME_ROOT_RESOLVED}"
offer_save_transfer

echo "已安装到 ${MOD_ROOT}"
echo "macOS / Linux / SteamOS 支持目前仍为实验性。"
