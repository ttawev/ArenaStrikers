// Player actions: kick, dash, lob, curve shot and the three hero abilities (hook, fire, teleport).
using System;
using static Arena.Sim.Field;

namespace Arena.Sim
{
    public partial class Match
    {
        // ---------- kick ----------
        public bool TryKick(SimPlayer p, float aimX, float aimY, float charge)
        {
            var b = Ball;
            if (p.KickCd > 0 || p.Stun > 0 || p.FireT > 0 || b.Hook != null || b.Z > LOW) return false;
            if (b.Owner != null && b.Owner != p) return false;
            if (b.Owner == null && M.Len(b.X - p.X, b.Y - p.Y) > R + BR + 18) return false;
            p.KickCd = 0.22f; b.Last = p; b.Spin = 0;
            if (b.Owner != p) p.GrabCd = Math.Max(p.GrabCd, 0.3f);
            float pow = T.kick * (0.7f + (T.charge - 0.7f) * charge);
            Rumble(p, charge > 0.6f ? 0.35f : 0.2f);
            if (b.Owner == p)
            { // kick from dribble: straight where you aim
                (float x, float y) a = (aimX != 0 || aimY != 0) ? M.Norm(aimX, aimY) : (p.Fx, p.Fy);
                if (a.x * p.Fx + a.y * p.Fy < -0.2f) a = (p.Fx, p.Fy);
                ReleaseBall(p, 0.3f);
                b.X = p.X + a.x * (R + BR + 2); b.Y = p.Y + a.y * (R + BR + 2);
                b.Vx = a.x * pow + p.Vx * 0.25f; b.Vy = a.y * pow + p.Vy * 0.25f;
                p.Flash = 0.15f;
                return true;
            }
            var toB = M.Norm(b.X - p.X, b.Y - p.Y);
            (float x, float y) aim = (aimX != 0 || aimY != 0) ? M.Norm(aimX, aimY) : (p.Fx, p.Fy);
            if (aim.x * toB.x + aim.y * toB.y < -0.2f) aim = toB; // can't kick backwards through yourself
            var dir = M.Norm(aim.x * 0.65f + toB.x * 0.35f, aim.y * 0.65f + toB.y * 0.35f);
            b.Vx = dir.x * pow + p.Vx * 0.25f; b.Vy = dir.y * pow + p.Vy * 0.25f;
            b.X = p.X + toB.x * (R + BR + 1); b.Y = p.Y + toB.y * (R + BR + 1);
            p.Flash = 0.15f;
            return true;
        }

        /// <summary>Where and how fast the ball would leave if this player kicked now (for the aim preview). Null if he can't.</summary>
        public (float x, float y, float vx, float vy)? KickLaunch(SimPlayer p, float ax, float ay, float charge)
        {
            var b = Ball;
            if (p.KickCd > 0 || p.Stun > 0 || p.FireT > 0 || b.Hook != null || b.Z > LOW || (b.Owner != null && b.Owner != p)) return null;
            float pow = T.kick * (0.7f + (T.charge - 0.7f) * charge);
            if (b.Owner == p)
            {
                (float x, float y) a = (ax != 0 || ay != 0) ? M.Norm(ax, ay) : (p.Fx, p.Fy);
                if (a.x * p.Fx + a.y * p.Fy < -0.2f) a = (p.Fx, p.Fy);
                return (p.X + a.x * (R + BR + 2), p.Y + a.y * (R + BR + 2), a.x * pow + p.Vx * 0.25f, a.y * pow + p.Vy * 0.25f);
            }
            if (M.Len(b.X - p.X, b.Y - p.Y) > R + BR + 18) return null;
            var toB = M.Norm(b.X - p.X, b.Y - p.Y);
            (float x, float y) aim = (ax != 0 || ay != 0) ? M.Norm(ax, ay) : (p.Fx, p.Fy);
            if (aim.x * toB.x + aim.y * toB.y < -0.2f) aim = toB;
            var dir = M.Norm(aim.x * 0.65f + toB.x * 0.35f, aim.y * 0.65f + toB.y * 0.35f);
            return (p.X + toB.x * (R + BR + 1), p.Y + toB.y * (R + BR + 1), dir.x * pow + p.Vx * 0.25f, dir.y * pow + p.Vy * 0.25f);
        }

        void TryDash(SimPlayer p, float dx, float dy)
        {
            if (p.DashCd > 0 || p.Stun > 0) return;
            (float x, float y) d = (dx != 0 || dy != 0) ? M.Norm(dx, dy) : (p.Fx, p.Fy);
            p.DashT = 0.17f; p.DashCd = T.dashCd; p.Vx = d.x * T.dashPow; p.Vy = d.y * T.dashPow;
        }

        // ---------- lob ----------
        // Lands about LobDist(charge) away; the apex grows with distance so long balls clear heads.
        public float LobDist(float c) => T.lobMin + (T.lobMax - T.lobMin) * c;
        (float vh, float vz) LobVel(float D)
        {
            float h = T.lobArc * (120 + 0.25f * D), vz = MathF.Sqrt(2 * GRAV * h), t = 2 * vz / GRAV;
            return (Math.Max(0, D) / t, vz);
        }
        public (float x, float y) LobAim(SimPlayer p, float ax, float ay)
        {
            (float x, float y) a = (ax != 0 || ay != 0) ? M.Norm(ax, ay) : (p.Fx, p.Fy);
            if (a.x * p.Fx + a.y * p.Fy < -0.2f) a = (p.Fx, p.Fy);
            return a;
        }
        public bool CanLob(SimPlayer p)
        {
            var b = Ball;
            if (p.KickCd > 0 || p.Stun > 0 || p.FireT > 0 || b.Hook != null || b.Z > LOW) return false;
            if (b.Owner != null) return b.Owner == p;
            return M.Len(b.X - p.X, b.Y - p.Y) <= R + BR + 18;
        }
        void LaunchLob(SimPlayer p, (float x, float y) a, float D)
        {
            var b = Ball;
            if (b.Owner == p) ReleaseBall(p, 0.45f); else p.GrabCd = 0.45f;
            p.KickCd = 0.25f; p.Flash = 0.15f; b.Last = p; b.Spin = 0;
            b.X = p.X + a.x * (R + BR + 2); b.Y = p.Y + a.y * (R + BR + 2);
            var v = LobVel(D - (R + BR + 2));
            b.Vx = a.x * v.vh; b.Vy = a.y * v.vh; b.Vz = v.vz; b.Z = 1;
            Rumble(p, 0.25f);
        }
        bool TryLob(SimPlayer p, float ax, float ay, float charge)
        {
            if (!CanLob(p)) { Denied(p); return false; }
            LaunchLob(p, LobAim(p, ax, ay), LobDist(charge));
            return true;
        }
        /// <summary>Where an airborne ball first touches the ground.</summary>
        public (float x, float y, float t) LandingPoint()
        {
            var b = Ball;
            float t = (b.Vz + MathF.Sqrt(b.Vz * b.Vz + 2 * GRAV * b.Z)) / GRAV;
            return (b.X + b.Vx * t, b.Y + b.Vy * t, t);
        }

        // ---------- curve shot (Вихрь) ----------
        bool TryCurve(SimPlayer p, float ax, float ay, float charge, int dir)
        {
            if (p.CurveCd > 0) { Denied(p); return false; }
            if (!TryKick(p, ax, ay, charge)) { Denied(p); return false; }
            Ball.Spin = dir * T.curveAmt; p.CurveCd = T.curveCd;
            Burst(Ball.X, Ball.Y, FxColor.Spin, 10, 0.7f);
            return true;
        }

        /// <summary>Dots along the path a curve shot would take now (aim preview). Fills 'output', returns count.</summary>
        public int CurvePath(SimPlayer p, float ax, float ay, float charge, int dir, (float x, float y)[] output)
        {
            var L = KickLaunch(p, ax, ay, charge);
            if (L == null) return 0;
            float x = L.Value.x, y = L.Value.y, vx = L.Value.vx, vy = L.Value.vy, spin = dir * T.curveAmt, h = 1f / 30;
            int n = 0;
            for (int i = 0; i < 45 && n < output.Length; i++)
            {
                (vx, vy) = CurveTurn(vx, vy, spin, h); spin *= MathF.Exp(-T.curveDecay * h);
                float f = MathF.Exp(-T.friction * h); vx *= f; vy *= f; x += vx * h; y += vy * h;
                if (x < BR || x > FW - BR || y < -NET || y > FH + NET) break;
                if (i % 3 == 2) output[n++] = (x, y);
            }
            return n;
        }

        // ---------- hook (Багор) ----------
        bool UseHook(SimPlayer p, float ax, float ay)
        {
            if (p.HookCd > 0 || p.Stun > 0 || p.FireT > 0 || Hooks.Exists(h => h.Owner == p)) { Denied(p); return false; }
            (float x, float y) d = (ax != 0 || ay != 0) ? M.Norm(ax, ay) : (p.Fx, p.Fy);
            Hooks.Add(new Hook { Owner = p, X = p.X + d.x * (R + 4), Y = p.Y + d.y * (R + 4), Dx = d.x, Dy = d.y });
            p.HookCd = T.hookCd; p.Flash = 0.12f; Rumble(p, 0.15f);
            return true;
        }

        void StepHooks(float dt)
        {
            var b = Ball;
            foreach (var h in Hooks)
            {
                var o = h.Owner;
                if (!h.Back)
                {
                    float s = T.hookSpd * dt; h.X += h.Dx * s; h.Y += h.Dy * s; h.Dist += s;
                    SimPlayer v = null;
                    foreach (var q in Enemies(o)) if (M.Len(q.X - h.X, q.Y - h.Y) < R + 8) { v = q; break; }
                    if ((b.Owner == null || b.Owner.Team != o.Team) && b.Hook == null && b.Z < HEAD && M.Len(b.X - h.X, b.Y - h.Y) < BR + 11)
                    {
                        b.Vz = 0;
                        if (b.Owner != null) ReleaseBall(b.Owner, 0.6f);
                        b.Hook = h; h.HasBall = true; b.Spin = 0; h.Back = true; b.Fire = 0;
                        Burst(h.X, h.Y, FxColor.Mist, 8, 0.6f); Rumble(o, 0.25f);
                    }
                    else if (v != null)
                    {
                        h.Victim = v; h.Back = true; v.Charging = false; v.Charge = 0; v.DashT = 0;
                        if (v.FireT > 0) { v.FireT = 0; v.FireCd = T.fireCd * 0.3f; }
                        Burst(h.X, h.Y, FxColor.Mist, 12, 0.7f); Shake(4); Rumble(o, 0.3f); Rumble(v, 0.5f);
                    }
                    if (h.Dist >= T.hookRange || h.X < 0 || h.X > FW || h.Y < 0 || h.Y > FH) h.Back = true;
                }
                else
                {
                    float dx = o.X - h.X, dy = o.Y - h.Y, d = M.Len(dx, dy), s = T.hookSpd * 1.1f * dt;
                    float nx = -o.Fx, ny = -o.Fy; if (d > 0) { nx = dx / d; ny = dy / d; }
                    float stop = h.Victim != null ? R * 2 + 4 : h.HasBall ? R + BR + 1 : R;
                    if (d - s <= stop)
                    {
                        h.Done = true;
                        if (h.HasBall)
                        {
                            b.Hook = null; b.Z = b.Vz = 0; b.X = o.X - nx * (R + BR + 1); b.Y = o.Y - ny * (R + BR + 1); b.Vx = o.Vx; b.Vy = o.Vy;
                            if (o.Stun <= 0 && b.Owner == null) { o.Fx = -nx; o.Fy = -ny; TakeBall(o); }
                        }
                        if (h.Victim != null)
                        {
                            var v = h.Victim;
                            v.X = o.X - nx * stop; v.Y = o.Y - ny * stop; v.Vx = v.Vy = 0; v.Stun = Math.Max(v.Stun, T.hookStun);
                            if (T.hookStun > 0) v.Charging = false;
                        }
                    }
                    else
                    {
                        h.X += nx * s; h.Y += ny * s;
                        if (h.HasBall) { b.X = h.X; b.Y = h.Y; b.Vx = nx * s / dt; b.Vy = ny * s / dt; b.Z = Math.Max(0, b.Z - 300 * dt); }
                        if (h.Victim != null) { var v = h.Victim; v.X = h.X; v.Y = h.Y; v.Vx = v.Vy = 0; v.Stun = Math.Max(v.Stun, 0.12f); }
                    }
                }
            }
            Hooks.RemoveAll(h => h.Done);
        }

        // ---------- fire super-kick (Жар) ----------
        public bool FireReady(SimPlayer p)
        {
            var b = Ball;
            if (b.Hook != null || b.Z > LOW) return false;
            if (b.Owner == p) return true;
            if (b.Owner != null) return false;
            return M.Len(b.X - p.X, b.Y - p.Y) < R + BR + 26;
        }
        bool UseFire(SimPlayer p)
        {
            if (p.FireCd > 0 || p.Stun > 0 || p.FireT > 0 || !FireReady(p)) { Denied(p); return false; }
            p.FireT = T.fireDelay; p.Charging = false; p.Charge = 0; Rumble(p, 0.2f);
            return true;
        }
        void ReleaseFire(SimPlayer p)
        {
            var b = Ball;
            p.FireT = 0;
            if (p.Stun > 0 || !FireReady(p)) { p.FireCd = T.fireCd * 0.3f; return; }
            float dx = p.Fx, dy = p.Fy;
            if (b.Owner == p) ReleaseBall(p, 0.4f); else p.GrabCd = 0.4f;
            b.X = p.X + dx * (R + BR + 2); b.Y = p.Y + dy * (R + BR + 2);
            b.Vx = dx * T.firePow; b.Vy = dy * T.firePow; b.Fire = 1.8f; b.FireBy = p; b.Last = p; b.Spin = 0; b.FireGrace = 0.2f;
            p.FireCd = T.fireCd; p.Flash = 0.2f; Shake(6); Rumble(p, 0.6f);
            Burst(b.X, b.Y, FxColor.Fire, 16, 1.2f);
        }

        // ---------- teleport (Миг) ----------
        bool UseTp(SimPlayer p)
        {
            if (p.TpBackT > 0)
            { // second press within the window: return to the start point
                if (Ball.Owner == p || p.Stun > 0) { Denied(p); return false; }
                Burst(p.X, p.Y, FxColor.Blink, 12); p.X = p.TpFx; p.Y = p.TpFy; p.TpBackT = 0; Burst(p.X, p.Y, TeamColor(p), 16); Rumble(p, 0.2f);
                return true;
            }
            if (p.TpCd > 0 || p.Stun > 0 || Ball.Owner == p || p.FireT > 0) { Denied(p); return false; }
            p.TpFx = p.X; p.TpFy = p.Y; Burst(p.X, p.Y, FxColor.Blink, 14);
            p.X = M.Clamp(p.X + p.Fx * T.tpDist, R, FW - R); p.Y = M.Clamp(p.Y + p.Fy * T.tpDist, R, FH - R);
            p.TpBackT = T.tpBack; p.TpCd = T.tpCd; Burst(p.X, p.Y, TeamColor(p), 16); Rumble(p, 0.2f);
            return true;
        }
    }
}
