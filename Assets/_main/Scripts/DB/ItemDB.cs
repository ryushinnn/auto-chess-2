using RExt.Extensions;
using RExt.Patterns.Singleton;
using UnityEngine;

[CreateAssetMenu(fileName = "ItemDB", menuName = "DB/ItemDB")]
public class ItemDB : ScriptableObjectSingleton<ItemDB> {
    [SerializeField] Item[] rawItems;
    [SerializeField] Item[] forgedItems;
    
    public Item FindForgedItem(Item ingredient0, Item ingredient1) {
        return forgedItems.Find(x => (
                x.ingredients[0] == ingredient0 && x.ingredients[1] == ingredient1)
                || (x.ingredients[0] == ingredient1 && x.ingredients[1] == ingredient0
            ));
    }

    public Item GetRandomRawItem() {
        return rawItems[Random.Range(0, rawItems.Length)];
    }
    
    public Item GetRandomForgedItem() {
        return forgedItems[Random.Range(0, forgedItems.Length)];
    }
}