using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DDEN.NetPerfTracker
{
    internal sealed class ItemCensus
    {
        private Item[] _snapshot = Array.Empty<Item>();
        private int _count, _cursor;
        private ObjectCounts _work;
        private readonly List<Collider2D> _colliders = new List<Collider2D>();
        internal bool Running { get; private set; }
        internal ObjectCounts Result { get; private set; }
        internal int Progress => _cursor;
        internal void Begin(float now, bool collision, List<Item> source = null)
        {
            Cancel();
            long started = Stopwatch.GetTimestamp();
            var items = source ?? Item.allItems;
            _count = items.Count;
            if (_snapshot.Length < _count) _snapshot = new Item[Math.Max(_count, Math.Max(128, _snapshot.Length * 2))];
            // Copy managed references, not FindObjectsOfType. Membership is fixed; states are observed over the scan interval.
            items.CopyTo(0, _snapshot, 0, _count);
            _work = new ObjectCounts { Started = now, CollisionChecked = collision };
            _work.WorkMs = _work.MaxSliceMs = (float)((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
            Running = true;
        }
        internal void Step(long frameStart, long budgetTicks, float now)
        {
            if (!Running) return;
            long started = Stopwatch.GetTimestamp();
            int handled = 0;
            while (_cursor < _count && handled++ < 64 && Stopwatch.GetTimestamp() - frameStart < budgetTicks)
            {
                Item item = _snapshot[_cursor]; _snapshot[_cursor++] = null;
                if (item == null) continue;
                _work.Total++;
                if (!item.gameObject.activeInHierarchy) continue;
                _work.Active++;
                if (item.transform.parent != null) continue;
                _work.Free++;
                var rb = item.rb;
                if (rb == null || !rb.simulated || rb.bodyType != RigidbodyType2D.Dynamic) continue;
                _work.Simulated++;
                if (rb.IsSleeping()) { _work.Sleeping++; continue; }
                _work.Awake++;
                if (rb.velocity.y < -2f) _work.Downward++;
                if (!_work.CollisionChecked) continue;
                _colliders.Clear(); rb.GetComponentsInChildren<Collider2D>(true, _colliders);
                bool solid = false;
                foreach (var col in _colliders)
                    if (col != null && col.enabled && !col.isTrigger && col.gameObject.activeInHierarchy && col.attachedRigidbody == rb)
                    { solid = true; break; }
                if (!solid) _work.NoSolid++;
            }
            float cost = (float)((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
            _work.WorkMs += cost; _work.MaxSliceMs = Math.Max(_work.MaxSliceMs, cost);
            if (_cursor >= _count)
            { _work.Ended = now; _work.Valid = true; Result = _work; Running = false; _count = _cursor = 0; _colliders.Clear(); }
        }
        internal void Cancel() { Array.Clear(_snapshot, _cursor, _count - _cursor); _colliders.Clear(); _count = _cursor = 0; Running = false; }
        internal void Reset() { Cancel(); Result = default; }
    }

    internal sealed class SceneCensus
    {
        private struct Cursor { internal Transform Node; internal bool Entered; internal int Child; }
        private readonly List<Cursor> _stack = new List<Cursor>();
        private readonly List<GameObject> _roots = new List<GameObject>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly List<Rigidbody2D> _bodies = new List<Rigidbody2D>();
        private readonly List<Collider2D> _colliders = new List<Collider2D>();
        private SceneCounts _work;
        internal SceneCounts Result { get; private set; }
        internal bool Running { get; private set; }
        internal int Progress => _work.Visited;
        internal void Begin(float now, Scene persistentScene)
        {
            Cancel();
            long start = Stopwatch.GetTimestamp();
            bool included = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                AddScene(scene);
                if (scene == persistentScene) included = true;
            }
            if (!included) AddScene(persistentScene);
            _work = new SceneCounts { Started = now };
            _work.WorkMs = _work.MaxSliceMs = (float)((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            Running = true;
        }
        private void AddScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            _roots.Clear();
            if (_roots.Capacity < scene.rootCount + 1) _roots.Capacity = scene.rootCount + 1;
            scene.GetRootGameObjects(_roots);
            foreach (var root in _roots) if (root != null) _stack.Add(new Cursor { Node = root.transform });
            _roots.Clear();
        }
        internal void BeginRoots(float now, IEnumerable<GameObject> roots)
        {
            Cancel(); _work = new SceneCounts { Started = now };
            foreach (var root in roots) if (root != null) _stack.Add(new Cursor { Node = root.transform });
            Running = true;
        }
        internal void Step(long frameStart, long budgetTicks, float now)
        {
            if (!Running) return;
            long start = Stopwatch.GetTimestamp();
            int operations = 0;
            while (_stack.Count > 0 && operations++ < 128 && Stopwatch.GetTimestamp() - frameStart < budgetTicks)
            {
                int top = _stack.Count - 1;
                Cursor cursor = _stack[top];
                if (cursor.Node == null || !cursor.Node.gameObject.activeInHierarchy) { _stack.RemoveAt(top); continue; }
                if (!cursor.Entered)
                {
                    if (!_seen.Add(cursor.Node.GetInstanceID())) { _stack.RemoveAt(top); continue; }
                    cursor.Entered = true; _stack[top] = cursor; _work.Visited++;
                    _bodies.Clear(); cursor.Node.GetComponents<Rigidbody2D>(_bodies);
                    foreach (var rb in _bodies)
                    {
                        _work.Rigid++;
                        if (!rb.simulated) continue;
                        if (rb.bodyType == RigidbodyType2D.Kinematic) { _work.Kinematic++; continue; }
                        if (rb.bodyType != RigidbodyType2D.Dynamic) continue;
                        _work.Dynamic++;
                        if (rb.IsSleeping()) _work.Sleeping++; else _work.Awake++;
                    }
                    _colliders.Clear(); cursor.Node.GetComponents<Collider2D>(_colliders);
                    foreach (var col in _colliders) if (col.enabled) _work.Colliders++;
                }
                else if (cursor.Child < cursor.Node.childCount)
                {
                    Transform child = cursor.Node.GetChild(cursor.Child++); _stack[top] = cursor;
                    _stack.Add(new Cursor { Node = child });
                }
                else _stack.RemoveAt(top);
            }
            float cost = (float)((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            _work.WorkMs += cost; _work.MaxSliceMs = Math.Max(_work.MaxSliceMs, cost);
            if (_stack.Count == 0) { _work.Valid = true; _work.Ended = now; Result = _work; Running = false; _seen.Clear(); _bodies.Clear(); _colliders.Clear(); }
        }
        internal void Cancel() { _stack.Clear(); _seen.Clear(); _roots.Clear(); _bodies.Clear(); _colliders.Clear(); Running = false; }
        internal void Reset() { Cancel(); Result = default; }
    }
}
