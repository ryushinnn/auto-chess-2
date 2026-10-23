using AutoChess.Core;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>Flying VFX that follows a simulated projectile (the hit itself is resolved in Core).</summary>
    public class Projectile : Vfx {
        IGameSession session;
        Core.Combat.Projectile model;

        public void Track(IGameSession gameSession, Core.Combat.Projectile projectile) {
            session = gameSession;
            model = projectile;
            transform.position = BoardSpace.BattleToWorld(projectile.Position);
        }

        void Update() {
            if (model == null || session == null) return;
            var position = BoardSpace.BattleToWorld(Vec2.Lerp(model.PreviousPosition, model.Position, session.TickProgress));
            var direction = BoardSpace.BattleToWorld(model.Target.Position) - position;
            if (direction.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(direction);
            transform.position = position;
        }

        void OnDisable() {
            model = null;
            session = null;
        }
    }
}
