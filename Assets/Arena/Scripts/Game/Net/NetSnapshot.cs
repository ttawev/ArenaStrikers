// Снимок матча для онлайна: хост 30 раз в секунду отправляет друзьям состояние героев, мяча, крюков, разломов
// и события (вспышки, удары, гол). У друзей матч не считается — картинка строится из снимков с плавным переходом между ними.
using System.Collections.Generic;
using Arena.Sim;
using Unity.Netcode;
using UnityEngine;

namespace Arena.Game
{
    public class Snap
    {
        public const int PF = 29; // чисел на героя (27 — стамина, 28 — отлёт от огненного мяча)
        public float T, Clock, Freeze, Intro;
        public byte ScoreH, ScoreA, Phase, BannerTeam; public bool Banner, Golden;
        public bool HasStats; public byte[] Stats = new byte[8 * 4]; // в конце матча: голы, передачи, сейвы, отборы каждого
        public uint[] Acks = new uint[8]; // по месту: последний выполненный хостом тик ввода (для предсказания у друга)
        public int N; public float[] P = new float[PF * 8]; public byte[] PFlags = new byte[8]; public sbyte[] PCurv = new sbyte[8];
        public float[] B = new float[11]; public sbyte BOwner, BLast, BFireBy; public bool BHook;
        public readonly List<HookS> Hooks = new List<HookS>(); public readonly List<WallS> Walls = new List<WallS>();
        public readonly List<EvS> Events = new List<EvS>();
        public struct HookS { public ushort Id; public sbyte Owner, Victim; public float X, Y, Dx, Dy, Dist; public byte Flags; }
        public struct WallS { public ushort Id; public sbyte Owner; public float X, Y, Tx, Ty, Len, MaxLen, Life, MaxLife; public bool Pushed; }
        public struct EvS { public SimEvent E; public sbyte Player; }
    }

    public static class NetSnapshot
    {
        static void H(FastBufferWriter w, float v) => w.WriteValueSafe(Mathf.FloatToHalf(v));
        static float H(FastBufferReader r) { r.ReadValueSafe(out ushort u); return Mathf.HalfToFloat(u); }
        static float F(FastBufferReader r) { r.ReadValueSafe(out float f); return f; }
        static byte ToB(int v) => (byte)Mathf.Clamp(v, 0, 255);
        static sbyte Idx(Match m, SimPlayer p) => p == null ? (sbyte)-1 : (sbyte)m.Players.IndexOf(p);

        // ---------- хост: запись ----------
        public static void Write(FastBufferWriter w, Match m, float simTime, List<SimEvent> evts, Dictionary<Hook, ushort> hookIds, Dictionary<IceWall, ushort> wallIds, uint[] acks)
        {
            w.WriteValueSafe(simTime); w.WriteValueSafe(m.Clock); H(w, m.Freeze);
            for (int i = 0; i < 8; i++) w.WriteValueSafe(acks != null && i < acks.Length ? acks[i] : 0u);
            w.WriteValueSafe((byte)m.ScoreHome); w.WriteValueSafe((byte)m.ScoreAway); w.WriteValueSafe((byte)m.Phase);
            w.WriteValueSafe(m.Banner != ""); w.WriteValueSafe((byte)m.BannerTeam);
            w.WriteValueSafe(m.Golden); H(w, m.Intro);
            bool stats = m.Phase == MatchPhase.Ended; w.WriteValueSafe(stats);
            if (stats)
            {
                int ns = Mathf.Min(m.Players.Count, 8); w.WriteValueSafe((byte)ns);
                for (int i = 0; i < ns; i++) { var p = m.Players[i]; w.WriteValueSafe(ToB(p.Goals)); w.WriteValueSafe(ToB(p.Assists)); w.WriteValueSafe(ToB(p.Saves)); w.WriteValueSafe(ToB(p.Steals)); }
            }
            w.WriteValueSafe((byte)m.Players.Count);
            foreach (var p in m.Players)
            {
                w.WriteValueSafe(p.X); w.WriteValueSafe(p.Y);
                H(w, p.Vx); H(w, p.Vy); H(w, p.Fx); H(w, p.Fy);
                H(w, p.DashT); H(w, p.DashCd); H(w, p.Stun); H(w, p.KickCd); H(w, p.Flash); H(w, p.GrabCd);
                H(w, p.Charge); H(w, p.LobCharge); H(w, p.CurveCharge);
                H(w, p.HookCd); H(w, p.FireCd); H(w, p.FireT); H(w, p.TpCd); H(w, p.TpBackT);
                w.WriteValueSafe(p.TpFx); w.WriteValueSafe(p.TpFy);
                H(w, p.CurveCd); H(w, p.InvisT); H(w, p.InvisCd); H(w, p.WallCd); H(w, p.CallT); H(w, p.Stamina); H(w, p.PushT);
                w.WriteValueSafe((byte)((p.Charging ? 1 : 0) | (p.Lobbing ? 2 : 0))); w.WriteValueSafe((sbyte)p.Curving);
            }
            var b = m.Ball;
            w.WriteValueSafe(b.X); w.WriteValueSafe(b.Y); w.WriteValueSafe(b.Z);
            H(w, b.Vx); H(w, b.Vy); H(w, b.Vz); H(w, b.Lock); H(w, b.Spin); H(w, b.Fire); H(w, b.FireGrace); H(w, b.GlueAng);
            w.WriteValueSafe(Idx(m, b.Owner)); w.WriteValueSafe(Idx(m, b.Last)); w.WriteValueSafe(Idx(m, b.FireBy)); w.WriteValueSafe(b.Hook != null);

            w.WriteValueSafe((byte)m.Hooks.Count);
            foreach (var h in m.Hooks)
            {
                w.WriteValueSafe(Id(hookIds, h)); w.WriteValueSafe(Idx(m, h.Owner)); w.WriteValueSafe(Idx(m, h.Victim));
                w.WriteValueSafe(h.X); w.WriteValueSafe(h.Y); H(w, h.Dx); H(w, h.Dy); H(w, h.Dist);
                w.WriteValueSafe((byte)((h.Back ? 1 : 0) | (h.HasBall ? 2 : 0) | (h.Done ? 4 : 0)));
            }
            w.WriteValueSafe((byte)m.Walls.Count);
            foreach (var x in m.Walls)
            {
                w.WriteValueSafe(Id(wallIds, x)); w.WriteValueSafe(Idx(m, x.Owner));
                w.WriteValueSafe(x.X); w.WriteValueSafe(x.Y); H(w, x.Tx); H(w, x.Ty); H(w, x.Len); H(w, x.MaxLen); H(w, x.Life); H(w, x.MaxLife);
                w.WriteValueSafe(x.BallPushed);
            }
            int n = Mathf.Min(evts.Count, 40);
            w.WriteValueSafe((byte)n);
            for (int i = evts.Count - n; i < evts.Count; i++)
            {
                var e = evts[i];
                w.WriteValueSafe((byte)e.Kind); w.WriteValueSafe(e.X); w.WriteValueSafe(e.Y); H(w, e.Amount);
                w.WriteValueSafe((byte)Mathf.Clamp(e.Count, 0, 255)); w.WriteValueSafe((byte)e.Color); w.WriteValueSafe((byte)e.Team); w.WriteValueSafe(Idx(m, e.Player));
            }
        }

        static ushort nextId = 1;
        static ushort Id<T>(Dictionary<T, ushort> ids, T o)
        {
            if (!ids.TryGetValue(o, out var id)) { id = nextId++; if (nextId == 0) nextId = 1; ids[o] = id; }
            return id;
        }

        // ---------- друг: чтение ----------
        public static Snap Read(FastBufferReader r)
        {
            var s = new Snap();
            s.T = F(r); s.Clock = F(r); s.Freeze = H(r);
            for (int i = 0; i < 8; i++) r.ReadValueSafe(out s.Acks[i]);
            r.ReadValueSafe(out s.ScoreH); r.ReadValueSafe(out s.ScoreA); r.ReadValueSafe(out s.Phase);
            r.ReadValueSafe(out s.Banner); r.ReadValueSafe(out s.BannerTeam);
            r.ReadValueSafe(out s.Golden); s.Intro = H(r);
            r.ReadValueSafe(out s.HasStats);
            if (s.HasStats)
            {
                r.ReadValueSafe(out byte ns);
                for (int i = 0; i < ns * 4; i++) { r.ReadValueSafe(out byte v); if (i < s.Stats.Length) s.Stats[i] = v; }
            }
            r.ReadValueSafe(out byte n); s.N = Mathf.Min(n, 8);
            for (int i = 0; i < n; i++)
            {
                var P = i < 8 ? s.P : new float[Snap.PF * (i + 1)]; int o = (i < 8 ? i : i) * Snap.PF;
                P[o] = F(r); P[o + 1] = F(r);
                for (int k = 2; k <= 19; k++) P[o + k] = H(r);
                P[o + 20] = F(r); P[o + 21] = F(r);
                for (int k = 22; k <= 28; k++) P[o + k] = H(r);
                r.ReadValueSafe(out byte fl); r.ReadValueSafe(out sbyte cv);
                if (i < 8) { s.PFlags[i] = fl; s.PCurv[i] = cv; }
            }
            s.B[0] = F(r); s.B[1] = F(r); s.B[2] = F(r);
            for (int k = 3; k <= 10; k++) s.B[k] = H(r);
            r.ReadValueSafe(out s.BOwner); r.ReadValueSafe(out s.BLast); r.ReadValueSafe(out s.BFireBy); r.ReadValueSafe(out s.BHook);
            r.ReadValueSafe(out byte nh);
            for (int i = 0; i < nh; i++)
            {
                var h = new Snap.HookS();
                r.ReadValueSafe(out h.Id); r.ReadValueSafe(out h.Owner); r.ReadValueSafe(out h.Victim);
                h.X = F(r); h.Y = F(r); h.Dx = H(r); h.Dy = H(r); h.Dist = H(r); r.ReadValueSafe(out h.Flags);
                s.Hooks.Add(h);
            }
            r.ReadValueSafe(out byte nw);
            for (int i = 0; i < nw; i++)
            {
                var x = new Snap.WallS();
                r.ReadValueSafe(out x.Id); r.ReadValueSafe(out x.Owner);
                x.X = F(r); x.Y = F(r); x.Tx = H(r); x.Ty = H(r); x.Len = H(r); x.MaxLen = H(r); x.Life = H(r); x.MaxLife = H(r);
                r.ReadValueSafe(out x.Pushed);
                s.Walls.Add(x);
            }
            r.ReadValueSafe(out byte ne);
            for (int i = 0; i < ne; i++)
            {
                var e = new SimEvent();
                r.ReadValueSafe(out byte kind); e.X = F(r); e.Y = F(r); e.Amount = H(r);
                r.ReadValueSafe(out byte cnt); r.ReadValueSafe(out byte col); r.ReadValueSafe(out byte team); r.ReadValueSafe(out sbyte pl);
                e.Kind = (FxKind)kind; e.Count = cnt; e.Color = (FxColor)col; e.Team = (Team)team;
                s.Events.Add(new Snap.EvS { E = e, Player = pl });
            }
            return s;
        }

        // ---------- друг: применить к своей копии матча (между снимками a и b, доля t) ----------
        static readonly Dictionary<ushort, Hook> hookObj = new Dictionary<ushort, Hook>();
        static readonly Dictionary<ushort, IceWall> wallObj = new Dictionary<ushort, IceWall>();
        static readonly Hook dummyHook = new Hook();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { hookObj.Clear(); wallObj.Clear(); nextId = 1; }

        static SimPlayer Pl(Match m, sbyte i) => i >= 0 && i < m.Players.Count ? m.Players[i] : null;

        public static void Apply(Match m, Snap a, Snap b, float t)
        {
            if (a == null) a = b;
            if (a.N != b.N) a = b;
            m.Clock = Mathf.Lerp(a.Clock, b.Clock, t); m.Freeze = b.Freeze;
            m.ScoreHome = b.ScoreH; m.ScoreAway = b.ScoreA; m.Phase = (MatchPhase)b.Phase;
            m.BannerTeam = (Team)b.BannerTeam;
            m.Banner = !b.Banner ? "" : m.Local != null && m.BannerTeam == m.Local.Team ? "ГОЛ!" : "ПРОПУСТИЛ"; // надпись — со стороны этого игрока
            m.Golden = b.Golden; m.Intro = b.Intro;
            if (b.HasStats)
                for (int i = 0; i < b.N && i < m.Players.Count; i++)
                { var p = m.Players[i]; p.Goals = b.Stats[i * 4]; p.Assists = b.Stats[i * 4 + 1]; p.Saves = b.Stats[i * 4 + 2]; p.Steals = b.Stats[i * 4 + 3]; }
            for (int i = 0; i < b.N && i < m.Players.Count; i++) ApplyPlayer(m.Players[i], a, b, i, t);
            ApplyRest(m, a, b, t);
        }

        /// <summary>Состояние одного героя между снимками a и b (t = 1 — ровно как в b).</summary>
        public static void ApplyPlayer(SimPlayer p, Snap a, Snap b, int i, float t)
        {
            {
                int o = i * Snap.PF; var A = a.P; var B = b.P;
                bool jump = Mathf.Abs(A[o] - B[o]) + Mathf.Abs(A[o + 1] - B[o + 1]) > 200; // телепорт, начало розыгрыша — без плавного перехода
                float tt = jump ? 1 : t;
                p.X = Mathf.Lerp(A[o], B[o], tt); p.Y = Mathf.Lerp(A[o + 1], B[o + 1], tt);
                p.Vx = Mathf.Lerp(A[o + 2], B[o + 2], tt); p.Vy = Mathf.Lerp(A[o + 3], B[o + 3], tt);
                var f = Vector2.Lerp(new Vector2(A[o + 4], A[o + 5]), new Vector2(B[o + 4], B[o + 5]), tt);
                if (f.sqrMagnitude > 1e-4f) { f.Normalize(); p.Fx = f.x; p.Fy = f.y; }
                p.DashT = B[o + 6]; p.DashCd = B[o + 7]; p.Stun = B[o + 8]; p.KickCd = B[o + 9]; p.Flash = B[o + 10]; p.GrabCd = B[o + 11];
                p.Charge = B[o + 12]; p.LobCharge = B[o + 13]; p.CurveCharge = B[o + 14];
                p.HookCd = B[o + 15]; p.FireCd = B[o + 16]; p.FireT = B[o + 17]; p.TpCd = B[o + 18]; p.TpBackT = B[o + 19];
                p.TpFx = B[o + 20]; p.TpFy = B[o + 21]; p.CurveCd = B[o + 22]; p.InvisT = B[o + 23]; p.InvisCd = B[o + 24]; p.WallCd = B[o + 25]; p.CallT = B[o + 26]; p.Stamina = B[o + 27]; p.PushT = B[o + 28];
                p.Charging = (b.PFlags[i] & 1) != 0; p.Lobbing = (b.PFlags[i] & 2) != 0; p.Curving = b.PCurv[i];
            }
        }

        static void ApplyRest(Match m, Snap a, Snap b, float t)
        {
            var ball = m.Ball;
            bool bj = a.BOwner != b.BOwner || Mathf.Abs(a.B[0] - b.B[0]) + Mathf.Abs(a.B[1] - b.B[1]) > 300;
            float bt = bj ? 1 : t;
            ball.X = Mathf.Lerp(a.B[0], b.B[0], bt); ball.Y = Mathf.Lerp(a.B[1], b.B[1], bt); ball.Z = Mathf.Lerp(a.B[2], b.B[2], bt);
            ball.Vx = b.B[3]; ball.Vy = b.B[4]; ball.Vz = b.B[5]; ball.Lock = b.B[6]; ball.Spin = b.B[7]; ball.Fire = b.B[8]; ball.FireGrace = b.B[9]; ball.GlueAng = b.B[10];
            ball.Owner = Pl(m, b.BOwner); ball.Last = Pl(m, b.BLast); ball.FireBy = Pl(m, b.BFireBy); ball.Hook = b.BHook ? dummyHook : null;

            m.Hooks.Clear();
            foreach (var h in b.Hooks)
            {
                if (!hookObj.TryGetValue(h.Id, out var o)) hookObj[h.Id] = o = new Hook();
                float x = h.X, y = h.Y;
                foreach (var ha in a.Hooks) if (ha.Id == h.Id) { x = Mathf.Lerp(ha.X, h.X, t); y = Mathf.Lerp(ha.Y, h.Y, t); break; }
                o.Owner = Pl(m, h.Owner); o.Victim = Pl(m, h.Victim); o.X = x; o.Y = y; o.Dx = h.Dx; o.Dy = h.Dy; o.Dist = h.Dist;
                o.Back = (h.Flags & 1) != 0; o.HasBall = (h.Flags & 2) != 0; o.Done = (h.Flags & 4) != 0;
                m.Hooks.Add(o);
            }
            m.Walls.Clear();
            foreach (var x in b.Walls)
            {
                if (!wallObj.TryGetValue(x.Id, out var o)) wallObj[x.Id] = o = new IceWall();
                o.Owner = Pl(m, x.Owner); o.X = x.X; o.Y = x.Y; o.Tx = x.Tx; o.Ty = x.Ty; o.Len = x.Len; o.MaxLen = x.MaxLen; o.Life = x.Life; o.MaxLife = x.MaxLife; o.BallPushed = x.Pushed;
                m.Walls.Add(o);
            }
            if (hookObj.Count > 64) hookObj.Clear();
            if (wallObj.Count > 64) wallObj.Clear();
        }

        public static SimEvent EventFor(Match m, Snap.EvS e) { var x = e.E; x.Player = Pl(m, e.Player); return x; }
    }
}
