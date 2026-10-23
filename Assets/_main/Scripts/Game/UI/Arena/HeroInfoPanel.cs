using System.Collections.Generic;
using AutoChess.Core;
using AutoChess.Core.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AutoChess.Game {
    /// <summary>
    /// Details of a clicked unit of the viewed board (right): name, cost and stars, realm / role chips and the skill
    /// (descriptions on hover), the items (hover for details, X takes one off back to the bench) and the stats.
    /// </summary>
    public sealed class HeroInfoPanel {
        const float Width = 440;
        const float ItemSize = 60;

        static readonly Role[] RoleBuffer = new Role[6];

        static readonly (StatKind stat, string label, string sprite, bool percent)[] StatRows = {
            (StatKind.MaxHp, "Máu", "hp", false), (StatKind.AttackSpeed, "Tốc đánh", "aspd", true),
            (StatKind.PhysicalDamage, "ST vật lý", "pdmg", false), (StatKind.MagicalDamage, "ST phép", "mdmg", false),
            (StatKind.Armor, "Giáp", "arm", false), (StatKind.Resistance, "Kháng phép", "res", false),
            (StatKind.CriticalChance, "Chí mạng", "crc", true), (StatKind.CriticalDamage, "ST chí mạng", "crd", true),
            (StatKind.PhysicalPenetration, "Xuyên giáp", "ppen", false), (StatKind.MagicalPenetration, "Xuyên kháng", "mpen", false),
            (StatKind.LifeSteal, "Hút máu", "ls", true), (StatKind.Tenacity, "Kháng hiệu ứng", "ten", true),
            (StatKind.EnergyRegenEfficiency, "Hồi năng lượng", "ere", true),
        };

        sealed class Chip {
            public RectTransform Rect;
            public Image Icon;
            public TMP_Text Name;
            public DestinyConfig Config;
        }

        sealed class ItemCell {
            public RectTransform Rect;
            public Image Icon;
            public GameObject Remove;
            public string ItemId;
        }

        readonly GameManager game;
        readonly StatBlock stats = new();
        readonly Image thumbnail;
        readonly TMP_Text nameText;
        readonly TMP_Text subText;
        readonly Image rankIcon;
        readonly List<Chip> chips = new();
        readonly Image skillIcon;
        readonly TMP_Text skillName;
        readonly List<ItemCell> items = new();
        readonly TMP_Text itemsHint;
        readonly List<TMP_Text> statTexts = new();
        HeroTrait trait;
        string shown;

        public RectTransform Root { get; }
        /// <summary>Unit shown, or -1.</summary>
        public int UnitId { get; private set; } = -1;
        public bool IsOpen => Root.gameObject.activeSelf;

        public HeroInfoPanel(GameManager game, UiBuilder ui, RectTransform parent) {
            this.game = game;
            Root = ui.Panel(parent, "HeroInfo", new Vector2(1, 0.5f), new Vector2(-12, 70), new Vector2(Width, 640));

            var frame = UiBuilder.Rect(Root, "Picture");
            UiBuilder.Place(frame, new Vector2(0, 1), new Vector2(12, -12), new Vector2(110, 110), new Vector2(0, 1));
            frame.gameObject.AddComponent<RectMask2D>();
            thumbnail = UiBuilder.Icon(frame, "Thumbnail", null);
            nameText = ui.Text(Root, "", 30, TextAlignmentOptions.TopLeft, Color.white);
            UiBuilder.Place(nameText.rectTransform, new Vector2(0, 1), new Vector2(134, -14), new Vector2(Width - 190, 40), new Vector2(0, 1));
            rankIcon = UiBuilder.Icon(Root, "Rank", null);
            UiBuilder.Place(rankIcon.rectTransform, new Vector2(0, 1), new Vector2(134, -54), new Vector2(28, 28), new Vector2(0, 1));
            subText = ui.Text(Root, "", 18, TextAlignmentOptions.TopLeft, UiBuilder.GoldColor);
            subText.richText = true;
            UiBuilder.Place(subText.rectTransform, new Vector2(0, 1), new Vector2(168, -56), new Vector2(Width - 184, 26), new Vector2(0, 1));
            var close = ui.Button(Root, "X", Close, 22);
            close.gameObject.name = "Close";
            close.GetComponent<Image>().color = new Color(0.45f, 0.15f, 0.15f, 1);
            UiBuilder.Place(close, new Vector2(1, 1), new Vector2(-10, -10), new Vector2(40, 40), new Vector2(1, 1));

            // realm / role chips (hover: the synergy)
            for (var i = 0; i < 3; i++) {
                var chip = new Chip();
                var background = UiBuilder.Image(Root, $"Trait{i}", UiBuilder.RowColor);
                chip.Rect = background.rectTransform;
                var chipWidth = (Width - 24 - 16) / 3;
                UiBuilder.Place(chip.Rect, new Vector2(0, 1), new Vector2(12 + i * (chipWidth + 8), -132), new Vector2(chipWidth, 32), new Vector2(0, 1));
                chip.Icon = UiBuilder.Icon(chip.Rect, "Icon", null);
                UiBuilder.Place(chip.Icon.rectTransform, new Vector2(0, 0.5f), new Vector2(4, 0), new Vector2(26, 26), new Vector2(0, 0.5f));
                chip.Name = ui.Text(chip.Rect, "", 16, TextAlignmentOptions.Left, Color.white);
                UiBuilder.Place(chip.Name.rectTransform, new Vector2(0, 0.5f), new Vector2(34, 0), new Vector2(chipWidth - 38, 30), new Vector2(0, 0.5f));
                Hover.On(background, () => ShowTrait(chip));
                chips.Add(chip);
            }

            // skill (hover: its description)
            var skill = UiBuilder.Image(Root, "Skill", UiBuilder.RowColor);
            UiBuilder.Place(skill.rectTransform, new Vector2(0, 1), new Vector2(12, -172), new Vector2(Width - 24, 64), new Vector2(0, 1));
            skillIcon = UiBuilder.Icon(skill.rectTransform, "Icon", null);
            UiBuilder.Place(skillIcon.rectTransform, new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(52, 52), new Vector2(0, 0.5f));
            skillName = ui.Text(skill.rectTransform, "", 20, TextAlignmentOptions.Left, Color.white);
            skillName.richText = true;
            UiBuilder.Place(skillName.rectTransform, new Vector2(0, 0.5f), new Vector2(68, 0), new Vector2(Width - 104, 52), new Vector2(0, 0.5f));
            Hover.On(skill, () => {
                if (trait != null) Tooltip.ShowText(trait.skillIcon, trait.skillName, SkillText(trait), skill.rectTransform);
            });

            // items (hover: details; X: back to the bench)
            var itemsLabel = ui.Label(Root, "Trang bị", new Vector2(12, -246));
            itemsLabel.color = UiBuilder.MutedColor;
            itemsHint = ui.Text(Root, "", 14, TextAlignmentOptions.TopRight, UiBuilder.MutedColor);
            UiBuilder.Place(itemsHint.rectTransform, new Vector2(1, 1), new Vector2(-12, -248), new Vector2(260, 22), new Vector2(1, 1));
            for (var i = 0; i < 3; i++) {
                var cell = new ItemCell();
                var background = UiBuilder.Image(Root, $"Item{i}", UiBuilder.RowColor);
                cell.Rect = background.rectTransform;
                UiBuilder.Place(cell.Rect, new Vector2(0, 1), new Vector2(12 + i * (ItemSize + 12), -276), new Vector2(ItemSize, ItemSize), new Vector2(0, 1));
                cell.Icon = UiBuilder.Icon(cell.Rect, "Icon", null);
                UiBuilder.Stretch(cell.Icon.rectTransform, 3, 3, 3, 3);
                Hover.On(background, () => {
                    if (cell.ItemId != null) Tooltip.ShowItem(cell.ItemId, cell.Rect);
                });
                var index = i;
                var remove = ui.Button(cell.Rect, "X", () => Unequip(index), 15);
                remove.gameObject.name = "Remove";
                remove.GetComponent<Image>().color = new Color(0.75f, 0.18f, 0.18f, 1);
                UiBuilder.Place(remove, new Vector2(1, 1), new Vector2(6, 6), new Vector2(22, 22), new Vector2(1, 1));
                cell.Remove = remove.gameObject;
                items.Add(cell);
            }

            // stats, two columns
            var statsTop = -352f;
            for (var i = 0; i < StatRows.Length; i++) {
                var text = ui.Text(Root, "", 16, TextAlignmentOptions.Left, Color.white);
                text.richText = true;
                var column = i % 2;
                var alone = i == StatRows.Length - 1 && column == 0; // the odd last row gets the full width
                UiBuilder.Place(text.rectTransform, new Vector2(0, 1), new Vector2(14 + column * (Width / 2 - 6), statsTop - i / 2 * 30),
                    new Vector2(alone ? Width - 28 : Width / 2 - 20, 28), new Vector2(0, 1));
                statTexts.Add(text);
            }
            Root.sizeDelta = new Vector2(Width, -statsTop + (StatRows.Length + 1) / 2 * 30 + 12);
            Root.gameObject.SetActive(false);
        }

        /// <summary>Opens the panel for a unit, or closes it when that unit is already shown.</summary>
        public void Toggle(RosterUnitView view) {
            if (IsOpen && UnitId == view.UnitId) {
                Close();
                return;
            }
            UnitId = view.UnitId;
            shown = null;
            Root.gameObject.SetActive(true);
            Refresh();
        }

        public void Close() {
            UnitId = -1;
            trait = null;
            if (Root.gameObject.activeSelf) Tooltip.Hide();
            Root.gameObject.SetActive(false);
        }

        public void OnEvent(GameEvent e) {
            if (UnitId < 0) return;
            if (e is UnitRemovedEvent removed && removed.UnitId == UnitId) {
                if (removed.MergedInto >= 0) UnitId = removed.MergedInto;
                else Close();
                shown = null;
            }
            else if (e is UnitRankChangedEvent || e is UnitItemsChangedEvent || e is PhaseChangedEvent) {
                shown = null;
            }
        }

        public void Refresh() {
            if (UnitId < 0) return;
            var unit = game.ViewedPlayer?.FindUnit(UnitId);
            if (unit == null) {
                Close();
                return;
            }
            var canEdit = CanEdit(unit);
            var key = $"{unit.DefinitionId}|{unit.Rank}|{string.Join(",", unit.Items)}|{canEdit}";
            if (key == shown) return;
            shown = key;
            Fill(unit, canEdit);
        }

        /// <summary>Items come off our own units: bench ones any time, board ones during preparation.</summary>
        bool CanEdit(UnitInstance unit) =>
            !game.IsScouting && (!unit.Location.IsBoard || game.Session.State.Phase == MatchPhase.Preparation);

        void Fill(UnitInstance unit, bool canEdit) {
            var db = game.Db;
            trait = HeroTraitDB.Instance.Find(unit.DefinitionId);
            thumbnail.sprite = trait.thumbnail;
            UiBuilder.AspectFill(thumbnail, (RectTransform)thumbnail.transform.parent, new Vector2(0, -0.3f));
            nameText.text = trait.name;
            rankIcon.sprite = AssetDB.Instance.GetRankIcon(unit.Rank);
            subText.text = $"<color=#C8D0DC>{trait.reputation.ToName()}</color>   {db.Rules.PriceOf(trait.reputation)} vàng";

            chips[0].Config = trait.realm != Realm.None ? DestinyDB.Instance.Find(trait.realm) : null;
            var roles = trait.role.GetRoles(RoleBuffer);
            for (var i = 1; i < chips.Count; i++) chips[i].Config = i - 1 < roles ? DestinyDB.Instance.Find(RoleBuffer[i - 1]) : null;
            foreach (var chip in chips) {
                chip.Rect.gameObject.SetActive(chip.Config != null);
                if (chip.Config == null) continue;
                chip.Icon.sprite = chip.Config.GetIcon();
                chip.Name.text = chip.Config.GetName();
            }

            skillIcon.sprite = trait.skillIcon;
            skillName.text = $"{trait.skillName}\n<size=14><color=#8C96A8>Rê chuột để xem kỹ năng</color></size>";

            for (var i = 0; i < items.Count; i++) {
                var cell = items[i];
                cell.ItemId = i < unit.Items.Count ? unit.Items[i] : null;
                var item = cell.ItemId != null ? ItemDB.Instance.Find(cell.ItemId) : null;
                cell.Icon.enabled = item != null;
                if (item != null) cell.Icon.sprite = item.icon;
                cell.Remove.SetActive(item != null && canEdit);
            }
            itemsHint.text = game.IsScouting ? "" : canEdit ? "X: tháo về hàng chờ" : "Chỉ tháo được khi chuẩn bị";

            UnitStats.Compute(db, db.Unit(unit.DefinitionId), unit.Rank, unit.Items, stats);
            for (var i = 0; i < StatRows.Length; i++) {
                var (stat, label, sprite, percent) = StatRows[i];
                var value = stats[stat];
                var shownValue = percent ? $"{value * 100:0}%" : $"{value:0}";
                statTexts[i].text = $"<sprite name={sprite}> <color=#C8D0DC>{label}</color>  {shownValue}";
            }
        }

        void ShowTrait(Chip chip) {
            if (chip.Config == null) return;
            var tier = -1;
            var members = 0;
            foreach (var status in Traits.ForBoard(game.Db, game.ViewedPlayer)) {
                if (!status.Id.Equals(chip.Config.TraitId)) continue;
                tier = status.Tier;
                members = status.Count;
            }
            Tooltip.ShowText(chip.Config.GetIcon(), chip.Config.GetName(), TraitsPanel.Describe(chip.Config, tier, members), chip.Rect);
        }

        static string SkillText(HeroTrait hero) {
            var text = hero.SkillDescription();
            var header = $"<uppercase>{hero.skillName}</uppercase>\n\n";
            return text.StartsWith(header) ? text.Substring(header.Length) : text;
        }

        void Unequip(int index) {
            if (UnitId < 0) return;
            Tooltip.Hide();
            game.Submit(new UnequipItemCommand { UnitId = UnitId, Index = index });
        }
    }
}
