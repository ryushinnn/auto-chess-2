using System;
using System.Text.RegularExpressions;
using AutoChess.Core;
using Sirenix.OdinInspector;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>Authoring data of a synergy (a role or a realm): checkpoints, parameters, description.</summary>
    public abstract class DestinyConfig : ScriptableObject {
        public int[] checkpoints;
        [TableList(ShowIndexLabels = true)] public DestinyParam[] destinyParams;
        [TextArea(1, 100)] public string description;

        public abstract TraitId TraitId { get; }

        public abstract string GetName();

        public abstract Sprite GetIcon();

        public int GetCheckpointIndex(int current) {
            for (int i = checkpoints.Length - 1; i >= 0; i--) {
                if (current >= checkpoints[i]) return i;
            }

            return -1;
        }

        public string Description(int checkpoint) {
            var result = Reform(description, @"#\[(\d+)\]", match => {
                var index = int.Parse(match.Groups[1].Value);
                if (index >= 0 && index < destinyParams.Length) {
                    var param = destinyParams[index];
                    var val = Mathf.Abs(param.value);
                    return param.isPercentage ? $"{val * 100}%" : $"{val}";
                }

                return match.Value;
            });

            result = Reform(result, @"&\[(\d+)\]", match => {
                var index = int.Parse(match.Groups[1].Value);
                return index >= 0 && index < checkpoints.Length ? $"{checkpoints[index]}" : match.Value;
            });

            result = Reform(result, @"</?(\d+)>", match => {
                var isClosing = match.Value.StartsWith("</");
                var index = int.Parse(match.Groups[1].Value);
                if (index == checkpoint) {
                    return isClosing ? "</color>" : "<color=yellow>";
                }

                return "";
            });

            return result;
        }

        static string Reform(string des, string pattern, MatchEvaluator eval) {
            return Regex.Replace(des ?? string.Empty, pattern, eval);
        }
    }

    [Serializable]
    public class DestinyParam {
        [TableColumnWidth(170, resizable: false)]
        public string key;
        public float value;
        [TableColumnWidth(30, resizable: false), VerticalGroup("%"), LabelText("")]
        public bool isPercentage;
    }
}
