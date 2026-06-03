using System;
using System.Diagnostics;

// Native C# baseline: the identical poison-tick logic, hand-written (no interpreter).
class CsBench
{
    static void Main(string[] args)
    {
        int N = args.Length > 0 ? int.Parse(args[0]) : 2000;
        int T = args.Length > 1 ? int.Parse(args[1]) : 200;

        double[] health = new double[N], maxHealth = new double[N];
        double[] poisoned = new double[N];
        void Build()
        {
            for (int i = 0; i < N; i++)
            {
                double mh = 50 + (i % 100);
                maxHealth[i] = mh;
                health[i] = ((i * 37) % (int)mh) + 1;
                poisoned[i] = (i % 3 == 0) ? 1 : 0;
            }
        }

        double checksum = 0, totalDamage = 0;
        void Tick()
        {
            for (int i = 0; i < N; i++)
            {
                double mh = maxHealth[i];
                if (poisoned[i] != 0)
                {
                    double dmg = Math.Clamp(mh * 0.05, 1, 25);
                    health[i] -= dmg;
                    if (health[i] <= 0) { poisoned[i] = 0; health[i] = 0; }
                    totalDamage += dmg;
                }
                else if (health[i] <= mh * 0.30)
                {
                    double regen = Math.Min(mh * 0.02, mh - health[i]);
                    health[i] += regen;
                }
                checksum += health[i];
            }
        }

        // warmup
        Build(); for (int i = 0; i < 3; i++) Tick();
        // timed
        Build(); checksum = 0; totalDamage = 0;
        var sw = Stopwatch.StartNew();
        for (int t = 0; t < T; t++) Tick();
        sw.Stop();

        long ops = (long)N * T;
        Console.WriteLine($"csharp\t{N}\t{T}\t{sw.Elapsed.TotalMilliseconds:F1}\t{ops / sw.Elapsed.TotalSeconds / 1e6:F3}\t{checksum:F0}\t{totalDamage:F0}");
    }
}
