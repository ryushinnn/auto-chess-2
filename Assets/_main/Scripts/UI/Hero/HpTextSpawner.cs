using System;
using System.Collections.Generic;
using DG.Tweening;
using RExt.Patterns.Singleton;
using RExt.Patterns.ObjectPool;
using UnityEngine;
using Random = UnityEngine.Random;

public class HpTextSpawner : Singleton<HpTextSpawner> {
    [SerializeField] HpText hpTextPrefab;
    
    Queue<HpText> pool = new();

    const float POOL_INIT_SIZE = 10;
    const float LEFT_POS = -0.5f;
    const float RIGHT_POS = 0.5f;
    const float POS_Y_GAP = 0.5f;
    
    protected override void OnAwake() {
        for (int i = 0; i < POOL_INIT_SIZE; i++) {
            var hpText = Instantiate(hpTextPrefab, transform);
            hpText.gameObject.SetActive(false);
            pool.Enqueue(hpText);
        }    
    }

    public HpText SpawnHpTextAsDamage(Transform parent, Damage damage) {
        var hpText = SpawnHpText(parent, new Vector3(RIGHT_POS,0,0));
        hpText.transform.SetAsLastSibling();
        hpText.SetAsDamage(damage.value, damage.type, damage.crit);
        return hpText;
    }

    public HpText[] SpawnHpTextAsDamage(Transform parent, List<Damage> damages) {
        var hpTexts = new HpText[damages.Count];
        for (int i = 0; i < damages.Count; i++) {
            var hpText = SpawnHpText(parent, new Vector3(RIGHT_POS, POS_Y_GAP * i,0));
            hpText.transform.SetAsLastSibling();
            hpText.SetAsDamage(damages[i].value, damages[i].type, damages[i].crit);
            hpTexts[i] = hpText;
        }

        return hpTexts;
    }
    
    public HpText SpawnHpTextAsHeal(Transform parent, float amount) {
        var hpText = SpawnHpText(parent, new Vector3(LEFT_POS,0,0));
        hpText.transform.SetAsLastSibling();
        hpText.SetAsHeal(amount);
        return hpText;
    }

    HpText SpawnHpText(Transform parent, Vector3 pos) {
        HpText hpText;
        if (pool.Count > 0) {
            hpText = pool.Dequeue();
            hpText.transform.SetParent(parent);
        }
        else {
            hpText = Instantiate(hpTextPrefab, parent);
        }
        hpText.gameObject.SetActive(true);
        hpText.transform.localPosition = pos;
        hpText.transform.localEulerAngles = Vector3.zero;
        DOVirtual.DelayedCall(1, () => {
            hpText.transform.SetParent(transform);
            hpText.gameObject.SetActive(false);
            pool.Enqueue(hpText);
        });
        return hpText;
    }
}