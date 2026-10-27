// State of players, ball and hooks. Plain data; the Match class runs the rules.
namespace Arena.Sim
{
    public class BotBrain
    {
        public float Mx, My, T;
        public bool WantKick;
        public float Kx, Ky, ChargeTo;
        public int Curve; // -1 / +1: the planned kick is a curve shot
    }

    public class SimPlayer
    {
        public int Id;
        public Team Team;
        public Hero Hero;
        public int HumanSlot = -1;   // -1 = bot; 0.. = which local controller drives this hero
        public bool Dummy;           // training mannequin
        public float HomeX, HomeY;   // where a mannequin walks back to

        public float X, Y, Vx, Vy, Fx, Fy = -1;
        public float DashT, DashCd, Stun, KickCd, Flash, GrabCd;
        public bool Charging; public float Charge;
        public bool Lobbing; public float LobCharge;
        public int Curving; public float CurveCharge;
        public float HookCd, FireCd, FireT, TpCd, TpBackT, TpFx, TpFy, CurveCd;
        public readonly BotBrain Brain = new BotBrain();

        public bool IsHuman => HumanSlot >= 0;

        public void ResetForKickoff()
        {
            Lobbing = false; LobCharge = 0; Curving = 0; CurveCharge = 0; CurveCd = 0;
            Brain.Curve = 0; Brain.WantKick = false;
            Vx = Vy = 0; DashT = 0; Stun = 0; Charge = 0; Charging = false; GrabCd = 0;
            HookCd = FireCd = FireT = TpCd = TpBackT = 0;
        }
    }

    public class SimBall
    {
        public float X = Field.FW / 2, Y = Field.FH / 2, Z, Vx, Vy, Vz;
        public SimPlayer Owner, Last, FireBy;
        public float Lock, Spin, Fire, FireGrace;
        public float GlueAng; // where around the carrier the ball sits; rolls to the front after a reception
        public Hook Hook; // set while a hook is reeling the ball in
    }

    public class Hook
    {
        public SimPlayer Owner;
        public float X, Y, Dx, Dy, Dist;
        public bool Back, HasBall, Done;
        public SimPlayer Victim;
    }
}
