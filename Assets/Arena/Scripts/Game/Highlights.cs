// Хайлайты ботов для клипов: прогоняет матчи ботов 4 на 4 без графики (во много раз быстрее реального времени),
// оценивает каждый гол и оставляет лучшие — вместе с записью последних секунд перед ним (GoalReplay).
// Ещё ищет моменты способностей (крюк вырвал мяч, разлом отбил удар, Тень невидимкой открылась...) — по 2 на героя;
// тогда порядок такой: Жар → Багор → Миг → Вихрь → Тень → Стена, потом голы.
// Проигрывает их ArenaGame (состояние Reel) с нескольких камер. Запуск — F8 в главном меню.
using System;
using System.Collections.Generic;
using Arena.Sim;
using UnityEngine;

namespace Arena.Game
{
    public class Highlight
    {
        public Match M;          // матч, в котором был момент (его герои и стадион показываются при проигрывании)
        public GoalReplay Rec;   // кадры: несколько секунд до гола и празднование после
        public float GoalT;      // время гола в записи
        public SimPlayer Scorer; // кто забил (может быть null)
        public float Score;
        public string Desc = "";
        public int MatchNo;
        // момент способности (не гол)
        public bool Ability;
        public Hero Hero;
        public SimPlayer Subject; // чей это момент: на нём облёт, его команда показывается «своей» (Тень видна полупрозрачной)
        public float T0, T1, KeyT; // запись от T0 до T1; KeyT — ключевое мгновение для замедленного облёта
        public float Follow;      // насколько «трансляция» держит в кадре героя, а не только мяч (0 — только мяч)
    }

    [Serializable]
    public class HighlightTuning
    {
        [Tooltip("Сколько матчей ботов прогнать")] [Range(1, 100)] public int matches = 20;
        [Tooltip("Сколько лучших моментов показать")] [Range(1, 40)] public int keep = 12;
        [Tooltip("Сложность ботов: 0 — лёгкий, 1 — средний, 2 — сложный")] [Range(0, 2)] public int difficulty = 2;
        [Tooltip("План 1 «трансляция»: секунд до гола")] [Range(2, 7)] public float leadSeconds = 5;
        [Tooltip("План 2 «за мячом»: секунд до гола")] [Range(0.5f, 4)] public float slowSeconds = 2f;
        [Tooltip("План 2 «за мячом»: скорость (меньше — медленнее)")] [Range(0.2f, 1)] public float slowSpeed = 0.5f;
        [Tooltip("План 3 «облёт» забившего после гола")] public bool orbit = true;
        [Tooltip("План 3: секунд после гола")] [Range(0.5f, 3)] public float orbitSeconds = 1.7f;
        [Tooltip("Плашка «ГОЛ!» со счётом поверх облёта")] public bool banner = true;
        [Tooltip("Чёрный кадр между моментами, с (по нему Claude режет запись; 0 — без него)")] [Range(0, 1)] public float gap = 0.25f;
        [Tooltip("Зерно: одно и то же — те же матчи; 0 — каждый раз новые")] public int seed = 0;
        [Header("Моменты способностей")]
        [Tooltip("Искать и моменты способностей, а не только голы. Порядок: по героям (Жар → Багор → Миг → Вихрь → Тень → Стена), потом голы")] public bool abilities = true;
        [Tooltip("Сколько лучших моментов на каждого героя")] [Range(0, 4)] public int perHero = 2;
        [Tooltip("Секунд до применения способности")] [Range(1, 4)] public float abilityLead = 2f;
        [Tooltip("Второй план: замедленный облёт героя в ключевой момент")] public bool abilityOrbit = true;
        [Tooltip("Скорость замедленного облёта (меньше — медленнее)")] [Range(0.2f, 1)] public float abilitySlow = 0.45f;
    }

    public class HighlightScan
    {
        const float DT = 1f / 120, KEEP = 7.5f, AFTER = 2f;
        static readonly Hero[] Heroes = { Hero.Fire, Hero.Hook, Hero.Teleport, Hero.Curve, Hero.Shade, Hero.Wall };
        public static string HeroName(Hero h) =>
            h == Hero.Fire ? "Жар" : h == Hero.Hook ? "Багор" : h == Hero.Teleport ? "Миг" : h == Hero.Curve ? "Вихрь" : h == Hero.Shade ? "Тень" : "Стена";

        readonly Tuning tuning; readonly HighlightTuning ht; readonly System.Random rnd;
        /// <summary>Что показывать, по порядку (заполняется, когда поиск закончен).</summary>
        public readonly List<Highlight> Best = new List<Highlight>();
        readonly List<Highlight> goals = new List<Highlight>();
        readonly Dictionary<Hero, List<Highlight>> byHero = new Dictionary<Hero, List<Highlight>>();
        public int Done, Goals, Moments;
        public int Total => ht.matches;
        public bool Finished => Done >= Total;

        Match cur; GoalReplay ring; float t;
        struct Pending { public float T; public Team Team; public string How; }
        readonly List<Pending> pending = new List<Pending>();
        static readonly InputCommand[] none = new InputCommand[8];

        public HighlightScan(Tuning tuning, HighlightTuning ht)
        {
            this.tuning = tuning; this.ht = ht;
            rnd = new System.Random(ht.seed != 0 ? ht.seed : Environment.TickCount);
        }

        /// <summary>Поработать ms миллисекунд (чтобы игра не зависала, пока ищем моменты).</summary>
        public void Work(double ms)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!Finished && sw.Elapsed.TotalMilliseconds < ms)
            {
                if (cur == null) StartMatch();
                for (int k = 0; k < 40 && cur != null; k++) Step();
            }
        }

        void StartMatch()
        {
            cur = new Match(tuning, rnd.Next());
            cur.Setup(GameMode.FourVsFour, Heroes[rnd.Next(Heroes.Length)], ht.difficulty);
            foreach (var p in cur.Players) p.HumanSlot = -1; // все — боты
            ring = new GoalReplay(); t = 0; pending.Clear(); abs.Clear();
            prev = new Prev[cur.Players.Count]; Capture();
        }

        void Step()
        {
            cur.Step(DT, none); t += DT;
            ring.Record(cur, t, KEEP + AFTER + 0.5f);
            bool goalNow = false; Team goalTeam = Team.Home;
            foreach (var e in cur.Events) if (e.Kind == FxKind.Goal) { pending.Add(new Pending { T = t, Team = e.Team, How = cur.Ball.LastHow }); Goals++; goalNow = true; goalTeam = e.Team; }
            if (ht.abilities && ht.perHero > 0) Abilities(goalNow, goalTeam);
            Capture();
            cur.Events.Clear(); cur.Log.Clear();
            for (int i = pending.Count - 1; i >= 0; i--)
                if (t >= pending[i].T + AFTER) { Finish(pending[i]); pending.RemoveAt(i); }
            for (int i = abs.Count - 1; i >= 0; i--)
                if (t >= abs[i].End) { FinishAbility(abs[i]); abs.RemoveAt(i); }
            if (cur.Phase == MatchPhase.Ended)
            {
                foreach (var p in pending) Finish(p);
                foreach (var a in abs) FinishAbility(a);
                pending.Clear(); abs.Clear(); cur = null; ring = null; Done++;
                if (Finished) BuildReel();
            }
        }

        /// <summary>Порядок показа: по героям, потом голы.</summary>
        void BuildReel()
        {
            Best.Clear();
            if (ht.abilities)
                foreach (var h in Heroes)
                    if (byHero.TryGetValue(h, out var list)) Best.AddRange(list);
            Best.AddRange(goals);
        }

        // ---------- моменты способностей ----------
        const float WATCH = 3.2f; // сколько секунд смотрим, чем кончилась способность

        struct Prev { public float X, Y, HookCd, CurveCd, InvisT, WallCd, TpCd, TpBackT, Stun; }
        Prev[] prev; SimPlayer prevOwner; float prevFire, prevVx, prevVy; int prevWalls;

        class Ab
        {
            public float T, End, OutT = -1, Bx, By, Carry, MaxSpin, Travel, HookDist;
            public SimPlayer P; public Hero H; public Hook Hk; public IceWall Wl;
            public bool OppHit, WallBroke, Goal, GotBall, Steal, Pulled, VictimHadBall, MateRecv, Settled, Bounce, OppLost, TpBack;
            public int Pushed;
        }
        readonly List<Ab> abs = new List<Ab>();

        void Capture()
        {
            for (int i = 0; i < prev.Length; i++)
            {
                var p = cur.Players[i];
                prev[i] = new Prev { X = p.X, Y = p.Y, HookCd = p.HookCd, CurveCd = p.CurveCd, InvisT = p.InvisT, WallCd = p.WallCd, TpCd = p.TpCd, TpBackT = p.TpBackT, Stun = p.Stun };
            }
            var b = cur.Ball;
            prevOwner = b.Owner; prevFire = b.Fire; prevVx = b.Vx; prevVy = b.Vy; prevWalls = cur.Walls.Count;
        }

        Ab Begin(SimPlayer p)
        {
            var a = new Ab { T = t, End = t + (p.Hero == Hero.Shade ? tuning.invisDur + 0.5f : WATCH), P = p, H = p.Hero, Bx = cur.Ball.X, By = cur.Ball.Y };
            abs.Add(a); return a;
        }
        void Out(Ab a) { if (a.OutT < 0) a.OutT = t; }

        static float SegDist(IceWall w, float x, float y)
        {
            float px = x - w.X, py = y - w.Y, k = Mathf.Clamp(px * w.Tx + py * w.Ty, 0, w.Len);
            float dx = px - w.Tx * k, dy = py - w.Ty * k;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Заметить применение способностей и проследить, чем кончилось.</summary>
        void Abilities(bool goalNow, Team goalTeam)
        {
            var b = cur.Ball;
            // 1. начало
            for (int i = 0; i < prev.Length; i++)
            {
                var p = cur.Players[i]; var q = prev[i];
                switch (p.Hero)
                {
                    case Hero.Hook:
                        if (p.HookCd > q.HookCd + 0.5f) Begin(p).Hk = cur.Hooks.FindLast(h => h.Owner == p);
                        break;
                    case Hero.Curve:
                        if (p.CurveCd > q.CurveCd + 0.5f) Begin(p);
                        break;
                    case Hero.Shade:
                        if (p.InvisT > 0 && q.InvisT <= 0) Begin(p);
                        break;
                    case Hero.Wall:
                        if (p.WallCd > q.WallCd + 0.5f) Begin(p).Wl = cur.Walls.FindLast(w => w.Owner == p);
                        break;
                    case Hero.Teleport:
                    {
                        float dx = p.X - q.X, dy = p.Y - q.Y;
                        bool jump = p.TpCd > q.TpCd + 0.5f, back = q.TpBackT > 0 && p.TpBackT <= 0;
                        if (dx * dx + dy * dy > 120 * 120 && (jump || back))
                        {
                            var old = abs.Find(x => x.P == p && t - x.T < 1.5f);
                            if (old != null) old.TpBack = true; else Begin(p).TpBack = back;
                        }
                        break;
                    }
                }
            }
            if (b.Fire > 0 && prevFire <= 0 && b.FireBy != null && b.FireBy.Hero == Hero.Fire) Begin(b.FireBy);

            // 2. чем кончилось
            float vx = b.Vx, vy = b.Vy, spd = Mathf.Sqrt(vx * vx + vy * vy);
            foreach (var a in abs)
            {
                var p = a.P;
                if (t <= a.T) continue;
                switch (a.H)
                {
                    case Hero.Fire:
                        if (b.FireBy == p && b.Fire > 0)
                        {
                            for (int i = 0; i < prev.Length; i++)
                            {
                                var e = cur.Players[i];
                                if (e.Team != p.Team && e.Stun > prev[i].Stun + 0.5f) { a.OppHit = true; Out(a); }
                            }
                            if (cur.Walls.Count < prevWalls) { a.WallBroke = true; Out(a); }
                        }
                        break;
                    case Hero.Hook:
                        if (a.Hk == null) break;
                        a.HookDist = Mathf.Max(a.HookDist, a.Hk.Dist);
                        if (a.Hk.HasBall && !a.GotBall) { a.GotBall = true; a.Steal = prevOwner != null && prevOwner.Team != p.Team; Out(a); }
                        if (a.Hk.Victim != null && !a.Pulled) { a.Pulled = true; a.VictimHadBall = prevOwner == a.Hk.Victim; Out(a); }
                        break;
                    case Hero.Teleport:
                        if (b.Owner == p && !a.GotBall && t - a.T < 2f) { a.GotBall = true; Out(a); }
                        break;
                    case Hero.Curve:
                        if (a.Settled) break;
                        if (b.Owner == null) a.MaxSpin = Mathf.Max(a.MaxSpin, Mathf.Abs(b.Spin));
                        else if (b.Owner != p || t - a.T > 0.4f)
                        {
                            a.Settled = true;
                            if (b.Owner.Team == p.Team && b.Owner != p)
                            {
                                a.MateRecv = true; Out(a);
                                float dx = b.X - a.Bx, dy = b.Y - a.By; a.Travel = Mathf.Sqrt(dx * dx + dy * dy);
                            }
                        }
                        break;
                    case Hero.Shade:
                        if (p.InvisT <= 0) break;
                        if (b.Owner == p)
                        {
                            if (prevOwner != p)
                            {
                                if (prevOwner != null && prevOwner.Team != p.Team) a.Steal = true; else a.GotBall = true;
                                Out(a);
                            }
                            int k = cur.Players.IndexOf(p);
                            float dx = p.X - prev[k].X, dy = p.Y - prev[k].Y;
                            a.Carry += Mathf.Sqrt(dx * dx + dy * dy);
                            if (a.Carry > 200) Out(a);
                        }
                        break;
                    case Hero.Wall:
                    {
                        if (a.Wl == null) break;
                        int pushed = 0;
                        foreach (var e in a.Wl.Pushed) if (e.Team != p.Team) pushed++;
                        if (pushed > a.Pushed) { a.Pushed = pushed; Out(a); }
                        if (!a.Bounce && a.Wl.BallPushed) { a.Bounce = true; Out(a); }
                        if (!a.Bounce && cur.Walls.Contains(a.Wl) && b.Owner == null && b.Z < IceWall.HEIGHT && spd > 250
                            && vx * prevVx + vy * prevVy < 0 && SegDist(a.Wl, b.X, b.Y) < 55) { a.Bounce = true; Out(a); }
                        if (!a.OppLost && prevOwner != null && prevOwner.Team != p.Team && b.Owner != prevOwner && a.Wl.Pushed.Contains(prevOwner)) { a.OppLost = true; Out(a); }
                        break;
                    }
                }
                if (goalNow && goalTeam == p.Team && !a.Goal) { a.Goal = true; Out(a); a.End = Mathf.Min(Mathf.Max(a.End, t + 1.4f), a.T + 6.5f); }
            }
        }

        void FinishAbility(Ab a)
        {
            float s, follow = 0.5f, key = a.T; string what;
            switch (a.H)
            {
                case Hero.Fire:
                    if (!a.OppHit && !a.WallBroke) return; // просто огненный гол — он и так среди голов
                    s = 2 + (a.OppHit ? 4 : 0) + (a.WallBroke ? 4 : 0) + (a.Goal ? 2 : 0); follow = 0;
                    what = a.OppHit ? "огонь сбил соперника" : "огонь разбил разлом";
                    break;
                case Hero.Hook:
                    if (!a.GotBall && !a.Pulled) return;
                    s = 2 + (a.Steal ? 5 : a.GotBall ? 2.5f : 0) + (a.Pulled ? 3.5f + (a.VictimHadBall ? 1.5f : 0) : 0) + (a.Goal ? 3 : 0) + Mathf.Min(a.HookDist / 200, 2);
                    follow = 0.45f; key = a.OutT >= 0 ? a.OutT : a.T;
                    what = a.Steal ? "крюк вырвал мяч" : a.Pulled ? (a.VictimHadBall ? "крюк выдернул соперника с мячом" : "крюк притянул соперника") : "крюк поймал мяч";
                    break;
                case Hero.Teleport:
                    if (!a.GotBall && !a.Goal && !a.TpBack) return;
                    s = 1 + (a.GotBall ? 3 : 0) + (a.Goal ? 3 : 0) + (a.TpBack ? 1 : 0); follow = 0.6f;
                    what = a.GotBall ? "телепорт к мячу" : a.TpBack ? "телепорт туда и обратно" : "телепорт";
                    break;
                case Hero.Curve:
                    if (a.MaxSpin < 0.3f || (!a.MateRecv && !a.Goal)) return;
                    s = 1.5f + a.MaxSpin * 1.5f + (a.MateRecv ? 3 : 0) + (a.Goal ? 4 : 0) + Mathf.Min(a.Travel / 400, 3); follow = 0;
                    what = a.Goal ? "кручёный в ворота" : "кручёный пас";
                    break;
                case Hero.Shade:
                    if (!a.GotBall && !a.Steal && !a.Goal && a.Carry < 200) return;
                    s = 1.5f + (a.GotBall ? 3 : 0) + (a.Steal ? 4 : 0) + Mathf.Min(a.Carry / 300, 3) + (a.Goal ? 4 : 0); follow = 0.6f;
                    what = a.Steal ? "невидимость: отбор мяча" : a.GotBall ? "невидимость: открылся под пас" : "невидимость: провёл мяч";
                    break;
                default: // Стена
                    if (a.Pushed == 0 && !a.Bounce && !a.OppLost) return;
                    s = 1.5f + Mathf.Min(a.Pushed, 3) * 1.5f + (a.Bounce ? 3 : 0) + (a.OppLost ? 3 : 0); key = a.T + 0.15f;
                    what = a.Bounce ? "разлом отбил мяч" : a.OppLost ? "разлом выбил мяч у соперника" : "разлом раскидал соперников";
                    break;
            }
            s += (float)rnd.NextDouble() * 0.5f;
            if (!byHero.TryGetValue(a.H, out var list)) byHero[a.H] = list = new List<Highlight>();
            if (list.Count >= ht.perHero && s <= list[list.Count - 1].Score) return;

            float t1 = a.Goal ? a.End : Mathf.Min(a.End, Mathf.Max((a.OutT >= 0 ? a.OutT : a.T) + 1.3f + (a.H == Hero.Shade ? 0.6f : 0), a.T + 2f));
            var hl = new Highlight
            {
                Ability = true, M = cur, Rec = ring.CopyFrom(a.T - ht.abilityLead), Hero = a.H, Subject = a.P, Scorer = a.P,
                T0 = a.T - ht.abilityLead, T1 = Mathf.Min(t1, t), KeyT = key, Follow = follow, GoalT = a.T,
                Score = s, MatchNo = Done + 1, Desc = HeroName(a.H) + ": " + what + (a.Goal ? ", гол" : "")
            };
            hl.Rec.KeepFrames = true; hl.Rec.WithState = true;
            int at = list.FindIndex(h => h.Score < s);
            if (at < 0) list.Add(hl); else list.Insert(at, hl);
            if (list.Count > ht.perHero) list.RemoveAt(list.Count - 1);
            Moments++;
        }

        /// <summary>Оценить гол: чем эффектнее, тем больше баллов.</summary>
        void Finish(Pending g)
        {
            var fr = ring.Frames;
            int gi = fr.Count - 1;
            while (gi > 0 && fr[gi].T > g.T) gi--;          // кадр гола
            float goalY = g.Team == Team.Home ? 0 : Field.FH;  // хозяева забивают в ворота у Y = 0
            // удар: последний кадр до гола, где мяч был у кого-то
            int ki = gi;
            while (ki > 0 && fr[ki].B.Owner == null && fr[ki].T > g.T - 6) ki--;
            var shooter = fr[ki].B.Owner;
            var kicker = fr[gi].B.Kicker;
            SimPlayer scorer = kicker != null && kicker.Team == g.Team ? kicker : shooter != null && shooter.Team == g.Team ? shooter : null;
            bool own = kicker != null && kicker.Team != g.Team;
            float dist = shooter != null && fr[ki].B.Owner != null ? Mathf.Abs(fr[ki].B.Y - goalY) : 0;
            // полёт мяча: закрутка и высота
            float spin = 0, top = 0;
            for (int i = ki; i <= gi; i++) { spin = Mathf.Max(spin, Mathf.Abs(fr[i].B.Spin)); top = Mathf.Max(top, fr[i].B.Z); }
            // передачи перед ударом
            int passes = 0; var prev = shooter;
            for (int i = ki; i >= 0 && fr[i].T > g.T - 7 && prev != null; i--)
            {
                var o = fr[i].B.Owner;
                if (o == null || o == prev) continue;
                if (o.Team != g.Team) break;
                passes++; prev = o;
            }
            // способности в розыгрыше
            bool hook = false, wall = false, invis = false, steal = false;
            for (int i = 0; i <= gi; i++)
            {
                var f = fr[i];
                if (f.T < g.T - 6) continue;
                foreach (var h in f.HCopy) if (h.Owner != null && h.Owner.Team == g.Team) hook = true;
                if (f.WCopy.Length > 0) wall = true;
                if (f.T > g.T - 4) foreach (var p in f.P) if (p.Team == g.Team && p.InvisT > 0) invis = true;
                if (f.B.LastHow == "выбил рывком" && f.B.Last != null && f.B.Last.Team == g.Team) steal = true;
            }
            string how = g.How ?? "";
            bool fire = how.Contains("огнен"), lob = how == "навес" || top > 120, curve = !fire && spin > 0.3f, feet = how == "занёс у ног";

            float score = 2
                + (fire ? 5 : 0) + (curve ? 4 : 0) + (lob ? 3 : 0)
                + Mathf.Clamp((dist - 400) / 250, 0, 4)
                + Mathf.Min(passes, 4) * 1.2f
                + (hook ? 2 : 0) + (wall ? 1.5f : 0) + (invis ? 1.5f : 0) + (steal ? 1.2f : 0)
                + (own ? 1 : 0) - (feet ? 2 : 0)
                + (float)rnd.NextDouble() * 0.5f;

            if (goals.Count >= ht.keep && score <= goals[goals.Count - 1].Score) return;

            var parts = new List<string>();
            if (own) parts.Add("автогол");
            if (fire) parts.Add("огненный удар"); else if (curve) parts.Add("кручёный"); else if (lob) parts.Add("навес"); else if (feet) parts.Add("занёс в ворота");
            if (dist > 0) parts.Add(Mathf.RoundToInt(dist / 100) + " м");
            if (passes > 0) parts.Add(passes + (passes == 1 ? " пас" : passes < 5 ? " паса" : " пасов"));
            if (hook) parts.Add("крюк"); if (wall) parts.Add("разлом"); if (invis) parts.Add("невидимость"); if (steal) parts.Add("отбор рывком");
            var who = scorer ?? kicker;
            var hl = new Highlight
            {
                M = cur, Rec = ring.CopyFrom(g.T - KEEP), GoalT = g.T, Scorer = scorer, Score = score, MatchNo = Done + 1,
                Desc = (who != null ? HeroName(who.Hero) : "?") + ": " + string.Join(", ", parts)
            };
            hl.Rec.KeepFrames = true; hl.Rec.WithState = true;
            int at = goals.FindIndex(b => b.Score < score);
            if (at < 0) goals.Add(hl); else goals.Insert(at, hl);
            if (goals.Count > ht.keep) goals.RemoveAt(goals.Count - 1);
        }
    }
}
