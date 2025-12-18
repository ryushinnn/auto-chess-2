using System.Collections.Generic;

public class PriorityQueue<T> {
    public int Count => queue.Count;
    
    List<(T element, int priority)> queue = new ();

    public void Enqueue(T element, int priority) {
        queue.Add((element, priority));
        queue.Sort((a,b)=>a.priority.CompareTo(b.priority));
    }

    public T Dequeue() {
        var item = queue[0];
        queue.RemoveAt(0);
        return item.element;
    }

    public bool Contains(T element) {
        foreach (var (e, _) in queue) {
            if (EqualityComparer<T>.Default.Equals(e,element)) return true;
        }

        return false;
    }

    public void Clear() {
        queue.Clear();
    }
}