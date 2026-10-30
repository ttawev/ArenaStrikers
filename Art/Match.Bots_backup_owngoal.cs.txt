// Bots on both teams. Same roles as the prototype: whoever reaches the ball first goes for it,
// the rest get open (when we have the ball) or guard the goal and mark (when we don't).
using System;
using System.Collections.Generic;
using static Arena.Sim.Field;

namespace Arena.Sim
{
    public partial class Match
    {
        BotLevel LevelOf(SimPlayer b) => BotLevel.Levels[b.Team == Team.Home ? 1 : Difficulty]; // teammates play at medium

        (float x, float y) AimWithError(float vx, float vy, float e) => M.Rot(vx, vy, (Rand() - 0.5f) * e * 2);

        void BotStep(SimPlayer b, float dt)
        {
            var P = b.Brain; var D = LevelOf(b);
            P.T -= dt;
            if (P.T <= 0) { P.T = D.React * (0.7f + Rand() * 0.6f); Think(b); }
            if (P.WantKick)
            {
                b.Charging = true; b.Charge = Math.Min(1, b.Charge + dt / 0.7f);
                if (b.Charge >= P.ChargeTo)
                {
                    if (P.Curve != 0) { TryCurve(b, P.Kx, P.Ky, b.Charge, P.Curve); P.Curve = 0; }
                    else TryKick(b, P.Kx, P.Ky, b.Charge);
                    b.Charging = false; b.Charge = 0; P.WantKick = false;
                }
            }
            else { b.Charging = false; b.Charge = 0; }
            StepPlayer(b, P.Mx, P.My, D.Spd, dt);
        }

        /// <summary>Earliest point on the ball's path this player can reach, and when.</summary>
        public (float t, float x, float y) Intercept(SimPlayer p)
        {
            var b = Ball;
            if (b.Owner != null) return (M.Len(b.X - p.X, b.Y - p.Y) / T.speed, b.X, b.Y);
            float x = b.X, y = b.Y, vx = b.Vx, vy = b.Vy, z = b.Z, vz = b.Vz, spin = b.Spin, h = 1f / 20, sp = T.speed * 0.95f;
            for (int i = 1; i <= 50; i++)
            {
                if (z > 0 || vz > 0) { vz -= GRAV * h; z += vz * h; if (z < 0) { z = 0; vz = 0; } }
                else
                {
                    float f = MathF.Exp(-T.friction * h); vx *= f; vy *= f;
                    (vx, vy) = CurveTurn(vx, vy, spin, h); spin *= MathF.Exp(-T.curveDecay * h);
                }
                x = M.Clamp(x + vx * h, BR, FW - BR); y = M.Clamp(y + vy * h, BR, FH - BR);
                float t = i * h;
                if (z < LOW && M.Len(x - p.X, y - p.Y) - R <= sp * t) return (t, x, y);
            }
            return (2.5f + M.Len(x - p.X, y - p.Y) / sp, x, y);
        }

        readonly List<SimPlayer> tmpFoes = new List<SimPlayer>(), tmpTeam = new List<SimPlayer>(), tmpSide = new List<SimPlayer>();

        void Think(SimPlayer b)
        {
            var D = LevelOf(b); var P = b.Brain; var ball = Ball;
            bool up = b.Team == Team.Home; float fwd = up ? -1 : 1, ownY = up ? FH : 0;
            float Dep(float y) => up ? FH - y : y; // distance from own goal line
            float gx = FW / 2 + (Rand() - 0.5f) * GOAL * 0.55f, gy = up ? -30 : FH + 30;
            float dBall = M.Len(ball.X - b.X, ball.Y - b.Y);
            tmpFoes.Clear(); foreach (var f0 in Enemies(b)) if (!f0.Invisible) tmpFoes.Add(f0); // bots can't see an invisible Тень
            tmpTeam.Clear(); tmpTeam.AddRange(Mates(b));
            var foes = tmpFoes; var team = tmpTeam;
            if (!P.WantKick) b.Charging = false;

            if (ball.Owner == b)
            {
                var toG = M.Norm(gx - b.X, gy - b.Y); float distG = M.Len(gx - b.X, gy - b.Y);
                SimPlayer blk = null; float bd = 1e9f; // closest defender in front
                foreach (var f in foes) { float ax = f.X - b.X, ay = f.Y - b.Y, d = M.Len(ax, ay); if (ax * toG.x + ay * toG.y > 0 && d < bd) { bd = d; blk = f; } }
                var mv = toG;
                if (blk != null && bd < 140)
                {
                    float ax = blk.X - b.X, ay = blk.Y - b.Y, px = -toG.y, py = toG.x, side = (ax * px + ay * py) > 0 ? -1 : 1;
                    mv = M.Norm(toG.x + px * side * 1.3f, toG.y + py * side * 1.3f);
                }
                P.Mx = mv.x; P.My = mv.y;
                if (b.FireT > 0 || P.WantKick) return;

                // pass: rate every teammate (forward, square or back) by openness, lane, progress and length
                if (team.Count > 0)
                {
                    float press = 1e9f; foreach (var f in foes) press = Math.Min(press, M.Len(f.X - b.X, f.Y - b.Y));
                    bool found = false; float bsc = 0, bmx = 0, bmy = 0, bmd = 0; bool blob = false;
                    foreach (var m in team)
                    {
                        if (m.Stun > 0) continue;
                        float mx = M.Clamp(m.X + m.Vx * 0.35f, R, FW - R), my = M.Clamp(m.Y + m.Vy * 0.35f, R, FH - R), md = M.Len(mx - b.X, my - b.Y);
                        if (md < 120 || md > T.lobMax) continue;
                        float open = 1e9f, lane = 1e9f;
                        foreach (var f in foes) { open = Math.Min(open, M.Len(f.X - mx, f.Y - my)); lane = Math.Min(lane, M.SegDist(f.X, f.Y, b.X, b.Y, mx, my)); }
                        if (open < 90) continue; // marked: don't force it
                        float openS = M.Clamp01((open - 60) / 220), laneS = M.Clamp01((lane - 25) / 90);
                        bool lob = laneS < 0.5f && md >= 250; // lane cut: go over the top
                        float prog = M.Clamp((Dep(my) - Dep(b.Y)) / 600, -1, 1.5f);
                        float sc = 1.4f * openS + (lob ? 0.55f : laneS) + 0.8f * prog - md / 2200;
                        if (prog < 0 && Dep(my) < 350) sc -= 0.6f; // back pass near own goal is risky
                        if (!found || sc > bsc) { found = true; bsc = sc; bmx = mx; bmy = my; bmd = md; blob = lob; }
                    }
                    bool pressured = press < 190;
                    if (found && ((pressured && bsc > 0.55f) || bsc > 1.75f) && Rand() < 0.85f)
                    {
                        var dn = M.Norm(bmx - b.X, bmy - b.Y); var a = AimWithError(dn.x, dn.y, D.Err * 0.5f);
                        if (blob) { if (CanLob(b)) { LaunchLob(b, a, bmd); return; } }
                        else { P.Curve = 0; P.WantKick = true; P.Kx = a.x; P.Ky = a.y; P.ChargeTo = M.Clamp((bmd * 1.15f / T.kick - 0.7f) / (T.charge - 0.7f), 0.02f, 1); return; }
                    }
                }
                // lob over the defender toward goal
                if (b.KickCd <= 0 && blk != null && bd < 180 && distG > 380 && distG < 1100 && Rand() < D.Abil * 0.35f)
                {
                    var a = AimWithError(toG.x, toG.y, D.Err);
                    if (CanLob(b)) { LaunchLob(b, a, M.Clamp(distG - 80 + (Rand() - 0.5f) * 140, T.lobMin, T.lobMax)); return; }
                }
                float facing = b.Fx * toG.x + b.Fy * toG.y;
                if (b.Hero == Hero.Fire && b.FireCd <= 0 && distG > 220 && distG < 820 && facing > 0.9f && Rand() < D.Abil * 0.45f) { if (UseFire(b)) return; }
                // Вихрь: bend the shot around a defender standing in the shooting lane
                if (b.Hero == Hero.Curve && b.CurveCd <= 0 && blk != null && distG < 900 && M.SegDist(blk.X, blk.Y, b.X, b.Y, gx, gy) < 70 && Rand() < D.Abil * 0.6f)
                {
                    float px = -toG.y, py = toG.x; int side = ((blk.X - b.X) * px + (blk.Y - b.Y) * py) > 0 ? 1 : -1;
                    var r = M.Rot(toG.x, toG.y, -side * 0.5f); var a = AimWithError(r.x, r.y, D.Err * 0.5f);
                    P.Curve = side; P.WantKick = true; P.Kx = a.x; P.Ky = a.y; P.ChargeTo = 0.6f + Rand() * 0.4f;
                    return;
                }
                if ((distG < 420 && facing > 0.8f) || (blk != null && bd < 75 && distG < 700 && facing > 0.5f))
                {
                    var a = AimWithError(toG.x, toG.y, D.Err);
                    P.Curve = 0; P.WantKick = true; P.Kx = a.x; P.Ky = a.y;
                    P.ChargeTo = distG < 260 ? 0.1f : 0.35f + Rand() * 0.55f;
                }
                return;
            }

            P.WantKick = false; P.Curve = 0;
            bool foeHas = ball.Owner != null && ball.Owner.Team != b.Team;
            bool mateHas = ball.Owner != null && ball.Owner.Team == b.Team && ball.Owner != b;
            tmpSide.Clear(); tmpSide.Add(b); tmpSide.AddRange(team);
            var sideList = tmpSide;
            // who goes for the ball: whoever gets there first (a teammate who just passed doesn't count)
            SimPlayer passer = ball.Owner == null && ball.Last != null && ball.Last.Team == b.Team && M.Len(ball.Vx, ball.Vy) > 250 ? ball.Last : null;
            (float t, float x, float y) myI = Intercept(b);
            SimPlayer chaser = null;
            if (!mateHas)
            {
                float bt = 1e9f;
                foreach (var m in sideList)
                {
                    float t = m == passer ? 1e9f : (m == b ? myI.t - 0.001f : Intercept(m).t);
                    if (t < bt) { bt = t; chaser = m; }
                }
            }
            bool someoneCloser = false;
            foreach (var m in sideList) if (m != b && M.Len(ball.X - m.X, ball.Y - m.Y) < dBall) { someoneCloser = true; break; }
            bool chase = chaser == b || (foeHas && dBall < 220 && !someoneCloser);
            bool threat = ball.Owner == null && (up ? -ball.Vy : ball.Vy) < -250 && Dep(ball.Y) < FH * 0.55f && Dep(b.Y) > Dep(ball.Y) - 20;
            float tx, ty;
            if (mateHas)
            { // get open: wings ahead of the carrier, a third one stays behind as a safety
                var sup = new List<SimPlayer>();
                foreach (var m in sideList) if (m != ball.Owner && !m.IsHuman) sup.Add(m);
                sup.Sort((a, c) => a.X.CompareTo(c.X));
                float[,] slots = sup.Count >= 3 ? new float[,] { { 0.2f, 1 }, { 0.5f, -1 }, { 0.8f, 1 } }
                               : sup.Count == 2 ? new float[,] { { 0.22f, 1 }, { 0.78f, 1 } }
                               : new float[,] { { ball.X < FW / 2 ? 0.72f : 0.28f, 1 } };
                int k = Math.Max(0, sup.IndexOf(b)); k = Math.Min(k, slots.GetLength(0) - 1);
                tx = FW * slots[k, 0]; ty = M.Clamp(ball.Y + fwd * (slots[k, 1] > 0 ? 380 : -320), R + 60, FH - R - 60);
            }
            else if (chase)
            {
                if (threat) { tx = M.Clamp(ball.X, GX1 + 10, GX2 - 10); ty = M.Clamp(ball.Y - fwd * 60, R + 6, FH - R - 6); }
                else if (foeHas)
                {
                    var o = ball.Owner; tx = ball.X + o.Fx * 8; ty = ball.Y + o.Fy * 8;
                    if (dBall > 200 && Dep(o.Y) < FH * 0.5f) { tx = (ball.X + FW / 2) / 2; ty = M.Clamp(ball.Y - fwd * 90, R + 6, FH - R - 6); }
                }
                else { tx = myI.x; ty = myI.y; }
            }
            else
            { // no keeper: the deepest free player guards the goal side, the rest mark the most dangerous opponents
                var free = new List<SimPlayer>();
                foreach (var m in sideList) if (m != chaser && !m.IsHuman) free.Add(m);
                free.Sort((a, c) => Dep(a.Y).CompareTo(Dep(c.Y)));
                int k = free.IndexOf(b);
                var marks = new List<SimPlayer>();
                foreach (var f in foes) if (f != ball.Owner) marks.Add(f);
                marks.Sort((a, c) => Dep(a.Y).CompareTo(Dep(c.Y)));
                if (k <= 0 || marks.Count == 0) { tx = (ball.X + FW / 2) / 2; ty = M.Clamp(ownY + (ball.Y - ownY) * 0.45f, R + 40, FH - R - 40); }
                else
                {
                    var f = marks[(k - 1) % marks.Count]; var g = M.Norm(FW / 2 - f.X, ownY - f.Y);
                    tx = f.X + g.x * 70; ty = M.Clamp(f.Y + g.y * 70, R + 20, FH - R - 20);
                }
            }
            float dist = M.Len(tx - b.X, ty - b.Y); var v = M.Norm(tx - b.X, ty - b.Y); float kk = dist < 30 ? dist / 30 : 1;
            P.Mx = v.x * kk; P.My = v.y * kk;
            // spacing: keep away from teammates unless I'm the one going for the ball up close
            if (!(chase && dBall < 220))
            {
                const float SEP = 230; float rx = 0, ry = 0;
                foreach (var m in team)
                {
                    float dx = b.X - m.X, dy = b.Y - m.Y, dd = M.Len(dx, dy);
                    if (dd > 1 && dd < SEP) { float w = (SEP - dd) / SEP; rx += dx / dd * w; ry += dy / dd * w; }
                }
                if (rx != 0 || ry != 0)
                {
                    float mx = P.Mx + rx * 1.8f, my = P.My + ry * 1.8f, mm = M.Len(mx, my);
                    P.Mx = mm > 1 ? mx / mm : mx; P.My = mm > 1 ? my / mm : my;
                }
            }
            // Стена: block a shot heading for our goal, or a carrier closing in on it
            if (b.Hero == Hero.Wall && b.WallCd <= 0)
            {
                bool shotAtUs = threat && Dep(b.Y) < Dep(ball.Y) && dBall > 90 && dBall < T.wallLen * 0.9f;
                bool carrierNear = foeHas && dBall > 70 && dBall < T.wallLen * 0.6f && Dep(b.Y) < Dep(ball.Owner.Y) && Dep(ball.Owner.Y) < FH * 0.4f;
                if ((shotAtUs || carrierNear) && Rand() < D.Abil * 0.5f)
                {
                    var tb = M.Norm(ball.X + ball.Vx * 0.15f - b.X, ball.Y + ball.Vy * 0.15f - b.Y); b.Fx = tb.x; b.Fy = tb.y; // aim the fissure at the danger
                    UseWall(b);
                }
            }
            // Тень: vanish to get open while we attack, or to sneak up on the carrier
            if (b.Hero == Hero.Shade && b.InvisCd <= 0 && !b.Invisible &&
                ((mateHas && Rand() < D.Abil * 0.15f) || (foeHas && chase && dBall < 380 && dBall > 120 && Rand() < D.Abil * 0.3f))) UseInvis(b);
            if (!chase) return;
            if (b.Hero == Hero.Hook && b.HookCd <= 0 && ball.Hook == null && !mateHas && !Hooks.Exists(h => h.Owner == b) && dBall > 170 && dBall < T.hookRange * 0.85f && Rand() < D.Abil * 0.35f)
            {
                var dn = M.Norm(ball.X + ball.Vx * 0.12f - b.X, ball.Y + ball.Vy * 0.12f - b.Y); var a = AimWithError(dn.x, dn.y, D.Err * 0.6f);
                UseHook(b, a.x, a.y);
            }
            else if (b.Hero == Hero.Teleport && b.TpCd <= 0 && b.TpBackT <= 0 && ball.Owner == null && dBall > T.tpDist + 80 && (v.x * b.Fx + v.y * b.Fy) > 0.93f && Rand() < D.Abil * 0.25f) UseTp(b);
            if (b.DashCd <= 0 && dBall > 55 && dBall < 220 && (foeHas || threat) && Rand() < D.Dash * 0.35f) TryDash(b, ball.X - b.X, ball.Y - b.Y);
        }
    }
}
