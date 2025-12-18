using System;
using System.Linq;
using RExt.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HeroInfoUI : BaseUI {
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

    Hero hero;

    void Awake() {
        skillButton.onClick.AddListener(SwitchSkillDescription);
        closeButton.onClick.AddListener(Close);
    }

    public override void Open(params object[] args) {
        if (!gameObject.activeSelf) {
            skillDescription.Switch(false);
        }
        
        base.Open();
        var targetHero = (Hero)args[0];
        
        if (hero == targetHero) {
            Close();
            return;
        }
        
        hero = targetHero;
        UpdateIdentity();
        UpdateItems();
        UpdateAttributeValues();
    }
    
    public override void Close() {
        hero = null;
        base.Close();
    }

    void UpdateIdentity() {
        var trait = hero.Trait;
        thumbnailImage.sprite = trait.thumbnail;
        nameText.text = trait.name;
        priceText.text = $"{GameConfigs.HERO_PRICES[trait.reputation]}";
        skillImage.sprite = trait.skillIcon;
        destinies[0].Initialize(trait.realm);
        var roles = trait.role.GetAllFlags().Where(x => x != 0).ToArray();
        for (int i = 1; i <= 2; i++) {
            if (i-1>=roles.Length) {
                destinies[i].gameObject.SetActive(false);
                continue;
            }
            
            destinies[i].gameObject.SetActive(true);
            destinies[i].Initialize(roles[i-1]);
        }
        
        skillDescription.SetText(hero.Trait.SkillDescription());
    }

    void UpdateItems() {
        var inventory = hero.GetAbility<HeroInventory>();
        var items = inventory.Get();
        for (int i = 0; i < this.items.Length; i++) {
            if (i >= items.Length) {
                this.items[i].SetData(null);
                continue;
            }
            
            this.items[i].SetData(items[i].icon);
        }
    }

    void UpdateAttributeValues() {
        var attributes = hero.GetAbility<HeroAttributes>();
        hpText.text = $"{(int)attributes.Hp}/{(int)attributes.MaxHp}";
        energyText.text = $"{(int)attributes.Energy}/{(int)HeroTrait.MAX_ENERGY}";
        physicalDmgText.text = $"{(int)attributes.PhysicalDamage}";
        magicalDmgText.text = $"{(int)attributes.MagicalDamage}";
        armorText.text = $"{(int)attributes.Armor}";
        resistanceText.text = $"{(int)attributes.Resistance}";
        attackSpeedText.text = $"{(int)(attributes.AttackSpeed*100)}%";
        energyRegenEfficientText.text = $"{(int)(attributes.EnergyRegenEfficiency*100)}%";
        physicalPenetrationText.text = $"{(int)attributes.PhysicalPenetration}";
        magicalPenetrationText.text = $"{(int)attributes.MagicalPenetration}";
        criticalChanceText.text = $"{(int)(attributes.CriticalChance*100)}%";
        criticalDamageText.text = $"{(int)(attributes.CriticalDamage*100)}%";
        lifeStealText.text = $"{(int)(attributes.LifeSteal*100)}%";
        tenacityText.text = $"{(int)(attributes.Tenacity*100)}%";
    }

    void SwitchSkillDescription() {
        skillDescription.Switch();
    }
}