// The settings page: collapsible sections with switches that apply at once, and custom images for each pose.
// Colours follow the portfolio web page the cursor came from; all text comes from Lang.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

sealed class SettingsForm : Form
{
    static readonly Color Cream = Color.FromArgb(0xFB, 0xF3, 0xE4);
    static readonly Color Wine = Color.FromArgb(0x8B, 0x1E, 0x3F);
    static readonly Color Ink = Color.FromArgb(0x3B, 0x1A, 0x22);
    static readonly Color Gold = Color.FromArgb(0xD8, 0xA5, 0x3A);
    static readonly Color NoteBack = Color.FromArgb(0xF6, 0xE7, 0xC8);
    static readonly Color ThumbBack = Color.FromArgb(0xF3, 0xE9, 0xEC);
    static readonly Color Muted = Color.FromArgb(0x9A, 0x86, 0x8C);
    const int Columns = 6;

    readonly Pet pet;
    readonly float k;                       // DPI scale for sizes given in pixels
    readonly int contentWidth;
    readonly FlowLayoutPanel content;
    readonly PictureBox[] thumbs = new PictureBox[Pet.Poses.Length];
    readonly Button[] resets = new Button[Pet.Poses.Length];
    readonly Label[] ids = new Label[Pet.Poses.Length];
    readonly List<Label> headers = new List<Label>();
    readonly List<Control> bodies = new List<Control>();
    Label note;
    ComboBox fxPicker;
    readonly List<string> fxIds = new List<string>();

    public SettingsForm(Pet pet)
    {
        this.pet = pet;
        Text = Lang.T("settings.title");
        Font = new Font(Lang.T("font"), 10f);  // each language file names a UI font for its script
        BackColor = Cream;
        ForeColor = Ink;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.Manual;
        using (Graphics g = CreateGraphics()) k = g.DpiX / 96f;
        contentWidth = Columns * (Px(112) + Px(8));

        // the content sizes itself; the scroller around it only scrolls when it is taller than the screen
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        content = Stack(FlowDirection.TopDown);
        content.Padding = new Padding(Px(18), Px(12), Px(18), Px(18));
        content.Controls.Add(Section(Lang.T("section.general"), GeneralBody(), true));
        content.Controls.Add(Section(Lang.T("section.images"), ImageBody(), false));
        content.Controls.Add(Section(Lang.T("section.effects"), EffectBody(), false));
        scroller.Controls.Add(content);
        Controls.Add(scroller);
        FormClosed += delegate { foreach (PictureBox p in thumbs) if (p.Image != null) p.Image.Dispose(); };
    }

    // centre on the screen under the cursor once the layout knows its real size
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        AlignWidths();
        FitToContent();
        Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2));
    }

    // header lines and the note box take the width of the widest section, so their right edges line up
    void AlignWidths()
    {
        int width = 0;
        foreach (Control body in bodies) width = Math.Max(width, body.GetPreferredSize(Size.Empty).Width);
        foreach (Label header in headers) header.Width = width;
        note.MaximumSize = new Size(width - note.Parent.Padding.Horizontal, 0);
        note.MinimumSize = new Size(width - note.Parent.Padding.Horizontal, 0);
    }

    // size the window to the content, but never taller than the screen; keep it on screen
    void FitToContent()
    {
        Size pref = content.GetPreferredSize(Size.Empty);
        Rectangle area = Screen.FromControl(this).WorkingArea;
        int frame = Height - ClientSize.Height;
        int h = Math.Min(pref.Height, area.Height - frame);
        ClientSize = new Size(pref.Width + (h < pref.Height ? SystemInformation.VerticalScrollBarWidth : 0), h);
        if (Bottom > area.Bottom) Top = Math.Max(area.Top, area.Bottom - Height);
    }

    int Px(float v) { return (int)Math.Round(v * k); }

    static FlowLayoutPanel Stack(FlowDirection dir)
    {
        return new FlowLayoutPanel { FlowDirection = dir, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false, Margin = new Padding(0) };
    }

    // a title bar with a gold underline; clicking it shows or hides the body
    Control Section(string title, Control body, bool open)
    {
        var section = Stack(FlowDirection.TopDown);
        var header = new Label { AutoSize = false, Size = new Size(contentWidth, Px(40)), ForeColor = Wine,
            Font = new Font(Font.FontFamily, 12f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.Hand, Margin = new Padding(0, Px(6), 0, Px(10)) };
        header.Paint += (s, e) =>
        {
            using (var pen = new Pen(Gold, Px(2)))
                e.Graphics.DrawLine(pen, 0, header.Height - Px(1), header.Width, header.Height - Px(1));
        };
        body.Visible = open;
        body.Margin = new Padding(Px(4), 0, 0, Px(6));
        header.Text = (open ? "▾  " : "▸  ") + title;
        header.Click += delegate
        {
            body.Visible = !body.Visible;
            header.Text = (body.Visible ? "▾  " : "▸  ") + title;
            FitToContent();
        };
        section.Controls.Add(header);
        section.Controls.Add(body);
        headers.Add(header);
        bodies.Add(body);
        return section;
    }

    Button Flat(string text)
    {
        var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.White,
            ForeColor = Wine, Cursor = Cursors.Hand, Padding = new Padding(Px(4), 0, Px(4), 0) };
        b.FlatAppearance.BorderColor = Wine;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(0xF7, 0xE6, 0xEB);
        return b;
    }

    static CheckBox Check(string text, bool on)
    {
        return new CheckBox { Text = text, Checked = on, AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
    }

    // ---- switches ----

    Control GeneralBody()
    {
        var list = Stack(FlowDirection.TopDown);

        var pause = Check(Lang.T("general.pause"), pet.Paused);
        pause.CheckedChanged += delegate { pet.Paused = pause.Checked; };
        list.Controls.Add(pause);

        var startup = Check(Lang.T("general.startup"), Startup.Enabled);
        startup.CheckedChanged += delegate { Startup.Enabled = startup.Checked; };
        list.Controls.Add(startup);

        var capture = Check(Lang.T("general.capture"), pet.HideFromCapture);
        capture.CheckedChanged += delegate { pet.HideFromCapture = capture.Checked; };
        list.Controls.Add(capture);

        var idleRow = Stack(FlowDirection.LeftToRight);
        var idle = Check(Lang.T("general.idle.before"), pet.IdleHide);
        var seconds = new NumericUpDown { Minimum = 1, Maximum = 3600, Value = pet.IdleSeconds, Width = Px(64),
            Margin = new Padding(Px(4), Px(2), Px(4), 0), Enabled = idle.Checked };
        var after = new Label { Text = Lang.T("general.idle.after"), AutoSize = true, Margin = new Padding(0, Px(6), 0, 0) };
        idle.CheckedChanged += delegate
        {
            pet.IdleHide = idle.Checked;
            Settings.IdleHide = idle.Checked;
            seconds.Enabled = idle.Checked;
        };
        seconds.ValueChanged += delegate
        {
            pet.IdleSeconds = (int)seconds.Value;
            Settings.IdleSeconds = (int)seconds.Value;
        };
        idleRow.Controls.Add(idle);
        idleRow.Controls.Add(seconds);
        idleRow.Controls.Add(after);
        list.Controls.Add(idleRow);

        // size: the pet follows the slider at once, the shadows are re-rendered a moment after it stops
        var sizeRow = Stack(FlowDirection.LeftToRight);
        sizeRow.Margin = new Padding(0, Px(6), 0, 0);
        sizeRow.Controls.Add(new Label { Text = Lang.T("general.size"), AutoSize = true, Margin = new Padding(0, Px(8), Px(8), 0) });
        var slider = new TrackBar { Minimum = 5, Maximum = 20, SmallChange = 1, LargeChange = 1, TickFrequency = 1,
            Value = pet.SizePercent / 10, Width = Px(220), BackColor = Cream, AutoSize = false, Height = Px(32) };
        var sizeText = new Label { Text = pet.SizePercent + "%", AutoSize = true, Margin = new Padding(Px(4), Px(8), 0, 0) };
        var applySize = new Timer { Interval = 250 };
        applySize.Tick += delegate
        {
            applySize.Stop();
            pet.SizePercent = slider.Value * 10;
        };
        slider.ValueChanged += delegate
        {
            sizeText.Text = slider.Value * 10 + "%";
            applySize.Stop();
            applySize.Start();
        };
        FormClosed += delegate { applySize.Dispose(); };
        sizeRow.Controls.Add(slider);
        sizeRow.Controls.Add(sizeText);
        list.Controls.Add(sizeRow);

        var langRow = Stack(FlowDirection.LeftToRight);
        langRow.Margin = new Padding(0, Px(6), 0, 0);
        langRow.Controls.Add(new Label { Text = Lang.T("general.language"), AutoSize = true, Margin = new Padding(0, Px(5), Px(8), 0) });
        var languages = Lang.Available();
        var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Px(160) };
        foreach (var l in languages)
        {
            picker.Items.Add(l.Value);
            if (l.Key == Lang.Code) picker.SelectedIndex = picker.Items.Count - 1;
        }
        picker.SelectedIndexChanged += delegate
        {
            string code = languages[picker.SelectedIndex].Key;
            if (code == Lang.Code) return;
            Settings.Language = code;
            Lang.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lang"), code);
            BeginInvoke((Action)pet.ApplyLanguage);     // rebuilds this page in the new language
        };
        langRow.Controls.Add(picker);
        list.Controls.Add(langRow);

        // the built-in character, with who made her in small print below
        var charRow = Stack(FlowDirection.LeftToRight);
        charRow.Margin = new Padding(0, Px(8), 0, 0);
        charRow.Controls.Add(new Label { Text = Lang.T("general.character"), AutoSize = true, Margin = new Padding(0, Px(5), Px(8), 0) });
        var characters = CharacterInfo.All();
        var charPicker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Px(160) };
        foreach (CharacterInfo c in characters)
        {
            charPicker.Items.Add(c.Name);
            if (c.Id == pet.Character.Id) charPicker.SelectedIndex = charPicker.Items.Count - 1;
        }
        var credit = new Label { AutoSize = true, ForeColor = Muted, Font = new Font(Font.FontFamily, 8.5f),
            MaximumSize = new Size(contentWidth - Px(28), 0), Margin = new Padding(0, Px(4), 0, 0) };
        credit.Text = pet.Character.Credit ?? "";
        charPicker.SelectionChangeCommitted += delegate
        {
            CharacterInfo c = characters[charPicker.SelectedIndex];
            if (c.Id == pet.Character.Id) return;
            pet.SetCharacter(c.Id);
            credit.Text = c.Credit ?? "";
            RefreshAll();
        };
        charRow.Controls.Add(charPicker);
        list.Controls.Add(charRow);
        list.Controls.Add(credit);
        return list;
    }

    // ---- custom images ----

    Control ImageBody()
    {
        var list = Stack(FlowDirection.TopDown);

        note = new Label { Text = Lang.T("images.notes"), AutoSize = true, MaximumSize = new Size(contentWidth - Px(28), 0),
            Font = new Font(Font.FontFamily, 9.5f) };
        var noteBox = new Panel { BackColor = NoteBack, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(Px(12), Px(10), Px(12), Px(10)), Margin = new Padding(0, 0, 0, Px(12)) };
        noteBox.Controls.Add(note);
        list.Controls.Add(noteBox);

        var buttons = Stack(FlowDirection.LeftToRight);
        buttons.Margin = new Padding(0, 0, 0, Px(10));
        Button all = Flat(Lang.T("images.all")), resetAll = Flat(Lang.T("images.resetall")),
            import = Flat(Lang.T("images.import")), export = Flat(Lang.T("images.export"));
        foreach (Button b in new[] { all, resetAll, import, export }) b.Margin = new Padding(0, 0, Px(8), 0);
        all.Click += delegate
        {
            string file = PickImage();
            if (file != null && Import(file, Pet.Poses)) RefreshAll();
        };
        resetAll.Click += delegate
        {
            if (MessageBox.Show(this, Lang.T("msg.resetall"), Lang.T("app.name"), MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                    != DialogResult.Yes) return;
            foreach (string pose in Pet.Poses) PoseFiles.Delete(Pet.CustomDir, pose);
            File.Delete(Path.Combine(Pet.CustomDir, CharacterInfo.FileName));
            pet.ReloadPoses();
            RefreshAll();
        };
        import.Click += delegate { ImportPack(); };
        export.Click += delegate { ExportPack(); };
        buttons.Controls.Add(all);
        buttons.Controls.Add(resetAll);
        buttons.Controls.Add(import);
        buttons.Controls.Add(export);
        list.Controls.Add(buttons);

        var grid = new TableLayoutPanel { ColumnCount = Columns, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0) };
        for (int i = 0; i < Pet.Poses.Length; i++) grid.Controls.Add(Card(i));
        list.Controls.Add(grid);
        RefreshAll();
        return list;
    }

    // one pose: thumbnail, when it shows, and its buttons, on a white card
    Control Card(int i)
    {
        int inner = Px(112) - Px(16);
        var card = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.White,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(Px(8)),
            Margin = new Padding(0, 0, Px(8), Px(8)) };
        thumbs[i] = new PictureBox { Size = new Size(inner, Px(104)), SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = ThumbBack, Margin = new Padding(0, 0, 0, Px(6)) };
        card.Controls.Add(thumbs[i]);
        // every caption gets two lines of room, so the buttons line up across a row
        var small = new Font(Font.FontFamily, 9f);
        int twoLines = TextRenderer.MeasureText("測\n測", small).Height;
        card.Controls.Add(new Label { Text = Lang.T("pose." + Pet.Poses[i]), AutoSize = false, Size = new Size(inner, twoLines),
            Font = small, Margin = new Padding(0) });
        ids[i] = new Label { AutoSize = false, Size = new Size(inner, twoLines), ForeColor = Muted,
            Font = new Font(Font.FontFamily, 8.5f), Margin = new Padding(0, 0, 0, Px(4)) };
        card.Controls.Add(ids[i]);

        var row = Stack(FlowDirection.LeftToRight);
        var change = Flat(Lang.T("card.change"));
        change.Font = small;
        change.Margin = new Padding(0, 0, Px(4), 0);
        resets[i] = Flat(Lang.T("card.reset"));
        resets[i].Font = small;
        resets[i].Margin = new Padding(0);
        int index = i;
        change.Click += delegate
        {
            string file = PickImage();
            if (file != null && Import(file, new[] { Pet.Poses[index] })) RefreshAll();
        };
        resets[i].Click += delegate
        {
            PoseFiles.Delete(Pet.CustomDir, Pet.Poses[index]);
            pet.ReloadPoses();
            RefreshAll();
        };
        row.Controls.Add(change);
        row.Controls.Add(resets[i]);
        card.Controls.Add(row);
        return card;
    }

    // a change to "default" can change what every other pose shows, so every card is refreshed
    void RefreshAll()
    {
        for (int i = 0; i < Pet.Poses.Length; i++)
        {
            string pose = Pet.Poses[i];
            Image old = thumbs[i].Image;
            int frames;
            using (PoseImage img = pet.LoadPose(pose))
            {
                thumbs[i].Image = new Bitmap(img.Frames[0]);    // an animation shows its first frame
                frames = img.Frames.Length;
            }
            if (old != null) old.Dispose();
            ids[i].Text = !pet.HasOwnImage(pose) ? pose + "\n" + Lang.T("card.fallback")
                        : frames > 1 ? pose + "\n" + Lang.T("card.frames", frames) : pose;
            bool custom = Pet.HasCustom(pose);
            resets[i].Enabled = custom;
            resets[i].FlatAppearance.BorderColor = custom ? Wine : Color.FromArgb(0xD8, 0xCF, 0xD2);
        }
    }

    string PickImage()
    {
        using (var dialog = new OpenFileDialog { Title = Lang.T("dialog.pick"),
            Filter = Lang.T("dialog.filter.png") + " (*.png;*.gif)|*.png;*.gif|" + Lang.T("dialog.filter.all") + "|*.png;*.gif;*.bmp;*.jpg;*.jpeg" })
            return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    // copies one image into the custom folder for every pose listed
    bool Import(string file, string[] poses)
    {
        PoseImage img;
        try { img = PoseFiles.ReadFile(file); }    // an animated GIF keeps all its frames
        catch (Exception)
        {
            MessageBox.Show(this, Lang.T("msg.cantopen"), Lang.T("app.name"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        using (img)
        {
            if (!Packs.HasTransparency(img.Frames[0]) && MessageBox.Show(this, Lang.T("msg.opaque"), Lang.T("app.name"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return false;
            foreach (string pose in poses) Packs.Save(img, pose);
        }
        pet.ReloadPoses();
        return true;
    }

    string PackFilter()
    {
        return Lang.T("dialog.pack") + " (*.zip)|*.zip";
    }

    void ImportPack()
    {
        string file;
        using (var dialog = new OpenFileDialog { Title = Lang.T("images.import"), Filter = PackFilter() })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            file = dialog.FileName;
        }
        string info;
        Dictionary<string, PoseImage> images = Packs.Read(file, out info);
        if (images == null)
        {
            MessageBox.Show(this, Lang.T("msg.import.bad"), Lang.T("app.name"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            if (images.Count == 0)
            {
                // a pack may carry only a click effect
                if (!InstallEffectFrom(file))
                    MessageBox.Show(this, Lang.T("msg.import.none"), Lang.T("app.name"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show(this, Lang.T("msg.import.confirm", images.Count), Lang.T("app.name"), MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes) return;
            Packs.Install(images, info);
        }
        finally
        {
            foreach (PoseImage p in images.Values) p.Dispose();
        }
        pet.ReloadPoses();
        RefreshAll();
        InstallEffectFrom(file);
    }

    void ExportPack()
    {
        bool any = false;
        foreach (string pose in Pet.Poses) any |= Pet.HasCustom(pose);
        string fxFolder = CurrentEffectFolder();
        if (!any && fxFolder == null)
        {
            MessageBox.Show(this, Lang.T("msg.export.none"), Lang.T("app.name"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using (var dialog = new SaveFileDialog { Title = Lang.T("images.export"), Filter = PackFilter(), FileName = "mompet-pack.zip" })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            int count = Packs.Export(dialog.FileName, fxFolder);
            MessageBox.Show(this, Lang.T("msg.export.done", count), Lang.T("app.name"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // ---- click effects ----

    Control EffectBody()
    {
        var list = Stack(FlowDirection.TopDown);

        var pickRow = Stack(FlowDirection.LeftToRight);
        pickRow.Controls.Add(new Label { Text = Lang.T("effects.choose"), AutoSize = true, Margin = new Padding(0, Px(5), Px(8), 0) });
        fxPicker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Px(220) };
        fxPicker.SelectionChangeCommitted += delegate { ChooseEffect(fxIds[fxPicker.SelectedIndex]); };
        pickRow.Controls.Add(fxPicker);
        list.Controls.Add(pickRow);
        FillEffects();

        var buttons = Stack(FlowDirection.LeftToRight);
        buttons.Margin = new Padding(0, Px(10), 0, Px(8));
        Button import = Flat(Lang.T("effects.import")), copy = Flat(Lang.T("effects.copy")),
            open = Flat(Lang.T("effects.open")), reload = Flat(Lang.T("effects.reload"));
        foreach (Button b in new[] { import, copy, open, reload }) b.Margin = new Padding(0, 0, Px(8), 0);
        import.Click += delegate
        {
            using (var dialog = new OpenFileDialog { Title = Lang.T("effects.import"), Filter = Lang.T("effects.filter") + " (*.zip)|*.zip" })
                if (dialog.ShowDialog(this) == DialogResult.OK && !InstallEffectFrom(dialog.FileName))
                    MessageBox.Show(this, Lang.T("effects.none"), Lang.T("app.name"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        copy.Click += delegate { CopyEffect(); };
        open.Click += delegate { OpenFolder(EffectDef.UserDir); };
        reload.Click += delegate
        {
            FillEffects();
            ChooseEffect(Settings.Effect);
        };
        buttons.Controls.Add(import);
        buttons.Controls.Add(copy);
        buttons.Controls.Add(open);
        buttons.Controls.Add(reload);
        list.Controls.Add(buttons);

        list.Controls.Add(new Label { Text = Lang.T("effects.note"), AutoSize = true, MaximumSize = new Size(contentWidth - Px(28), 0),
            Font = new Font(Font.FontFamily, 9.5f) });
        return list;
    }

    // fills the picker with "off" and every effect, selecting the saved one
    void FillEffects()
    {
        fxPicker.Items.Clear();
        fxIds.Clear();
        fxIds.Add(EffectDef.Off);
        fxPicker.Items.Add(Lang.T("effects.off"));
        foreach (var e in EffectDef.All())
        {
            fxIds.Add(e.Key);
            fxPicker.Items.Add(EffectDef.NameOf(e.Value));
        }
        int at = fxIds.FindIndex(id => string.Equals(id, Settings.Effect, StringComparison.OrdinalIgnoreCase));
        fxPicker.SelectedIndex = at < 0 ? 0 : at;
    }

    // switches to an effect; a broken effect file is reported with its line and the picker goes back
    void ChooseEffect(string id)
    {
        string error = pet.SetEffect(id);
        if (error != null)
            MessageBox.Show(this, Lang.T("effects.broken", error), Lang.T("app.name"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        FillEffects();
    }

    // installs and switches to the effect in a zip; false when the zip has none
    bool InstallEffectFrom(string zip)
    {
        string error;
        string id = Packs.InstallEffect(zip, out error);
        if (error != null)
        {
            MessageBox.Show(this, Lang.T("effects.broken", error), Lang.T("app.name"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return true;
        }
        if (id == null) return false;
        ChooseEffect(id);
        return true;
    }

    // the folder of the chosen effect, or null when effects are off
    static string CurrentEffectFolder()
    {
        string folder;
        return Settings.Effect != EffectDef.Off && EffectDef.All().TryGetValue(Settings.Effect, out folder) ? folder : null;
    }

    // copies the chosen effect into the user effect folder, switches to the copy and opens it for editing
    void CopyEffect()
    {
        string from = CurrentEffectFolder() ?? EffectDef.All()[EffectDef.DefaultId];
        string id = Path.GetFileName(from) + "-copy";
        string unique = id;
        for (int k = 2; Directory.Exists(Path.Combine(EffectDef.UserDir, unique)); k++) unique = id + "-" + k;
        string to = Path.Combine(EffectDef.UserDir, unique);
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        FillEffects();
        ChooseEffect(unique);
        OpenFolder(to);
    }

    static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\"");
    }
}
