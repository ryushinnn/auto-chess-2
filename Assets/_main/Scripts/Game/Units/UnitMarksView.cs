using System.Collections.Generic;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>Effect marks above a unit (the visual half of the old HeroMark).</summary>
    public class UnitMarksView : UnitPart {
        [SerializeField] MarkHolder markHolderPrefab;
        [SerializeField] Transform markHolderParent;

        readonly List<MarkHolder> holders = new();

        public override void Initialize(UnitView owner) {
            base.Initialize(owner);
            foreach (Transform child in markHolderParent) {
                if (child.TryGetComponent(out MarkHolder holder)) {
                    holders.Add(holder);
                    holder.gameObject.SetActive(false);
                }
            }
        }

        public override void ResetAll(bool home) {
            foreach (var holder in holders) holder.gameObject.SetActive(false);
        }

        public void AddMark(int markId, string key, int stacks, float duration) {
            var holder = holders.Find(x => !x.gameObject.activeSelf);
            if (holder == null) {
                holder = Instantiate(markHolderPrefab, markHolderParent);
                holders.Add(holder);
            }
            holder.gameObject.SetActive(true);
            var icon = AssetDB.Instance.GetMarkIcon(key)?.value;
            if (float.IsPositiveInfinity(duration)) holder.SetData(markId, icon, stacks);
            else holder.SetData(markId, icon, stacks, duration);
        }

        public void RemoveMark(int markId) {
            foreach (var holder in holders) {
                if (holder.gameObject.activeSelf && holder.Id == markId) holder.gameObject.SetActive(false);
            }
        }
    }
}
