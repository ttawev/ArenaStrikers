// Звук матча. Как анимации (HeroView), звуки включаются по изменениям состояния героев и мяча —
// поэтому работают и в повторе гола, и у друга в онлайне (у него есть только снимки состояния).
// Трибуны и музыка берутся из файлов Resources/Audio/<имя> (crowd_loop, crowd_cheer, crowd_ooh, music_menu, music_match);
// пока файла нет — временный синтезированный звук (трибуны) или тишина (музыка).
using System.Collections.Generic;
using Arena.Sim;
using UnityEngine;
using static Arena.Sim.Field;

namespace Arena.Game
{
    public enum UiSound { Move, Confirm, Back }

    public class MatchAudio
    {
        public float Master = 1, Sfx = 1, Crowd = 0.8f, Music = 0.5f;

        static MatchAudio inst;
        /// <summary>Весь звук игры вкл/выкл (главное меню), запоминается.</summary>
        public static bool SoundOn
        {
            get => PlayerPrefs.GetInt("arena.sound", 1) == 1;
            set { PlayerPrefs.SetInt("arena.sound", value ? 1 : 0); PlayerPrefs.Save(); AudioListener.volume = value ? 1 : 0; }
        }
        /// <summary>Трибуны (гул, рёв, «у-у-у») вкл/выкл (главное меню), запоминается.</summary>
        public static bool CrowdOn
        {
            get => PlayerPrefs.GetInt("arena.crowd", 1) == 1;
            set { PlayerPrefs.SetInt("arena.crowd", value ? 1 : 0); PlayerPrefs.Save(); }
        }
        /// <summary>Музыка вкл/выкл (кнопка в главном меню), запоминается между запусками.</summary>
        public static bool MusicOn
        {
            get => PlayerPrefs.GetInt("arena.music", 1) == 1;
            set { PlayerPrefs.SetInt("arena.music", value ? 1 : 0); PlayerPrefs.Save(); }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { inst = null; }
        /// <summary>Звук меню (вызывает MenuUI).</summary>
        public static void Ui(UiSound s) { if (inst != null) inst.Play(s == UiSound.Move ? inst.uiMove : s == UiSound.Confirm ? inst.uiOk : inst.uiBack, 0.8f); }

        readonly AudioSource[] pool = new AudioSource[20]; int next;
        readonly AudioSource crowdSrc, musicSrc;
        readonly AudioClip receive, kickSoft, kickHard, lob, spin, fireIgnite, fireBoom, hookThrow, hookClank, tp, tpBack, vanish, reveal,
            ice, iceShatter, dash, hit, post, board, bounce, net, horn, whistleShort, whistleEnd, uiMove, uiOk, uiBack, denied,
            crowdLoop, cheer, ooh, musicMenu, musicMatch;

        public MatchAudio(GameObject host)
        {
            inst = this;
            for (int i = 0; i < pool.Length; i++) { pool[i] = host.AddComponent<AudioSource>(); pool[i].playOnAwake = false; pool[i].spatialBlend = 0; }
            crowdSrc = host.AddComponent<AudioSource>(); crowdSrc.loop = true; crowdSrc.playOnAwake = false;
            musicSrc = host.AddComponent<AudioSource>(); musicSrc.loop = true; musicSrc.playOnAwake = false;
            receive = SoundFx.Receive(); kickSoft = SoundFx.KickSoft(); kickHard = SoundFx.KickHard(); lob = SoundFx.Lob(); spin = SoundFx.Spin();
            fireIgnite = SoundFx.FireIgnite(); fireBoom = SoundFx.FireBoom(); hookThrow = SoundFx.HookThrow(); hookClank = SoundFx.HookClank();
            tp = SoundFx.Teleport(false); tpBack = SoundFx.Teleport(true); vanish = SoundFx.Vanish(); reveal = SoundFx.Reveal();
            ice = SoundFx.IceCrack(); iceShatter = SoundFx.IceShatter(); dash = SoundFx.Dash(); hit = SoundFx.BodyHit();
            post = SoundFx.Post(); board = SoundFx.Board(); bounce = SoundFx.Bounce(); net = SoundFx.NetSwish(); horn = SoundFx.Horn();
            whistleShort = SoundFx.Whistle(1, false); whistleEnd = SoundFx.Whistle(3, true);
            uiMove = SoundFx.UiMove(); uiOk = SoundFx.UiConfirm(); uiBack = SoundFx.UiBack(); denied = SoundFx.Denied();
            crowdLoop = Resources.Load<AudioClip>("Audio/crowd_loop") ?? SoundFx.CrowdLoop();
            cheer = Resources.Load<AudioClip>("Audio/crowd_cheer") ?? SoundFx.CrowdCheer();
            ooh = Resources.Load<AudioClip>("Audio/crowd_ooh") ?? SoundFx.CrowdOoh();
            musicMenu = Resources.Load<AudioClip>("Audio/music_menu");
            musicMatch = Resources.Load<AudioClip>("Audio/music_match");
            crowdSrc.clip = crowdLoop; crowdSrc.volume = 0; crowdSrc.Play();
            AudioListener.volume = SoundOn ? 1 : 0;
        }

        // ---------- проигрывание ----------
        Camera cam;
        public void Play(AudioClip c, float vol, float pitch = 1, float pan = 0)
        {
            if (c == null || vol <= 0.001f) return;
            var s = pool[next++ % pool.Length];
            s.pitch = pitch; s.panStereo = pan; s.volume = Mathf.Clamp01(vol * Sfx * Master);
            s.clip = c; s.Play();
        }
        /// <summary>Звук в точке поля: панорама по положению на экране, за кадром — тише.</summary>
        void At(AudioClip c, float x, float y, float vol, float pitchVar = 0.06f, float pitch = 1)
        {
            float pan = 0;
            if (cam != null)
            {
                var vp = cam.WorldToViewportPoint(W.Pos(x, y));
                pan = Mathf.Clamp((vp.x - 0.5f) * 1.1f, -0.75f, 0.75f);
                if (vp.z < 0 || vp.x < -0.1f || vp.x > 1.1f || vp.y < -0.1f || vp.y > 1.1f) vol *= 0.6f;
            }
            Play(c, vol, pitch * (1 + (Random.value * 2 - 1) * pitchVar), pan);
        }

        // ---------- события симуляции ----------
        float oohCd;
        public void OnEvent(Match m, SimEvent e, bool live, bool remote)
        {
            switch (e.Kind)
            {
                case FxKind.Receive: At(receive, e.X, e.Y, e.Player == m.Local ? 0.8f : 0.45f, 0.08f); break;
                case FxKind.Denied: if ((live || remote) && e.Player == m.Local) Play(denied, 0.6f); break;
                case FxKind.Goal:
                    At(net, e.X, e.Y, 0.9f, 0.03f);
                    Play(horn, live || remote ? 0.55f : 0.35f);
                    if (CrowdOn) Play(cheer, (live || remote ? 1f : 0.6f) * Crowd / Mathf.Max(0.01f, Sfx), 1, 0); // трибуны — своей громкостью
                    excite = 1;
                    break;
            }
        }

        // ---------- каждый кадр: следим за героями и мячом ----------
        class Prev { public float KickCd, Charge, Lob, Curve, FireT, HookCd, WallCd, TpCd, TpBackT, InvisT, Stun, DashCd, X, Y; }
        readonly List<Prev> prev = new List<Prev>();
        Match lastMatch; bool lastReplay, wasInMatch; MatchPhase lastPhase; bool lastFrozen = true;
        float pbVx, pbVy, pbVz, pbFire, pbSpin; bool pbHook;
        float excite, crowdVol; AudioClip wantMusic;

        /// <summary>inMatch — идёт матч (не меню); replay — повтор гола.</summary>
        public void Sync(Match m, Camera camera, float dt, bool inMatch, bool replay)
        {
            cam = camera;
            var b = m.Ball;
            bool reset = m != lastMatch || replay != lastReplay || prev.Count != m.Players.Count;
            if (reset)
            {
                prev.Clear(); foreach (var p in m.Players) prev.Add(Snap(new Prev(), p));
                lastMatch = m; lastReplay = replay; pbVx = b.Vx; pbVy = b.Vy; pbVz = b.Vz; pbFire = b.Fire; pbSpin = b.Spin; pbHook = b.Hook != null;
                lastPhase = m.Phase; lastFrozen = m.Freeze > 0;
            }
            // свисток: начало матча, разводка после гола, конец матча
            if (inMatch && !wasInMatch && !replay) Play(whistleShort, 0.55f);
            wasInMatch = inMatch;
            bool frozen = m.Freeze > 0;
            if (inMatch && !replay && lastFrozen && !frozen && m.Phase == MatchPhase.Playing && m.Mode != GameMode.Training) Play(whistleShort, 0.5f);
            lastFrozen = frozen;
            if (inMatch && lastPhase == MatchPhase.Playing && m.Phase == MatchPhase.Ended) { Play(whistleEnd, 0.6f); if (CrowdOn) Play(cheer, 0.8f * Crowd / Mathf.Max(0.01f, Sfx)); }
            lastPhase = m.Phase;

            if (!reset)
            {
                for (int i = 0; i < m.Players.Count; i++) Player(m, m.Players[i], prev[i]);
                BallSounds(m, b);
            }
            for (int i = 0; i < m.Players.Count; i++) Snap(prev[i], m.Players[i]);
            pbVx = b.Vx; pbVy = b.Vy; pbVz = b.Vz; pbFire = b.Fire; pbSpin = b.Spin; pbHook = b.Hook != null;

            Ambience(m, dt, inMatch);
        }

        static Prev Snap(Prev q, SimPlayer p)
        {
            q.KickCd = p.KickCd; q.Charge = p.Charging ? p.Charge : 0; q.Lob = p.Lobbing ? p.LobCharge : 0; q.Curve = p.Curving != 0 ? p.CurveCharge : 0;
            q.FireT = p.FireT; q.HookCd = p.HookCd; q.WallCd = p.WallCd; q.TpCd = p.TpCd; q.TpBackT = p.TpBackT; q.InvisT = p.InvisT;
            q.Stun = p.Stun; q.DashCd = p.DashCd; q.X = p.X; q.Y = p.Y;
            return q;
        }

        void Player(Match m, SimPlayer p, Prev q)
        {
            float me = p == m.Local ? 1f : 0.7f;
            // удар / навес / кручёный
            if (p.KickCd > q.KickCd + 0.05f && p.FireT <= 0 && q.FireT <= 0)
            {
                float c = Mathf.Max(q.Charge, q.Curve);
                if (q.Lob > 0) At(lob, p.X, p.Y, (0.55f + 0.35f * q.Lob) * me);
                else if (c > 0.5f) At(kickHard, p.X, p.Y, (0.7f + 0.3f * c) * me, 0.05f, 1.05f - 0.1f * c);
                else At(kickSoft, p.X, p.Y, (0.5f + 0.4f * c) * me, 0.08f);
            }
            if (p.FireT > 0 && q.FireT <= 0) At(fireIgnite, p.X, p.Y, 0.7f * me, 0.04f);     // Жар: замах
            if (p.HookCd > q.HookCd + 0.5f) At(hookThrow, p.X, p.Y, 0.7f * me);               // Багор: бросок
            if (p.WallCd > q.WallCd + 0.5f) At(ice, p.X, p.Y, 0.85f * me, 0.04f);             // Стена: разлом
            if (p.TpCd > q.TpCd + 0.5f) At(tp, p.X, p.Y, 0.65f * me, 0.03f);                  // Миг: прыжок
            else if (q.TpBackT > 0 && p.TpBackT <= 0 && Mathf.Abs(p.X - q.X) + Mathf.Abs(p.Y - q.Y) > 80) At(tpBack, p.X, p.Y, 0.6f * me, 0.03f); // и возврат
            if (p.InvisT > 0 && q.InvisT <= 0) At(vanish, p.X, p.Y, 0.65f * me);              // Тень: исчез
            else if (q.InvisT > 0 && p.InvisT <= 0) At(reveal, p.X, p.Y, 0.5f * me);          // проявился
            if (p.DashCd > q.DashCd + 0.5f) At(dash, p.X, p.Y, 0.55f * me, 0.1f);             // рывок
            if (p.Stun > 0 && q.Stun <= 0 && p.Stun > 0.3f)                                   // сбили / попали огнём / притянули крюком
            {
                At(hit, p.X, p.Y, 0.85f, 0.08f);
                if (p.Stun > 0.9f) At(fireBoom, p.X, p.Y, 0.45f, 0.05f, 1.2f);
            }
        }

        void BallSounds(Match m, SimBall b)
        {
            if (b.Fire > 0 && pbFire <= 0) At(fireBoom, b.X, b.Y, 1f, 0.03f);               // огненный суперудар полетел
            if (b.Spin != 0 && pbSpin == 0) At(spin, b.X, b.Y, 0.5f, 0.05f);                 // кручёный
            if (b.Hook != null && !pbHook) At(hookClank, b.X, b.Y, 0.75f, 0.05f);            // крюк поймал мяч
            if (b.Owner != null || b.Hook != null) return;
            // отскоки: от штанги — звон, от борта — глухой удар, от земли — мягкий
            bool flipX = pbVx * b.Vx < 0 && Mathf.Abs(pbVx) > 220, flipY = pbVy * b.Vy < 0 && Mathf.Abs(pbVy) > 220;
            if (flipX || flipY)
            {
                float sp = Mathf.Max(Mathf.Abs(pbVx), Mathf.Abs(pbVy));
                bool nearPost = false;
                foreach (float gy in new[] { 0f, FH })
                    foreach (float gx in new[] { GX1, GX2 })
                        if (Mathf.Abs(b.X - gx) + Mathf.Abs(b.Y - gy) < 45) nearPost = true;
                bool atEnd = b.Y < 30 || b.Y > FH - 30;
                if (nearPost) { At(post, b.X, b.Y, Mathf.Clamp01(sp / 1400) * 0.9f + 0.2f, 0.04f); Ooh(); }
                else
                {
                    At(board, b.X, b.Y, Mathf.Clamp01(sp / 1600) * 0.7f + 0.15f, 0.1f);
                    if (atEnd && sp > 800 && Mathf.Abs(b.X - FW / 2) < GOAL) Ooh(); // чуть мимо ворот
                }
            }
            else if (pbVz < -170 && b.Vz > 0 && b.Z < 5) At(bounce, b.X, b.Y, Mathf.Clamp01(-pbVz / 900) * 0.5f, 0.1f);
        }

        void Ooh()
        {
            if (oohCd > 0 || !CrowdOn) return;
            oohCd = 3; excite = Mathf.Max(excite, 0.8f);
            Play(ooh, 0.8f * Crowd / Mathf.Max(0.01f, Sfx));
        }

        // ---------- трибуны и музыка ----------
        void Ambience(Match m, float dt, bool inMatch)
        {
            oohCd -= dt;
            // азарт трибун: мяч летит к воротам, атака в последней трети
            var b = m.Ball; float hot = 0;
            float toTop = b.Vy < 0 ? b.Y : 9999, toBot = b.Vy > 0 ? FH - b.Y : 9999;
            float dGoal = Mathf.Min(b.Y, FH - b.Y);
            if (b.Owner == null && Mathf.Min(toTop, toBot) < 700 && Mathf.Abs(b.Vy) > 500) hot = 0.9f;
            else if (dGoal < 600) hot = 0.6f;
            excite = Mathf.MoveTowards(excite, hot, dt * (hot > excite ? 2.5f : 0.35f));
            float target = (inMatch ? 0.35f + 0.5f * excite : 0.18f) * (m.Mode == GameMode.Training ? 0.5f : 1);
            crowdVol = Mathf.MoveTowards(crowdVol, target, dt * 0.8f);
            crowdSrc.volume = CrowdOn ? crowdVol * Crowd * Master : 0;
            crowdSrc.pitch = 1 + 0.05f * excite;

            // музыка: в меню — своя, в матче — своя (тише, под трибуны); смена — через затухание
            var want = !MusicOn ? null : inMatch ? musicMatch : musicMenu;
            float mv = (inMatch ? 0.45f : 0.8f) * Music * Master;
            if (musicSrc.clip != want)
            {
                musicSrc.volume = Mathf.MoveTowards(musicSrc.volume, 0, dt * 1.5f);
                if (musicSrc.volume <= 0.001f || musicSrc.clip == null) { musicSrc.clip = want; if (want != null) musicSrc.Play(); }
            }
            else if (want != null) musicSrc.volume = Mathf.MoveTowards(musicSrc.volume, mv, dt * 0.8f);
        }
    }
}
