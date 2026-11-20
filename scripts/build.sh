#!/usr/bin/env bash
# DDEN 系列一键构建 + 部署到 BepInEx/plugins/DDEN
#
# 用法：
#   ./scripts/build.sh                    # Debug 构建，部署 Shared / CUmodM / MPfix（**不含存档编辑器**）
#   ./scripts/build.sh Release            # Release 构建，同上
#   ./scripts/build.sh Debug --with-saveeditor   # 临时把 DDEN.SaveEditor 也部署进去（开发用）
#
# 每次部署后会在仓库根目录生成插件备份：
#   backup/plugins_<时间戳>/*.dll + manifest.txt
#   backup/latest/            （最近一次部署的副本）
#   backup/DDEN-plugins-<时间戳>.zip（如果 PowerShell 可用）
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GAME_DIR="${GAME_DIR:-L:/SteamLibrary/steamapps/common/Casualties Unknown}"
PLUGINS="$GAME_DIR/BepInEx/plugins"
DDEN_DIR="$PLUGINS/DDEN"

CONFIG="Debug"
WITH_SAVEEDITOR=0
for arg in "$@"; do
  case "$arg" in
    --with-saveeditor) WITH_SAVEEDITOR=1 ;;
    --no-saveeditor)   WITH_SAVEEDITOR=0 ;;
    Debug|Release)     CONFIG="$arg" ;;
    *) echo "== 忽略未知参数: $arg" ;;
  esac
done

echo "== 仓库: $ROOT"
echo "== 游戏: $GAME_DIR"
echo "== 配置: $CONFIG   存档编辑器: $([ "$WITH_SAVEEDITOR" = "1" ] && echo 部署 || echo 不部署)"

dotnet build "$ROOT/CUmod.slnx" -c "$CONFIG" -v m

echo "== 部署到 $DDEN_DIR"
mkdir -p "$DDEN_DIR"

# 我们的产物清单：**只有这些文件由本脚本负责写/删**。
# DDEN/ 目录里可能还有别人做的修复 mod，绝不能碰。
OWNED=("DDEN.Shared.dll" "DDExcNet-CUmodM.dll" "DDEN.MPfix.dll" "DDEN.SaveEditor.dll")
LEGACY_IN_DDEN=("DDEN.MPUnchippedSyncFix.dll")

is_owned() {
  local name="$1" o
  for o in "${OWNED[@]}"; do [ "$name" = "$o" ] && return 0; done
  return 1
}

cp -v "$ROOT/src/DDEN.Shared/bin/$CONFIG/DDEN.Shared.dll"         "$DDEN_DIR/"
cp -v "$ROOT/src/DDExcNet.CUmodM/bin/$CONFIG/DDExcNet-CUmodM.dll" "$DDEN_DIR/"
cp -v "$ROOT/src/DDEN.MPfix/bin/$CONFIG/DDEN.MPfix.dll"           "$DDEN_DIR/"

if [ "$WITH_SAVEEDITOR" = "1" ]; then
  cp -v "$ROOT/src/DDEN.SaveEditor/bin/$CONFIG/DDEN.SaveEditor.dll" "$DDEN_DIR/"
else
  # 正常游戏不带存档编辑器：已部署过就撤掉（只撤我们自己的这个名字）
  if [ -e "$DDEN_DIR/DDEN.SaveEditor.dll" ] && is_owned "DDEN.SaveEditor.dll"; then
    if rm -f "$DDEN_DIR/DDEN.SaveEditor.dll" 2>/dev/null; then
      echo "== 已撤下 DDEN.SaveEditor.dll（正常游戏不带它；需要时加 --with-saveeditor）"
    else
      echo "== ⚠ 撤下 DDEN.SaveEditor.dll 失败（游戏可能在运行），请关掉游戏后重跑本脚本"
    fi
  else
    echo "== 存档编辑器不在部署目录（符合预期）"
  fi
fi

# 清理我们自己的历史命名残留（只按已知文件名删，不做通配删除）
for legacy in "$PLUGINS"/CUmod.*.dll; do
  [ -e "$legacy" ] || continue
  echo "== 删除旧版本残留: $legacy"
  rm -f "$legacy"
done
for legacy in "${LEGACY_IN_DDEN[@]}"; do
  [ -e "$DDEN_DIR/$legacy" ] || continue
  echo "== 删除旧版本残留: $DDEN_DIR/$legacy"
  rm -f "$DDEN_DIR/$legacy"
done

# 报告并保护其它模组的文件（不删、不覆盖）
shopt -s nullglob
kept=()
for f in "$DDEN_DIR"/*; do
  [ -e "$f" ] || continue
  name="$(basename "$f")"
  is_owned "$name" && continue
  kept+=("$name")
done
shopt -u nullglob
if [ ${#kept[@]} -gt 0 ]; then
  echo "== 保持不动（其它模组 / 非本脚本产物）: ${kept[*]}"
fi

# ===== 仓库根目录的插件备份 =====
TS="$(date +%Y%m%d-%H%M%S)"
BACKUP_ROOT="$ROOT/backup"
BK="$BACKUP_ROOT/plugins_$TS"
mkdir -p "$BK"

shopt -s nullglob
deployed=( "$DDEN_DIR"/*.dll )
shopt -u nullglob
if [ ${#deployed[@]} -gt 0 ]; then
  cp -v "${deployed[@]}" "$BK/"
fi

{
  echo "time     : $(date '+%Y-%m-%d %H:%M:%S')"
  echo "config   : $CONFIG"
  echo "saveeditor: $([ "$WITH_SAVEEDITOR" = "1" ] && echo deployed || echo skipped)"
  echo "source   : $DDEN_DIR"
  echo "files    :"
  for f in "$BK"/*.dll; do
    [ -e "$f" ] || continue
    printf '  %-32s %s bytes  %s\n' "$(basename "$f")" "$(stat -c%s "$f")" "$(md5sum "$f" | cut -d' ' -f1)"
  done
} > "$BK/manifest.txt"

rm -rf "$BACKUP_ROOT/latest"
cp -r "$BK" "$BACKUP_ROOT/latest"

BK_WIN="$(cygpath -m "$BK" 2>/dev/null || echo "$BK")"
ZIP_WIN="$(cygpath -m "$BACKUP_ROOT/DDEN-plugins-$TS.zip" 2>/dev/null || echo "$BACKUP_ROOT/DDEN-plugins-$TS.zip")"
powershell.exe -NoProfile -Command "Compress-Archive -Path '$BK_WIN/*' -DestinationPath '$ZIP_WIN' -Force" >/dev/null 2>&1 \
  && echo "== 备份: backup/plugins_$TS  +  backup/DDEN-plugins-$TS.zip" \
  || echo "== 备份: backup/plugins_$TS（zip 跳过）"

echo "== 完成。启动游戏后看 BepInEx/LogOutput.log 里的 [CUmodM] / [MPfix] 行（存档编辑器仅 --with-saveeditor 时才有 [SaveEditor]）。"
