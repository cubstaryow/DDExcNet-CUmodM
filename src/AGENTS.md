# src/AGENTS.md —— 写代码时的局部约定

继承根目录 [../AGENTS.md](../AGENTS.md)（环境事实、构建坑、验证流程都在那里，先读它）。

## 结构

```
src/DDEN.Shared/          公共层（被三个插件引用）
  Hub.cs        IDdenModule 契约 + DdenHub 注册中心 + ConfigFile 摊平扩展
  Compat.cs     兼容性总控：插件清单/旧 dll/补丁栈/抢键/QoL autosave/指令撞名
  DdenUI.cs     统一皮肤与控件：配色、精灵绘制、条、配置行渲染、独立小窗兜底
  Reflect.cs    反射工具（找不到类型返回 null，不抛）
  Log.cs        日志：LogChannel（插件各一份）+ 静态 Log（共享代码落点）
  Mp.cs         CUCoreLib 联机桥封装 + Together 状态读写
  SaveCodec.cs  *.sv 编解码 + 原子写 + meta 同步 + QoL 存档位桥
  Game.cs       InWorld / LocalBody / Alert / Console + LiveFields 运行时写回
src/DDExcNet.CUmodM/     管理器（唯一画窗口的地方）
src/DDEN.MPfix/          多人修复合集（无芯片同步 + 联机合成/物品生成兜底 + 网络登记）
src/DDEN.NetPerfTracker/  性能跟踪器（调试用：零补丁，管理器里画帧数/CPU/物理/对象曲线 + 写 perf 日志）
src/DDEN.SaveEditor/     存档编辑器（零 patch：文件 + 反射 + 图标）
```

## 新增一个 DDEN 模组

```bash
cp -r src/DDEN.SaveEditor src/DDEN.MyMod
# 改 csproj: AssemblyName / RootNamespace / Version
# 改插件: [BepInPlugin("com.dden.mymod", ...)] + Mission: 实现 IDdenModule + DdenHub.Register(this)
dotnet sln CUmod.slnx add src/DDEN.MyMod/DDEN.MyMod.csproj
./scripts/build.sh
```

`csproj` 模板只需 `ProjectReference → DDEN.Shared` 与 `DeployPlugin` 目标（输出目录由 `Directory.Build.props` 的 `DdenPluginsDir` 决定）。

## 硬性规则

1. **不硬引用 Together / QoL / 别的模块 dll**。跨模块调用用 `IDdenModule` 或 `Reflect`；管理器对模块的可选方法用反射（见 `InvokeModuleMethod`）。
2. **日志用通道**：`internal static LogChannel Log = DDEN.Shared.Log.For(Logger, "Tag", verbose)`；
   共享层的静态 `Log` 只在管理器 `Awake` 里 `Log.Attach` 一次，否则日志会掉进 Player.log。
3. **UI 只画在管理器里**：模块实现 `DrawPage/DrawSettings`，不要自己开 `GUI.Window`（管理器缺席时的兜底小窗除外）。
   控件优先用 `DdenUI.*`；精灵一律 `DdenUI.DrawSprite`（按 `textureRect` 画，别直接画整张图集）。
4. **所有 Unity 生命周期与绘制代码包 try/catch**，异常走 `Log.Exception`。
5. 面向玩家文案中文；日志前缀用通道标签，便于 grep。
6. 不提交 `bin/ obj/ decomp/`；不 `git push`。

## 关键不变量

- **芯片同步**：主机真值只能从 `Mp.HostUnchipped()` 取；客机应用必须三件一起做
  （`world.SetUnchipped` + `MirrorTogetherLock` + `SyncLocalPlayer`），漏一个就会出现“HUD 变了但名牌/语音没变”。
  Harmony postfix 已声明 `[HarmonyAfter("CasualtiesMP","net.cucorelib")]`。
- **存档编辑器**：`FieldRow` 是 JToken 视图，改它就是改文档，**不要重建文档**；
  写盘只走 `SaveCodec.Write` + `SyncMeta`；`cheatedSave` 只有“进度/成就”页的手动开关能改；
  删除物品时 `items` 与 `itemComponents` 必须同步删；
  物品按 `slot|wearSlot` 归组（原版存档把父物品写在子物品前），子物品显示在容器头下面。
- **兼容层**：任何“会不会和别的模组打架”的判断都加到 `Compat`，不要在模块里各写一份。
