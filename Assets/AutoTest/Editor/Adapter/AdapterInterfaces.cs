using System.Collections.Generic;

namespace AutoTest
{
    // Game plug-in points (PROTOCOL.md §2). Implement IAutoTestGameAdapter on ONE concrete class with a parameterless
    // constructor in the game's own editor code (Assembly-CSharp-Editor, e.g. Assets/_main/Editor/AutoTest/); it is
    // found through TypeCache and created once per domain. The optional interfaces go on the same class.
    // Every member is called on the editor main thread; an exception becomes a 500 reply, never a dead server.

    public interface IAutoTestGameAdapter
    {
        /// <summary>Echoed in /status and /state.</summary>
        string GameId { get; }

        /// <summary>False while the game scene / session is not ready.</summary>
        bool IsAvailable();

        /// <summary>READ-ONLY snapshot, JSON-serializable (dictionaries, lists, DTOs; Unity structs/objects are fine).</summary>
        object GetState();
    }

    /// <summary>Optional: named test hooks served by /game-op.</summary>
    public interface IAutoTestGameOps
    {
        /// <summary>Names listed by /game-op?op=list.</summary>
        IReadOnlyList<string> Ops { get; }

        /// <summary>Throw ArgumentException for bad args (400). Return any JSON-serializable result.</summary>
        object GameOp(string op, IReadOnlyDictionary<string, string> args);
    }

    /// <summary>Optional: "must always hold" rules served by /invariants.</summary>
    public interface IAutoTestInvariants
    {
        IReadOnlyList<string> Rules { get; }

        /// <summary>only == null -> all rules. Read-only.</summary>
        List<InvariantFinding> Check(ISet<string> only);
    }

    public sealed class InvariantFinding
    {
        public string rule, severity /* high|medium|low */, detail;
        public object data;
    }

    /// <summary>Optional: things a scenario may click/drag by id, served by /targets.</summary>
    public interface IAutoTestTargets
    {
        List<TargetInfo> GetTargets();
    }

    public sealed class TargetInfo
    {
        public string id, kind, label, path;  // id is stable for the current state, e.g. "shop:2", "bench:0", "board:1,3"
        public float[] center;                 // screen px, bottom-left origin
        public float[] rect;                   // xMin,yMin,xMax,yMax screen px
        public bool visible, interactable;
        public Dictionary<string, object> data;
    }
}
