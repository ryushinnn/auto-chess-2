using System;
using System.Text.RegularExpressions;
using AutoChess.Core;
using AutoChess.Core.Combat;
using RExt.Extensions;
using Sirenix.OdinInspector;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>
    /// Authoring data of a hero (stats, skill numbers, model and icons). The simulation reads it through
    /// <see cref="GameDataBuilder"/>; the views use the visual fields directly.
    /// </summary>
    [CreateAssetMenu(menuName = "Hero/Trait")]
    public class HeroTrait : ScriptableObject {
        [FoldoutGroup("Identity"), StringDropdown(typeof(HeroIds))] public string id;
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
        [Tooltip("Energy at the start of a battle (assassins / sorcerers open with their skill sooner).")]
        [FoldoutGroup("Stats"), BoxGroup("Stats/Unmodifiable")] public float startingEnergy;
        [FoldoutGroup("Stats"), BoxGroup("Stats/Unmodifiable")] public int attackRange;

        [FoldoutGroup("Evolution")] public Evolution[] evolutions;

        [FoldoutGroup("Skill")] public string skillName;
        [FoldoutGroup("Skill")] public bool unstoppable;
        [FoldoutGroup("Skill"), TableList(ShowIndexLabels = true)] public HeroSkillParam[] skillParams;
        [FoldoutGroup("Skill"), TextArea(1, 100)] public string passiveDescription;
        [FoldoutGroup("Skill"), TextArea(1, 100)] public string activeDescription;
        [FoldoutGroup("Skill")] public string[] specialKeys;

        // Same constant as GameRules.DamageReductionConstant, for the inspector preview only.
        const float DamageReductionConstant = 100;

        [BoxGroup("Test"), SerializeField, ReadOnly] float dps;
        [BoxGroup("Test"), SerializeField, ReadOnly, LabelText("Physical Reduction (%)")] float physicalReduction;
        [BoxGroup("Test"), SerializeField, ReadOnly, LabelText("Magical Reduction (%)")] float magicalReduction;

        void OnValidate() {
            dps = (physicalDamage + magicalDamage) * attackSpeed;
            physicalReduction = 100 * (1 - DamageReductionConstant / (DamageReductionConstant + armor));
            magicalReduction = 100 * (1 - DamageReductionConstant / (DamageReductionConstant + resistance));

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

        static string ReformDescription(string des, MatchEvaluator eval) {
            const string pattern = @"#\[(\d+)\]";
            return Regex.Replace(des ?? string.Empty, pattern, eval);
        }

#if UNITY_EDITOR
        [Button]
        void AddSkillParam(int index) {
            var arr = new HeroSkillParam[skillParams.Length + 1];
            for (int i = arr.Length - 1; i >= 0; i--) {
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
                return i >= index ? $"#[{i + 1}]" : match.Value;
            };

            passiveDescription = ReformDescription(passiveDescription, eval);
            activeDescription = ReformDescription(activeDescription, eval);
        }

        [Button]
        void RemoveSkillParam(int index) {
            if (skillParams.Length <= 1) return;

            var arr = new HeroSkillParam[skillParams.Length - 1];
            for (int i = 0; i < arr.Length; i++) {
                arr[i] = i < index ? skillParams[i] : skillParams[i + 1];
            }

            skillParams = arr;

            MatchEvaluator eval = match => {
                var i = int.Parse(match.Groups[1].Value);
                return i > index ? $"#[{i - 1}]" : match.Value;
            };

            passiveDescription = ReformDescription(passiveDescription, eval);
            activeDescription = ReformDescription(activeDescription, eval);
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
        [TableColumnWidth(170, resizable: false)]
        public string key;
        public float value;
        [TableColumnWidth(30, resizable: false), VerticalGroup("%"), LabelText("")]
        public bool isPercentage;
    }
}
