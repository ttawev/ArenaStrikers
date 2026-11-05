// Хайлайты ботов для клипов: прогоняет матчи ботов 4 на 4 без графики (во много раз быстрее реального времени),
// оценивает каждый гол и оставляет лучшие — вместе с записью последних секунд перед ним (GoalReplay).
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
    }

    public class HighlightScan
    {
        const float DT = 1f / 120, KEEP = 7.5f, AFTER = 2f;
        static readonly Hero[] Heroes = { Hero.Fire, Hero.Hook, Hero.Teleport, Hero.Curve, Hero.Shade, Hero.Wall };
        public static string HeroName(Hero h) =>
            h == Hero.Fire ? "Жар" : h == Hero.Hook ? "Багор" : h == Hero.Teleport ? "Миг" : h == Hero.Curve ? "Вихрь" : h == Hero.Shade ? "Тень" : "Стена";

        readonly Tuning tuning; readonly HighlightTuning ht; readonly System.Random rnd;
        public readonly List<Highlight> Best = new List<Highlight>();
        public int Done, Goals;
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
            ring = new GoalReplay(); t = 0; pending.Clear();
        }

        void Step()
        {
            cur.Step(DT, none); t += DT;
            ring.Record(cur, t, KEEP + AFTER + 0.5f);
            foreach (var e in cur.Events) if (e.Kind == FxKind.Goal) { pending.Add(new Pending { T = t, Team = e.Team, How = cur.Ball.LastHow }); Goals++; }
            cur.Events.Clear(); cur.Log.Clear();
            for (int i = pending.Count - 1; i >= 0; i--)
                if (t >= pending[i].T + AFTER) { Finish(pending[i]); pending.RemoveAt(i); }
            if (cur.Phase == MatchPhase.Ended)
            {
                foreach (var p in pending) Finish(p);
                pending.Clear(); cur = null; ring = null; Done++;
            }
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

            if (Best.Count >= ht.keep && score <= Best[Best.Count - 1].Score) return;

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
            int at = Best.FindIndex(b => b.Score < score);
            if (at < 0) Best.Add(hl); else Best.Insert(at, hl);
            if (Best.Count > ht.keep) Best.RemoveAt(Best.Count - 1);
        }
    }
}
