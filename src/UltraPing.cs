// UltraPing - monitor de ping com janela dinamica (ate 512 hosts, so N paineis visiveis)
// Compilar com compilar.bat (usa o csc.exe que ja vem no Windows).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.Mail;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UltraPing
{
    // ------------------------------------------------------------------
    // Programa principal
    // ------------------------------------------------------------------
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                MessageBox.Show("Erro inesperado:\n\n" + e.Exception.Message, "UltraPing");
            };
            string profile = args.Length > 0 ? args[0] : null;
            Application.Run(new MainForm(profile));
        }
    }

    // ------------------------------------------------------------------
    // Escala de DPI
    // ------------------------------------------------------------------
    static class Dpi
    {
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
    }

    // ------------------------------------------------------------------
    // Arquivo INI
    // ------------------------------------------------------------------
    class IniFile
    {
        class Section
        {
            public List<string> Keys = new List<string>();
            public Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        readonly List<string> order = new List<string>();
        readonly Dictionary<string, Section> sections = new Dictionary<string, Section>(StringComparer.OrdinalIgnoreCase);
        readonly string path;

        public IniFile(string path, bool load)
        {
            this.path = path;
            if (!load || !File.Exists(path)) return;
            string cur = "";
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';') continue;
                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    cur = line.Substring(1, line.Length - 2);
                    GetSection(cur, true);
                    continue;
                }
                int eq = raw.IndexOf('=');
                if (eq <= 0) continue;
                SetRaw(cur, raw.Substring(0, eq).Trim(), raw.Substring(eq + 1));
            }
        }

        Section GetSection(string name, bool create)
        {
            Section s;
            if (sections.TryGetValue(name, out s)) return s;
            if (!create) return null;
            s = new Section();
            sections[name] = s;
            order.Add(name);
            return s;
        }

        void SetRaw(string sec, string key, string val)
        {
            Section s = GetSection(sec, true);
            if (!s.Values.ContainsKey(key)) s.Keys.Add(key);
            s.Values[key] = val;
        }

        public string Get(string sec, string key, string def)
        {
            Section s = GetSection(sec, false);
            string v;
            if (s != null && s.Values.TryGetValue(key, out v)) return v;
            return def;
        }

        public int GetInt(string sec, string key, int def)
        {
            string v = Get(sec, key, null);
            int n;
            if (v != null && int.TryParse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            return def;
        }

        public bool GetBool(string sec, string key, bool def)
        {
            string v = Get(sec, key, null);
            if (v == null) return def;
            v = v.Trim().ToLowerInvariant();
            if (v == "true" || v == "1" || v == "yes" || v == "sim") return true;
            if (v == "false" || v == "0" || v == "no" || v == "nao" || v == "não") return false;
            return def;
        }

        public void Set(string sec, string key, string val) { SetRaw(sec, key, val ?? ""); }
        public void Set(string sec, string key, int val) { SetRaw(sec, key, val.ToString(CultureInfo.InvariantCulture)); }
        public void Set(string sec, string key, bool val) { SetRaw(sec, key, val ? "True" : "False"); }

        public void Remove(string sec, string key)
        {
            Section s = GetSection(sec, false);
            if (s == null) return;
            s.Values.Remove(key);
            s.Keys.Remove(key);
        }

        public List<string> KeysOf(string sec)
        {
            Section s = GetSection(sec, false);
            return s == null ? new List<string>() : new List<string>(s.Keys);
        }

        public void Save()
        {
            StringBuilder sb = new StringBuilder();
            foreach (string name in order)
            {
                Section s = sections[name];
                if (name.Length > 0) sb.Append("[").Append(name).Append("]\r\n");
                foreach (string k in s.Keys) sb.Append(k).Append("=").Append(s.Values[k]).Append("\r\n");
                sb.Append("\r\n");
            }
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            File.Copy(tmp, path, true);
            File.Delete(tmp);
        }
    }

    // ------------------------------------------------------------------
    // Configuracao
    // ------------------------------------------------------------------
    enum ViewMode { Basic = 1, Compact = 2, Minimal = 3, Detailed = 4 }

    class HostEntry
    {
        public string Address = "";
        public string Name = "";
        public bool Enabled = true;
        public HostEntry Clone()
        {
            HostEntry h = new HostEntry();
            h.Address = Address; h.Name = Name; h.Enabled = Enabled;
            return h;
        }
    }

    class Config
    {
        public const int MaxHosts = 512;
        public int WindowCount = 40;      // quantos paineis aparecem na tela (por pagina)
        public int Columns = 0;           // 0 = automatico
        public bool HideEmpty = false;
        public int PromoteAfter = 1;      // falhas seguidas para o host subir ao topo
        public bool RecheckDisabled = true;   // testar 1 vez por dia os desativados e reativar os que responderem
        public string RecheckTime = "08:00";  // horario da verificacao diaria (HH:mm)
        public string RecheckLastRun = "";    // data (yyyy-MM-dd) da ultima verificacao
        public List<HostEntry> Hosts = new List<HostEntry>();
        public int PingInterval = 1000;
        public int PingTimeout = 2000;
        public int ConfirmPings = 2;      // pings de confirmacao antes de dar a falha como certa (0 = desligado)
        public int UnstableMinutes = 10;  // minutos em amarelo depois de uma oscilacao
        public bool AutoStart = true;
        public bool AutoScroll = true;
        public bool PlaySounds = true;
        public bool ShowTime = true;
        public bool LogToFiles = true;
        public ViewMode View = ViewMode.Compact;
        public int MaxLines = 500;
        public bool MailAlerts = false;
        public int SendAfter = 3;
        public int MaxPerMinute = 1;
        public int MaxPerHour = 10;
        public string SmtpServer = "smtp.gmail.com";
        public int SmtpPort = 587;
        public bool SmtpSsl = true;
        public string SmtpUser = "";
        public string SmtpPass = "";
        public string MailFrom = "";
        public string MailTo = "";
        public string MailSubject = "UltraPing: %HOST_NAME% (%HOST_IP%) está %HOST_STATUS%";
        public string MailBody = "Host %HOST_ID%-%HOST_NAME% (%HOST_IP%) está %HOST_STATUS%\nEm %DATE%, pacotes perdidos: %PL_COUNT%\n\nMensagem enviada pelo UltraPing";

        // configuracao de fabrica: so a propria maquina (localhost e 127.0.0.1), pingando assim que abre
        public static Config CreateDefault()
        {
            Config c = new Config();
            c.Hosts.Add(new HostEntry { Address = "localhost", Name = "localhost", Enabled = true });
            c.Hosts.Add(new HostEntry { Address = "127.0.0.1", Name = "127.0.0.1", Enabled = true });
            return c;
        }

        public Config Clone()
        {
            Config c = (Config)MemberwiseClone();
            c.Hosts = new List<HostEntry>();
            foreach (HostEntry h in Hosts) c.Hosts.Add(h.Clone());
            return c;
        }

        static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        public static string ViewLabel(ViewMode v)
        {
            switch (v)
            {
                case ViewMode.Basic: return "Básica";
                case ViewMode.Compact: return "Compacta";
                case ViewMode.Minimal: return "Mínima";
                default: return "Detalhada";
            }
        }

        static ViewMode ParseView(string s)
        {
            if (s == null) return ViewMode.Compact;
            s = s.Trim().ToLowerInvariant();
            if (s == "basic" || s == "basica" || s == "básica") return ViewMode.Basic;
            if (s == "minimal" || s == "minima" || s == "mínima") return ViewMode.Minimal;
            if (s == "detailed" || s == "detalhada") return ViewMode.Detailed;
            return ViewMode.Compact;
        }

        public static bool TryParseTime(string s, out TimeSpan t)
        {
            t = TimeSpan.Zero;
            if (s == null) return false;
            string[] p = s.Trim().Split(':');
            int h, m;
            if (p.Length != 2 || !int.TryParse(p[0], out h) || !int.TryParse(p[1], out m)) return false;
            if (h < 0 || h > 23 || m < 0 || m > 59) return false;
            t = new TimeSpan(h, m, 0);
            return true;
        }

        public static string Escape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\r\n", "\\n").Replace("\n", "\\n");
        }

        public static string Unescape(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[i + 1];
                    if (n == 'n') { sb.Append('\n'); i++; continue; }
                    if (n == '\\') { sb.Append('\\'); i++; continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static Config Load(string path)
        {
            Config c = new Config();
            IniFile ini = new IniFile(path, true);
            c.WindowCount = Clamp(ini.GetInt("Window", "WindowCount", c.WindowCount), 1, 256);
            c.Columns = Clamp(ini.GetInt("Window", "Columns", 0), 0, 32);
            c.HideEmpty = ini.GetBool("Window", "HideEmpty", false);
            c.PromoteAfter = Clamp(ini.GetInt("Window", "PromoteAfter", 1), 1, 1000);
            c.RecheckDisabled = ini.GetBool("Recheck", "Enabled", true);
            c.RecheckTime = ini.Get("Recheck", "Time", c.RecheckTime).Trim();
            c.RecheckLastRun = ini.Get("Recheck", "LastRun", "").Trim();
            for (int i = 1; i <= MaxHosts; i++)
            {
                HostEntry h = new HostEntry();
                h.Address = ini.Get("Hosts", "IP-" + i, "").Trim();
                h.Name = ini.Get("Hosts", "Name-" + i, "").Trim();
                h.Enabled = ini.GetBool("Hosts", "Enabled-" + i, true);
                if (h.Address.Length == 0 && h.Name.Length == 0) continue;
                c.Hosts.Add(h);
            }
            c.PingInterval = Clamp(ini.GetInt("Ping", "PingTimeDelta", 1000), 100, 600000);
            c.PingTimeout = Clamp(ini.GetInt("Ping", "PingTimeout", c.PingTimeout), 100, 60000);
            c.ConfirmPings = Clamp(ini.GetInt("Ping", "ConfirmPings", c.ConfirmPings), 0, 5);
            c.UnstableMinutes = Clamp(ini.GetInt("Ping", "UnstableMinutes", c.UnstableMinutes), 0, 1440);
            c.AutoStart = ini.GetBool("Ping", "PingAutoStart", true);
            c.AutoScroll = ini.GetBool("General", "AutoScroll", true);
            c.PlaySounds = ini.GetBool("General", "PlaySounds", c.PlaySounds);
            c.ShowTime = ini.GetBool("General", "ShowTime", true);
            c.LogToFiles = ini.GetBool("General", "LogToFiles", true);
            c.MaxLines = Clamp(ini.GetInt("General", "MaxLines", 500), 50, 20000);
            c.View = ParseView(ini.Get("General", "PingResultsView", ""));
            c.MailAlerts = ini.GetBool("Mail", "MailAlerts", false);
            c.SendAfter = Clamp(ini.GetInt("Mail", "SendAfter", c.SendAfter), 1, 100000);
            c.MaxPerMinute = Clamp(ini.GetInt("Mail", "MaxPerMinute", 1), 0, 1000);
            c.MaxPerHour = Clamp(ini.GetInt("Mail", "MaxPerHour", 10), 0, 100000);
            c.SmtpServer = ini.Get("Mail", "SMTPServer", c.SmtpServer);
            c.SmtpPort = Clamp(ini.GetInt("Mail", "SMTPServerPort", 587), 1, 65535);
            c.SmtpSsl = ini.GetBool("Mail", "SMTPSSL", true);
            c.SmtpUser = ini.Get("Mail", "SMTPUser", "");
            c.SmtpPass = ini.Get("Mail", "SMTPPass", "");
            c.MailFrom = ini.Get("Mail", "MailFrom", "");
            c.MailTo = ini.Get("Mail", "MailTo", "");
            c.MailSubject = ini.Get("Mail", "MailSubject", c.MailSubject);
            c.MailBody = Unescape(ini.Get("Mail", "MailBody", Escape(c.MailBody)));
            return c;
        }

        public void Save(string path)
        {
            IniFile ini = new IniFile(path, true);
            ini.Set("Window", "WindowCount", WindowCount);
            ini.Set("Window", "Columns", Columns);
            ini.Set("Window", "HideEmpty", HideEmpty);
            ini.Set("Window", "PromoteAfter", PromoteAfter);
            foreach (string k in ini.KeysOf("Hosts"))
            {
                int dash = k.LastIndexOf('-');
                int n;
                if (dash > 0 && int.TryParse(k.Substring(dash + 1), out n) && n > Hosts.Count) ini.Remove("Hosts", k);
            }
            for (int i = 0; i < Hosts.Count; i++)
            {
                ini.Set("Hosts", "IP-" + (i + 1), Hosts[i].Address);
                ini.Set("Hosts", "Name-" + (i + 1), Hosts[i].Name);
                ini.Set("Hosts", "Enabled-" + (i + 1), Hosts[i].Enabled);
            }
            ini.Set("Ping", "PingTimeDelta", PingInterval);
            ini.Set("Ping", "PingTimeout", PingTimeout);
            ini.Set("Ping", "ConfirmPings", ConfirmPings);
            ini.Set("Ping", "UnstableMinutes", UnstableMinutes);
            ini.Set("Ping", "PingAutoStart", AutoStart);
            ini.Set("General", "AutoScroll", AutoScroll);
            ini.Set("General", "PlaySounds", PlaySounds);
            ini.Set("General", "ShowTime", ShowTime);
            ini.Set("General", "LogToFiles", LogToFiles);
            ini.Set("General", "PingResultsView", View.ToString());
            ini.Set("General", "MaxLines", MaxLines);
            ini.Set("Mail", "MailAlerts", MailAlerts);
            ini.Set("Mail", "SendAfter", SendAfter);
            ini.Set("Mail", "MaxPerMinute", MaxPerMinute);
            ini.Set("Mail", "MaxPerHour", MaxPerHour);
            ini.Set("Mail", "SMTPServer", SmtpServer);
            ini.Set("Mail", "SMTPServerPort", SmtpPort);
            ini.Set("Mail", "SMTPSSL", SmtpSsl);
            ini.Set("Mail", "SMTPUser", SmtpUser);
            ini.Set("Mail", "SMTPPass", SmtpPass);
            ini.Set("Mail", "MailFrom", MailFrom);
            ini.Set("Mail", "MailTo", MailTo);
            ini.Set("Mail", "MailSubject", MailSubject);
            ini.Set("Mail", "MailBody", Escape(MailBody));
            ini.Set("Recheck", "Enabled", RecheckDisabled);
            ini.Set("Recheck", "Time", RecheckTime);
            ini.Set("Recheck", "LastRun", RecheckLastRun);
            ini.Save();
        }
    }

    // ------------------------------------------------------------------
    // Resultado de ping e monitor (uma thread por host)
    // ------------------------------------------------------------------
    class PingResult
    {
        public DateTime Time;
        public bool Success;
        public int Status;          // IPStatus ou -1 para excecao
        public long Ms;
        public int Ttl;
        public int Bytes;
        public string ReplyAddress;
        public string Error;
        public bool Blip;           // o primeiro ping falhou, mas a confirmacao respondeu (oscilacao)
    }

    class HostMonitor
    {
        static readonly byte[] Buffer = Encoding.ASCII.GetBytes("abcdefghijklmnopqrstuvwabcdefghi");

        public PingPane Pane;
        readonly string host;
        readonly int interval;
        readonly int timeout;
        readonly int startDelay;
        readonly int confirm;             // pings extras antes de dar a falha como certa
        readonly Action<HostMonitor, PingResult> callback;
        readonly ManualResetEvent stopEvent = new ManualResetEvent(false);
        volatile bool running;
        Thread thread;

        public HostMonitor(PingPane pane, string host, int interval, int timeout, int startDelay, int confirm, Action<HostMonitor, PingResult> callback)
        {
            this.Pane = pane;
            this.host = host;
            this.interval = interval;
            this.timeout = timeout;
            this.startDelay = startDelay;
            this.confirm = confirm;
            this.callback = callback;
        }

        PingResult Send(Ping ping)
        {
            PingResult r = new PingResult();
            r.Time = DateTime.Now;
            try
            {
                PingReply reply = ping.Send(host, timeout, Buffer);
                r.Status = (int)reply.Status;
                r.Success = reply.Status == IPStatus.Success;
                if (reply.Address != null && !reply.Address.Equals(System.Net.IPAddress.Any))
                    r.ReplyAddress = reply.Address.ToString();
                r.Ms = reply.RoundtripTime;
                if (reply.Options != null) r.Ttl = reply.Options.Ttl;
                if (reply.Buffer != null) r.Bytes = reply.Buffer.Length;
            }
            catch (Exception ex)
            {
                Exception inner = ex;
                while (inner.InnerException != null) inner = inner.InnerException;
                r.Success = false;
                r.Status = -1;
                r.Error = inner.Message;
            }
            return r;
        }

        public void Start()
        {
            running = true;
            stopEvent.Reset();
            thread = new Thread(new ThreadStart(Run), 512 * 1024);
            thread.IsBackground = true;
            thread.Name = "Ping " + host;
            thread.Start();
        }

        public void Stop()
        {
            running = false;
            stopEvent.Set();
        }

        void Run()
        {
            using (Ping ping = new Ping())
            {
                if (startDelay > 0 && stopEvent.WaitOne(startDelay)) return;
                while (running)
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    PingResult r = Send(ping);
                    // falhou: confirma com mais pings antes de alertar (evita alerta por 1 pacote perdido)
                    for (int k = 0; k < confirm && !r.Success && running; k++)
                    {
                        if (stopEvent.WaitOne(1000)) break;
                        PingResult c = Send(ping);
                        if (c.Success)
                        {
                            c.Time = r.Time;
                            c.Blip = true;
                            r = c;
                        }
                    }
                    if (!running) break;
                    callback(this, r);
                    int wait = interval - (int)sw.ElapsedMilliseconds;
                    if (wait < 10) wait = 10;
                    if (stopEvent.WaitOne(wait)) break;
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // Painel de um host (desenhado por codigo: leve, aguenta centenas)
    // ------------------------------------------------------------------
    class LogLine
    {
        public List<string> Parts = new List<string>();
        public List<Color> Colors = new List<Color>();
        public bool Inline;
        public int Chars;
    }

    class PingPane : Control
    {
        public static readonly Color ColorText = Color.FromArgb(215, 215, 215);
        public static readonly Color ColorOk = Color.FromArgb(120, 220, 140);
        public static readonly Color ColorFail = Color.FromArgb(255, 95, 95);
        public static readonly Color ColorInfo = Color.FromArgb(110, 170, 255);
        public static readonly Color ColorWarn = Color.FromArgb(255, 200, 80);
        static readonly Color HeaderOk = Color.FromArgb(30, 125, 55);
        static readonly Color HeaderFail = Color.FromArgb(190, 40, 40);
        static readonly Color HeaderIdle = Color.FromArgb(70, 70, 70);
        static readonly Color HeaderOff = Color.FromArgb(45, 45, 45);
        static readonly Color HeaderPaused = Color.FromArgb(150, 110, 20);
        static readonly Color HeaderUnstable = Color.FromArgb(190, 145, 0);
        static readonly Color BodyBack = Color.FromArgb(18, 18, 18);
        static readonly Color BorderColor = Color.FromArgb(50, 50, 50);

        readonly Font logFont;
        readonly Font headerFont;

        public int Index;                 // posicao fixa na configuracao (0 = primeiro host)
        public string Address = "";
        public string HostName = "";
        public bool Paused;
        public bool Active;
        public int LastState;             // 0 = sem dado, 1 = OK, 2 = falha, 3 = oscilou (respondendo, amarelo)
        public long Ok, Lost, ContLost, SumMs, MaxMs, LastMs;
        public long Blips;                // oscilacoes: ping perdido que a confirmacao recuperou
        public DateTime UnstableUntil;    // ate quando o painel fica amarelo
        public long MinMs = -1;
        public bool DownAlertHandled, DownMailSent, LogErrorShown;
        public List<DateTime> MailTimes = new List<DateTime>();
        public HostMonitor Monitor;
        public ViewMode View = ViewMode.Compact;

        readonly List<LogLine> lines = new List<LogLine>();
        int scroll;
        int charWidth;

        public event EventHandler HeaderDoubleClick;
        public event EventHandler DisableClick;

        Rectangle disableRect;            // botao "Desativar" (so aparece com ping vermelho)
        const string DisableText = "Desativar";

        public PingPane()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            logFont = new Font("Lucida Console", 8f);
            headerFont = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            BackColor = BodyBack;
            DoubleBuffered = true;
            Visible = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                logFont.Dispose();
                headerFont.Dispose();
            }
            base.Dispose(disposing);
        }

        public bool HasHost { get { return Address.Length > 0; } }

        int HeaderHeight { get { return headerFont.Height + Dpi.S(4); } }

        public void ResetStats()
        {
            Ok = 0; Lost = 0; ContLost = 0; SumMs = 0; MaxMs = 0; LastMs = 0; MinMs = -1;
            Blips = 0; UnstableUntil = DateTime.MinValue;
            if (LastState == 3) LastState = 1;
            DownAlertHandled = false; DownMailSent = false;
            Invalidate();
        }

        public void ClearText()
        {
            lines.Clear();
            scroll = 0;
            Invalidate();
        }

        public string GetText()
        {
            StringBuilder sb = new StringBuilder();
            foreach (LogLine l in lines)
            {
                foreach (string p in l.Parts) sb.Append(p);
                sb.Append("\r\n");
            }
            return sb.ToString();
        }

        public string Summary()
        {
            if (!HasHost) return "(sem host)";
            if (Paused) return "PAUSADO";
            long total = Ok + Lost;
            double pct = total > 0 ? Lost * 100.0 / total : 0.0;
            StringBuilder sb = new StringBuilder();
            sb.Append("OK ").Append(Ok).Append("   PL ").Append(Lost).Append(" (").Append(pct.ToString("0.0")).Append("%)   ");
            sb.Append(LastMs).Append(" ms");
            if (Blips > 0) sb.Append("   osc ").Append(Blips);
            if (View == ViewMode.Detailed && Ok > 0)
            {
                sb.Append("   mín/méd/máx ").Append(MinMs < 0 ? 0 : MinMs).Append("/").Append(SumMs / Ok).Append("/").Append(MaxMs);
            }
            return sb.ToString();
        }

        void Trim(int maxLines)
        {
            if (maxLines < 10) maxLines = 10;
            while (lines.Count > maxLines) lines.RemoveAt(0);
        }

        void AfterAppend(bool autoScroll)
        {
            if (autoScroll) scroll = 0;
            else if (scroll > 0) scroll++;
            if (scroll > lines.Count) scroll = lines.Count;
            if (Visible) Invalidate();
        }

        public void AppendLine(string text, Color color, bool autoScroll, int maxLines)
        {
            LogLine l = new LogLine();
            l.Parts.Add(text);
            l.Colors.Add(color);
            lines.Add(l);
            Trim(maxLines);
            AfterAppend(autoScroll);
        }

        // Visualizacao "Minima": varios simbolos na mesma linha
        public void AppendInline(string glyph, Color color, bool autoScroll, int maxLines)
        {
            LogLine last = lines.Count > 0 ? lines[lines.Count - 1] : null;
            if (last == null || !last.Inline || last.Chars >= 40)
            {
                last = new LogLine();
                last.Inline = true;
                lines.Add(last);
                Trim(maxLines);
            }
            last.Parts.Add(glyph);
            last.Colors.Add(color);
            last.Chars += glyph.Length;
            AfterAppend(autoScroll);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (!Focused) Focus();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int step = e.Delta > 0 ? 3 : -3;
            scroll += step;
            if (scroll < 0) scroll = 0;
            if (scroll > lines.Count) scroll = lines.Count;
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (disableRect.Contains(e.Location)) return;
            if (e.Y <= HeaderHeight && HeaderDoubleClick != null) HeaderDoubleClick(this, EventArgs.Empty);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && disableRect.Contains(e.Location) && DisableClick != null)
                DisableClick(this, EventArgs.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor c = disableRect.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            if (Cursor != c) Cursor = c;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int hh = HeaderHeight;
            Color hc;
            if (!HasHost) hc = HeaderOff;
            else if (Paused) hc = HeaderPaused;
            else if (LastState == 1) hc = HeaderOk;
            else if (LastState == 2) hc = HeaderFail;
            else if (LastState == 3) hc = HeaderUnstable;
            else hc = HeaderIdle;

            using (SolidBrush b = new SolidBrush(hc))
                g.FillRectangle(b, 0, 0, Width, hh);
            using (SolidBrush b = new SolidBrush(BodyBack))
                g.FillRectangle(b, 0, hh, Width, Height - hh);

            // cabecalho
            TextFormatFlags rf = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            TextFormatFlags lf = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            int pad = Dpi.S(4);
            string right = Summary();
            int rw = TextRenderer.MeasureText(g, right, headerFont, new Size(10000, hh), rf).Width;
            string left;
            if (!HasHost) left = (Index + 1).ToString("000") + " (sem host)";
            else left = (Index + 1).ToString("000") + " " + (HostName.Length > 0 ? HostName + " (" + Address + ")" : Address);
            int leftW = Width - rw - pad * 3;
            if (leftW < 20) leftW = 20;
            TextRenderer.DrawText(g, left, headerFont, new Rectangle(pad, 0, leftW, hh), Color.White, lf);
            TextRenderer.DrawText(g, right, headerFont, new Rectangle(Width - rw - pad, 0, rw, hh), Color.White, rf);

            // corpo
            int lh = logFont.Height;
            int bpad = Dpi.S(3);
            int rows = (Height - hh - bpad) / lh;
            if (rows > 0 && lines.Count > 0)
            {
                TextFormatFlags bf = TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
                if (charWidth == 0)
                {
                    charWidth = TextRenderer.MeasureText(g, "XXXXXXXXXX", logFont, new Size(10000, lh), bf).Width / 10;
                    if (charWidth < 1) charWidth = 6;
                }
                int end = lines.Count - scroll;
                if (end < 0) end = 0;
                int start = end - rows;
                if (start < 0) start = 0;
                int y = hh + bpad / 2;
                for (int i = start; i < end; i++)
                {
                    LogLine l = lines[i];
                    if (!l.Inline)
                    {
                        TextRenderer.DrawText(g, l.Parts[0], logFont, new Rectangle(bpad, y, Width - 2 * bpad, lh), l.Colors[0], bf);
                    }
                    else
                    {
                        int x = bpad;
                        for (int k = 0; k < l.Parts.Count; k++)
                        {
                            TextRenderer.DrawText(g, l.Parts[k], logFont, new Point(x, y), l.Colors[k], bf);
                            x += charWidth * l.Parts[k].Length;
                        }
                    }
                    y += lh;
                }
            }

            // botao "Desativar": canto superior direito do corpo, so com ping vermelho
            disableRect = Rectangle.Empty;
            if (HasHost && !Paused && LastState == 2)
            {
                int bw = TextRenderer.MeasureText(g, DisableText, headerFont, new Size(10000, hh), rf).Width + pad * 2;
                disableRect = new Rectangle(Width - bw - pad, hh + Dpi.S(3), bw, hh - 2);
                using (SolidBrush b = new SolidBrush(HeaderFail))
                    g.FillRectangle(b, disableRect);
                using (Pen p = new Pen(Color.FromArgb(255, 170, 170)))
                    g.DrawRectangle(p, disableRect.X, disableRect.Y, disableRect.Width - 1, disableRect.Height - 1);
                TextRenderer.DrawText(g, DisableText, headerFont, disableRect, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }

            using (Pen p = new Pen(BorderColor))
                g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }

    // ------------------------------------------------------------------
    // Painel de grade (posicionamento manual dos paineis)
    // ------------------------------------------------------------------
    class GridPanel : Panel
    {
        public GridPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(30, 30, 30);
        }
    }

    // ------------------------------------------------------------------
    // E-mail
    // ------------------------------------------------------------------
    static class Mailer
    {
        public static string Fill(string template, PingPane p, string status)
        {
            if (template == null) return "";
            return template
                .Replace("%HOST_ID%", (p.Index + 1).ToString())
                .Replace("%HOST_NAME%", p.HostName)
                .Replace("%HOST_IP%", p.Address)
                .Replace("%HOST_STATUS%", status)
                .Replace("%DATE%", DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"))
                .Replace("%PL_COUNT%", p.Lost.ToString());
        }

        public static void Send(Config c, string subject, string body)
        {
            string from = c.MailFrom.Length > 0 ? c.MailFrom : c.SmtpUser;
            using (SmtpClient client = new SmtpClient(c.SmtpServer, c.SmtpPort))
            {
                client.EnableSsl = c.SmtpSsl;
                client.Timeout = 20000;
                if (c.SmtpUser.Length > 0)
                    client.Credentials = new System.Net.NetworkCredential(c.SmtpUser, c.SmtpPass);
                using (MailMessage msg = new MailMessage())
                {
                    msg.From = new MailAddress(from);
                    msg.To.Add(c.MailTo.Replace(';', ','));
                    msg.Subject = subject;
                    msg.Body = body;
                    msg.SubjectEncoding = Encoding.UTF8;
                    msg.BodyEncoding = Encoding.UTF8;
                    client.Send(msg);
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // Janela principal
    // ------------------------------------------------------------------
    class MainForm : Form
    {
        string appDir, profilesDir, systemIniPath, profileName, profileDir;
        Config cfg;
        readonly List<PingPane> allPanes = new List<PingPane>();   // ordem da configuracao
        List<PingPane> order = new List<PingPane>();               // ordem de exibicao (dinamica)
        PingPane maximized;
        PingPane menuPane;
        bool running;
        bool updatingUi;
        bool saveErrorShown;
        DateTime runningSince;
        long lastBeepTicks;
        int beepBusy;
        int currentCols = 1;
        bool recheckRunning;
        bool recheckOnOpen = true;        // roda a verificacao uma vez ao abrir o programa
        string recheckInfo;

        readonly ConcurrentQueue<KeyValuePair<HostMonitor, PingResult>> pending = new ConcurrentQueue<KeyValuePair<HostMonitor, PingResult>>();
        readonly Dictionary<int, StreamWriter> writers = new Dictionary<int, StreamWriter>();
        readonly Dictionary<int, string> writerDates = new Dictionary<int, string>();

        GridPanel grid;
        ToolStrip bar;
        StatusStrip statusStrip;
        ToolStripStatusLabel lblState, lblUp, lblDown;
        ToolStripButton btnStart, btnStop, btnAutoScroll, btnSounds, btnTime, btnLog, btnRestore, btnResetOrder, btnPrev, btnNext;
        ToolStripLabel lblPage;
        int page;                         // pagina mostrada (0 = primeira)
        ToolStripDropDownButton ddView, ddProfile;
        ContextMenuStrip paneMenu;
        ToolStripMenuItem miMax, miPause;
        System.Windows.Forms.Timer uiTimer, flushTimer;

        public MainForm(string profileArg)
        {
            appDir = AppDomain.CurrentDomain.BaseDirectory;
            profilesDir = Path.Combine(appDir, "Profiles");
            systemIniPath = Path.Combine(appDir, "UltraPing.ini");
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }  // o mesmo icone do exe (assets\UltraPing.ico)
            try { Directory.CreateDirectory(profilesDir); } catch { }

            string name = profileArg;
            if (string.IsNullOrEmpty(name))
            {
                IniFile sys = new IniFile(systemIniPath, true);
                name = sys.Get("Program", "Profile", "Default");
            }
            if (!ValidProfileName(name)) name = "Default";

            Text = "UltraPing";
            BackColor = Color.FromArgb(30, 30, 30);
            MinimumSize = new Size(900, 500);
            Size = new Size(1280, 800);
            WindowState = FormWindowState.Maximized;

            grid = new GridPanel();
            grid.Dock = DockStyle.Fill;
            grid.Resize += delegate { LayoutPanes(); };

            BuildToolbar();
            BuildStatusBar();
            BuildPaneMenu();

            Controls.Add(grid);
            Controls.Add(statusStrip);
            Controls.Add(bar);

            LoadProfile(name);
            BuildPanes();
            RebuildProfileMenu();

            flushTimer = new System.Windows.Forms.Timer();
            flushTimer.Interval = 100;
            flushTimer.Tick += delegate { FlushResults(); };
            flushTimer.Start();

            uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 1000;
            uiTimer.Tick += delegate { UpdateStatus(); CheckRecheck(); };
            uiTimer.Start();

            FormClosing += MainForm_FormClosing;
            Shown += delegate
            {
                LayoutPanes();
                if (cfg.AutoStart) StartAll();
                UpdateToolbarState();
                UpdateStatus();
            };
            UpdateToolbarState();
            UpdateStatus();
        }

        // ---------------- perfis / configuracao ----------------

        static bool ValidProfileName(string n)
        {
            if (string.IsNullOrEmpty(n) || n.Trim().Length == 0) return false;
            if (n == "." || n == "..") return false;
            return n.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        string ConfigPath { get { return Path.Combine(profileDir, "config.ini"); } }
        string LogsDir { get { return Path.Combine(profileDir, "Logs"); } }

        void LoadProfile(string name)
        {
            profileName = name;
            profileDir = Path.Combine(profilesDir, name);
            try { Directory.CreateDirectory(profileDir); } catch { }
            if (!File.Exists(ConfigPath))
            {
                string beside = Path.Combine(appDir, "config.ini");
                try
                {
                    if (name == "Default" && File.Exists(beside)) File.Copy(beside, ConfigPath, false);
                    else Config.CreateDefault().Save(ConfigPath);
                }
                catch { }
            }
            cfg = Config.Load(ConfigPath);
            try
            {
                IniFile sys = new IniFile(systemIniPath, true);
                sys.Set("Program", "Profile", name);
                sys.Save();
            }
            catch { }
        }

        void SaveConfig()
        {
            try { cfg.Save(ConfigPath); }
            catch (Exception ex)
            {
                if (!saveErrorShown)
                {
                    saveErrorShown = true;
                    MessageBox.Show("Não foi possível salvar a configuração:\n" + ex.Message, "UltraPing");
                }
            }
        }

        void SwitchProfile(string name)
        {
            if (name == profileName) return;
            bool wasRunning = running;
            StopAll();
            CloseAllWriters();
            maximized = null;
            LoadProfile(name);
            BuildPanes();
            RebuildProfileMenu();
            LayoutPanes();
            UpdateToolbarState();
            if (wasRunning || cfg.AutoStart) StartAll();
            UpdateStatus();
        }

        void RebuildProfileMenu()
        {
            ddProfile.DropDownItems.Clear();
            try
            {
                string[] dirs = Directory.GetDirectories(profilesDir);
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                foreach (string d in dirs)
                {
                    string n = Path.GetFileName(d);
                    ToolStripMenuItem it = new ToolStripMenuItem(n);
                    it.Checked = (n == profileName);
                    string captured = n;
                    it.Click += delegate { SwitchProfile(captured); };
                    ddProfile.DropDownItems.Add(it);
                }
            }
            catch { }
            ddProfile.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem open = new ToolStripMenuItem("Abrir pasta de perfis");
            open.Click += delegate { try { Process.Start("explorer.exe", "\"" + profilesDir + "\""); } catch { } };
            ddProfile.DropDownItems.Add(open);
            ddProfile.Text = "Perfil: " + profileName;
        }

        // ---------------- construcao da interface ----------------

        ToolStripButton AddButton(string text, string tip, EventHandler click)
        {
            ToolStripButton b = new ToolStripButton(text);
            b.ToolTipText = tip;
            b.DisplayStyle = ToolStripItemDisplayStyle.Text;
            b.Click += click;
            bar.Items.Add(b);
            return b;
        }

        ToolStripButton AddToggle(string text, string tip, EventHandler click)
        {
            ToolStripButton b = AddButton(text, tip, click);
            b.CheckOnClick = true;
            return b;
        }

        void BuildToolbar()
        {
            bar = new ToolStrip();
            bar.GripStyle = ToolStripGripStyle.Hidden;
            bar.Dock = DockStyle.Top;

            btnStart = AddButton("▶ Iniciar", "Iniciar o ping em todas as janelas", delegate { StartAll(); });
            btnStop = AddButton("■ Parar", "Parar o ping em todas as janelas", delegate { StopAll(); });
            bar.Items.Add(new ToolStripSeparator());
            AddButton("⚙ Opções...", "Hosts, intervalo, visualização, e-mail", delegate { OpenOptions(); });
            AddButton("Limpar tela", "Limpar o texto de todas as janelas", delegate { foreach (PingPane p in allPanes) p.ClearText(); });
            AddButton("Zerar contadores", "Zerar OK / PL de todas as janelas", delegate { foreach (PingPane p in allPanes) p.ResetStats(); });
            bar.Items.Add(new ToolStripSeparator());

            btnAutoScroll = AddToggle("Auto-rolagem", "Rolar automaticamente para o último resultado", delegate
            {
                if (updatingUi) return;
                cfg.AutoScroll = btnAutoScroll.Checked; SaveConfig();
            });
            btnSounds = AddToggle("Som", "Bipe quando um ping falhar", delegate
            {
                if (updatingUi) return;
                cfg.PlaySounds = btnSounds.Checked; SaveConfig();
            });
            btnTime = AddToggle("Hora", "Mostrar a hora em cada resultado", delegate
            {
                if (updatingUi) return;
                cfg.ShowTime = btnTime.Checked; SaveConfig();
            });
            btnLog = AddToggle("Log CSV", "Gravar todos os resultados em arquivos CSV", delegate
            {
                if (updatingUi) return;
                cfg.LogToFiles = btnLog.Checked; SaveConfig();
                if (!cfg.LogToFiles) CloseAllWriters();
            });

            ddView = new ToolStripDropDownButton("Visualização");
            ddView.ToolTipText = "Tipo de visualização dos resultados";
            foreach (ViewMode vm in new ViewMode[] { ViewMode.Basic, ViewMode.Compact, ViewMode.Minimal, ViewMode.Detailed })
            {
                ViewMode captured = vm;
                ToolStripMenuItem it = new ToolStripMenuItem(Config.ViewLabel(vm));
                it.Click += delegate
                {
                    cfg.View = captured; SaveConfig();
                    foreach (PingPane p in allPanes) { p.View = captured; p.Invalidate(); }
                    UpdateToolbarState();
                };
                ddView.DropDownItems.Add(it);
            }
            bar.Items.Add(ddView);
            bar.Items.Add(new ToolStripSeparator());

            btnRestore = AddButton("Restaurar janelas", "Voltar à grade com todas as janelas", delegate { maximized = null; LayoutPanes(); UpdateToolbarState(); });
            btnResetOrder = AddButton("Ordem original", "Voltar os painéis para a ordem do arquivo de configuração", delegate
            {
                order = new List<PingPane>(allPanes);
                LayoutPanes();
            });
            AddButton("Pasta de logs", "Abrir a pasta de logs do perfil", delegate { OpenLogsFolder(); });
            bar.Items.Add(new ToolStripSeparator());
            btnPrev = AddButton("◀", "Página anterior (PageUp)", delegate { GoToPage(page - 1); });
            lblPage = new ToolStripLabel("Página 1/1");
            lblPage.ToolTipText = "Quando um host cai, a tela volta para a página 1";
            bar.Items.Add(lblPage);
            btnNext = AddButton("▶", "Próxima página (PageDown)", delegate { GoToPage(page + 1); });

            ddProfile = new ToolStripDropDownButton("Perfil");
            ddProfile.Alignment = ToolStripItemAlignment.Right;
            ddProfile.ToolTipText = "Trocar de perfil";
            bar.Items.Add(ddProfile);
        }

        void BuildStatusBar()
        {
            statusStrip = new StatusStrip();
            lblState = new ToolStripStatusLabel();
            lblState.Spring = true;
            lblState.TextAlign = ContentAlignment.MiddleLeft;
            lblUp = new ToolStripStatusLabel();
            lblUp.ForeColor = Color.FromArgb(20, 120, 40);
            lblUp.Font = new Font(statusStrip.Font, FontStyle.Bold);
            lblDown = new ToolStripStatusLabel();
            lblDown.ForeColor = Color.FromArgb(190, 40, 40);
            lblDown.Font = new Font(statusStrip.Font, FontStyle.Bold);
            ToolStripStatusLabel hint = new ToolStripStatusLabel("Duplo clique no título = maximizar  |  Botão direito = mais opções");
            statusStrip.Items.Add(lblState);
            statusStrip.Items.Add(lblUp);
            statusStrip.Items.Add(lblDown);
            statusStrip.Items.Add(hint);
        }

        void BuildPaneMenu()
        {
            paneMenu = new ContextMenuStrip();
            miMax = new ToolStripMenuItem("Maximizar esta janela");
            miMax.Click += delegate { if (menuPane != null) ToggleMaximize(menuPane); };
            miPause = new ToolStripMenuItem("Pausar este host");
            miPause.Click += delegate { if (menuPane != null) TogglePause(menuPane); };
            ToolStripMenuItem miCopy = new ToolStripMenuItem("Copiar texto da janela");
            miCopy.Click += delegate
            {
                if (menuPane == null) return;
                string t = menuPane.GetText();
                if (t.Length > 0) { try { Clipboard.SetText(t); } catch { } }
            };
            ToolStripMenuItem miClear = new ToolStripMenuItem("Limpar janela");
            miClear.Click += delegate { if (menuPane != null) menuPane.ClearText(); };
            ToolStripMenuItem miAddr = new ToolStripMenuItem("Copiar endereço do host");
            miAddr.Click += delegate { if (menuPane != null && menuPane.HasHost) { try { Clipboard.SetText(menuPane.Address); } catch { } } };
            ToolStripMenuItem miCmd = new ToolStripMenuItem("Abrir \"ping -t\" no prompt");
            miCmd.Click += delegate { if (menuPane != null) OpenCmdPing(menuPane); };
            paneMenu.Items.Add(miMax);
            paneMenu.Items.Add(miPause);
            paneMenu.Items.Add(new ToolStripSeparator());
            paneMenu.Items.Add(miCopy);
            paneMenu.Items.Add(miClear);
            paneMenu.Items.Add(miAddr);
            paneMenu.Items.Add(miCmd);
            paneMenu.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                menuPane = paneMenu.SourceControl as PingPane;
                if (menuPane == null) { e.Cancel = true; return; }
                miMax.Text = (maximized == menuPane) ? "Restaurar todas as janelas" : "Maximizar esta janela";
                miPause.Text = menuPane.Paused ? "Retomar este host" : "Pausar este host";
                miPause.Enabled = menuPane.HasHost;
            };
        }

        // ---------------- paineis ----------------

        void BuildPanes()
        {
            foreach (PingPane p in allPanes)
            {
                if (p.Monitor != null) p.Monitor.Stop();
                grid.Controls.Remove(p);
                p.Dispose();
            }
            allPanes.Clear();
            for (int i = 0; i < cfg.Hosts.Count; i++)
            {
                HostEntry h = cfg.Hosts[i];
                if (h.Address.Length == 0 || !h.Enabled) continue;   // desativados nao aparecem nem sao pingados
                allPanes.Add(CreatePane(i, h));
            }
            order = new List<PingPane>(allPanes);
            maximized = null;
            page = 0;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.PageDown) { GoToPage(page + 1); return true; }
            if (keyData == Keys.PageUp) { GoToPage(page - 1); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        PingPane CreatePane(int index, HostEntry h)
        {
            PingPane p = new PingPane();
            p.Index = index;
            p.Address = h.Address;
            p.HostName = h.Name;
            p.View = cfg.View;
            p.ContextMenuStrip = paneMenu;
            p.HeaderDoubleClick += delegate(object s, EventArgs e) { ToggleMaximize((PingPane)s); };
            p.DisableClick += delegate(object s, EventArgs e) { DisableHost((PingPane)s); };
            grid.Controls.Add(p);
            return p;
        }

        // botao "Desativar" do painel vermelho: grava Enabled=False e tira o painel da tela.
        // A verificacao diaria reativa o host sozinha se ele voltar a responder.
        void DisableHost(PingPane p)
        {
            if (p.Index >= cfg.Hosts.Count) return;
            HostEntry h = cfg.Hosts[p.Index];
            if (h.Address != p.Address || !h.Enabled) return;
            string nome = h.Name.Length > 0 ? h.Name + " (" + h.Address + ")" : h.Address;
            string volta = cfg.RecheckDisabled
                ? "\n\nEle será testado 1 vez por dia às " + cfg.RecheckTime + " e volta sozinho se responder."
                : "\n\nA verificação diária está desligada: para voltar, reative nas Opções.";
            if (MessageBox.Show(this, "Desativar " + nome + "?" + volta, "UltraPing", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            h.Enabled = false;
            SaveConfig();
            StopMonitor(p);
            CloseWriter(p.Index);
            allPanes.Remove(p);
            order.Remove(p);
            if (maximized == p) maximized = null;
            if (menuPane == p) menuPane = null;
            grid.Controls.Remove(p);
            p.Dispose();
            LayoutPanes();
            UpdateToolbarState();
            UpdateStatus();
        }

        // ---------------- paginas: pagina 1 = primeiros WindowCount da fila, pagina 2 = proximos ... ----------------

        int PageCount()
        {
            return Math.Max(1, (order.Count + cfg.WindowCount - 1) / cfg.WindowCount);
        }

        void GoToPage(int p)
        {
            if (p < 0) p = 0;
            if (p > PageCount() - 1) p = PageCount() - 1;
            if (p == page && maximized == null) return;
            page = p;
            maximized = null;
            LayoutPanes();
            UpdateToolbarState();
            UpdateStatus();
        }

        void UpdatePageUi()
        {
            if (lblPage == null) return;
            int pc = PageCount();
            lblPage.Text = "Página " + (page + 1) + "/" + pc;
            btnPrev.Enabled = page > 0;
            btnNext.Enabled = page < pc - 1;
        }

        List<PingPane> VisiblePanes()
        {
            if (page > PageCount() - 1) page = PageCount() - 1;
            int start = page * cfg.WindowCount;
            int n = Math.Max(0, Math.Min(cfg.WindowCount, order.Count - start));
            return order.GetRange(start, n);
        }

        int ComputeColumns(int n)
        {
            if (n <= 1) return 1;
            if (cfg.Columns > 0) return Math.Min(cfg.Columns, n);
            int w = grid.ClientSize.Width;
            int h = grid.ClientSize.Height;
            if (w <= 0 || h <= 0) return (int)Math.Ceiling(Math.Sqrt(n));
            int bestCols = 1;
            double best = -1.0;
            for (int cols = 1; cols <= n; cols++)
            {
                int rows = (n + cols - 1) / cols;
                // paineis largos (proporcao ~3:1) para o nome do host caber no titulo
                double size = Math.Min((w / (double)cols) / 3.0, h / (double)rows);
                if (size > best + 0.01) { best = size; bestCols = cols; }
            }
            return bestCols;
        }

        void LayoutPanes()
        {
            if (grid == null || cfg == null) return;
            List<PingPane> show;
            if (maximized != null && allPanes.Contains(maximized))
            {
                show = new List<PingPane>();
                show.Add(maximized);
            }
            else
            {
                maximized = null;
                show = VisiblePanes();
            }
            int n = show.Count;
            // a grade tem sempre o tamanho de uma pagina cheia (a ultima pagina nao estica os paineis)
            int slots = (maximized != null) ? 1 : Math.Max(1, Math.Min(cfg.WindowCount, order.Count));
            int cols = (maximized != null) ? 1 : ComputeColumns(slots);
            int rows = Math.Max(1, (slots + cols - 1) / cols);
            int W = grid.ClientSize.Width;
            int H = grid.ClientSize.Height;
            HashSet<PingPane> shown = new HashSet<PingPane>(show);
            grid.SuspendLayout();
            foreach (PingPane p in allPanes)
            {
                if (!shown.Contains(p) && p.Visible) p.Visible = false;
            }
            for (int i = 0; i < n; i++)
            {
                int c = i % cols;
                int r = i / cols;
                int x1 = c * W / cols, x2 = (c + 1) * W / cols;
                int y1 = r * H / rows, y2 = (r + 1) * H / rows;
                PingPane p = show[i];
                p.SetBounds(x1, y1, x2 - x1, y2 - y1);
                if (!p.Visible) p.Visible = true;
            }
            grid.ResumeLayout(false);
            currentCols = cols;
            UpdatePageUi();
        }

        void ToggleMaximize(PingPane p)
        {
            maximized = (maximized == p) ? null : p;
            LayoutPanes();
            UpdateToolbarState();
        }

        // ---------------- start / stop ----------------

        void StartAll()
        {
            if (running) return;
            running = true;
            runningSince = DateTime.Now;
            int total = allPanes.Count;
            for (int i = 0; i < total; i++) StartMonitor(allPanes[i], i, total);
            UpdateToolbarState();
            UpdateStatus();
        }

        void StopAll()
        {
            if (!running) return;
            running = false;
            foreach (PingPane p in allPanes)
            {
                StopMonitor(p);
                if (p.HasHost) Msg(p, "## Ping parado ##   ", PingPane.ColorInfo);
            }
            KeyValuePair<HostMonitor, PingResult> kv;
            while (pending.TryDequeue(out kv)) { }
            CloseAllWriters();
            UpdateToolbarState();
            UpdateStatus();
        }

        void StartMonitor(PingPane p, int pos, int total)
        {
            if (!p.HasHost || p.Paused) return;
            if (p.Monitor != null) p.Monitor.Stop();
            int delay = total > 1 ? (int)((long)cfg.PingInterval * pos / total) : 0;
            HostMonitor m = new HostMonitor(p, p.Address, cfg.PingInterval, cfg.PingTimeout, delay, cfg.ConfirmPings, PostResult);
            p.Monitor = m;
            p.Active = true;
            p.LastState = 0;
            Msg(p, "## Ping iniciado ##", PingPane.ColorInfo);
            m.Start();
        }

        void StopMonitor(PingPane p)
        {
            if (p.Monitor != null) { p.Monitor.Stop(); p.Monitor = null; }
            p.Active = false;
        }

        void TogglePause(PingPane p)
        {
            if (!p.HasHost) return;
            if (p.Paused)
            {
                p.Paused = false;
                if (running) StartMonitor(p, 0, 1);
                Msg(p, "## Host retomado ##", PingPane.ColorInfo);
            }
            else
            {
                p.Paused = true;
                StopMonitor(p);
                Msg(p, "## Host pausado ##", PingPane.ColorInfo);
            }
            p.Invalidate();
        }

        // ---------------- verificacao diaria dos desativados ----------------

        class RecheckItem
        {
            public int Index;
            public string Address;
            public bool Alive;
        }

        void CheckRecheck()
        {
            if (recheckRunning || cfg == null || !cfg.RecheckDisabled) return;
            TimeSpan at;
            if (!Config.TryParseTime(cfg.RecheckTime, out at)) return;
            DateTime now = DateTime.Now;
            string today = now.ToString("yyyy-MM-dd");
            if (recheckOnOpen)
            {
                // ao abrir: sempre testa; se ja passou do horario, conta como a verificacao do dia
                recheckOnOpen = false;
                if (now.TimeOfDay >= at && cfg.RecheckLastRun != today)
                {
                    cfg.RecheckLastRun = today;
                    SaveConfig();
                }
                StartRecheck();
                return;
            }
            if (cfg.RecheckLastRun == today || now.TimeOfDay < at) return;   // ja rodou hoje ou ainda nao deu a hora
            cfg.RecheckLastRun = today;
            SaveConfig();
            StartRecheck();
        }

        void StartRecheck()
        {
            List<RecheckItem> items = new List<RecheckItem>();
            for (int i = 0; i < cfg.Hosts.Count; i++)
            {
                HostEntry h = cfg.Hosts[i];
                if (h.Enabled || h.Address.Length == 0) continue;
                RecheckItem it = new RecheckItem();
                it.Index = i;
                it.Address = h.Address;
                items.Add(it);
            }
            if (items.Count == 0)
            {
                recheckInfo = "verificação diária " + DateTime.Now.ToString("dd/MM HH:mm") + ": nenhum host desativado";
                return;
            }
            recheckInfo = "verificação diária: testando " + items.Count + " desativado(s)...";
            recheckRunning = true;
            int timeout = cfg.PingTimeout;
            string prof = profileName;
            Thread t = new Thread(delegate()
            {
                ParallelOptions po = new ParallelOptions();
                po.MaxDegreeOfParallelism = 16;
                Parallel.ForEach(items, po, delegate(RecheckItem it)
                {
                    using (Ping ping = new Ping())
                    {
                        for (int k = 0; k < 3 && !it.Alive; k++)
                        {
                            try { it.Alive = ping.Send(it.Address, timeout).Status == IPStatus.Success; }
                            catch { }
                        }
                    }
                });
                try { BeginInvoke(new MethodInvoker(delegate { FinishRecheck(items, prof); })); }
                catch { }
            });
            t.IsBackground = true;
            t.Name = "Verificacao diaria";
            t.Start();
        }

        void FinishRecheck(List<RecheckItem> items, string prof)
        {
            recheckRunning = false;
            if (prof != profileName) return;   // trocou de perfil durante o teste
            int revived = 0;
            foreach (RecheckItem it in items)
            {
                if (!it.Alive || it.Index >= cfg.Hosts.Count) continue;
                HostEntry h = cfg.Hosts[it.Index];
                if (h.Enabled || h.Address != it.Address) continue;   // config mudou nesse meio tempo
                h.Enabled = true;
                revived++;
                // entra no fim da fila de exibicao; so sobe se falhar
                PingPane p = CreatePane(it.Index, h);
                int pos = 0;
                while (pos < allPanes.Count && allPanes[pos].Index < it.Index) pos++;
                allPanes.Insert(pos, p);
                order.Add(p);
                Msg(p, "## Reativado pela verificação diária ##", PingPane.ColorInfo);
                if (running) StartMonitor(p, 0, 1);
            }
            if (revived > 0)
            {
                SaveConfig();
                LayoutPanes();
            }
            recheckInfo = "verificação diária " + DateTime.Now.ToString("dd/MM HH:mm") + ": " + revived + " de " + items.Count + " reativado(s)";
            UpdateStatus();
        }

        // ---------------- resultados ----------------

        void PostResult(HostMonitor m, PingResult r)
        {
            pending.Enqueue(new KeyValuePair<HostMonitor, PingResult>(m, r));
        }

        void FlushResults()
        {
            bool changed = false;
            int n = 0;
            KeyValuePair<HostMonitor, PingResult> kv;
            while (n < 3000 && pending.TryDequeue(out kv))
            {
                n++;
                if (OnResult(kv.Key, kv.Value)) changed = true;
            }
            if (changed) LayoutPanes();
        }

        static string ShortFail(PingResult r)
        {
            if (r.Status == -1) return "erro";
            IPStatus s = (IPStatus)r.Status;
            if (s == IPStatus.TimedOut) return "timeout";
            if (s == IPStatus.DestinationHostUnreachable) return "host inacessível";
            if (s == IPStatus.DestinationNetworkUnreachable) return "rede inacessível";
            return "erro";
        }

        static string FailText(PingResult r, PingPane p)
        {
            if (r.Status == -1) return "Falha: " + r.Error;
            IPStatus s = (IPStatus)r.Status;
            if (s == IPStatus.TimedOut) return "Esgotado o tempo limite do pedido.";
            if (s == IPStatus.DestinationHostUnreachable)
                return "Host de destino inacessível." + (r.ReplyAddress != null ? " (resposta de " + r.ReplyAddress + ")" : "");
            if (s == IPStatus.DestinationNetworkUnreachable) return "Rede de destino inacessível.";
            if (s == IPStatus.TtlExpired) return "TTL expirado em trânsito.";
            return "Falha: " + s.ToString();
        }

        // retorna true se a ordem dos paineis mudou
        bool OnResult(HostMonitor m, PingResult r)
        {
            PingPane p = m.Pane;
            if (!running || p == null || p.Monitor != m) return false;
            bool ok = r.Success;
            if (ok)
            {
                p.Ok++;
                p.LastMs = r.Ms;
                p.SumMs += r.Ms;
                if (p.MinMs < 0 || r.Ms < p.MinMs) p.MinMs = r.Ms;
                if (r.Ms > p.MaxMs) p.MaxMs = r.Ms;
                p.ContLost = 0;
            }
            else
            {
                p.Lost++;
                p.ContLost++;
            }
            // oscilacao: o 1o ping se perdeu mas a confirmacao respondeu -> amarelo, sem alerta
            if (ok && r.Blip)
            {
                p.Blips++;
                p.UnstableUntil = DateTime.Now.AddMinutes(cfg.UnstableMinutes);
            }
            if (!ok) p.LastState = 2;
            else p.LastState = DateTime.Now < p.UnstableUntil ? 3 : 1;

            string time = cfg.ShowTime ? r.Time.ToString("HH:mm:ss") + "  " : "";
            string addr = r.ReplyAddress != null ? r.ReplyAddress : p.Address;
            string ms = r.Ms >= 1 ? "=" + r.Ms + "ms" : "<1ms";
            string osc = r.Blip ? "OSCILOU (1 ping perdido, confirmado) " : "";
            Color okColor = r.Blip ? PingPane.ColorWarn : PingPane.ColorText;
            string detail;
            switch (cfg.View)
            {
                case ViewMode.Minimal:
                    p.AppendInline(ok ? (r.Blip ? "~" : "!") : "X", ok ? (r.Blip ? PingPane.ColorWarn : PingPane.ColorOk) : PingPane.ColorFail, cfg.AutoScroll, cfg.MaxLines);
                    detail = ok ? osc + "Resposta de " + addr : FailText(r, p);
                    break;
                case ViewMode.Compact:
                    detail = ok ? (r.Blip ? "OSCILOU, confirmado " + r.Ms + " ms" : r.Ms + " ms") : "FALHA (" + ShortFail(r) + ")";
                    p.AppendLine(time + detail, ok ? okColor : PingPane.ColorFail, cfg.AutoScroll, cfg.MaxLines);
                    break;
                case ViewMode.Basic:
                    detail = ok ? osc + "Resposta de " + addr + ": tempo" + ms : FailText(r, p);
                    p.AppendLine(time + detail, ok ? okColor : PingPane.ColorFail, cfg.AutoScroll, cfg.MaxLines);
                    break;
                default:
                    detail = ok ? osc + "Resposta de " + addr + ": bytes=" + r.Bytes + " tempo" + ms + (r.Ttl > 0 ? " TTL=" + r.Ttl : "") : FailText(r, p);
                    p.AppendLine(time + detail, ok ? okColor : PingPane.ColorFail, cfg.AutoScroll, cfg.MaxLines);
                    break;
            }

            if (!ok && cfg.PlaySounds) Beep();
            WriteLog(p, r, detail);
            CheckMail(p, ok);

            // ---- logica dinamica: falhou -> sobe para a primeira fileira ----
            bool changed = false;
            if (!ok && p.ContLost == cfg.PromoteAfter)
            {
                if (order.Count > 0 && order[0] != p)
                {
                    order.Remove(p);
                    order.Insert(0, p);
                    changed = true;
                }
                if (page != 0) { page = 0; changed = true; }   // queda: volta para a pagina 1
            }
            if (p.Visible) p.Invalidate();
            return changed;
        }

        void Msg(PingPane p, string text, Color color)
        {
            string time = (cfg != null && cfg.ShowTime) ? DateTime.Now.ToString("HH:mm:ss") + "  " : "";
            p.AppendLine(time + text, color, cfg.AutoScroll, cfg.MaxLines);
        }

        void Beep()
        {
            long now = DateTime.UtcNow.Ticks;
            if (now - lastBeepTicks < 20000000L) return;   // no maximo 1 bipe a cada 2 s
            lastBeepTicks = now;
            // mesmo som do 33Ping original: tom de 1000 Hz por 150 ms, tocado fora da thread da tela
            if (Interlocked.CompareExchange(ref beepBusy, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Console.Beep(1000, 150); }
                catch { }
                finally { Interlocked.Exchange(ref beepBusy, 0); }
            });
        }

        // ---------------- log CSV ----------------

        static string Csv(string s)
        {
            if (s == null) return "";
            return s.Replace(';', ',').Replace("\r", " ").Replace("\n", " ");
        }

        void WriteLog(PingPane p, PingResult r, string detail)
        {
            if (!cfg.LogToFiles) return;
            try
            {
                string date = r.Time.ToString("yyyy-MM-dd");
                StreamWriter w;
                string wd;
                if (!writers.TryGetValue(p.Index, out w) || !writerDates.TryGetValue(p.Index, out wd) || wd != date)
                {
                    CloseWriter(p.Index);
                    string dir = Path.Combine(LogsDir, date);
                    Directory.CreateDirectory(dir);
                    string file = Path.Combine(dir, "Janela" + (p.Index + 1).ToString("000") + ".csv");
                    bool isNew = !File.Exists(file);
                    w = new StreamWriter(new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(true));
                    w.AutoFlush = true;
                    if (isNew) w.WriteLine("DataHora;Janela;Nome;Host;Status;Tempo_ms;TTL;Detalhe");
                    writers[p.Index] = w;
                    writerDates[p.Index] = date;
                }
                w.WriteLine(r.Time.ToString("yyyy-MM-dd HH:mm:ss") + ";" + (p.Index + 1) + ";" + Csv(p.HostName) + ";" + Csv(p.Address) + ";" +
                            (r.Success ? (r.Blip ? "OSCILOU" : "OK") : "FALHA") + ";" + (r.Success ? r.Ms.ToString() : "") + ";" + (r.Ttl > 0 ? r.Ttl.ToString() : "") + ";" + Csv(detail));
            }
            catch (Exception ex)
            {
                if (!p.LogErrorShown)
                {
                    p.LogErrorShown = true;
                    Msg(p, "## Erro ao gravar log: " + ex.Message + " ##", PingPane.ColorWarn);
                }
            }
        }

        void CloseWriter(int idx)
        {
            StreamWriter w;
            if (writers.TryGetValue(idx, out w))
            {
                try { w.Dispose(); } catch { }
                writers.Remove(idx);
                writerDates.Remove(idx);
            }
        }

        void CloseAllWriters()
        {
            foreach (int k in new List<int>(writers.Keys)) CloseWriter(k);
        }

        void OpenLogsFolder()
        {
            try
            {
                Directory.CreateDirectory(LogsDir);
                Process.Start("explorer.exe", "\"" + LogsDir + "\"");
            }
            catch { }
        }

        void OpenCmdPing(PingPane p)
        {
            if (!p.HasHost) return;
            try { Process.Start("cmd.exe", "/k ping -t " + p.Address); } catch { }
        }

        // ---------------- e-mail ----------------

        string MailLimitReason(PingPane p)
        {
            DateTime now = DateTime.Now;
            p.MailTimes.RemoveAll(delegate(DateTime t) { return (now - t).TotalHours >= 1.0; });
            if (cfg.MaxPerMinute > 0)
            {
                int inMin = 0;
                foreach (DateTime t in p.MailTimes) if ((now - t).TotalMinutes < 1.0) inMin++;
                if (inMin >= cfg.MaxPerMinute) return "limite de e-mails por minuto atingido";
            }
            if (cfg.MaxPerHour > 0 && p.MailTimes.Count >= cfg.MaxPerHour) return "limite de e-mails por hora atingido";
            p.MailTimes.Add(now);
            return null;
        }

        void CheckMail(PingPane p, bool ok)
        {
            if (!cfg.MailAlerts) return;
            if (!ok)
            {
                if (p.ContLost >= cfg.SendAfter && !p.DownAlertHandled)
                {
                    p.DownAlertHandled = true;
                    string why = MailLimitReason(p);
                    if (why == null) { SendAlert(p, "queda"); p.DownMailSent = true; }
                    else Msg(p, "## Host fora, e-mail NÃO enviado: " + why + " ##", PingPane.ColorWarn);
                }
            }
            else
            {
                if (p.DownMailSent)
                {
                    string why = MailLimitReason(p);
                    if (why == null) SendAlert(p, "recuperação");
                }
                p.DownAlertHandled = false;
                p.DownMailSent = false;
            }
        }

        void SendAlert(PingPane p, string status)
        {
            Config c = cfg;
            string subject = Mailer.Fill(c.MailSubject, p, status);
            string body = Mailer.Fill(c.MailBody, p, status);
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Mailer.Send(c, subject, body); }
                catch (Exception ex)
                {
                    string msg = ex.Message;
                    try
                    {
                        BeginInvoke(new MethodInvoker(delegate { Msg(p, "## Erro ao enviar e-mail: " + msg + " ##", PingPane.ColorWarn); }));
                    }
                    catch { }
                }
            });
        }

        // ---------------- opcoes ----------------

        void OpenOptions()
        {
            Config edit = cfg.Clone();
            using (OptionsForm f = new OptionsForm(edit))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
            }
            bool wasRunning = running;
            StopAll();
            edit.RecheckLastRun = cfg.RecheckLastRun;   // a verificacao pode ter rodado com a janela aberta
            cfg = edit;
            SaveConfig();
            BuildPanes();
            LayoutPanes();
            UpdateToolbarState();
            if (wasRunning) StartAll();
            UpdateStatus();
        }

        // ---------------- barra e status ----------------

        void UpdateToolbarState()
        {
            updatingUi = true;
            btnStart.Enabled = !running;
            btnStop.Enabled = running;
            btnAutoScroll.Checked = cfg.AutoScroll;
            btnSounds.Checked = cfg.PlaySounds;
            btnTime.Checked = cfg.ShowTime;
            btnLog.Checked = cfg.LogToFiles;
            btnRestore.Enabled = maximized != null;
            ddView.Text = "Visualização: " + Config.ViewLabel(cfg.View);
            ddProfile.Text = "Perfil: " + profileName;
            foreach (ToolStripItem it in ddView.DropDownItems)
            {
                ToolStripMenuItem mi = it as ToolStripMenuItem;
                if (mi != null) mi.Checked = (mi.Text == Config.ViewLabel(cfg.View));
            }
            updatingUi = false;
        }

        void UpdateStatus()
        {
            int up = 0, down = 0, hiddenDown = 0, unstable = 0;
            HashSet<PingPane> vis = new HashSet<PingPane>(VisiblePanes());
            foreach (PingPane p in allPanes)
            {
                if (p.LastState == 1) up++;
                else if (p.LastState == 3) { up++; unstable++; }
                else if (p.LastState == 2)
                {
                    down++;
                    if (!vis.Contains(p)) hiddenDown++;
                }
            }
            int shownCount = Math.Min(cfg.WindowCount, allPanes.Count);
            string info = allPanes.Count + " host(s) ativos, " + shownCount + " janelas por página, página " + (page + 1) + " de " + PageCount();
            if (hiddenDown > 0) info += ", " + hiddenDown + " com falha fora da tela";
            if (recheckInfo != null) info += "  |  " + recheckInfo;
            if (running) lblState.Text = "Monitorando desde " + runningSince.ToString("dd/MM HH:mm:ss") + "  —  " + info;
            else lblState.Text = "Parado  —  " + info;
            lblUp.Text = "Respondendo: " + up + (unstable > 0 ? " (" + unstable + " oscilando)" : "");
            lblDown.Text = "Falhando: " + down;
            string title = "UltraPing - " + profileName;
            if (down > 0) title += "   [" + down + " FALHANDO]";
            if (Text != title) Text = title;
        }

        void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            uiTimer.Stop();
            flushTimer.Stop();
            running = false;
            foreach (PingPane p in allPanes) StopMonitor(p);
            CloseAllWriters();
        }
    }

    // ------------------------------------------------------------------
    // Janela de opcoes (simplificada: hosts editados como texto)
    // ------------------------------------------------------------------
    class OptionsForm : Form
    {
        readonly Config cfg;
        NumericUpDown numInterval, numTimeout, numWindows, numColumns, numMaxLines, numPromote, numConfirm, numUnstable;
        CheckBox chkAuto, chkScroll, chkSound, chkTime, chkLog, chkRecheck;
        TextBox txtRecheckTime;
        ComboBox cmbView;
        CheckBox chkMail, chkSsl;
        NumericUpDown numSendAfter, numPerMin, numPerHour, numPort;
        TextBox txtServer, txtUser, txtPass, txtFrom, txtTo, txtSubject, txtBody;
        DataGridView gridHosts;

        const int ColNum = 0, ColOn = 1, ColIp = 2, ColName = 3;

        public OptionsForm(Config c)
        {
            cfg = c;
            Text = "Opções";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            ClientSize = new Size(760, 600);

            TabControl tabs = new TabControl();
            tabs.SetBounds(8, 8, 744, 542);
            tabs.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            TabPage tHosts = new TabPage("Hosts");
            TabPage tGeral = new TabPage("Geral");
            TabPage tMail = new TabPage("E-mail");
            tabs.TabPages.Add(tHosts);
            tabs.TabPages.Add(tGeral);
            tabs.TabPages.Add(tMail);
            Controls.Add(tabs);

            // ---- Geral ----
            int y = 16;
            numInterval = AddNum(tGeral, "Intervalo entre pings (ms)", ref y, 100, 600000, c.PingInterval);
            numTimeout = AddNum(tGeral, "Timeout do ping (ms)", ref y, 100, 60000, c.PingTimeout);
            numConfirm = AddNum(tGeral, "Pings de confirmação antes de alertar (0 = desligado)", ref y, 0, 5, c.ConfirmPings);
            numUnstable = AddNum(tGeral, "Minutos em amarelo depois de uma oscilação", ref y, 0, 1440, c.UnstableMinutes);
            numColumns = AddNum(tGeral, "Colunas (0 = automático)", ref y, 0, 32, c.Columns);
            numPromote = AddNum(tGeral, "Falhas seguidas para subir ao topo", ref y, 1, 1000, c.PromoteAfter);
            numMaxLines = AddNum(tGeral, "Máximo de linhas por janela", ref y, 50, 20000, c.MaxLines);
            Label lv = new Label();
            lv.Text = "Visualização dos resultados";
            lv.SetBounds(16, y + 3, 360, 20);
            tGeral.Controls.Add(lv);
            cmbView = new ComboBox();
            cmbView.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbView.Items.Add("Básica");
            cmbView.Items.Add("Compacta");
            cmbView.Items.Add("Mínima");
            cmbView.Items.Add("Detalhada");
            cmbView.SelectedIndex = (int)c.View - 1;
            cmbView.SetBounds(400, y, 150, 24);
            tGeral.Controls.Add(cmbView);
            y += 34;
            chkAuto = AddChk(tGeral, "Iniciar o ping automaticamente ao abrir", ref y, c.AutoStart);
            chkScroll = AddChk(tGeral, "Auto-rolagem", ref y, c.AutoScroll);
            chkSound = AddChk(tGeral, "Som quando um ping falhar", ref y, c.PlaySounds);
            chkTime = AddChk(tGeral, "Mostrar a hora em cada resultado", ref y, c.ShowTime);
            chkLog = AddChk(tGeral, "Gravar log CSV", ref y, c.LogToFiles);
            chkRecheck = AddChk(tGeral, "Testar os hosts desativados ao abrir e 1 vez por dia, e reativar os que responderem", ref y, c.RecheckDisabled);
            txtRecheckTime = AddTxt(tGeral, "Horário do teste (HH:mm)", ref y, c.RecheckTime, false);

            // ---- E-mail ----
            y = 16;
            chkMail = AddChk(tMail, "Enviar alertas por e-mail", ref y, c.MailAlerts);
            numSendAfter = AddNum(tMail, "Enviar após N falhas seguidas", ref y, 1, 100000, c.SendAfter);
            numPerMin = AddNum(tMail, "Máx. e-mails por minuto (0 = sem limite)", ref y, 0, 1000, c.MaxPerMinute);
            numPerHour = AddNum(tMail, "Máx. e-mails por hora (0 = sem limite)", ref y, 0, 100000, c.MaxPerHour);
            txtServer = AddTxt(tMail, "Servidor SMTP", ref y, c.SmtpServer, false);
            numPort = AddNum(tMail, "Porta", ref y, 1, 65535, c.SmtpPort);
            chkSsl = AddChk(tMail, "Usar SSL/TLS", ref y, c.SmtpSsl);
            txtUser = AddTxt(tMail, "Usuário", ref y, c.SmtpUser, false);
            txtPass = AddTxt(tMail, "Senha", ref y, c.SmtpPass, true);
            txtFrom = AddTxt(tMail, "De", ref y, c.MailFrom, false);
            txtTo = AddTxt(tMail, "Para (separe vários com ;)", ref y, c.MailTo, false);
            txtSubject = AddTxt(tMail, "Assunto", ref y, c.MailSubject, false);
            Label lb = new Label();
            lb.Text = "Mensagem (variáveis: %HOST_ID% %HOST_NAME% %HOST_IP% %HOST_STATUS% %DATE% %PL_COUNT%)";
            lb.SetBounds(16, y, 580, 18);
            tMail.Controls.Add(lb);
            y += 20;
            txtBody = new TextBox();
            txtBody.Multiline = true;
            txtBody.AcceptsReturn = true;
            txtBody.ScrollBars = ScrollBars.Vertical;
            txtBody.Text = c.MailBody.Replace("\r\n", "\n").Replace("\n", "\r\n");
            txtBody.SetBounds(16, y, 580, 60);
            tMail.Controls.Add(txtBody);

            // ---- Hosts: grade (Nº, Ativo, IP, Nome) ----
            gridHosts = new DataGridView();
            gridHosts.Dock = DockStyle.Fill;
            gridHosts.AllowUserToAddRows = true;
            gridHosts.AllowUserToDeleteRows = true;
            gridHosts.AllowUserToResizeRows = false;
            gridHosts.RowHeadersVisible = false;
            gridHosts.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            gridHosts.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            gridHosts.BackgroundColor = SystemColors.Window;
            gridHosts.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            gridHosts.Columns.Add(MakeTextCol("Nº", 8, true));
            DataGridViewCheckBoxColumn colOn = new DataGridViewCheckBoxColumn();
            colOn.HeaderText = "Ativo";
            colOn.FillWeight = 8;
            colOn.SortMode = DataGridViewColumnSortMode.NotSortable;
            gridHosts.Columns.Add(colOn);
            gridHosts.Columns.Add(MakeTextCol("IP / Host", 34, false));
            gridHosts.Columns.Add(MakeTextCol("Nome (opcional)", 50, false));
            foreach (HostEntry h in c.Hosts) gridHosts.Rows.Add("", h.Enabled, h.Address, h.Name);
            Renumber();
            gridHosts.RowsAdded += delegate { Renumber(); };
            gridHosts.RowsRemoved += delegate { Renumber(); };
            gridHosts.DefaultValuesNeeded += delegate(object s, DataGridViewRowEventArgs e) { e.Row.Cells[ColOn].Value = true; };
            gridHosts.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.V && !gridHosts.IsCurrentCellInEditMode)
                {
                    string t = "";
                    try { t = Clipboard.GetText(); } catch { }
                    if (t.Length > 0)
                    {
                        DataGridViewCell cur = gridHosts.CurrentCell;
                        int start = cur != null ? cur.RowIndex : gridHosts.Rows.Count - 1;
                        PasteRows(t, start, cur != null && cur.ColumnIndex == ColName);
                    }
                    e.Handled = true;
                }
            };
            tHosts.Controls.Add(gridHosts);

            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 62;
            Label lw = new Label();
            lw.Text = "Número de janelas:";
            lw.SetBounds(8, 11, 110, 20);
            top.Controls.Add(lw);
            numWindows = new NumericUpDown();
            numWindows.Minimum = 1;
            numWindows.Maximum = 256;
            numWindows.Value = Math.Max(1, Math.Min(256, c.WindowCount));
            numWindows.SetBounds(120, 8, 60, 24);
            top.Controls.Add(numWindows);
            Button bPaste = new Button();
            bPaste.Text = "Colar lista de hosts...";
            bPaste.SetBounds(192, 6, 150, 27);
            bPaste.Click += delegate { PasteDialog(); };
            top.Controls.Add(bPaste);
            Button bAll = new Button();
            bAll.Text = "Ativar todos";
            bAll.SetBounds(348, 6, 100, 27);
            bAll.Click += delegate { SetAllEnabled(true); };
            top.Controls.Add(bAll);
            Button bNone = new Button();
            bNone.Text = "Desativar todos";
            bNone.SetBounds(454, 6, 110, 27);
            bNone.Click += delegate { SetAllEnabled(false); };
            top.Controls.Add(bNone);
            Label hint = new Label();
            hint.Text = "Dica: copie colunas do Excel (IP [tab] Nome) e cole com Ctrl+V na grade. Delete apaga as linhas selecionadas. IP repetido é removido ao salvar.";
            hint.ForeColor = SystemColors.GrayText;
            hint.SetBounds(8, 40, 720, 18);
            hint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            top.Controls.Add(hint);
            tHosts.Controls.Add(top);

            Button ok = new Button();
            ok.Text = "OK";
            ok.SetBounds(580, 560, 80, 28);
            ok.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            ok.Click += delegate { if (Apply()) { DialogResult = DialogResult.OK; Close(); } };
            Button cancel = new Button();
            cancel.Text = "Cancelar";
            cancel.SetBounds(670, 560, 80, 28);
            cancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = null;
            CancelButton = cancel;
            Load += delegate { MinimumSize = Size; };
        }

        static DataGridViewTextBoxColumn MakeTextCol(string header, float weight, bool readOnly)
        {
            DataGridViewTextBoxColumn col = new DataGridViewTextBoxColumn();
            col.HeaderText = header;
            col.FillWeight = weight;
            col.ReadOnly = readOnly;
            col.SortMode = DataGridViewColumnSortMode.NotSortable;
            if (readOnly) col.DefaultCellStyle.ForeColor = SystemColors.GrayText;
            return col;
        }

        void Renumber()
        {
            foreach (DataGridViewRow row in gridHosts.Rows)
            {
                if (row.IsNewRow) continue;
                row.Cells[ColNum].Value = (row.Index + 1).ToString("000");
            }
        }

        static bool ParseOn(string s)
        {
            string en = s.Trim().ToLowerInvariant();
            return !(en == "0" || en == "false" || en == "nao" || en == "não" || en == "no");
        }

        // cola linhas "IP [tab ou ;] Nome [tab ou ;] 1/0" a partir da linha start (sobrescreve; cria linhas no fim)
        void PasteRows(string text, int start, bool nameOnly)
        {
            gridHosts.EndEdit();
            int r = start;
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (raw.Trim().Length == 0) continue;
                string[] f = raw.Split(new char[] { '\t', ';' });
                if (r < 0 || r >= gridHosts.Rows.Count || gridHosts.Rows[r].IsNewRow)
                    r = gridHosts.Rows.Add("", true, "", "");
                DataGridViewRow row = gridHosts.Rows[r];
                if (nameOnly)
                {
                    row.Cells[ColName].Value = f[0].Trim();
                }
                else
                {
                    row.Cells[ColIp].Value = f[0].Trim();
                    if (f.Length > 1) row.Cells[ColName].Value = f[1].Trim();
                    if (f.Length > 2) row.Cells[ColOn].Value = ParseOn(f[2]);
                }
                r++;
            }
        }

        void PasteDialog()
        {
            using (Form f = new Form())
            {
                f.Text = "Colar lista de hosts";
                f.StartPosition = FormStartPosition.CenterParent;
                f.ShowInTaskbar = false;
                f.MinimizeBox = false;
                f.AutoScaleMode = AutoScaleMode.Dpi;
                f.AutoScaleDimensions = new SizeF(96f, 96f);
                f.ClientSize = new Size(520, 400);
                Label l = new Label();
                l.Text = "Um host por linha:  IP [tab ou ;] Nome [tab ou ;] 1/0 (opcional). Os hosts entram no fim da lista.";
                l.SetBounds(8, 8, 504, 32);
                l.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                TextBox t = new TextBox();
                t.Multiline = true;
                t.AcceptsReturn = true;
                t.AcceptsTab = true;
                t.WordWrap = false;
                t.ScrollBars = ScrollBars.Both;
                t.Font = new Font("Consolas", 9f);
                t.SetBounds(8, 42, 504, 312);
                t.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                Button ok = new Button();
                ok.Text = "Adicionar";
                ok.SetBounds(342, 364, 80, 28);
                ok.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                ok.DialogResult = DialogResult.OK;
                Button cancel = new Button();
                cancel.Text = "Cancelar";
                cancel.SetBounds(432, 364, 80, 28);
                cancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                cancel.DialogResult = DialogResult.Cancel;
                f.Controls.Add(l);
                f.Controls.Add(t);
                f.Controls.Add(ok);
                f.Controls.Add(cancel);
                f.CancelButton = cancel;
                if (f.ShowDialog(this) == DialogResult.OK && t.Text.Trim().Length > 0)
                {
                    PasteRows(t.Text, gridHosts.Rows.Count - 1, false);
                    gridHosts.FirstDisplayedScrollingRowIndex = Math.Max(0, gridHosts.Rows.Count - 1);
                }
            }
        }

        void SetAllEnabled(bool on)
        {
            gridHosts.EndEdit();
            foreach (DataGridViewRow row in gridHosts.Rows)
                if (!row.IsNewRow) row.Cells[ColOn].Value = on;
        }

        static string CellText(DataGridViewRow row, int col)
        {
            object v = row.Cells[col].Value;
            return v == null ? "" : v.ToString().Trim();
        }

        NumericUpDown AddNum(Control parent, string label, ref int y, int min, int max, int value)
        {
            Label l = new Label();
            l.Text = label;
            l.SetBounds(16, y + 3, 380, 20);
            parent.Controls.Add(l);
            NumericUpDown n = new NumericUpDown();
            n.Minimum = min;
            n.Maximum = max;
            n.Value = Math.Max(min, Math.Min(max, value));
            n.SetBounds(400, y, 150, 24);
            parent.Controls.Add(n);
            y += 30;
            return n;
        }

        CheckBox AddChk(Control parent, string label, ref int y, bool value)
        {
            CheckBox c = new CheckBox();
            c.Text = label;
            c.Checked = value;
            c.SetBounds(16, y, 560, 22);
            parent.Controls.Add(c);
            y += 26;
            return c;
        }

        TextBox AddTxt(Control parent, string label, ref int y, string value, bool password)
        {
            Label l = new Label();
            l.Text = label;
            l.SetBounds(16, y + 3, 200, 20);
            parent.Controls.Add(l);
            TextBox t = new TextBox();
            t.Text = value;
            t.UseSystemPasswordChar = password;
            t.SetBounds(220, y, 376, 24);
            parent.Controls.Add(t);
            y += 28;
            return t;
        }

        bool Apply()
        {
            TimeSpan rt;
            if (!Config.TryParseTime(txtRecheckTime.Text, out rt))
            {
                MessageBox.Show(this, "Horário do teste diário inválido. Use HH:mm (ex.: 08:00).", "Opções");
                return false;
            }
            List<HostEntry> hosts = new List<HostEntry>();
            gridHosts.EndEdit();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow row in gridHosts.Rows)
            {
                if (row.IsNewRow) continue;
                HostEntry h = new HostEntry();
                h.Address = CellText(row, ColIp);
                h.Name = CellText(row, ColName);
                object on = row.Cells[ColOn].Value;
                h.Enabled = on is bool ? (bool)on : true;
                if (h.Address.Length == 0 && h.Name.Length == 0) continue;
                if (h.Address.Length > 0 && !seen.Add(h.Address)) continue;   // remove IP duplicado
                hosts.Add(h);
                if (hosts.Count >= Config.MaxHosts) break;
            }
            cfg.Hosts = hosts;
            cfg.PingInterval = (int)numInterval.Value;
            cfg.PingTimeout = (int)numTimeout.Value;
            cfg.ConfirmPings = (int)numConfirm.Value;
            cfg.UnstableMinutes = (int)numUnstable.Value;
            cfg.WindowCount = (int)numWindows.Value;
            cfg.Columns = (int)numColumns.Value;
            cfg.PromoteAfter = (int)numPromote.Value;
            cfg.MaxLines = (int)numMaxLines.Value;
            cfg.View = (ViewMode)(cmbView.SelectedIndex + 1);
            cfg.AutoStart = chkAuto.Checked;
            cfg.AutoScroll = chkScroll.Checked;
            cfg.PlaySounds = chkSound.Checked;
            cfg.ShowTime = chkTime.Checked;
            cfg.LogToFiles = chkLog.Checked;
            cfg.RecheckDisabled = chkRecheck.Checked;
            cfg.RecheckTime = rt.Hours.ToString("00") + ":" + rt.Minutes.ToString("00");
            cfg.MailAlerts = chkMail.Checked;
            cfg.SendAfter = (int)numSendAfter.Value;
            cfg.MaxPerMinute = (int)numPerMin.Value;
            cfg.MaxPerHour = (int)numPerHour.Value;
            cfg.SmtpServer = txtServer.Text.Trim();
            cfg.SmtpPort = (int)numPort.Value;
            cfg.SmtpSsl = chkSsl.Checked;
            cfg.SmtpUser = txtUser.Text.Trim();
            cfg.SmtpPass = txtPass.Text;
            cfg.MailFrom = txtFrom.Text.Trim();
            cfg.MailTo = txtTo.Text.Trim();
            cfg.MailSubject = txtSubject.Text;
            cfg.MailBody = txtBody.Text.Replace("\r\n", "\n");
            return true;
        }
    }
}
