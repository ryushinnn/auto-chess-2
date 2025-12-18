using System;
using System.Collections;
using System.Collections.Generic;
using RExt.Extensions;
using Sirenix.OdinInspector;
using UnityEngine;

public class BodyParts : MonoBehaviour {
    [SerializeField, TableList(ShowIndexLabels = true)] BodyPart[] parts;
    [SerializeField, TableList(ShowIndexLabels = true)] Bone[] bones;

    Dictionary<string, GameObject> dic = new();
    Dictionary<string, Coroutine> coroutines = new();

    void Awake() {
        foreach (var part in parts) {
            part.obj.SetActive(part.enableAtStart);
            dic.Add(part.name, part.obj);
        }
    }

    public void SetBodyParts(float delay, params (string, bool)[] datas) {
        foreach (var data in datas) {
            if (delay > 0) {
                if (coroutines.TryGetValue(data.Item1, out var c)) {
                    if (c != null) StopCoroutine(c);
                    coroutines.Remove(data.Item1);
                }
                coroutines.Add(data.Item1, StartCoroutine(DoSetBodyPart(delay, data)));
            } else {
                SetBodyPart(data);
            }
        }
    }

    IEnumerator DoSetBodyPart(float delay, (string,bool) data) {
        yield return new WaitForSeconds(delay);
        SetBodyPart(data);
    }

    void SetBodyPart((string, bool) data) {
        if (dic.TryGetValue(data.Item1, out var part)) {
            part.SetActive(data.Item2);
        }
    }

    public Transform GetBone(string name) {
        return bones.Find(x => x.name == name).obj;
    }
}

[Serializable]
public class BodyPart {
    public bool enableAtStart;
    public string name;
    public GameObject obj;
}

[Serializable]
public class Bone {
    public string name;
    public Transform obj;
}