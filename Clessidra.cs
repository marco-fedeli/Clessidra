// Clessidra - timer con conto alla rovescia che vive nell'area di notifica.
// Alla fine suona un "ding" discreto e mostra un avviso silenzioso, senza rubare il focus.
//   Ctrl+Alt+T  -> apre/chiude il pannellino del timer (anche clic sinistro sull'icona)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Clessidra")]
[assembly: AssemblyProduct("Clessidra")]
[assembly: AssemblyDescription("Clessidra - timer con conto alla rovescia")]
[assembly: AssemblyVersion("1.1.0.0")]

namespace Clessidra
{
    static class Program
    {
        public const string AppName = "Clessidra";

        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (new Mutex(true, "Clessidra_SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("Clessidra è già in esecuzione (icona vicino all'orologio, Ctrl+Alt+T).", AppName);
                    return;
                }
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                // "/auto" = avviato insieme a Windows: niente messaggio di benvenuto.
                bool auto = args.Length > 0 && args[0].Equals("/auto", StringComparison.OrdinalIgnoreCase);
                Application.Run(new TrayContext(!auto));
            }
        }
    }

    // ------------------------------------------------------------------ preferenze (HKCU\Software\Clessidra)

    static class Prefs
    {
        const string Key = @"Software\Clessidra";
        public const int NoPos = int.MinValue;   // posizione non ancora scelta: si usa quella predefinita
        public static int LastSeconds = 25 * 60;
        public static string Sound = Sounds.Names[0];
        public static bool ShowOverlay = true;
        public static Point PanelPos = new Point(NoPos, NoPos);
        public static Point OverlayPos = new Point(NoPos, NoPos);

        public static void Load()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Key))
                {
                    if (k == null) return;
                    object v = k.GetValue("UltimaDurata");
                    if (v is int && (int)v > 0) LastSeconds = (int)v;
                    string s = k.GetValue("Suono") as string;
                    if (s != null && Array.IndexOf(Sounds.Names, s) >= 0) Sound = s;
                    v = k.GetValue("CountdownSulloSchermo");
                    if (v is int) ShowOverlay = (int)v != 0;
                    PanelPos = ReadPoint(k, "Pannello");
                    OverlayPos = ReadPoint(k, "Countdown");
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Key))
                {
                    k.SetValue("UltimaDurata", LastSeconds, RegistryValueKind.DWord);
                    k.SetValue("Suono", Sound);
                    k.SetValue("CountdownSulloSchermo", ShowOverlay ? 1 : 0, RegistryValueKind.DWord);
                    WritePoint(k, "Pannello", PanelPos);
                    WritePoint(k, "Countdown", OverlayPos);
                }
            }
            catch { }
        }

        static Point ReadPoint(RegistryKey k, string name)
        {
            object x = k.GetValue(name + "X"), y = k.GetValue(name + "Y");
            return x is int && y is int ? new Point((int)x, (int)y) : new Point(NoPos, NoPos);
        }

        static void WritePoint(RegistryKey k, string name, Point p)
        {
            if (p.X == NoPos) { k.DeleteValue(name + "X", false); k.DeleteValue(name + "Y", false); return; }
            k.SetValue(name + "X", p.X, RegistryValueKind.DWord);
            k.SetValue(name + "Y", p.Y, RegistryValueKind.DWord);
        }

        // La finestra in quella posizione è ancora visibile (es. dopo aver staccato un monitor)?
        public static bool OnScreen(Point p, Size s)
        {
            if (p.X == NoPos) return false;
            var r = new Rectangle(p, s);
            foreach (Screen scr in Screen.AllScreens)
            {
                Rectangle i = Rectangle.Intersect(scr.WorkingArea, r);
                if (i.Width >= Math.Min(40, s.Width) && i.Height >= Math.Min(20, s.Height)) return true;
            }
            return false;
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsAutostart()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue("Clessidra") != null;
        }

        public static void SetAutostart(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue("Clessidra", "\"" + Application.ExecutablePath + "\" /auto");
                else k.DeleteValue("Clessidra", false);
            }
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Key))
                    k.SetValue("AvvioAutomatico", on ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ durate

    static class Durata
    {
        // "25" = 25 minuti, "2,5" = 2 min 30 s, "1:30" = 1 min 30 s, "1:05:00" = 1 ora e 5 min,
        // oppure con unità: "90s", "20m", "20min", "1h". Restituisce i secondi, -1 se non valida.
        public static int Parse(string text)
        {
            if (text == null) return -1;
            string t = text.Trim().ToLowerInvariant().Replace(" ", "");
            if (t.Length == 0) return -1;
            double secs;
            if (t.Contains(":"))
            {
                string[] parts = t.Split(':');
                if (parts.Length > 3) return -1;
                secs = 0;
                foreach (string part in parts)
                {
                    int n;
                    if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out n)) return -1;
                    secs = secs * 60 + n;
                }
            }
            else
            {
                double mult = 60;
                if (t.EndsWith("min")) t = t.Substring(0, t.Length - 3);
                else if (t.EndsWith("s")) { mult = 1; t = t.Substring(0, t.Length - 1); }
                else if (t.EndsWith("m")) t = t.Substring(0, t.Length - 1);
                else if (t.EndsWith("h")) { mult = 3600; t = t.Substring(0, t.Length - 1); }
                double n;
                if (!double.TryParse(t.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out n)) return -1;
                secs = n * mult;
            }
            if (secs < 1 || secs > 24 * 3600) return -1;
            return (int)Math.Round(secs);
        }

        // Testo per il campo "Durata": minuti interi se possibile, altrimenti mm:ss.
        public static string ToField(int s)
        {
            return s % 60 == 0 ? (s / 60).ToString() : Clock(s);
        }

        public static string Clock(int s)
        {
            if (s < 0) s = 0;
            int h = s / 3600, m = s / 60 % 60, sec = s % 60;
            return h > 0 ? string.Format("{0}:{1:00}:{2:00}", h, m, sec) : string.Format("{0:00}:{1:00}", m, sec);
        }

        public static string Words(int s)
        {
            int h = s / 3600, m = s / 60 % 60, sec = s % 60;
            var parts = new List<string>();
            if (h > 0) parts.Add(h + " h");
            if (m > 0) parts.Add(m + " min");
            if (sec > 0) parts.Add(sec + " s");
            return parts.Count == 0 ? "0 s" : string.Join(" ", parts.ToArray());
        }
    }

    // ------------------------------------------------------------------ suoni (generati dal programma)

    static class Sounds
    {
        public const string None = "Nessuno";
        public static readonly string[] Names = { "Campanella", "Doppio rintocco", "Gong leggero", None };

        const int Rate = 44100;
        const uint SND_ASYNC = 0x1, SND_NODEFAULT = 0x2, SND_MEMORY = 0x4;
        [DllImport("winmm.dll")] static extern bool PlaySound(IntPtr data, IntPtr hmod, uint flags);

        static readonly Dictionary<string, byte[]> cache = new Dictionary<string, byte[]>();
        static IntPtr buffer = IntPtr.Zero;   // memoria nativa: resta valida finché il suono suona

        public static void Play(string name)
        {
            if (name == None) return;
            try
            {
                byte[] wav = Wav(name);
                Stop();
                buffer = Marshal.AllocHGlobal(wav.Length);
                Marshal.Copy(wav, 0, buffer, wav.Length);
                if (!PlaySound(buffer, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT))
                    SystemSounds.Asterisk.Play();
            }
            catch { SystemSounds.Asterisk.Play(); }
        }

        public static void Stop()
        {
            PlaySound(IntPtr.Zero, IntPtr.Zero, 0);
            if (buffer != IntPtr.Zero) { Marshal.FreeHGlobal(buffer); buffer = IntPtr.Zero; }
        }

        public static byte[] Wav(string name)
        {
            byte[] wav;
            if (cache.TryGetValue(name, out wav)) return wav;
            float[] buf;
            switch (name)
            {
                case "Doppio rintocco":   // ding-dong: Mi6 poi Do6
                    buf = new float[(int)(Rate * 1.9)];
                    Bell(buf, 0.00, 1318.5, 0.38, 0.85);
                    Bell(buf, 0.30, 1046.5, 0.50, 1.0);
                    break;
                case "Gong leggero":      // Sol4 con armoniche non intere, coda più lunga
                    buf = new float[(int)(Rate * 2.2)];
                    Note(buf, 0.0, 392.0, 0.015, new[] { 1.0, 2.32, 3.17, 4.11 }, new[] { 1.0, 0.45, 0.22, 0.1 }, 0.85);
                    break;
                default:                  // campanella: un solo La5 morbido
                    buf = new float[(int)(Rate * 1.6)];
                    Bell(buf, 0.0, 880.0, 0.45, 1.0);
                    break;
            }
            wav = Encode(buf, 0.3);
            cache[name] = wav;
            return wav;
        }

        static void Bell(float[] buf, double start, double freq, double decay, double amp)
        {
            Note(buf, start, freq, 0.004, new[] { 1.0, 2.0, 3.0, 4.2 }, new[] { amp, amp * 0.3, amp * 0.1, amp * 0.04 }, decay);
        }

        // Somma una nota: attacco morbido e coda che si spegne (le armoniche alte si spengono prima).
        static void Note(float[] buf, double start, double freq, double attack, double[] ratios, double[] amps, double decay)
        {
            int s0 = (int)(start * Rate);
            for (int i = s0; i < buf.Length; i++)
            {
                double t = (double)(i - s0) / Rate;
                double env = t < attack ? t / attack : 1.0;
                double v = 0;
                for (int k = 0; k < ratios.Length; k++)
                    v += amps[k] * Math.Exp(-t * (1 + k * 0.6) / decay) * Math.Sin(2 * Math.PI * freq * ratios[k] * t);
                buf[i] += (float)(env * v);
            }
        }

        // WAV mono 16 bit, normalizzato a "peak" (0,3 = volume moderato) con dissolvenza finale.
        static byte[] Encode(float[] buf, double peak)
        {
            double max = 1e-9;
            foreach (float f in buf) max = Math.Max(max, Math.Abs(f));
            int fade = Rate / 20;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                int dataLen = buf.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + dataLen);
                w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2);
                w.Write((short)2); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(dataLen);
                for (int i = 0; i < buf.Length; i++)
                {
                    double v = buf[i] / max * peak;
                    int left = buf.Length - i;
                    if (left < fade) v *= (double)left / fade;
                    w.Write((short)Math.Round(v * 32767));
                }
                w.Flush();
                return ms.ToArray();
            }
        }
    }

    // ------------------------------------------------------------------ icone

    public static class IconArt
    {
        public static readonly Color Sand = Color.FromArgb(242, 172, 44);
        static readonly Color Wood = Color.FromArgb(128, 78, 40);

        // Clessidra: due assi di legno, vetro chiaro e sabbia ambrata che scende.
        public static Bitmap Hourglass(int s)
        {
            var b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float barH = Math.Max(2f, s * 0.12f);
                float top = s * 0.04f, bottom = s * 0.96f;
                float gt = top + barH, gb = bottom - barH;          // vetro tra le due assi
                float l = s * 0.24f, r = s * 0.76f, cx = s * 0.5f, neck = Math.Max(1f, s * 0.05f);
                float mid = (gt + gb) / 2;

                using (var glass = new GraphicsPath())
                {
                    glass.AddLines(new[] {
                        new PointF(l, gt), new PointF(r, gt), new PointF(cx + neck, mid),
                        new PointF(r, gb), new PointF(l, gb), new PointF(cx - neck, mid) });
                    glass.CloseFigure();
                    using (var br = new SolidBrush(Color.FromArgb(235, 232, 243, 250))) g.FillPath(br, glass);

                    using (var sand = new SolidBrush(Sand))
                    {
                        // un po' di sabbia rimasta sopra e il mucchietto sotto
                        float h1 = (mid - gt) * 0.45f, w1 = (r - l) / 2 * 0.45f;
                        g.FillPolygon(sand, new[] { new PointF(cx - w1, mid - h1), new PointF(cx + w1, mid - h1), new PointF(cx, mid) });
                        float h2 = (gb - mid) * 0.55f;
                        g.FillPolygon(sand, new[] { new PointF(l + s * 0.04f, gb), new PointF(r - s * 0.04f, gb), new PointF(cx, gb - h2) });
                        g.FillRectangle(sand, cx - Math.Max(0.5f, s * 0.015f), mid, Math.Max(1f, s * 0.03f), gb - h2 - mid);
                    }
                    using (var p = new Pen(Color.FromArgb(70, 100, 120), Math.Max(1f, s * 0.045f))) g.DrawPath(p, glass);
                }
                using (var wb = new SolidBrush(Wood))
                {
                    using (var p1 = RoundRect(new RectangleF(s * 0.14f, top, s * 0.72f, barH), barH * 0.35f)) g.FillPath(wb, p1);
                    using (var p2 = RoundRect(new RectangleF(s * 0.14f, gb, s * 0.72f, barH), barH * 0.35f)) g.FillPath(wb, p2);
                }
            }
            return b;
        }

        // Icona con i minuti che mancano: sfondo ambra mentre scorre, grigio in pausa.
        public static Bitmap Number(int s, string text, bool paused)
        {
            var b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                var rc = new RectangleF(0, 0, s, s);
                using (var path = RoundRect(rc, s * 0.22f))
                using (var br = new SolidBrush(paused ? Color.FromArgb(150, 150, 150) : Sand))
                    g.FillPath(br, path);
                using (var tp = new GraphicsPath())
                using (var ff = new FontFamily("Segoe UI"))
                {
                    tp.AddString(text, ff, (int)FontStyle.Bold, 100f, new PointF(0, 0), StringFormat.GenericTypographic);
                    RectangleF tb = tp.GetBounds();
                    float maxW = s * (text.Length > 1 ? 0.9f : 0.6f), maxH = s * 0.68f;
                    float k = Math.Min(maxW / tb.Width, maxH / tb.Height);
                    using (var m = new Matrix())
                    {
                        m.Translate(s / 2f, s / 2f);
                        m.Scale(k, k);
                        m.Translate(-(tb.X + tb.Width / 2), -(tb.Y + tb.Height / 2));
                        tp.Transform(m);
                    }
                    using (var tbr = new SolidBrush(paused ? Color.White : Color.FromArgb(45, 30, 5))) g.FillPath(tbr, tp);
                }
            }
            return b;
        }

        // Tempo scaduto: quadratino verde con la spunta (resta finché apri il pannello).
        public static Bitmap Done(int s)
        {
            var b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var path = RoundRect(new RectangleF(0, 0, s, s), s * 0.22f))
                using (var br = new SolidBrush(Color.FromArgb(60, 165, 95)))
                    g.FillPath(br, path);
                using (var p = new Pen(Color.White, Math.Max(1.6f, s * 0.13f)))
                {
                    p.StartCap = p.EndCap = LineCap.Round;
                    p.LineJoin = LineJoin.Round;
                    g.DrawLines(p, new[] { new PointF(s * 0.25f, s * 0.53f), new PointF(s * 0.43f, s * 0.71f), new PointF(s * 0.76f, s * 0.31f) });
                }
            }
            return b;
        }

        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Max(0.5f, rad * 2);
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Scrive un file .ico con immagini PNG (usato dallo script di compilazione).
        public static void SaveIco(string path)
        {
            int[] sizes = { 16, 24, 32, 48, 64, 256 };
            var images = new List<byte[]>();
            foreach (int s in sizes)
                using (var bmp = Hourglass(s))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    images.Add(ms.ToArray());
                }
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(images[i].Length); w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (var img in images) w.Write(img);
            }
        }
    }

    // ------------------------------------------------------------------ avviso silenzioso

    // NotifyIcon.ShowBalloonTip fa suonare anche la notifica di Windows: qui si chiede
    // a Windows lo stesso avviso ma senza suono (NIIF_NOSOUND), così si sente solo il nostro ding.
    static class Notifier
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(int msg, ref NOTIFYICONDATA data);
        const int NIM_MODIFY = 1, NIF_INFO = 0x10;
        const int NIIF_USER = 0x4, NIIF_NOSOUND = 0x10, NIIF_LARGE_ICON = 0x20;

        public static void Show(NotifyIcon tray, IntPtr bigIcon, string title, string text)
        {
            if (title.Length > 63) title = title.Substring(0, 62) + "…";
            if (text.Length > 255) text = text.Substring(0, 254) + "…";
            try
            {
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                FieldInfo fId = typeof(NotifyIcon).GetField("id", flags);
                FieldInfo fWin = typeof(NotifyIcon).GetField("window", flags);
                var win = fWin == null ? null : fWin.GetValue(tray) as NativeWindow;
                if (fId != null && win != null && win.Handle != IntPtr.Zero)
                {
                    var d = new NOTIFYICONDATA();
                    d.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA));
                    d.hWnd = win.Handle;
                    d.uID = (int)fId.GetValue(tray);
                    d.uFlags = NIF_INFO;
                    d.szInfo = text;
                    d.szInfoTitle = title;
                    d.uTimeoutOrVersion = 6000;
                    d.dwInfoFlags = NIIF_USER | NIIF_LARGE_ICON | NIIF_NOSOUND;
                    d.hBalloonIcon = bigIcon;
                    if (Shell_NotifyIcon(NIM_MODIFY, ref d)) return;
                }
            }
            catch { }
            tray.ShowBalloonTip(6000, title, text, ToolTipIcon.None);   // ripiego (con il suono di Windows)
        }
    }

    // ------------------------------------------------------------------ icona nell'area di notifica e timer

    static class Native
    {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);

        // Sposta una finestra senza bordi come se la si prendesse per la barra del titolo.
        // Ritorna quando si lascia il pulsante del mouse; true se la finestra si è mossa.
        public static bool DragWindow(Form f)
        {
            Point before = f.Location;
            ReleaseCapture();
            SendMessage(f.Handle, 0xA1, new IntPtr(2), IntPtr.Zero);   // WM_NCLBUTTONDOWN, HTCAPTION
            return f.Location != before;
        }

        public static void RoundCorners(IntPtr h)
        {
            try { int round = 2; DwmSetWindowAttribute(h, 33, ref round, 4); } catch { }   // solo Windows 11
        }
    }

    enum TimerState { Idle, Running, Paused, Done }

    class TrayContext : ApplicationContext
    {
        const int IdPanel = 1;

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        readonly NotifyIcon tray;
        readonly HotkeyWindow hotkeys;
        readonly System.Windows.Forms.Timer ticker;
        readonly PanelForm panel;
        readonly OverlayForm overlay;
        readonly ToolStripMenuItem miPause, miCancel, miAuto, miOverlay;
        readonly List<ToolStripMenuItem> miSounds = new List<ToolStripMenuItem>();
        IntPtr trayIcon = IntPtr.Zero, bigIcon = IntPtr.Zero;
        string iconKey = "";
        string tipText = "";

        public TimerState State { get; private set; }
        public int Total { get; private set; }
        public string Activity { get; private set; }
        public DateTime FinishedAt { get; private set; }
        DateTime endUtc;
        int pausedLeft;

        public TrayContext(bool welcome)
        {
            Prefs.Load();
            Activity = "";

            tray = new NotifyIcon();
            using (var bmp = IconArt.Hourglass(SystemInformation.IconSize.Width)) bigIcon = bmp.GetHicon();

            var menu = new ContextMenuStrip();
            var miOpen = new ToolStripMenuItem("Apri il timer", null, delegate { ShowPanel(); });
            miOpen.ShortcutKeyDisplayString = "Ctrl+Alt+T";   // mostrato a destra, allineato come nei menu di Windows
            miOpen.Font = new Font(miOpen.Font, FontStyle.Bold);
            menu.Items.Add(miOpen);
            menu.Items.Add(new ToolStripSeparator());
            foreach (int m in PanelForm.Presets)
            {
                int min = m;
                menu.Items.Add(min + " minuti", null, delegate { Start(min * 60, ""); });
            }
            menu.Items.Add(new ToolStripSeparator());
            miPause = new ToolStripMenuItem("Pausa", null, delegate { TogglePause(); });
            miCancel = new ToolStripMenuItem("Annulla il timer", null, delegate { Cancel(); });
            menu.Items.Add(miPause);
            menu.Items.Add(miCancel);
            menu.Items.Add(new ToolStripSeparator());
            var miSound = new ToolStripMenuItem("Suono finale");
            foreach (string n in Sounds.Names)
            {
                string name = n;
                var it = new ToolStripMenuItem(name, null, delegate { SetSound(name); });
                miSounds.Add(it);
                miSound.DropDownItems.Add(it);
            }
            miSound.DropDownItems.Add(new ToolStripSeparator());
            miSound.DropDownItems.Add("Prova il suono", null, delegate { Sounds.Play(Prefs.Sound); });
            menu.Items.Add(miSound);
            miOverlay = new ToolStripMenuItem("Countdown sullo schermo", null, delegate
            {
                Prefs.ShowOverlay = !Prefs.ShowOverlay;
                Prefs.Save();
                UpdateTray();
            });
            menu.Items.Add(miOverlay);
            menu.Items.Add("Rimetti le finestre al loro posto", null, delegate
            {
                Prefs.PanelPos = Prefs.OverlayPos = new Point(Prefs.NoPos, Prefs.NoPos);
                Prefs.Save();
                overlay.PlaceDefault();
            });
            miAuto =new ToolStripMenuItem("Avvia con Windows", null, delegate { Prefs.SetAutostart(!Prefs.IsAutostart()); });
            menu.Items.Add(miAuto);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Esci", null, delegate { ExitThread(); });
            menu.Opening += delegate { UpdateMenu(); };
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) TogglePanel(); };

            panel = new PanelForm(this);
            panel.VisibleChanged += delegate { if (!panel.Visible) Dismiss(); };
            overlay = new OverlayForm(this);

            UpdateTray();
            tray.Visible = true;

            ticker = new System.Windows.Forms.Timer();
            ticker.Interval = 200;
            ticker.Tick += delegate { Tick(); };
            ticker.Start();

            hotkeys = new HotkeyWindow();
            hotkeys.Pressed += id => TogglePanel();
            if (!hotkeys.Register(IdPanel, HotkeyWindow.MOD_CONTROL | HotkeyWindow.MOD_ALT, Keys.T))
                Notifier.Show(tray, bigIcon, "Ctrl+Alt+T è già in uso",
                    "Un altro programma usa già questa combinazione. Puoi comunque aprire il timer cliccando sulla clessidra.");
            else if (welcome)
                Notifier.Show(tray, bigIcon, "Clessidra è attiva",
                    "Ctrl+Alt+T o clic sulla clessidra per impostare il timer.");
        }

        public int Remaining
        {
            get
            {
                if (State == TimerState.Running)
                    return Math.Max(0, (int)Math.Ceiling((endUtc - DateTime.UtcNow).TotalSeconds));
                if (State == TimerState.Paused) return pausedLeft;
                return 0;
            }
        }

        public void Start(int seconds, string activity)
        {
            if (seconds < 1) return;
            Total = seconds;
            Activity = (activity ?? "").Trim();
            endUtc = DateTime.UtcNow.AddSeconds(seconds);
            State = TimerState.Running;
            Prefs.LastSeconds = seconds;
            Prefs.Save();
            UpdateTray();
        }

        public void TogglePause()
        {
            if (State == TimerState.Running)
            {
                pausedLeft = Remaining;
                State = TimerState.Paused;
            }
            else if (State == TimerState.Paused)
            {
                endUtc = DateTime.UtcNow.AddSeconds(pausedLeft);
                State = TimerState.Running;
            }
            UpdateTray();
        }

        public ContextMenuStrip Menu { get { return tray.ContextMenuStrip; } }

        // "Finito" visto: la clessidra torna a riposo e il countdown sullo schermo sparisce.
        // Riporta il countdown alla durata di partenza (anche dopo "Finito": stesso timer da capo).
        public void Restart()
        {
            if (Total > 0) Start(Total, Activity);
        }

        public void Dismiss()
        {
            if (State != TimerState.Done) return;
            State = TimerState.Idle;
            UpdateTray();
        }

        public void Cancel()
        {
            if (State == TimerState.Running || State == TimerState.Paused) State = TimerState.Idle;
            UpdateTray();
        }

        void Tick()
        {
            if (State == TimerState.Running && (endUtc - DateTime.UtcNow).TotalSeconds <= 0) Finish();
            UpdateTray();
        }

        void Finish()
        {
            State = TimerState.Done;
            FinishedAt = DateTime.Now;
            Sounds.Play(Prefs.Sound);
            string title = Activity.Length == 0 ? "Tempo scaduto" : "Finito: " + Activity;
            Notifier.Show(tray, bigIcon, title,
                "Timer di " + Durata.Words(Total) + " terminato alle " + FinishedAt.ToString("HH:mm") + ".");
        }

        void UpdateTray()
        {
            string key, tip;
            switch (State)
            {
                case TimerState.Running:
                case TimerState.Paused:
                    int left = Remaining;
                    int mins = (left + 59) / 60;
                    string num = mins >= 100 ? ((mins + 59) / 60) + "h" : mins.ToString();
                    bool paused = State == TimerState.Paused;
                    key = (paused ? "p" : "r") + num;
                    tip = "Clessidra · " + (paused ? "in pausa " : "") + Durata.Clock(left) +
                          (Activity.Length > 0 ? " · " + Activity : "");
                    break;
                case TimerState.Done:
                    key = "done";
                    tip = "Clessidra · finito alle " + FinishedAt.ToString("HH:mm") + (Activity.Length > 0 ? " · " + Activity : "");
                    break;
                default:
                    key = "idle";
                    tip = "Clessidra  (Ctrl+Alt+T)";
                    break;
            }
            if (key != iconKey)
            {
                iconKey = key;
                int s = SystemInformation.SmallIconSize.Width;
                Bitmap bmp = key == "idle" ? IconArt.Hourglass(s)
                           : key == "done" ? IconArt.Done(s)
                           : IconArt.Number(s, key.Substring(1), key[0] == 'p');
                IntPtr old = trayIcon;
                using (bmp) trayIcon = bmp.GetHicon();
                tray.Icon = Icon.FromHandle(trayIcon);
                if (old != IntPtr.Zero) DestroyIcon(old);
            }
            if (tip.Length > 63) tip = tip.Substring(0, 62) + "…";
            if (tip != tipText) { tipText = tip; tray.Text = tip; }
            if (panel.Visible) panel.RefreshView();
            overlay.Sync();
        }

        void UpdateMenu()
        {
            bool active = State == TimerState.Running || State == TimerState.Paused;
            miPause.Enabled = active;
            miPause.Text = State == TimerState.Paused ? "Riprendi" : "Pausa";
            miCancel.Enabled = active;
            miAuto.Checked = Prefs.IsAutostart();
            miOverlay.Checked = Prefs.ShowOverlay;
            foreach (var it in miSounds) it.Checked = it.Text == Prefs.Sound;
        }

        void SetSound(string name)
        {
            Prefs.Sound = name;
            Prefs.Save();
            Sounds.Play(name);   // lo fa sentire subito
        }

        void TogglePanel()
        {
            if (panel.Visible) { panel.Hide(); return; }
            // un clic sull'icona mentre il pannello è aperto lo chiude (perde il focus): non riaprirlo subito
            if ((DateTime.Now - panel.HiddenAt).TotalMilliseconds < 400) return;
            ShowPanel();
        }

        public void ShowPanel()
        {
            panel.ShowNearTray();
        }

        protected override void ExitThreadCore()
        {
            ticker.Stop();
            hotkeys.Dispose();
            Sounds.Stop();
            panel.Dispose();
            overlay.Dispose();
            tray.Visible = false;
            tray.Dispose();
            if (trayIcon != IntPtr.Zero) DestroyIcon(trayIcon);
            if (bigIcon != IntPtr.Zero) DestroyIcon(bigIcon);
            base.ExitThreadCore();
        }
    }

    // ------------------------------------------------------------------ pannellino

    class Theme
    {
        public Color Back, Fore, Dim, Field, Button, ButtonHover, Border, Accent, AccentHover, AccentFore, Error, Ok;

        public static Theme Current()
        {
            bool light = true;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    if (v is int) light = (int)v != 0;
                }
            }
            catch { }
            var t = new Theme();
            t.Accent = IconArt.Sand;
            t.AccentHover = Color.FromArgb(250, 192, 90);
            t.AccentFore = Color.FromArgb(40, 28, 5);
            if (light)
            {
                t.Back = Color.FromArgb(250, 250, 250); t.Fore = Color.FromArgb(28, 28, 28); t.Dim = Color.FromArgb(110, 110, 110);
                t.Field = Color.White; t.Button = Color.FromArgb(234, 234, 234); t.ButtonHover = Color.FromArgb(220, 220, 220);
                t.Border = Color.FromArgb(200, 200, 200); t.Error = Color.FromArgb(196, 43, 28); t.Ok = Color.FromArgb(40, 140, 70);
            }
            else
            {
                t.Back = Color.FromArgb(36, 36, 36); t.Fore = Color.FromArgb(240, 240, 240); t.Dim = Color.FromArgb(160, 160, 160);
                t.Field = Color.FromArgb(52, 52, 52); t.Button = Color.FromArgb(58, 58, 58); t.ButtonHover = Color.FromArgb(74, 74, 74);
                t.Border = Color.FromArgb(75, 75, 75); t.Error = Color.FromArgb(255, 110, 95); t.Ok = Color.FromArgb(110, 205, 135);
            }
            return t;
        }
    }

    class PanelForm : Form
    {
        public static readonly int[] Presets = { 5, 10, 15, 25, 45, 60 };

        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        readonly TrayContext owner;
        readonly Label lblInfo, lblTime, lblDur, lblHint, lblAct;
        readonly TextBox txtDur, txtAct;
        readonly Button btnStart, btnPause, btnCancel;
        readonly List<Button> presetButtons = new List<Button>();
        readonly float k;
        Theme theme = Theme.Current();
        string error;
        public DateTime HiddenAt = DateTime.MinValue;

        public PanelForm(TrayContext owner)
        {
            this.owner = owner;
            Text = "Clessidra";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            Font = new Font("Segoe UI", 9.5f);
            using (var g = CreateGraphics()) k = g.DpiX / 96f;

            int W = S(286), P = S(14), gap = S(6), rowH = S(30);
            int inner = W - 2 * P;
            int y = S(10);

            lblInfo = AddLabel(P, y, inner, S(20));
            lblInfo.AutoEllipsis = true;
            y += S(20);
            lblTime = AddLabel(P - S(3), y, inner, S(54));
            lblTime.Font = new Font("Segoe UI Light", 28f);
            y += S(58);

            int bw = (inner - 2 * gap) / 3;
            for (int i = 0; i < Presets.Length; i++)
            {
                int min = Presets[i];
                var b = MakeButton(min + " min");
                b.SetBounds(P + (i % 3) * (bw + gap), y + (i / 3) * (rowH + gap), bw, rowH);
                b.Click += delegate { StartWith(min * 60); };
                presetButtons.Add(b);
            }
            y += 2 * rowH + gap + S(14);

            int lw = S(64), dw = S(84);
            lblDur = AddLabel(P, y, lw, rowH);
            lblDur.Text = "Durata";
            txtDur = AddText(P + lw, y, dw, rowH);
            lblHint = AddLabel(P + lw + dw + gap, y, inner - lw - dw - gap, rowH);
            lblHint.Text = "min  o  mm:ss";
            y += rowH + gap;
            lblAct = AddLabel(P, y, lw, rowH);
            lblAct.Text = "Attività";
            txtAct = AddText(P + lw, y, inner - lw, rowH);
            y += rowH + S(14);

            btnStart = MakeButton("Avvia");
            btnPause = MakeButton("Pausa");
            btnCancel = MakeButton("Annulla");
            btnStart.SetBounds(P, y, bw, rowH + S(2));
            btnPause.SetBounds(P + bw + gap, y, bw, rowH + S(2));
            btnCancel.SetBounds(P + 2 * (bw + gap), y, bw, rowH + S(2));
            btnStart.Font = new Font(Font, FontStyle.Bold);
            btnStart.Click += delegate { StartFromField(); };
            btnPause.Click += delegate { owner.TogglePause(); RefreshView(); };
            btnCancel.Click += delegate { owner.Cancel(); RefreshView(); };
            AcceptButton = btnStart;
            y += rowH + S(2) + P;

            ClientSize = new Size(W, y);

            txtDur.TextChanged += delegate { error = null; ApplyTheme(); RefreshView(); };
            SendMessage(txtAct.Handle, 0x1501, new IntPtr(1), "facoltativa, es. Relazione");   // EM_SETCUEBANNER
            Deactivate += delegate { if (Visible) Hide(); };
            VisibleChanged += delegate { if (!Visible) HiddenAt = DateTime.Now; };
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Hide(); } };
            MouseDown += DragPanel;
            foreach (var l in new[] { lblInfo, lblTime, lblDur, lblHint, lblAct }) l.MouseDown += DragPanel;
            ApplyTheme();
        }

        int S(int v) { return (int)Math.Round(v * k); }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000;   // CS_DROPSHADOW
                cp.ExStyle |= 0x80;         // WS_EX_TOOLWINDOW: non compare in Alt+Tab
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundCorners(Handle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        // Tenendo premuto su una zona vuota (o sulle scritte) si sposta il pannello; la posizione viene ricordata.
        void DragPanel(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (Native.DragWindow(this))
            {
                Prefs.PanelPos = Location;
                Prefs.Save();
            }
        }

        Label AddLabel(int x, int y, int w, int h)
        {
            var l = new Label();
            l.SetBounds(x, y, w, h);
            l.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(l);
            return l;
        }

        TextBox AddText(int x, int y, int w, int rowH)
        {
            var t = new TextBox();
            t.BorderStyle = BorderStyle.FixedSingle;
            t.Width = w;
            t.Location = new Point(x, y + (rowH - t.Height) / 2);
            Controls.Add(t);
            return t;
        }

        Button MakeButton(string text)
        {
            var b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.UseVisualStyleBackColor = false;
            b.FlatAppearance.BorderSize = 0;
            b.Cursor = Cursors.Hand;
            Controls.Add(b);
            return b;
        }

        void ApplyTheme()
        {
            BackColor = theme.Back;
            ForeColor = theme.Fore;
            foreach (Control c in Controls)
            {
                var b = c as Button;
                if (b != null)
                {
                    bool accent = b == btnStart;
                    b.BackColor = accent ? theme.Accent : theme.Button;
                    b.ForeColor = accent ? theme.AccentFore : theme.Fore;
                    b.FlatAppearance.MouseOverBackColor = accent ? theme.AccentHover : theme.ButtonHover;
                    b.FlatAppearance.MouseDownBackColor = accent ? theme.AccentHover : theme.ButtonHover;
                }
                var t = c as TextBox;
                if (t != null) { t.BackColor = theme.Field; t.ForeColor = theme.Fore; }
                var l = c as Label;
                if (l != null) { l.BackColor = theme.Back; l.ForeColor = theme.Fore; }
            }
            lblHint.ForeColor = theme.Dim;
            if (error != null)   // campo durata leggermente arrossato
                txtDur.BackColor = Color.FromArgb((theme.Field.R * 3 + theme.Error.R) / 4, (theme.Field.G * 3 + theme.Error.G) / 4, (theme.Field.B * 3 + theme.Error.B) / 4);
            Invalidate();
        }

        public void ShowNearTray()
        {
            theme = Theme.Current();   // segue il tema chiaro/scuro di Windows
            error = null;
            if (owner.State == TimerState.Running || owner.State == TimerState.Paused) txtAct.Text = owner.Activity;
            txtDur.Text = Durata.ToField(Prefs.LastSeconds);
            ApplyTheme();
            RefreshView();

            if (Prefs.OnScreen(Prefs.PanelPos, Size))
                Location = Prefs.PanelPos;   // dove l'hai spostato l'ultima volta
            else
            {
                // nell'angolo vicino all'orologio (in basso a destra, o dove sta la barra delle applicazioni)
                Screen scr = Screen.FromPoint(Cursor.Position);
                Rectangle wa = scr.WorkingArea, all = scr.Bounds;
                int m = S(12);
                int x = wa.Left > all.Left ? wa.Left + m : wa.Right - Width - m;
                int y = wa.Top > all.Top ? wa.Top + m : wa.Bottom - Height - m;
                Location = new Point(x, y);
            }

            Show();
            Activate();
            SetForegroundWindow(Handle);
            txtDur.Focus();
            txtDur.SelectAll();
        }

        void StartFromField()
        {
            int secs = Durata.Parse(txtDur.Text);
            if (secs < 0)
            {
                error = "Durata non valida: prova 25, 1:30 o 90s";
                ApplyTheme();
                RefreshView();
                txtDur.Focus();
                txtDur.SelectAll();
                return;
            }
            StartWith(secs);
        }

        void StartWith(int secs)
        {
            owner.Start(secs, txtAct.Text);
            Hide();
        }

        public void RefreshView()
        {
            string act = owner.Activity;
            string withAct = act.Length > 0 ? "  ·  " + act : "";
            Color timeColor = theme.Fore;
            switch (owner.State)
            {
                case TimerState.Running:
                    lblInfo.Text = "In corso" + withAct;
                    lblTime.Text = Durata.Clock(owner.Remaining);
                    break;
                case TimerState.Paused:
                    lblInfo.Text = "In pausa" + withAct;
                    lblTime.Text = Durata.Clock(owner.Remaining);
                    timeColor = theme.Dim;
                    break;
                case TimerState.Done:
                    lblInfo.Text = "Finito alle " + owner.FinishedAt.ToString("HH:mm") + withAct;
                    lblTime.Text = "00:00";
                    timeColor = theme.Ok;
                    break;
                default:
                    int s = Durata.Parse(txtDur.Text);
                    lblInfo.Text = "Pronta";
                    lblTime.Text = Durata.Clock(s > 0 ? s : Prefs.LastSeconds);
                    timeColor = theme.Dim;
                    break;
            }
            lblInfo.ForeColor = theme.Dim;
            if (error != null) { lblInfo.Text = error; lblInfo.ForeColor = theme.Error; }
            lblTime.ForeColor = timeColor;

            bool active = owner.State == TimerState.Running || owner.State == TimerState.Paused;
            btnPause.Enabled = active;
            btnPause.Text = owner.State == TimerState.Paused ? "Riprendi" : "Pausa";
            btnCancel.Enabled = active;
            btnStart.Text = active ? "Ricomincia" : "Avvia";
        }
    }

    // ------------------------------------------------------------------ countdown in sovraimpressione

    // Piccola finestra sempre in primo piano con il tempo che manca. Non prende mai il focus,
    // quindi non interrompe quello che stai scrivendo. Si trascina dove si vuole;
    // doppio clic apre il pannello, clic destro apre il menu, un clic su "Finito" lo chiude.
    class OverlayForm : Form
    {
        readonly TrayContext owner;
        readonly float k;
        readonly Font bigFont = new Font("Segoe UI Semibold", 20f);
        readonly Font smallFont = new Font("Segoe UI", 8.5f);
        readonly ToolTip tip = new ToolTip();
        string shownKey = "";
        Point downAt;
        enum Btn { None, Pause, Restart }
        bool pressed;            // tasto sinistro premuto sul riquadro (clic o trascinamento)
        Btn pressedBtn;          // pulsante su cui è stato premuto il tasto sinistro
        Btn hot;                 // pulsante sotto il mouse

        public OverlayForm(TrayContext owner)
        {
            this.owner = owner;
            Text = "Clessidra - countdown";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            // niente TopMost = true: WinForms attiverebbe la finestra; ci pensa WS_EX_TOPMOST in CreateParams
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            MaximizeBox = false;
            Opacity = 0.9;
            Cursor = Cursors.SizeAll;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            using (var g = CreateGraphics()) k = g.DpiX / 96f;
            ClientSize = new Size(S(180), S(62));
            if (Prefs.OnScreen(Prefs.OverlayPos, Size)) Location = Prefs.OverlayPos;
            else PlaceDefault();
            tip.ShowAlways = true;   // la finestra non è mai attiva: senza questo il suggerimento non compare
        }

        int S(int v) { return (int)Math.Round(v * k); }

        // Due piccoli pulsanti: in alto a sinistra pausa/riprendi, in alto a destra ricomincia da capo.
        Rectangle PauseRect { get { return new Rectangle(S(5), S(5), S(22), S(22)); } }
        Rectangle RestartRect { get { return new Rectangle(Width - S(27), S(5), S(22), S(22)); } }

        bool CanPause { get { return owner.State == TimerState.Running || owner.State == TimerState.Paused; } }

        Btn ButtonAt(Point p)
        {
            if (CanPause && PauseRect.Contains(p)) return Btn.Pause;
            if (RestartRect.Contains(p)) return Btn.Restart;
            return Btn.None;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x08000000 | 0x8;   // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundCorners(Handle);
        }

        // In basso a destra, sopra l'orologio, sul monitor principale.
        public void PlaceDefault()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - S(24), wa.Bottom - Height - S(24));
        }

        // Chiamato a ogni scatto del timer: si mostra solo quando c'è un countdown (o il "Finito").
        public void Sync()
        {
            TimerState st = owner.State;
            if (!Prefs.ShowOverlay || st == TimerState.Idle)
            {
                if (Visible) Hide();
                return;
            }
            if (!Visible)
            {
                if (!Prefs.OnScreen(Location, Size)) PlaceDefault();
                Show();
            }
            string key = st + "|" + owner.Remaining + "|" + owner.Activity;
            if (key != shownKey) { shownKey = key; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            TimerState st = owner.State;
            bool done = st == TimerState.Done, paused = st == TimerState.Paused;
            g.Clear(done ? Color.FromArgb(46, 125, 72) : Color.FromArgb(30, 30, 30));

            string big = done ? "Finito" : Durata.Clock(owner.Remaining);
            string small = paused ? "In pausa" : done ? "alle " + owner.FinishedAt.ToString("HH:mm") : "";
            if (owner.Activity.Length > 0) small = small.Length > 0 ? small + "  ·  " + owner.Activity : owner.Activity;
            Color fg = paused ? Color.FromArgb(165, 165, 165) : Color.White;

            int barH = S(4), pad = S(8);
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            if (small.Length == 0)
                TextRenderer.DrawText(g, big, bigFont, new Rectangle(pad, 0, Width - 2 * pad, Height - barH), fg, flags);
            else
            {
                TextRenderer.DrawText(g, big, bigFont, new Rectangle(pad, S(3), Width - 2 * pad, S(36)), fg, flags);
                TextRenderer.DrawText(g, small, smallFont, new Rectangle(pad, S(38), Width - 2 * pad, S(18)),
                    done ? Color.White : Color.FromArgb(175, 175, 175), flags);
            }

            // barretta in fondo: quanto tempo è già passato
            if (!done && owner.Total > 0)
            {
                double frac = 1 - (double)owner.Remaining / owner.Total;
                using (var track = new SolidBrush(Color.FromArgb(60, 60, 60)))
                    g.FillRectangle(track, 0, Height - barH, Width, barH);
                using (var fill = new SolidBrush(paused ? Color.FromArgb(130, 130, 130) : IconArt.Sand))
                    g.FillRectangle(fill, 0, Height - barH, (int)Math.Round(Width * frac), barH);
            }

            if (CanPause) DrawPause(g, PauseRect, paused);
            DrawRestart(g, RestartRect, done);
        }

        // Cerchietto sotto il pulsante quando ci passi sopra; restituisce il colore del simbolo.
        Color ButtonBack(Graphics g, Rectangle r, Btn which, bool done)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool isHot = hot == which;
            if (isHot)
                using (var b = new SolidBrush(done ? Color.FromArgb(72, 152, 98) : Color.FromArgb(68, 68, 68)))
                    g.FillEllipse(b, r);
            return isHot ? Color.White : done ? Color.FromArgb(205, 235, 213) : Color.FromArgb(170, 170, 170);
        }

        // Due barrette mentre scorre; triangolo ▶ (riprendi) quando è in pausa.
        void DrawPause(Graphics g, Rectangle r, bool paused)
        {
            Color c = ButtonBack(g, r, Btn.Pause, false);
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, h = r.Height * 0.42f;
            using (var b = new SolidBrush(paused ? IconArt.Sand : c))
            {
                if (paused)
                    g.FillPolygon(b, new[] {
                        new PointF(cx - h * 0.38f, cy - h / 2), new PointF(cx + h * 0.55f, cy), new PointF(cx - h * 0.38f, cy + h / 2) });
                else
                {
                    float w = Math.Max(2f, h * 0.27f), gap = h * 0.22f;
                    g.FillRectangle(b, cx - gap - w, cy - h / 2, w, h);
                    g.FillRectangle(b, cx + gap, cy - h / 2, w, h);
                }
            }
        }

        // Freccia circolare ↺: grigia e discreta, si accende quando ci passi sopra.
        void DrawRestart(Graphics g, Rectangle r, bool done)
        {
            Color c = ButtonBack(g, r, Btn.Restart, done);
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, rad = r.Width * 0.27f;
            // arco da in alto a destra (-50°) girando fino in alto a sinistra; la punta sta all'inizio
            double a0 = -50 * Math.PI / 180;
            using (var p = new Pen(c, Math.Max(1.5f, 1.6f * k)))
                g.DrawArc(p, cx - rad, cy - rad, 2 * rad, 2 * rad, -50, 290);
            var at = new PointF(cx + rad * (float)Math.Cos(a0), cy + rad * (float)Math.Sin(a0));
            var d = new PointF((float)Math.Sin(a0), -(float)Math.Cos(a0));   // verso antiorario
            var n = new PointF(-d.Y, d.X);
            float h = r.Width * 0.2f;
            using (var b = new SolidBrush(c))
                g.FillPolygon(b, new[] {
                    new PointF(at.X + d.X * h, at.Y + d.Y * h),
                    new PointF(at.X - d.X * h * 0.35f + n.X * h * 0.85f, at.Y - d.Y * h * 0.35f + n.Y * h * 0.85f),
                    new PointF(at.X - d.X * h * 0.35f - n.X * h * 0.85f, at.Y - d.Y * h * 0.35f - n.Y * h * 0.85f) });
        }

        void SetHot(Btn value)
        {
            if (hot == value) return;
            hot = value;
            Cursor = hot != Btn.None ? Cursors.Hand : Cursors.SizeAll;
            tip.SetToolTip(this, TipText(hot));
            Invalidate();
        }

        string TipText(Btn b)
        {
            if (b == Btn.Restart) return "Ricomincia da " + Durata.Clock(owner.Total);
            if (b == Btn.Pause) return owner.State == TimerState.Paused ? "Riprendi" : "Pausa";
            return null;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            SetHot(Btn.None);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            pressedBtn = ButtonAt(e.Location);
            if (pressedBtn != Btn.None) return;
            pressed = true;
            downAt = Cursor.Position;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            SetHot(ButtonAt(e.Location));
            if (!pressed) return;
            if ((Control.MouseButtons & MouseButtons.Left) == 0) { pressed = false; return; }
            Point p = Cursor.Position;
            if (Math.Abs(p.X - downAt.X) < SystemInformation.DragSize.Width &&
                Math.Abs(p.Y - downAt.Y) < SystemInformation.DragSize.Height) return;
            pressed = false;
            if (Native.DragWindow(this))
            {
                Prefs.OverlayPos = Location;
                Prefs.Save();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && pressedBtn != Btn.None)
            {
                Btn b = pressedBtn;
                pressedBtn = Btn.None;
                if (ButtonAt(e.Location) != b) return;   // rilasciato fuori dal pulsante: niente
                if (b == Btn.Restart) owner.Restart();
                else owner.TogglePause();
                tip.SetToolTip(this, TipText(hot));     // "Pausa" diventa "Riprendi" e viceversa
                Invalidate();
            }
            else if (e.Button == MouseButtons.Left && pressed) { pressed = false; owner.Dismiss(); }
            else if (e.Button == MouseButtons.Right) owner.Menu.Show(Cursor.Position);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && ButtonAt(e.Location) == Btn.None) owner.ShowPanel();
        }
    }

    // ------------------------------------------------------------------ combinazione di tasti globale

    class HotkeyWindow : NativeWindow, IDisposable
    {
        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public event Action<int> Pressed;
        readonly List<int> ids = new List<int>();

        public HotkeyWindow() { CreateHandle(new CreateParams()); }

        public bool Register(int id, uint mods, Keys key)
        {
            if (!RegisterHotKey(Handle, id, mods | MOD_NOREPEAT, (uint)key)) return false;
            ids.Add(id);
            return true;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && Pressed != null) Pressed(m.WParam.ToInt32());
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            foreach (int id in ids) UnregisterHotKey(Handle, id);
            ids.Clear();
            DestroyHandle();
        }
    }
}
