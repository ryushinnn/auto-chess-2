using System;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;

public class HeroMark : HeroAbility {
    [SerializeField] MarkHolder markHolderPrefab;
    [SerializeField] Transform markHolderParent;
    [SerializeField, ReadOnly] List<MarkGroup> markGroups = new();

    List<MarkHolder> markHolders = new();

    public override void Initialize(Hero hero) {
        base.Initialize(hero);
        foreach (Transform child in markHolderParent) {
            if (child.TryGetComponent(out MarkHolder mh)) {
                markHolders.Add(mh);
                mh.gameObject.SetActive(false);
            }
        }
    }

    public override void ResetAll() {
        foreach (var holder in markHolders) {
            holder.gameObject.SetActive(false);
        }
        markGroups.Clear();
    }

    public void AddMark(Mark mark) {
        var group = markGroups.Find(g => g.key == mark.key);
        if (group == null) {
            group = new MarkGroup(mark.key);
            markGroups.Add(group);
        }

        group.marks.RemoveAll(x => x.SameAs(mark));
        group.marks.Add(mark);

        var markHolder = markHolders.Find(x => !x.gameObject.activeSelf);
        if (markHolder == null) {
            markHolder = Instantiate(markHolderPrefab, markHolderParent);
            markHolders.Add(markHolder);
        }
        
        markHolder.gameObject.SetActive(true);
        if (mark.permanent) {
            markHolder.SetData(mark.id, AssetDB.Instance.GetMarkIcon(mark.key).value, mark.stacks);
        }
        else {
            markHolder.SetData(mark.id, AssetDB.Instance.GetMarkIcon(mark.key).value, mark.stacks, mark.duration);
        }
    }

    public void RemoveMark(Mark mark) {
        for (int i= markGroups.Count-1; i>=0; i--) {
            if (markGroups[i].marks.Contains(mark)) {
                markGroups[i].marks.Remove(mark);
                markHolders.ForEach(x => {
                    if (x.Id == mark.id) {
                        x.gameObject.SetActive(false);
                    }
                });
                
                if (markGroups[i].marks.Count == 0) {
                    markGroups.RemoveAt(i);
                }
                return;
            }
        }
    }

    public Mark GetMark(string key, Hero owner) {
        var group = markGroups.Find(g => g.key == key);
        return group?.marks.Find(m => m.owner == owner);
    }
}

[Serializable]
public class MarkGroup {
    public string key;
    public List<Mark> marks;

    public MarkGroup(string key) {
        this.key = key;
        marks = new List<Mark>();
    }
}

[Serializable]
public class Mark {
    public Hero owner;
    public string id;
    public string key;
    public int stacks;
    [HideInInspector] public float duration;
    [HideInInspector] public bool permanent;
    
    public static Mark Create(Hero owner, string key, int stacks, float duration) {
        return new Mark {
            id = Guid.NewGuid().ToString(),
            owner = owner,
            key = key,
            stacks = stacks,
            duration = duration,
            permanent = false
        };
    }

    public static Mark Create(Hero owner, string key, int stacks) {
        return new Mark {
            id = Guid.NewGuid().ToString(),
            owner = owner,
            key = key,
            stacks = stacks,
            duration = Mathf.Infinity,
            permanent = true,
        };
    }
    
    public bool SameAs(Mark other) {
        return key == other.key && owner == other.owner;
    }
}