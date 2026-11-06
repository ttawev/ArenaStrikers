# Таймлайн таинственного трейлера (30 с, 30 кадров/с, 900 кадров). Общий для teaser.py (картинка) и teaser_audio.py (звук).
FPS, F_END = 30, 900

LIGHTS = [150, 158, 166, 174]        # прожекторы включаются по одному («клац»)
STEP_FOOT = 262                       # Жар ставит ногу на мяч
# монтажные склейки (на каждой — вспышка и «вжух»)
CUTS = {'step': 240, 'fire': 300, 'net': 342, 'curve': 378, 'hook': 420, 'blink': 456, 'wall': 484,
        'shade': 512, 'run': 538, 'final': 586, 'crowd': 690, 'black': 720, 'title': 780}
FIRE_KICK, FIRE_NET = 316, 350        # огненный удар и мяч в сетке
CURVE_KICK, CURVE_END = 386, 419      # кручёный удар Вихря
HOOK_THROW, HOOK_GRAB, HOOK_BACK = 426, 432, 444
BLINK = 466                           # телепорт Мига
WALL_PUNCH, WALL_HIT = 490, 498       # разлом Стены и отскок мяча
SHADE_VANISH = 522                    # Тень растворяется, мяч катится сам
FINAL_KICK, FINAL_GOAL = 600, 622     # финальный огненный гол — мяч летит прямо в камеру за сеткой
TITLE_POP = 790

# Реплики комментатора: (файл в Builds/Trailer/voice/, секунда начала, текст, как говорить).
# Английский голос: те же файлы v1…v6 в папке Builds/Trailer/voice_en/.
VOICE_EN = [
    ('v1.wav', 1.2, 'Silence. The stadium is still asleep…', 'quiet, almost a whisper'),
    ('v2.wav', 6.4, 'But tonight, it wakes up.', 'quiet, with a smile'),
    ('v3.wav', 11.8, 'What a strike!', 'loud'),
    ('v4.wav', 13.6, 'Look at that curve!', 'fast'),
    ('v5.wav', 20.8, 'GOOOAL!', 'full voice, long'),
    ('v6.wav', 26.4, 'SpellStrike. Coming soon.', 'calm, low, with a pause'),
]

VOICE = [
    ('v1.wav', 1.2, 'Тишина. Стадион ещё спит…', 'тихо, почти шёпотом, медленно'),
    ('v2.wav', 6.4, 'Но этой ночью он проснётся.', 'тихо, с улыбкой'),
    ('v3.wav', 11.8, 'Какой удар!', 'громко, на выдохе'),
    ('v4.wav', 13.6, 'Закрутил!', 'быстро'),
    ('v5.wav', 20.8, 'Го-о-о-ол!', 'во весь голос, тянуть'),
    ('v6.wav', 26.4, 'SpellStrike. Скоро.', 'спокойно, низко, с паузой; читается «Спэлл-страйк»'),
]
