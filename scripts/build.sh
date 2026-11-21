#!/usr/bin/env bash
# DDEN 系列一键构建 + 部署到 BepInEx/plugins/DDEN
#
# 用法：
#   ./scripts/build.sh                    # Debug 构建，默认部署五个DDEN产物及ET加热笼修复
#   ./scripts/build.sh Release            # Release 构建，同上
#   ./scripts/build.sh Debug --no-saveeditor     # 这次不部署存档编辑器（默认会部署）
#
# 注意：本脚本**不再生成插件备份**（2026-11 按用户要求移除）。需要留旧版就在部署前自己复制一份。
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GAME_DIR="${GAME_DIR:-L:/SteamLibrary/steamapps/common/Casualties Unknown}"
PLUGINS="$GAME_DIR/BepInEx/plugins"
DDEN_DIR="$PLUGINS/DDEN"

CONFIG="Debug"
WITH_SAVEEDITOR=1
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
OWNED=("DDEN.Shared.dll" "DDExcNet-CUmodM.dll" "DDEN.MPfix.dll" "DDEN.NetPerfTracker.dll" "DDEN.SaveEditor.dll" "ET-HeatingCageFix.dll")
LEGACY_IN_DDEN=("DDEN.MPUnchippedSyncFix.dll" "CageFix.dll")

is_owned() {
  local name="$1" o
  for o in "${OWNED[@]}"; do [ "$name" = "$o" ] && return 0; done
  return 1
}

cp -v "$ROOT/src/DDEN.Shared/bin/$CONFIG/DDEN.Shared.dll"         "$DDEN_DIR/"
cp -v "$ROOT/src/DDExcNet.CUmodM/bin/$CONFIG/DDExcNet-CUmodM.dll" "$DDEN_DIR/"
cp -v "$ROOT/src/DDEN.MPfix/bin/$CONFIG/DDEN.MPfix.dll"           "$DDEN_DIR/"
cp -v "$ROOT/src/外部mod/ET-HeatingCageFix/bin/$CONFIG/ET-HeatingCageFix.dll"          "$DDEN_DIR/"
# 性能分析器：完整构建和发布包均包含，可按需不安装
cp -v "$ROOT/src/DDEN.NetPerfTracker/bin/$CONFIG/DDEN.NetPerfTracker.dll"             "$DDEN_DIR/"

if [ "$WITH_SAVEEDITOR" = "1" ]; then
  cp -v "$ROOT/src/DDEN.SaveEditor/bin/$CONFIG/DDEN.SaveEditor.dll" "$DDEN_DIR/"
else
  # 显式指定不带编辑器时撤下旧文件，只处理这个自有准确名称
  if [ -e "$DDEN_DIR/DDEN.SaveEditor.dll" ] && is_owned "DDEN.SaveEditor.dll"; then
    if rm -f "$DDEN_DIR/DDEN.SaveEditor.dll" 2>/dev/null; then
      echo "== 已按 --no-saveeditor 撤下 DDEN.SaveEditor.dll"
    else
      echo "== ⚠ 撤下 DDEN.SaveEditor.dll 失败（游戏可能在运行），请关掉游戏后重跑本脚本"
    fi
  else
    echo "== 存档编辑器不在部署目录（符合预期）"
  fi
fi

# 只处理DDEN目录内已知旧名；其它位置的历史CUmod文件由兼容性页提示后人工核对。
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

echo "== 完成。启动游戏后在 BepInEx/LogOutput.log 核对本次选用组件的加载情况。"
