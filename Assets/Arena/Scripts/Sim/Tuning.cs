// All gameplay numbers in one place. Defaults = values agreed in the design doc.
// Shown in the Inspector on the GameRunner object, so balance can be tweaked while playing.
using System;
using UnityEngine;

namespace Arena.Sim
{
    [Serializable]
    public class Tuning
    {
        [Header("Движение и удар")]
        [Tooltip("Скорость героя")] [Range(180, 480)] public float speed = 370;
        [Tooltip("Скорость с мячом, множитель")] [Range(0.5f, 1.1f)] public float dribble = 0.88f;
        [Tooltip("Скорость разворота, рад/с")] [Range(4, 30)] public float turn = 11;
        [Tooltip("Макс. относительная скорость мяча для подбора")] [Range(200, 2200)] public float catchSpd = 1100;
        [Tooltip("Сила удара")] [Range(600, 2000)] public float kick = 1150;
        [Tooltip("Множитель полного заряда удара")] [Range(1, 2.5f)] public float charge = 1.6f;
        [Tooltip("Трение мяча")] [Range(0.3f, 2.5f)] public float friction = 1.0f;
        [Tooltip("Отскок от бортов")] [Range(0.3f, 1)] public float bounce = 0.75f;
        [Tooltip("Сила рывка")] [Range(500, 1400)] public float dashPow = 880;
        [Tooltip("Перезарядка рывка, с")] [Range(0.6f, 5)] public float dashCd = 2.2f;

        [Header("Навес")]
        [Range(100, 600)] public float lobMin = 250;
        [Range(500, 1800)] public float lobMax = 1150;
        [Tooltip("Время полного заряда навеса, с")] [Range(0.3f, 2)] public float lobTime = 1.0f;
        [Tooltip("Высота дуги, множитель")] [Range(0.4f, 2)] public float lobArc = 1.0f;

        [Header("Багор — крюк")]
        [Range(200, 900)] public float hookRange = 520;
        [Range(600, 2600)] public float hookSpd = 1500;
        [Tooltip("Оглушение после притягивания, с")] [Range(0, 1.5f)] public float hookStun = 0.5f;
        [Range(1, 12)] public float hookCd = 5;

        [Header("Жар — огненный суперудар")]
        [Tooltip("Замах, с")] [Range(0.1f, 1.2f)] public float fireDelay = 0.4f;
        [Range(1400, 3200)] public float firePow = 2300;
        [Tooltip("Оглушение от попадания, с")] [Range(0.2f, 2.5f)] public float fireStun = 1.0f;
        [Tooltip("Доля скорости мяча после попадания в игрока")] [Range(0.1f, 0.8f)] public float fireKeep = 0.3f;
        [Range(2, 20)] public float fireCd = 8;

        [Header("Вихрь — кручёный удар")]
        [Tooltip("Сила закрутки, рад/с")] [Range(0.3f, 3)] public float curveAmt = 1.3f;
        [Tooltip("Затухание закрутки")] [Range(0.2f, 3)] public float curveDecay = 0.9f;
        [Range(0.5f, 10)] public float curveCd = 3;

        [Header("Миг — телепорт")]
        [Range(120, 500)] public float tpDist = 260;
        [Tooltip("Окно возврата, с")] [Range(0.4f, 2.5f)] public float tpBack = 1.0f;
        [Range(1, 15)] public float tpCd = 6;
    }
}
