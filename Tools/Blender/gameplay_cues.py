# Таймлайн геймплейного трейлера SpellStrike (65 с, 30 кадров/с, 1950 кадров, 16:9).
# Общий для gameplay_trailer.py (картинка) и будущего gameplay_audio.py (звук и музыка).
FPS, F_END = 30, 1950

# 1. Холодное открытие: огненный суперудар Жара в сетку
OPEN_KICK, GOAL_OPEN = 46, 60
# 2. Что это за игра: два розыгрыша с игровой камеры
PLAY1, PLAY1_POST = 150, 332          # пасы, сбивание рывком, удар в штангу
PLAY2, GOAL_PLAY2 = 370, 468          # навес, приём грудью, удар — гол
TEAM_CEL = 520                        # команда празднует вместе («играй с друзьями»)
# 3. Шесть героев, по 4 с
HEROES = 600
V_HOOK, V_CURVE, V_BLINK, V_ICE, V_SHADOW, V_FIRE = 600, 720, 840, 960, 1080, 1200
GOAL_CURVE, GOAL_BLINK, GOAL_SHADOW, GOAL_FIRE = 778, 930, 1180, 1262
# 4. Нарастание и золотой гол
PLAY3, GOLDEN_KICK, GOAL_GOLDEN = 1320, 1474, 1494
MVP = 1600
BLACK = 1722                          # затемнение
# 5. Титр
TITLE, TITLE_POP = 1740, 1746

GOALS = [GOAL_OPEN, GOAL_PLAY2, GOAL_CURVE, GOAL_BLINK, GOAL_SHADOW, GOAL_FIRE, GOAL_GOLDEN]
BIG_GOALS = [GOAL_OPEN, GOAL_PLAY2, GOAL_FIRE, GOAL_GOLDEN]   # стробоскоп, конфетти, трибуны вскакивают
