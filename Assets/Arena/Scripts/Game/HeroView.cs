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
        readonly Mesh arcMesh;
        float arcShown = -1;
        readonly Team team; HeroModel model; int modelHero = -1;
        float prevKickCd, prevCharge, prevLob, prevFireT, animLock;

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
            hookHead = Shapes.MeshObj(Shapes.Cone(9 * S, 22 * S, 10), parent, Mat.Solid(Mat.Hex("#d9e2e6"), 0.6f), name + "_hook");
            tpMark = Shapes.MeshObj(Shapes.Ring((Field.R - 6) * S, (Field.R + 2) * S), parent, Mat.Clear(new Color(0.56f, 0.85f, 1f, 0.8f), true), name + "_tpMark");
            youMark = Shapes.MeshObj(Shapes.Cone(8 * S, 16 * S, 4), Root.transform, Mat.Glow(Mat.Hex("#fff3c4")), "youMark");
            youMark.transform.localRotation = Quaternion.Euler(180, 0, 0);
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

        void DriveModel(SimPlayer p)
        {
            var T = HeroModel.Tune; float dt = HeroModel.FrameDt;
            float speed = new Vector2(p.Vx, p.Vy).magnitude;
            bool kicked = p.KickCd > prevKickCd + 0.05f;
            if (p.FireT > 0 && prevFireT <= 0)
            {   // fire super-kick: the wind-up plays while the sim charges it
                model.Play(HeroAnim.KickStrong, T.fade * 0.5f, true, T.fireSpeed, T.fireStart);
                animLock = p.FireT + T.kickHold;
            }
            else if (kicked && p.FireT <= 0 && prevFireT <= 0)
            {
                bool strong = prevCharge > 0.6f || prevLob > 0.6f;
                if (strong) model.Play(HeroAnim.KickStrong, T.fade * 0.5f, true, T.strongSpeed, T.strongStart);
                else model.Play(HeroAnim.Kick, T.fade * 0.5f, true, T.kickSpeed, T.kickStart);
                animLock = T.kickHold;
            }
            animLock -= dt;
            if (p.Stun > 0) model.Play(HeroAnim.Stun, T.fade);
            else if (p.DashT > 0) model.Play(HeroAnim.Dash, T.fade * 0.5f, false, T.dashSpeed, T.dashStart);
            else if (animLock > 0) { }
            else if (speed > 30) model.Play(HeroAnim.Run, T.fade, false, Mathf.Clamp(speed / T.runRef, 0.6f, 1.8f));
            else model.Play(HeroAnim.Idle, T.fade);
            model.Tick(dt);
            prevKickCd = p.KickCd; prevCharge = p.Charging ? p.Charge : 0; prevLob = p.Lobbing ? p.LobCharge : 0; prevFireT = p.FireT;
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
            tr.rotation = W.Yaw(p.Fx, p.Fy);
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

            if (model != null) DriveModel(p);

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
                Vector3 d = b - a; float L = Mathf.Max(0.01f, d.magnitude);
                rope.SetActive(true); rope.transform.position = (a + b) / 2; rope.transform.localScale = new Vector3(3 * S, 3 * S, L);
                rope.transform.rotation = Quaternion.LookRotation(d.sqrMagnitude > 0 ? d : Vector3.forward);
                hookHead.SetActive(true); hookHead.transform.position = b;
                hookHead.transform.rotation = Quaternion.FromToRotation(Vector3.up, d.sqrMagnitude > 0 ? d.normalized : W.Dir(hook.Dx, hook.Dy));
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
        }
    }
}
