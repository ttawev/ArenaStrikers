// Подиум для экрана выбора героя: модель выбранного героя стоит у ближней бровки на светящемся круге
// и дышит анимацией «стойка»; при смене героя новая модель «выпрыгивает». Камеру наводит ArenaGame (CameraPose).
using Arena.Sim;
using UnityEngine;

namespace Arena.Game
{
    public class HeroShowcase
    {
        /// <summary>Где стоит герой (метры сцены): центр поля по длине, у ближней к камере бровки.</summary>
        public static Vector3 Spot = new Vector3(0, 0, -4.2f);

        readonly GameObject root;
        readonly Transform turn;
        readonly Material ringMat;
        readonly Light keyLight;
        HeroModel model; Hero shown; Team shownTeam; bool hasModel; float shownT;
        float pop, t;

        public HeroShowcase(Transform parent)
        {
            root = new GameObject("showcase");
            root.transform.SetParent(parent, false);
            root.transform.position = Spot;
            turn = new GameObject("turn").transform; turn.SetParent(root.transform, false);
            var disc = Shapes.MeshObj(Shapes.Disc(0.9f, 48), root.transform, Mat.Solid(Mat.Hex("#0d1530"), 0.6f), "podium");
            disc.transform.localPosition = Vector3.up * 0.02f;
            ringMat = Mat.Clear(new Color(1, 0.7f, 0.12f, 0.9f), true);
            var ring = Shapes.MeshObj(Shapes.Ring(0.86f, 0.96f, 64), root.transform, ringMat, "podiumRing");
            ring.transform.localPosition = Vector3.up * 0.03f;
            var lgo = new GameObject("showcaseLight"); lgo.transform.SetParent(root.transform, false);
            lgo.transform.localPosition = new Vector3(-0.8f, 2.4f, -1.6f);
            keyLight = lgo.AddComponent<Light>();
            keyLight.type = LightType.Point; keyLight.range = 6; keyLight.intensity = 3.2f; keyLight.color = new Color(1f, 0.93f, 0.85f);
            keyLight.shadows = LightShadows.None;
            root.SetActive(false);
        }

        public void Show(Hero h, Team team)
        {
            if (!root.activeSelf) root.SetActive(true);
            if (hasModel && h == shown && team == shownTeam) return;
            if (model != null) Object.Destroy(model.Go);
            model = HeroModel.Create(h, team, turn);
            shown = h; shownTeam = team; hasModel = true; pop = 1;
            if (model != null) model.Play(HeroAnim.Celebrate, 0.01f, true); // выбрал героя — он радуется, потом встаёт в стойку
            shownT = 0;
            var c = team == Team.Home ? Mat.Home : Mat.Away;
            ringMat.SetColor("_BaseColor", new Color(c.r, c.g, c.b, 0.95f));
        }

        public void Hide() { if (root.activeSelf) root.SetActive(false); }
        public bool Visible => root.activeSelf;

        /// <summary>Поза камеры для экрана выбора: герой справа от панели меню, за ним поле и дальняя трибуна.</summary>
        public static void CameraPose(out Vector3 pos, out Quaternion rot)
        {
            pos = Spot + new Vector3(0.35f, 1.25f, -3.3f);
            var look = Spot + new Vector3(-0.75f, 0.85f, 0);
            rot = Quaternion.LookRotation(look - pos, Vector3.up);
        }

        public void Tick(float dt, Vector3 camPos)
        {
            if (!root.activeSelf) return;
            t += dt;
            pop = Mathf.MoveTowards(pop, 0, dt * 3.5f);
            // смотрит в камеру, слегка поворачиваясь из стороны в сторону
            Vector3 to = camPos - root.transform.position; to.y = 0;
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg + Mathf.Sin(t * 0.6f) * 22 - 12;
            turn.localRotation = Quaternion.Euler(0, yaw, 0);
            float s = 1 + Mathf.Sin(pop * Mathf.PI) * 0.12f;
            turn.localScale = Vector3.one * s;
            turn.localPosition = Vector3.up * (Mathf.Sin(pop * Mathf.PI) * 0.12f);
            shownT += dt;
            if (model != null) { model.Play(shownT < HeroModel.Tune.showCelebrate ? HeroAnim.Celebrate : HeroAnim.ShowIdle, 0.35f); model.Tick(dt); }
        }
    }
}
