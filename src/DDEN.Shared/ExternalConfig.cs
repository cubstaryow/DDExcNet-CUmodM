using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace DDEN.Shared
{
    /// <summary>
    /// 「把配置交给管理器」的外部接入点。
    ///
    /// 别的模组**不需要引用 DDEN.Shared.dll**，只要在自己的插件程序集里暴露下面任意一组成员，
    /// 管理器启动后就会把它的配置收进「全部配置」页（按插件分组渲染、可直接改值、写回 cfg 文件）：
    ///
    /// <code>
    /// // 方式 A：交出一个 ConfigFile 或 IEnumerable&lt;ConfigEntryBase&gt;
    /// public static ConfigFile DdenConfig() =&gt; Instance.Config;
    ///
    /// // 方式 B：可选——显示名
    /// public static string DdenTitle =&gt; "我的模组";
    ///
    /// // 方式 C：可选——自定义一整页（管理器会给出内容区矩形）
    /// public static void DdenDraw(Rect area) { ... }
    ///
    /// // 方式 D：可选——总览里的一行状态
    /// public static string DdenSummary() =&gt; "就绪";
    /// </code>
    ///
    /// 约定：成员必须是 public static；方法不能有参数（DdenDraw 除外，它接收 Rect）；
    /// 返回值可以是 ConfigFile、IEnumerable&lt;ConfigEntryBase&gt;、或 ConfigEntryBase 数组。
    /// 任何异常都被吞掉并记 Debug，绝不影响对方模组。
    /// </summary>
    public static class DdenExternal
    {
        /// <summary>一个外部模组交出来的配置块。</summary>
        public sealed class Block : IDdenConfigPolicy, IDdenConfigAvailability
        {
            public string Owner;      // 插件名（BepInEx 元数据）
            public string Title;      // 显示名（DdenTitle 或 Owner）
            public string Summary;    // 可选，一行状态
            public Type Type;         // 提供成员的静态类型
            public IEnumerable<ConfigEntryBase> Entries;
            public Action<Rect> Draw; // 可选，自定义页面
            private bool _summaryRead;
            private int _summaryAt;
            public string CurrentSummary
            {
                get
                {
                    int now = Environment.TickCount;
                    if (!_summaryRead || unchecked(now - _summaryAt) >= 1000)
                    { _summaryRead = true; _summaryAt = now; Summary = ReadSummary(Type) ?? Summary; }
                    return Summary;
                }
            }
            internal Func<ConfigEntryBase, string> Unavailable;
            public bool CanEditConfig(ConfigEntryBase entry, bool normallyInteractive) => normallyInteractive && string.IsNullOrEmpty(ConfigUnavailableReason(entry));
            public object ConfigDisplayValue(ConfigEntryBase entry) => entry.BoxedValue;
            public string ConfigNotice => null;
            public string ConfigUnavailableReason(ConfigEntryBase entry)
            {
                try { return Unavailable?.Invoke(entry); }
                catch (Exception ex) { Log.Debug("外部配置可用性读取失败：" + ex.Message); return "此项暂时不可用"; }
            }
        }

        private static readonly List<Block> _blocks = new List<Block>();

        public static IReadOnlyList<Block> Blocks => _blocks;

        /// <summary>扫描所有已加载插件（重复调用安全，会重建列表）。</summary>
        public static void Scan()
        {
            _blocks.Clear();

            IEnumerable<KeyValuePair<string, PluginInfo>> plugins;
            try { plugins = BepInEx.Bootstrap.Chainloader.PluginInfos; }
            catch (Exception ex) { Log.Debug("拿不到 PluginInfos：" + ex.Message); return; }

            foreach (KeyValuePair<string, PluginInfo> kv in plugins)
            {
                try
                {
                    Assembly asm = kv.Value?.Instance?.GetType().Assembly;
                    if (asm == null) continue;
                    if (asm == typeof(DdenExternal).Assembly) continue;      // 跳过我们自己的 dll

                    Block block = TryBuild(asm, kv.Value.Metadata?.Name ?? kv.Key);
                    if (block != null && block.Entries != null) _blocks.Add(block);
                }
                catch (Exception ex)
                {
                    Log.Debug("扫描外部配置失败：" + ex.Message);
                }
            }

            if (_blocks.Count > 0)
            {
                Log.Info("外部配置接入：" + string.Join("、", _blocks.Select(b => b.Title)));
            }
        }

        private static Block TryBuild(Assembly asm, string owner)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
            catch { return null; }

            foreach (Type type in types)
            {
                if (type == null || !type.IsPublic) continue;

                Block block = new Block { Owner = owner, Title = owner, Type = type };

                block.Entries = ReadConfig(type);
                if (block.Entries == null) continue;    // 没交配置就跳过

                block.Title = ReadString(type) ?? owner;
                block.Summary = ReadSummary(type);
                block.Draw = ReadDraw(type);
                MethodInfo reason = type.GetMethod("DdenUnavailable", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ConfigEntryBase) }, null);
                if (reason != null && reason.ReturnType == typeof(string))
                    block.Unavailable = (Func<ConfigEntryBase, string>)Delegate.CreateDelegate(typeof(Func<ConfigEntryBase, string>), reason);
                return block;
            }
            return null;
        }

        private static readonly string[] ConfigNames = { "DdenConfig", "DdenConfigEntries", "DdenConfigFile" };

        private static IEnumerable<ConfigEntryBase> ReadConfig(Type type)
        {
            foreach (string name in ConfigNames)
            {
                object value = InvokeStaticMember(type, name);
                if (value == null) continue;

                if (value is ConfigFile cf) return ConfigExt.Entries(cf);
                if (value is ConfigEntryBase single) return new[] { single };
                if (value is IEnumerable<ConfigEntryBase> typed) return typed;
                if (value is IEnumerable en)
                {
                    var list = new List<ConfigEntryBase>();
                    foreach (object o in en)
                    {
                        if (o is ConfigEntryBase e) list.Add(e);
                        else break;
                    }
                    if (list.Count > 0) return list;
                }
            }
            return null;
        }

        private static string ReadString(Type type)
        {
            object v = InvokeStaticMember(type, "DdenTitle");
            return v as string;
        }

        private static string ReadSummary(Type type)
        {
            object v = InvokeStaticMember(type, "DdenSummary");
            return v as string;
        }

        private static Action<Rect> ReadDraw(Type type)
        {
            try
            {
                MethodInfo m = type.GetMethod("DdenDraw", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(Rect) }, null);
                if (m == null) return null;
                return (Action<Rect>)Delegate.CreateDelegate(typeof(Action<Rect>), m);
            }
            catch { return null; }
        }

        /// <summary>读取 public static 属性或字段或（无参）方法的返回值，全部失败返回 null。</summary>
        private static object InvokeStaticMember(Type type, string name)
        {
            try
            {
                PropertyInfo p = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
                if (p != null) return p.GetValue(null);

                FieldInfo f = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
                if (f != null) return f.GetValue(null);

                MethodInfo m = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (m != null) return m.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Log.Debug($"{type.Name}.{name} 读取失败：{ex.Message}");
            }
            return null;
        }
    }
}
