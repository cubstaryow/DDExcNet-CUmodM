using System;
using System.Collections.Generic;
using DDEN.Shared;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.LoongNova
{
    internal static class HealingFields
    {
        private static readonly HealingFieldState State = new HealingFieldState();
        private static readonly Dictionary<string, FieldVisual> Visuals = new Dictionary<string, FieldVisual>();
        private static object _blocks, _connection;
        private sealed class FieldVisual
        {
            internal GameObject Root;
            internal Material Material;
            internal Mesh Mesh;
            internal LineRenderer Ring;
            internal Color[] Colors;
        }
        private static void Context()
        {
            var world = WorldGeneration.world;
            object blocks = world == null ? null : Reflect.GetInstance(world, "worldBlocks");
            object connection = NetworkAccess.Connection;
            if (ReferenceEquals(blocks, _blocks) && ReferenceEquals(connection, _connection)) return;
            Clear(); _blocks = blocks; _connection = connection;
        }
        internal static void Create(Vector2 point)
        {
            if (Mp.IsRunning && (!Mp.IsServer || !Mp.CoreReady)) throw new InvalidOperationException("Healing field spawn requires authority");
            Context();
            string id = Guid.NewGuid().ToString("N");
            State.Add(id, point.x, point.y, HealingFieldState.Duration);
            EnsureVisuals();
            if (Mp.IsRunning) Mp.Broadcast(WeaponService.VisualChannel, new JObject
            {
                ["healingFields"] = true, ["fields"] = new JArray(Pack(State.Active[State.Active.Count - 1]))
            });
        }
        private static JObject Pack(HealingFieldState.Field field) => new JObject
        { ["id"] = field.Id, ["x"] = field.X, ["y"] = field.Y, ["remaining"] = field.Remaining };
        internal static JObject Capture()
        {
            Context(); var fields = new JArray();
            foreach (var field in State.Active) fields.Add(Pack(field));
            return new JObject { ["healingFields"] = true, ["fields"] = fields };
        }
        internal static void Receive(JToken data, bool snapshot = false)
        {
            if (!Mp.IsRunning || Mp.IsServer || !(data?["fields"] is JArray fields)) return;
            Context();
            if (snapshot) Clear();
            foreach (var field in fields)
            {
                if (!(field is JObject) || !Guid.TryParseExact(field.Value<string>("id"), "N", out _)) continue;
                State.Add(field.Value<string>("id"), field.Value<float>("x"), field.Value<float>("y"), field.Value<float>("remaining"));
            }
            EnsureVisuals();
        }
        internal static float RemainingAt(Vector2 point) { Context(); return State.RemainingAt(point.x, point.y); }
        internal static void Tick()
        {
            Context(); State.Advance(Time.deltaTime); EnsureVisuals();
        }
        private static void EnsureVisuals()
        {
            if (State.Active.Count == 0 && Visuals.Count == 0) return;
            var removed = new List<string>();
            foreach (var pair in Visuals)
                if (!State.Active.Exists(field => field.Id == pair.Key)) { Destroy(pair.Value); removed.Add(pair.Key); }
            foreach (string id in removed) Visuals.Remove(id);
            foreach (var field in State.Active)
            {
                if (!Visuals.TryGetValue(field.Id, out var visual))
                {
                    visual = MakeVisual(field); if (visual == null) continue;
                    Visuals.Add(field.Id, visual);
                }
                float fade = Mathf.Min(1, field.Remaining);
                float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 2);
                visual.Ring.startColor = visual.Ring.endColor = new Color(0.35f, 1, 0.1f, 0.65f * fade * pulse);
                visual.Colors[0] = new Color(0.35f, 1, 0.1f, 0.14f * fade * pulse);
                for (int i = 1; i < visual.Colors.Length; i++) visual.Colors[i] = new Color(0.35f, 1, 0.1f, 0.03f * fade);
                visual.Mesh.colors = visual.Colors;
            }
        }
        private static FieldVisual MakeVisual(HealingFieldState.Field field)
        {
            var shader = Shader.Find("Sprites/Default"); if (shader == null) return null;
            const int edges = 64;
            var visual = new FieldVisual { Root = new GameObject("LoongNova healing field"), Material = new Material(shader), Mesh = new Mesh(), Colors = new Color[edges + 2] };
            visual.Root.transform.position = new Vector3(field.X, field.Y, 0);
            var vertices = new Vector3[edges + 2]; var triangles = new int[edges * 3];
            var outline = new Vector3[edges + 1];
            for (int i = 0; i <= edges; i++)
            {
                float angle = i * Mathf.PI * 2 / edges;
                vertices[i + 1] = outline[i] = new Vector3(Mathf.Cos(angle) * HealingFieldState.Radius, Mathf.Sin(angle) * HealingFieldState.Radius, 0);
                if (i < edges) { triangles[i * 3] = 0; triangles[i * 3 + 1] = i + 1; triangles[i * 3 + 2] = i + 2; }
            }
            visual.Mesh.vertices = vertices; visual.Mesh.triangles = triangles;
            visual.Root.AddComponent<MeshFilter>().sharedMesh = visual.Mesh;
            var renderer = visual.Root.AddComponent<MeshRenderer>(); renderer.sharedMaterial = visual.Material; renderer.sortingOrder = 49;
            visual.Ring = visual.Root.AddComponent<LineRenderer>();
            visual.Ring.sharedMaterial = visual.Material; visual.Ring.useWorldSpace = false;
            visual.Ring.startWidth = visual.Ring.endWidth = 0.08f; visual.Ring.positionCount = outline.Length;
            visual.Ring.sortingOrder = 50; visual.Ring.SetPositions(outline);
            return visual;
        }
        private static void Destroy(FieldVisual visual)
        {
            if (visual.Root != null) UnityEngine.Object.Destroy(visual.Root);
            if (visual.Mesh != null) UnityEngine.Object.Destroy(visual.Mesh);
            if (visual.Material != null) UnityEngine.Object.Destroy(visual.Material);
        }
        internal static void Clear()
        {
            foreach (var visual in Visuals.Values) Destroy(visual);
            Visuals.Clear(); State.Active.Clear();
        }
    }
}
