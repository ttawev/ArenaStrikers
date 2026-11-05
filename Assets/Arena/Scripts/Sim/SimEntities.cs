// State of players, ball and hooks. Plain data; the Match class runs the rules.
namespace Arena.Sim
{
    public class BotBrain
    {
        public float Mx, My, T;
        public bool WantKick;
        public float Kx, Ky, ChargeTo;
        public int Curve; // -1 / +1: the planned kick is a curve shot
        public int Intent;     // что задумано этим ударом: см. BotStats.*
        public float KickWait; // сколько бот ждёт, пока развернётся для удара
    }

    /// <summary>Счётчики для «лаборатории ботов» (Editor → Arena → Лаборатория ботов): пасы, удары, автоголы.</summary>
    public class BotStats
    {
        public const int None = 0, PassGround = 1, PassLob = 2, Shot = 3, Clear = 4;
        public int PassGroundTry, PassGroundOk, PassLobTry, PassLobOk, Shots, Clears, OwnGoals, Deflected, Goals, Saves, TurnWaits, KickCancels;
    }

    public class SimPlayer
    {
        public int Id;
        public Team Team;
        public Hero Hero;
        public int HumanSlot = -1;   // -1 = bot; 0.. = which local controller drives this hero
        public bool Dummy;           // training mannequin
        public bool Keeper;          // сейчас — последний защитник у ворот (меняется по ходу игры)
        public float HomeX, HomeY;   // where a mannequin walks back to

        public float X, Y, Vx, Vy, Fx, Fy = -1;
        public float DashT, DashCd, Stun, KickCd, Flash, GrabCd;
        public bool Charging; public float Charge;
        public bool Lobbing; public float LobCharge;
        public int Curving; public float CurveCharge;
        public float HookCd, FireCd, FireT, TpCd, TpBackT, TpFx, TpFy, CurveCd;
        public float InvisT, InvisCd; // Тень: time left invisible, cooldown
        public float WallCd;          // Стена
        public float CallT;           // «дай пас»: сколько ещё секунд висит метка над героем
        public float KoX, KoY;        // выход команд: куда бежать к разводке
        public int IntroOrder;        // выход команд: номер в своей команде
        // статистика матча (экран MVP)
        public int Goals, Assists, Saves, Steals;
        public void ResetStats() { Goals = Assists = Saves = Steals = 0; }
        public bool Invisible => InvisT > 0;
        public readonly BotBrain Brain = new BotBrain();

        public bool IsHuman => HumanSlot >= 0;

        public void ResetForKickoff()
        {
            Lobbing = false; LobCharge = 0; Curving = 0; CurveCharge = 0; CurveCd = 0;
            Brain.Curve = 0; Brain.WantKick = false;
            Vx = Vy = 0; DashT = 0; Stun = 0; Charge = 0; Charging = false; GrabCd = 0;
            HookCd = FireCd = FireT = TpCd = TpBackT = 0; InvisT = InvisCd = 0; WallCd = 0; CallT = 0;
        }
    }

    public class SimBall
    {
        public float X = Field.FW / 2, Y = Field.FH / 2, Z, Vx, Vy, Vz;
        public SimPlayer Owner, Last, FireBy;
        public SimPlayer PassBy; // кто из партнёров отдал мяч нынешнему владельцу (для голевых передач)
        public string LastHow = "";
        public SimPlayer Kicker; // последний, кто ОСОЗНАННО сыграл мячом (удар, навес, приём); рикошеты не в счёт // как Last коснулся мяча последним — для журнала автоголов
        public float Lock, Spin, Fire, FireGrace;
        public float GlueAng; // where around the carrier the ball sits; rolls to the front after a reception
        public Hook Hook; // set while a hook is reeling the ball in
    }

    /// <summary>Ice wall: a thick segment centred at (X,Y) along (Tx,Ty). Stops the ball and players; a lob flies over.</summary>
    /// <summary>Ice fissure: a ridge that shoots out from (X,Y) along (Tx,Ty), growing to MaxLen.
    /// Whatever it reaches while growing is shoved aside; afterwards it blocks the ball and players. A lob flies over.</summary>
    public class IceWall
    {
        public SimPlayer Owner;
        public float X, Y, Tx, Ty, Len, MaxLen, Life, MaxLife;
        public bool BallPushed;
        public readonly System.Collections.Generic.HashSet<SimPlayer> Pushed = new System.Collections.Generic.HashSet<SimPlayer>();
        public const float THICK = 30, HEIGHT = 80, GROW_SPEED = 4200;
    }

    public class Hook
    {
        public SimPlayer Owner;
        public float X, Y, Dx, Dy, Dist;
        public bool Back, HasBall, Done;
        public SimPlayer Victim;
    }
}
