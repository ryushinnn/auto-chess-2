using AutoChess.Core;
using AutoChess.Core.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AutoChess.Game {
    /// <summary>Details of an owned unit: identity, synergies, skill, items and computed stats.</summary>
    public class HeroInfoUI : BaseUI, ISessionView {
        [SerializeField] Image thumbnailImage;
        [SerializeField] TMP_Text nameText;
        [SerializeField] TMP_Text priceText;
        [SerializeField] HeroInfo_Destiny[] destinies;
        [SerializeField] Button skillButton;
        [SerializeField] Image skillImage;
        [SerializeField] TextBox skillDescription;
        [SerializeField] HeroInfo_Item[] items;

        [SerializeField] TMP_Text hpText;
        [SerializeField] TMP_Text energyText;
        [SerializeField] TMP_Text physicalDmgText;
        [SerializeField] TMP_Text magicalDmgText;
        [SerializeField] TMP_Text armorText;
        [SerializeField] TMP_Text resistanceText;
        [SerializeField] TMP_Text attackSpeedText;
        [SerializeField] TMP_Text energyRegenEfficientText;
        [SerializeField] TMP_Text physicalPenetrationText;
        [SerializeField] TMP_Text magicalPenetrationText;
        [SerializeField] TMP_Text criticalChanceText;
        [SerializeField] TMP_Text criticalDamageText;
        [SerializeField] TMP_Text lifeStealText;
        [SerializeField] TMP_Text tenacityText;

        [SerializeField] Button closeButton;

        static readonly Role[] RoleBuffer = new Role[6];
        readonly StatBlock stats = new();

        GameManager game;
        int unitId = -1;

        /// <summary>Unit shown, or -1.</summary>
        public int UnitId => unitId;

        void Awake() {
            skillButton.onClick.AddListener(SwitchSkillDescription);
            closeButton.onClick.AddListener(Close);
        }

        public void Bind(GameManager gameManager) {
            game = gameManager;
            game.EventRaised += OnEvent;
        }

        void OnDestroy() {
            if (game != null) game.EventRaised -= OnEvent;
        }

        /// <summary>Opens the panel for a unit, or closes it when that unit is already shown.</summary>
        public void Toggle(RosterUnitView view) {
            if (IsOpen && unitId == view.UnitId) {
                Close();
                return;
            }
            if (!IsOpen) skillDescription.Switch(false);
            Open();
            unitId = view.UnitId;
            Refresh();
        }

        public override void Close() {
            unitId = -1;
            base.Close();
        }

        void OnEvent(GameEvent e) {
            if (unitId < 0) return;
            switch (e) {
                case UnitRemovedEvent removed when removed.UnitId == unitId:
                    if (removed.MergedInto >= 0) {
                        unitId = removed.MergedInto;
                        Refresh();
                    }
                    else {
                        Close();
                    }
                    break;
                case UnitRankChangedEvent ranked when ranked.UnitId == unitId:
                case UnitItemsChangedEvent itemsChanged when itemsChanged.UnitId == unitId:
                    Refresh();
                    break;
            }
        }

        void Refresh() {
            var unit = game.Session.LocalPlayer.FindUnit(unitId);
            if (unit == null) {
                Close();
                return;
            }
            var trait = HeroTraitDB.Instance.Find(unit.DefinitionId);
            UpdateIdentity(trait);
            UpdateItems(unit);
            UpdateAttributeValues(unit);
        }

        void UpdateIdentity(HeroTrait trait) {
            thumbnailImage.sprite = trait.thumbnail;
            nameText.text = trait.name;
            priceText.text = $"{game.Db.Rules.PriceOf(trait.reputation)}";
            skillImage.sprite = trait.skillIcon;
            destinies[0].Initialize(trait.realm);
            var roles = trait.role.GetRoles(RoleBuffer);
            for (int i = 1; i <= 2; i++) {
                if (i - 1 >= roles) {
                    destinies[i].gameObject.SetActive(false);
                    continue;
                }
                destinies[i].gameObject.SetActive(true);
                destinies[i].Initialize(RoleBuffer[i - 1]);
            }
            skillDescription.SetText(trait.SkillDescription());
        }

        void UpdateItems(UnitInstance unit) {
            for (int i = 0; i < items.Length; i++) {
                var item = i < unit.Items.Count ? ItemDB.Instance.Find(unit.Items[i]) : null;
                items[i].SetData(item);
            }
        }

        void UpdateAttributeValues(UnitInstance unit) {
            var db = game.Db;
            UnitStats.Compute(db, db.Unit(unit.DefinitionId), unit.Rank, unit.Items, stats);
            var maxHp = (int)stats[StatKind.MaxHp];
            hpText.text = $"{maxHp}/{maxHp}";
            energyText.text = $"0/{(int)db.Rules.MaxEnergy}";
            physicalDmgText.text = $"{(int)stats[StatKind.PhysicalDamage]}";
            magicalDmgText.text = $"{(int)stats[StatKind.MagicalDamage]}";
            armorText.text = $"{(int)stats[StatKind.Armor]}";
            resistanceText.text = $"{(int)stats[StatKind.Resistance]}";
            attackSpeedText.text = $"{(int)(stats[StatKind.AttackSpeed] * 100)}%";
            energyRegenEfficientText.text = $"{(int)(stats[StatKind.EnergyRegenEfficiency] * 100)}%";
            physicalPenetrationText.text = $"{(int)stats[StatKind.PhysicalPenetration]}";
            magicalPenetrationText.text = $"{(int)stats[StatKind.MagicalPenetration]}";
            criticalChanceText.text = $"{(int)(stats[StatKind.CriticalChance] * 100)}%";
            criticalDamageText.text = $"{(int)(stats[StatKind.CriticalDamage] * 100)}%";
            lifeStealText.text = $"{(int)(stats[StatKind.LifeSteal] * 100)}%";
            tenacityText.text = $"{(int)(stats[StatKind.Tenacity] * 100)}%";
        }

        void SwitchSkillDescription() {
            skillDescription.Switch();
        }
    }
}
