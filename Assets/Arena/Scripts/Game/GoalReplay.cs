// Повтор гола для клипов: запоминает последние секунды матча (состояние героев, мяча, крюков, разломов и события-эффекты)
// и после гола проигрывает их ещё раз замедленно. Симуляция на время повтора стоит; в конце всё возвращается как было.
using System;
using System.Collections.Generic;
using System.Reflection;
using Arena.Sim;

namespace Arena.Game
{
    public class GoalReplay
    {
        class Frame
        {
            public float T;
            public SimPlayer[] P; public SimBall B;
            public Hook[] HSrc, HCopy; public IceWall[] WSrc, WCopy;
            public SimEvent[] E;
        }

        static readonly Func<object, object> Clone =
            (Func<object, object>)Delegate.CreateDelegate(typeof(Func<object, object>), typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic));
        static readonly Dictionary<Type, FieldInfo[]> fieldsOf = new Dictionary<Type, FieldInfo[]>();

        static void CopyInto(object from, object to)
        {
            var t = from.GetType();
            if (!fieldsOf.TryGetValue(t, out var fs))
            {
                var list = new List<FieldInfo>();
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    if (!f.IsInitOnly) list.Add(f); // readonly — общие объекты (мозг бота, списки), их не трогаем
                fs = fieldsOf[t] = list.ToArray();
            }
            foreach (var f in fs) f.SetValue(to, f.GetValue(from));
        }

        readonly List<Frame> frames = new List<Frame>();
        readonly Dictionary<Hook, Hook> hookStand = new Dictionary<Hook, Hook>();
        readonly Dictionary<IceWall, IceWall> wallStand = new Dictionary<IceWall, IceWall>();

        public bool Active { get; private set; }
        public bool HasFrames => frames.Count > 1;

        /// <summary>Запомнить состояние после шага симуляции (вместе с событиями этого шага).</summary>
        public void Record(Match m, float simTime, float keepSeconds)
        {
            if (Active) return;
            var f = new Frame { T = simTime, P = new SimPlayer[m.Players.Count], B = (SimBall)Clone(m.Ball), E = m.Events.ToArray() };
            for (int i = 0; i < f.P.Length; i++) f.P[i] = (SimPlayer)Clone(m.Players[i]);
            f.HSrc = m.Hooks.ToArray(); f.HCopy = new Hook[f.HSrc.Length];
            for (int i = 0; i < f.HSrc.Length; i++) f.HCopy[i] = (Hook)Clone(f.HSrc[i]);
            f.WSrc = m.Walls.ToArray(); f.WCopy = new IceWall[f.WSrc.Length];
            for (int i = 0; i < f.WSrc.Length; i++) f.WCopy[i] = (IceWall)Clone(f.WSrc[i]);
            frames.Add(f);
            int drop = 0;
            while (drop < frames.Count - 1 && frames[drop].T < simTime - keepSeconds) drop++;
            if (drop > 0) frames.RemoveRange(0, drop);
        }

        public void Clear() { if (!Active) frames.Clear(); }

        // ---------- проигрывание ----------
        Frame saved; List<Hook> savedHooks; List<IceWall> savedWalls;
        float clock, endT, hold; int next;

        public bool Begin(Match m, float seconds, float holdEnd)
        {
            if (Active || frames.Count < 2 || frames[0].P.Length != m.Players.Count) return false;
            saved = new Frame { P = new SimPlayer[m.Players.Count], B = (SimBall)Clone(m.Ball) };
            for (int i = 0; i < saved.P.Length; i++) saved.P[i] = (SimPlayer)Clone(m.Players[i]);
            savedHooks = new List<Hook>(m.Hooks); savedWalls = new List<IceWall>(m.Walls);
            endT = frames[frames.Count - 1].T;
            clock = Math.Max(frames[0].T, endT - seconds);
            next = 0;
            while (next < frames.Count - 1 && frames[next + 1].T <= clock) next++;
            hold = holdEnd;
            hookStand.Clear(); wallStand.Clear();
            Active = true;
            Apply(m, frames[next]);
            return true;
        }

        /// <summary>Продвинуть повтор; fx получает события (вспышки, удары) пройденных кадров. false — повтор закончился.</summary>
        public bool Tick(Match m, float dt, Action<SimEvent> fx)
        {
            if (!Active) return false;
            clock += dt;
            bool moved = false;
            while (next < frames.Count - 1 && frames[next + 1].T <= clock)
            {
                next++; moved = true;
                foreach (var e in frames[next].E) fx(e);
            }
            if (moved) Apply(m, frames[next]);
            if (clock > endT + hold) { End(m); return false; }
            return true;
        }

        /// <summary>Доля просмотренного повтора 0..1 (для полоски).</summary>
        public float Progress => endT + hold - frames[0].T <= 0 ? 1 : (clock - frames[0].T) / (endT + hold - frames[0].T);

        public void End(Match m)
        {
            if (!Active) return;
            for (int i = 0; i < saved.P.Length; i++) CopyInto(saved.P[i], m.Players[i]);
            CopyInto(saved.B, m.Ball);
            m.Hooks.Clear(); m.Hooks.AddRange(savedHooks);
            m.Walls.Clear(); m.Walls.AddRange(savedWalls);
            Active = false; saved = null;
            frames.Clear(); hookStand.Clear(); wallStand.Clear();
        }

        void Apply(Match m, Frame f)
        {
            for (int i = 0; i < f.P.Length; i++) CopyInto(f.P[i], m.Players[i]);
            CopyInto(f.B, m.Ball);
            // у каждого крюка/разлома в повторе — один постоянный объект, чтобы эффекты не считали его каждый кадр новым
            m.Hooks.Clear();
            for (int i = 0; i < f.HSrc.Length; i++)
            {
                if (!hookStand.TryGetValue(f.HSrc[i], out var h)) hookStand[f.HSrc[i]] = h = new Hook();
                CopyInto(f.HCopy[i], h); m.Hooks.Add(h);
            }
            m.Walls.Clear();
            for (int i = 0; i < f.WSrc.Length; i++)
            {
                if (!wallStand.TryGetValue(f.WSrc[i], out var w)) wallStand[f.WSrc[i]] = w = new IceWall();
                CopyInto(f.WCopy[i], w); m.Walls.Add(w);
            }
        }
    }
}
