// Боты обеих команд (версия 2, 01.11.2026).
// Без мяча: один идёт на мяч (кто быстрее успеет), остальные занимают места в построении команды:
//   в атаке — ширина (фланги) и глубина (один страхует сзади), каждый ищет рядом открытое место, куда можно дать пас;
//   в обороне — один закрывает ворота, остальные держат самых опасных соперников или зону между мячом и воротами.
// С мячом: пас низом открытому партнёру, если линию паса не перехватят (считается по времени: мяч против соперника);
//   навес — только если низом нельзя и партнёр совсем свободен; удар, когда есть линия на ворота; иначе ведение.
// Удар выполняется, только когда бот развернулся в сторону удара (иначе мяч ушёл бы по взгляду — бывало, в свои ворота).
using System;
using System.Collections.Generic;
using static Arena.Sim.Field;

namespace Arena.Sim
{
    public partial class Match
    {
        BotLevel LevelOf(SimPlayer b) => BotLevel.Levels[b.Team == Team.Home ? 1 : Difficulty]; // teammates play at medium

        (float x, float y) AimWithError(float vx, float vy, float e) => M.Rot(vx, vy, (Rand() - 0.5f) * e * 2);

        static float DepOf(SimPlayer b, float y) => b.Team == Team.Home ? FH - y : y; // расстояние от своей линии ворот
        static float Fwd(SimPlayer b) => b.Team == Team.Home ? -1 : 1;               // куда «вперёд» по y

        /// <summary>Мяч, пущенный из позиции b по направлению (ax, ay), летит в створ СВОИХ ворот (с запасом), ближе reach.</summary>
        bool AimsAtOwnGoal(SimPlayer b, float ax, float ay, float reach = 2600, float margin = 170)
        {
            float ownY = b.Team == Team.Home ? FH : 0;
            if (b.Team == Team.Home ? ay <= 0.05f : ay >= -0.05f) return false; // летит не к своим воротам
            float t = (ownY - b.Y) / ay, xh = b.X + ax * t;
            return t < reach && xh > GX1 - margin && xh < GX2 + margin;
        }

        /// <summary>Вынос вместо удара в свои ворота: вперёд и к ближней бровке.</summary>
        (float x, float y) Clearance(SimPlayer b)
        {
            float side = b.X < FW / 2 ? -1 : 1;
            return M.Norm(side * 0.75f, Fwd(b));
        }

        // ---------- шаг бота ----------
        void BotStep(SimPlayer b, float dt)
        {
            var P = b.Brain; var D = LevelOf(b);
            P.T -= dt;
            if (P.T <= 0) { P.T = D.React * (b.Keeper ? 0.45f : 1) * (0.7f + Rand() * 0.6f); Think(b); } // вратарь реагирует быстрее
            float mx = P.Mx, my = P.My;
            if (P.WantKick)
            {
                b.Charging = true; b.Charge = Math.Min(1, b.Charge + dt / 0.7f);
                P.KickWait += dt;
                if (b.Charge >= P.ChargeTo)
                {
                    if (AimsAtOwnGoal(b, P.Kx, P.Ky)) { var c = Clearance(b); P.Kx = c.x; P.Ky = c.y; P.Curve = 0; P.Intent = BotStats.Clear; }
                    bool owner = Ball.Owner == b;
                    if (owner && P.Kx * b.Fx + P.Ky * b.Fy < -0.1f)
                    {   // ещё не развернулся: удар с мяча у ног ушёл бы по взгляду — сначала повернуться
                        if (P.KickWait < 0.05f + dt) Stats.TurnWaits++;
                        mx = P.Kx * 0.35f; my = P.Ky * 0.35f;
                        if (P.KickWait > 1.1f) { P.WantKick = false; P.Curve = 0; P.Intent = BotStats.None; b.Charging = false; b.Charge = 0; Stats.KickCancels++; }
                    }
                    else
                    {
                        var L = KickLaunch(b, P.Kx, P.Ky, b.Charge);
                        if (L.HasValue)
                        {
                            var ld = M.Norm(L.Value.vx, L.Value.vy);
                            if (AimsAtOwnGoal(b, ld.x, ld.y)) // реальное направление (с учётом взгляда и положения мяча) — в свои ворота
                            { P.WantKick = false; P.Curve = 0; P.Intent = BotStats.None; b.Charging = false; b.Charge = 0; Stats.KickCancels++; Log.Add("бот " + b.Hero + " (" + b.Team + ") отменил удар: мяч ушёл бы в свои ворота"); }
                        }
                        if (P.WantKick)
                        {
                            bool done = P.Curve != 0 ? TryCurve(b, P.Kx, P.Ky, b.Charge, P.Curve) : TryKick(b, P.Kx, P.Ky, b.Charge);
                            if (done) CountKick(b, P.Intent);
                            P.Curve = 0; b.Charging = false; b.Charge = 0; P.WantKick = false;
                        }
                    }
                }
            }
            else { b.Charging = false; b.Charge = 0; }
            StepPlayer(b, mx, my, D.Spd, dt);
        }

        void CountKick(SimPlayer b, int intent)
        {
            passFrom = intent == BotStats.PassGround || intent == BotStats.PassLob ? b : null; passType = intent;
            if (intent == BotStats.PassGround) Stats.PassGroundTry++;
            else if (intent == BotStats.PassLob) Stats.PassLobTry++;
            else if (intent == BotStats.Shot) Stats.Shots++;
            else if (intent == BotStats.Clear) Stats.Clears++;
        }

        void PlanKick(SimPlayer b, (float x, float y) a, float charge, int intent, int curve = 0)
        {
            var P = b.Brain;
            P.WantKick = true; P.Kx = a.x; P.Ky = a.y; P.ChargeTo = M.Clamp(charge, 0.02f, 1); P.Curve = curve; P.Intent = intent; P.KickWait = 0;
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

        // ---------- оценка паса низом ----------
        /// <summary>Скорость удара, с которой мяч дойдёт до цели на расстоянии d со скоростью около arrive.</summary>
        float PassSpeed(float d, float arrive = 380) => M.Clamp(arrive + T.friction * d, T.kick * 0.7f, T.kick * T.charge);
        float ChargeFor(float v0) => M.Clamp((v0 / T.kick - 0.7f) / (T.charge - 0.7f), 0.02f, 1);

        /// <summary>Запас времени (с) самого опасного соперника на линии паса: успеет он к линии раньше мяча — меньше нуля.</summary>
        float LaneMargin(float ax, float ay, float bx, float by, float v0, List<SimPlayer> foes)
        {
            float L = M.Len(bx - ax, by - ay); if (L < 1) return 1;
            float ux = (bx - ax) / L, uy = (by - ay) / L, f = T.friction, worst = 9;
            foreach (var q in foes)
            {
                if (q.Stun > 0.3f) continue;
                float s = M.Clamp((q.X - ax) * ux + (q.Y - ay) * uy, 0, L);
                float cx = ax + ux * s, cy = ay + uy * s, d = M.Len(q.X - cx, q.Y - cy);
                float k = 1 - s * f / v0; float tb = k <= 0.02f ? 9 : -MathF.Log(k) / f;
                float tf = Math.Max(0, d - R - BR) / (T.speed * 0.95f);
                worst = Math.Min(worst, tf - tb);
            }
            return worst;
        }

        /// <summary>Навес приземляется с недолётом: после касания мяч прыгает ещё ~2 м и катится к партнёру.</summary>
        float LobLand(float d) => M.Clamp(d - 190, T.lobMin, T.lobMax);

        float MinFoeDist(float x, float y, List<SimPlayer> foes)
        {
            float m = 1e9f; foreach (var q in foes) m = Math.Min(m, M.Len(q.X - x, q.Y - y)); return m;
        }

        readonly List<SimPlayer> tmpFoes = new List<SimPlayer>(), tmpTeam = new List<SimPlayer>(), tmpSide = new List<SimPlayer>(), tmpFree = new List<SimPlayer>();
        readonly List<(float x, float y)> tmpSlots = new List<(float x, float y)>();

        void Think(SimPlayer b)
        {
            var D = LevelOf(b); var P = b.Brain; var ball = Ball;
            bool up = b.Team == Team.Home; float fwd = Fwd(b), ownY = up ? FH : 0;
            float Dep(float y) => DepOf(b, y);
            float dBall = M.Len(ball.X - b.X, ball.Y - b.Y);
            tmpFoes.Clear(); foreach (var f0 in Enemies(b)) if (!f0.Invisible) tmpFoes.Add(f0); // bots can't see an invisible Тень
            tmpTeam.Clear(); tmpTeam.AddRange(Mates(b));
            var foes = tmpFoes; var team = tmpTeam;
            if (!P.WantKick) b.Charging = false;

            if (ball.Owner == b) { CarrierThink(b, D, foes, team); return; }

            P.WantKick = false; P.Curve = 0;
            bool foeHas = ball.Owner != null && ball.Owner.Team != b.Team;
            bool mateHas = ball.Owner != null && ball.Owner.Team == b.Team && ball.Owner != b;
            tmpSide.Clear(); tmpSide.Add(b); tmpSide.AddRange(team);
            var sideList = tmpSide;
            // кто идёт на мяч: кто раньше успеет (партнёр, который только что отдал пас, не в счёт)
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
            bool chase = chaser == b || (foeHas && dBall < 200 && !someoneCloser);
            bool threat = ball.Owner == null && (up ? -ball.Vy : ball.Vy) < -250 && Dep(ball.Y) < FH * 0.55f && Dep(b.Y) > Dep(ball.Y) - 20;
            float tx, ty;
            if (chase)
            {
                b.Keeper = false;
                // мяч катится к нашим воротам, а я позади него: бежать «на его место у ворот» значит пройти сквозь мяч
                // и толкнуть его в свои ворота. Догоняем по ходу мяча и принимаем.
                if (threat) { tx = myI.x; ty = myI.y; }
                else if (foeHas)
                {   // отбор: встать между соперником и нашими воротами
                    var o = ball.Owner; var g = M.Norm(FW / 2 - o.X, ownY - o.Y);
                    tx = ball.X + g.x * 18; ty = ball.Y + g.y * 18;
                    if (dBall > 220 && Dep(o.Y) < FH * 0.5f) { tx = (ball.X + FW / 2) / 2; ty = M.Clamp(ball.Y - fwd * 90, R + 6, FH - R - 6); }
                }
                else { tx = myI.x; ty = myI.y; }
            }
            else
            {
                bool attack = mateHas || (ball.Owner == null && ball.Last != null && ball.Last.Team == b.Team && Dep(ball.Y) > FH * 0.35f);
                b.Keeper = !attack && Mode != GameMode.OneVsOne && LastDefender(b, sideList, chaser) == b;
                if (b.Keeper)
                {   // команда обороняется, а я глубже всех из тех, кто не идёт на мяч, — закрываю ворота
                    KeeperThink(b, D, foes);
                    Abilities(b, D, foes, false, foeHas, mateHas, threat, dBall, (P.Mx, P.My));
                    return;
                }
                (tx, ty) = TeamSlot(b, attack, sideList, chaser, foes);
            }
            float dist = M.Len(tx - b.X, ty - b.Y); var v = M.Norm(tx - b.X, ty - b.Y); float kk = dist < 40 ? dist / 40 : 1;
            P.Mx = v.x * kk; P.My = v.y * kk;
            // расстояние до партнёров: не толпиться (кроме того, кто идёт на мяч)
            if (!chase)
            {
                const float SEP = 260; float rx = 0, ry = 0;
                foreach (var m in team)
                {
                    float dx = b.X - m.X, dy = b.Y - m.Y, dd = M.Len(dx, dy);
                    if (dd > 1 && dd < SEP) { float w = (SEP - dd) / SEP; rx += dx / dd * w; ry += dy / dd * w; }
                }
                if (rx != 0 || ry != 0)
                {
                    float mx = P.Mx + rx * 1.4f, my = P.My + ry * 1.4f, mm = M.Len(mx, my);
                    P.Mx = mm > 1 ? mx / mm : mx; P.My = mm > 1 ? my / mm : my;
                }
            }
            Abilities(b, D, foes, chase, foeHas, mateHas, threat, dBall, v);
        }

        // ---------- вратарь ----------
        /// <summary>Последний защитник: самый близкий к своим воротам бот из тех, кто не идёт на мяч.</summary>
        SimPlayer LastDefender(SimPlayer b, List<SimPlayer> sideList, SimPlayer chaser)
        {
            SimPlayer last = null;
            foreach (var m in sideList)
                if (m != chaser && m != Ball.Owner && !m.IsHuman && (last == null || DepOf(m, m.Y) < DepOf(last, last.Y) - 1 || (Math.Abs(DepOf(m, m.Y) - DepOf(last, last.Y)) <= 1 && m.Id < last.Id))) last = m;
            return last;
        }

        static bool KeeperZone(SimPlayer k, float x, float y) => DepOf(k, y) < 380 && Math.Abs(x - FW / 2) < 430;

        void KeeperThink(SimPlayer b, BotLevel D, List<SimPlayer> foes)
        {
            var P = b.Brain; var ball = Ball; bool up = b.Team == Team.Home;
            float Dep(float y) => DepOf(b, y);
            float YAt(float dep) => up ? FH - dep : dep;
            P.WantKick = false; P.Curve = 0;
            float gx = FW / 2, ownY = up ? FH : 0;
            float distGoal = M.Len(ball.X - gx, ball.Y - ownY);
            float vIn = up ? ball.Vy : -ball.Vy; // скорость мяча к нашим воротам
            float tx, ty; bool urgent = false;
            if (ball.Owner == null && vIn > 250 && ball.Z < HEAD + 40)
            {   // удар по воротам: встать туда, где мяч пересечёт линию перед воротами
                float lineDep = M.Clamp(Dep(b.Y), 40, 160), ly = YAt(lineDep);
                float t = (ly - ball.Y) / ball.Vy;
                float xc = ball.X + ball.Vx * Math.Max(0, t);
                if (t > 0 && t < 1.6f && xc > GX1 - 90 && xc < GX2 + 90)
                {
                    tx = M.Clamp(xc, GX1 - 30, GX2 + 30); ty = ly; urgent = true;
                    float miss = Math.Abs(tx - b.X);
                    if (miss > 55 && t < 0.5f && b.DashCd <= 0 && Rand() < 0.4f + D.Dash * 0.6f)
                        TryDash(b, tx - b.X, (ball.Y - b.Y) * 0.15f); // бросок в сторону
                }
                else { tx = gx + (ball.X - gx) * 0.3f; ty = YAt(70); }
            }
            else if (ball.Owner == null && KeeperZone(b, ball.X, ball.Y))
            {   // ничейный мяч в штрафной — выйти и забрать, если успеваю раньше соперников
                var me = Intercept(b); float foeT = 9;
                foreach (var f in foes) foeT = Math.Min(foeT, Intercept(f).t);
                if (me.t < foeT + 0.15f) { tx = me.x; ty = me.y; urgent = true; }
                else { tx = gx + (ball.X - gx) * 0.35f; ty = YAt(M.Clamp(distGoal * 0.25f, 60, 150)); }
            }
            else if (ball.Owner != null && ball.Owner.Team != b.Team && distGoal < 520)
            {   // соперник с мячом близко: сократить угол, а вплотную — в ноги
                float dB = M.Len(ball.X - b.X, ball.Y - b.Y);
                if (dB < 110 && KeeperZone(b, ball.X, ball.Y)) { tx = ball.X; ty = ball.Y; urgent = true; }
                else
                {
                    var d = M.Norm(ball.X - gx, ball.Y - ownY); float r = M.Clamp(distGoal * 0.3f, 60, 170);
                    tx = gx + d.x * r; ty = ownY + d.y * r;
                }
            }
            else if (distGoal > 950)
            {   // мяч далеко: не стоять в воротах, а страховать на подходах (выше линии штрафной)
                var d = M.Norm(ball.X - gx, ball.Y - ownY); float r = M.Clamp(distGoal * 0.38f, 300, 720);
                tx = gx + d.x * r; ty = ownY + d.y * r;
                float dist0 = M.Len(tx - b.X, ty - b.Y); var v0 = M.Norm(tx - b.X, ty - b.Y); float k0 = dist0 < 30 ? dist0 / 30 : 1;
                P.Mx = v0.x * k0; P.My = v0.y * k0;
                return;
            }
            else
            {   // мяч на нашей половине: на линии «мяч — центр ворот», ближе к воротам
                var d = M.Norm(ball.X - gx, ball.Y - ownY); float r = M.Clamp(70 + distGoal * 0.1f, 70, 230);
                tx = gx + d.x * r; ty = ownY + d.y * r;
            }
            tx = M.Clamp(tx, GX1 - 120, GX2 + 120);
            if (Dep(ty) > 360) ty = YAt(360);
            if (Dep(ty) < 25) ty = YAt(25);
            float dist = M.Len(tx - b.X, ty - b.Y); var v = M.Norm(tx - b.X, ty - b.Y); float kk = dist < (urgent ? 8 : 30) ? dist / (urgent ? 8 : 30) : 1;
            P.Mx = v.x * kk; P.My = v.y * kk;
        }

        // ---------- места в построении ----------
        (float x, float y) TeamSlot(SimPlayer b, bool attack, List<SimPlayer> sideList, SimPlayer chaser, List<SimPlayer> foes)
        {
            var ball = Ball; float fwd = Fwd(b), ownY = b.Team == Team.Home ? FH : 0;
            float Dep(float y) => DepOf(b, y);
            float YAt(float dep) => b.Team == Team.Home ? FH - dep : dep;
            var free = tmpFree; free.Clear();
            var last = attack || Mode == GameMode.OneVsOne ? null : LastDefender(b, sideList, chaser); // он у ворот, места ему не нужно
            bool haveKeeper = last != null;
            foreach (var m in sideList) if (m != chaser && m != ball.Owner && !m.IsHuman && m != last) free.Add(m);
            if (free.Count == 0) return (b.X, b.Y);
            var slots = tmpSlots; slots.Clear();
            float bx = ball.X, by = ball.Y, bdep = Dep(by);
            if (attack)
            {
                bool left = bx < FW / 2;
                float aheadDep = M.Clamp(bdep + 380, 300, FH - 230);
                slots.Add((left ? FW * 0.8f : FW * 0.2f, YAt(aheadDep)));                                      // дальний фланг впереди
                if (Math.Abs(bx - FW / 2) > 280) slots.Add((FW / 2, YAt(M.Clamp(bdep + 450, 300, FH - 300))));  // мяч на фланге — в центр под пас
                else slots.Add((left ? FW * 0.2f : FW * 0.8f, YAt(M.Clamp(bdep + 300, 300, FH - 230))));       // ближний фланг
                slots.Add(((bx + FW / 2) / 2, YAt(M.Clamp(bdep - 320, 180, FH - 400))));                       // страхующий сзади
                while (slots.Count < free.Count) slots.Add((FW / 2, YAt(M.Clamp(bdep + 150, 200, FH - 300))));
            }
            else
            {
                if (!haveKeeper) slots.Add((FW / 2 + (bx - FW / 2) * 0.3f, YAt(M.Clamp(bdep * 0.35f, 140, 420)))); // у ворот (если нет вратаря)
                // опасные соперники без мяча, ближе всего к нашим воротам
                var marks = new List<SimPlayer>();
                foreach (var f in foes) if (f != ball.Owner && Dep(f.Y) < FH * 0.62f) marks.Add(f);
                marks.Sort((a, c) => Dep(a.Y).CompareTo(Dep(c.Y)));
                int z = 0;
                for (int i = 1; i < Math.Max(free.Count, 3); i++)
                {
                    if (i - 1 < marks.Count)
                    {
                        var f = marks[i - 1]; var g = M.Norm(FW / 2 - f.X, ownY - f.Y);
                        slots.Add((f.X + g.x * 80, f.Y + g.y * 80));
                    }
                    else { slots.Add((M.Clamp(bx + (z % 2 == 0 ? -240 : 240), 120, FW - 120), YAt(M.Clamp(bdep - 280, 320, FH * 0.6f)))); z++; }
                }
            }
            // жадное распределение: ближайшая пара «игрок—место» первой
            int n = free.Count, k = slots.Count;
            var usedP = new bool[n]; var usedS = new bool[k];
            int me = free.IndexOf(b); (float x, float y) mine = (b.X, b.Y);
            for (int step = 0; step < Math.Min(n, k); step++)
            {
                float best = 1e18f; int bi = -1, bj = -1;
                for (int i = 0; i < n; i++) if (!usedP[i])
                    for (int j = 0; j < k; j++) if (!usedS[j])
                    {
                        float c = M.Len(free[i].X - slots[j].x, free[i].Y - slots[j].y) + j * 40; // первые места важнее
                        if (c < best) { best = c; bi = i; bj = j; }
                    }
                if (bi < 0) break;
                usedP[bi] = usedS[bj] = true;
                if (bi == me) { mine = slots[bj]; break; }
            }
            mine.x = M.Clamp(mine.x, R + 60, FW - R - 60); mine.y = M.Clamp(mine.y, R + 60, FH - R - 60);
            if (!attack) return mine;
            // открыться: рядом с местом найти точку, свободную от соперников и с чистой линией паса от мяча
            (float x, float y) bestP = mine; float bestS = -1e9f;
            for (int i = 0; i < 9; i++)
            {
                float ox = i == 0 ? 0 : MathF.Cos(i * 0.785f) * 150, oy = i == 0 ? 0 : MathF.Sin(i * 0.785f) * 150;
                float px = M.Clamp(mine.x + ox, R + 60, FW - R - 60), py = M.Clamp(mine.y + oy, R + 60, FH - R - 60);
                float open = Math.Min(MinFoeDist(px, py, foes), 380) / 380;
                float lane = 1e9f; foreach (var q in foes) lane = Math.Min(lane, M.SegDist(q.X, q.Y, bx, by, px, py));
                float s = open + 0.6f * M.Clamp01((lane - 30) / 130) - M.Len(ox, oy) / 900;
                if (s > bestS) { bestS = s; bestP = (px, py); }
            }
            return bestP;
        }

        // ---------- с мячом ----------
        void CarrierThink(SimPlayer b, BotLevel D, List<SimPlayer> foes, List<SimPlayer> team)
        {
            var P = b.Brain; float fwd = Fwd(b); bool up = b.Team == Team.Home;
            float Dep(float y) => DepOf(b, y);
            float gx = FW / 2 + (Rand() - 0.5f) * GOAL * 0.55f, gy = up ? -30 : FH + 30;
            var toG = M.Norm(gx - b.X, gy - b.Y); float distG = M.Len(gx - b.X, gy - b.Y);
            SimPlayer blk = null; float bd = 1e9f; // closest defender in front
            foreach (var f in foes) { float ax = f.X - b.X, ay = f.Y - b.Y, d = M.Len(ax, ay); if (ax * toG.x + ay * toG.y > 0 && d < bd) { bd = d; blk = f; } }
            float press = 1e9f; foreach (var f in foes) press = Math.Min(press, M.Len(f.X - b.X, f.Y - b.Y));
            // ведение: к воротам, обходя защитника, не прижимаясь к бровке
            var mv = toG;
            if (blk != null && bd < 150)
            {
                float ax = blk.X - b.X, ay = blk.Y - b.Y, px = -toG.y, py = toG.x, side = (ax * px + ay * py) > 0 ? -1 : 1;
                if ((b.X < 160 && px * side < 0) || (b.X > FW - 160 && px * side > 0)) side = -side;
                mv = M.Norm(toG.x + px * side * 1.3f, toG.y + py * side * 1.3f);
            }
            P.Mx = mv.x; P.My = mv.y;
            if (b.FireT > 0 || P.WantKick) return;

            // ---- пас: оцениваем каждого партнёра ----
            bool found = false, bestLob = false; float bsc = -9, bProg = 0, bmx = 0, bmy = 0, bmd = 0;
            foreach (var m in team)
            {
                if (m.Stun > 0) continue;
                float mx = M.Clamp(m.X + m.Vx * 0.35f, R, FW - R), my = M.Clamp(m.Y + m.Vy * 0.35f, R, FH - R), md = M.Len(mx - b.X, my - b.Y);
                float lx = M.Clamp(m.X + m.Vx * 0.8f, R, FW - R), ly = M.Clamp(m.Y + m.Vy * 0.8f, R, FH - R); // навес летит дольше — упреждение больше
                if (md < 140 || md > T.lobMax) continue;
                var pd = M.Norm(mx - b.X, my - b.Y);
                if (AimsAtOwnGoal(b, pd.x, pd.y, 2600, 280)) continue; // промах — и мяч в своих воротах: такой пас не даём
                float open = MinFoeDist(mx, my, foes);
                float openS = M.Clamp01((open - 60) / 240);
                float prog = M.Clamp((Dep(my) - Dep(b.Y)) / 600, -1, 1.5f);
                float back = prog < 0 && Dep(my) < 420 ? 0.9f : 0; // назад у своих ворот — опасно
                float v0 = PassSpeed(md);
                float margin = LaneMargin(b.X, b.Y, mx, my, v0, foes);
                float sc; bool lob;
                if (margin > 0.08f && open > 75)
                { sc = 1.3f * openS + 0.9f * prog + 0.55f + Math.Min(margin, 0.4f) - md / 2600 - back; lob = false; }       // низом
                else if (md >= 380 && MinFoeDist(lx, ly, foes) >= 230 && prog > 0.2f)
                { sc = 1.0f * openS + 0.9f * prog - 0.6f - md / 2600 - back; lob = true; mx = lx; my = ly; md = M.Len(mx - b.X, my - b.Y); } // навес — только вперёд и совсем свободному
                else continue;
                if (!found || sc > bsc) { found = true; bsc = sc; bProg = prog; bmx = mx; bmy = my; bmd = md; bestLob = lob; }
            }
            bool pressured = press < 170, blocked = blk != null && bd < 110;
            float need = pressured ? 0.45f : blocked ? 0.75f : 1.35f;
            bool shootLane = true;
            foreach (var f in foes) if (M.SegDist(f.X, f.Y, b.X, b.Y, gx, gy) < 38 && Dep(f.Y) > Dep(b.Y) - 10) { shootLane = false; break; }
            bool canShoot = distG < 520 && (shootLane || distG < 260);
            if (found && !canShoot && bsc > need && (bProg > 0.12f || pressured || blocked) && Rand() < 0.9f)
            {
                var dn = M.Norm(bmx - b.X, bmy - b.Y); var a = AimWithError(dn.x, dn.y, D.Err * 0.45f);
                if (bestLob) { if (CanLob(b)) { CountKick(b, BotStats.PassLob); LaunchLob(b, a, LobLand(bmd)); } return; }
                PlanKick(b, a, ChargeFor(PassSpeed(bmd)), BotStats.PassGround);
                return;
            }
            float facing = b.Fx * toG.x + b.Fy * toG.y;
            // огненный суперудар
            if (b.Hero == Hero.Fire && b.FireCd <= 0 && distG > 220 && distG < 820 && facing > 0.9f && Rand() < D.Abil * 0.45f) { if (UseFire(b)) { b.Brain.Intent = BotStats.Shot; Stats.Shots++; return; } }
            // Вихрь: обвести удар вокруг защитника на линии удара
            if (b.Hero == Hero.Curve && b.CurveCd <= 0 && blk != null && distG < 900 && M.SegDist(blk.X, blk.Y, b.X, b.Y, gx, gy) < 70 && Rand() < D.Abil * 0.6f)
            {
                float px = -toG.y, py = toG.x; int side = ((blk.X - b.X) * px + (blk.Y - b.Y) * py) > 0 ? 1 : -1;
                var r = M.Rot(toG.x, toG.y, -side * 0.5f); var a = AimWithError(r.x, r.y, D.Err * 0.5f);
                PlanKick(b, a, 0.6f + Rand() * 0.4f, BotStats.Shot, side);
                return;
            }
            // удар по воротам
            if (canShoot && facing > 0.6f)
            {
                var a = AimWithError(toG.x, toG.y, D.Err);
                PlanKick(b, a, distG < 260 ? 0.15f : 0.5f + 0.5f * M.Clamp01(distG / 520), BotStats.Shot);
                return;
            }
            // навес на ворота через защитника
            if (b.KickCd <= 0 && blk != null && bd < 180 && distG > 380 && distG < 1100 && Rand() < D.Abil * 0.3f)
            {
                var a = AimWithError(toG.x, toG.y, D.Err);
                if (CanLob(b)) { b.Brain.Intent = BotStats.Shot; Stats.Shots++; LaunchLob(b, a, M.Clamp(distG - 80 + (Rand() - 0.5f) * 140, T.lobMin, T.lobMax)); return; }
            }
            // у своих ворот под давлением и некому отдать — вынос
            if (pressured && Dep(b.Y) < 520 && (!found || bsc < 0.2f)) { PlanKick(b, Clearance(b), 1, BotStats.Clear); return; }
            // пас «хоть куда-то», если зажали со всех сторон
            if (found && press < 110 && bsc > 0.1f)
            {
                var dn = M.Norm(bmx - b.X, bmy - b.Y);
                if (bestLob) { if (CanLob(b)) { CountKick(b, BotStats.PassLob); LaunchLob(b, dn, LobLand(bmd)); } }
                else PlanKick(b, dn, ChargeFor(PassSpeed(bmd)), BotStats.PassGround);
            }
        }

        // ---------- способности без мяча ----------
        void Abilities(SimPlayer b, BotLevel D, List<SimPlayer> foes, bool chase, bool foeHas, bool mateHas, bool threat, float dBall, (float x, float y) v)
        {
            var ball = Ball; float fwd = Fwd(b);
            float Dep(float y) => DepOf(b, y);
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
            // рывок в сторону своих ворот сбивает мяч туда же (на рывке мяч отскакивает, а не ловится) — так не делаем нигде
            var toBall = M.Norm(ball.X - b.X, ball.Y - b.Y);
            bool towardOwnGoal = toBall.y * -fwd > 0.2f || AimsAtOwnGoal(b, toBall.x, toBall.y);
            bool goalSide = Dep(b.Y) < Dep(ball.Y) + 10; // я между мячом и своими воротами
            if (b.DashCd <= 0 && dBall > 55 && dBall < 220 && foeHas && goalSide && !towardOwnGoal && Rand() < D.Dash * 0.35f) TryDash(b, ball.X - b.X, ball.Y - b.Y);
        }
    }
}
