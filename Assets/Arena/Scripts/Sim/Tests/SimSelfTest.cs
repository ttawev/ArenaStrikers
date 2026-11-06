// Автотесты симуляции матча (чистый C#, без Unity).
// 1) Сценарии: голы, штанга, перекладина, способности, рывок, золотой гол, детерминизм...
// 2) «Прогон»: сотни матчей ботов и случайно жмущих кнопки «людей» во всех форматах; на каждом тике
//    проверяются инварианты (числа конечные, герои в поле, мяч у владельца, мяч не завис, матч кончается...).
// Запуск: Unity — меню «Arena → Автотесты симуляции» (отчёт в Logs/arena-tests.log);
//         без Unity — Tools/SimTests: dotnet run -c Release
using System;
using System.Collections.Generic;
using System.Text;
using static Arena.Sim.Field;

namespace Arena.Sim
{
    public static class SimSelfTest
    {
        public class Result
        {
            public int Passed, Failed;
            public readonly List<string> Failures = new List<string>();
            public readonly Dictionary<string, int> IssueCount = new Dictionary<string, int>();
            public readonly Dictionary<string, string> IssueExample = new Dictionary<string, string>();
            public readonly StringBuilder Report = new StringBuilder();
            public void Issue(string kind, string example)
            {
                IssueCount.TryGetValue(kind, out int n); IssueCount[kind] = n + 1;
                if (!IssueExample.ContainsKey(kind)) IssueExample[kind] = example;
            }
        }

        const float DT = 1f / 120;
        static Action<string> outp;
        static Result res;

        public static Result RunAll(bool quick, Action<string> output)
        {
            outp = output ?? (s => { }); res = new Result();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Say("=== Автотесты симуляции SpellStrike ===");
            Say("--- Сценарии ---");
            RunScenarios();
            Say("--- Прогон матчей ---");
            Soak(quick ? 4 : 16);
            Say("");
            Say(string.Format("Итог: сценариев пройдено {0}, провалено {1}; время {2:0.0} с", res.Passed, res.Failed, sw.Elapsed.TotalSeconds));
            foreach (var f in res.Failures) Say("  ПРОВАЛ: " + f);
            if (res.IssueCount.Count == 0) Say("Нарушений инвариантов в прогоне нет.");
            else
            {
                Say("Нарушения инвариантов в прогоне (сколько раз, первый пример):");
                foreach (var kv in res.IssueCount) Say("  " + kv.Key + " — " + kv.Value + " раз; напр.: " + res.IssueExample[kv.Key]);
            }
            return res;
        }

        static void Say(string s) { res.Report.AppendLine(s); outp(s); }

        // ================= сценарии =================
        static void Check(string name, Func<string> test)
        {
            string err;
            try { err = test(); }
            catch (Exception e) { err = "исключение: " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace; }
            if (err == null) { res.Passed++; Say("  ок      " + name); }
            else { res.Failed++; res.Failures.Add(name + ": " + err); Say("  ПРОВАЛ  " + name + ": " + err); }
        }

        /// <summary>Матч без выхода команд и паузы; все герои — люди в своих слотах (стоят, пока не дать ввод).</summary>
        /// <summary>Настройки для прогона (консольный запуск может подменить их для подбора баланса).</summary>
        public static Func<Tuning> MakeTuning = () => new Tuning();

        static Match Fresh(GameMode mode, Hero hero, int seed = 1, bool allHumans = true)
        {
            var m = new Match(new Tuning(), seed);
            m.Setup(mode, hero, 1);
            if (allHumans) for (int i = 0; i < m.Players.Count; i++) m.Players[i].HumanSlot = i;
            m.Freeze = 0; m.Intro = 0;
            return m;
        }
        static readonly InputCommand[] noIn = new InputCommand[8];
        static void Run(Match m, float sec, InputCommand[] inp = null) { int n = (int)(sec / DT); for (int i = 0; i < n; i++) m.Step(DT, inp ?? noIn); }
        static void Park(Match m) { for (int i = 0; i < m.Players.Count; i++) { var p = m.Players[i]; p.X = 80 + 60 * i; p.Y = FH / 2 + (i % 2 == 0 ? 300 : -300); p.Vx = p.Vy = 0; } }
        static void BallAt(Match m, float x, float y, float vx, float vy, float z = 0, float vz = 0)
        { var b = m.Ball; b.Owner = null; b.Hook = null; b.X = x; b.Y = y; b.Vx = vx; b.Vy = vy; b.Z = z; b.Vz = vz; b.Spin = 0; b.Fire = 0; b.Lock = 0; }
        static void GiveBall(Match m, SimPlayer p)
        { var b = m.Ball; b.Owner = p; b.Hook = null; b.Lock = 0; b.GlueAng = MathF.Atan2(p.Fy, p.Fx); b.X = p.X + p.Fx * (R + BR + 1); b.Y = p.Y + p.Fy * (R + BR + 1); b.Vx = b.Vy = 0; b.Z = b.Vz = 0; p.GrabCd = 0; }

        static void RunScenarios()
        {
            Check("Гол: мяч по центру ворот", () =>
            {
                var m = Fresh(GameMode.OneVsOne, Hero.Fire); Park(m);
                BallAt(m, FW / 2, 80, 0, -1500); Run(m, 0.5f);
                return m.ScoreHome == 1 ? null : "счёт " + m.ScoreHome + ":" + m.ScoreAway;
            });
            Check("Гол в другие ворота засчитывается гостям", () =>
            {
                var m = Fresh(GameMode.OneVsOne, Hero.Fire); Park(m);
                BallAt(m, FW / 2, FH - 80, 0, 1500); Run(m, 0.5f);
                return m.ScoreAway == 1 ? null : "счёт " + m.ScoreHome + ":" + m.ScoreAway;
            });
            Check("Нет гола: мимо штанги", () =>
            {
                var m = Fresh(GameMode.OneVsOne, Hero.Fire); Park(m);
                BallAt(m, GX1 - 50, 80, 0, -1500); Run(m, 1);
                return m.ScoreHome + m.ScoreAway == 0 ? null : "засчитан гол";
            });
            Check("Нет гола: быстрый мяч наискосок мимо створа", () =>
            {
                var m = Fresh(GameMode.OneVsOne, Hero.Fire); Park(m);
                BallAt(m, GX1 - 40, 30, 300, -2200); Run(m, 1);
                return m.ScoreHome + m.ScoreAway == 0 ? null : "засчитан гол";
            });
            Check("Нет гола: выше перекладины", () =>
            {
                var m = Fresh(GameMode.OneVsOne, Hero.Fire); Park(m);
                BallAt(m, FW / 2, 120, 0, -1200, 150, 0); m.Ball.Vz = 0; Run(m, 0.12f);
                return m.ScoreHome + m.ScoreAway == 0 ? null : "засчитан гол";
            });
            Check("Отскок от штанги", () =>
            {
                var m = Fresh(GameMode.OneVsOne, Hero.Fire); Park(m);
                BallAt(m, GX1, 120, 0, -1500); Run(m, 0.3f);
                return m.Ball.Vy > 0 || m.ScoreHome == 0 ? null : "мяч прошёл сквозь штангу";
            });
            Check("Удар с мячом у ног: мяч летит, сила растёт с зарядом", () =>
            {
                float Shot(float hold)
                {
                    var m = Fresh(GameMode.OneVsOne, Hero.Fire); var p = m.Players[0]; p.X = FW / 2; p.Y = FH * 0.7f; p.Fx = 0; p.Fy = -1;
                    m.Players[1].X = 100; m.Players[1].Y = 100;
                    GiveBall(m, p);
                    var inp = new InputCommand[8]; inp[0].Kick = true; Run(m, hold, inp); inp[0].Kick = false; Run(m, DT * 2, inp);
                    return M.Len(m.Ball.Vx, m.Ball.Vy);
                }
                float weak = Shot(0.05f), strong = Shot(0.8f);
                return weak > 500 && strong > weak * 1.4f ? null : "слабый " + (int)weak + ", сильный " + (int)strong;
            });
            Check("Навес поднимает мяч и опускает его", () =>
            {
                var m = Fresh(GameMode.OneVsOne, Hero.Fire); var p = m.Players[0]; p.X = FW / 2; p.Y = FH * 0.7f; p.Fy = -1; p.Fx = 0;
                m.Players[1].X = 100; m.Players[1].Y = 100; GiveBall(m, p);
                var inp = new InputCommand[8]; inp[0].Lob = true; Run(m, 0.5f, inp); inp[0].Lob = false; Run(m, DT * 2, inp);
                float z = 0; for (int i = 0; i < 120; i++) { m.Step(DT, noIn); z = Math.Max(z, m.Ball.Z); }
                Run(m, 3);
                return z > HEAD && m.Ball.Z == 0 ? null : "макс. высота " + (int)z + ", сейчас " + (int)m.Ball.Z;
            });
            Check("Рывок выбивает мяч и оглушает соперника", () =>
            {
                var m = Fresh(GameMode.OneVsOne, Hero.Fire); var a = m.Players[0]; var b = m.Players[1];
                a.X = FW / 2; a.Y = FH / 2 + 120; a.Fx = 0; a.Fy = -1; b.X = FW / 2; b.Y = FH / 2; b.Fx = 0; b.Fy = 1;
                GiveBall(m, b);
                var inp = new InputCommand[8]; inp[0].MoveY = -1; inp[0].Dash = true; m.Step(DT, inp); inp[0].Dash = false; Run(m, 0.25f, inp);
                return m.Ball.Owner != b && b.Stun > 0 ? null : "владелец " + (m.Ball.Owner == null ? "нет" : m.Ball.Owner.Hero.ToString()) + ", оглушение " + b.Stun;
            });
            Check("Мяч у ног не заносится в свои ворота", () =>
            {
                var m = Fresh(GameMode.OneVsOne, Hero.Fire); var p = m.Players[0]; p.X = FW / 2; p.Y = FH - 60; p.Fx = 0; p.Fy = 1;
                m.Players[1].X = 100; m.Players[1].Y = 100; GiveBall(m, p);
                var inp = new InputCommand[8]; inp[0].MoveY = 1; Run(m, 1.5f, inp);
                return m.ScoreAway == 0 ? null : "автогол";
            });
            foreach (var hero in new[] { Hero.Fire, Hero.Hook, Hero.Teleport, Hero.Curve, Hero.Shade, Hero.Wall })
            {
                var h = hero;
                Check("Способность работает: " + h, () => AbilityWorks(h));
            }
            Check("Золотой гол при ничьей после основного времени", () =>
            {
                var m = Fresh(GameMode.OneVsOne, Hero.Fire); Park(m);
                m.Clock = 0.05f; Run(m, 0.2f);
                if (!m.Golden || m.Phase != MatchPhase.Playing) return "золотой гол не начался";
                Run(m, m.T.goldenPause + 0.2f);
                BallAt(m, FW / 2, 80, 0, -1500); Run(m, 0.5f); Run(m, m.T.goalPause + 0.2f);
                return m.Phase == MatchPhase.Ended && m.ScoreHome == 1 ? null : "после гола матч не кончился (фаза " + m.Phase + ")";
            });
            Check("Победа по голам заканчивает матч", () =>
            {
                var m = Fresh(GameMode.OneVsOne, Hero.Fire); m.WinGoals = 2;
                for (int g = 0; g < 2; g++) { Park(m); BallAt(m, FW / 2, 80, 0, -1500); Run(m, 0.5f); Run(m, m.T.goalPause + 1.2f); }
                return m.Phase == MatchPhase.Ended ? null : "матч идёт, счёт " + m.ScoreHome;
            });
            Check("Гол: медленный мяч целиком закатился за линию", () =>
            {
                var m = Fresh(GameMode.OneVsOne, Hero.Fire); Park(m);
                BallAt(m, FW / 2, FH - 30, 0, 120); Run(m, 3);
                return m.ScoreAway == 1 ? null : "мяч остановился в воротах без гола (y=" + m.Ball.Y.ToString("0") + ")";
            });
            Check("Крюк по сопернику с мячом: мяч выпадает, соперник оглушён", () =>
            {
                var m = Fresh(GameMode.OneVsOne, Hero.Hook); var p = m.Players[0]; var v = m.Players[1];
                p.X = FW / 2; p.Y = FH * 0.6f; p.Fx = 0; p.Fy = -1; v.X = FW / 2; v.Y = p.Y - 300; v.Fx = 0; v.Fy = -1; // мяч у соперника с дальней стороны
                GiveBall(m, v);
                var inp = new InputCommand[8]; inp[0].Ability = true; m.Step(DT, inp); inp[0].Ability = false;
                bool stunnedWithBall = false;
                for (int i = 0; i < 150; i++) { m.Step(DT, inp); if (v.Stun > 0.05f && m.Ball.Owner == v) stunnedWithBall = true; }
                return !stunnedWithBall ? null : "оглушённый соперник держал мяч";
            });
            Check("Стамина: бег тратит, стояние восстанавливает", () =>
            {
                var m = Fresh(GameMode.OneVsOne, Hero.Fire); var p = m.Players[0]; p.X = FW / 2; p.Y = 60; m.Players[1].X = 100; m.Players[1].Y = 100;
                var inp = new InputCommand[8];
                for (int i = 0; i < 10; i++) { inp[0].MoveY = 1; Run(m, 0.5f, inp); inp[0].MoveY = -1; Run(m, 0.5f, inp); } // 10 с бега туда-обратно
                float afterRun = p.Stamina;
                inp[0].MoveY = 0; Run(m, 3, inp);
                float afterRest = p.Stamina;
                float want = 1 - m.T.staminaRun * 10;
                if (Math.Abs(afterRun - want) > 0.05f) return "после 10 с бега " + afterRun.ToString("0.00") + " (ждали ~" + want.ToString("0.00") + ")";
                return afterRest >= Math.Min(1, afterRun + 0.3f) - 0.01f ? null : "за 3 с отдыха " + afterRun.ToString("0.00") + " → " + afterRest.ToString("0.00");
            });
            Check("Стамина: рывок и сильный удар тратят, слабый удар — нет", () =>
            {
                var m = Fresh(GameMode.OneVsOne, Hero.Fire); var p = m.Players[0]; p.X = FW / 2; p.Y = FH * 0.7f; p.Fx = 0; p.Fy = -1; m.Players[1].X = 100; m.Players[1].Y = 100;
                var inp = new InputCommand[8];
                inp[0].Dash = true; m.Step(DT, inp); inp[0].Dash = false;
                float d = 1 - p.Stamina; Run(m, 1, inp);
                p.Stamina = 1; GiveBall(m, p); inp[0].Kick = true; m.Step(DT, inp); inp[0].Kick = false; m.Step(DT, inp);
                float weak = 1 - p.Stamina;
                p.Stamina = 1; p.KickCd = 0; GiveBall(m, p); inp[0].Kick = true; Run(m, 0.75f, inp); inp[0].Kick = false; m.Step(DT, inp);
                float strong = 1 - p.Stamina;
                return d > 0.15f && weak < 0.01f && strong > 0.08f ? null : "рывок " + d.ToString("0.00") + ", слабый " + weak.ToString("0.000") + ", сильный " + strong.ToString("0.00");
            });
            Check("Стамина: без сил подкат (рывок) не срабатывает", () =>
            {
                var m = Fresh(GameMode.OneVsOne, Hero.Fire); var p = m.Players[0]; p.X = FW / 2; p.Y = FH / 2; m.Players[1].X = 100; m.Players[1].Y = 100;
                p.Stamina = m.T.staminaDash * 0.5f;
                var inp = new InputCommand[8]; inp[0].Dash = true; m.Step(DT, inp);
                if (p.DashT > 0) return "рывок сработал при силах " + p.Stamina.ToString("0.00");
                p.Stamina = 1; inp[0].Dash = true; m.Step(DT, inp);
                return p.DashT > 0 ? null : "с полными силами рывок не сработал";
            });
            Check("Стамина: без сил герой бежит медленнее", () =>
            {
                float Speed(float st)
                {
                    var m = Fresh(GameMode.OneVsOne, Hero.Fire); var p = m.Players[0]; p.X = FW / 2; p.Y = FH - 100; m.Players[1].X = 100; m.Players[1].Y = 100;
                    var inp = new InputCommand[8]; inp[0].MoveY = -1; Run(m, 0.3f, inp); p.Stamina = st; Run(m, 0.3f, inp);
                    return M.Len(p.Vx, p.Vy);
                }
                float full = Speed(1), empty = Speed(0);
                return empty < full * 0.85f && empty > full * 0.6f ? null : "полные силы " + (int)full + ", без сил " + (int)empty;
            });
            Check("Разводка: места внутри команды перемешиваются после голов", () =>
            {
                var m = Fresh(GameMode.FourVsFour, Hero.Fire, 9);
                var me = m.Players[0]; var spots = new HashSet<int>(); spots.Add(me.Spot);
                for (int g = 0; g < 6; g++)
                {
                    m.Freeze = 0; Park(m); BallAt(m, FW / 2, 80, 0, -1500); Run(m, 0.5f); Run(m, m.T.goalPause + 0.2f);
                    if (m.Phase != MatchPhase.Playing) break;
                    spots.Add(me.Spot);
                    foreach (var team in new[] { Team.Home, Team.Away })
                    {
                        var used = new HashSet<int>();
                        foreach (var p in m.Players) if (p.Team == team && !used.Add(p.Spot)) return "два героя на одном месте";
                        foreach (var p in m.Players) if ((p.Team == Team.Home) != (p.Y > FH / 2)) return "герой не на своей половине: " + p.Hero + " " + p.Team + " y=" + p.Y.ToString("0") + ", место " + p.Spot + ", гол " + g + ", пауза " + m.Freeze.ToString("0.00") + ", счёт " + m.ScoreHome;
                    }
                }
                return spots.Count >= 2 ? null : "игрок всё время на месте " + me.Spot;
            });
            Check("Выход команд: после него герои там, куда бежали (без скачка)", () =>
            {
                var m = new Match(new Tuning(), 3); m.PlayIntro = true; m.Setup(GameMode.FourVsFour, Hero.Fire, 1);
                for (int i = 0; i < m.Players.Count; i++) m.Players[i].HumanSlot = i;
                var ko = new List<(float, float)>(); foreach (var p in m.Players) ko.Add((p.KoX, p.KoY));
                int n = 0; while (m.Intro > 0 && n++ < 100000) m.Step(DT, noIn);
                for (int i = 0; i < m.Players.Count; i++)
                    if (M.Len(m.Players[i].X - ko[i].Item1, m.Players[i].Y - ko[i].Item2) > 2) return m.Players[i].Hero + " перескочил на другое место";
                return null;
            });
            Check("Детерминизм: одно зерно — одинаковый матч", () =>
            {
                string a = PlayHash(GameMode.FourVsFour, 77), b = PlayHash(GameMode.FourVsFour, 77);
                return a == b ? null : "разные итоги: " + a + " / " + b;
            });
        }

        static string AbilityWorks(Hero hero)
        {
            var m = Fresh(GameMode.OneVsOne, hero); var p = m.Players[0]; var foe = m.Players[1];
            p.X = FW / 2; p.Y = FH * 0.6f; p.Fx = 0; p.Fy = -1; foe.X = FW / 2; foe.Y = FH * 0.6f - 300; foe.Fx = 0; foe.Fy = 1;
            var inp = new InputCommand[8];
            switch (hero)
            {
                case Hero.Fire:
                    GiveBall(m, p); inp[0].Ability = true; m.Step(DT, inp); inp[0].Ability = false;
                    if (p.FireT <= 0) return "замах не начался";
                    Run(m, m.T.fireDelay + 0.05f, inp);
                    return m.Ball.Owner == null && M.Len(m.Ball.Vx, m.Ball.Vy) > 1200 ? null : "огненный удар не вылетел";
                case Hero.Hook:
                    BallAt(m, FW / 2, p.Y - 300, 0, 0); foe.X = 200; foe.Y = 200;
                    inp[0].MoveY = -0.01f; inp[0].Ability = true; m.Step(DT, inp); inp[0].Ability = false; inp[0].MoveY = 0;
                    if (m.Hooks.Count == 0) return "крюк не брошен";
                    Run(m, 1.2f, inp);
                    return m.Ball.Owner == p ? null : "мяч не притянут";
                case Hero.Teleport:
                {
                    float y0 = p.Y; inp[0].Ability = true; m.Step(DT, inp);
                    return y0 - p.Y > m.T.tpDist * 0.8f ? null : "сдвиг " + (y0 - p.Y);
                }
                case Hero.Curve:
                    GiveBall(m, p); inp[0].CurveLeft = true; Run(m, 0.4f, inp); inp[0].CurveLeft = false; m.Step(DT, inp);
                    return m.Ball.Owner == null && m.Ball.Spin != 0 ? null : "мяч без закрутки";
                case Hero.Shade:
                    inp[0].Ability = true; m.Step(DT, inp);
                    return p.Invisible ? null : "не стал невидимым";
                default:
                    inp[0].Ability = true; m.Step(DT, inp); inp[0].Ability = false; Run(m, 0.3f, inp);
                    return m.Walls.Count == 1 && m.Walls[0].Len > m.T.wallLen * 0.9f ? null : "разлом не вырос";
            }
        }

        static string PlayHash(GameMode mode, int seed)
        {
            var m = new Match(new Tuning(), seed); m.Setup(mode, Hero.Hook, 2); m.Players[0].HumanSlot = -1;
            int n = 0; while (m.Phase == MatchPhase.Playing && n++ < 200000) m.Step(DT, noIn);
            double h = 0; foreach (var p in m.Players) h += p.X * 1.3 + p.Y * 0.7;
            return m.ScoreHome + ":" + m.ScoreAway + "/" + h.ToString("0.000") + "/" + m.Ball.X.ToString("0.000");
        }

        // ================= прогон матчей =================
        class RandomHuman
        {
            readonly Random rnd; float t, holdK, holdL, holdC; int curve; public InputCommand C;
            public RandomHuman(int seed) { rnd = new Random(seed); }
            float F() => (float)rnd.NextDouble();
            public InputCommand Next(float dt)
            {
                C.Dash = C.Ability = C.BallToMe = false;
                t -= dt;
                if (t <= 0)
                {
                    t = 0.1f + F() * 0.5f;
                    if (F() < 0.12f) { C.MoveX = C.MoveY = 0; }
                    else { float a = F() * MathF.PI * 2, l = 0.4f + F() * 0.6f; C.MoveX = MathF.Cos(a) * l; C.MoveY = MathF.Sin(a) * l; }
                    if (F() < 0.3f) holdK = F() * 0.9f;
                    if (F() < 0.12f) holdL = F() * 1.1f;
                    if (F() < 0.15f) { holdC = F() * 0.8f; curve = F() < 0.5f ? -1 : 1; }
                    C.CallPass = F() < 0.1f;
                }
                if (F() < 0.004f) C.Dash = true;
                if (F() < 0.004f) C.Ability = true;
                holdK -= dt; holdL -= dt; holdC -= dt;
                C.Kick = holdK > 0; C.Lob = holdL > 0 && !C.Kick;
                C.CurveLeft = holdC > 0 && curve < 0 && !C.Kick; C.CurveRight = holdC > 0 && curve > 0 && !C.Kick;
                return C;
            }
        }

        class Acc
        {
            public int Matches, Goals, Ticks, Golden, Draws, Unfinished; public double StaminaSum; public int StaminaN;
            public double Secs; public double TiredN;
        }

        static void Soak(int perCombo)
        {
            tiredLine = MakeTuning().staminaTired;
            var modes = new[] { GameMode.OneVsOne, GameMode.TwoVsTwo, GameMode.ThreeVsThree, GameMode.FourVsFour, GameMode.Training };
            int seed = 5000;
            foreach (var mode in modes)
            {
                var acc = new Acc();
                for (int variant = 0; variant < 3; variant++)      // 0 — только боты, 1 — человек в слоте 0, 2 — все «люди» (жмут случайно)
                    for (int i = 0; i < perCombo; i++)
                    {
                        if (mode == GameMode.Training && variant == 0) continue;
                        PlayChecked(mode, variant, i % 3, (Hero)(i % 6), seed++, acc);
                    }
                float n = Math.Max(1, acc.Matches);
                Say(string.Format("  {0}: матчей {1}, голов/матч {2:0.0}, золотых голов {3}, ничьих {4}, игрового времени {5:0} мин; силы в среднем {6:0}%, уставшим {7:0.0}% времени",
                    Match.ModeName(mode), acc.Matches, acc.Goals / n, acc.Golden, acc.Draws, acc.Secs / 60,
                    100 * acc.StaminaSum / Math.Max(1, acc.StaminaN), 100 * acc.TiredN / Math.Max(1, acc.StaminaN)));
            }
        }

        static float tiredLine = 0.3f;
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        static void PlayChecked(GameMode mode, int variant, int diff, Hero hero, int seed, Acc acc)
        {
            var m = new Match(MakeTuning(), seed);
            m.PlayIntro = variant == 1;
            m.Duration = mode == GameMode.Training ? 60 : 120; // покороче, чтобы прогнать больше матчей
            m.Setup(mode, hero, diff);
            string tag = Match.ModeName(mode) + ", вариант " + variant + ", зерно " + seed;
            var humans = new List<RandomHuman>();
            if (variant == 0) m.Players[0].HumanSlot = -1;
            if (variant == 2) for (int i = 0; i < m.Players.Count; i++) if (!m.Players[i].Dummy) m.Players[i].HumanSlot = i;
            for (int i = 0; i < 8; i++) humans.Add(new RandomHuman(seed * 31 + i));
            var inp = new InputCommand[8];
            int lastH = 0, lastA = 0; float stillT = 0, netT = 0, ownT = 0; SimPlayer lastOwner = null;
            float limit = mode == GameMode.Training ? 120 : m.Duration + 400 + m.T.goldenMax; int ticks = 0, maxTicks = (int)(limit / DT);
            bool prevGolden = false;
            while (m.Phase == MatchPhase.Playing && ticks < maxTicks)
            {
                for (int i = 0; i < 8; i++) inp[i] = humans[i].Next(DT);
                try { m.Step(DT, inp); }
                catch (Exception e) { res.Issue("исключение в Step: " + e.GetType().Name + ": " + e.Message, tag + ", t=" + (ticks * DT).ToString("0.0") + "\n" + e.StackTrace); return; }
                ticks++;
                float t = ticks * DT; string at = tag + ", t=" + t.ToString("0.0") + " с";
                m.Events.Clear(); m.Log.Clear();
                if (m.Golden && !prevGolden) acc.Golden++;
                prevGolden = m.Golden;

                var b = m.Ball;
                if (!Finite(b.X) || !Finite(b.Y) || !Finite(b.Z) || !Finite(b.Vx) || !Finite(b.Vy) || !Finite(b.Vz)) { res.Issue("мяч: NaN/бесконечность", at); return; }
                if (b.Z < -0.01f) res.Issue("мяч ниже земли", at);
                if (b.X < BR - 2 || b.X > FW - BR + 2) res.Issue("мяч за боковым бортом", at + " x=" + b.X.ToString("0"));
                if (b.Y < -NET - BR - 40 || b.Y > FH + NET + BR + 40) res.Issue("мяч улетел за ворота", at + " y=" + b.Y.ToString("0"));
                if ((b.Y < -2 || b.Y > FH + 2) && (b.X < GX1 - 2 || b.X > GX2 + 2)) res.Issue("мяч за линией ворот мимо створа", at + " x=" + b.X.ToString("0") + " y=" + b.Y.ToString("0"));
                if (b.Owner != null)
                {
                    if (!m.Players.Contains(b.Owner)) res.Issue("владелец мяча не в составе", at);
                    float d = M.Len(b.X - b.Owner.X, b.Y - b.Owner.Y);
                    if (d > R + BR + 25) res.Issue("мяч оторвался от владельца", at + " d=" + d.ToString("0"));
                    if (b.Owner.Stun > 0.05f) res.Issue("оглушённый герой держит мяч", at + " " + b.Owner.Hero + " (оглушение " + b.Owner.Stun.ToString("0.00") + ", мяч: " + b.LastHow + ", на крюке: " + m.Hooks.Exists(h => h.Victim == b.Owner) + ")");
                    if (b.Owner.Dummy) res.Issue("манекен держит мяч", at);
                }
                if (b.Hook != null && !m.Hooks.Contains(b.Hook)) res.Issue("мяч на крюке, которого нет", at);
                if (m.Hooks.Count > m.Players.Count) res.Issue("крюков больше, чем игроков", at);
                if (m.Walls.Count > m.Players.Count) res.Issue("разломов больше, чем игроков", at);
                foreach (var p in m.Players)
                {
                    if (!Finite(p.X) || !Finite(p.Y) || !Finite(p.Vx) || !Finite(p.Vy) || !Finite(p.Fx) || !Finite(p.Fy)) { res.Issue("герой: NaN/бесконечность", at + " " + p.Hero); return; }
                    if (m.Intro <= 0 && (p.X < R - 3 || p.X > FW - R + 3 || p.Y < R - 3 || p.Y > FH - R + 3)) res.Issue("герой за бортом", at + " " + p.Hero + " (" + p.X.ToString("0") + ", " + p.Y.ToString("0") + ")");
                    if (p.DashCd < 0 || p.HookCd < 0 || p.FireCd < 0 || p.TpCd < 0 || p.WallCd < 0 || p.InvisCd < 0 || p.CurveCd < 0) res.Issue("отрицательная перезарядка", at + " " + p.Hero);
                    float fl = M.Len(p.Fx, p.Fy); if (fl < 0.9f || fl > 1.1f) res.Issue("взгляд героя не единичный", at + " " + p.Hero + " |F|=" + fl.ToString("0.00"));
                    float sp = M.Len(p.Vx, p.Vy); if (sp > 1500) res.Issue("герой слишком быстр", at + " " + p.Hero + " v=" + sp.ToString("0"));
                    if (p.Charge < 0 || p.Charge > 1 || p.LobCharge < 0 || p.LobCharge > 1) res.Issue("заряд вне 0..1", at);
                    if (p.Stun > 2.6f) res.Issue("слишком долгое оглушение", at + " " + p.Hero + " " + p.Stun.ToString("0.0"));
                    StaminaProbe(p, acc, at);
                }
                if (m.ScoreHome < lastH || m.ScoreAway < lastA) res.Issue("счёт уменьшился", at);
                if (m.WinGoals > 0 && mode != GameMode.Training && (m.ScoreHome > m.WinGoals || m.ScoreAway > m.WinGoals)) res.Issue("голов больше лимита", at);
                lastH = m.ScoreHome; lastA = m.ScoreAway;

                bool live = m.Freeze <= 0 && m.Intro <= 0;
                if (live && b.Owner == null && b.Hook == null && M.Len(b.Vx, b.Vy) < 3 && b.Z == 0) stillT += DT; else stillT = 0;
                if (stillT > 12 && mode != GameMode.Training && variant == 0) { res.Issue("мяч лежит без движения >12 с (боты не идут к мячу)", at + " мяч (" + b.X.ToString("0") + ", " + b.Y.ToString("0") + ")"); stillT = -1000; }
                if (live && b.Owner == null && (b.Y < -BR || b.Y > FH + BR)) netT += DT; else netT = 0;
                if (netT > 0.2f) { res.Issue("мяч застрял в воротах без гола", at + " (" + b.X.ToString("0") + ", " + b.Y.ToString("0") + ")"); netT = -1000; }
                if (live && b.Owner != null && b.Owner == lastOwner) ownT += DT; else ownT = 0;
                lastOwner = b.Owner;
                if (ownT > 30 && variant == 0 && !b.Owner.IsHuman) { res.Issue("бот держит мяч >30 с", at + " " + b.Owner.Hero); ownT = -1000; }
            }
            acc.Matches++; acc.Secs += ticks * DT; acc.Goals += m.ScoreHome + m.ScoreAway;
            if (m.Phase == MatchPhase.Playing) { acc.Unfinished++; if (mode != GameMode.Training) res.Issue("матч не закончился за отведённое время", tag); }
            else if (m.ScoreHome == m.ScoreAway) acc.Draws++;
        }

        static void StaminaProbe(SimPlayer p, Acc acc, string at)
        {
            if (!Finite(p.Stamina) || p.Stamina < -0.001f || p.Stamina > 1.001f) res.Issue("стамина вне 0..1", at + " " + p.Hero + " " + p.Stamina);
            if (p.Dummy) return;
            acc.StaminaSum += p.Stamina; acc.StaminaN++;
            if (p.Stamina < tiredLine) acc.TiredN++;
        }
    }
}
