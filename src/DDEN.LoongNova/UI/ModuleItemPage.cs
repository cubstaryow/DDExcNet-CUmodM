using DDEN.Shared;
using UnityEngine;

namespace DDEN.LoongNova
{
    internal sealed class ModuleItemPage
    {
        private sealed class Card
        {
            internal readonly string Id, Key;
            internal Card(string id, string key) { Id = id; Key = key; }
        }
        private static readonly Card[] Cards =
        {
            new Card(SpatialCatalog.ChargerId, "recharger"), new Card(SpatialCatalog.CrateId, "spatialCrate"), new Card(SpatialCatalog.MembraneId, "membrane"), new Card(SpatialCatalog.ComponentId, "spatialComponent"),
            new Card(SupplyCrateRules.SealedId, "supplySealed"), new Card(SupplyCrateRules.OpenId, "supplyOpen"),
            new Card(MedicalCatalog.InjectorId, "injector"), new Card(MedicalCatalog.RegenerationId, "regeneration"), new Card(MedicalCatalog.MedicalId, "medical"), new Card(MedicalCatalog.EmptyId, "emptyAmpoule"), new Card(CannonAmmoState.RevivalId, "revival"), new Card(Registration.BeltId, "belt"), new Card(Registration.GunId, "gun"),
            new Card(Registration.CellId, "cell"), new Card(Registration.WrenchId, "wrench"),
            new Card(Registration.ChargeGunId, "charge"), new Card(CannonAmmoState.BioId, "bio"), new Card(CannonAmmoState.DemolitionId, "demo"), new Card(CannonAmmoState.DrillId, "drill"), new Card(CannonAmmoState.DrainId, "drain"), new Card(CannonAmmoState.BreachId, "breach"), new Card(CannonAmmoState.CarrierId, "carrier"), new Card(CannonAmmoState.ExplosiveId, "explosive"), new Card(Registration.PickaxeId, "pickaxe"), new Card(Registration.MatterId, "matter"), new Card(Registration.DragonId, "dragon")
        };
        private readonly Color _copper = new Color(0.93f, 0.55f, 0.28f);
        private Vector2 _scroll;
        private int _selected;
        private bool _recipe;

        internal void Draw(Rect area)
        {
            float width = Mathf.Max(1, area.width - 20);
            var selected = Cards[_selected];
            string detail = Registration.Text(selected.Key + (_recipe ? "Recipe" : "Description"));
            if (_recipe) detail = RecipeUnlocks.CatalogDetail(selected.Id, detail);
            float textHeight = DdenUI.Wrap.CalcHeight(new GUIContent(detail), Mathf.Max(1, width - 32));
            float gridHeight = ItemCardLayout.GridHeight(width, Cards.Length);
            float detailHeight = textHeight + 86;
            float total = 82 + gridHeight + detailHeight + 14;
            _scroll = DdenUI.BeginScrollView(area, _scroll, new Rect(0, 0, width, Mathf.Max(area.height, total)));
            try { DrawContent(width, gridHeight, detailHeight, detail); }
            finally { DdenUI.EndScrollView(); }
        }

        private void DrawContent(float width, float gridHeight, float detailHeight, string detail)
        {
            DrawStatus(new Rect(0, 0, width, 64));
            float y = 76;
            int columns = ItemCardLayout.Columns(width);
            float cardWidth = ItemCardLayout.CardWidth(width);
            for (int i = 0; i < Cards.Length; i++)
            {
                var box = new Rect((i % columns) * (cardWidth + ItemCardLayout.Gap),
                    y + (i / columns) * (ItemCardLayout.Height + ItemCardLayout.Gap), cardWidth, ItemCardLayout.Height);
                DrawCard(box, i);
            }
            y += gridHeight + 8;
            var selected = Cards[_selected];
            var panel = new Rect(0, y, width, detailHeight);
            DdenUI.Fill(panel, DdenUI.Panel);
            DdenUI.Fill(new Rect(0, y, 3, detailHeight), _copper);
            DdenUI.Label(new Rect(16, y + 10, width - 32, 24), Registration.Text(selected.Key + "Name"), DdenUI.Title);
            float tabWidth = Mathf.Min(110, (width - 42) * 0.5f);
            if (DdenUI.Button(new Rect(16, y + 36, tabWidth, 24), Registration.Text("infoTab"), !_recipe)) _recipe = false;
            if (DdenUI.Button(new Rect(26 + tabWidth, y + 36, tabWidth, 24), Registration.Text("recipeTab"), _recipe)) _recipe = true;
            DdenUI.Label(new Rect(16, y + 70, width - 32, detailHeight - 76), detail, DdenUI.Wrap);
        }

        private void DrawCard(Rect box, int index)
        {
            bool selected = index == _selected;
            var card = Cards[index];
            DdenUI.Fill(box, selected ? DdenUI.PanelAlt : DdenUI.Panel);
            DdenUI.Frame(box, selected ? _copper : DdenUI.AccentDim);
            DdenUI.Fill(new Rect(box.x, box.y, 3, box.height), selected ? _copper : DdenUI.AccentDim);
            var icon = new Rect(box.x + 12, box.y + 12, 44, 44);
            DdenUI.Fill(icon, DdenUI.Bg);
            DdenUI.DrawItemIcon(new Rect(icon.x + 4, icon.y + 4, 36, 36), card.Id);
            float textX = box.x + 66;
            float textWidth = Mathf.Max(1, box.width - 76);
            string name = Registration.Text(card.Key + "Name");
            DdenUI.Label(new Rect(textX, box.y + 5, textWidth, 22), DdenUI.ElideToWidth(name, textWidth, DdenUI.Row), DdenUI.Row);
            DdenUI.Label(new Rect(textX, box.y + 29, textWidth, 38), Registration.Text(card.Key + "Card"), DdenUI.Wrap);
            DdenUI.Hover(box, name + "\n" + Registration.Text(card.Key + "Description"));
            if (GUI.Button(box, GUIContent.none, GUIStyle.none)) _selected = index;
        }

        private void DrawStatus(Rect box)
        {
            DdenUI.Fill(box, DdenUI.Panel);
            var body = PlayerCamera.main != null ? PlayerCamera.main.body : null;
            var held = body != null ? body.GetItem(body.handSlot) : null;
            string gunText = Registration.Text("notEquipped");
            if (held != null && Registration.IsGun(held.id))
            {
                var gun = DragonGun.For(held);
                gunText = string.Format(Registration.Text(gun.IsChargeGun ? "chargeLive" : "gunLive"), gun.CellHint(), held.condition * 100);
                if (gun.Status.Length > 0) gunText += " · " + gun.Status;
            }
            DdenUI.Label(new Rect(box.x + 12, box.y + 4, box.width - 24, 22),
                DdenUI.ElideToWidth(gunText, box.width - 24, DdenUI.Row), DdenUI.Row);
            var belt = GravityBelt.Worn(body);
            string beltText = belt == null ? Registration.Text("beltNotEquipped") : belt.Status.Length > 0 ? belt.Status
                : belt.State.Cell.Loaded ? string.Format(Registration.Text("beltSocketLoaded"), belt.State.Remaining, belt.State.ChargeFraction * 100)
                : Registration.Text("beltSocketEmpty");
            float buttonWidth = belt != null ? Mathf.Min(120, box.width * 0.35f) : 0;
            DdenUI.Label(new Rect(box.x + 12, box.y + 34, box.width - 30 - buttonWidth, 24),
                DdenUI.ElideToWidth(beltText, box.width - 30 - buttonWidth, DdenUI.Sub), DdenUI.Sub);
            if (belt != null && DdenUI.Button(new Rect(box.xMax - buttonWidth - 12, box.y + 34, buttonWidth, 24), Registration.Text("ejectShort")))
                belt.Request("eject", body);
        }
    }
}
