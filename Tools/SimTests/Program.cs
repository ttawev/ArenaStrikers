// Запуск: dotnet run -c Release [-- быстро]
// Подбор баланса: переменные окружения ARENA_<поле Tuning>=число, например ARENA_staminaRun=0.02
using System;
using System.Globalization;
class Program
{
    static int Main(string[] args)
    {
        bool quick = args.Length > 0;
        Arena.Sim.SimSelfTest.MakeTuning = () =>
        {
            var t = new Arena.Sim.Tuning();
            foreach (var f in typeof(Arena.Sim.Tuning).GetFields())
            {
                var v = Environment.GetEnvironmentVariable("ARENA_" + f.Name);
                if (v == null) continue;
                if (f.FieldType == typeof(float)) f.SetValue(t, float.Parse(v, CultureInfo.InvariantCulture));
                else if (f.FieldType == typeof(bool)) f.SetValue(t, v == "1" || v == "true");
            }
            return t;
        };
        var r = Arena.Sim.SimSelfTest.RunAll(quick, Console.WriteLine);
        return r.Failed > 0 ? 1 : 0;
    }
}
