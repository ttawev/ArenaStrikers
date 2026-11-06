// A hero built from primitives (grey-box), with the headgear that identifies him from above,
// and the per-hero markers: team ring, charge arc, fire wind-up ring, stun stars, hook rope, teleport mark.
using Arena.Sim;
using UnityEngine;

namespace Arena.Game
{
    public class HeroView
    {
        const float S = W.S;
        public readonly GameObject Root;
        readonly Transform body, legL, legR, armL, armR, stars, propeller, gem;
        readonly GameObject[] hats = new GameObject[6];
        Renderer[] rends; Material[] origMats; Material ghostMat; bool ghosted; float ghostAlpha = -1;
        readonly GameObject arcObj, fireRing, rope, hookHead, tpMark, youMark;
        readonly GameObject ownRing, ownWave; readonly Material ownRingMat, ownWaveMat; float ownT; // подсветка владельца мяча
        readonly GameObject callMark, callWave; readonly Material callWaveMat; float callPop, mvpTurn; // «дай пас» и поворот MVP к камере
        readonly Mesh arcMesh;
        float arcShown = -1;
        readonly Team team; HeroModel model; int modelHero = -1;
        float prevKickCd, prevCharge, prevLob, prevCurve, prevFireT, animLock;
        float prevStun, prevHookCd, prevWallCd, prevTpCd, prevInvisT, recoverT;
        int stunKind;            // 0 — шатается, 1 — упал от рывка, 2 — упал навзничь от огня
        HeroAnim? prevReact;     // празднует / расстроен / конец матча

        static Vector3 V(float x, float y, float z) => new Vector3(x * S, y * S, z * S);

        public HeroView(Transform parent, Team team, string name)
        {
            Root = new GameObject(name);
            Root.transform.SetParent(parent, false);
            this.team = team;
            Color teamCol = team == Team.Home ? Mat.Home : Mat.Away;
            var shirt = Mat.Solid(teamCol);
            var pants = Mat.Solid(team == Team.Home ? Mat.Hex("#3a2a10") : Mat.Hex("#3a1420"));
            var skin = Mat.Solid(Mat.Hex("#f0c49c"));
            var black = Mat.Solid(Mat.Night);

            legL = Shapes.Prim(PrimitiveType.Cylinder, Root.transform, V(-8, 7, 0), V(12, 7, 12), pants, "legL").transform;
            legR = Shapes.Prim(PrimitiveType.Cylinder, Root.transform, V(8, 7, 0), V(12, 7, 12), pants, "legR").transform;
            body = new GameObject("body").transform; body.SetParent(Root.transform, false);
            Shapes.Prim(PrimitiveType.Capsule, body, V(0, 37, 0), V(34, 23, 34), shirt, "torso");
            armL = Shapes.Prim(PrimitiveType.Sphere, body, V(-21, 40, 2), V(16, 16, 16), shirt, "armL").transform;
            armR = Shapes.Prim(PrimitiveType.Sphere, body, V(21, 40, 2), V(16, 16, 16), shirt, "armR").transform;
            Shapes.Prim(PrimitiveType.Sphere, body, V(-24, 30, 6), V(12, 12, 12), skin, "fistL");
            Shapes.Prim(PrimitiveType.Sphere, body, V(24, 30, 6), V(12, 12, 12), skin, "fistR");
            Shapes.Prim(PrimitiveType.Sphere, body, V(0, 70, 0), V(28, 28, 28), skin, "head");
            Shapes.Prim(PrimitiveType.Cylinder, body, V(0, 76, 0), V(29, 2.6f, 29), shirt, "band");
            Shapes.Prim(PrimitiveType.Sphere, body, V(-5, 71, 12.5f), V(5, 5, 5), black, "eyeL");
            Shapes.Prim(PrimitiveType.Sphere, body, V(5, 71, 12.5f), V(5, 5, 5), black, "eyeR");

            // ---- headgear ----
            // Жар: flames for hair
            hats[(int)Hero.Fire] = new GameObject("hat_fire"); hats[0].transform.SetParent(body, false);
            float[,] flames = { { 0, -2, 22, 0 }, { -8, 2, 17, 1 }, { 8, 2, 17, 1 }, { -4, -9, 15, 0 }, { 4, -9, 15, 0 } };
            for (int i = 0; i < 5; i++)
            {
                var f = Shapes.MeshObj(Shapes.Cone(5.5f * S, flames[i, 2] * S, 10), hats[0].transform, Mat.Solid(flames[i, 3] > 0 ? Mat.Hex("#ffc93e") : Mat.Hex("#ff6a1a")), "flame");
                f.transform.localPosition = V(flames[i, 0], 78, flames[i, 1]); f.transform.localRotation = Quaternion.Euler(-20, 0, 0);
            }
            // Багор: wide hat and a hook in the right hand
            hats[(int)Hero.Hook] = new GameObject("hat_hook"); hats[1].transform.SetParent(body, false);
            Shapes.Prim(PrimitiveType.Cylinder, hats[1].transform, V(0, 82, 0), V(46, 1.25f, 46), Mat.Solid(Mat.Hex("#6b4a2b")), "brim");
            Shapes.Prim(PrimitiveType.Cylinder, hats[1].transform, V(0, 89, 0), V(24, 6.5f, 24), Mat.Solid(Mat.Hex("#7d5733")), "crown");
            Shapes.Prim(PrimitiveType.Cylinder, hats[1].transform, V(26, 34, 9), V(3.5f, 10, 3.5f), Mat.Solid(Mat.Hex("#b8c4c8"), 0.6f), "hookShaft");
            Shapes.Prim(PrimitiveType.Sphere, hats[1].transform, V(26, 22, 12), V(8, 6, 10), Mat.Solid(Mat.Hex("#b8c4c8"), 0.6f), "hookTip");
            // Миг: tall hood and a floating crystal
            hats[(int)Hero.Teleport] = new GameObject("hat_tp"); hats[2].transform.SetParent(body, false);
            Shapes.MeshObj(Shapes.Cone(15.5f * S, 34 * S, 16), hats[2].transform, Mat.Solid(Mat.Hex("#5b4fd6")), "hood").transform.localPosition = V(0, 74, 0);
            gem = Shapes.Prim(PrimitiveType.Cube, hats[2].transform, V(0, 124, 0), V(7, 7, 7), Mat.Glow(Mat.Hex("#9ff3ff")), "gem").transform;
            // Вихрь: beanie with a propeller
            hats[(int)Hero.Curve] = new GameObject("hat_curve"); hats[3].transform.SetParent(body, false);
            Shapes.Prim(PrimitiveType.Sphere, hats[3].transform, V(0, 74, 0), V(30, 22, 30), Mat.Solid(Mat.Hex("#2fbf9f")), "cap");
            Shapes.Prim(PrimitiveType.Cylinder, hats[3].transform, V(0, 89, 0), V(3, 4, 3), Mat.Solid(Mat.Hex("#203a38")), "post");
            propeller = new GameObject("propeller").transform; propeller.SetParent(hats[3].transform, false); propeller.localPosition = V(0, 93, 0);
            Shapes.Prim(PrimitiveType.Cube, propeller, Vector3.zero, V(32, 1.6f, 5.5f), Mat.Solid(Mat.Hex("#ff4f9a")), "bladeA");
            Shapes.Prim(PrimitiveType.Cube, propeller, Vector3.zero, V(5.5f, 1.6f, 32), Mat.Solid(Mat.Hex("#ffd23e")), "bladeB");

            // Тень: low dark hood, a long scarf and two glowing eyes
            hats[(int)Hero.Shade] = new GameObject("hat_shade"); hats[4].transform.SetParent(body, false);
            var cloth = Mat.Solid(Mat.Hex("#2a2f3a"));
            Shapes.Prim(PrimitiveType.Sphere, hats[4].transform, V(0, 74, -2), V(33, 30, 33), cloth, "hood");
            Shapes.Prim(PrimitiveType.Cylinder, hats[4].transform, V(0, 62, 0), V(31, 4, 31), Mat.Solid(Mat.Hex("#3d4454")), "mask");
            var scarf = Shapes.Prim(PrimitiveType.Cube, hats[4].transform, V(-6, 56, -26), V(7, 2, 40), Mat.Solid(Mat.Hex("#7b2fbf")), "scarf");
            scarf.transform.localRotation = Quaternion.Euler(-25, 10, 0);
            Shapes.Prim(PrimitiveType.Sphere, hats[4].transform, V(-5, 72, 14), V(5, 3, 3), Mat.Glow(Mat.Hex("#c9f7ff")), "eyeGlowL");
            Shapes.Prim(PrimitiveType.Sphere, hats[4].transform, V(5, 72, 14), V(5, 3, 3), Mat.Glow(Mat.Hex("#c9f7ff")), "eyeGlowR");

            // Стена: ice helmet with crystal spikes
            hats[(int)Hero.Wall] = new GameObject("hat_wall"); hats[5].transform.SetParent(body, false);
            Shapes.Prim(PrimitiveType.Sphere, hats[5].transform, V(0, 75, 0), V(31, 22, 31), Mat.Solid(Mat.Hex("#9fe8ff"), 0.8f), "helmet");
            float[,] spikes = { { 0, 0, 26, 0 }, { -10, 3, 18, -18 }, { 10, 3, 18, 18 }, { 0, -10, 16, 0 } };
            for (int i = 0; i < 4; i++)
            {
                var sp = Shapes.MeshObj(Shapes.Cone(5 * S, spikes[i, 2] * S, 6), hats[5].transform, Mat.Solid(Mat.Hex("#e6fbff"), 0.9f), "spike");
                sp.transform.localPosition = V(spikes[i, 0], 82, spikes[i, 1]); sp.transform.localRotation = Quaternion.Euler(spikes[i, 1] < 0 ? -25 : 0, 0, -spikes[i, 3]);
            }

            // ---- markers ----
            Shapes.MeshObj(Shapes.Ring((Field.R + 1) * S, (Field.R + 5) * S), Root.transform, Mat.Glow(teamCol), "teamRing").transform.localPosition = V(0, 0.6f, 0);
            arcMesh = new Mesh { name = "arc" };
            arcObj = Shapes.MeshObj(arcMesh, Root.transform, Mat.Glow(teamCol), "chargeArc"); arcObj.transform.localPosition = V(0, 1, 0);
            fireRing = Shapes.MeshObj(Shapes.Ring((Field.R + 6) * S, (Field.R + 18) * S), Root.transform, Mat.Clear(new Color(1, 0.48f, 0.1f, 0.8f), true), "fireRing");
            fireRing.transform.localPosition = V(0, 1.4f, 0);
            stars = new GameObject("stars").transform; stars.SetParent(Root.transform, false); stars.localPosition = V(0, 100, 0);
            for (int i = 0; i < 3; i++) Shapes.Prim(PrimitiveType.Cube, stars, Vector3.zero, V(6, 6, 6), Mat.Glow(Mat.Hex("#fff3c4")), "star");

            rope = Shapes.Prim(PrimitiveType.Cube, parent, Vector3.zero, Vector3.one, Mat.Solid(Mat.Hex("#c9a36a")), name + "_rope");
            hookHead = Shapes.MeshObj(HeroModel.HookMetalMesh(0), parent, Mat.Solid(Mat.Hex("#d9e2e6"), 0.8f), name + "_hook"); // тот же крюк, что в руке у Багора
            hookHead.transform.localScale = Vector3.one * 1.8f;
            tpMark = Shapes.MeshObj(Shapes.Ring((Field.R - 6) * S, (Field.R + 2) * S), parent, Mat.Clear(new Color(0.56f, 0.85f, 1f, 0.8f), true), name + "_tpMark");
            youMark = Shapes.MeshObj(Shapes.Cone(8 * S, 16 * S, 4), Root.transform, Mat.Glow(Mat.Hex("#fff3c4")), "youMark");
            youMark.transform.localRotation = Quaternion.Euler(180, 0, 0);
            // «дай пас»: восклицательный знак над головой и волна по земле (видят только свои)
            callMark = new GameObject("callMark"); callMark.transform.SetParent(Root.transform, false);
            var callCol = Mat.Glow(Mat.Hex("#ffe14d"));
            Shapes.Prim(PrimitiveType.Capsule, callMark.transform, V(0, 14, 0), V(9, 11, 9), callCol, "bar");
            Shapes.Prim(PrimitiveType.Sphere, callMark.transform, V(0, -6, 0), V(10, 10, 10), callCol, "dot");
            callWaveMat = Mat.Clear(new Color(1f, 0.88f, 0.3f, 0.7f), true);
            callWave = Shapes.MeshObj(Shapes.Ring((Field.R + 2) * S, (Field.R + 9) * S), Root.transform, callWaveMat, "callWave");
            callWave.transform.localPosition = V(0, 1.8f, 0);
            callMark.SetActive(false); callWave.SetActive(false);
            // у кого мяч: яркое кольцо под ногами в цвет команды и расходящаяся волна
            Color oc = Color.Lerp(teamCol, Color.white, 0.35f);
            ownRingMat = Mat.Clear(new Color(oc.r * 2.2f, oc.g * 2.2f, oc.b * 2.2f, 0.95f), true);
            ownRing = Shapes.MeshObj(Shapes.Ring((Field.R + 7) * S, (Field.R + 15) * S), Root.transform, ownRingMat, "ownRing");
            ownRing.transform.localPosition = V(0, 1.2f, 0);
            ownWaveMat = Mat.Clear(new Color(oc.r, oc.g, oc.b, 0.6f), true);
            ownWave = Shapes.MeshObj(Shapes.Ring((Field.R + 13) * S, (Field.R + 17) * S), Root.transform, ownWaveMat, "ownWave");
            ownWave.transform.localPosition = V(0, 1.0f, 0);
            ownRing.SetActive(false); ownWave.SetActive(false);
            rope.SetActive(false); hookHead.SetActive(false); tpMark.SetActive(false); youMark.SetActive(false);
        }

        void SetGhost(float alpha)
        {
            if (rends == null)
            {
                rends = Root.GetComponentsInChildren<Renderer>(true);
                origMats = new Material[rends.Length];
                for (int i = 0; i < rends.Length; i++) origMats[i] = rends[i].sharedMaterial;
                ghostMat = Mat.Clear(new Color(0.75f, 0.9f, 1f, 0.3f));
            }
            if (alpha < 0)
            {
                for (int i = 0; i < rends.Length; i++) if (rends[i]) { rends[i].sharedMaterial = origMats[i]; rends[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; rends[i].enabled = true; }
                ghosted = false; ghostAlpha = -1; return;
            }
            if (!ghosted)
            {
                for (int i = 0; i < rends.Length; i++) if (rends[i]) { rends[i].sharedMaterial = ghostMat; rends[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
                ghosted = true;
            }
            if (Mathf.Abs(alpha - ghostAlpha) > 0.005f)
            {
                for (int i = 0; i < rends.Length; i++) if (rends[i]) rends[i].enabled = alpha > 0;
                ghostMat.SetColor("_BaseColor", new Color(0.75f, 0.9f, 1f, alpha)); ghostAlpha = alpha;
            }
        }

        /// <summary>Uses the rigged model when the hero has art, otherwise the grey-box body.</summary>
        void SwapModel(Hero h)
        {
            if (ghosted) SetGhost(-1);
            rends = null;
            if (model != null) Object.Destroy(model.Go);
            model = HeroModel.Create(h, team, Root.transform);
            modelHero = (int)h;
            bool grey = model == null;
            body.gameObject.SetActive(grey); legL.gameObject.SetActive(grey); legR.gameObject.SetActive(grey);
        }

        void DriveModel(Match m, SimPlayer p)
        {
            var ball = m.Ball;
            var T = HeroModel.Tune; float dt = HeroModel.FrameDt;
            float speed = new Vector2(p.Vx, p.Vy).magnitude;

            // ---- после гола и в конце матча: забившая команда празднует, пропустившая расстраивается ----
            HeroAnim? react = null;
            if (m.Phase == MatchPhase.Ended)
            {
                if (m.ScoreHome != m.ScoreAway && m.Mode != GameMode.Training)
                    react = (p.Team == Team.Home) == (m.ScoreHome > m.ScoreAway) ? HeroAnim.Celebrate : HeroAnim.Defeated;
                if (m.Mode != GameMode.Training && m.Mvp() == p) react = HeroAnim.Celebrate; // лучший игрок матча празднует, даже если проиграл
            }
            else if (m.Intro > 0 && m.IntroStage == 1)
                react = model.Has(HeroAnim.ShowIdle) ? HeroAnim.ShowIdle : HeroAnim.Idle; // выход команд: стоят в строю собранно, без празднований
            else if (m.Freeze > 0 && m.Banner != "" && m.Mode != GameMode.Training && !p.Dummy)
                react = p.Team == m.BannerTeam ? HeroAnim.Celebrate : HeroAnim.Sad;
            if (react != null)
            {
                model.Play(react.Value, T.fade * 2, react != prevReact, react == HeroAnim.Celebrate ? T.celebrateSpeed : 1);
                prevReact = react; animLock = 0; recoverT = 0;
                FinishModel(m, p, ball, dt);
                return;
            }
            prevReact = null;

            // ---- удары ----
            bool kicked = p.KickCd > prevKickCd + 0.05f;
            if (p.FireT > 0 && prevFireT <= 0)
            {   // fire super-kick: the wind-up plays while the sim charges it
                model.Play(HeroAnim.KickStrong, T.fade * 0.5f, true, T.fireSpeed, T.fireStart);
                animLock = p.FireT + T.kickHold;
            }
            else if (kicked && p.FireT <= 0 && prevFireT <= 0)
            {
                float charge = Mathf.Max(prevCharge, prevCurve);
                bool strong = charge > 0.6f || prevLob > 0.6f;
                bool pass = !strong && prevLob <= 0 && charge < T.passMax;
                if (strong) model.Play(HeroAnim.KickStrong, T.fade * 0.5f, true, T.strongSpeed, T.strongStart);
                else if (pass) model.Play(HeroAnim.Pass, T.fade * 0.5f, true, T.passSpeed, T.passStart);
                else model.Play(HeroAnim.Kick, T.fade * 0.5f, true, T.kickSpeed, T.kickStart);
                animLock = T.kickHold;
            }
            // ---- способности ----
            else if (p.Hero == Hero.Hook && p.HookCd > prevHookCd + 0.5f) { model.Play(HeroAnim.Throw, T.fade * 0.5f, true, T.throwSpeed, T.throwStart); animLock = T.abilityHold; }
            else if (p.Hero == Hero.Wall && p.WallCd > prevWallCd + 0.5f) { model.Play(HeroAnim.GroundPunch, T.fade * 0.5f, true, T.punchSpeed, T.punchStart); animLock = T.abilityHold; }
            else if (p.Hero == Hero.Shade && p.InvisT > 0 && prevInvisT <= 0) { model.Play(HeroAnim.Spell, T.fade * 0.5f, true, T.spellSpeed, T.spellStart); animLock = T.abilityHold; }
            else if (p.Hero == Hero.Teleport && p.TpCd > prevTpCd + 0.5f) { model.Play(HeroAnim.Magic, T.fade * 0.3f, true, T.magicSpeed); animLock = T.abilityHold; }
            animLock -= dt;

            // ---- оглушение: падение и подъём ----
            if (p.Stun > 0 && prevStun <= 0)
                stunKind = p.Stun < T.fallStun ? 0 : (m.T.fireStun > 0.8f && p.Stun > m.T.fireStun - 0.05f) ? 2 : 1;
            recoverT -= dt;
            if (p.Stun > 0)
            {
                animLock = 0;
                if (stunKind == 0) model.Play(HeroAnim.Stun, T.fade);
                else if (p.Stun > T.getUpLead)
                {
                    if (stunKind == 2) model.Play(HeroAnim.FallBack, T.fade * 0.5f, false, T.fallBackSpeed, T.fallBackStart);
                    else model.Play(HeroAnim.Fall, T.fade * 0.5f, false, T.fallSpeed, T.fallStart);
                }
                else
                {
                    if (recoverT <= 0) recoverT = model.Length(HeroAnim.GetUp) * Mathf.Max(0, 0.68f - T.getUpStart) / Mathf.Max(0.1f, T.getUpSpeed);
                    model.Play(HeroAnim.GetUp, T.fade, false, T.getUpSpeed, T.getUpStart);
                }
            }
            else if (recoverT > 0 && speed < 60) model.Play(HeroAnim.GetUp, T.fade, false, T.getUpSpeed, T.getUpStart); // встаёт, если не побежал сразу
            else if (p.DashT > 0) model.Play(HeroAnim.Dash, T.fade * 0.5f, false, T.dashSpeed, T.dashStart);
            else if (animLock > 0) { }
            else if (speed > 30)
            {
                // смотрит не туда, куда бежит (вратарь следит за мячом и пятится/смещается вбок)
                float fwd = (p.Vx * p.Fx + p.Vy * p.Fy) / speed;
                Vector3 wf = W.Dir(p.Fx, p.Fy), wv = W.Dir(p.Vx, p.Vy);
                float side = (wv.x * wf.z - wv.z * wf.x) / speed;      // > 0 — движется вправо от взгляда
                float cosA = Mathf.Cos(T.strafeAngle * Mathf.Deg2Rad);
                bool own = ball.Owner == p;
                if (T.strafe && !own && fwd < -cosA && model.Has(HeroAnim.RunBack))
                    model.Play(HeroAnim.RunBack, T.fade, false, Mathf.Clamp(speed / T.runBackRef, 0.6f, 1.8f));
                else if (T.strafe && !own && fwd < cosA && side > 0 && model.Has(HeroAnim.StrafeR))
                    model.Play(HeroAnim.StrafeR, T.fade, false, Mathf.Clamp(speed / T.strafeRef, 0.6f, 1.8f));
                else if (T.strafe && !own && fwd < cosA && side < 0 && model.Has(HeroAnim.StrafeL))
                    model.Play(HeroAnim.StrafeL, T.fade, false, Mathf.Clamp(speed / T.strafeRef, 0.6f, 1.8f));
                else if (T.runBall && own && model.Has(HeroAnim.RunBall))
                    model.Play(HeroAnim.RunBall, T.fade, false, Mathf.Clamp(speed / T.runBallRef, 0.6f, 1.8f));
                else if (T.dribble && ball.Owner == p && model.Has(HeroAnim.Dribble))
                    model.Play(HeroAnim.Dribble, T.fade, false, Mathf.Clamp(speed / T.dribbleRef, 0.6f, 2.2f));
                else model.Play(HeroAnim.Run, T.fade, false, Mathf.Clamp(speed / T.runRef, 0.6f, 1.8f));
            }
            else model.Play(HeroAnim.Idle, T.fade);
            if (p.Stun <= 0 && speed >= 60) recoverT = 0;
            FinishModel(m, p, ball, dt);
        }

        void FinishModel(Match m, SimPlayer p, SimBall ball, float dt)
        {
            bool superKick = p.FireT > 0 || (ball.Fire > 0 && ball.FireBy == p); // замах суперудара и пока его мяч горит
            model.SetHeat(superKick ? 2.6f : 1); // the super-kick flares the fire
            if (p.Hero == Hero.Hook) model.SetHookOut(m.Hooks.Exists(h => h.Owner == p));
            model.Tick(dt);
            prevKickCd = p.KickCd; prevCharge = p.Charging ? p.Charge : 0; prevLob = p.Lobbing ? p.LobCharge : 0; prevCurve = p.Curving != 0 ? p.CurveCharge : 0; prevFireT = p.FireT;
            prevStun = p.Stun; prevHookCd = p.HookCd; prevWallCd = p.WallCd; prevTpCd = p.TpCd; prevInvisT = p.InvisT;
        }

        /// <summary>Убрать героя со сцены вместе с верёвкой, крюком и меткой телепорта (они лежат не внутри героя).</summary>
        public void Destroy()
        {
            Object.Destroy(Root); Object.Destroy(rope); Object.Destroy(hookHead); Object.Destroy(tpMark);
        }

        public void SetVisible(bool on)
        {
            Root.SetActive(on);
            if (!on) { rope.SetActive(false); hookHead.SetActive(false); tpMark.SetActive(false); }
        }

        public void Sync(Match m, SimPlayer p, float t, bool showYouMark)
        {
            var tr = Root.transform;
            tr.position = W.Pos(p.X, p.Y);
            // конец матча: лучший игрок плавно разворачивается к камере
            bool mvp = m.Phase == MatchPhase.Ended && m.Mode != GameMode.Training && m.Mvp() == p;
            mvpTurn = mvp ? Mathf.MoveTowards(mvpTurn, 1, Time.unscaledDeltaTime * 1.5f) : 0;
            tr.rotation = mvpTurn > 0 ? Quaternion.Slerp(W.Yaw(p.Fx, p.Fy), W.Yaw(1, 0), Mathf.SmoothStep(0, 1, mvpTurn)) : W.Yaw(p.Fx, p.Fy);
            if (modelHero != (int)p.Hero) SwapModel(p.Hero);
            for (int i = 0; i < hats.Length; i++) hats[i].SetActive(i == (int)p.Hero);
            // invisible Тень: teammates see a ghost, opponents only a faint shimmer
            if (p.Invisible) SetGhost(p.Team == m.Local.Team ? 0.28f : 0); // allies: a ghost; opponents: nothing at all
            else if (ghosted) SetGhost(-1);
            if (p.Hero == Hero.Curve) propeller.localRotation = Quaternion.Euler(0, t * (500 + new Vector2(p.Vx, p.Vy).magnitude * 2.3f), 0);
            if (p.Hero == Hero.Teleport) { gem.localPosition = V(0, 124 + Mathf.Sin(t * 3) * 3, 0); gem.localRotation = Quaternion.Euler(45, t * 115, 45); }

            // run cycle and poses
            float run = Mathf.Min(1, new Vector2(p.Vx, p.Vy).magnitude / 280) * (p.Stun > 0 ? 0 : 1), ph = t * 15;
            body.localPosition = V(0, Mathf.Abs(Mathf.Sin(ph)) * 4 * run, 0);
            legL.localPosition = V(-8, 7, Mathf.Sin(ph) * 6 * run); legR.localPosition = V(8, 7, -Mathf.Sin(ph) * 6 * run);
            armL.localPosition = V(-21, 40, 2 - Mathf.Sin(ph) * 5 * run); armR.localPosition = V(21, 40, 2 + Mathf.Sin(ph) * 5 * run);
            body.localScale = p.DashT > 0 ? new Vector3(0.92f, 0.86f, 1.2f) : Vector3.one;
            float charge = p.Charging ? p.Charge : p.Curving != 0 ? p.CurveCharge : 0;
            float lean = p.Flash > 0 ? 0.4f : p.FireT > 0 ? -0.35f : p.Lobbing ? -0.12f - 0.25f * p.LobCharge : charge > 0 ? -0.18f * charge : p.DashT > 0 ? 0.3f : 0;
            float wobble = p.Stun > 0 ? Mathf.Sin(t * 11) * 0.28f : 0;
            body.localRotation = Quaternion.Euler(lean * Mathf.Rad2Deg, 0, wobble * Mathf.Rad2Deg);

            if (model != null) DriveModel(m, p);

            stars.gameObject.SetActive(p.Stun > 0);
            if (p.Stun > 0)
                for (int i = 0; i < 3; i++)
                {
                    float a = t * 6 + i * 2.09f;
                    var s = stars.GetChild(i); s.localPosition = V(Mathf.Cos(a) * 18, Mathf.Sin(t * 9 + i) * 2, Mathf.Sin(a) * 18); s.localRotation = Quaternion.Euler(0, t * 230, 45);
                }

            // charge arc on the ground (kick or curve)
            if (charge > 0.01f)
            {
                if (Mathf.Abs(charge - arcShown) > 0.015f) { Shapes.Ring((Field.R + 9) * S, (Field.R + 14) * S, 48, 0, Mathf.PI * 2 * charge, arcMesh); arcShown = charge; }
                arcObj.SetActive(true);
            }
            else { arcObj.SetActive(false); arcShown = -1; }

            // fire wind-up ring
            fireRing.SetActive(p.FireT > 0);
            if (p.FireT > 0) { float k = 1 - p.FireT / m.T.fireDelay; fireRing.transform.localScale = Vector3.one * (1.6f - k * 0.6f); }

            // hook rope and head
            Hook hook = m.Hooks.Find(h => h.Owner == p);
            if (hook != null)
            {
                Vector3 a = W.Pos(p.X + p.Fx * Field.R * 0.8f, p.Y + p.Fy * Field.R * 0.8f, 30), b = W.Pos(hook.X, hook.Y, 30);
                if (model != null && model.RopeStart != null) a = model.RopeStart.position; // верёвка идёт от древка в руке
                Vector3 d = b - a; float L = Mathf.Max(0.01f, d.magnitude);
                rope.SetActive(true); rope.transform.position = (a + b) / 2; rope.transform.localScale = new Vector3(3 * S, 3 * S, L);
                rope.transform.rotation = Quaternion.LookRotation(d.sqrMagnitude > 0 ? d : Vector3.forward);
                hookHead.SetActive(true); hookHead.transform.position = b;
                Vector3 hd = d; hd.y = 0; if (hd.sqrMagnitude < 1e-4f) hd = W.Dir(hook.Dx, hook.Dy); // острие всегда от хозяина
                hookHead.transform.rotation = Quaternion.LookRotation(hd.sqrMagnitude > 0 ? hd : Vector3.forward, Vector3.up);
            }
            else { rope.SetActive(false); hookHead.SetActive(false); }

            // teleport return point
            tpMark.SetActive(p.TpBackT > 0);
            if (p.TpBackT > 0)
            {
                tpMark.transform.position = W.Pos(p.TpFx, p.TpFy, 1.2f);
                tpMark.transform.localScale = Vector3.one * (1 + Mathf.Sin(t * 14) * 0.12f);
            }

            youMark.SetActive(showYouMark);
            if (showYouMark) { youMark.transform.localPosition = V(0, 150 + Mathf.Sin(t * 5) * 4, 0); }

            // подсветка владельца мяча (невидимую Тень соперникам не выдаём)
            bool own = m.Ball.Owner == p && m.Phase == MatchPhase.Playing && (!p.Invisible || (m.Local != null && p.Team == m.Local.Team));
            ownRing.SetActive(own); ownWave.SetActive(own);
            if (own)
            {
                ownT = Mathf.Min(1, ownT + Time.unscaledDeltaTime * 5);
                float pop = 1 + 0.35f * (1 - ownT) + 0.06f * Mathf.Sin(t * 8);
                ownRing.transform.localScale = new Vector3(pop, 1, pop);
                float wv = (t * 1.3f) % 1f;
                ownWave.transform.localScale = new Vector3(1 + wv * 1.2f, 1, 1 + wv * 1.2f);
                var wc = ownWaveMat.GetColor("_BaseColor"); wc.a = 0.6f * (1 - wv); ownWaveMat.SetColor("_BaseColor", wc);
            }
            else ownT = 0;

            // «дай пас»: метку видят свои (соперникам незачем знать, кому сейчас отдадут)
            bool call = p.CallT > 0 && m.Local != null && p.Team == m.Local.Team && m.Phase == MatchPhase.Playing;
            callMark.SetActive(call); callWave.SetActive(call);
            if (call)
            {
                callPop = Mathf.Min(1, callPop + Time.unscaledDeltaTime * 6);
                float pop = 1 + 0.6f * Mathf.Sin(callPop * Mathf.PI) * (1 - callPop * 0.5f);
                float fade = Mathf.Clamp01(p.CallT / 0.3f);
                callMark.transform.localPosition = V(0, (showYouMark ? 182 : 158) + Mathf.Sin(t * 9) * 5, 0);
                callMark.transform.localScale = Vector3.one * pop * (0.6f + 0.4f * fade);
                callMark.transform.rotation = Quaternion.Euler(0, t * 120, 0);
                float w = (t * 1.8f) % 1f; // волна расходится от героя раз в ~0,55 с
                callWave.transform.localScale = new Vector3(1 + w * 1.6f, 1, 1 + w * 1.6f);
                callWaveMat.SetColor("_BaseColor", new Color(1f, 0.88f, 0.3f, 0.75f * (1 - w) * fade));
            }
            else callPop = 0;
        }
    }
}
