using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using DDEN.Shared;

namespace DDEN.NetPerfTracker
{
    /// <summary>Explicit startup self-test only, before entering a world. All temporary objects die before Start/Update.</summary>
    internal static class CensusProbe
    {
        internal static bool Run()
        {
            if (Game.InWorld || Mp.IsRunning) throw new InvalidOperationException("清点自检仅允许在菜单运行");
            var roots = new List<GameObject>();
            var items = new List<Item>();
            var census = new ItemCensus(); var scene = new SceneCensus();
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    var go = new GameObject("DDEN census probe " + i); roots.Add(go);
                    go.transform.position = new Vector3(100000 + i * 10, 100000, 0);
                    var rb = go.AddComponent<Rigidbody2D>(); rb.gravityScale = 0;
                    var item = go.AddComponent<Item>(); item.rb = rb; item.enabled = false; items.Add(item);
                }
                roots[0].AddComponent<BoxCollider2D>();
                roots[2].transform.SetParent(roots[0].transform, true);
                roots[3].SetActive(false);
                items[0].rb.Sleep(); items[1].rb.velocity = new Vector2(0, -3); items[1].rb.WakeUp(); items[2].rb.WakeUp();
                items.Add(null); // A destroyed/null registration must not become an object count.
                float now = Time.realtimeSinceStartup;
                census.Begin(now, true, items);
                long start = Stopwatch.GetTimestamp();
                census.Step(start, 0, now);
                if (!census.Running || census.Result.Valid) throw new Exception("零预算清点没有暂停");
                census.Step(Stopwatch.GetTimestamp(), Stopwatch.Frequency, now);
                var o = census.Result;
                if (!o.Valid || o.Total != 4 || o.Active != 3 || o.Free != 2 || o.Simulated != 2 ||
                    o.Awake != 1 || o.Sleeping != 1 || o.Downward != 1 || o.NoSolid != 1)
                    throw new Exception($"物品分类错误 total={o.Total} active={o.Active} free={o.Free} dynamic={o.Simulated} awake={o.Awake} sleeping={o.Sleeping} down={o.Downward} noSolid={o.NoSolid}");
                scene.BeginRoots(now, roots); // Overlapping roots must not double-count the child.
                while (scene.Running) scene.Step(Stopwatch.GetTimestamp(), Stopwatch.Frequency, now);
                var s = scene.Result;
                if (!s.Valid || s.Visited != 3 || s.Dynamic != 3 || s.Awake != 2 || s.Sleeping != 1 || s.Colliders != 1)
                    throw new Exception($"场景分类错误 visited={s.Visited} dynamic={s.Dynamic} awake={s.Awake} sleeping={s.Sleeping} colliders={s.Colliders}");
                NetPerfTrackerPlugin.Log.Info("[PERFTEST] 分类 PASS：排除停用/有父级物品；清醒/休眠、向下运动、无实体碰撞、零预算让步、重叠场景根去重。");
                return true;
            }
            finally
            {
                census.Reset(); scene.Reset();
                foreach (var go in roots) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
