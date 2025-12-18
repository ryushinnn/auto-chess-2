using System;
using System.Text.RegularExpressions;
using RExt.Extensions;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(menuName = "Hero/Trait")]
public class HeroTrait : ScriptableObject {
    [FoldoutGroup("Identity"), StringDropdown(typeof(HeroId))] public string id;
    [FoldoutGroup("Identity")] public new string name;
    [FoldoutGroup("Identity")] public string subName;
    [FoldoutGroup("Identity")] public bool summoned;
    [FoldoutGroup("Identity"), HideIf("summoned")] public Realm realm;
    [FoldoutGroup("Identity"), HideIf("summoned")] public Role role;
    [FoldoutGroup("Identity"), HideIf("summoned")] public Reputation reputation;
    [FoldoutGroup("Identity")] public bool moveable;
    
    [FoldoutGroup("Asset")] public Mecanim mecanim;
    [FoldoutGroup("Asset"), PreviewField(ObjectFieldAlignment.Left), HideIf("summoned")] public Sprite thumbnail;
    [FoldoutGroup("Asset"), PreviewField(ObjectFieldAlignment.Left)] public Sprite skillIcon;
    
    [FoldoutGroup("Stats"), BoxGroup("Stats/Basic")] public float maxHp;
    [FoldoutGroup("Stats"), BoxGroup("Stats/Basic")] public float physicalDamage;
    [FoldoutGroup("Stats"), BoxGroup("Stats/Basic")] public float magicalDamage;
    [FoldoutGroup("Stats"), BoxGroup("Stats/Basic")] public float armor;
    [FoldoutGroup("Stats"), BoxGroup("Stats/Basic")] public float resistance;
    [FoldoutGroup("Stats"), BoxGroup("Stats/Basic")] public float attackSpeed;
    [FoldoutGroup("Stats"), BoxGroup("Stats/Basic")] public float movementSpeed;

    [FoldoutGroup("Stats"), BoxGroup("Stats/Unmodifiable")] public float energyRegenPerAttack;
    [FoldoutGroup("Stats"), BoxGroup("Stats/Unmodifiable")] public float energyRegenPerHit;
    [FoldoutGroup("Stats"), BoxGroup("Stats/Unmodifiable")] public int attackRange;
    
    [FoldoutGroup("Evolution")] public Evolution[] evolutions;
    
    [FoldoutGroup("Skill")] public string skillName;
    [FoldoutGroup("Skill")] public bool unstoppable;
    [FoldoutGroup("Skill"), TableList(ShowIndexLabels = true)] public HeroSkillParam[] skillParams;
    [FoldoutGroup("Skill"), TextArea(1,100)] public string passiveDescription;
    [FoldoutGroup("Skill"), TextArea(1,100)] public string activeDescription;
    [FoldoutGroup("Skill")] public string[] specialKeys;

    public const float BASE_CRITICAL_CHANCE = 0.15f;
    public const float BASE_CRITICAL_DAMAGE = 1.5f;
    public const float BASE_ENERGY_REGEN_EFFICIENCY = 1;
    public const float BASE_PENETRATION = 0;
    public const float BASE_LIFE_STEAL = 0;
    public const float BASE_TENACITY = 0;
    public const float MAX_ENERGY = 100;
    public const float DAMAGE_REDUCTION_CONSTANT = 100;
    public const float MAX_CRITICAL_CHANCE = 1;
    public const float MAX_PENETRATION = 0.7f;
    public const float MAX_LIFE_STEAL = 1;
    public const float MAX_TENACITY = 0.5f;
    public const float ANTI_HEAL_EFFICIENCY = 0.5f;
    public const float MIN_ARMOR_AND_RESISTANCE = 1;
    public const float MIN_DAMAGE_DEALT = 1;
    public const float MIN_DAMAGE = 1;
    public const float MIN_ATTACK_SPEED = 0.1f;

    [BoxGroup("Test"), SerializeField, ReadOnly] float dps;
    [BoxGroup("Test"), SerializeField, ReadOnly, LabelText("Physical Reduction (%)")] float physicalReduction;
    [BoxGroup("Test"), SerializeField, ReadOnly, LabelText("Magical Reduction (%)")] float magicalReduction;

    void OnValidate() {
        var outputDamage = physicalDamage + magicalDamage;
        dps = outputDamage * attackSpeed;
        physicalReduction = 100 * (1 - DAMAGE_REDUCTION_CONSTANT / (DAMAGE_REDUCTION_CONSTANT + armor));
        magicalReduction = 100 * (1 - DAMAGE_REDUCTION_CONSTANT / (DAMAGE_REDUCTION_CONSTANT + resistance));
        
        if (summoned) {
            realm = Realm.None;
            role = Role.None;
            reputation = Reputation.None;
        }
    }

    public string DisplayName() {
        return name + (subName.IsValid() ? "\n" + subName : "");
    }

    public string SkillDescription() {
        MatchEvaluator eval = match => {
            var index = int.Parse(match.Groups[1].Value);
            if (index >= 0 && index < skillParams.Length) {
                var param = skillParams[index];
                var val = Mathf.Abs(param.value);
                return param.isPercentage ? $"{val * 100}%" : $"{val}";
            }

            return match.Value;
        };
        
        var header = $"<uppercase>{skillName}</uppercase>\n\n";
        var attack = passiveDescription.IsValid() ? $"<color=grey><i>Nội tại: {ReformDescription(passiveDescription, eval)}</color></i>\n\n" : "";
        var skill = ReformDescription(activeDescription, eval);
        var footer = unstoppable ? "\n\n<color=grey><i>(Khi đang sử dụng kỹ năng, không thể bị cản phá)</color></i>" : "";
        return $"{header}{attack}{skill}{footer}";
    }

    string ReformDescription(string des, MatchEvaluator eval) {
        const string pattern = @"#\[(\d+)\]";
        return Regex.Replace(des, pattern, eval);
    }

#if UNITY_EDITOR
    [Button]
    void AddSkillParam(int index) {
        var arr = new HeroSkillParam[skillParams.Length + 1];
        for (int i = arr.Length-1; i >=0; i--) {
            if (i == index) {
                arr[i] = new HeroSkillParam();
            }
            else if (i < index) {
                arr[i] = skillParams[i];
            }
            else {
                arr[i] = skillParams[i - 1];
            }
        }

        skillParams = arr;

        MatchEvaluator eval = match => {
            var i = int.Parse(match.Groups[1].Value);
            if (i >= index) {
                return $"#[{i + 1}]";
            }

            return match.Value;
        };

        passiveDescription = ReformDescription(passiveDescription, eval);
        activeDescription = ReformDescription(activeDescription, eval);
    }
    
    [Button]
    void RemoveSkillParam(int index) {
        if (skillParams.Length <= 1) return;
        
        var arr = new HeroSkillParam[skillParams.Length - 1];
        for (int i = 0; i < arr.Length; i++) {
            if (i < index) {
                arr[i] = skillParams[i];
            }
            else {
                arr[i] = skillParams[i + 1];
            }
        }

        skillParams = arr;

        MatchEvaluator eval = match => {
            var i = int.Parse(match.Groups[1].Value);
            if (i > index) {
                return $"#[{i - 1}]";
            }

            return match.Value;
        };

        passiveDescription = ReformDescription(passiveDescription, eval);
        activeDescription = ReformDescription(activeDescription, eval);
    }

    [Button]
    void test() {
        
    }
#endif
}

[Serializable]
public class Evolution {
    public HeroRank rank;
    public AttributeModifier[] modifiers;
}

[Serializable]
public class HeroSkillParam {
    [TableColumnWidth(170, resizable:false)] 
    public string key;
    public float value;
    [TableColumnWidth(30, resizable:false), VerticalGroup("%"), LabelText("")] 
    public bool isPercentage;
}

public static class HeroId {
    public const string _Dummy = "_Dummy";
    public const string Aatrox_Dark = "Aatrox_Dark";
    public const string Aatrox_Light = "Aatrox_Light";
    public const string Aatrox_TrueForm = "Aatrox_TrueForm";
    public const string Akali = "Akali";
    public const string Ashe = "Ashe";
    public const string Caitlyn = "Caitlyn";
    public const string Irelia = "Irelia";
    public const string Jinx = "Jinx";
    public const string Katarina = "Katarina";
    public const string Malphite = "Malphite";
    public const string MissFortune = "MissFortune";
    public const string Morgana = "Morgana";
    public const string Teemo = "Teemo";
    public const string Tristana = "Tristana";
    public const string Yasuo = "Yasuo";
    public const string Yone = "Yone";
    public const string Zed = "Zed";
}

public enum Realm {
    None,
    Mortal,
    Divine,
    Nether,
    Mecha,
    Chaos
}

[Flags]
public enum Role {
    None = 0x00,
    Duelist = 0x01,
    Sorcerer = 0x02,
    Marksman = 0x04,
    Assassin = 0x08,
    Bruiser = 0x10,
    Cultist = 0x20,
}

public enum Reputation {
    None,
    Unknown,
    Elite,
    Legendary,
}