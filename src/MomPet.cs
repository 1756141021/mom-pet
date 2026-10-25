// Moom desktop pet: a chibi sticker that follows the system cursor in a click-through layered window.
// Ported from reference\moom-cursor.js; every motion and mood value keeps the web version's number.
// Distances below are CSS pixels from the web version and get multiplied by the monitor's DPI scale.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        bool firstInstance;
        using (var mutex = new Mutex(true, "MomPet.SingleInstance", out firstInstance))
        {
            if (!firstInstance) return;
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                Migration.Run();
                Lang.Load(Path.Combine(baseDir, "lang"), Settings.Language);
                var pet = new Pet(Path.Combine(baseDir, "assets"));
                if (Array.IndexOf(args, "--stats") >= 0) pet.StatsFile = Path.Combine(baseDir, "stats.log");
                pet.Run(Array.IndexOf(args, "--settings") >= 0);
            }
            catch (Exception e)
            {
                Crash(e);
            }
        }
    }

    public static void Crash(Exception e)
    {
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), DateTime.Now + "\r\n" + e);
    }
}

// Until 2026-10-25 the internal name was MoomPet; the first start under the new name moves the settings,
// the custom images and effects, and the start-with-Windows entry over, then removes the old ones.
static class Migration
{
    const string OldName = "MoomPet";

    public static void Run()
    {
        try
        {
            MoveSettings();
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string oldDir = Path.Combine(local, OldName), newDir = Path.Combine(local, Settings.AppName);
            if (Directory.Exists(oldDir) && !Directory.Exists(newDir)) Directory.Move(oldDir, newDir);
            using (RegistryKey run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                if (run != null && run.GetValue(OldName) != null)
                {
                    run.DeleteValue(OldName, false);
                    Startup.Enabled = true;     // now pointing at this exe under the new name
                }
        }
        catch (Exception e)
        {
            Program.Crash(e);                   // a failed move must not stop the pet from starting
        }
    }

    static void MoveSettings()
    {
        bool copy;
        using (RegistryKey current = Registry.CurrentUser.OpenSubKey(@"Software\" + Settings.AppName))
            copy = current == null;
        using (RegistryKey old = Registry.CurrentUser.OpenSubKey(@"Software\" + OldName))
        {
            if (old == null) return;
            if (copy)
                using (RegistryKey dst = Registry.CurrentUser.CreateSubKey(@"Software\" + Settings.AppName))
                    foreach (string name in old.GetValueNames())
                        dst.SetValue(name, old.GetValue(name), old.GetValueKind(name));
        }
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\" + OldName, false);
    }
}

sealed class Pet
{
    // one pose per pointer in the Windows mouse settings (in that order), then the click reactions;
    // a pose without its own image uses the "default" one. Names are also the file names in image packs.
    public static readonly string[] Poses = { "default", "help", "working", "busy", "zoom", "handwriting", "pen",
        "unavailable", "resize_ns", "resize_we", "resize_nwse", "resize_nesw", "move", "alternate", "link",
        "location", "person", "idle", "float",
        "happy", "surprised", "flustered", "annoyed", "angry", "dizzy", "sulk" };
    public static readonly string CustomDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Settings.AppName, "custom");
    // system cursor id -> pose; the arrow and cursors drawn by apps themselves show "default".
    // 32631 (handwriting pen) is not in the documented IDC_ list, but LoadCursor returns the shared system handle.
    static readonly int[] CursorIds = { 32651, 32650, 32514, 32515, 32513, 32631, 32648, 32645, 32644, 32642, 32643,
        32646, 32516, 32649, 32671, 32672 };
    static readonly string[] CursorPoses = { "help", "working", "busy", "zoom", "handwriting", "pen", "unavailable",
        "resize_ns", "resize_we", "resize_nwse", "resize_nesw", "move", "alternate", "link", "location", "person" };
    const int IDC_HAND = 32649;

    // click reactions: a streak of clicks on the same spot raises her mood step by step; the streak goes on
    // while each click comes within CLICK_GAP seconds of the one before (the web version's 1.5 s window over
    // all clicks needed 10+ clicks a second to reach 16, too hard even with an auto clicker)
    const double CLICK_GAP = 0.8;
    const double SAME_SPOT = 60;
    const int MAX_ON_TARGET = 3;            // clicking with the hand cursor never goes past "surprised"
    const double OFFSET_X = 6, OFFSET_Y = 4;
    // every pose is scaled to fit a box the character sets in its character.txt (Hein's images are all 104 wide
    // and at most 167 tall); the float pose flies sideways, so it may be wider to keep her head the same size
    double petWidth = 104, petHeight = 168, floatWidth = 200;
    bool sleepZ = true;                     // Zs drift up from her head in the idle pose
    // pet canvas, in CSS px: big enough for the tallest pose tilted, squashed and popping
    const double CANVAS_W = 360, CANVAS_H = 320, ANCHOR_X = 180, ANCHOR_Y = 230;
    const double IDLE_AFTER = 8;            // seconds without pointer movement or clicks before she dozes off
    const double FLOAT_ON = 900, FLOAT_OFF = 300;   // her own speed in CSS px per second
    const double Z_SIZE = 130;

    readonly Dictionary<string, PoseImage> sources = new Dictionary<string, PoseImage>();
    readonly Dictionary<string, Sprite[]> sprites = new Dictionary<string, Sprite[]>();   // one per frame
    readonly Dictionary<IntPtr, string> cursorPoses = new Dictionary<IntPtr, string>();
    readonly IntPtr handCursor;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Random rng = new Random();
    readonly List<Click> pending = new List<Click>();     // filled by the hook thread, locked
    readonly List<Action> posted = new List<Action>();    // work from the UI thread for the render thread, locked
    readonly List<Click> clicks = new List<Click>();
    readonly List<Burst> bursts = new List<Burst>();
    readonly List<Zee> zees = new List<Zee>();
    readonly Native.HookProc hookProc;
    readonly string assetDir;
    IntPtr hook;
    uint hookThreadId, renderThreadId;
    Thread renderThread;
    volatile bool overHand;                 // the system cursor is the hand, read by the hook thread
    volatile bool quitting, settingsOpen;

    // Threads: the UI thread runs the tray icon and the settings page; the render thread owns every
    // Layer and runs Frame; the hook thread only queues clicks. Fields below without a note belong to
    // the render thread.
    Layer layer;
    NotifyIcon tray;                        // UI thread
    SettingsForm settingsForm;              // UI thread
    volatile bool paused;
    bool hideFromCapture, fullscreen, reduceMotion, needSnap = true;
    double alpha;                           // whole-pet fade, 0..1
    double scale = 0;                       // DPI scale of the monitor under the cursor
    double size = Settings.SizePercent / 100.0;   // the size chosen in settings
    double zoom = 0;                        // scale * size: how big she and her effects are drawn
    double last, nextCheck;
    long lastFrameAt, lastVBlankTarget;

    // MomPet.exe --stats: every 5 s, append frame pacing numbers to stats.log next to the exe
    public string StatsFile;
    double statStart, statWork, statMaxGap;
    int statFrames, statLong;
    double lastActivity;                    // last time the cursor moved or a button went down
    int lastX = int.MinValue, lastY;

    double mx, my;                          // cursor, physical px
    double px, py, vx, vy;                  // pet position and velocity
    double squash, squashV;                 // click squash spring
    double hop, hopV;                       // click jump (negative = up)
    double wig, wigV;                       // click wiggle (deg)
    string pose = "default";
    double poseStart;                       // when the current pose came up; animations start from their first frame
    string hoverPose = "default";           // pose picked by the system cursor shape
    string reaction;                        // click reaction, shown over hoverPose while set
    int peakMood;
    double calmAt;                          // when the current reaction ends; 0 = none
    bool sulking;
    double popStart = -1;
    bool floating, facingLeft;
    double floatHold, nextZ;
    double headX, headY;                    // screen position above her head where sleep Zs start
    int zCount;

    CharacterInfo character;                // UI thread: the built-in character chosen in settings

    public Pet(string assetDir)
    {
        this.assetDir = assetDir;
        character = CharacterInfo.Find(Settings.Character);
        foreach (string p in Poses) sources[p] = LoadPose(p);
        ApplyLook(Look());
        // system cursor handles stay fixed when the cursor scheme changes, so one lookup is enough
        for (int i = 0; i < CursorIds.Length; i++)
            cursorPoses[Native.LoadCursor(IntPtr.Zero, (IntPtr)CursorIds[i])] = CursorPoses[i];
        handCursor = Native.LoadCursor(IntPtr.Zero, (IntPtr)IDC_HAND);
        hookProc = OnMouse;
    }

    // showSettings: open the settings page right away (MomPet.exe --settings)
    public void Run(bool showSettings)
    {
        hideFromCapture = Settings.HideFromCapture;
        string fxError;
        effect = LoadEffect(Settings.Effect, out fxError);
        if (fxError != null) effect = LoadEffect(EffectDef.DefaultId, out fxError);
        renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "render" };
        renderThread.SetApartmentState(ApartmentState.STA);
        renderThread.Start();
        tray = new NotifyIcon { Icon = MakeTrayIcon(), Text = Lang.T("app.name"), Visible = true };
        var menu = new ContextMenu();
        menu.MenuItems.Add(Lang.T("tray.settings"), delegate { ShowSettings(); });
        menu.MenuItems.Add(Lang.T("tray.quit"), delegate { Quit(); });
        tray.ContextMenu = menu;
        tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };

        // Windows waits for a low-level hook on every mouse event system-wide, so the hook gets a
        // thread of its own that only queues clicks and never waits for drawing or sleeps
        var hookThread = new Thread(HookLoop) { IsBackground = true, Name = "mouse hook" };
        hookThread.Start();
        if (showSettings) ShowSettings();
        Application.Run();
    }

    void HookLoop()
    {
        hookThreadId = Native.GetCurrentThreadId();
        hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, hookProc, Native.GetModuleHandle(null), 0);
        Native.MSG msg;
        while (Native.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) { }
        Native.UnhookWindowsHookEx(hook);
    }

    // ---- used by the settings page ----

    public bool Paused
    {
        get { return paused; }
        set { paused = value; }
    }

    public bool HideFromCapture
    {
        get { return Settings.HideFromCapture; }
        set
        {
            Settings.HideFromCapture = value;
            Post(delegate
            {
                hideFromCapture = value;
                layer.HideFromCapture(value);
                foreach (Burst b in bursts) b.Layer.HideFromCapture(value);
                foreach (Zee z in zees) z.Layer.HideFromCapture(value);
            });
        }
    }

    // 50-200, percent of the normal size; saved, and applied on the render thread
    public int SizePercent
    {
        get { return Settings.SizePercent; }
        set
        {
            Settings.SizePercent = value;
            Post(delegate { size = value / 100.0; });
        }
    }

    EffectDef effect;                       // render thread; null when click effects are off

    // Loads an effect by id (or EffectDef.Off) and switches to it. Call from the UI thread; returns the
    // error to show when the effect file is wrong, or null. Old definitions are left for bursts still playing.
    public string SetEffect(string id)
    {
        string error;
        EffectDef def = LoadEffect(id, out error);
        if (error != null) return error;
        Settings.Effect = id;
        Post(delegate { effect = def; });
        return null;
    }

    static EffectDef LoadEffect(string id, out string error)
    {
        error = null;
        if (id == EffectDef.Off) return null;
        string folder;
        if (!EffectDef.All().TryGetValue(id, out folder))
        {
            error = Lang.T("fx.err.missing", id);
            return null;
        }
        try { return EffectDef.Load(folder); }
        catch (Exception e)
        {
            error = e.Message;
            return null;
        }
    }

    public volatile bool IdleHide = Settings.IdleHide;
    public volatile int IdleSeconds = Settings.IdleSeconds;

    // runs work on the render thread before its next frame
    void Post(Action work)
    {
        lock (posted) posted.Add(work);
        Native.PostThreadMessage(renderThreadId, 0 /* WM_NULL: wake it up */, IntPtr.Zero, IntPtr.Zero);
    }

    public static bool HasCustom(string pose)
    {
        return PoseFiles.Exists(CustomDir, pose);
    }

    public CharacterInfo Character { get { return character; } }

    // switches to another built-in character; call from the UI thread
    public void SetCharacter(string id)
    {
        character = CharacterInfo.Find(id);
        Settings.Character = character.Id;
        ReloadPoses();
    }

    // Which image a pose shows: its custom image; else, once "default" has a custom image, that one (so a
    // custom character is never mixed with a built-in one); else the character's image for the pose; else
    // the character's "default".
    public PoseImage LoadPose(string pose)
    {
        CharacterInfo custom = CharacterInfo.Load(CustomDir);
        PoseImage img = PoseFiles.Read(CustomDir, pose, custom);
        if (img == null && pose != "default") img = PoseFiles.Read(CustomDir, "default", custom);
        if (img == null) img = PoseFiles.Read(character.Dir, pose, character);
        if (img == null) img = PoseFiles.Read(character.Dir, "default", character);
        return img ?? PoseImage.Still(new Bitmap(1, 1));
    }

    // whether the pose shows an image of its own rather than falling back to "default"
    public bool HasOwnImage(string pose)
    {
        if (pose == "default" || HasCustom(pose)) return true;
        return !HasCustom("default") && PoseFiles.Exists(character.Dir, pose);
    }

    // the box and sleep Zs come from the custom images' character.txt once she is a custom character
    sealed class LookInfo { public double W, H, FloatW; public bool SleepZ; }

    LookInfo Look()
    {
        CharacterInfo c = HasCustom("default") ? CharacterInfo.Load(CustomDir) : character;
        return new LookInfo { W = c.Width, H = c.Height, FloatW = c.FloatWidth, SleepZ = c.SleepZ };
    }

    // render thread (or before it starts)
    void ApplyLook(LookInfo look)
    {
        petWidth = look.W;
        petHeight = look.H;
        floatWidth = look.FloatW;
        sleepZ = look.SleepZ;
    }

    public static Bitmap ReadBitmap(string path)
    {
        using (var ms = new MemoryStream(File.ReadAllBytes(path)))
        using (var img = Image.FromStream(ms))
            return new Bitmap(img);
    }

    // call from the UI thread after custom images or the character changed; since every pose may fall back to
    // "default", all poses are reloaded
    public void ReloadPoses()
    {
        var fresh = new Dictionary<string, PoseImage>();
        foreach (string p in Poses) fresh[p] = LoadPose(p);
        LookInfo look = Look();
        Post(delegate
        {
            ApplyLook(look);
            foreach (string p in Poses)
            {
                sources[p].Dispose();
                sources[p] = fresh[p];
                DropSprites(p);
                if (scale > 0) GetSprites(p);
            }
        });
    }

    void DropSprites(string p)
    {
        Sprite[] frames;
        if (!sprites.TryGetValue(p, out frames)) return;
        foreach (Sprite sp in frames) sp.Image.Dispose();
        sprites.Remove(p);
    }

    // call from the UI thread after the language changed
    public void ApplyLanguage()
    {
        tray.Text = Lang.T("app.name");
        tray.ContextMenu.MenuItems[0].Text = Lang.T("tray.settings");
        tray.ContextMenu.MenuItems[1].Text = Lang.T("tray.quit");
        if (settingsForm != null && !settingsForm.IsDisposed)
        {
            Point at = settingsForm.Location;
            settingsForm.Close();
            ShowSettings();
            settingsForm.Location = at;
        }
    }

    public void ShowSettings()
    {
        if (settingsForm == null || settingsForm.IsDisposed)
        {
            settingsForm = new SettingsForm(this) { Icon = tray.Icon };
            settingsForm.FormClosed += delegate { settingsOpen = false; };
            settingsOpen = true;
            settingsForm.Show();
        }
        if (settingsForm.WindowState == FormWindowState.Minimized) settingsForm.WindowState = FormWindowState.Normal;
        settingsForm.Activate();
    }

    void Quit()
    {
        if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.Close();
        quitting = true;
        Native.PostThreadMessage(renderThreadId, 0, IntPtr.Zero, IntPtr.Zero);
        renderThread.Join(1000);
        Native.PostThreadMessage(hookThreadId, 0x12 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
        tray.Visible = false;
        tray.Dispose();
        Application.Exit();
    }

    // a yellow cat face, drawn at the tray's icon size
    Icon MakeTrayIcon()
    {
        Size size = SystemInformation.SmallIconSize;
        var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using (var cat = new Bitmap(Path.Combine(assetDir, "cat.png")))
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(cat, 0, 0, size.Width, size.Height);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    // ---- render thread ----

    void RenderLoop()
    {
        try
        {
            renderThreadId = Native.GetCurrentThreadId();
            layer = new Layer();
            layer.HideFromCapture(hideFromCapture);
            // a high resolution waitable timer (Windows 10 1803+) wakes within a fraction of a millisecond;
            // Thread.Sleep follows the system timer and can oversleep by 15.6 ms, which made her stutter
            IntPtr timer = Native.CreateWaitableTimerEx(IntPtr.Zero, null, 2 /* HIGH_RESOLUTION */, 0x1F0003);
            if (timer == IntPtr.Zero) timer = Native.CreateWaitableTimerEx(IntPtr.Zero, null, 0, 0x1F0003);
            last = clock.Elapsed.TotalSeconds;
            long next = 0;
            Native.MSG msg;
            while (!quitting)
            {
                while (Native.PeekMessage(out msg, IntPtr.Zero, 0, 0, 1 /* PM_REMOVE */))
                {
                    Native.TranslateMessage(ref msg);
                    Native.DispatchMessage(ref msg);
                }
                Action[] work;
                lock (posted)
                {
                    work = posted.ToArray();
                    posted.Clear();
                }
                foreach (Action a in work) a();

                long now = Stopwatch.GetTimestamp();
                if (now < next)
                {
                    long due = -(next - now) * 10000000 / Stopwatch.Frequency;   // relative, in 100 ns
                    Native.SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false);
                    Native.MsgWaitForMultipleObjectsEx(1, new[] { timer }, 0xFFFFFFFF, 0x4FF /* QS_ALLINPUT */,
                        4 /* MWMO_INPUTAVAILABLE */);
                    continue;
                }
                long gap = now - lastFrameAt;
                lastFrameAt = now;
                Frame();
                if (StatsFile != null) Stats(now, gap);
                next = NextFrameAt(Stopwatch.GetTimestamp());
            }
            layer.DestroyHandle();
            foreach (Burst b in bursts) b.Layer.DestroyHandle();
            foreach (Zee z in zees) z.Layer.DestroyHandle();
        }
        catch (Exception e)
        {
            Program.Crash(e);
            Environment.Exit(1);
        }
    }

    // About 60 frames a second, each started just after a monitor refresh so every frame stays on screen
    // for the same number of refreshes: 1 at 60 Hz, 2 at 120 Hz, 4 at 240 Hz.
    long NextFrameAt(long now)
    {
        long f = Stopwatch.Frequency;
        if (alpha <= 0 && bursts.TrueForAll(b => !b.Active) && zees.TrueForAll(z => !z.Active))
            return now + f / 20;            // hidden: check 20 times a second
        var ti = new Native.DWM_TIMING_INFO { cbSize = 292, rest = new byte[256] };
        if (Native.DwmGetCompositionTimingInfo(IntPtr.Zero, ref ti) != 0 || ti.qpcRefreshPeriod == 0)
            return now + f / 60;
        long period = (long)ti.qpcRefreshPeriod, vblank = (long)ti.qpcVBlank;
        long refreshes = Math.Max(1, (long)Math.Round(f / 60.0 / period));
        long target = lastVBlankTarget + refreshes * period;
        target = vblank + (long)Math.Round((double)(target - vblank) / period) * period;   // back onto the refresh grid
        if (target <= now) target = vblank + ((now - vblank) / period + 1) * period;      // fell behind: next refresh
        lastVBlankTarget = target;
        return target + f / 2000;           // half a millisecond after the refresh
    }

    void Stats(long now, long gapTicks)
    {
        if (alpha <= 0) return;             // only count frames where she is on screen
        double start = (double)now / Stopwatch.Frequency, gap = (double)gapTicks / Stopwatch.Frequency;
        statFrames++;
        statWork += (double)(Stopwatch.GetTimestamp() - now) / Stopwatch.Frequency;
        statMaxGap = Math.Max(statMaxGap, gap);
        if (gap > 0.020) statLong++;
        if (statStart == 0) statStart = start;
        if (start - statStart < 5) return;
        File.AppendAllText(StatsFile, string.Format(
            "{0:HH:mm:ss} fps {1:F1}  longest gap {2:F1} ms  gaps over 20 ms {3}  work {4:F2} ms/frame  settings open {5}\r\n",
            DateTime.Now, statFrames / (start - statStart), statMaxGap * 1000, statLong, statWork / Math.Max(1, statFrames) * 1000,
            settingsOpen));
        statStart = start; statFrames = 0; statLong = 0; statWork = 0; statMaxGap = 0;
    }

    IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        int m = wParam.ToInt32();
        if (code >= 0 && (m == Native.WM_LBUTTONDOWN || m == Native.WM_RBUTTONDOWN || m == Native.WM_MBUTTONDOWN))
        {
            var info = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
            // the hook only queues the click; the frame loop handles it, so the hook stays fast
            lock (pending) pending.Add(new Click { X = info.x, Y = info.y, OnTarget = overHand });
        }
        return Native.CallNextHookEx(hook, code, wParam, lParam);
    }

    void Frame()
    {
        double now = clock.Elapsed.TotalSeconds;
        double dt = Math.Min(0.05, now - last);
        last = now;

        var ci = new Native.CURSORINFO { cbSize = Marshal.SizeOf(typeof(Native.CURSORINFO)) };
        bool showing = Native.GetCursorInfo(ref ci) && (ci.flags & 1) != 0 && ci.hCursor != IntPtr.Zero;
        if (showing)
        {
            mx = ci.x; my = ci.y;
            overHand = ci.hCursor == handCursor;
            string p;
            hoverPose = cursorPoses.TryGetValue(ci.hCursor, out p) ? p : "default";
        }

        var pt = new Native.POINT { x = (int)mx, y = (int)my };
        IntPtr monitor = Native.MonitorFromPoint(pt, 2 /* MONITOR_DEFAULTTONEAREST */);
        uint dpiX, dpiY;
        double s = Native.GetDpiForMonitor(monitor, 0, out dpiX, out dpiY) == 0 ? dpiX / 96.0 : 1;
        if (s != scale || s * size != zoom)
        {
            scale = s;
            zoom = s * size;
            foreach (string p in Poses) DropSprites(p);
            // render every pose now, so switching to a pose for the first time never stalls a frame
            foreach (string p in Poses) GetSprites(p);
            layer.Resize((int)Math.Ceiling(CANVAS_W * zoom), (int)Math.Ceiling(CANVAS_H * zoom));
        }
        if (now >= nextCheck)
        {
            nextCheck = now + 0.5;
            reduceMotion = !Native.ClientAreaAnimation();
            fullscreen = Native.FullscreenOn(monitor);
            layer.KeepOnTop();
        }

        Click[] queued;
        lock (pending)
        {
            queued = pending.ToArray();
            pending.Clear();
        }
        if (ci.x != lastX || ci.y != lastY || queued.Length > 0)
        {
            lastActivity = now;
            lastX = ci.x; lastY = ci.y;
        }
        bool resting = IdleHide && now - lastActivity > IdleSeconds;
        bool visible = showing && !fullscreen && !paused && !resting;
        foreach (Click c in queued)
            if (visible && alpha > 0) OnClick(now, c);

        if (calmAt > 0 && now >= calmAt)
        {
            // after she has been poked a lot, she sulks for a moment before calming down
            if (!sulking && peakMood >= 8)
            {
                sulking = true;
                reaction = "sulk";
                calmAt = now + 1.8;
            }
            else Calm();
        }
        // with the plain arrow pointer she floats while pulled along fast and dozes off when left alone
        double cssSpeed = Math.Sqrt(vx * vx + vy * vy) / scale;
        if (cssSpeed > FLOAT_ON) floating = true;
        if (floating && cssSpeed > FLOAT_OFF) floatHold = now + 0.2;
        if (floating && now > floatHold) floating = false;
        if (vx / scale > 60) facingLeft = false;
        else if (vx / scale < -60) facingLeft = true;
        bool dozing = now - lastActivity > IDLE_AFTER;
        string next = reaction ?? (hoverPose != "default" ? hoverPose : floating ? "float" : dozing ? "idle" : "default");
        if (pose == "idle" && next != "idle" && !reduceMotion)
        {
            hopV -= 300;                    // startled awake
            squashV -= 0.8;
        }
        SetPose(next, now);

        foreach (Burst b in bursts) if (b.Active) DrawBurst(b, now);
        if (pose == "idle" && sleepZ && !reduceMotion && alpha > 0.9 && now >= nextZ)
        {
            SpawnZ(now);
            nextZ = now + 1.3;
        }
        foreach (Zee z in zees) if (z.Active) DrawZ(z, now);

        if (paused) alpha = 0;
        else alpha = Math.Max(0, Math.Min(1, alpha + (visible ? dt : -dt) / 0.15));
        if (alpha <= 0)
        {
            layer.Hide();
            needSnap = true;
            return;
        }

        double tx = mx + OFFSET_X * scale, ty = my + OFFSET_Y * scale;
        // near the bottom or right edge of the monitor she stops at the edge instead of sliding off it
        Sprite size0 = GetSprites(pose)[0];
        var mon = new Native.MONITORINFO { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO)) };
        if (Native.GetMonitorInfo(monitor, ref mon))
        {
            tx = Math.Min(tx, mon.rcMonitor.Right - size0.W);
            ty = Math.Min(ty, mon.rcMonitor.Bottom - size0.H);
        }
        if (needSnap)
        {
            px = tx; py = ty; vx = vy = 0;
            needSnap = false;
        }
        if (reduceMotion)
        {
            px = tx; py = ty; vx = vy = 0;
            squash = squashV = hop = hopV = wig = wigV = 0;
        }
        else
        {
            // spring toward the pointer, slightly underdamped so it bounces a little
            const double k = 170, d = 17;
            vx += ((tx - px) * k - vx * d) * dt;
            vy += ((ty - py) * k - vy * d) * dt;
            px += vx * dt; py += vy * dt;
            squashV += (-squash * 260 - squashV * 14) * dt;
            squash += squashV * dt;
            hopV += (-hop * 220 - hopV * 9) * dt;
            hop += hopV * dt;
            wigV += (-wig * 380 - wigV * 10) * dt;
            wig += wigV * dt;
        }
        double cssVx = vx / scale, cssVy = vy / scale;
        double tilt = Math.Max(-14, Math.Min(14, cssVx * 0.025)) + wig;
        double shakeX = 0, shakeY = 0;
        if (!reduceMotion && pose == "angry")          // trembling with rage
        {
            shakeX = (rng.NextDouble() - 0.5) * 4;
            shakeY = (rng.NextDouble() - 0.5) * 3;
        }
        else if (!reduceMotion && pose == "dizzy")     // swaying in circles
        {
            tilt += Math.Sin(now * 1000 / 140) * 9;
            shakeX = Math.Cos(now * 1000 / 140) * 3;
        }
        double speed = Math.Min(1, Math.Sqrt(cssVx * cssVx + cssVy * cssVy) / 1600);
        double breathe = reduceMotion ? 0 : Math.Sin(now * 1000 / 520) * 0.025 * (1 - speed);
        if (!reduceMotion && pose == "idle")                // asleep: slower, deeper breaths and a gentle sway
        {
            breathe = Math.Sin(now * 2 * Math.PI / 3.0) * 0.045;
            tilt += Math.Sin(now * 2 * Math.PI / 4.0) * 4;
        }
        double sy = 1 + squash * 1.2 + breathe - speed * 0.06;
        double sx = 1 - squash * 0.9 - breathe * 0.6 + speed * 0.05;
        double pop = 1;
        if (popStart >= 0 && !reduceMotion)
        {
            double t = (now - popStart) / 0.32;
            if (t >= 1) popStart = -1;
            else pop = 0.7 + 0.3 * Ease.Bezier(.3, 1.6, .5, 1, t);
        }

        Sprite sprite = GetSprites(pose)[reduceMotion ? 0 : sources[pose].FrameAt(now - poseStart)];
        float w = sprite.W, h = sprite.H;
        // transform origin is 50% 90% of the pet box, as in the web version
        double ox = px + shakeX * zoom + 0.5 * w, oy = py + (hop + shakeY) * zoom + 0.9 * h;
        // place the origin near the window anchor; keep the fraction so slow movement stays smooth
        double ax = Math.Round(ANCHOR_X * zoom), ay = Math.Round(ANCHOR_Y * zoom);
        int wx = (int)Math.Floor(ox - ax), wy = (int)Math.Floor(oy - ay);
        var rect = new RectangleF(-0.5f * w - sprite.Pad, -0.9f * h - sprite.Pad, sprite.Image.Width, sprite.Image.Height);
        using (var m = new Matrix())
        {
            m.Translate((float)(ox - wx), (float)(oy - wy));
            m.Rotate((float)tilt);
            m.Scale((float)sx, (float)sy);
            if (pop != 1)
            {
                // the pop scales the image around its own centre, 40% of its height above the origin
                m.Translate(0, -0.4f * h);
                m.Scale((float)pop, (float)pop);
                m.Translate(0, 0.4f * h);
            }
            // the float image flies to the right; mirrored when she is pulled to the left
            if (pose == "float" && facingLeft) m.Scale(-1, 1);
            headX = ox + 0.28 * w;
            headY = oy - 0.75 * h;
            // only the part of the canvas the pose covers is redrawn and copied to the window
            Rectangle box = Bounds(m, rect, layer.Width, layer.Height);
            m.Translate(rect.X, rect.Y);           // now maps sprite pixels to canvas pixels
            Rectangle dirty = layer.DrawSprite(sprite, m, box);
            layer.Present(wx, wy, (byte)Math.Round(alpha * 255), dirty);
        }
    }

    static Rectangle Bounds(Matrix m, RectangleF r, int maxW, int maxH)
    {
        PointF[] pts = { new PointF(r.Left, r.Top), new PointF(r.Right, r.Top),
                         new PointF(r.Left, r.Bottom), new PointF(r.Right, r.Bottom) };
        m.TransformPoints(pts);
        float x0 = pts[0].X, y0 = pts[0].Y, x1 = x0, y1 = y0;
        foreach (PointF p in pts)
        {
            x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y);
            x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y);
        }
        int left = Math.Max(0, (int)Math.Floor(x0) - 1), top = Math.Max(0, (int)Math.Floor(y0) - 1);
        int right = Math.Min(maxW, (int)Math.Ceiling(x1) + 1), bottom = Math.Min(maxH, (int)Math.Ceiling(y1) + 1);
        return new Rectangle(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    void OnClick(double now, Click c)
    {
        if (reduceMotion)
        {
            React(now, c);
            return;
        }
        squashV -= 1.6;
        hopV -= 420;
        wigV += (rng.NextDouble() < 0.5 ? -1 : 1) * 260;
        React(now, c);
        SpawnBurst(now, c.X, c.Y);
    }

    void React(double now, Click c)
    {
        double spot = SAME_SPOT * scale;
        if (clicks.Count > 0)
        {
            Click prev = clicks[clicks.Count - 1];
            if (now - prev.T > CLICK_GAP || Math.Sqrt((prev.X - c.X) * (prev.X - c.X) + (prev.Y - c.Y) * (prev.Y - c.Y)) >= spot)
                clicks.Clear();
        }
        c.T = now;
        clicks.Add(c);
        int count = c.OnTarget ? Math.Min(clicks.Count, MAX_ON_TARGET) : clicks.Count;
        string mood = count >= 16 ? "dizzy" : count >= 11 ? "angry" : count >= 8 ? "annoyed"
            : count >= 5 ? "flustered" : count >= 3 ? "surprised" : "happy";
        peakMood = Math.Max(peakMood, count);
        reaction = mood;
        sulking = false;
        calmAt = now + (mood == "dizzy" ? 1.6 : mood == "angry" ? 1.3 : 0.9);
    }

    void Calm()
    {
        reaction = null;
        peakMood = 0;
        clicks.Clear();
        calmAt = 0;
        sulking = false;
    }

    void SetPose(string next, double now)
    {
        if (next == pose) return;
        pose = next;
        popStart = now;
        poseStart = now;
    }

    // every frame of a pose, scaled for the current zoom; the frames of an animation share one scale, taken
    // from the first frame, so a frame drawn a little bigger does not shrink the others
    Sprite[] GetSprites(string name)
    {
        Sprite[] frames;
        if (!sprites.TryGetValue(name, out frames))
        {
            PoseImage src = sources[name];
            double maxW = name == "float" ? floatWidth : petWidth;
            double fit = Math.Min(maxW / src.Frames[0].Width, petHeight / src.Frames[0].Height);
            frames = new Sprite[src.Frames.Length];
            for (int i = 0; i < frames.Length; i++)
                frames[i] = Sprite.Render(src.Frames[i], src.Frames[i].Width * fit * zoom, src.Frames[i].Height * fit * zoom, zoom);
            sprites[name] = frames;
        }
        return frames;
    }

    // ---- click burst: a ring and particles from the chosen effect file, each burst in its own small window ----

    void SpawnBurst(double now, int x, int y)
    {
        EffectDef def = effect;
        if (def == null) return;            // effects switched off
        Burst b = bursts.Find(o => !o.Active);
        if (b == null)
        {
            if (bursts.Count >= 12) return;
            b = new Burst { Layer = new Layer() };
            b.Layer.HideFromCapture(hideFromCapture);
            bursts.Add(b);
        }
        b.Active = true;
        b.Def = def;
        b.Start = now;
        b.X = x; b.Y = y;
        b.Scale = zoom;
        b.StartAngle = rng.NextDouble() * Math.PI;
        b.Seed = rng.Next();
        int side = (int)Math.Ceiling(def.Size * zoom);
        b.Layer.Resize(side, side);
    }

    static readonly Color Wine = Color.FromArgb(0x8b, 0x1e, 0x3f);
    static readonly GraphicsPath Star = MakeStar();
    static readonly GraphicsPath Heart = MakeHeart();
    static readonly GraphicsPath Dot = MakeDot();
    readonly EffectContext fxc = new EffectContext();

    void DrawBurst(Burst b, double now)
    {
        EffectDef def = b.Def;
        double time = now - b.Start;
        if (time >= def.Duration)
        {
            b.Active = false;
            b.Layer.Hide();
            return;
        }
        float s = (float)b.Scale;
        float c = b.Layer.Width / 2f;
        Graphics g = b.Layer.G;
        g.ResetTransform();
        g.Clear(Color.Transparent);
        EffectContext x = fxc;
        x.Time = time;
        x.T = time / def.Duration;
        x.Seed = b.Seed;
        x.N = def.Count;

        if (def.Ring)
        {
            x.I = 0;
            x.Angle = b.StartAngle;
            double a = Clamp01(def.RingAlpha(x));
            float k = (float)def.RingScale(x) * s;
            float r = (float)def.RingRadius(x), w = (float)def.RingWidth(x), edge = (float)def.RingEdge(x);
            if (a > 0 && k > 0 && r > 0)
            {
                int al = (int)Math.Round(255 * a);
                // the edge sits just outside the ring line, like the white border of the web version
                float er = r + w / 2 + edge / 2;
                if (edge > 0)
                    using (var pen = new Pen(Color.FromArgb(al, def.EdgeColor), edge * k))
                        g.DrawEllipse(pen, c - er * k, c - er * k, 2 * er * k, 2 * er * k);
                if (w > 0)
                    using (var pen = new Pen(Color.FromArgb(al, def.RingColor), w * k))
                        g.DrawEllipse(pen, c - r * k, c - r * k, 2 * r * k, 2 * r * k);
            }
        }

        using (var glow = new Pen(Color.White, 1.6f) { LineJoin = LineJoin.Round })
        using (var fill = new SolidBrush(Color.White))
        using (var faded = new ImageAttributes())
        {
            for (int i = 0; i < def.Count; i++)
            {
                x.I = i;
                x.Angle = b.StartAngle + (double)i / def.Count * Math.PI * 2;
                double a = Clamp01(def.Alpha(x));
                float k = (float)def.Scale(x) * s;
                if (a <= 0 || k <= 0) continue;
                ParticleKind kind = def.Kinds[i % def.Kinds.Count];
                g.ResetTransform();
                g.TranslateTransform(c + (float)def.X(x) * s, c + (float)def.Y(x) * s);
                g.ScaleTransform(k, k);
                g.RotateTransform((float)def.Spin(x));
                int al = (int)Math.Round(255 * a);
                if (kind.Shape == ParticleKind.Picture)
                {
                    // a picture is drawn 16 px on its longest side before scaling
                    float f = 16f / Math.Max(kind.Image.Width, kind.Image.Height);
                    float iw = kind.Image.Width * f, ih = kind.Image.Height * f;
                    faded.SetColorMatrix(new ColorMatrix { Matrix33 = (float)a });
                    g.DrawImage(kind.Image, new[] { new PointF(-iw / 2, -ih / 2), new PointF(iw / 2, -ih / 2), new PointF(-iw / 2, ih / 2) },
                        new RectangleF(0, 0, kind.Image.Width, kind.Image.Height), GraphicsUnit.Pixel, faded);
                    continue;
                }
                fill.Color = Color.FromArgb(al, kind.Color);
                GraphicsPath shape = kind.Shape == ParticleKind.Heart ? Heart : kind.Shape == ParticleKind.Dot ? Dot : Star;
                if (kind.Shape != ParticleKind.Heart)
                {
                    glow.Color = Color.FromArgb(al, Color.White);
                    g.DrawPath(glow, shape);    // stars and dots get a thin white glow, as on the web page
                }
                g.FillPath(fill, shape);
            }
        }
        b.Layer.Present(b.X - (int)c, b.Y - (int)c, 255);
    }

    static double Clamp01(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }

    static GraphicsPath MakeDot()
    {
        var path = new GraphicsPath();
        path.AddEllipse(-4, -4, 8, 8);
        return path;
    }

    // ---- sleep Zs: while she dozes, a small z and a big Z take turns drifting up from her head ----

    static readonly GraphicsPath ZGlyph = MakeZ();

    void SpawnZ(double now)
    {
        Zee z = zees.Find(o => !o.Active);
        if (z == null)
        {
            if (zees.Count >= 4) return;
            z = new Zee { Layer = new Layer() };
            z.Layer.HideFromCapture(hideFromCapture);
            zees.Add(z);
        }
        z.Active = true;
        z.Start = now;
        z.X = (int)headX; z.Y = (int)headY;
        z.Scale = zoom;
        z.Big = zCount++ % 2 == 1;
        int side = (int)Math.Ceiling(Z_SIZE * zoom);
        z.Layer.Resize(side, side);
    }

    void DrawZ(Zee z, double now)
    {
        double t = (now - z.Start) / 2.0;   // each Z lives 2 seconds
        if (t >= 1)
        {
            z.Active = false;
            z.Layer.Hide();
            return;
        }
        float s = (float)z.Scale;
        float c = z.Layer.Width / 2f;
        Graphics g = z.Layer.G;
        g.ResetTransform();
        g.Clear(Color.Transparent);
        // drifts up and to the right with a little side-to-side sway, grows, fades in quickly and out slowly
        double rise = Ease.Bezier(.2, .6, .4, 1, t);
        float dx = (float)(Math.Sin(t * Math.PI * 2) * 5 + 16 * rise) * s;
        float dy = (float)(-46 * rise) * s;
        float k = (float)((z.Big ? 1.0 : 0.68) * (0.6 + 0.5 * rise)) * s;
        double fade = t < 0.15 ? t / 0.15 : t > 0.6 ? 1 - (t - 0.6) / 0.4 : 1;
        int a = (int)Math.Round(255 * fade);
        g.TranslateTransform(c + dx, c + dy);
        g.RotateTransform((float)(-12 + 10 * Math.Sin(t * Math.PI * 3)));
        g.ScaleTransform(k, k);
        using (var edge = new Pen(Color.FromArgb(a, Color.White), 4f) { LineJoin = LineJoin.Round })
        using (var fill = new SolidBrush(Color.FromArgb(a, Wine)))
        {
            g.DrawPath(edge, ZGlyph);
            g.FillPath(fill, ZGlyph);
        }
        z.Layer.Present(z.X - (int)c, z.Y - (int)c, 255);
    }

    static GraphicsPath MakeZ()
    {
        // a 22 px bold Z centred on 0,0
        var path = new GraphicsPath();
        using (var family = new FontFamily(System.Drawing.Text.GenericFontFamilies.SansSerif))
            path.AddString("Z", family, (int)FontStyle.Bold, 22f, new PointF(0, 0), StringFormat.GenericTypographic);
        RectangleF b = path.GetBounds();
        using (var m = new Matrix())
        {
            m.Translate(-b.X - b.Width / 2, -b.Y - b.Height / 2);
            path.Transform(m);
        }
        return path;
    }

    sealed class Zee
    {
        public Layer Layer;
        public bool Active, Big;
        public double Start, Scale;
        public int X, Y;
    }

    static GraphicsPath MakeStar()
    {
        // 12px four-point star, same polygon as the web clip-path, centred on 0,0
        float[] pts = { 50, 0, 62, 38, 100, 50, 62, 62, 50, 100, 38, 62, 0, 50, 38, 38 };
        var p = new PointF[8];
        for (int i = 0; i < 8; i++) p[i] = new PointF(pts[i * 2] * 0.12f - 6, pts[i * 2 + 1] * 0.12f - 6);
        var path = new GraphicsPath();
        path.AddPolygon(p);
        return path;
    }

    static GraphicsPath MakeHeart()
    {
        // 11px heart from the web path "M5.5 10 L1 5.5 A2.5 2.5 0 0 1 5.5 2 A2.5 2.5 0 0 1 10 5.5 Z";
        // the arcs are too small for their chords, so SVG grows them into half circles of radius 2.85
        const float r = 2.85f;
        var path = new GraphicsPath();
        path.AddLine(5.5f, 10, 1, 5.5f);
        path.AddArc(3.25f - r, 3.75f - r, 2 * r, 2 * r, 142.1f, 180);
        path.AddArc(7.75f - r, 3.75f - r, 2 * r, 2 * r, 217.9f, 180);
        path.CloseFigure();
        using (var m = new Matrix())
        {
            m.Translate(-5.5f, -5.5f);
            path.Transform(m);
        }
        return path;
    }

    sealed class Click
    {
        public int X, Y;
        public double T;
        public bool OnTarget;
    }

    sealed class Burst
    {
        public Layer Layer;
        public bool Active;
        public double Start, Scale, StartAngle;
        public int X, Y;
        public EffectDef Def;
        public int Seed;
    }
}

// A pose scaled for one DPI, with the web version's drop shadow baked in around it.
sealed class Sprite
{
    public Bitmap Image;
    public uint[] Pixels;                   // Image's premultiplied ARGB, row by row
    public int W, H, Pad;

    // scales src to fit inside maxW x maxH keeping its proportions
    public static Sprite Render(Bitmap src, double maxW, double maxH, double scale)
    {
        double fit = Math.Min(maxW / src.Width, maxH / src.Height);
        int w = Math.Max(1, (int)Math.Round(src.Width * fit));
        int h = Math.Max(1, (int)Math.Round(src.Height * fit));
        int pad = (int)Math.Ceiling(14 * scale);
        int off = (int)Math.Round(4 * scale);
        int W = w + 2 * pad, H = h + 2 * pad;

        var body = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(body))
        using (var ia = new ImageAttributes())
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            ia.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
        }

        // drop-shadow(0 4px 6px rgba(60, 12, 28, .25)): blur the alpha, tint it, put the pose on top
        var a = new int[W * H];
        BitmapData bd = body.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        var pixels = new byte[bd.Stride * h];
        Marshal.Copy(bd.Scan0, pixels, 0, pixels.Length);
        body.UnlockBits(bd);
        for (int y = 0; y < h; y++)
        {
            int ty = y + pad + off;
            if (ty >= H) break;
            for (int x = 0; x < w; x++) a[ty * W + x + pad] = pixels[y * bd.Stride + x * 4 + 3];
        }
        int r = Math.Max(1, (int)Math.Round(3 * scale));   // three box blurs of radius sigma ~ a 6px CSS blur
        for (int i = 0; i < 3; i++)
        {
            BlurRows(a, W, H, r);
            BlurColumns(a, W, H, r);
        }
        var shadow = new byte[W * H * 4];
        for (int i = 0; i < a.Length; i++)
        {
            int al = a[i] / 4;
            shadow[i * 4] = (byte)(28 * al / 255);
            shadow[i * 4 + 1] = (byte)(12 * al / 255);
            shadow[i * 4 + 2] = (byte)(60 * al / 255);
            shadow[i * 4 + 3] = (byte)al;
        }
        var image = new Bitmap(W, H, PixelFormat.Format32bppPArgb);
        BitmapData od = image.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        for (int y = 0; y < H; y++) Marshal.Copy(shadow, y * W * 4, od.Scan0 + y * od.Stride, W * 4);
        image.UnlockBits(od);
        using (var g = Graphics.FromImage(image))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(body, new Rectangle(pad, pad, w, h));
        }
        body.Dispose();
        var pixels32 = new uint[W * H];
        BitmapData pd = image.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        for (int y = 0; y < H; y++)
        {
            var line = new int[W];
            Marshal.Copy(pd.Scan0 + y * pd.Stride, line, 0, W);
            Buffer.BlockCopy(line, 0, pixels32, y * W * 4, W * 4);
        }
        image.UnlockBits(pd);
        return new Sprite { Image = image, Pixels = pixels32, W = w, H = h, Pad = pad };
    }

    static void BlurRows(int[] a, int w, int h, int r)
    {
        var row = new int[w];
        int div = 2 * r + 1;
        for (int y = 0; y < h; y++)
        {
            int o = y * w, sum = 0;
            for (int i = 0; i <= r && i < w; i++) sum += a[o + i];
            for (int x = 0; x < w; x++)
            {
                row[x] = sum / div;
                if (x + r + 1 < w) sum += a[o + x + r + 1];
                if (x - r >= 0) sum -= a[o + x - r];
            }
            Array.Copy(row, 0, a, o, w);
        }
    }

    static void BlurColumns(int[] a, int w, int h, int r)
    {
        var col = new int[h];
        int div = 2 * r + 1;
        for (int x = 0; x < w; x++)
        {
            int sum = 0;
            for (int i = 0; i <= r && i < h; i++) sum += a[i * w + x];
            for (int y = 0; y < h; y++)
            {
                col[y] = sum / div;
                if (y + r + 1 < h) sum += a[(y + r + 1) * w + x];
                if (y - r >= 0) sum -= a[(y - r) * w + x];
            }
            for (int y = 0; y < h; y++) a[y * w + x] = col[y];
        }
    }
}

// A topmost, click-through, never-activated window drawn with per-pixel alpha.
// UpdateLayeredWindow would be simpler, but SetWindowDisplayAffinity refuses windows in that mode, so the
// window is layered only for click-through and whole-window fade (SetLayeredWindowAttributes), and the DWM
// frame covers the whole client area so the alpha channel of what BitBlt copies in is used as transparency.
sealed class Layer : NativeWindow
{
    public int Width, Height;
    public Graphics G;
    Bitmap canvas;
    IntPtr dib, memDC, oldBitmap, bits;
    bool shown, fullDirty;
    int x = int.MinValue, y = int.MinValue;
    byte alpha;
    Rectangle drawn;                        // canvas area the last DrawSprite wrote

    public Layer()
    {
        var cp = new CreateParams
        {
            Caption = "Mom Pet",
            Style = unchecked((int)0x80000000),        // WS_POPUP
            ExStyle = Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW
                    | Native.WS_EX_TOPMOST | Native.WS_EX_NOACTIVATE,
        };
        CreateHandle(cp);
        Native.SetLayeredWindowAttributes(Handle, 0, 0, 2 /* LWA_ALPHA */);
        var glass = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        Native.DwmExtendFrameIntoClientArea(Handle, ref glass);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x14)                  // WM_ERASEBKGND: the canvas covers everything
        {
            m.Result = (IntPtr)1;
            return;
        }
        if (m.Msg == 0xF && G != null)      // WM_PAINT: copy the whole canvas again
        {
            Blit(new Rectangle(0, 0, Width, Height));
            Native.ValidateRect(Handle, IntPtr.Zero);
            return;
        }
        base.WndProc(ref m);
    }

    public void Resize(int w, int h)
    {
        if (w == Width && h == Height) return;
        Free();
        Width = w; Height = h;
        Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, w, h, 0x16 /* NOMOVE | NOZORDER | NOACTIVATE */);
        fullDirty = true;
        drawn = Rectangle.Empty;
        var bi = new Native.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER)),
            biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32,
        };
        dib = Native.CreateDIBSection(IntPtr.Zero, ref bi, 0, out bits, IntPtr.Zero, 0);
        memDC = Native.CreateCompatibleDC(IntPtr.Zero);
        oldBitmap = Native.SelectObject(memDC, dib);
        // GDI+ draws straight into the DIB memory in premultiplied ARGB, which is what DWM composites with
        canvas = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
        G = Graphics.FromImage(canvas);
        G.SmoothingMode = SmoothingMode.AntiAlias;
        G.InterpolationMode = InterpolationMode.Bilinear;
        G.PixelOffsetMode = PixelOffsetMode.HighSpeed;
        G.CompositingQuality = CompositingQuality.HighSpeed;
    }

    void Free()
    {
        if (G == null) return;
        G.Dispose();
        canvas.Dispose();
        Native.SelectObject(memDC, oldBitmap);
        Native.DeleteDC(memDC);
        Native.DeleteObject(dib);
        G = null;
    }

    // Erases what the previous call drew, then fills canvas area `box` with the sprite drawn through matrix m
    // (sprite px -> canvas px), sampling bilinearly. GDI+ needs about 1.3 ms a frame for the same rotated
    // draw; this needs far less. Returns the canvas area that changed.
    public unsafe Rectangle DrawSprite(Sprite sp, Matrix m, Rectangle box)
    {
        float[] e;
        using (Matrix inv = m.Clone())
        {
            inv.Invert();
            e = inv.Elements;
        }
        double a = e[0], b = e[1], c = e[2], d = e[3];
        int sw = sp.Image.Width, sh = sp.Image.Height;
        uint* dst = (uint*)bits;
        for (int y = drawn.Top; y < drawn.Bottom; y++)
            for (int x = drawn.Left; x < drawn.Right; x++) dst[y * Width + x] = 0;
        fixed (uint* src = sp.Pixels)
        {
            for (int y = box.Top; y < box.Bottom; y++)
            {
                uint* row = dst + y * Width;
                // source position of this row's first pixel centre, in pixel-corner coordinates
                double u = a * (box.Left + 0.5) + c * (y + 0.5) + e[4] - 0.5;
                double v = b * (box.Left + 0.5) + d * (y + 0.5) + e[5] - 0.5;
                for (int x = box.Left; x < box.Right; x++, u += a, v += b)
                {
                    if (u <= -1 || v <= -1 || u >= sw || v >= sh)
                    {
                        row[x] = 0;
                        continue;
                    }
                    int x0 = u < 0 ? -1 : (int)u, y0 = v < 0 ? -1 : (int)v;
                    uint fx = (uint)((u - x0) * 256), fy = (uint)((v - y0) * 256);
                    uint p00 = 0, p10 = 0, p01 = 0, p11 = 0;
                    bool left = x0 >= 0, right = x0 + 1 < sw;
                    if (y0 >= 0)
                    {
                        uint* r0 = src + y0 * sw;
                        if (left) p00 = r0[x0];
                        if (right) p10 = r0[x0 + 1];
                    }
                    if (y0 + 1 < sh)
                    {
                        uint* r1 = src + (y0 + 1) * sw;
                        if (left) p01 = r1[x0];
                        if (right) p11 = r1[x0 + 1];
                    }
                    row[x] = Lerp(Lerp(p00, p10, fx), Lerp(p01, p11, fx), fy);
                }
            }
        }
        Rectangle changed = drawn.IsEmpty ? box : Rectangle.Union(drawn, box);
        drawn = box;
        return changed;
    }

    // mixes two premultiplied ARGB pixels, f in 0..256, two channels per multiply
    static uint Lerp(uint p, uint q, uint f)
    {
        uint g = 256 - f;
        uint rb = (((p & 0xFF00FF) * g + (q & 0xFF00FF) * f) >> 8) & 0xFF00FF;
        uint ag = (((p >> 8) & 0xFF00FF) * g + ((q >> 8) & 0xFF00FF) * f) & 0xFF00FF00;
        return rb | ag;
    }

    public void Present(int left, int top, byte opacity)
    {
        Present(left, top, opacity, new Rectangle(0, 0, Width, Height));
    }

    // moves the window to left, top, copies the changed canvas area in and sets the whole-window opacity
    public void Present(int left, int top, byte opacity, Rectangle dirty)
    {
        G.Flush(FlushIntention.Sync);
        if (!shown)
        {
            // show it fully transparent first: GDI output to a hidden window is thrown away
            Native.SetLayeredWindowAttributes(Handle, 0, 0, 2);
            alpha = 0;
            Native.ShowWindow(Handle, 4 /* SW_SHOWNOACTIVATE */);
            shown = true;
            fullDirty = true;
        }
        if (left != x || top != y)
        {
            Native.SetWindowPos(Handle, IntPtr.Zero, left, top, 0, 0, 0x15 /* NOSIZE | NOZORDER | NOACTIVATE */);
            x = left; y = top;
        }
        Blit(fullDirty ? new Rectangle(0, 0, Width, Height) : dirty);
        fullDirty = false;
        if (opacity != alpha)
        {
            Native.SetLayeredWindowAttributes(Handle, 0, opacity, 2);
            alpha = opacity;
        }
    }

    void Blit(Rectangle r)
    {
        IntPtr dc = Native.GetDC(Handle);
        Native.BitBlt(dc, r.X, r.Y, r.Width, r.Height, memDC, r.X, r.Y, 0x00CC0020 /* SRCCOPY */);
        Native.ReleaseDC(Handle, dc);
    }

    public void Hide()
    {
        if (!shown) return;
        Native.ShowWindow(Handle, 0 /* SW_HIDE */);
        shown = false;
    }

    // WDA_EXCLUDEFROMCAPTURE (Windows 10 2004+): still shown on screen, left out of screenshots and recordings
    public void HideFromCapture(bool hide)
    {
        Native.SetWindowDisplayAffinity(Handle, hide ? 0x11u : 0u);
    }

    public void KeepOnTop()
    {
        if (shown) Native.SetWindowPos(Handle, (IntPtr)(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, 0x0213);
    }
}

// Start with Windows through the current user's Run key.
static class Startup
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = Settings.AppName;

    public static bool Enabled
    {
        get
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
                return k != null && k.GetValue(Name) != null;
        }
        set
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key))
            {
                if (value) k.SetValue(Name, "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue(Name, false);
            }
        }
    }
}

// Options from the settings page, kept in HKCU\Software\MomPet.
static class Settings
{
    public const string AppName = "MomPet";
    const string Key = @"Software\" + AppName;

    public static bool HideFromCapture
    {
        get { return Read("HideFromCapture", 0) != 0; }
        set { Write("HideFromCapture", value ? 1 : 0); }
    }

    // hide her after the cursor has not moved for IdleSeconds
    public static bool IdleHide
    {
        get { return Read("IdleHide", 0) != 0; }
        set { Write("IdleHide", value ? 1 : 0); }
    }

    public static int IdleSeconds
    {
        get { return Math.Max(1, Read("IdleSeconds", 10)); }
        set { Write("IdleSeconds", value); }
    }

    // the folder name of the chosen click effect, or EffectDef.Off
    public static string Effect
    {
        get
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
                return (k == null ? null : k.GetValue("Effect") as string) ?? EffectDef.DefaultId;
        }
        set
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key))
                k.SetValue("Effect", value, RegistryValueKind.String);
        }
    }

    public static int SizePercent
    {
        get { return Math.Max(50, Math.Min(200, Read("SizePercent", 100))); }
        set { Write("SizePercent", value); }
    }

    // the folder name of the chosen built-in character in characters\
    public static string Character
    {
        get
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
                return (k == null ? null : k.GetValue("Character") as string) ?? CharacterInfo.DefaultId;
        }
        set
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key))
                k.SetValue("Character", value, RegistryValueKind.String);
        }
    }

    // a lang\<code>.txt file name, or null to follow the Windows display language
    public static string Language
    {
        get
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
                return k == null ? null : k.GetValue("Language") as string;
        }
        set
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key))
                k.SetValue("Language", value, RegistryValueKind.String);
        }
    }

    static int Read(string name, int fallback)
    {
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
            return k == null ? fallback : (k.GetValue(name) as int? ?? fallback);
    }

    static void Write(string name, int value)
    {
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key))
            k.SetValue(name, value, RegistryValueKind.DWord);
    }
}

static class Ease
{
    // CSS cubic-bezier(x1, y1, x2, y2) evaluated at progress t
    public static double Bezier(double x1, double y1, double x2, double y2, double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        double lo = 0, hi = 1, s = t;
        for (int i = 0; i < 24; i++)
        {
            s = (lo + hi) / 2;
            if (Curve(x1, x2, s) < t) lo = s; else hi = s;
        }
        return Curve(y1, y2, s);
    }

    static double Curve(double a, double b, double s)
    {
        double u = 1 - s;
        return 3 * u * u * s * a + 3 * u * s * s * b + s * s * s;
    }
}

static class Native
{
    public const int WH_MOUSE_LL = 14;
    public const int WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204, WM_MBUTTONDOWN = 0x207;
    public const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
        WS_EX_TOPMOST = 0x8, WS_EX_NOACTIVATE = 0x8000000;

    public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] public struct MARGINS { public int Left, Right, Top, Bottom; }
    // DWM_TIMING_INFO is packed and 292 bytes; only the refresh period and last vblank time are read
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DWM_TIMING_INFO
    {
        public uint cbSize, refreshNum, refreshDen;
        public ulong qpcRefreshPeriod;
        public uint composeNum, composeDen;
        public ulong qpcVBlank;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public byte[] rest;
    }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct CURSORINFO { public int cbSize, flags; public IntPtr hCursor; public int x, y; }
    [StructLayout(LayoutKind.Sequential)] public struct MSLLHOOKSTRUCT { public int x, y; public uint mouseData, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
    }

    // the foreground window covers the whole monitor without a title bar, or Windows reports a
    // full-screen Direct3D game or presentation mode
    public static bool FullscreenOn(IntPtr monitor)
    {
        int state;
        if (SHQueryUserNotificationState(out state) == 0 && (state == 3 || state == 4)) return true;
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == GetShellWindow() || fg == GetDesktopWindow()) return false;
        var cls = new System.Text.StringBuilder(64);
        GetClassName(fg, cls, 64);
        if (cls.ToString() == "WorkerW" || cls.ToString() == "Progman") return false;
        if (MonitorFromWindow(fg, 2) != monitor) return false;
        RECT r;
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
        if (!GetWindowRect(fg, out r) || !GetMonitorInfo(monitor, ref mi)) return false;
        if (r.Left > mi.rcMonitor.Left || r.Top > mi.rcMonitor.Top || r.Right < mi.rcMonitor.Right || r.Bottom < mi.rcMonitor.Bottom)
            return false;
        const int WS_CAPTION = 0x00C00000;
        return (GetWindowLong(fg, -16 /* GWL_STYLE */) & WS_CAPTION) != WS_CAPTION;
    }

    // "Show animations in Windows"; browsers map prefers-reduced-motion to this same setting
    public static bool ClientAreaAnimation()
    {
        bool on;
        return !SystemParametersInfo(0x1042 /* SPI_GETCLIENTAREAANIMATION */, 0, out on, 0) || on;
    }

    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] public static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr inst, IntPtr id);
    [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CURSORINFO ci);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] public static extern uint MsgWaitForMultipleObjectsEx(uint count, IntPtr[] handles, uint ms, uint wake, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateWaitableTimerEx(IntPtr attrs, string name, uint flags, uint access);
    [DllImport("kernel32.dll")] public static extern bool SetWaitableTimer(IntPtr timer, ref long due, int period, IntPtr fn, IntPtr arg, bool resume);
    [DllImport("dwmapi.dll")] public static extern int DwmGetCompositionTimingInfo(IntPtr hwnd, ref DWM_TIMING_INFO info);
    [DllImport("user32.dll")] public static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] public static extern bool PostThreadMessage(uint thread, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, int key, byte alpha, int flags);
    [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS m);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] public static extern bool ValidateRect(IntPtr hwnd, IntPtr rect);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action, uint param, out bool value, uint winIni);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr mon, int type, out uint dpiX, out uint dpiY);
    [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
    [DllImport("dwmapi.dll")] public static extern int DwmFlush();
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage,
        out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
}
