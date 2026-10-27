// Core types of the match simulation. Pure C#: no scene objects, so the same code can run
// on a network host later. Field units match the browser prototype (field 1500 x 2400).
using System;

namespace Arena.Sim
{
    /// <summary>Home attacks toward y = 0 (right side of the screen), Away toward y = FH.</summary>
    public enum Team { Home, Away }

    public enum Hero { Fire, Hook, Teleport, Curve }

    public enum GameMode { Training = 0, OneVsOne = 1, TwoVsTwo = 2, FourVsFour = 4 }

    /// <summary>One tick of input from a human (or, later, from the network).
    /// Kick, Lob, CurveLeft, CurveRight are held buttons; Dash, Ability, BallToMe fire once on the tick they were pressed.</summary>
    public struct InputCommand
    {
        public float MoveX, MoveY;      // field direction, length 0..1
        public bool Kick, Lob, CurveLeft, CurveRight;
        public bool Dash, Ability, BallToMe;
    }

    public enum FxKind { Burst, Shake, Goal, Rumble, Denied, Impact, Receive }
    public enum FxColor { White, Mist, Fire, FireYellow, Blink, Home, Away, Spin, Dust }

    /// <summary>Something the view should show or play: particles, camera shake, gamepad rumble.</summary>
    public struct SimEvent
    {
        public FxKind Kind;
        public float X, Y, Amount;
        public int Count;
        public FxColor Color;
        public Team Team;
        public SimPlayer Player;
    }

    public static class Field
    {
        public const float FW = 1500, FH = 2400, GOAL = 400, R = 24, BR = 13, POST = 7, NET = 36;
        public const float GOALH = 62, GRAV = 1800, LOW = 35, HEAD = 95; // crossbar, gravity, ball at feet below LOW, hits the body below HEAD
        public const float GX1 = (FW - GOAL) / 2, GX2 = (FW + GOAL) / 2;
        public const float FRONT = 0.34f; // cos 70°: touches within ±70° of facing count as "the face"
        public const float MATCH_TIME = 180, WIN_GOALS = 5;
    }

    /// <summary>Bot difficulty: speed multiplier, reaction time, aim error (rad), dash and ability eagerness.</summary>
    public struct BotLevel
    {
        public float Spd, React, Err, Dash, Abil;
        public static readonly BotLevel[] Levels =
        {
            new BotLevel { Spd = 0.78f, React = 0.24f, Err = 0.32f, Dash = 0.25f, Abil = 0.3f },
            new BotLevel { Spd = 0.93f, React = 0.13f, Err = 0.17f, Dash = 0.55f, Abil = 0.6f },
            new BotLevel { Spd = 1.05f, React = 0.06f, Err = 0.07f, Dash = 0.9f, Abil = 0.9f },
        };
    }

    static class M
    {
        public static float Len(float x, float y) => MathF.Sqrt(x * x + y * y);
        public static (float x, float y) Norm(float x, float y)
        {
            float m = MathF.Sqrt(x * x + y * y);
            if (m == 0) m = 1;
            return (x / m, y / m);
        }
        public static float Clamp(float v, float a, float b) => v < a ? a : (v > b ? b : v);
        public static float Clamp01(float v) => Clamp(v, 0, 1);
        public static (float x, float y) Rot(float x, float y, float a)
        {
            float c = MathF.Cos(a), s = MathF.Sin(a);
            return (x * c - y * s, x * s + y * c);
        }
        /// <summary>Distance from point P to segment AB.</summary>
        public static float SegDist(float px, float py, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay, l2 = vx * vx + vy * vy;
            if (l2 == 0) l2 = 1;
            float t = Clamp(((px - ax) * vx + (py - ay) * vy) / l2, 0, 1);
            return Len(px - ax - vx * t, py - ay - vy * t);
        }
    }
}
