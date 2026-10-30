// UltraPingViewer - dashboard dos logs CSV do UltraPing (disponibilidade, latencia, perda, quedas)
// Compilar com src\compilar.bat (csc.exe do .NET Framework 4, C# 5 - sem sintaxe C# 6).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace UltraPingViewer
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        // argumentos opcionais: [pasta Profiles | pasta do perfil | pasta Logs] /aba:N /host:IP /periodo:24h|hoje|ontem|7d|30d|tudo
        [STAThread]
        static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                MessageBox.Show("Erro inesperado:\n\n" + e.Exception.Message, "UltraPingViewer");
            };
            string path = null, host = null, period = null;
            int tab = 0;
            foreach (string a in args)
            {
                if (a.StartsWith("/aba:", StringComparison.OrdinalIgnoreCase)) int.TryParse(a.Substring(5), out tab);
                else if (a.StartsWith("/host:", StringComparison.OrdinalIgnoreCase)) host = a.Substring(6);
                else if (a.StartsWith("/periodo:", StringComparison.OrdinalIgnoreCase)) period = a.Substring(9);
                else path = a;
            }
            Application.Run(new ViewerForm(path, tab, host, period));
        }
    }

    // ------------------------------------------------------------------
    // Visual
    // ------------------------------------------------------------------
    static class Theme
    {
        public static readonly Color Back = Color.FromArgb(21, 22, 26);
        public static readonly Color Card = Color.FromArgb(32, 34, 40);
        public static readonly Color Border = Color.FromArgb(50, 53, 62);
        public static readonly Color Grid = Color.FromArgb(46, 49, 57);
        public static readonly Color Text = Color.FromArgb(226, 229, 235);
        public static readonly Color Muted = Color.FromArgb(138, 145, 158);
        public static readonly Color Good = Color.FromArgb(72, 199, 116);
        public static readonly Color GoodDim = Color.FromArgb(36, 96, 62);
        public static readonly Color Warn = Color.FromArgb(242, 184, 56);
        public static readonly Color Bad = Color.FromArgb(236, 82, 82);
        public static readonly Color Blue = Color.FromArgb(84, 160, 255);
        public static readonly Color Purple = Color.FromArgb(168, 128, 255);
        public static readonly Color NoData = Color.FromArgb(40, 42, 49);
        public static readonly Color Select = Color.FromArgb(48, 68, 108);

        static float k;
        public static int S(int v)
        {
            if (k == 0)
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) { k = g.DpiX / 96f; }
                if (k < 1f) k = 1f;
            }
            return (int)Math.Round(v * k);
        }

        public static Color Lerp(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        // disponibilidade (0..1) -> cor: verde >= 99,9%, amarelo ~99%, vermelho < 95%
        public static Color Avail(double a)
        {
            if (a < 0) return NoData;
            if (a >= 0.999) return Good;
            if (a >= 0.99) return Lerp(Warn, Good, (a - 0.99) / 0.009);
            if (a >= 0.95) return Lerp(Bad, Warn, (a - 0.95) / 0.04);
            return Bad;
        }
    }

    static class Fonts
    {
        public static readonly Font Title = new Font("Segoe UI Semibold", 14f);
        public static readonly Font H = new Font("Segoe UI Semibold", 10f);
        public static readonly Font Text = new Font("Segoe UI", 9f);
        public static readonly Font Small = new Font("Segoe UI", 8f);
        public static readonly Font Big = new Font("Segoe UI Semibold", 18f);
    }

    static class Fmt
    {
        public static readonly CultureInfo Pt = new CultureInfo("pt-BR");

        public static string Pct(double frac)
        {
            if (frac < 0) return "—";
            double p = frac * 100.0;
            if (p >= 99.0 && p < 100.0) return p.ToString("0.000", Pt) + "%";
            return p.ToString("0.0#", Pt) + "%";
        }

        public static string Pct100(double p)
        {
            if (p < 0) return "—";
            if (p > 0 && p < 0.1) return p.ToString("0.000", Pt) + "%";
            return p.ToString("0.0#", Pt) + "%";
        }

        public static string Ms(double v)
        {
            if (v < 0) return "—";
            if (v < 10) return v.ToString("0.0", Pt) + " ms";
            return v.ToString("0", Pt) + " ms";
        }

        public static string Dur(TimeSpan t)
        {
            if (t.Ticks <= 0) return "0s";
            if (t.TotalSeconds < 60) return ((int)t.TotalSeconds) + "s";
            if (t.TotalMinutes < 60) return t.Minutes + "m " + t.Seconds.ToString("00") + "s";
            if (t.TotalHours < 24) return ((int)t.TotalHours) + "h " + t.Minutes.ToString("00") + "m";
            return ((int)t.TotalDays) + "d " + t.Hours + "h";
        }

        public static string N(long v) { return v.ToString("#,0", Pt); }

        // "1 queda" / "3 quedas"
        public static string Pl(long n, string one, string many) { return N(n) + " " + (n == 1 ? one : many); }

        public static string Bucket(TimeSpan b)
        {
            if (b.TotalMinutes < 60) return ((int)b.TotalMinutes) + " min";
            if (b.TotalHours < 24) return ((int)b.TotalHours) + " h";
            return ((int)b.TotalDays) + " dia(s)";
        }

        public static string When(DateTime t) { return t.ToString("dd/MM HH:mm:ss", Pt); }

        public static string Names(List<HostStats> l, int max)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < l.Count && i < max; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(l[i].Label);
            }
            if (l.Count > max) sb.Append(" e mais ").Append(l.Count - max);
            return sb.ToString();
        }
    }

    // ------------------------------------------------------------------
    // Histograma de latencia (1 ms ate 200 ms, 10 ms ate 2 s)
    // ------------------------------------------------------------------
    static class LatHist
    {
        public const int Bins = 381;

        public static int Index(int ms)
        {
            if (ms < 0) ms = 0;
            if (ms < 200) return ms;
            if (ms < 2000) return 200 + (ms - 200) / 10;
            return Bins - 1;
        }

        public static int Value(int bin)
        {
            if (bin < 200) return bin;
            if (bin < Bins - 1) return 200 + (bin - 200) * 10 + 5;
            return 2000;
        }

        public static int Percentile(int[] h, double p)
        {
            long total = 0;
            foreach (int c in h) total += c;
            if (total == 0) return -1;
            long target = (long)Math.Ceiling(total * p);
            if (target < 1) target = 1;
            long acc = 0;
            for (int i = 0; i < h.Length; i++)
            {
                acc += h[i];
                if (acc >= target) return Value(i);
            }
            return Value(h.Length - 1);
        }
    }

    // ------------------------------------------------------------------
    // Dados
    // ------------------------------------------------------------------
    class Outage
    {
        public HostStats Host;
        public DateTime Start, End;
        public int Fails;
        public bool Ongoing;
        public TimeSpan Duration { get { return End - Start; } }
    }

    class NetEvent
    {
        public DateTime Start, End;
        public List<Outage> Outages = new List<Outage>();
    }

    class CorrGroup
    {
        public List<HostStats> Hosts = new List<HostStats>();
        public int Shared;
    }

    class HostStats
    {
        public string Ip = "", Name = "", Group = "";
        public int Janela;
        public long Ok, Osc, Fail, SumMs;
        public int MinMs = int.MaxValue, MaxMs = -1;
        public int[] Hist = new int[LatHist.Bins];
        public double JitterSum;
        public long JitterN;
        public int LastMs = -1;
        public int[] BOk, BOsc, BFail, BMax;
        public long[] BSum;
        public int[] HourFail = new int[24], HourTotal = new int[24];
        public List<Outage> Outages = new List<Outage>();
        public Outage Open;
        public List<int> EvMinutes = new List<int>();
        public DateTime FirstSeen = DateTime.MaxValue, LastSeen = DateTime.MinValue;
        public string LastStatus = "";
        public int P50 = -1, P95 = -1, P99 = -1;
        public TimeSpan Downtime, Longest, Mttr, Mtbf;

        public HostStats(int nb)
        {
            BOk = new int[nb]; BOsc = new int[nb]; BFail = new int[nb]; BMax = new int[nb]; BSum = new long[nb];
        }

        public long Total { get { return Ok + Osc + Fail; } }
        public double Avail { get { long t = Total; return t > 0 ? 1.0 - Fail / (double)t : -1; } }
        public double LossPct { get { long t = Total; return t > 0 ? (Fail + Osc) * 100.0 / t : -1; } }
        public double OscPct { get { long t = Total; return t > 0 ? Osc * 100.0 / t : -1; } }
        public double AvgMs { get { long n = Ok + Osc; return n > 0 ? SumMs / (double)n : -1; } }
        public double Jitter { get { return JitterN > 0 ? JitterSum / JitterN : -1; } }
        public bool DownNow { get { return Open != null && Open.Ongoing; } }
        public string Label { get { return Name.Length > 0 ? Name : Ip; } }

        public double BucketAvail(int b)
        {
            int t = BOk[b] + BOsc[b] + BFail[b];
            return t > 0 ? 1.0 - BFail[b] / (double)t : -1;
        }

        public string Situation
        {
            get
            {
                if (DownNow) return "FORA há " + Fmt.Dur(Open.Duration);
                return LastStatus;
            }
        }
    }

    class GroupStats
    {
        public string Name;
        public List<HostStats> Hosts = new List<HostStats>();
        public long Ok, Osc, Fail, SumMs;
        public int[] Hist = new int[LatHist.Bins];
        public int Outages, HostsWithOutage, DownNow;
        public TimeSpan Downtime;
        public double JitterSum;
        public int JitterHosts;
        public int P95 = -1;

        public long Total { get { return Ok + Osc + Fail; } }
        public double Avail { get { long t = Total; return t > 0 ? 1.0 - Fail / (double)t : -1; } }
        public double LossPct { get { long t = Total; return t > 0 ? (Fail + Osc) * 100.0 / t : -1; } }
        public double AvgMs { get { long n = Ok + Osc; return n > 0 ? SumMs / (double)n : -1; } }
        public double Jitter { get { return JitterHosts > 0 ? JitterSum / JitterHosts : -1; } }
    }

    class Dataset
    {
        public string Source, Profile;
        public DateTime From, To, DataFirst = DateTime.MaxValue, DataLast = DateTime.MinValue;
        public TimeSpan Bucket;
        public int NB;
        public List<HostStats> Hosts = new List<HostStats>();
        public List<Outage> Outages = new List<Outage>();
        public List<NetEvent> Events = new List<NetEvent>();
        public List<CorrGroup> Corr = new List<CorrGroup>();
        public List<GroupStats> Groups = new List<GroupStats>();
        public long[] NOk, NOsc, NFail, NSum;
        public int[][] NHist;
        public double[] NetAvail, NetLat, NetP95, NetFailPct, NetOscPct, NetDown;
        public int[] Hist = new int[LatHist.Bins];
        public int[,] WeekFail = new int[7, 24], WeekOsc = new int[7, 24], WeekTotal = new int[7, 24];
        public long Rows, BadRows, Ok, Osc, Fail, SumMs;
        public int Files, P95 = -1, P99 = -1;
        public double JitterAvg = -1;
        public TimeSpan Downtime;

        public long Total { get { return Ok + Osc + Fail; } }
        public double Avail { get { long t = Total; return t > 0 ? 1.0 - Fail / (double)t : -1; } }
        public double AvgMs { get { long n = Ok + Osc; return n > 0 ? SumMs / (double)n : -1; } }
        public TimeSpan Span { get { return TimeSpan.FromTicks(Bucket.Ticks * NB); } }

        public int BucketOf(DateTime t)
        {
            long i = (t - From).Ticks / Bucket.Ticks;
            if (i < 0) i = 0;
            if (i >= NB) i = NB - 1;
            return (int)i;
        }

        public DateTime BucketStart(int i) { return From.AddTicks(Bucket.Ticks * i); }
    }

    // sub-redes / VLANs: vlans.txt ao lado do exe ("127.0.0.0/8;Loopback"); sem o arquivo, agrupa por /24
    class Groups
    {
        class Net { public uint Addr, Mask; public int Bits; public string Name; }
        readonly List<Net> nets = new List<Net>();

        public static Groups Load(string path)
        {
            Groups g = new Groups();
            try
            {
                if (!File.Exists(path)) return g;
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int sc = line.IndexOf(';');
                    string cidr = sc > 0 ? line.Substring(0, sc).Trim() : line;
                    string name = sc > 0 ? line.Substring(sc + 1).Trim() : cidr;
                    int sl = cidr.IndexOf('/');
                    int bits = 24;
                    if (sl > 0 && !int.TryParse(cidr.Substring(sl + 1), out bits)) continue;
                    uint a;
                    if (!TryIp(sl > 0 ? cidr.Substring(0, sl) : cidr, out a) || bits < 0 || bits > 32) continue;
                    Net n = new Net();
                    n.Bits = bits;
                    n.Mask = bits == 0 ? 0u : uint.MaxValue << (32 - bits);
                    n.Addr = a & n.Mask;
                    n.Name = name;
                    g.nets.Add(n);
                }
            }
            catch { }
            return g;
        }

        public string Of(string ip)
        {
            uint a;
            if (!TryIp(ip, out a)) return "(nomes / outros)";
            Net best = null;
            foreach (Net n in nets)
                if ((a & n.Mask) == n.Addr && (best == null || n.Bits > best.Bits)) best = n;
            if (best != null) return best.Name;
            return ((a >> 24) & 255) + "." + ((a >> 16) & 255) + "." + ((a >> 8) & 255) + ".0/24";
        }

        public static bool TryIp(string s, out uint a)
        {
            a = 0;
            string[] p = s.Trim().Split('.');
            if (p.Length != 4) return false;
            for (int i = 0; i < 4; i++)
            {
                int v;
                if (!int.TryParse(p[i], out v) || v < 0 || v > 255) return false;
                a = (a << 8) | (uint)v;
            }
            return true;
        }
    }

    // ------------------------------------------------------------------
    // Leitura dos logs
    // ------------------------------------------------------------------
    static class Loader
    {
        public delegate void Progress(int done, int total, string msg);
        public delegate bool Cancelled();

        const int EventMinHosts = 5;          // queda em massa: 5+ hosts
        const int EventWindowSec = 90;        // ... comecando em ate 90 s

        public static TimeSpan PickBucket(TimeSpan range)
        {
            TimeSpan[] opts = new TimeSpan[] {
                TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10),
                TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(2),
                TimeSpan.FromHours(3), TimeSpan.FromHours(6), TimeSpan.FromHours(12), TimeSpan.FromDays(1) };
            foreach (TimeSpan o in opts)
                if (range.Ticks / o.Ticks <= 360) return o;
            return TimeSpan.FromDays(Math.Ceiling(range.TotalDays / 360));
        }

        public static List<DateTime> LogDays(string logsDir)
        {
            List<DateTime> days = new List<DateTime>();
            try
            {
                if (!Directory.Exists(logsDir)) return days;
                foreach (string dd in Directory.GetDirectories(logsDir))
                {
                    DateTime day;
                    if (DateTime.TryParseExact(Path.GetFileName(dd), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
                        days.Add(day);
                }
            }
            catch { }
            days.Sort();
            return days;
        }

        public static Dataset Load(string logsDir, DateTime from, DateTime to, Groups groups, Progress cb, Cancelled cancel)
        {
            Dataset d = new Dataset();
            d.Source = logsDir;
            if (to <= from) to = from.AddMinutes(1);
            d.Bucket = PickBucket(to - from);
            d.From = new DateTime(from.Ticks - from.Ticks % d.Bucket.Ticks);
            d.To = to;
            d.NB = (int)Math.Max(1, (to.Ticks - d.From.Ticks + d.Bucket.Ticks - 1) / d.Bucket.Ticks);
            d.NOk = new long[d.NB]; d.NOsc = new long[d.NB]; d.NFail = new long[d.NB]; d.NSum = new long[d.NB];
            d.NHist = new int[d.NB][];
            for (int i = 0; i < d.NB; i++) d.NHist[i] = new int[LatHist.Bins];

            List<string> files = new List<string>();
            foreach (DateTime day in LogDays(logsDir))
            {
                if (day.AddDays(1) <= from || day >= to) continue;
                string[] fs = Directory.GetFiles(Path.Combine(logsDir, day.ToString("yyyy-MM-dd")), "*.csv");
                Array.Sort(fs, StringComparer.OrdinalIgnoreCase);
                files.AddRange(fs);
            }

            Dictionary<string, HostStats> map = new Dictionary<string, HostStats>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Count; i++)
            {
                if (cancel != null && cancel()) return null;
                if (cb != null && i % 25 == 0) cb(i, files.Count, Path.GetFileName(Path.GetDirectoryName(files[i])));
                try { ReadFile(d, map, files[i], from, to); }
                catch { d.BadRows++; }
            }
            if (cb != null) cb(files.Count, files.Count, "calculando");
            Finish(d, map, groups);
            return d;
        }

        static bool ParseTime(string s, out DateTime t)
        {
            t = DateTime.MinValue;
            if (s.Length < 19 || s[4] != '-' || s[7] != '-' || s[10] != ' ' || s[13] != ':' || s[16] != ':') return false;
            int y = D(s, 0, 4), mo = D(s, 5, 2), da = D(s, 8, 2), h = D(s, 11, 2), mi = D(s, 14, 2), se = D(s, 17, 2);
            if (y < 2000 || mo < 1 || mo > 12 || da < 1 || da > 31 || h > 23 || mi > 59 || se > 59) return false;
            try { t = new DateTime(y, mo, da, h, mi, se); return true; }
            catch { return false; }
        }

        static int D(string s, int pos, int len)
        {
            int v = 0;
            for (int i = pos; i < pos + len; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9') return -1;
                v = v * 10 + (c - '0');
            }
            return v;
        }

        static int Int(string s, int start, int end)
        {
            int v = 0;
            bool any = false;
            for (int i = start; i < end; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9') break;
                v = v * 10 + (c - '0');
                any = true;
            }
            return any ? v : -1;
        }

        static void ReadFile(Dataset d, Dictionary<string, HostStats> map, string file, DateTime from, DateTime to)
        {
            d.Files++;
            int[] sep = new int[7];
            HostStats h = null;
            using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader r = new StreamReader(fs, Encoding.UTF8, true, 1 << 16))
            {
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    if (line.Length < 20 || line[0] < '0' || line[0] > '9') continue;   // cabecalho ou lixo
                    int n = 0;
                    for (int i = 0; i < line.Length && n < 7; i++)
                        if (line[i] == ';') sep[n++] = i;
                    if (n < 5) { d.BadRows++; continue; }
                    DateTime t;
                    if (!ParseTime(line, out t)) { d.BadRows++; continue; }
                    if (t < from || t >= to) continue;

                    int hs = sep[2] + 1, he = sep[3];
                    if (h == null || h.Ip.Length != he - hs || string.CompareOrdinal(line, hs, h.Ip, 0, he - hs) != 0)
                    {
                        string ip = line.Substring(hs, he - hs);
                        string key = ip.Length > 0 ? ip : "Janela" + line.Substring(sep[0] + 1, sep[1] - sep[0] - 1);
                        if (!map.TryGetValue(key, out h))
                        {
                            h = new HostStats(d.NB);
                            h.Ip = ip;
                            map[key] = h;
                        }
                    }
                    int jan = Int(line, sep[0] + 1, sep[1]);
                    if (jan > 0) h.Janela = jan;
                    if (sep[2] - sep[1] - 1 > 0 && (h.Name.Length != sep[2] - sep[1] - 1 || string.CompareOrdinal(line, sep[1] + 1, h.Name, 0, h.Name.Length) != 0))
                        h.Name = line.Substring(sep[1] + 1, sep[2] - sep[1] - 1);

                    char c0 = line[sep[3] + 1];
                    int st;
                    if (c0 == 'F') st = 2;
                    else if (c0 == 'O' && sep[3] + 2 < line.Length && line[sep[3] + 2] == 'S') st = 1;
                    else if (c0 == 'O') st = 0;
                    else { d.BadRows++; continue; }
                    int ms = n >= 5 ? Int(line, sep[4] + 1, n >= 6 ? sep[5] : line.Length) : -1;
                    if (st != 2 && ms < 0) ms = 0;
                    Add(d, h, t, st, ms);
                }
            }
        }

        static void Add(Dataset d, HostStats h, DateTime t, int st, int ms)
        {
            d.Rows++;
            if (t < h.FirstSeen) h.FirstSeen = t;
            // buraco nos logs (programa fechado) fecha a queda em aberto no ultimo registro
            if (h.Open != null && (t - h.LastSeen).TotalMinutes > 10) { h.Open.End = h.LastSeen; h.Open = null; }
            if (t > h.LastSeen) h.LastSeen = t;
            if (t < d.DataFirst) d.DataFirst = t;
            if (t > d.DataLast) d.DataLast = t;
            int b = d.BucketOf(t);
            int hour = t.Hour;
            int dow = ((int)t.DayOfWeek + 6) % 7;   // 0 = segunda
            h.HourTotal[hour]++;
            d.WeekTotal[dow, hour]++;
            if (st != 0)
            {
                int minute = (int)((t - d.From).Ticks / TimeSpan.TicksPerMinute);
                if (h.EvMinutes.Count == 0 || h.EvMinutes[h.EvMinutes.Count - 1] != minute) h.EvMinutes.Add(minute);
            }
            if (st == 2)
            {
                h.Fail++; h.BFail[b]++; d.NFail[b]++; h.HourFail[hour]++; d.WeekFail[dow, hour]++;
                if (h.Open == null)
                {
                    h.Open = new Outage();
                    h.Open.Host = h;
                    h.Open.Start = t;
                    h.Outages.Add(h.Open);
                }
                h.Open.Fails++;
                h.Open.End = t;
                h.LastMs = -1;
                h.LastStatus = "FALHA";
                return;
            }
            if (st == 1) { h.Osc++; h.BOsc[b]++; d.NOsc[b]++; d.WeekOsc[dow, hour]++; h.LastStatus = "OSCILOU"; }
            else { h.Ok++; h.BOk[b]++; d.NOk[b]++; h.LastStatus = "OK"; }
            if (h.Open != null) { h.Open.End = t; h.Open = null; }   // voltou: a queda termina aqui
            h.SumMs += ms; h.BSum[b] += ms; d.NSum[b] += ms;
            if (ms < h.MinMs) h.MinMs = ms;
            if (ms > h.MaxMs) h.MaxMs = ms;
            if (ms > h.BMax[b]) h.BMax[b] = ms;
            int hi = LatHist.Index(ms);
            h.Hist[hi]++;
            d.NHist[b][hi]++;
            if (h.LastMs >= 0) { h.JitterSum += Math.Abs(ms - h.LastMs); h.JitterN++; }
            h.LastMs = ms;
        }

        static void Finish(Dataset d, Dictionary<string, HostStats> map, Groups groups)
        {
            DateTime now = DateTime.Now;
            DateTime liveEnd = d.To < now ? d.To : now;
            double jsum = 0;
            int jn = 0;
            foreach (HostStats h in map.Values)
            {
                if (h.Open != null)
                {
                    // ainda caido no fim do periodo (e o log continua chegando) = queda em andamento
                    h.Open.Ongoing = (liveEnd - h.LastSeen).TotalMinutes < 5;
                    if (h.Open.Ongoing && liveEnd > h.Open.End) h.Open.End = liveEnd;
                    if (!h.Open.Ongoing) h.Open = null;
                }
                h.P50 = LatHist.Percentile(h.Hist, 0.50);
                h.P95 = LatHist.Percentile(h.Hist, 0.95);
                h.P99 = LatHist.Percentile(h.Hist, 0.99);
                if (h.MinMs == int.MaxValue) h.MinMs = -1;
                long closedTicks = 0;
                int closed = 0;
                foreach (Outage o in h.Outages)
                {
                    h.Downtime += o.Duration;
                    if (o.Duration > h.Longest) h.Longest = o.Duration;
                    if (!o.Ongoing) { closedTicks += o.Duration.Ticks; closed++; }
                    d.Outages.Add(o);
                }
                if (closed > 0) h.Mttr = TimeSpan.FromTicks(closedTicks / closed);
                if (h.Outages.Count > 0 && h.LastSeen > h.FirstSeen)
                {
                    TimeSpan up = (h.LastSeen - h.FirstSeen) - h.Downtime;
                    if (up.Ticks > 0) h.Mtbf = TimeSpan.FromTicks(up.Ticks / h.Outages.Count);
                }
                h.Group = groups.Of(h.Ip);
                d.Hosts.Add(h);
                d.Ok += h.Ok; d.Osc += h.Osc; d.Fail += h.Fail; d.SumMs += h.SumMs;
                d.Downtime += h.Downtime;
                for (int i = 0; i < LatHist.Bins; i++) d.Hist[i] += h.Hist[i];
                if (h.JitterN > 0) { jsum += h.Jitter; jn++; }
            }
            d.Hosts.Sort(delegate(HostStats a, HostStats b)
            {
                int c = a.Janela.CompareTo(b.Janela);
                return c != 0 ? c : string.CompareOrdinal(a.Ip, b.Ip);
            });
            if (jn > 0) d.JitterAvg = jsum / jn;
            d.P95 = LatHist.Percentile(d.Hist, 0.95);
            d.P99 = LatHist.Percentile(d.Hist, 0.99);

            // series da rede por intervalo
            d.NetAvail = new double[d.NB]; d.NetLat = new double[d.NB]; d.NetP95 = new double[d.NB];
            d.NetFailPct = new double[d.NB]; d.NetOscPct = new double[d.NB]; d.NetDown = new double[d.NB];
            for (int b = 0; b < d.NB; b++)
            {
                long tot = d.NOk[b] + d.NOsc[b] + d.NFail[b];
                long resp = d.NOk[b] + d.NOsc[b];
                d.NetAvail[b] = tot > 0 ? 100.0 * (1.0 - d.NFail[b] / (double)tot) : -1;
                d.NetFailPct[b] = tot > 0 ? 100.0 * d.NFail[b] / tot : -1;
                d.NetOscPct[b] = tot > 0 ? 100.0 * d.NOsc[b] / tot : -1;
                d.NetLat[b] = resp > 0 ? d.NSum[b] / (double)resp : -1;
                d.NetP95[b] = resp > 0 ? LatHist.Percentile(d.NHist[b], 0.95) : -1;
                int down = 0;
                foreach (HostStats h in d.Hosts) if (h.BFail[b] > 0) down++;
                d.NetDown[b] = tot > 0 ? down : -1;
            }
            d.NHist = null;   // libera memoria

            BuildEvents(d);
            BuildGroups(d);
            Correlate(d);
        }

        static void BuildEvents(Dataset d)
        {
            List<Outage> all = new List<Outage>(d.Outages);
            all.Sort(delegate(Outage a, Outage b) { return a.Start.CompareTo(b.Start); });
            int i = 0;
            while (i < all.Count)
            {
                int j = i;
                while (j + 1 < all.Count && (all[j + 1].Start - all[i].Start).TotalSeconds <= EventWindowSec) j++;
                if (j - i + 1 >= EventMinHosts)
                {
                    NetEvent ev = new NetEvent();
                    ev.Start = all[i].Start;
                    ev.End = all[i].End;
                    for (int k = i; k <= j; k++)
                    {
                        ev.Outages.Add(all[k]);
                        if (all[k].End > ev.End) ev.End = all[k].End;
                    }
                    d.Events.Add(ev);
                    i = j + 1;
                }
                else i++;
            }
            d.Events.Reverse();   // mais recente primeiro
        }

        static void BuildGroups(Dataset d)
        {
            Dictionary<string, GroupStats> gm = new Dictionary<string, GroupStats>();
            foreach (HostStats h in d.Hosts)
            {
                GroupStats g;
                if (!gm.TryGetValue(h.Group, out g)) { g = new GroupStats(); g.Name = h.Group; gm[h.Group] = g; }
                g.Hosts.Add(h);
                g.Ok += h.Ok; g.Osc += h.Osc; g.Fail += h.Fail; g.SumMs += h.SumMs;
                for (int i = 0; i < LatHist.Bins; i++) g.Hist[i] += h.Hist[i];
                g.Outages += h.Outages.Count;
                if (h.Outages.Count > 0) g.HostsWithOutage++;
                if (h.DownNow) g.DownNow++;
                g.Downtime += h.Downtime;
                if (h.JitterN > 0) { g.JitterSum += h.Jitter; g.JitterHosts++; }
            }
            foreach (GroupStats g in gm.Values)
            {
                g.P95 = LatHist.Percentile(g.Hist, 0.95);
                d.Groups.Add(g);
            }
            d.Groups.Sort(delegate(GroupStats a, GroupStats b) { return a.Avail.CompareTo(b.Avail); });
        }

        // hosts cujas falhas/oscilacoes acontecem nos mesmos minutos (provavel switch, link ou energia em comum)
        static void Correlate(Dataset d)
        {
            Dictionary<int, int> perMinute = new Dictionary<int, int>();
            foreach (HostStats h in d.Hosts)
                foreach (int m in h.EvMinutes)
                {
                    int c;
                    perMinute.TryGetValue(m, out c);
                    perMinute[m] = c + 1;
                }
            int limit = Math.Max(8, d.Hosts.Count * 3 / 10);   // minutos de pane geral nao contam
            List<HostStats> cand = new List<HostStats>();
            List<int[]> sets = new List<int[]>();
            foreach (HostStats h in d.Hosts)
            {
                List<int> keep = new List<int>();
                foreach (int m in h.EvMinutes) if (perMinute[m] <= limit) keep.Add(m);
                if (keep.Count >= 3 && keep.Count <= 50000) { cand.Add(h); sets.Add(keep.ToArray()); }
            }
            int n = cand.Count;
            int[] parent = new int[n];
            int[] best = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    int inter = Inter(sets[i], sets[j]);
                    if (inter < 3) continue;
                    double jac = inter / (double)(sets[i].Length + sets[j].Length - inter);
                    if (jac < 0.4) continue;
                    int ri = Root(parent, i), rj = Root(parent, j);
                    if (ri != rj) parent[rj] = ri;
                    if (inter > best[i]) best[i] = inter;
                    if (inter > best[j]) best[j] = inter;
                }
            Dictionary<int, CorrGroup> groups = new Dictionary<int, CorrGroup>();
            for (int i = 0; i < n; i++)
            {
                int r = Root(parent, i);
                CorrGroup g;
                if (!groups.TryGetValue(r, out g)) { g = new CorrGroup(); groups[r] = g; }
                g.Hosts.Add(cand[i]);
                if (best[i] > g.Shared) g.Shared = best[i];
            }
            foreach (CorrGroup g in groups.Values)
                if (g.Hosts.Count >= 2) d.Corr.Add(g);
            d.Corr.Sort(delegate(CorrGroup a, CorrGroup b)
            {
                int c = b.Hosts.Count.CompareTo(a.Hosts.Count);
                return c != 0 ? c : b.Shared.CompareTo(a.Shared);
            });
        }

        static int Root(int[] p, int i)
        {
            while (p[i] != i) { p[i] = p[p[i]]; i = p[i]; }
            return i;
        }

        static int Inter(int[] a, int[] b)
        {
            int i = 0, j = 0, n = 0;
            while (i < a.Length && j < b.Length)
            {
                if (a[i] == b[j]) { n++; i++; j++; }
                else if (a[i] < b[j]) i++;
                else j++;
            }
            return n;
        }
    }

    // ------------------------------------------------------------------
    // Diagnostico automatico
    // ------------------------------------------------------------------
    class Insight
    {
        public Color C;
        public string T;
        public Insight(Color c, string t) { C = c; T = t; }
    }

    static class Analysis
    {
        public static List<Insight> Build(Dataset d)
        {
            List<Insight> r = new List<Insight>();
            if (d == null || d.Total == 0)
            {
                r.Add(new Insight(Theme.Muted, "Sem amostras no período escolhido. Confira o perfil, o período e se o Log CSV está ligado no UltraPing."));
                return r;
            }
            double av = d.Avail;
            if (av >= 0.999) r.Add(new Insight(Theme.Good, "Disponibilidade da rede em " + Fmt.Pct(av) + ": dentro da meta de 99,9%."));
            else r.Add(new Insight(av >= 0.99 ? Theme.Warn : Theme.Bad, "Disponibilidade da rede em " + Fmt.Pct(av) + ": abaixo da meta de 99,9%."));

            List<HostStats> downNow = new List<HostStats>();
            foreach (HostStats h in d.Hosts) if (h.DownNow) downNow.Add(h);
            if (downNow.Count > 0) r.Add(new Insight(Theme.Bad, downNow.Count + " host(s) fora agora: " + Fmt.Names(downNow, 6) + "."));

            List<HostStats> byDown = new List<HostStats>();
            long totalDown = 0;
            foreach (HostStats h in d.Hosts) if (h.Downtime.Ticks > 0) { byDown.Add(h); totalDown += h.Downtime.Ticks; }
            byDown.Sort(delegate(HostStats a, HostStats b) { return b.Downtime.CompareTo(a.Downtime); });
            if (byDown.Count == 0) r.Add(new Insight(Theme.Good, "Nenhuma queda confirmada no período."));
            else
            {
                long acc = 0;
                int k = 0;
                while (k < byDown.Count && acc < totalDown * 0.8) { acc += byDown[k].Downtime.Ticks; k++; }
                if (byDown.Count >= 4 && k <= byDown.Count / 2)
                    r.Add(new Insight(Theme.Warn, k + " host(s) concentram " + (acc * 100.0 / totalDown).ToString("0", Fmt.Pt) + "% de todo o tempo fora do ar: " + Fmt.Names(byDown.GetRange(0, k), 5) + ". Atacar esses resolve a maior parte."));
                else
                    r.Add(new Insight(Theme.Warn, byDown.Count + " host(s) tiveram queda. Maior tempo fora: " + byDown[0].Label + " (" + Fmt.Dur(byDown[0].Downtime) + ")."));
            }

            if (d.Events.Count > 0)
            {
                NetEvent big = d.Events[0];
                foreach (NetEvent e in d.Events) if (e.Outages.Count > big.Outages.Count) big = e;
                r.Add(new Insight(Theme.Bad, d.Events.Count + " queda(s) em massa (5+ hosts caindo em até 90 s). A maior derrubou " + big.Outages.Count + " hosts em " + Fmt.When(big.Start) + ". Verifique uplinks, core, firewall e energia nesse horário."));
            }

            for (int i = 0; i < d.Corr.Count && i < 3; i++)
            {
                CorrGroup g = d.Corr[i];
                r.Add(new Insight(Theme.Warn, "Falham ou oscilam juntos (" + g.Shared + " minutos em comum), provável switch, porta, link ou energia em comum: " + Fmt.Names(g.Hosts, 6) + "."));
            }

            List<HostStats> flap = new List<HostStats>();
            foreach (HostStats h in d.Hosts) if (h.Outages.Count >= 5) flap.Add(h);
            flap.Sort(delegate(HostStats a, HostStats b) { return b.Outages.Count.CompareTo(a.Outages.Count); });
            if (flap.Count > 0)
                r.Add(new Insight(Theme.Warn, flap.Count + " host(s) com quedas repetidas (5 ou mais): " + Fmt.Names(flap, 5) + " (pior: " + flap[0].Outages.Count + " quedas). Típico de cabo, porta, PoE ou equipamento com defeito."));

            List<HostStats> unst = new List<HostStats>();
            foreach (HostStats h in d.Hosts) if (h.Osc >= 3 && h.OscPct > 0.5) unst.Add(h);
            unst.Sort(delegate(HostStats a, HostStats b) { return b.OscPct.CompareTo(a.OscPct); });
            if (unst.Count > 0)
                r.Add(new Insight(Theme.Warn, unst.Count + " host(s) instáveis (perdem ping em mais de 0,5% dos ciclos sem chegar a cair): " + Fmt.Names(unst, 5) + "."));

            List<HostStats> slow = new List<HostStats>();
            foreach (HostStats h in d.Hosts) if (h.P95 > 100) slow.Add(h);
            slow.Sort(delegate(HostStats a, HostStats b) { return b.P95.CompareTo(a.P95); });
            if (slow.Count > 0)
                r.Add(new Insight(Theme.Warn, slow.Count + " host(s) com p95 de latência acima de 100 ms (pior: " + slow[0].Label + ", " + slow[0].P95 + " ms)."));

            List<HostStats> jit = new List<HostStats>();
            foreach (HostStats h in d.Hosts) if (h.Jitter > 30) jit.Add(h);
            jit.Sort(delegate(HostStats a, HostStats b) { return b.Jitter.CompareTo(a.Jitter); });
            if (jit.Count > 0)
                r.Add(new Insight(Theme.Warn, jit.Count + " host(s) com jitter acima de 30 ms (ruim para áudio/vídeo em tempo real): " + Fmt.Names(jit, 5) + "."));

            GroupStats worst = null, slowest = null;
            foreach (GroupStats g in d.Groups)
            {
                if (g.Total == 0) continue;
                if (worst == null || g.Avail < worst.Avail) worst = g;
                if (slowest == null || g.P95 > slowest.P95) slowest = g;
            }
            if (worst != null && worst.Avail < 0.999 && d.Groups.Count > 1)
                r.Add(new Insight(Theme.Warn, "Sub-rede/VLAN com pior disponibilidade: " + worst.Name + " (" + Fmt.Pct(worst.Avail) + ", " + worst.HostsWithOutage + " de " + worst.Hosts.Count + " hosts com queda)."));
            if (slowest != null && slowest.P95 > 20 && d.Groups.Count > 1)
                r.Add(new Insight(Theme.Muted, "Sub-rede/VLAN mais lenta: " + slowest.Name + " (p95 " + slowest.P95 + " ms)."));

            int bh = -1;
            double br = 0;
            long failSum = 0;
            for (int h = 0; h < 24; h++)
            {
                long f = 0, t = 0;
                for (int w = 0; w < 7; w++) { f += d.WeekFail[w, h]; t += d.WeekTotal[w, h]; }
                failSum += f;
                if (t > 0 && f / (double)t > br) { br = f / (double)t; bh = h; }
            }
            if (bh >= 0 && failSum >= 10)
                r.Add(new Insight(Theme.Muted, "Horário com mais falhas: " + bh + "h (" + Fmt.Pct100(br * 100) + " dos ciclos). Veja a aba Padrões para o mapa por dia da semana."));

            r.Add(new Insight(Theme.Muted, "Base: " + Fmt.N(d.Rows) + " amostras de " + d.Hosts.Count + " hosts em " + d.Files + " arquivos, de " + Fmt.When(d.DataFirst) + " a " + Fmt.When(d.DataLast) + "."));
            return r;
        }
    }

    // ------------------------------------------------------------------
    // Desenho
    // ------------------------------------------------------------------
    delegate int Painter(Graphics g, int width, Canvas hits);
    delegate string HitText(Point p);
    delegate void HitClick(Point p);

    class Hit
    {
        public Rectangle R;
        public HitText Text;
        public HitClick Click;
    }

    class Canvas : Control
    {
        public readonly List<Hit> Hits = new List<Hit>();
        public Painter Painter;
        readonly ToolTip tip = new ToolTip();
        string lastTip;

        public Canvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Back;
            tip.InitialDelay = 0;
            tip.ReshowDelay = 0;
        }

        public void AddHit(Rectangle r, HitText t, HitClick c)
        {
            Hit h = new Hit();
            h.R = r; h.Text = t; h.Click = c;
            Hits.Add(h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Back);
            Hits.Clear();
            if (Painter != null) Painter(e.Graphics, Width, this);
        }

        Hit Find(Point p)
        {
            for (int i = Hits.Count - 1; i >= 0; i--)
                if (Hits[i].R.Contains(p)) return Hits[i];
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Hit h = Find(e.Location);
            string t = (h != null && h.Text != null) ? h.Text(e.Location) : null;
            Cursor = (h != null && h.Click != null) ? Cursors.Hand : Cursors.Default;
            if (t != lastTip)
            {
                lastTip = t;
                if (t == null) tip.Hide(this);
                else tip.Show(t, this, e.X + 16, e.Y + 18);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            lastTip = null;
            tip.Hide(this);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            Hit h = Find(e.Location);
            if (h != null && h.Click != null) h.Click(e.Location);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollableControl p = Parent as ScrollableControl;
            if (p != null)
            {
                int y = -p.AutoScrollPosition.Y - e.Delta;
                if (y < 0) y = 0;
                p.AutoScrollPosition = new Point(0, y);
            }
        }
    }

    // painel com rolagem que mede a altura do desenho
    class ScrollHost : Panel
    {
        public readonly Canvas C = new Canvas();

        public ScrollHost(Painter p)
        {
            AutoScroll = true;
            BackColor = Theme.Back;
            C.Painter = p;
            C.Location = Point.Empty;
            Controls.Add(C);
        }

        public void Relayout()
        {
            int w = ClientSize.Width;
            if (w < 300) w = 300;
            int h;
            using (Bitmap b = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(b)) { h = C.Painter(g, w, null); }
            if (h < ClientSize.Height) h = ClientSize.Height;
            if (C.Width != w || C.Height != h) C.Size = new Size(w, h);
            C.Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (!Focused) Focus();
        }
    }

    static class Draw
    {
        public const TextFormatFlags L = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        public const TextFormatFlags R = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        public const TextFormatFlags C = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

        public static int S(int v) { return Theme.S(v); }

        public static void Text(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags fl)
        {
            TextRenderer.DrawText(g, s, f, r, c, fl);
        }

        public static int TextW(Graphics g, string s, Font f)
        {
            return TextRenderer.MeasureText(g, s, f, new Size(10000, 100), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
        }

        public static void Box(Graphics g, Rectangle r)
        {
            using (SolidBrush b = new SolidBrush(Theme.Card)) g.FillRectangle(b, r);
            using (Pen p = new Pen(Theme.Border)) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
        }

        // caixa com titulo; devolve a area interna
        public static Rectangle Panel(Graphics g, Rectangle r, string title, string sub)
        {
            Box(g, r);
            Rectangle tr = new Rectangle(r.X + S(12), r.Y + S(6), r.Width - S(24), S(24));
            Text(g, title, Fonts.H, Theme.Text, tr, L);
            if (!string.IsNullOrEmpty(sub))
            {
                int tw = TextW(g, title, Fonts.H) + S(12);
                Text(g, sub, Fonts.Small, Theme.Muted, new Rectangle(tr.X + tw, tr.Y, tr.Width - tw, tr.Height), L);
            }
            return new Rectangle(r.X + S(12), r.Y + S(34), r.Width - S(24), r.Height - S(44));
        }

        public static void Card(Graphics g, Rectangle r, string title, string value, string sub, Color accent)
        {
            Box(g, r);
            using (SolidBrush b = new SolidBrush(accent)) g.FillRectangle(b, r.X, r.Y, S(4), r.Height);
            int x = r.X + S(14), w = r.Width - S(22);
            Text(g, title, Fonts.Small, Theme.Muted, new Rectangle(x, r.Y + S(8), w, S(18)), L);
            Text(g, value, Fonts.Big, Theme.Text, new Rectangle(x, r.Y + S(26), w, S(36)), L);
            Text(g, sub, Fonts.Small, Theme.Muted, new Rectangle(x, r.Y + S(64), w, S(18)), L);
        }

        public static int Legend(Graphics g, int x, int y, Color c, string s)
        {
            using (SolidBrush b = new SolidBrush(c)) g.FillRectangle(b, x, y + S(5), S(10), S(10));
            int w = TextW(g, s, Fonts.Small);
            Text(g, s, Fonts.Small, Theme.Muted, new Rectangle(x + S(14), y, w + S(4), S(20)), L);
            return x + S(14) + w + S(14);
        }

        public static void YAxis(Graphics g, Rectangle plot, double min, double max, int lines, string fmt, string suffix, bool right, bool grid, Color label)
        {
            using (Pen p = new Pen(Theme.Grid))
            {
                for (int i = 0; i <= lines; i++)
                {
                    double v = min + (max - min) * i / lines;
                    int y = plot.Bottom - (int)Math.Round(plot.Height * (double)i / lines);
                    if (grid) g.DrawLine(p, plot.X, y, plot.Right, y);
                    string s = v.ToString(fmt, Fmt.Pt) + suffix;
                    Rectangle lr = right ? new Rectangle(plot.Right + S(5), y - S(8), S(52), S(16))
                                         : new Rectangle(plot.X - S(56), y - S(8), S(52), S(16));
                    Text(g, s, Fonts.Small, label, lr, right ? L : R);
                }
            }
        }

        public static void TimeAxis(Graphics g, Rectangle plot, Dataset d)
        {
            TimeSpan span = d.Span;
            string fmt = span.TotalHours <= 26 ? "HH:mm" : (span.TotalDays <= 8 ? "dd/MM HH'h'" : "dd/MM");
            int n = Math.Max(2, plot.Width / S(110));
            using (Pen p = new Pen(Theme.Grid))
            {
                for (int i = 0; i <= n; i++)
                {
                    int x = plot.X + (int)((long)plot.Width * i / n);
                    DateTime t = d.From.AddTicks(span.Ticks * i / n);
                    g.DrawLine(p, x, plot.Bottom, x, plot.Bottom + S(4));
                    Text(g, t.ToString(fmt, Fmt.Pt), Fonts.Small, Theme.Muted, new Rectangle(x - S(50), plot.Bottom + S(5), S(100), S(16)), C);
                }
            }
        }

        public static float Xb(Rectangle plot, int i, int n) { return plot.X + (i + 0.5f) * plot.Width / n; }

        public static float Yv(Rectangle plot, double v, double min, double max)
        {
            double t = (v - min) / (max - min);
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return (float)(plot.Bottom - t * plot.Height);
        }

        public static void Line(Graphics g, Rectangle plot, double[] v, double min, double max, Color c, float width, bool fill)
        {
            int n = v.Length;
            if (n == 0 || max <= min) return;
            List<PointF> seg = new List<PointF>();
            using (Pen p = new Pen(c, S(1) * width))
            using (SolidBrush fb = new SolidBrush(Color.FromArgb(38, c)))
            using (SolidBrush db = new SolidBrush(c))
            {
                p.LineJoin = LineJoin.Round;
                for (int i = 0; i <= n; i++)
                {
                    bool ok = i < n && v[i] >= 0;
                    if (ok) { seg.Add(new PointF(Xb(plot, i, n), Yv(plot, v[i], min, max))); continue; }
                    if (seg.Count == 0) continue;
                    if (seg.Count == 1) g.FillEllipse(db, seg[0].X - S(2), seg[0].Y - S(2), S(4), S(4));
                    else
                    {
                        if (fill)
                        {
                            List<PointF> poly = new List<PointF>(seg);
                            poly.Add(new PointF(seg[seg.Count - 1].X, plot.Bottom));
                            poly.Add(new PointF(seg[0].X, plot.Bottom));
                            g.FillPolygon(fb, poly.ToArray());
                        }
                        g.DrawLines(p, seg.ToArray());
                    }
                    seg.Clear();
                }
            }
        }

        public static void Bars(Graphics g, Rectangle plot, double[] v, double max, Color c)
        {
            int n = v.Length;
            if (n == 0 || max <= 0) return;
            float bw = Math.Max(1f, (float)plot.Width / n - 1f);
            using (SolidBrush b = new SolidBrush(c))
            {
                for (int i = 0; i < n; i++)
                {
                    if (v[i] <= 0) continue;
                    float y = Yv(plot, v[i], 0, max);
                    float x = plot.X + (float)plot.Width * i / n;
                    g.FillRectangle(b, x, y, bw, Math.Max(1f, plot.Bottom - y));
                }
            }
        }

        public static void StackBars(Graphics g, Rectangle plot, double[] a, double[] b, double max, Color ca, Color cb)
        {
            int n = a.Length;
            if (n == 0 || max <= 0) return;
            float bw = Math.Max(1f, (float)plot.Width / n - 1f);
            using (SolidBrush ba = new SolidBrush(ca))
            using (SolidBrush bb = new SolidBrush(cb))
            {
                for (int i = 0; i < n; i++)
                {
                    double va = Math.Max(0, a[i]), vb = Math.Max(0, b[i]);
                    if (va + vb <= 0) continue;
                    float x = plot.X + (float)plot.Width * i / n;
                    float ya = Yv(plot, va, 0, max);
                    float yb = Yv(plot, va + vb, 0, max);
                    if (va > 0) g.FillRectangle(ba, x, ya, bw, Math.Max(1f, plot.Bottom - ya));
                    if (vb > 0) g.FillRectangle(bb, x, yb, bw, Math.Max(1f, ya - yb));
                }
            }
        }

        public static double NiceMax(double v)
        {
            if (v <= 0) return 1;
            double e = Math.Pow(10, Math.Floor(Math.Log10(v)));
            double m = v / e;
            double nm = m <= 1 ? 1 : m <= 2 ? 2 : m <= 2.5 ? 2.5 : m <= 5 ? 5 : 10;
            return nm * e;
        }

        public static double Max(double[] v)
        {
            double m = 0;
            foreach (double x in v) if (x > m) m = x;
            return m;
        }
    }

    // ------------------------------------------------------------------
    // Janela principal
    // ------------------------------------------------------------------
    class ProfileItem
    {
        public string Name, Logs;
        public override string ToString() { return Name; }
    }

    class HostItem
    {
        public HostStats H;
        public override string ToString() { return H.Janela.ToString("000") + "  " + H.Label + (H.Name.Length > 0 ? "  (" + H.Ip + ")" : ""); }
    }

    class ViewerForm : Form
    {
        static readonly string[] PeriodNames = { "Última hora", "Últimas 6 horas", "Últimas 24 horas", "Hoje", "Ontem", "Últimos 7 dias", "Últimos 30 dias", "Tudo", "Personalizado" };
        static readonly string[] WeekDays = { "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb", "Dom" };

        readonly string appDir;
        string profilesDir, singleLogs, startHost;
        readonly Groups groups;
        Dataset data;
        bool loading;
        int loadGen;
        HostStats selHost;
        List<Insight> insights = new List<Insight>();

        ComboBox cmbProfile, cmbPeriod, cmbHost;
        DateTimePicker dtFrom, dtTo;
        Button btnLoad, btnPng, btnCsv;
        CheckBox chkAuto;
        TextBox txtSearch;
        Label lblStatus;
        ProgressBar prog;
        Panel content;
        readonly List<Button> navButtons = new List<Button>();
        readonly List<Control> pages = new List<Control>();
        int page;
        ScrollHost shOverview, shPatterns, shGroups, shHost;
        DataGridView gridHosts, gridEvents, gridOutages, gridGroups, gridHostOut;
        System.Windows.Forms.Timer autoTimer;
        bool fillingHost;

        public ViewerForm(string path, int tab, string host, string period)
        {
            appDir = AppDomain.CurrentDomain.BaseDirectory;
            startHost = host;
            Text = "UltraPingViewer";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }  // o mesmo icone do exe (assets\UltraPingViewer.ico)
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Fonts.Text;
            MinimumSize = new Size(1000, 650);
            Size = new Size(1500, 950);
            WindowState = FormWindowState.Maximized;

            ResolvePath(path);
            groups = Groups.Load(Path.Combine(appDir, "vlans.txt"));

            Panel nav = BuildNav();
            Panel top = BuildTop();
            Panel status = BuildStatus();
            content = new Panel();
            content.Dock = DockStyle.Fill;
            content.BackColor = Theme.Back;
            BuildPages();

            Controls.Add(content);
            Controls.Add(nav);
            Controls.Add(top);
            Controls.Add(status);

            // tempo real: recarrega a cada 10 s (o mesmo ritmo do ping do UltraPing), sem mexer na tela
            autoTimer = new System.Windows.Forms.Timer();
            autoTimer.Interval = 10000;
            autoTimer.Tick += delegate
            {
                if (loading || cmbHost.DroppedDown || cmbPeriod.DroppedDown || cmbProfile.DroppedDown) return;
                Reload(true);
            };
            chkAuto.Checked = true;

            FillProfiles();
            cmbPeriod.SelectedIndex = PeriodIndex(period);
            ShowPage(Math.Max(0, Math.Min(pages.Count - 1, tab)));
            Shown += delegate { Reload(); };
        }

        static int S(int v) { return Theme.S(v); }

        void ResolvePath(string path)
        {
            profilesDir = Path.Combine(appDir, "Profiles");
            if (string.IsNullOrEmpty(path)) return;
            path = path.TrimEnd('\\', '/');
            if (string.Equals(Path.GetFileName(path), "Logs", StringComparison.OrdinalIgnoreCase) && Directory.Exists(path)) singleLogs = path;
            else if (Directory.Exists(Path.Combine(path, "Logs"))) singleLogs = Path.Combine(path, "Logs");
            else if (Directory.Exists(Path.Combine(path, "Profiles"))) profilesDir = Path.Combine(path, "Profiles");
            else if (Directory.Exists(path)) profilesDir = path;
        }

        static int PeriodIndex(string p)
        {
            if (p == null) return 2;
            switch (p.ToLowerInvariant())
            {
                case "1h": return 0;
                case "6h": return 1;
                case "24h": return 2;
                case "hoje": return 3;
                case "ontem": return 4;
                case "7d": return 5;
                case "30d": return 6;
                case "tudo": return 7;
            }
            return 2;
        }

        // ---------------- construcao ----------------

        Button FlatButton(string text, EventHandler click)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Theme.Border;
            b.BackColor = Theme.Card;
            b.ForeColor = Theme.Text;
            b.AutoSize = true;
            b.Height = S(28);
            b.Margin = new Padding(S(4), S(6), S(4), S(4));
            b.Click += click;
            return b;
        }

        Label FlowLabel(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.ForeColor = Theme.Muted;
            l.Margin = new Padding(S(10), S(12), S(2), S(4));
            return l;
        }

        static ComboBox DarkCombo(int width)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.FlatStyle = FlatStyle.Flat;
            c.BackColor = Theme.Card;
            c.ForeColor = Theme.Text;
            c.Width = Theme.S(width);
            c.Margin = new Padding(Theme.S(4), Theme.S(8), Theme.S(4), Theme.S(4));
            return c;
        }

        Panel BuildTop()
        {
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.Dock = DockStyle.Top;
            f.Height = S(44);
            f.BackColor = Theme.Card;
            f.WrapContents = false;
            f.Padding = new Padding(S(6), 0, 0, 0);

            Label title = new Label();
            title.Text = "UltraPingViewer";
            title.Font = Fonts.H;
            title.ForeColor = Theme.Text;
            title.AutoSize = true;
            title.Margin = new Padding(S(4), S(11), S(14), S(4));
            f.Controls.Add(title);

            f.Controls.Add(FlowLabel("Perfil"));
            cmbProfile = DarkCombo(150);
            cmbProfile.SelectedIndexChanged += delegate { Reload(); };
            f.Controls.Add(cmbProfile);

            f.Controls.Add(FlowLabel("Período"));
            cmbPeriod = DarkCombo(150);
            cmbPeriod.Items.AddRange(PeriodNames);
            cmbPeriod.SelectedIndexChanged += delegate
            {
                bool custom = cmbPeriod.SelectedIndex == PeriodNames.Length - 1;
                dtFrom.Visible = custom;
                dtTo.Visible = custom;
                if (cmbProfile.Items.Count > 0) Reload();
            };
            f.Controls.Add(cmbPeriod);

            dtFrom = new DateTimePicker();
            dtFrom.Format = DateTimePickerFormat.Short;
            dtFrom.Width = S(110);
            dtFrom.Margin = new Padding(S(4), S(9), S(2), S(4));
            dtFrom.Value = DateTime.Today.AddDays(-7);
            dtFrom.Visible = false;
            f.Controls.Add(dtFrom);
            dtTo = new DateTimePicker();
            dtTo.Format = DateTimePickerFormat.Short;
            dtTo.Width = S(110);
            dtTo.Margin = new Padding(S(2), S(9), S(4), S(4));
            dtTo.Value = DateTime.Today;
            dtTo.Visible = false;
            f.Controls.Add(dtTo);

            btnLoad = FlatButton("Atualizar", delegate { Reload(); });
            f.Controls.Add(btnLoad);
            chkAuto = new CheckBox();
            chkAuto.Text = "Tempo real (10 s)";
            chkAuto.AutoSize = true;
            chkAuto.ForeColor = Theme.Muted;
            chkAuto.Margin = new Padding(S(8), S(12), S(8), S(4));
            chkAuto.CheckedChanged += delegate { autoTimer.Enabled = chkAuto.Checked; };
            f.Controls.Add(chkAuto);
            btnPng = FlatButton("Exportar imagem", delegate { ExportPng(); });
            f.Controls.Add(btnPng);
            btnCsv = FlatButton("Exportar CSV", delegate { ExportCsv(); });
            f.Controls.Add(btnCsv);
            return f;
        }

        Panel BuildNav()
        {
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.Dock = DockStyle.Top;
            f.Height = S(40);
            f.BackColor = Theme.Back;
            f.WrapContents = false;
            f.Padding = new Padding(S(8), S(4), 0, 0);
            string[] names = { "Visão geral", "Hosts", "Incidentes", "Sub-redes / VLANs", "Padrões", "Host" };
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                Button b = new Button();
                b.Text = names[i];
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.ForeColor = Theme.Muted;
                b.BackColor = Theme.Back;
                b.AutoSize = true;
                b.Height = S(30);
                b.Padding = new Padding(S(8), 0, S(8), 0);
                b.Margin = new Padding(0, 0, S(2), 0);
                b.Click += delegate { ShowPage(idx); };
                navButtons.Add(b);
                f.Controls.Add(b);
            }
            return f;
        }

        Panel BuildStatus()
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Bottom;
            p.Height = S(26);
            p.BackColor = Theme.Card;
            prog = new ProgressBar();
            prog.Dock = DockStyle.Right;
            prog.Width = S(220);
            prog.Visible = false;
            lblStatus = new Label();
            lblStatus.Dock = DockStyle.Fill;
            lblStatus.ForeColor = Theme.Muted;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.Padding = new Padding(S(8), 0, 0, 0);
            p.Controls.Add(lblStatus);
            p.Controls.Add(prog);
            return p;
        }

        Label SectionLabel(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.Dock = DockStyle.Top;
            l.Height = S(30);
            l.Font = Fonts.H;
            l.ForeColor = Theme.Text;
            l.BackColor = Theme.Back;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Padding = new Padding(S(10), 0, 0, 0);
            return l;
        }

        static void StyleGrid(DataGridView g)
        {
            g.Dock = DockStyle.Fill;
            g.BackgroundColor = Theme.Back;
            g.BorderStyle = BorderStyle.None;
            g.GridColor = Theme.Grid;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersDefaultCellStyle.BackColor = Theme.Card;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Card;
            g.ColumnHeadersDefaultCellStyle.Font = Fonts.H;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            g.DefaultCellStyle.BackColor = Theme.Back;
            g.DefaultCellStyle.ForeColor = Theme.Text;
            g.DefaultCellStyle.SelectionBackColor = Theme.Select;
            g.DefaultCellStyle.SelectionForeColor = Color.White;
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(26, 27, 32);
            g.RowHeadersVisible = false;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AllowUserToResizeRows = false;
            g.ReadOnly = true;
            g.MultiSelect = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            g.RowTemplate.Height = Theme.S(24);
            try
            {
                typeof(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(g, true, null);
            }
            catch { }
        }

        static void AddCol(DataGridView g, string header, Type t, string tag, float weight)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.HeaderText = header;
            c.ValueType = t;
            c.Tag = tag;
            c.FillWeight = weight;
            c.SortMode = DataGridViewColumnSortMode.Automatic;
            if (t != typeof(string)) c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            g.Columns.Add(c);
        }

        readonly List<DataGridView> topPending = new List<DataGridView>();

        DataGridView NewGrid()
        {
            DataGridView g = new DataGridView();
            StyleGrid(g);
            g.CellFormatting += GridFormat;
            g.VisibleChanged += delegate { if (g.Visible && topPending.Contains(g)) ScrollTop(g); };
            return g;
        }

        // depois de recarregar, a lista volta ao topo (se a aba estiver escondida, faz isso quando ela aparecer)
        void ScrollTop(DataGridView g)
        {
            if (!g.Visible) { if (!topPending.Contains(g)) topPending.Add(g); return; }
            topPending.Remove(g);
            foreach (DataGridViewRow r in g.Rows)
            {
                if (!r.Visible) continue;
                try { g.FirstDisplayedScrollingRowIndex = r.Index; } catch { }
                break;
            }
        }

        void BuildPages()
        {
            // 0 visao geral
            shOverview = new ScrollHost(PaintOverview);
            shOverview.Dock = DockStyle.Fill;
            AddPage(shOverview);

            // 1 hosts
            Panel ph = new Panel();
            ph.Dock = DockStyle.Fill;
            gridHosts = NewGrid();
            AddCol(gridHosts, "Nº", typeof(int), "", 30);
            AddCol(gridHosts, "Host", typeof(string), "", 110);
            AddCol(gridHosts, "IP", typeof(string), "", 75);
            AddCol(gridHosts, "Sub-rede / VLAN", typeof(string), "", 85);
            AddCol(gridHosts, "Disponib.", typeof(double), "avail", 55);
            AddCol(gridHosts, "Quedas", typeof(int), "n", 40);
            AddCol(gridHosts, "Tempo fora", typeof(TimeSpan), "", 55);
            AddCol(gridHosts, "Maior queda", typeof(TimeSpan), "", 55);
            AddCol(gridHosts, "MTTR", typeof(TimeSpan), "", 50);
            AddCol(gridHosts, "Perda", typeof(double), "pct", 45);
            AddCol(gridHosts, "Oscil.", typeof(long), "n", 40);
            AddCol(gridHosts, "Lat. média", typeof(double), "ms", 50);
            AddCol(gridHosts, "p95", typeof(int), "ms", 42);
            AddCol(gridHosts, "p99", typeof(int), "ms", 42);
            AddCol(gridHosts, "Máx", typeof(int), "ms", 42);
            AddCol(gridHosts, "Jitter", typeof(double), "ms", 42);
            AddCol(gridHosts, "Amostras", typeof(long), "n", 50);
            AddCol(gridHosts, "Situação", typeof(string), "sit", 70);
            gridHosts.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0) OpenHost(gridHosts.Rows[e.RowIndex].Tag as HostStats);
            };
            Panel search = new Panel();
            search.Dock = DockStyle.Top;
            search.Height = S(38);
            search.BackColor = Theme.Back;
            Label sl = new Label();
            sl.Text = "Buscar:";
            sl.ForeColor = Theme.Muted;
            sl.AutoSize = true;
            sl.Location = new Point(S(10), S(11));
            search.Controls.Add(sl);
            txtSearch = new TextBox();
            txtSearch.BackColor = Theme.Card;
            txtSearch.ForeColor = Theme.Text;
            txtSearch.BorderStyle = BorderStyle.FixedSingle;
            txtSearch.SetBounds(S(64), S(8), S(280), S(24));
            txtSearch.TextChanged += delegate { FilterHosts(); };
            search.Controls.Add(txtSearch);
            Label hint = new Label();
            hint.Text = "Clique no título da coluna para ordenar. Duplo clique abre o detalhe do host.";
            hint.ForeColor = Theme.Muted;
            hint.AutoSize = true;
            hint.Location = new Point(S(360), S(11));
            search.Controls.Add(hint);
            ph.Controls.Add(gridHosts);
            ph.Controls.Add(search);
            AddPage(ph);

            // 2 incidentes
            SplitContainer si = DarkSplit();
            gridEvents = NewGrid();
            AddCol(gridEvents, "Início", typeof(DateTime), "", 60);
            AddCol(gridEvents, "Fim", typeof(DateTime), "", 60);
            AddCol(gridEvents, "Duração", typeof(TimeSpan), "", 45);
            AddCol(gridEvents, "Hosts", typeof(int), "n", 30);
            AddCol(gridEvents, "Hosts afetados", typeof(string), "", 300);
            si.Panel1.Controls.Add(gridEvents);
            si.Panel1.Controls.Add(SectionLabel("Quedas em massa: 5 ou mais hosts caindo em até 90 s (falha de core, uplink, firewall ou energia)"));
            gridOutages = NewGrid();
            AddCol(gridOutages, "Host", typeof(string), "", 110);
            AddCol(gridOutages, "IP", typeof(string), "", 70);
            AddCol(gridOutages, "Sub-rede / VLAN", typeof(string), "", 80);
            AddCol(gridOutages, "Início", typeof(DateTime), "", 60);
            AddCol(gridOutages, "Fim", typeof(DateTime), "", 60);
            AddCol(gridOutages, "Duração", typeof(TimeSpan), "", 45);
            AddCol(gridOutages, "Falhas", typeof(int), "n", 35);
            AddCol(gridOutages, "Situação", typeof(string), "sit", 55);
            gridOutages.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0) OpenHost(gridOutages.Rows[e.RowIndex].Tag as HostStats);
            };
            si.Panel2.Controls.Add(gridOutages);
            si.Panel2.Controls.Add(SectionLabel("Todas as quedas confirmadas (mais recentes primeiro). Duplo clique abre o host."));
            AddPage(si);

            // 3 sub-redes
            SplitContainer sg = DarkSplit();
            shGroups = new ScrollHost(PaintGroups);
            shGroups.Dock = DockStyle.Fill;
            sg.Panel1.Controls.Add(shGroups);
            gridGroups = NewGrid();
            AddCol(gridGroups, "Sub-rede / VLAN", typeof(string), "", 130);
            AddCol(gridGroups, "Hosts", typeof(int), "n", 35);
            AddCol(gridGroups, "Disponib.", typeof(double), "avail", 55);
            AddCol(gridGroups, "Hosts c/ queda", typeof(int), "n", 50);
            AddCol(gridGroups, "Fora agora", typeof(int), "n", 45);
            AddCol(gridGroups, "Quedas", typeof(int), "n", 40);
            AddCol(gridGroups, "Tempo fora", typeof(TimeSpan), "", 55);
            AddCol(gridGroups, "Perda", typeof(double), "pct", 45);
            AddCol(gridGroups, "Lat. média", typeof(double), "ms", 50);
            AddCol(gridGroups, "p95", typeof(int), "ms", 40);
            AddCol(gridGroups, "Jitter", typeof(double), "ms", 40);
            sg.Panel2.Controls.Add(gridGroups);
            AddPage(sg);

            // 4 padroes
            shPatterns = new ScrollHost(PaintPatterns);
            shPatterns.Dock = DockStyle.Fill;
            AddPage(shPatterns);

            // 5 host
            Panel phd = new Panel();
            phd.Dock = DockStyle.Fill;
            SplitContainer sh = DarkSplit();
            shHost = new ScrollHost(PaintHost);
            shHost.Dock = DockStyle.Fill;
            sh.Panel1.Controls.Add(shHost);
            gridHostOut = NewGrid();
            AddCol(gridHostOut, "Início", typeof(DateTime), "", 60);
            AddCol(gridHostOut, "Fim", typeof(DateTime), "", 60);
            AddCol(gridHostOut, "Duração", typeof(TimeSpan), "", 45);
            AddCol(gridHostOut, "Falhas", typeof(int), "n", 35);
            AddCol(gridHostOut, "Situação", typeof(string), "sit", 60);
            sh.Panel2.Controls.Add(gridHostOut);
            sh.Panel2.Controls.Add(SectionLabel("Quedas deste host"));
            Panel hostTop = new Panel();
            hostTop.Dock = DockStyle.Top;
            hostTop.Height = S(40);
            hostTop.BackColor = Theme.Back;
            Label hl = new Label();
            hl.Text = "Host:";
            hl.ForeColor = Theme.Muted;
            hl.AutoSize = true;
            hl.Location = new Point(S(10), S(12));
            hostTop.Controls.Add(hl);
            cmbHost = DarkCombo(420);
            cmbHost.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbHost.Location = new Point(S(54), S(8));
            cmbHost.SelectedIndexChanged += delegate
            {
                if (fillingHost) return;
                HostItem it = cmbHost.SelectedItem as HostItem;
                if (it != null) { selHost = it.H; FillHostOutages(); shHost.Relayout(); }
            };
            hostTop.Controls.Add(cmbHost);
            phd.Controls.Add(sh);
            phd.Controls.Add(hostTop);
            AddPage(phd);
        }

        SplitContainer DarkSplit()
        {
            SplitContainer s = new SplitContainer();
            s.Dock = DockStyle.Fill;
            s.Orientation = Orientation.Horizontal;
            s.BackColor = Theme.Border;
            s.Panel1.BackColor = Theme.Back;
            s.Panel2.BackColor = Theme.Back;
            s.SplitterWidth = S(5);
            s.HandleCreated += delegate
            {
                try { s.SplitterDistance = Math.Max(S(120), s.Height * 55 / 100); } catch { }
            };
            return s;
        }

        void AddPage(Control c)
        {
            c.Visible = false;
            pages.Add(c);
            content.Controls.Add(c);
        }

        void ShowPage(int i)
        {
            page = i;
            for (int k = 0; k < pages.Count; k++)
            {
                pages[k].Visible = k == i;
                navButtons[k].ForeColor = k == i ? Theme.Text : Theme.Muted;
                navButtons[k].BackColor = k == i ? Theme.Card : Theme.Back;
            }
            ScrollHost sh = CurrentScroll();
            if (sh != null) sh.Relayout();
            if (i == 5 && shHost != null) shHost.Relayout();
            if (i == 3 && shGroups != null) shGroups.Relayout();
        }

        ScrollHost CurrentScroll()
        {
            switch (page)
            {
                case 0: return shOverview;
                case 3: return shGroups;
                case 4: return shPatterns;
                case 5: return shHost;
            }
            return null;
        }

        // ---------------- carga ----------------

        void FillProfiles()
        {
            cmbProfile.Items.Clear();
            if (singleLogs != null)
            {
                ProfileItem pi = new ProfileItem();
                pi.Logs = singleLogs;
                pi.Name = Path.GetFileName(Path.GetDirectoryName(singleLogs));
                cmbProfile.Items.Add(pi);
            }
            else if (Directory.Exists(profilesDir))
            {
                string[] dirs = Directory.GetDirectories(profilesDir);
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                foreach (string d in dirs)
                {
                    string logs = Path.Combine(d, "Logs");
                    if (!Directory.Exists(logs)) continue;
                    ProfileItem pi = new ProfileItem();
                    pi.Name = Path.GetFileName(d);
                    pi.Logs = logs;
                    cmbProfile.Items.Add(pi);
                }
            }
            int sel = 0;
            for (int i = 0; i < cmbProfile.Items.Count; i++)
                if (((ProfileItem)cmbProfile.Items[i]).Name == "Default") sel = i;
            loading = true;   // nao recarrega ao selecionar aqui
            if (cmbProfile.Items.Count > 0) cmbProfile.SelectedIndex = sel;
            loading = false;
        }

        void PeriodRange(string logs, out DateTime from, out DateTime to)
        {
            DateTime now = DateTime.Now;
            to = now.AddSeconds(1);
            switch (cmbPeriod.SelectedIndex)
            {
                case 0: from = now.AddHours(-1); break;
                case 1: from = now.AddHours(-6); break;
                case 2: from = now.AddHours(-24); break;
                case 3: from = now.Date; break;
                case 4: from = now.Date.AddDays(-1); to = now.Date; break;
                case 5: from = now.AddDays(-7); break;
                case 6: from = now.AddDays(-30); break;
                case 7:
                    List<DateTime> days = Loader.LogDays(logs);
                    from = days.Count > 0 ? days[0] : now.Date;
                    break;
                default:
                    from = dtFrom.Value.Date;
                    to = dtTo.Value.Date.AddDays(1);
                    if (to > now) to = now.AddSeconds(1);
                    break;
            }
        }

        void Reload() { Reload(false); }

        // auto = recarga do tempo real: silenciosa e sem mexer na posicao das listas
        void Reload(bool auto)
        {
            if (!IsHandleCreated || cmbProfile == null || cmbPeriod == null || cmbPeriod.SelectedIndex < 0) return;   // a primeira carga e no Shown
            ProfileItem pi = cmbProfile.SelectedItem as ProfileItem;
            if (pi == null)
            {
                data = null;
                insights = Analysis.Build(null);
                lblStatus.Text = "Nenhuma pasta de logs encontrada em " + profilesDir + ". Abra o UltraPingViewer na pasta do UltraPing (ou passe a pasta Profiles como argumento).";
                RefreshAll();
                return;
            }
            DateTime from, to;
            PeriodRange(pi.Logs, out from, out to);
            int gen = ++loadGen;
            loading = true;
            if (!auto)
            {
                btnLoad.Enabled = false;
                prog.Visible = true;
                prog.Value = 0;
                lblStatus.Text = "Lendo logs de " + pi.Logs + " ...";
            }
            string logs = pi.Logs, name = pi.Name;
            Groups gr = groups;
            Stopwatch sw = Stopwatch.StartNew();
            Thread t = new Thread(delegate()
            {
                Dataset d = null;
                string err = null;
                try
                {
                    d = Loader.Load(logs, from, to, gr,
                        delegate(int done, int total, string msg)
                        {
                            try
                            {
                                BeginInvoke(new MethodInvoker(delegate
                                {
                                    if (auto || gen != loadGen || total <= 0) return;
                                    prog.Maximum = total;
                                    prog.Value = Math.Min(done, total);
                                    lblStatus.Text = "Lendo " + msg + "  (" + done + " de " + total + " arquivos)...";
                                }));
                            }
                            catch { }
                        },
                        delegate { return gen != loadGen; });
                }
                catch (Exception ex) { err = ex.Message; }
                try { BeginInvoke(new MethodInvoker(delegate { LoadDone(gen, d, err, name, sw.Elapsed, auto); })); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        void LoadDone(int gen, Dataset d, string err, string profile, TimeSpan took, bool auto)
        {
            if (gen != loadGen) return;
            loading = false;
            btnLoad.Enabled = true;
            prog.Visible = false;
            if (err != null) { lblStatus.Text = "Erro ao ler os logs: " + err; return; }
            if (d == null) return;
            d.Profile = profile;
            data = d;
            insights = Analysis.Build(d);
            string ip = selHost != null ? selHost.Ip : startHost;
            startHost = null;
            selHost = null;
            if (ip != null) foreach (HostStats h in d.Hosts) if (h.Ip == ip) selHost = h;
            if (selHost == null && d.Hosts.Count > 0) selHost = WorstHost(d);
            FillGrids(auto);
            RefreshAll();
            int down = 0;
            foreach (HostStats h in d.Hosts) if (h.DownNow) down++;
            lblStatus.Text = "Perfil " + profile + "  ·  " + PeriodNames[cmbPeriod.SelectedIndex] + "  ·  " + d.Hosts.Count + " hosts  ·  " +
                Fmt.N(d.Rows) + " amostras em " + d.Files + " arquivos  ·  intervalo dos gráficos: " + Fmt.Bucket(d.Bucket) +
                (down > 0 ? "  ·  " + down + " FORA AGORA" : "") + "  ·  lido em " + took.TotalSeconds.ToString("0.0", Fmt.Pt) + " s" +
                (d.BadRows > 0 ? "  ·  " + d.BadRows + " linhas ignoradas" : "") +
                "  ·  atualizado às " + DateTime.Now.ToString("HH:mm:ss") + (chkAuto.Checked ? " (tempo real)" : "");
            Text = "UltraPingViewer - " + profile + (down > 0 ? "   [" + down + " FORA]" : "");
        }

        static HostStats WorstHost(Dataset d)
        {
            HostStats w = d.Hosts[0];
            foreach (HostStats h in d.Hosts)
                if (h.Downtime > w.Downtime || (h.Downtime == w.Downtime && h.Osc > w.Osc)) w = h;
            return w;
        }

        void RefreshAll()
        {
            shOverview.Relayout();
            shPatterns.Relayout();
            shGroups.Relayout();
            shHost.Relayout();
        }

        // estado de uma lista (rolagem, ordenacao e linha escolhida) para sobreviver a recarga do tempo real
        class GridState
        {
            public int First = -1;
            public DataGridViewColumn Col;
            public SortOrder Order;
            public string Sel;
        }

        static string RowKey(DataGridViewRow r)
        {
            HostStats h = r.Tag as HostStats;
            return h != null ? "ip:" + h.Ip : "idx:" + r.Index;
        }

        static GridState SaveGrid(DataGridView g)
        {
            GridState s = new GridState();
            try { s.First = g.FirstDisplayedScrollingRowIndex; } catch { }
            s.Col = g.SortedColumn;
            s.Order = g.SortOrder;
            if (g.CurrentRow != null) s.Sel = RowKey(g.CurrentRow);
            return s;
        }

        static void RestoreGrid(DataGridView g, GridState s)
        {
            if (s.Col != null && s.Order != SortOrder.None)
                g.Sort(s.Col, s.Order == SortOrder.Ascending ? ListSortDirection.Ascending : ListSortDirection.Descending);
            g.ClearSelection();
            if (s.Sel != null)
                foreach (DataGridViewRow r in g.Rows)
                    if (r.Visible && RowKey(r) == s.Sel) { r.Selected = true; break; }
            if (s.First >= 0 && g.Rows.Count > 0)
            {
                int i = Math.Min(s.First, g.Rows.Count - 1);
                while (i > 0 && !g.Rows[i].Visible) i--;
                try { g.FirstDisplayedScrollingRowIndex = i; } catch { }
            }
        }

        void FillGrids() { FillGrids(false); }

        void FillGrids(bool keep)
        {
            Dataset d = data;
            GridState sHosts = SaveGrid(gridHosts), sEvents = SaveGrid(gridEvents), sOut = SaveGrid(gridOutages),
                      sGroups = SaveGrid(gridGroups), sHostOut = SaveGrid(gridHostOut);
            gridHosts.SuspendLayout();
            gridHosts.Rows.Clear();
            foreach (HostStats h in d.Hosts)
            {
                int r = gridHosts.Rows.Add(h.Janela, h.Label, h.Ip, h.Group, h.Avail < 0 ? -1.0 : h.Avail * 100.0, h.Outages.Count,
                    h.Downtime, h.Longest, h.Mttr, h.LossPct, h.Osc, h.AvgMs, h.P95, h.P99, h.MaxMs, h.Jitter, h.Total, h.Situation);
                gridHosts.Rows[r].Tag = h;
            }
            gridHosts.ResumeLayout();
            FilterHosts(keep);

            gridEvents.Rows.Clear();
            foreach (NetEvent e in d.Events)
            {
                List<HostStats> hs = new List<HostStats>();
                foreach (Outage o in e.Outages) hs.Add(o.Host);
                gridEvents.Rows.Add(e.Start, e.End, e.End - e.Start, e.Outages.Count, Fmt.Names(hs, 15));
            }

            List<Outage> outs = new List<Outage>(d.Outages);
            outs.Sort(delegate(Outage a, Outage b) { return b.Start.CompareTo(a.Start); });
            gridOutages.Rows.Clear();
            int max = Math.Min(outs.Count, 20000);
            List<DataGridViewRow> rows = new List<DataGridViewRow>(max);
            for (int i = 0; i < max; i++)
            {
                Outage o = outs[i];
                DataGridViewRow row = new DataGridViewRow();
                row.CreateCells(gridOutages, o.Host.Label, o.Host.Ip, o.Host.Group, o.Start, o.End, o.Duration, o.Fails, o.Ongoing ? "FORA (em andamento)" : "resolvida");
                row.Tag = o.Host;
                rows.Add(row);
            }
            gridOutages.Rows.AddRange(rows.ToArray());

            gridGroups.Rows.Clear();
            foreach (GroupStats g in d.Groups)
                gridGroups.Rows.Add(g.Name, g.Hosts.Count, g.Avail < 0 ? -1.0 : g.Avail * 100.0, g.HostsWithOutage, g.DownNow, g.Outages,
                    g.Downtime, g.LossPct, g.AvgMs, g.P95, g.Jitter);

            fillingHost = true;
            cmbHost.Items.Clear();
            foreach (HostStats h in d.Hosts)
            {
                HostItem it = new HostItem();
                it.H = h;
                cmbHost.Items.Add(it);
                if (h == selHost) cmbHost.SelectedIndex = cmbHost.Items.Count - 1;
            }
            fillingHost = false;
            FillHostOutages(keep);
            if (keep)
            {
                RestoreGrid(gridHosts, sHosts);
                RestoreGrid(gridEvents, sEvents);
                RestoreGrid(gridOutages, sOut);
                RestoreGrid(gridGroups, sGroups);
                RestoreGrid(gridHostOut, sHostOut);
            }
            else
            {
                ScrollTop(gridEvents);
                ScrollTop(gridOutages);
                ScrollTop(gridGroups);
            }
        }

        void FilterHosts() { FilterHosts(false); }

        void FilterHosts(bool keep)
        {
            string q = txtSearch.Text.Trim();
            gridHosts.CurrentCell = null;
            foreach (DataGridViewRow r in gridHosts.Rows)
            {
                HostStats h = r.Tag as HostStats;
                bool ok = q.Length == 0 || h == null ||
                          h.Label.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                          h.Ip.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                          h.Group.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                r.Visible = ok;
            }
            if (!keep) ScrollTop(gridHosts);
        }

        void FillHostOutages() { FillHostOutages(false); }

        void FillHostOutages(bool keep)
        {
            gridHostOut.Rows.Clear();
            if (selHost == null) return;
            for (int i = selHost.Outages.Count - 1; i >= 0; i--)
            {
                Outage o = selHost.Outages[i];
                gridHostOut.Rows.Add(o.Start, o.End, o.Duration, o.Fails, o.Ongoing ? "FORA (em andamento)" : "resolvida");
            }
            if (!keep) ScrollTop(gridHostOut);
        }

        public void OpenHost(HostStats h)
        {
            if (h == null) return;
            selHost = h;
            fillingHost = true;
            for (int i = 0; i < cmbHost.Items.Count; i++)
                if (((HostItem)cmbHost.Items[i]).H == h) cmbHost.SelectedIndex = i;
            fillingHost = false;
            FillHostOutages();
            ShowPage(5);
        }

        void GridFormat(object sender, DataGridViewCellFormattingEventArgs e)
        {
            DataGridView g = (DataGridView)sender;
            if (e.RowIndex < 0 || e.Value == null) return;
            string tag = g.Columns[e.ColumnIndex].Tag as string;
            object v = e.Value;
            if (v is TimeSpan)
            {
                TimeSpan t = (TimeSpan)v;
                e.Value = t.Ticks <= 0 ? "—" : Fmt.Dur(t);
                e.FormattingApplied = true;
                return;
            }
            if (v is DateTime)
            {
                e.Value = Fmt.When((DateTime)v);
                e.FormattingApplied = true;
                return;
            }
            string s = v as string;
            if (s != null)
            {
                if (tag == "sit")
                {
                    if (s.StartsWith("FORA") || s == "FALHA") e.CellStyle.ForeColor = Theme.Bad;
                    else if (s == "OSCILOU") e.CellStyle.ForeColor = Theme.Warn;
                    else if (s == "OK" || s == "resolvida") e.CellStyle.ForeColor = Theme.Good;
                }
                return;
            }
            double num;
            if (v is double) num = (double)v;
            else if (v is int) num = (int)v;
            else if (v is long) num = (long)v;
            else return;
            if (num < 0) { e.Value = "—"; e.FormattingApplied = true; return; }
            if (tag == "avail")
            {
                e.Value = Fmt.Pct(num / 100.0);
                e.CellStyle.ForeColor = Theme.Avail(num / 100.0);
            }
            else if (tag == "pct")
            {
                e.Value = Fmt.Pct100(num);
                if (num >= 1) e.CellStyle.ForeColor = Theme.Warn;
            }
            else if (tag == "ms")
            {
                e.Value = Fmt.Ms(num);
                if (num >= 100) e.CellStyle.ForeColor = Theme.Warn;
            }
            else if (tag == "n")
            {
                e.Value = Fmt.N((long)num);
            }
            else return;
            e.FormattingApplied = true;
        }

        // ---------------- tooltips ----------------

        string BucketTip(int b)
        {
            Dataset d = data;
            DateTime t0 = d.BucketStart(b), t1 = t0 + d.Bucket;
            long tot = d.NOk[b] + d.NOsc[b] + d.NFail[b];
            StringBuilder sb = new StringBuilder();
            sb.Append(t0.ToString("dd/MM HH:mm", Fmt.Pt)).Append(" – ").Append(t1.ToString("HH:mm", Fmt.Pt)).Append("\n");
            if (tot == 0) { sb.Append("sem amostras"); return sb.ToString(); }
            sb.Append("Disponibilidade: ").Append(Fmt.Pct100(d.NetAvail[b])).Append("\n");
            sb.Append("Hosts com falha: ").Append(d.NetDown[b]).Append("\n");
            sb.Append("Latência média: ").Append(Fmt.Ms(d.NetLat[b])).Append("   p95: ").Append(Fmt.Ms(d.NetP95[b])).Append("\n");
            sb.Append("Falhas: ").Append(Fmt.N(d.NFail[b])).Append("   Oscilações: ").Append(Fmt.N(d.NOsc[b])).Append("\n");
            sb.Append("Amostras: ").Append(Fmt.N(tot));
            return sb.ToString();
        }

        static string HostTip(HostStats h)
        {
            return h.Label + "  (" + h.Ip + ")\n" + h.Group +
                "\nDisponibilidade: " + Fmt.Pct(h.Avail) + "   Quedas: " + h.Outages.Count + "   Tempo fora: " + Fmt.Dur(h.Downtime) +
                "\nLatência média: " + Fmt.Ms(h.AvgMs) + "   p95: " + Fmt.Ms(h.P95) + "   Jitter: " + Fmt.Ms(h.Jitter) +
                "\nOscilações: " + Fmt.N(h.Osc) + "\n(clique para abrir o detalhe)";
        }

        void AddTimeHit(Canvas hc, Rectangle plot)
        {
            if (hc == null) return;
            Dataset d = data;
            Rectangle pr = plot;
            hc.AddHit(pr, delegate(Point p)
            {
                int b = (int)((long)(p.X - pr.X) * d.NB / Math.Max(1, pr.Width));
                if (b < 0 || b >= d.NB) return null;
                return BucketTip(b);
            }, null);
        }

        // ---------------- paginas desenhadas ----------------

        int NoData(Graphics g, int W)
        {
            string msg = loading ? "Carregando logs..." : "Sem dados para mostrar. Escolha o perfil e o período e clique em Atualizar.";
            Draw.Text(g, msg, Fonts.H, Theme.Muted, new Rectangle(S(20), S(20), W - S(40), S(30)), Draw.L);
            int y = S(60);
            foreach (Insight i in insights)
            {
                Draw.Text(g, i.T, Fonts.Text, Theme.Muted, new Rectangle(S(20), y, W - S(40), S(22)), Draw.L);
                y += S(22);
            }
            return y + S(20);
        }

        int PaintOverview(Graphics g, int W, Canvas hc)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Dataset d = data;
            if (d == null || d.Total == 0) return NoData(g, W);
            int pad = S(16), gap = S(12);
            int x0 = pad, cw = W - 2 * pad, y = pad;

            Draw.Text(g, "Visão geral da rede", Fonts.Title, Theme.Text, new Rectangle(x0, y, cw, S(30)), Draw.L);
            y += S(30);
            Draw.Text(g, "Perfil " + d.Profile + "  ·  " + PeriodNames[cmbPeriod.SelectedIndex] + "  ·  dados de " + Fmt.When(d.DataFirst) + " a " + Fmt.When(d.DataLast) +
                "  ·  " + Fmt.N(d.Rows) + " amostras", Fonts.Small, Theme.Muted, new Rectangle(x0, y, cw, S(18)), Draw.L);
            y += S(28);

            // ---- cartoes ----
            int downNow = 0, hostsOut = 0, unstable = 0;
            HostStats longest = null;
            long closedTicks = 0;
            int closed = 0;
            foreach (HostStats h in d.Hosts)
            {
                if (h.DownNow) downNow++;
                if (h.Outages.Count > 0) hostsOut++;
                if (h.Osc >= 3 && h.OscPct > 0.5) unstable++;
                if (longest == null || h.Longest > longest.Longest) longest = h;
                foreach (Outage o in h.Outages) if (!o.Ongoing) { closedTicks += o.Duration.Ticks; closed++; }
            }
            TimeSpan mttr = closed > 0 ? TimeSpan.FromTicks(closedTicks / closed) : TimeSpan.Zero;
            string[] ct = { "Disponibilidade da rede", "Hosts monitorados", "Quedas confirmadas", "Tempo total fora", "MTTR (recuperação média)", "Latência média", "Jitter médio", "Oscilações" };
            string[] cv = {
                Fmt.Pct(d.Avail), d.Hosts.Count.ToString(), d.Outages.Count.ToString(), Fmt.Dur(d.Downtime),
                closed > 0 ? Fmt.Dur(mttr) : "—", Fmt.Ms(d.AvgMs), Fmt.Ms(d.JitterAvg), Fmt.N(d.Osc) };
            string[] cs = {
                "meta 99,9%  ·  " + Fmt.N(d.Fail) + " ciclos em falha",
                downNow > 0 ? downNow + " fora agora" : "nenhum fora agora",
                Fmt.Pl(hostsOut, "host", "hosts") + "  ·  " + d.Events.Count + " em massa",
                longest != null && longest.Longest.Ticks > 0 ? "maior: " + Fmt.Dur(longest.Longest) + " (" + longest.Label + ")" : "sem quedas",
                Fmt.Pl(closed, "queda resolvida", "quedas resolvidas"),
                "p95 " + Fmt.Ms(d.P95) + "  ·  p99 " + Fmt.Ms(d.P99),
                "variação média entre pings",
                Fmt.Pl(unstable, "host instável", "hosts instáveis") };
            Color[] cc = {
                Theme.Avail(d.Avail), Theme.Blue, d.Outages.Count > 0 ? Theme.Warn : Theme.Good, d.Downtime.Ticks > 0 ? Theme.Bad : Theme.Good,
                Theme.Purple, d.P95 > 100 ? Theme.Warn : Theme.Blue, d.JitterAvg > 30 ? Theme.Warn : Theme.Blue, d.Osc > 0 ? Theme.Warn : Theme.Good };
            if (downNow > 0) cc[1] = Theme.Bad;
            int cols = Math.Max(2, Math.Min(8, (cw + gap) / (S(200) + gap)));
            int cardW = (cw - gap * (cols - 1)) / cols, cardH = S(90);
            for (int i = 0; i < ct.Length; i++)
            {
                Rectangle r = new Rectangle(x0 + (i % cols) * (cardW + gap), y + (i / cols) * (cardH + gap), cardW, cardH);
                Draw.Card(g, r, ct[i], cv[i], cs[i], cc[i]);
            }
            y += ((ct.Length + cols - 1) / cols) * (cardH + gap);

            // ---- diagnostico ----
            int lineH = S(22);
            int ih = S(46) + insights.Count * lineH;
            Rectangle ir = Draw.Panel(g, new Rectangle(x0, y, cw, ih), "Diagnóstico automático", "o que olhar primeiro");
            int iy = ir.Y;
            foreach (Insight ins in insights)
            {
                using (SolidBrush b = new SolidBrush(ins.C)) g.FillEllipse(b, ir.X, iy + S(7), S(8), S(8));
                Draw.Text(g, ins.T, Fonts.Text, Theme.Text, new Rectangle(ir.X + S(16), iy, ir.Width - S(16), lineH), Draw.L);
                iy += lineH;
            }
            y += ih + gap;

            // ---- disponibilidade x hosts em falha ----
            int ch = S(250);
            Rectangle pr = Draw.Panel(g, new Rectangle(x0, y, cw, ch), "Disponibilidade da rede e hosts em falha", "por intervalo de " + Fmt.Bucket(d.Bucket) + "  ·  passe o mouse para detalhes");
            Rectangle plot = new Rectangle(pr.X + S(56), pr.Y + S(24), pr.Width - S(112), pr.Height - S(46));
            double amin = 100;
            foreach (double v in d.NetAvail) if (v >= 0 && v < amin) amin = v;
            double lo = Math.Floor(amin - 0.5);
            if (lo > 99) lo = 99;
            if (lo < 0) lo = 0;
            double dmax = Draw.NiceMax(Math.Max(1, Draw.Max(d.NetDown)) * 1.2);
            Draw.YAxis(g, plot, lo, 100, 4, "0.#", "%", false, true, Theme.Good);
            Draw.YAxis(g, plot, 0, dmax, 4, "0.#", "", true, false, Theme.Bad);
            Draw.Bars(g, plot, d.NetDown, dmax, Color.FromArgb(150, Theme.Bad));
            Draw.Line(g, plot, d.NetAvail, lo, 100, Theme.Good, 2f, true);
            Draw.TimeAxis(g, plot, d);
            int lx = Draw.Legend(g, pr.X, pr.Y, Theme.Good, "disponibilidade (%) - eixo esquerdo");
            Draw.Legend(g, lx, pr.Y, Theme.Bad, "hosts com falha no intervalo - eixo direito");
            AddTimeHit(hc, plot);
            y += ch + gap;

            // ---- latencia | perda ----
            int half = (cw - gap) / 2;
            Rectangle pl = Draw.Panel(g, new Rectangle(x0, y, half, ch), "Latência da rede", "média e p95 das respostas (ms)");
            Rectangle plotL = new Rectangle(pl.X + S(56), pl.Y + S(24), pl.Width - S(66), pl.Height - S(46));
            double lmax = Draw.NiceMax(Math.Max(1, Draw.Max(d.NetP95)) * 1.1);
            Draw.YAxis(g, plotL, 0, lmax, 4, "0.#", " ms", false, true, Theme.Muted);
            Draw.Line(g, plotL, d.NetP95, 0, lmax, Theme.Purple, 1.5f, false);
            Draw.Line(g, plotL, d.NetLat, 0, lmax, Theme.Blue, 2f, true);
            Draw.TimeAxis(g, plotL, d);
            lx = Draw.Legend(g, pl.X, pl.Y, Theme.Blue, "média");
            Draw.Legend(g, lx, pl.Y, Theme.Purple, "p95");
            AddTimeHit(hc, plotL);

            Rectangle pp = Draw.Panel(g, new Rectangle(x0 + half + gap, y, cw - half - gap, ch), "Perda de pacotes", "% dos ciclos com falha confirmada e com oscilação");
            Rectangle plotP = new Rectangle(pp.X + S(56), pp.Y + S(24), pp.Width - S(66), pp.Height - S(46));
            double pmax = 0;
            for (int b = 0; b < d.NB; b++) pmax = Math.Max(pmax, Math.Max(0, d.NetFailPct[b]) + Math.Max(0, d.NetOscPct[b]));
            pmax = Draw.NiceMax(Math.Max(0.1, pmax) * 1.1);
            Draw.YAxis(g, plotP, 0, pmax, 4, "0.##", "%", false, true, Theme.Muted);
            Draw.StackBars(g, plotP, d.NetFailPct, d.NetOscPct, pmax, Theme.Bad, Theme.Warn);
            Draw.TimeAxis(g, plotP, d);
            lx = Draw.Legend(g, pp.X, pp.Y, Theme.Bad, "falha confirmada");
            Draw.Legend(g, lx, pp.Y, Theme.Warn, "oscilação (1 ping perdido)");
            AddTimeHit(hc, plotP);
            y += ch + gap;

            // ---- rankings ----
            List<HostStats> byDown = new List<HostStats>();
            List<HostStats> byLat = new List<HostStats>();
            foreach (HostStats h in d.Hosts)
            {
                if (h.Downtime.Ticks > 0 || h.Osc > 0) byDown.Add(h);
                if (h.P95 >= 0) byLat.Add(h);
            }
            byDown.Sort(delegate(HostStats a, HostStats b)
            {
                int c = b.Downtime.CompareTo(a.Downtime);
                return c != 0 ? c : b.Osc.CompareTo(a.Osc);
            });
            byLat.Sort(delegate(HostStats a, HostStats b) { return b.P95.CompareTo(a.P95); });
            int topN = 15;
            int rowH = S(22);
            int rh = S(48) + topN * rowH;
            Rectangle r1 = Draw.Panel(g, new Rectangle(x0, y, half, rh), "Mais tempo fora do ar", "tempo total de queda (e oscilações)");
            List<HostStats> l1 = byDown.GetRange(0, Math.Min(topN, byDown.Count));
            double m1 = 1;
            foreach (HostStats h in l1) m1 = Math.Max(m1, h.Downtime.TotalSeconds);
            HBarList(g, r1, l1, rowH, hc, delegate(HostStats h) { return h.Downtime.TotalSeconds / m1; },
                delegate(HostStats h) { return h.Downtime.Ticks > 0 ? Fmt.Dur(h.Downtime) + "  ·  " + Fmt.Pl(h.Outages.Count, "queda", "quedas") : Fmt.Pl(h.Osc, "oscilação", "oscilações"); },
                delegate(HostStats h) { return h.Downtime.Ticks > 0 ? Theme.Bad : Theme.Warn; }, "Nenhum host com queda ou oscilação. Rede estável no período.");
            Rectangle r2 = Draw.Panel(g, new Rectangle(x0 + half + gap, y, cw - half - gap, rh), "Maior latência", "p95 por host");
            List<HostStats> l2 = byLat.GetRange(0, Math.Min(topN, byLat.Count));
            double m2 = 1;
            foreach (HostStats h in l2) m2 = Math.Max(m2, h.P95);
            HBarList(g, r2, l2, rowH, hc, delegate(HostStats h) { return h.P95 / m2; },
                delegate(HostStats h) { return "p95 " + Fmt.Ms(h.P95) + "  ·  média " + Fmt.Ms(h.AvgMs); },
                delegate(HostStats h) { return h.P95 >= 100 ? Theme.Warn : Theme.Blue; }, "Sem respostas no período.");
            y += rh + gap;

            // ---- mapa de calor: hosts mais problematicos x tempo ----
            List<HostStats> worst = new List<HostStats>();
            foreach (HostStats h in byDown) { worst.Add(h); if (worst.Count >= 30) break; }
            if (worst.Count > 0)
            {
                int cellH = S(16);
                int hh = S(70) + worst.Count * cellH;
                Rectangle hr = Draw.Panel(g, new Rectangle(x0, y, cw, hh), "Linha do tempo dos hosts mais problemáticos", "verde = ok  ·  amarelo = oscilou  ·  vermelho = caiu  ·  cinza = sem dados");
                HeatRows(g, new Rectangle(hr.X, hr.Y + S(4), hr.Width, worst.Count * cellH), worst, cellH, hc);
                Draw.TimeAxis(g, new Rectangle(hr.X + S(190), hr.Y + S(4), hr.Width - S(190), worst.Count * cellH), d);
                y += hh + gap;
            }
            return y + pad;
        }

        delegate double HostValue(HostStats h);
        delegate string HostLabel(HostStats h);
        delegate Color HostColor(HostStats h);

        void HBarList(Graphics g, Rectangle r, List<HostStats> list, int rowH, Canvas hc, HostValue val, HostLabel txt, HostColor col, string empty)
        {
            if (list.Count == 0)
            {
                Draw.Text(g, empty, Fonts.Text, Theme.Muted, new Rectangle(r.X, r.Y, r.Width, rowH), Draw.L);
                return;
            }
            int labelW = Math.Min(S(200), r.Width / 3);
            int textW = S(170);
            int barX = r.X + labelW + S(8);
            int barW = Math.Max(S(20), r.Width - labelW - textW - S(16));
            for (int i = 0; i < list.Count; i++)
            {
                HostStats h = list[i];
                int yy = r.Y + i * rowH;
                Draw.Text(g, h.Label, Fonts.Text, Theme.Text, new Rectangle(r.X, yy, labelW, rowH), Draw.L);
                using (SolidBrush bg = new SolidBrush(Theme.NoData)) g.FillRectangle(bg, barX, yy + S(5), barW, rowH - S(10));
                int w = (int)Math.Round(barW * Math.Max(0, Math.Min(1, val(h))));
                using (SolidBrush b = new SolidBrush(col(h))) g.FillRectangle(b, barX, yy + S(5), Math.Max(S(2), w), rowH - S(10));
                Draw.Text(g, txt(h), Fonts.Small, Theme.Muted, new Rectangle(barX + barW + S(8), yy, textW, rowH), Draw.L);
                if (hc != null)
                {
                    HostStats cap = h;
                    hc.AddHit(new Rectangle(r.X, yy, r.Width, rowH), delegate(Point p) { return HostTip(cap); }, delegate(Point p) { OpenHost(cap); });
                }
            }
        }

        static Color CellColor(HostStats h, int b)
        {
            int t = h.BOk[b] + h.BOsc[b] + h.BFail[b];
            if (t == 0) return Theme.NoData;
            if (h.BFail[b] > 0) return Theme.Lerp(Theme.Warn, Theme.Bad, Math.Min(1.0, 0.35 + h.BFail[b] / (double)t * 3));
            if (h.BOsc[b] > 0) return Theme.Lerp(Theme.GoodDim, Theme.Warn, 0.75);
            return Theme.GoodDim;
        }

        void HeatRows(Graphics g, Rectangle r, List<HostStats> hosts, int cellH, Canvas hc)
        {
            Dataset d = data;
            int labelW = S(190);
            Rectangle grid = new Rectangle(r.X + labelW, r.Y, r.Width - labelW, hosts.Count * cellH);
            for (int i = 0; i < hosts.Count; i++)
            {
                HostStats h = hosts[i];
                int yy = r.Y + i * cellH;
                Draw.Text(g, h.Label, Fonts.Small, Theme.Text, new Rectangle(r.X, yy, labelW - S(8), cellH), Draw.L);
                for (int b = 0; b < d.NB; b++)
                {
                    int x1 = grid.X + (int)((long)grid.Width * b / d.NB);
                    int x2 = grid.X + (int)((long)grid.Width * (b + 1) / d.NB);
                    using (SolidBrush br = new SolidBrush(CellColor(h, b)))
                        g.FillRectangle(br, x1, yy + 1, Math.Max(1, x2 - x1), cellH - 2);
                }
            }
            if (hc != null)
            {
                Rectangle gr = grid;
                hc.AddHit(gr, delegate(Point p)
                {
                    int row = (p.Y - gr.Y) / cellH;
                    int b = (int)((long)(p.X - gr.X) * d.NB / Math.Max(1, gr.Width));
                    if (row < 0 || row >= hosts.Count || b < 0 || b >= d.NB) return null;
                    HostStats h = hosts[row];
                    DateTime t0 = d.BucketStart(b);
                    return h.Label + "  (" + h.Ip + ")\n" + t0.ToString("dd/MM HH:mm", Fmt.Pt) + " – " + (t0 + d.Bucket).ToString("HH:mm", Fmt.Pt) +
                        "\nOK: " + h.BOk[b] + "   Oscilou: " + h.BOsc[b] + "   Falha: " + h.BFail[b] +
                        "\nDisponibilidade: " + Fmt.Pct(h.BucketAvail(b)) + "\n(clique para abrir o host)";
                }, delegate(Point p)
                {
                    int row = (p.Y - gr.Y) / cellH;
                    if (row >= 0 && row < hosts.Count) OpenHost(hosts[row]);
                });
            }
        }

        int PaintGroups(Graphics g, int W, Canvas hc)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Dataset d = data;
            if (d == null || d.Total == 0) return NoData(g, W);
            int pad = S(16), x0 = pad, cw = W - 2 * pad, y = pad;
            int rowH = S(26);
            int h = S(52) + d.Groups.Count * rowH;
            string sub = File.Exists(Path.Combine(appDir, "vlans.txt")) ? "agrupado pelo vlans.txt" : "agrupado por /24 (crie um vlans.txt ao lado do exe para nomear as VLANs)";
            Rectangle r = Draw.Panel(g, new Rectangle(x0, y, cw, h), "Disponibilidade por sub-rede / VLAN", sub);
            double lo = 100;
            foreach (GroupStats gs in d.Groups) if (gs.Total > 0) lo = Math.Min(lo, gs.Avail * 100);
            lo = Math.Floor(lo - 0.5);
            if (lo > 99) lo = 99;
            if (lo < 0) lo = 0;
            int labelW = Math.Min(S(260), cw / 3), textW = S(330);
            int barX = r.X + labelW + S(8), barW = Math.Max(S(40), r.Width - labelW - textW - S(16));
            Draw.Text(g, "escala: " + lo.ToString("0", Fmt.Pt) + "% a 100%", Fonts.Small, Theme.Muted, new Rectangle(barX, r.Y - S(4), barW, S(16)), Draw.L);
            for (int i = 0; i < d.Groups.Count; i++)
            {
                GroupStats gs = d.Groups[i];
                int yy = r.Y + S(14) + i * rowH;
                Draw.Text(g, gs.Name + "  ·  " + Fmt.Pl(gs.Hosts.Count, "host", "hosts"), Fonts.Text, Theme.Text, new Rectangle(r.X, yy, labelW, rowH), Draw.L);
                using (SolidBrush bg = new SolidBrush(Theme.NoData)) g.FillRectangle(bg, barX, yy + S(6), barW, rowH - S(12));
                double a = gs.Avail * 100;
                int w = (int)Math.Round(barW * Math.Max(0, Math.Min(1, (a - lo) / (100 - lo))));
                using (SolidBrush b = new SolidBrush(Theme.Avail(gs.Avail))) g.FillRectangle(b, barX, yy + S(6), Math.Max(S(2), w), rowH - S(12));
                Draw.Text(g, Fmt.Pct(gs.Avail) + "  ·  " + Fmt.Pl(gs.Outages, "queda", "quedas") + "  ·  p95 " + Fmt.Ms(gs.P95) + "  ·  perda " + Fmt.Pct100(gs.LossPct),
                    Fonts.Small, Theme.Muted, new Rectangle(barX + barW + S(8), yy, textW, rowH), Draw.L);
            }
            return y + h + pad;
        }

        int PaintPatterns(Graphics g, int W, Canvas hc)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Dataset d = data;
            if (d == null || d.Total == 0) return NoData(g, W);
            int pad = S(16), gap = S(12), x0 = pad, cw = W - 2 * pad, y = pad;
            int heatH = S(46) + 7 * S(26) + S(26);
            Rectangle r = Draw.Panel(g, new Rectangle(x0, y, cw, heatH), "Taxa de falha por dia da semana e hora", "% dos ciclos com falha confirmada  ·  mostra se há horário ou dia crítico (backup, troca de turno, manutenção)");
            WeekHeat(g, r, d.WeekFail, d.WeekTotal, Theme.Bad, hc, "falha");
            y += heatH + gap;
            r = Draw.Panel(g, new Rectangle(x0, y, cw, heatH), "Oscilações por dia da semana e hora", "% dos ciclos com 1 ping perdido (rede congestionada ou instável)");
            WeekHeat(g, r, d.WeekOsc, d.WeekTotal, Theme.Warn, hc, "oscilação");
            y += heatH + gap;

            // distribuicao de latencia + falhas por hora do dia
            int half = (cw - gap) / 2, ch = S(250);
            Rectangle pl = Draw.Panel(g, new Rectangle(x0, y, half, ch), "Distribuição da latência", "todas as respostas do período");
            LatencyDist(g, pl, d.Hist, Theme.Blue);
            Rectangle ph = Draw.Panel(g, new Rectangle(x0 + half + gap, y, cw - half - gap, ch), "Falhas por hora do dia", "% dos ciclos com falha, somando todos os dias");
            double[] hr = new double[24];
            for (int h = 0; h < 24; h++)
            {
                long f = 0, t = 0;
                for (int w = 0; w < 7; w++) { f += d.WeekFail[w, h]; t += d.WeekTotal[w, h]; }
                hr[h] = t > 0 ? f * 100.0 / t : -1;
            }
            HourBars(g, ph, hr, Theme.Bad);
            y += ch + gap;

            // grupos correlacionados
            int lineH = S(22);
            int cn = Math.Max(1, Math.Min(12, d.Corr.Count));
            int corrH = S(48) + cn * lineH;
            Rectangle rc = Draw.Panel(g, new Rectangle(x0, y, cw, corrH), "Hosts que falham ou oscilam juntos", "mesmos minutos com problema  ·  provável switch, porta, link, PoE ou energia em comum");
            if (d.Corr.Count == 0)
                Draw.Text(g, "Nenhum grupo encontrado: os problemas dos hosts não coincidem no tempo.", Fonts.Text, Theme.Muted, new Rectangle(rc.X, rc.Y, rc.Width, lineH), Draw.L);
            for (int i = 0; i < d.Corr.Count && i < 12; i++)
            {
                CorrGroup cg = d.Corr[i];
                int yy = rc.Y + i * lineH;
                using (SolidBrush b = new SolidBrush(Theme.Warn)) g.FillEllipse(b, rc.X, yy + S(7), S(8), S(8));
                Draw.Text(g, "Grupo " + (i + 1) + "  (" + cg.Hosts.Count + " hosts, até " + cg.Shared + " minutos em comum):  " + Fmt.Names(cg.Hosts, 12),
                    Fonts.Text, Theme.Text, new Rectangle(rc.X + S(16), yy, rc.Width - S(16), lineH), Draw.L);
            }
            y += corrH + gap;

            // quedas repetidas
            List<HostStats> flap = new List<HostStats>();
            foreach (HostStats h in d.Hosts) if (h.Outages.Count >= 2) flap.Add(h);
            flap.Sort(delegate(HostStats a, HostStats b) { return b.Outages.Count.CompareTo(a.Outages.Count); });
            if (flap.Count > 15) flap.RemoveRange(15, flap.Count - 15);
            int rowH = S(22);
            int fh = S(48) + Math.Max(1, flap.Count) * rowH;
            Rectangle rf = Draw.Panel(g, new Rectangle(x0, y, cw, fh), "Quedas repetidas (flapping)", "hosts que caem e voltam várias vezes: cabo, porta, PoE, energia ou equipamento com defeito");
            double fm = 1;
            foreach (HostStats h in flap) fm = Math.Max(fm, h.Outages.Count);
            HBarList(g, rf, flap, rowH, hc, delegate(HostStats h) { return h.Outages.Count / fm; },
                delegate(HostStats h) { return Fmt.Pl(h.Outages.Count, "queda", "quedas") + "  ·  MTBF " + (h.Mtbf.Ticks > 0 ? Fmt.Dur(h.Mtbf) : "—"); },
                delegate(HostStats h) { return Theme.Bad; }, "Nenhum host caiu mais de uma vez.");
            y += fh + gap;
            return y + pad;
        }

        void WeekHeat(Graphics g, Rectangle r, int[,] val, int[,] tot, Color baseColor, Canvas hc, string what)
        {
            int labelW = S(44), rowH = S(26);
            Rectangle grid = new Rectangle(r.X + labelW, r.Y, r.Width - labelW, 7 * rowH);
            double max = 0;
            for (int w = 0; w < 7; w++)
                for (int h = 0; h < 24; h++)
                    if (tot[w, h] > 0) max = Math.Max(max, val[w, h] / (double)tot[w, h]);
            for (int w = 0; w < 7; w++)
            {
                int yy = grid.Y + w * rowH;
                Draw.Text(g, WeekDays[w], Fonts.Small, Theme.Muted, new Rectangle(r.X, yy, labelW, rowH), Draw.L);
                for (int h = 0; h < 24; h++)
                {
                    int x1 = grid.X + grid.Width * h / 24, x2 = grid.X + grid.Width * (h + 1) / 24;
                    Rectangle cell = new Rectangle(x1 + 1, yy + 1, x2 - x1 - 2, rowH - 2);
                    Color c;
                    double rate = tot[w, h] > 0 ? val[w, h] / (double)tot[w, h] : -1;
                    if (rate < 0) c = Theme.NoData;
                    else if (rate == 0 || max <= 0) c = Color.FromArgb(38, 44, 52);
                    else c = Theme.Lerp(Color.FromArgb(60, 52, 48), baseColor, 0.25 + 0.75 * Math.Sqrt(rate / max));
                    using (SolidBrush b = new SolidBrush(c)) g.FillRectangle(b, cell);
                    if (rate > 0 && cell.Width >= S(34))
                    {
                        // texto escuro em celula clara, claro em celula escura
                        Color tc = (c.R * 299 + c.G * 587 + c.B * 114) / 1000 > 140 ? Color.FromArgb(25, 25, 25) : Theme.Text;
                        Draw.Text(g, (rate * 100).ToString(rate * 100 < 1 ? "0.00" : "0.0", Fmt.Pt), Fonts.Small, tc, cell, Draw.C);
                    }
                }
            }
            for (int h = 0; h < 24; h++)
            {
                int x1 = grid.X + grid.Width * h / 24, x2 = grid.X + grid.Width * (h + 1) / 24;
                Draw.Text(g, h + "h", Fonts.Small, Theme.Muted, new Rectangle(x1, grid.Bottom + S(4), x2 - x1, S(16)), Draw.C);
            }
            if (hc != null)
            {
                Rectangle gr = grid;
                hc.AddHit(gr, delegate(Point p)
                {
                    int w = (p.Y - gr.Y) / rowH;
                    int h = (int)((long)(p.X - gr.X) * 24 / Math.Max(1, gr.Width));
                    if (w < 0 || w > 6 || h < 0 || h > 23) return null;
                    if (tot[w, h] == 0) return WeekDays[w] + " " + h + "h: sem amostras";
                    return WeekDays[w] + " " + h + "h–" + (h + 1) + "h\n" + Fmt.N(val[w, h]) + " ciclos com " + what + " de " + Fmt.N(tot[w, h]) +
                        " (" + Fmt.Pct100(val[w, h] * 100.0 / tot[w, h]) + ")";
                }, null);
            }
        }

        static readonly int[] LatEdges = { 0, 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, int.MaxValue };
        static readonly string[] LatNames = { "<1", "1–2", "2–5", "5–10", "10–20", "20–50", "50–100", "100–200", "200–500", "500–1k", "≥1000" };

        void LatencyDist(Graphics g, Rectangle r, int[] hist, Color c)
        {
            long[] cnt = new long[LatNames.Length];
            long total = 0;
            for (int i = 0; i < hist.Length; i++)
            {
                if (hist[i] == 0) continue;
                int v = LatHist.Value(i);
                for (int k = 0; k < LatNames.Length; k++)
                    if (v >= LatEdges[k] && v < LatEdges[k + 1]) { cnt[k] += hist[i]; break; }
                total += hist[i];
            }
            if (total == 0) { Draw.Text(g, "Sem respostas.", Fonts.Text, Theme.Muted, new Rectangle(r.X, r.Y, r.Width, S(20)), Draw.L); return; }
            Rectangle plot = new Rectangle(r.X + S(8), r.Y + S(18), r.Width - S(16), r.Height - S(44));
            int n = LatNames.Length;
            double max = 0;
            foreach (long x in cnt) max = Math.Max(max, x);
            using (SolidBrush b = new SolidBrush(c))
            {
                for (int k = 0; k < n; k++)
                {
                    int x1 = plot.X + plot.Width * k / n, x2 = plot.X + plot.Width * (k + 1) / n;
                    int bh = (int)(plot.Height * cnt[k] / max);
                    if (cnt[k] > 0) g.FillRectangle(b, x1 + S(3), plot.Bottom - Math.Max(1, bh), x2 - x1 - S(6), Math.Max(1, bh));
                    double pct = cnt[k] * 100.0 / total;
                    if (cnt[k] > 0) Draw.Text(g, Fmt.Pct100(pct), Fonts.Small, Theme.Text, new Rectangle(x1, plot.Bottom - Math.Max(1, bh) - S(17), x2 - x1, S(16)), Draw.C);
                    Draw.Text(g, LatNames[k], Fonts.Small, Theme.Muted, new Rectangle(x1, plot.Bottom + S(4), x2 - x1, S(16)), Draw.C);
                }
            }
            Draw.Text(g, "ms", Fonts.Small, Theme.Muted, new Rectangle(plot.Right - S(30), plot.Bottom + S(20), S(30), S(14)), Draw.R);
        }

        void HourBars(Graphics g, Rectangle r, double[] v, Color c)
        {
            Rectangle plot = new Rectangle(r.X + S(50), r.Y + S(10), r.Width - S(58), r.Height - S(36));
            double max = Draw.NiceMax(Math.Max(0.01, Draw.Max(v)) * 1.1);
            Draw.YAxis(g, plot, 0, max, 4, "0.##", "%", false, true, Theme.Muted);
            using (SolidBrush b = new SolidBrush(c))
            {
                for (int h = 0; h < 24; h++)
                {
                    int x1 = plot.X + plot.Width * h / 24, x2 = plot.X + plot.Width * (h + 1) / 24;
                    if (v[h] > 0)
                    {
                        float yy = Draw.Yv(plot, v[h], 0, max);
                        g.FillRectangle(b, x1 + 2, yy, x2 - x1 - 4, Math.Max(1f, plot.Bottom - yy));
                    }
                    if (h % 2 == 0) Draw.Text(g, h + "h", Fonts.Small, Theme.Muted, new Rectangle(x1 - S(6), plot.Bottom + S(4), x2 - x1 + S(12), S(16)), Draw.C);
                }
            }
        }

        int PaintHost(Graphics g, int W, Canvas hc)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Dataset d = data;
            if (d == null || d.Total == 0) return NoData(g, W);
            HostStats h = selHost;
            int pad = S(16), gap = S(12), x0 = pad, cw = W - 2 * pad, y = pad;
            if (h == null)
            {
                Draw.Text(g, "Escolha um host na lista acima (ou dê duplo clique em um host na aba Hosts).", Fonts.H, Theme.Muted, new Rectangle(x0, y, cw, S(30)), Draw.L);
                return S(80);
            }
            Draw.Text(g, h.Janela.ToString("000") + "  " + h.Label, Fonts.Title, Theme.Text, new Rectangle(x0, y, cw, S(30)), Draw.L);
            y += S(30);
            Draw.Text(g, h.Ip + "  ·  " + h.Group + "  ·  visto de " + Fmt.When(h.FirstSeen) + " a " + Fmt.When(h.LastSeen) + "  ·  situação: " + h.Situation,
                Fonts.Small, h.DownNow ? Theme.Bad : Theme.Muted, new Rectangle(x0, y, cw, S(18)), Draw.L);
            y += S(28);

            string[] ct = { "Disponibilidade", "Quedas", "Tempo fora", "Maior queda", "MTTR / MTBF", "Latência média", "p95 / p99", "Mín / Máx", "Jitter", "Oscilações" };
            string[] cv = { Fmt.Pct(h.Avail), h.Outages.Count.ToString(), Fmt.Dur(h.Downtime), h.Longest.Ticks > 0 ? Fmt.Dur(h.Longest) : "—",
                h.Mttr.Ticks > 0 ? Fmt.Dur(h.Mttr) : "—", Fmt.Ms(h.AvgMs), h.P95 >= 0 ? h.P95 + " / " + h.P99 + " ms" : "—",
                h.MinMs >= 0 ? h.MinMs + " / " + h.MaxMs + " ms" : "—", Fmt.Ms(h.Jitter), Fmt.N(h.Osc) };
            string[] cs = { Fmt.N(h.Fail) + " ciclos em falha de " + Fmt.N(h.Total), h.DownNow ? "FORA AGORA" : "nenhuma em andamento", "soma das quedas",
                "", h.Mtbf.Ticks > 0 ? "MTBF " + Fmt.Dur(h.Mtbf) : "sem quedas suficientes", "mediana " + Fmt.Ms(h.P50), "95% / 99% das respostas abaixo de",
                "", "variação entre pings seguidos", Fmt.Pct100(h.OscPct) + " dos ciclos" };
            Color[] cc = { Theme.Avail(h.Avail), h.Outages.Count > 0 ? Theme.Bad : Theme.Good, h.Downtime.Ticks > 0 ? Theme.Bad : Theme.Good, Theme.Purple, Theme.Purple,
                Theme.Blue, h.P95 > 100 ? Theme.Warn : Theme.Blue, Theme.Blue, h.Jitter > 30 ? Theme.Warn : Theme.Blue, h.Osc > 0 ? Theme.Warn : Theme.Good };
            int cols = Math.Max(2, Math.Min(5, (cw + gap) / (S(200) + gap)));
            int cardW = (cw - gap * (cols - 1)) / cols, cardH = S(90);
            for (int i = 0; i < ct.Length; i++)
            {
                Rectangle r = new Rectangle(x0 + (i % cols) * (cardW + gap), y + (i / cols) * (cardH + gap), cardW, cardH);
                Draw.Card(g, r, ct[i], cv[i], cs[i], cc[i]);
            }
            y += ((ct.Length + cols - 1) / cols) * (cardH + gap);

            // latencia no tempo + falhas
            double[] avg = new double[d.NB], mx = new double[d.NB], fp = new double[d.NB];
            for (int b = 0; b < d.NB; b++)
            {
                int resp = h.BOk[b] + h.BOsc[b], tot = resp + h.BFail[b];
                avg[b] = resp > 0 ? h.BSum[b] / (double)resp : -1;
                mx[b] = resp > 0 ? h.BMax[b] : -1;
                fp[b] = tot > 0 ? h.BFail[b] * 100.0 / tot : 0;
            }
            int ch = S(260);
            Rectangle pr = Draw.Panel(g, new Rectangle(x0, y, cw, ch), "Latência e falhas no tempo", "por intervalo de " + Fmt.Bucket(d.Bucket));
            Rectangle plot = new Rectangle(pr.X + S(56), pr.Y + S(24), pr.Width - S(112), pr.Height - S(46));
            double lmax = Draw.NiceMax(Math.Max(1, Draw.Max(mx)) * 1.1);
            Draw.YAxis(g, plot, 0, lmax, 4, "0.#", " ms", false, true, Theme.Muted);
            Draw.YAxis(g, plot, 0, 100, 4, "0", "%", true, false, Theme.Bad);
            Draw.Bars(g, plot, fp, 100, Color.FromArgb(110, Theme.Bad));
            Draw.Line(g, plot, mx, 0, lmax, Color.FromArgb(150, Theme.Purple), 1f, false);
            Draw.Line(g, plot, avg, 0, lmax, Theme.Blue, 2f, true);
            Draw.TimeAxis(g, plot, d);
            int lx = Draw.Legend(g, pr.X, pr.Y, Theme.Blue, "latência média");
            lx = Draw.Legend(g, lx, pr.Y, Theme.Purple, "latência máxima");
            Draw.Legend(g, lx, pr.Y, Theme.Bad, "% de ciclos em falha (eixo direito)");
            if (hc != null)
            {
                Rectangle pl = plot;
                HostStats cap = h;
                hc.AddHit(pl, delegate(Point p)
                {
                    int b = (int)((long)(p.X - pl.X) * d.NB / Math.Max(1, pl.Width));
                    if (b < 0 || b >= d.NB) return null;
                    DateTime t0 = d.BucketStart(b);
                    int resp = cap.BOk[b] + cap.BOsc[b];
                    return t0.ToString("dd/MM HH:mm", Fmt.Pt) + " – " + (t0 + d.Bucket).ToString("HH:mm", Fmt.Pt) +
                        "\nOK: " + cap.BOk[b] + "   Oscilou: " + cap.BOsc[b] + "   Falha: " + cap.BFail[b] +
                        "\nLatência média: " + (resp > 0 ? Fmt.Ms(cap.BSum[b] / (double)resp) : "—") + "   máx: " + (resp > 0 ? cap.BMax[b] + " ms" : "—");
                }, null);
            }
            y += ch + gap;

            // faixa de disponibilidade
            int sh = S(76);
            Rectangle rs = Draw.Panel(g, new Rectangle(x0, y, cw, sh), "Faixa de disponibilidade", "verde = ok  ·  amarelo = oscilou  ·  vermelho = caiu  ·  cinza = sem dados");
            List<HostStats> one = new List<HostStats>();
            one.Add(h);
            HeatRows(g, new Rectangle(rs.X - S(190) + S(56), rs.Y, rs.Width + S(190) - S(112), S(22)), one, S(22), null);
            y += sh + gap;

            // hora do dia + distribuicao
            int half = (cw - gap) / 2;
            int hh = S(240);
            Rectangle rh = Draw.Panel(g, new Rectangle(x0, y, half, hh), "Falhas por hora do dia", "% dos ciclos com falha");
            double[] hr = new double[24];
            for (int i = 0; i < 24; i++) hr[i] = h.HourTotal[i] > 0 ? h.HourFail[i] * 100.0 / h.HourTotal[i] : -1;
            HourBars(g, rh, hr, Theme.Bad);
            Rectangle rd = Draw.Panel(g, new Rectangle(x0 + half + gap, y, cw - half - gap, hh), "Distribuição da latência", "respostas deste host");
            LatencyDist(g, rd, h.Hist, Theme.Blue);
            y += hh + gap;
            return y + pad;
        }

        // ---------------- exportar ----------------

        void ExportPng()
        {
            ScrollHost sh = CurrentScroll();
            if (sh == null) sh = shOverview;
            if (data == null) return;
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "Imagem PNG|*.png";
                dlg.FileName = "UltraPing-" + (data.Profile ?? "rede") + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".png";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                int w = Math.Max(S(1200), sh.C.Width);
                int h;
                using (Bitmap tmp = new Bitmap(1, 1))
                using (Graphics g = Graphics.FromImage(tmp)) { h = sh.C.Painter(g, w, null); }
                using (Bitmap bmp = new Bitmap(w, h))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Theme.Back);
                        sh.C.Painter(g, w, null);
                    }
                    bmp.Save(dlg.FileName, ImageFormat.Png);
                }
                lblStatus.Text = "Imagem salva em " + dlg.FileName;
            }
        }

        static string Csv(string s)
        {
            if (s == null) return "";
            return s.Replace(';', ',').Replace("\r", " ").Replace("\n", " ");
        }

        void ExportCsv()
        {
            if (data == null) return;
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "CSV (Excel)|*.csv";
                dlg.FileName = "UltraPing-hosts-" + (data.Profile ?? "rede") + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                StringBuilder sb = new StringBuilder();
                sb.Append("Janela;Host;IP;Grupo;Disponibilidade_%;Quedas;TempoFora_s;MaiorQueda_s;MTTR_s;Perda_%;Oscilacoes;LatMedia_ms;P95_ms;P99_ms;Max_ms;Jitter_ms;Amostras;Situacao\r\n");
                foreach (HostStats h in data.Hosts)
                {
                    sb.Append(h.Janela).Append(';').Append(Csv(h.Label)).Append(';').Append(h.Ip).Append(';').Append(Csv(h.Group)).Append(';')
                      .Append(h.Avail < 0 ? "" : (h.Avail * 100).ToString("0.####", Fmt.Pt)).Append(';').Append(h.Outages.Count).Append(';')
                      .Append((long)h.Downtime.TotalSeconds).Append(';').Append((long)h.Longest.TotalSeconds).Append(';').Append((long)h.Mttr.TotalSeconds).Append(';')
                      .Append(h.LossPct < 0 ? "" : h.LossPct.ToString("0.###", Fmt.Pt)).Append(';').Append(h.Osc).Append(';')
                      .Append(h.AvgMs < 0 ? "" : h.AvgMs.ToString("0.#", Fmt.Pt)).Append(';').Append(h.P95 < 0 ? "" : h.P95.ToString()).Append(';')
                      .Append(h.P99 < 0 ? "" : h.P99.ToString()).Append(';').Append(h.MaxMs < 0 ? "" : h.MaxMs.ToString()).Append(';')
                      .Append(h.Jitter < 0 ? "" : h.Jitter.ToString("0.#", Fmt.Pt)).Append(';').Append(h.Total).Append(';').Append(Csv(h.Situation)).Append("\r\n");
                }
                File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
                lblStatus.Text = "CSV salvo em " + dlg.FileName;
            }
        }
    }
}
