using System;
using CUCoreLib.Helpers;
using DDEN.Shared;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DDEN.LoongNova
{
    internal static class CannonService
    {
        // Caller has already authenticated the holder and server role on the normal action channel.
        internal static JObject Execute(string operation, DragonGun gun, Item round, Body body, Vector2? direction)
        {
            if (operation == "read") return Reply(gun, true);
            // A magazine command must never remove an occupied chamber or cancel its charge.
            if (operation == "eject" && gun.Ammo.Chambered) return Reply(gun, false);
            if (operation == "cancel") { if (!ForceCommitted(gun)) gun.Charge.Cancel(); return Reply(gun, true); }
            if (operation == "load" || operation == "eject" || operation == "rack")
            {
                if (ForceCommitted(gun)) return Reply(gun, true);
                gun.Charge.Cancel();
            }
            if (operation == "rack")
            {
                if (!gun.Ammo.Loaded) return Reply(gun, false);
                Item ejected = null;
                if (gun.Ammo.Chambered && !CreateReturnedRound(gun, out ejected)) return Reply(gun, false);
                if (!gun.Ammo.Rack()) return Reply(gun, false);
                if (ejected != null && ejected.GetComponent<FreshItemDrop>() == null) ejected.gameObject.AddComponent<FreshItemDrop>();
                Sound.Play("gunrack", gun.transform.position);
                gun.Known = true; gun.Normalize(); return Reply(gun, true);
            }
            if (operation == "load")
            {
                if (!gun.Ammo.CanLoad || gun.State.Loaded || round == null || !CannonAmmoState.IsRound(round.id)
                    || !NetworkAccess.CanAccess(body, round) || float.IsNaN(round.condition) || float.IsInfinity(round.condition)
                    || round.condition <= 0 || round.condition > 1) return Reply(gun, false);
                if (!gun.Ammo.Load(round.id, round.condition, round.favourited)) return Reply(gun, false);
                // Native loading consumes the item in place, including container contents.
                UnityEngine.Object.Destroy(round.gameObject);
                Sound.Play("gunloadmag", gun.transform.position);
            }
            else if (operation == "eject")
            {
                if (!gun.Ammo.CanUnload && !gun.State.Loaded) return Reply(gun, false);
                if (!CreateReturnedRound(gun, out var unloaded)) return Reply(gun, false);
                if (gun.Ammo.Loaded) gun.Ammo.Clear(); else gun.State.Set(false, 0, false);
                gun.Normalize();
                try { body.AutoPickUpItem(unloaded); } catch (Exception error) { LoongNovaPlugin.Log.Warn("Cannon unload: " + error.Message); }
                Sound.Play("gununloadmag", gun.transform.position);
            }
            else if (operation == "charge")
            {
                if (!CanFire(gun, body)) return Reply(gun, false);
                return Reply(gun, gun.Charge.Begin(body, Time.time));
            }
            else if (operation == "release")
            {
                if (!CanFire(gun, body)) return Reply(gun, false);
                return Fire(gun, body, direction);
            }
            else return Reply(gun, false); // Old cell, stock shoot and unrecognized action paths cannot fire this cannon.
            gun.Known = true; gun.Normalize(); return Reply(gun, true);
        }
        internal static bool ForceCommitted(DragonGun gun)
        {
            if (Mp.IsRunning && (!Mp.IsServer || !Mp.CoreReady) || gun == null || !gun.Charge.Committed(Time.time)) return false;
            var owner = gun.Charge.Owner as Body;
            if (owner == null || !gun.Ammo.Ready) return false;
            return Fire(gun, owner, null).Value<bool?>("ok") == true;
        }
        private static JObject Fire(DragonGun gun, Body body, Vector2? direction)
        {
            Vector2 aim = direction ?? (Vector2)(gun.Gun.transform.right * (body.isRight ? 1 : -1));
            if (!Finite(aim.x) || !Finite(aim.y) || aim.sqrMagnitude < 0.25f || aim.sqrMagnitude > 4) return Reply(gun, false);
            aim.Normalize();
            Vector2 origin = gun.Gun.barrel.position;
            if (Vector2.Distance(origin, body.transform.position) > 10 || Mp.IsRunning && NetworkAccess.NetBody(body) == null) return Reply(gun, false);
            if (!gun.Charge.Release(body, Time.time, out float fraction) || !gun.Ammo.Consume(out string id)) return Reply(gun, false);
            gun.NextShot = Time.unscaledTime + 0.5f;
            gun.Item.condition = Mathf.Max(0, gun.Item.condition - 0.002f);
            var profile = CannonAmmoState.Profile(id);
            float damageMultiplier = profile.DamageMultiplier(fraction * ChargeCycle.Maximum);
            var segments = new JArray();
            for (int pellet = 0; pellet < profile.Pellets; pellet++)
            {
                float angle = profile.Spread > 0 ? UnityEngine.Random.Range(-profile.Spread, profile.Spread) : 0;
                Vector2 pelletAim = Quaternion.Euler(0, 0, angle) * aim;
                var fire = new FireInfo { pos = origin, dir = pelletAim, structureDamage = profile.Structure * damageMultiplier,
                    animalDamage = profile.Creature * damageMultiplier, doTinnitus = false, ignoreBody = !Mp.IsRunning };
                Vector2 end;
                if (profile.Effect == CannonEffect.Revive) end = CannonRevival.Fire(body, origin, pelletAim, profile);
                else if (profile.Effect != CannonEffect.None) end = CannonUtility.Fire(body, origin, pelletAim, profile);
                else if (profile.BlastRadius > 0) end = CannonBlast.Fire(body, origin, pelletAim, profile);
                else
                {
                    PulseVisual.Begin(fire);
                    try { if (Mp.IsRunning) MultiplayerCombat.Fire(gun, body, fire); else TurretScript.Shoot(fire); }
                    finally { end = PulseVisual.End(); }
                }
                segments.Add(new JObject { ["gun"] = NetworkAccess.Id(gun.Item), ["x"] = origin.x, ["y"] = origin.y,
                    ["dx"] = pelletAim.x, ["dy"] = pelletAim.y, ["ex"] = end.x, ["ey"] = end.y, ["feedback"] = pellet == 0, ["cannon"] = true });
            }
            gun.Known = true; gun.Normalize();
            var visual = new JObject { ["gun"] = NetworkAccess.Id(gun.Item), ["state"] = gun.CaptureState(), ["pellets"] = segments };
            PulseVisual.Play(visual, profile.BlastRadius > 0 || profile.Effect != CannonEffect.None, gun.Item);
            if (Mp.IsRunning) Mp.Broadcast(WeaponService.VisualChannel, visual);
            gun.Known = true; gun.Normalize(); return Reply(gun, true);
        }
        private static bool CreateReturnedRound(DragonGun gun, out Item item)
        {
            // Instantiate first: failed creation leaves both original slots untouched.
            string id = gun.Ammo.Loaded ? gun.Ammo.Id : gun.State.Loaded ? Registration.CellId : null;
            item = null;
            if (id == null) return false;
            float condition = gun.Ammo.Loaded ? gun.Ammo.Condition : gun.State.Energy / EnergyState.Capacity;
            bool favourite = gun.Ammo.Loaded ? gun.Ammo.Favourite : gun.State.Favourite;
            var created = CustomInstantiate.InstantiateReturn(id, gun.transform.position, Quaternion.identity, condition);
            item = created != null ? created.GetComponent<Item>() : null;
            if (item == null) { if (created != null) UnityEngine.Object.Destroy(created); return false; }
            item.favourited = favourite;
            return true;
        }
        private static bool CanFire(DragonGun gun, Body body) => body.GetItem(body.handSlot) == gun.Item && body.allowUseItem
            && !body.forceNoMovement && !gun.Gun.safe && gun.Item.condition > 0 && gun.Ammo.Ready && Time.unscaledTime >= gun.NextShot;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static JObject Reply(DragonGun gun, bool ok) => new JObject { ["ok"] = ok, ["state"] = gun.CaptureState(), ["chargeElapsed"] = gun.Charge.Elapsed(Time.time) };
    }
}
