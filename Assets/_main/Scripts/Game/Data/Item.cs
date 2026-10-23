using System.Text.RegularExpressions;
using Sirenix.OdinInspector;
using UnityEngine;

namespace AutoChess.Game {
    [CreateAssetMenu(menuName = "Item")]
    public class Item : ScriptableObject {
        public new string name;
        [PreviewField] public Sprite icon;
        public Item[] ingredients;
        public AttributeModifier[] modifiers;
        [Tooltip("Numbers of the passive (Core/Combat/ItemEffects.cs reads them by index); #[i] in the description.")]
        [TableList(ShowIndexLabels = true)] public HeroSkillParam[] itemParams;
        [Tooltip("Passive text (TMP rich text, <sprite name=...> icons, #[i] = itemParams[i]). Empty for components.")]
        [TextArea(2, 20)] public string description;

        /// <summary>Stable id used by the simulation: the asset name (e.g. "_armor", "armor+crit").</summary>
        public string Id => ((Object)this).name;

        public bool IsForgedItem() {
            return ingredients != null && ingredients.Length == 2;
        }

        /// <summary>The passive text with its numbers filled in.</summary>
        public string Description() {
            if (string.IsNullOrEmpty(description)) return string.Empty;
            return Regex.Replace(description, @"#\[(\d+)\]", match => {
                var index = int.Parse(match.Groups[1].Value);
                if (itemParams == null || index < 0 || index >= itemParams.Length) return match.Value;
                var param = itemParams[index];
                var value = Mathf.Abs(param.value);
                return param.isPercentage ? $"{value * 100:0.#}%" : $"{value:0.##}";
            });
        }
    }
}
