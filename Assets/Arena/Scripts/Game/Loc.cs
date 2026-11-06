// Язык интерфейса: русский или английский. Выбирается в «Настройках», запоминается (PlayerPrefs arena.lang).
// Перевод прямо в коде: Loc.T("по-русски", "in English") — без файлов и ключей.
using UnityEngine;

namespace Arena.Game
{
    public static class Loc
    {
        static int cached = -1;
        /// <summary>true — английский, false — русский.</summary>
        public static bool En
        {
            get { if (cached < 0) cached = PlayerPrefs.GetInt("arena.lang", Default()); return cached == 1; }
            set { cached = value ? 1 : 0; PlayerPrefs.SetInt("arena.lang", cached); PlayerPrefs.Save(); }
        }
        /// <summary>Первый запуск: русский, если система на русском, украинском, белорусском или язык не определён (так бывает в Кыргызстане, Казахстане), иначе английский.</summary>
        static int Default()
        {
            var l = Application.systemLanguage;
            return l == SystemLanguage.Russian || l == SystemLanguage.Ukrainian || l == SystemLanguage.Belarusian || l == SystemLanguage.Unknown ? 0 : 1;
        }
        public static string T(string ru, string en) => En ? en : ru;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { cached = -1; }
    }
}
