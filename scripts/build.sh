#!/usr/bin/env bash
# DDEN 系列一键构建 + 部署到 BepInEx/plugins/DDEN
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GAME_DIR="${GAME_DIR:-L:/SteamLibrary/steamapps/common/Casualties Unknown}"
PLUGINS="$GAME_DIR/BepInEx/plugins"
DDEN_DIR="$PLUGINS/DDEN"
CONFIG="${1:-Debug}"

echo "== 仓库: $ROOT"
echo "== 游戏: $GAME_DIR"

dotnet build "$ROOT/CUmod.slnx" -c "$CONFIG" -v m

echo "== 部署到 $DDEN_DIR"
mkdir -p "$DDEN_DIR"
cp -v "$ROOT/src/DDEN.Shared/bin/$CONFIG/DDEN.Shared.dll"                    "$DDEN_DIR/"
cp -v "$ROOT/src/DDExcNet.CUmodM/bin/$CONFIG/DDExcNet-CUmodM.dll"            "$DDEN_DIR/"
cp -v "$ROOT/src/DDEN.MPUnchippedSyncFix/bin/$CONFIG/DDEN.MPUnchippedSyncFix.dll" "$DDEN_DIR/"
cp -v "$ROOT/src/DDEN.SaveEditor/bin/$CONFIG/DDEN.SaveEditor.dll"            "$DDEN_DIR/"

# 清理旧命名残留（否则会和新 dll 抢同一个功能 / 重复界面）
for legacy in "$PLUGINS"/CUmod.*.dll; do
  [ -e "$legacy" ] || continue
  echo "== 删除旧版本残留: $legacy"
  rm -f "$legacy"
done

echo "== 完成。启动游戏后看 BepInEx/LogOutput.log 里的 [CUmodM] / [MPUnchipped] / [SaveEditor] 行。"
