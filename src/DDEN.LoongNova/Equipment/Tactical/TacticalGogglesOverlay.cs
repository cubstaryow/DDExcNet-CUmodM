using System.Collections.Generic;
using DDEN.Shared;
using UnityEngine;

namespace DDEN.LoongNova
{
    public sealed class TacticalGogglesOverlay : MonoBehaviour
    {
        private sealed class Target
        {
            internal BuildingEntity Building;
            internal SpriteRenderer[] Renderers;
            internal Collider2D Collider;
            internal TraderScript Trader;
            internal TacticalKind Kind;
            internal TurretScript Turret;
            internal CeilingTurret Ceiling;
            internal SoundCannon Sound;
            internal LaserDroneScript Drone;
            internal GuardScript Guard;
            internal bool Elder;
        }
        private struct Mark { internal Vector2 Position; internal float Range; internal string Label; }
        private static readonly List<Target> Targets = new List<Target>();
        private static readonly HashSet<BuildingEntity> Known = new HashSet<BuildingEntity>();
        private static bool _seeded;
        private WorldGeneration _world;
        private BuildingEntity[] _seed;
        private int _seedIndex, _targetIndex;
        private TacticalLineMesh _terrain, _hazards;
        private readonly List<Mark> _warnings = new List<Mark>(TacticalRules.MaxWarnings);
        private readonly List<Mark> _nextWarnings = new List<Mark>(TacticalRules.MaxWarnings);
        private float _nextTargets, _nextTerrain;
        private bool _scanningTerrain;
        private RectInt _terrainArea;
        private int _cell;
        private readonly List<Mark> _bosses = new List<Mark>(3), _nextBosses = new List<Mark>(3);
        private readonly List<Mark> _traders=new List<Mark>(4), _nextTraders=new List<Mark>(4);
        private bool _belowWarning;
        private Vector2 _belowPosition;
        private GUIStyle _labelStyle;
        private struct SoundAlert{internal Vector2 Position;internal float Until;}
        private static readonly List<SoundAlert> SoundAlerts=new List<SoundAlert>(4);
        private static readonly System.Reflection.FieldInfo SoundCharging=HarmonyLib.AccessTools.Field(typeof(SoundCannon),"charging"),SoundSpent=HarmonyLib.AccessTools.Field(typeof(SoundCannon),"spent");
        private Rect _protectedHud;
        private readonly Rect[] _labelRects=new Rect[24];private int _labelCount;
        internal static void SoundWarning(Vector2 position)
        {
            for(int i=0;i<SoundAlerts.Count;i++)if((SoundAlerts[i].Position-position).sqrMagnitude<4)
            {SoundAlerts[i]=new SoundAlert{Position=position,Until=Time.unscaledTime+5.5f};return;}
            if(SoundAlerts.Count<8)SoundAlerts.Add(new SoundAlert{Position=position,Until=Time.unscaledTime+5.5f});
        }
        private Camera _camera;
        private Rect _view;
        private Vector2 _player;
        private static readonly Color TerrainColor = new Color(0.3f,0.95f,1,0.95f), DangerColor = new Color(1,0.3f,0.12f,0.95f);
        internal static void Observe(BuildingEntity building)
        {
            if (!_seeded || building == null || Known.Contains(building)) return;
            var turret = building.GetComponent<TurretScript>(); var ceiling = building.GetComponent<CeilingTurret>(); var sound = building.GetComponent<SoundCannon>();
            var drone = building.GetComponent<LaserDroneScript>(); var guard = building.GetComponent<GuardScript>();
            bool elder = building.GetComponent<ElderThornbackBehaviour>() != null;
            var trader=building.GetComponentInChildren<TraderScript>(true);
            bool stationary=BreachTargets.IsTrapId(building.id);
            foreach(var script in building.GetComponentsInChildren<MonoBehaviour>(true))
                if(script!=null && TacticalRules.HazardScript(script.GetType().Name))stationary=true;
            bool hazard = building.animal || stationary;
            if(!hazard && turret==null && ceiling==null && sound==null && drone==null && guard==null && !elder && trader==null)return;
            Known.Add(building);
            Targets.Add(new Target { Building = building, Renderers = building.GetComponentsInChildren<SpriteRenderer>(true), Collider=building.GetComponentInChildren<Collider2D>(true), Trader=trader, Turret = turret, Ceiling = ceiling, Sound = sound, Drone = drone, Guard = guard, Elder = elder,
                Kind = sound != null ? TacticalKind.Sound : turret != null || ceiling != null || drone != null || guard != null ? TacticalKind.Turret : building.animal && !stationary ? TacticalKind.Creature : TacticalKind.Trap });
        }
        private bool VisibleOverlay() => _camera != null && TacticalGogglesPatches.Active(PlayerCamera.main) && !PauseHandler.paused
            && !DdenHub.AnyPanelVisible && !PlayerCamera.main.radialOpen && !PlayerCamera.main.woundView.activeSelf && !PlayerCamera.main.tradeMenu.activeSelf;
        private void LateUpdate()
        {
            _camera = Camera.main;
            if (_world != WorldGeneration.world || _world != null && _world.generatingWorld)
            { ClearTargets(); _world=WorldGeneration.world; _seed=null; }
            if (!VisibleOverlay()) { _terrain?.Show(false); _hazards?.Show(false); return; }
            if (!_seeded)
            {
                _seeded = true;
                // One discovery snapshot per world, processed in bounded batches, never a per-frame scene search.
                _seed = Object.FindObjectsOfType<BuildingEntity>(); _seedIndex = _targetIndex = 0;
                _terrain?.Begin(); _terrain?.Commit(); _hazards?.Begin(); _hazards?.Commit();
                _nextTerrain = _nextTargets = 0; _scanningTerrain = false; _warnings.Clear(); _bosses.Clear(); _traders.Clear(); _belowWarning = false;
            }
            if (_terrain == null) { _terrain = new TacticalLineMesh("LoongNova local terrain",94,transform); _hazards = new TacticalLineMesh("LoongNova local warnings",96,transform); }
            _terrain.Show(true); _hazards.Show(true);
            int seeded = 0;
            while (_seed != null && _seedIndex < _seed.Length && seeded++ < TacticalRules.TargetsPerFrame) Observe(_seed[_seedIndex++]);
            if (_seed != null && _seedIndex >= _seed.Length) _seed = null;
            var a = _camera.ViewportToWorldPoint(new Vector3(0,0,0)); var b = _camera.ViewportToWorldPoint(new Vector3(1,1,0));
            _view = Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y),Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y));
            _player = PlayerCamera.main.body.transform.position;
            ScanTerrain(); ScanTargets();
        }
        private void ScanTargets()
        {
            if (Time.unscaledTime < _nextTargets) return;
            if (_targetIndex == 0) { _hazards.Begin(); _nextWarnings.Clear(); _nextBosses.Clear(); _nextTraders.Clear(); }
            int processed = 0;
            while (_targetIndex < Targets.Count && processed++ < TacticalRules.TargetsPerFrame)
            {
                var target = Targets[_targetIndex]; var building = target.Building;
                if (building == null)
                { Known.Remove(building); Targets[_targetIndex] = Targets[Targets.Count-1]; Targets.RemoveAt(Targets.Count-1); continue; }
                _targetIndex++;
                if (!building.gameObject.activeInHierarchy || building.animal && building.health <= 0 && !building.indestructible && target.Trader==null) continue;
                Vector2 position = building.transform.position;
                var bounds=new Bounds(position,new Vector3(1,1,0)); bool hasBounds=false;
                foreach(var renderer in target.Renderers)
                {
                    if(renderer==null || renderer.bounds.size.sqrMagnitude<0.001f)continue;
                    if(!hasBounds){bounds=renderer.bounds;hasBounds=true;}else bounds.Encapsulate(renderer.bounds);
                }
                if(!hasBounds && target.Collider!=null)bounds=target.Collider.bounds;
                var size=bounds.size;
                size.x=Mathf.Max(size.x,_view.width/Screen.width*14);size.y=Mathf.Max(size.y,_view.height/Screen.height*14);
                bounds.size=size;
                bounds.Expand(0.3f);
                bool inside = _view.Overlaps(Rect.MinMaxRect(bounds.min.x,bounds.min.y,bounds.max.x,bounds.max.y));
                if(target.Trader!=null)
                {
                    if(TacticalRules.TraderSignal((position-_player).sqrMagnitude) && _nextTraders.Count<4)
                        _nextTraders.Add(new Mark{Position=position,Range=(position-_player).sqrMagnitude,
                            Label=string.Format(Registration.Text("tacticalTraderSignal"),Vector2.Distance(position,_player))});
                    if(inside)_hazards.Box(bounds,new Color(0.15f,0.85f,1,1));
                    continue;
                }
                if(target.Sound!=null)
                {
                    bool spent=SoundSpent?.GetValue(target.Sound) is bool done&&done;
                    if(!spent && SoundCharging?.GetValue(target.Sound) is bool charging&&charging && Vector2.Distance(position,_player)<=target.Sound.maxDistance)
                        SoundWarning(position);
                    if(spent)for(int k=SoundAlerts.Count-1;k>=0;k--)if((SoundAlerts[k].Position-position).sqrMagnitude<4)SoundAlerts.RemoveAt(k);
                }
                float range = target.Sound != null ? target.Sound.maxDistance : target.Drone != null ? 64 : target.Guard != null ? GuardScript.MAX_ACTIVE_DISTANCE : 100;
                bool boss = target.Elder && TacticalRules.ElderSignal(_world.biomeDepth,building.health>0,building.gameObject.activeInHierarchy,(position-_player).sqrMagnitude);
                bool extra = TacticalRules.ExtraWarning(target.Kind,position.x-_player.x,position.y-_player.y,range);
                if (!inside && !extra && !boss) continue;
                if (boss && _nextBosses.Count < 3)
                    _nextBosses.Add(new Mark { Position=position, Range=(position-_player).sqrMagnitude,
                        Label=string.Format(Registration.Text("tacticalElderSignal"),Vector2.Distance(position,_player)) });
                if (inside) _hazards.Box(bounds,DangerColor);
                else if (!boss) AddWarning(position);
                if (target.Turret != null && target.Turret.barrel != null)
                    Beam(target.Turret.barrel.position, target.Turret.transform.right * target.Turret.transform.localScale.x,200);
                else if (target.Ceiling != null && target.Ceiling.lowerGun != null)
                    Beam(target.Ceiling.lowerGun.position,-target.Ceiling.lowerGun.up,200);
                else if (target.Drone != null && target.Drone.barrel != null && target.Drone.open)
                    Beam(target.Drone.barrel.position,target.Drone.shootTarget-(Vector2)target.Drone.barrel.position,200);
                else if (target.Guard != null && target.Guard.barrel != null && target.Guard.isAiming)
                    Beam(target.Guard.barrel.position,target.Guard.barrel.right*(target.Guard.transform.localScale.x<0?-1:1),200);
            }
            if (_targetIndex < Targets.Count) return;
            _hazards.Commit(); _warnings.Clear(); _warnings.AddRange(_nextWarnings); _bosses.Clear(); _bosses.AddRange(_nextBosses); _traders.Clear(); _traders.AddRange(_nextTraders);
            _targetIndex = 0; _nextTargets = Time.unscaledTime + 0.2f;
        }
        private void Beam(Vector2 start, Vector2 direction, float distance)
        {
            Vector2 end = start + direction.normalized * distance;
            if (_world.Linecast(start + direction.normalized * 0.6f,end,out var hit,out _)) end = hit;
            _hazards.Line(start,end,new Color(1,0.5f,0.1f,0.65f));
        }
        private void AddWarning(Vector2 position)
        {
            var mark = new Mark { Position = position, Range = (position-_player).sqrMagnitude };
            int index = _nextWarnings.Count;
            while (index > 0 && _nextWarnings[index-1].Range > mark.Range) index--;
            if (index >= TacticalRules.MaxWarnings) return;
            _nextWarnings.Insert(index,mark);
            if (_nextWarnings.Count > TacticalRules.MaxWarnings) _nextWarnings.RemoveAt(TacticalRules.MaxWarnings);
        }
        private void ScanTerrain()
        {
            if (!_scanningTerrain)
            {
                if (Time.unscaledTime < _nextTerrain) return;
                var min = _world.WorldToBlockPos(_view.min-Vector2.one*8); var max = _world.WorldToBlockPos(_view.max+Vector2.one*8);
                int x = Mathf.Clamp(min.x,1,(int)_world.width-2), y = Mathf.Clamp(min.y,1,(int)_world.height-2);
                _terrainArea = new RectInt(x,y,Mathf.Max(1,Mathf.Min(max.x,(int)_world.width-2)-x+1),Mathf.Max(1,Mathf.Min(max.y,(int)_world.height-2)-y+1));
                _terrain.Begin(); _cell = 0; _scanningTerrain = true;
            }
            int total = _terrainArea.width * _terrainArea.height, stop = Mathf.Min(total,_cell+TacticalRules.CellsPerFrame);
            for (; _cell < stop; _cell++)
            {
                var cell = new Vector2Int(_terrainArea.x+_cell%_terrainArea.width,_terrainArea.y+_cell/_terrainArea.width);
                ushort block = _world.GetBlock(cell); if (block == 0) continue;
                Vector2 p = _world.BlockToWorldPos(cell)-Vector2.one*0.5f; var info = _world.GetBlockInfo(block);
                bool danger = info != null && (info.hot || info.toxicity > 0);
                var color = danger ? DangerColor : TerrainColor;
                if (_world.GetBlock(cell+Vector2Int.up)==0) _terrain.Line(p+Vector2.up,p+Vector2.one,color);
                if (_world.GetBlock(cell+Vector2Int.down)==0) _terrain.Line(p,p+Vector2.right,color);
                if (_world.GetBlock(cell+Vector2Int.left)==0) _terrain.Line(p,p+Vector2.up,color);
                if (_world.GetBlock(cell+Vector2Int.right)==0) _terrain.Line(p+Vector2.right,p+Vector2.one,color);
            }
            if (_cell < total) return;
            // Only the narrow column beneath the wearer receives additional off-screen terrain detection.
            _belowWarning = false;
            var below = _world.WorldToBlockPos(_player);
            for (int depth=1; depth<=32; depth++)
            for (int dx=-4; dx<=4; dx++)
            {
                var cell = new Vector2Int(below.x+dx,below.y-depth);
                if (cell.x<1 || cell.y<1 || cell.x>=(int)_world.width-1 || cell.y>=(int)_world.height-1) continue;
                ushort block = _world.GetBlock(cell); if (block==0) continue;
                var info = _world.GetBlockInfo(block);
                if (info != null && (info.hot || info.toxicity>0))
                {
                    var p=_world.BlockToWorldPos(cell); _terrain.Box(new Bounds(p,Vector2.one),DangerColor);
                    if (!_belowWarning && !_view.Contains(p)) { _belowPosition=p; _belowWarning=true; }
                }
            }
            _terrain.Commit(); _scanningTerrain = false; _nextTerrain = Time.unscaledTime+0.5f;
        }
        private void OnGUI()
        {
            if(Event.current.type!=EventType.Repaint || !VisibleOverlay())return;
            var previousColor=GUI.color;var previousMatrix=GUI.matrix;int depth=GUI.depth;
            try
            {
                GUI.matrix=Matrix4x4.identity;GUI.depth=-50;
                float scale=PlayerCamera.main.mainCanvas!=null?PlayerCamera.main.mainCanvas.scaleFactor:1;
                _protectedHud=new Rect(0,Screen.height-Mathf.Max(190*scale,Screen.height*0.19f),Mathf.Max(560*scale,Screen.width*0.38f),Screen.height);
                _terrain?.Draw(_camera,_protectedHud);_hazards?.Draw(_camera,_protectedHud);_labelCount=0;
                if(_labelStyle==null)_labelStyle=new GUIStyle(GUI.skin.label){fontSize=14,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter};
                for(int i=0;i<_warnings.Count+_bosses.Count+_traders.Count+(_belowWarning?1:0);i++)
                {
                    int n=i-_warnings.Count;
                    bool boss=n>=0&&n<_bosses.Count;
                    bool trader=n>=_bosses.Count&&n<_bosses.Count+_traders.Count;
                    var warning=i<_warnings.Count?_warnings[i]:boss?_bosses[n]:trader?_traders[n-_bosses.Count]:new Mark{Position=_belowPosition};
                    var color=boss?new Color(1,0.4f,0.95f,1):trader?new Color(0.2f,0.9f,1,1):DangerColor;
                    var v=_camera.WorldToViewportPoint(warning.Position);
                    if(v.x>=0&&v.x<=1&&v.y>=0&&v.y<=1)
                    {
                        if(boss||trader)Plate(new Vector2(v.x*Screen.width,(1-v.y)*Screen.height+24),warning.Label,color);
                        continue;
                    }
                    var playerScreen=_camera.WorldToScreenPoint(_player);
                    var origin=new Vector2(Mathf.Clamp(playerScreen.x,100,Screen.width-100),Mathf.Clamp(Screen.height-playerScreen.y,100,Screen.height-100));
                    var targetScreen=new Vector2(v.x*Screen.width,(1-v.y)*Screen.height);
                    TacticalScreenGeometry.Edge(origin.x,origin.y,targetScreen.x,targetScreen.y,70,70,Screen.width-70,Screen.height-70,out float px,out float py);
                    var point=new Vector2(px,py);
                    if(TacticalScreenGeometry.Span(origin.x,origin.y,point.x,point.y,_protectedHud.xMin-30,_protectedHud.yMin-30,_protectedHud.xMax+30,_protectedHud.yMax+30,out float entry,out _) && entry>0)
                        point=Vector2.Lerp(origin,point,Mathf.Max(0,entry-0.02f));
                    var direction=targetScreen-origin;
                    if(!_protectedHud.Contains(point))Arrow(point,direction,color,boss||trader?25:18);
                    if(boss||trader)Plate(point-direction.normalized*38,warning.Label,color);
                }
                for(int i=SoundAlerts.Count-1;i>=0;i--)
                {
                    var alert=SoundAlerts[i];if(Time.unscaledTime>alert.Until){SoundAlerts.RemoveAt(i);continue;}
                    var v=_camera.WorldToScreenPoint(alert.Position);var center=new Vector2(v.x,Screen.height-v.y);
                    var origin=_camera.WorldToScreenPoint(_player);origin.y=Screen.height-origin.y;
                    TacticalScreenGeometry.Edge(origin.x,origin.y,center.x,center.y,90,90,Screen.width-90,Screen.height-90,out float x,out float y);
                    bool inside=center.x>=90&&center.x<=Screen.width-90&&center.y>=90&&center.y<=Screen.height-90;
                    var point=inside?center:new Vector2(x,y);
                    if(TacticalScreenGeometry.Span(origin.x,origin.y,point.x,point.y,_protectedHud.xMin-35,_protectedHud.yMin-35,_protectedHud.xMax+35,_protectedHud.yMax+35,out float alarmEntry,out _)&&alarmEntry>0)point=Vector2.Lerp(origin,point,Mathf.Max(0,alarmEntry-0.02f));
                    if(!_protectedHud.Contains(point))Arrow(point,center-(Vector2)origin,new Color(1,0.18f,0.08f,1),30);
                    Plate(point+Vector2.up*34,Registration.Text("tacticalSoundCharging"),new Color(1,0.18f,0.08f,1));
                }
            }
            finally{GUI.matrix=previousMatrix;GUI.color=previousColor;GUI.depth=depth;}
        }
        private void Plate(Vector2 point,string label,Color color)
        {
            var rect=new Rect(Mathf.Clamp(point.x-83,8,Screen.width-174),Mathf.Clamp(point.y-14,8,Screen.height-38),166,28);
            if(rect.Overlaps(_protectedHud))rect.y=Mathf.Max(8,_protectedHud.yMin-rect.height-12);
            for(int attempt=0;attempt<18;attempt++)
            {
                bool overlap=false;for(int i=0;i<_labelCount;i++)if(rect.Overlaps(_labelRects[i])){overlap=true;break;}
                if(!overlap)break;
                rect.y=rect.y>Screen.height/2?rect.y-32:rect.y+32;
                rect.y=Mathf.Clamp(rect.y,8,Screen.height-38);
                if(rect.Overlaps(_protectedHud))rect.y=_protectedHud.yMin-rect.height-12;
            }
            if(_labelCount<_labelRects.Length)_labelRects[_labelCount++]=rect;
            GUI.color=new Color(0.015f,0.025f,0.04f,0.94f);GUI.DrawTexture(rect,Texture2D.whiteTexture);
            GUI.color=color;GUI.DrawTexture(new Rect(rect.x,rect.y,4,rect.height),Texture2D.whiteTexture);
            GUI.color=Color.white;GUI.Label(rect,label,_labelStyle);
        }
        private static void Arrow(Vector2 point,Vector2 direction,Color color,float size)
        {
            var matrix=GUI.matrix;float angle=Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg;
            for(int pass=0;pass<2;pass++)
            {
                GUI.color=pass==0?new Color(0.01f,0.02f,0.03f,1):color;
                float width=pass==0?8:4;
                GUIUtility.RotateAroundPivot(angle+45,point);
                GUI.DrawTexture(new Rect(point.x-size,point.y-width/2,size,width),Texture2D.whiteTexture);
                GUI.matrix=matrix;GUIUtility.RotateAroundPivot(angle-45,point);
                GUI.DrawTexture(new Rect(point.x-size,point.y-width/2,size,width),Texture2D.whiteTexture);
                GUI.matrix=matrix;
            }
        }
        private static void ClearTargets() { Targets.Clear(); Known.Clear(); SoundAlerts.Clear(); _seeded=false; }
        private void OnDisable() { _terrain?.Show(false); _hazards?.Show(false); TacticalGogglesPatches.Reset(); }
        private void OnDestroy() { _terrain?.Dispose(); _hazards?.Dispose(); ClearTargets(); TacticalGogglesPatches.Reset(); }
    }
}
