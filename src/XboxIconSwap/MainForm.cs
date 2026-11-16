using System.Diagnostics;
using System.Text.Json;
using IconSwap.Core;

namespace XboxIconSwap;

internal sealed class MainForm : Form
{
    private readonly List<GameProfile> _profiles;
    private readonly ComboBox _game = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _folder = new() { Dock = DockStyle.Fill };
    private readonly Button _browse = new() { Text = "Обзор...", AutoSize = true };
    private readonly Button _detect = new() { Text = "Найти", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Padding = new Padding(0, 6, 0, 6) };
    private readonly FlowLayoutPanel _preview = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(45, 45, 48), Padding = new Padding(6) };
    private readonly Button _install = new() { Text = "Установить иконки Xbox", AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
    private readonly Button _restore = new() { Text = "Вернуть оригинальные", AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
    private readonly Button _openIcons = new() { Text = "Папка с иконками", AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Height = 18 };
    private readonly TextBox _log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f) };
    private bool _busy;

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XboxIconSwap", "settings.json");

    public MainForm()
    {
        Text = "Xbox Icon Swap — иконки Xbox вместо PlayStation";
        Font = new Font("Segoe UI", 9.5f);
        MinimumSize = new Size(760, 600);
        Size = new Size(860, 680);
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        Controls.Add(root);

        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(new Label { Text = "Игра:", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 4, 6, 0) }, 0, 0);
        top.Controls.Add(_game, 1, 0);
        top.SetColumnSpan(_game, 3);
        top.Controls.Add(new Label { Text = "Папка игры:", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 4, 6, 0) }, 0, 1);
        top.Controls.Add(_folder, 1, 1);
        top.Controls.Add(_browse, 2, 1);
        top.Controls.Add(_detect, 3, 1);
        root.Controls.Add(top);

        root.Controls.Add(_status);
        root.Controls.Add(new Label { Text = "Новые иконки (их можно отредактировать — это обычные PNG в папке Icons):", AutoSize = true });
        root.Controls.Add(_preview);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 4) };
        buttons.Controls.AddRange(new Control[] { _install, _restore, _openIcons });
        root.Controls.Add(buttons);
        root.Controls.Add(_progress);
        root.Controls.Add(_log);

        _profiles = LoadProfiles();
        foreach (var p in _profiles) _game.Items.Add(p.Name);

        _game.SelectedIndexChanged += (_, _) => OnProfileChanged();
        _folder.TextChanged += (_, _) => { RefreshState(); _ = GenerateIconsIfMissing(); };
        Shown += (_, _) => _ = GenerateIconsIfMissing();
        _browse.Click += (_, _) => Browse();
        _detect.Click += (_, _) => Detect(showMessage: true);
        _install.Click += async (_, _) => await Run(install: true);
        _restore.Click += async (_, _) => await Run(install: false);
        _openIcons.Click += (_, _) =>
        {
            var dir = Profile?.Folder ?? Installer.IconsRoot;
            if (Directory.Exists(dir)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        };

        if (_profiles.Count == 0)
        {
            _status.Text = "Не найдена папка Icons с профилями игр рядом с программой.";
            _status.ForeColor = Color.Firebrick;
            SetButtons();
            return;
        }
        _game.SelectedIndex = 0;
    }

    private GameProfile? Profile => _game.SelectedIndex >= 0 && _game.SelectedIndex < _profiles.Count ? _profiles[_game.SelectedIndex] : null;

    private List<GameProfile> LoadProfiles()
    {
        try { return GameProfile.LoadAll(Installer.IconsRoot); }
        catch (Exception ex) { Log("Ошибка чтения профилей: " + ex.Message); return new(); }
    }

    private void OnProfileChanged()
    {
        var saved = LoadSettings();
        var p = Profile;
        if (p == null) return;
        if (saved.TryGetValue(p.Id, out var folder) && p.ResolveGameFolder(folder) is { } ok) _folder.Text = ok;
        else Detect(showMessage: false);
        ShowPreview(p);
        RefreshState();
        _ = GenerateIconsIfMissing();
    }

    /// <summary>First run: draw the icons from the game's own textures, then show them.</summary>
    private async Task GenerateIconsIfMissing()
    {
        var p = Profile;
        var game = GameFolder;
        if (!IsHandleCreated || p == null || game == null || _busy || !p.Archives.Any(s => IconSwap.Core.Art.IconGenerator.NeedsIcons(p, s))) return;
        _busy = true;
        SetButtons();
        try
        {
            await Task.Run(() => IconSwap.Core.Art.IconGenerator.EnsureIcons(p, game, s => BeginInvoke(() => Log(s))));
            ShowPreview(p);
        }
        catch (Exception ex) { Log("Не удалось создать иконки: " + Installer.Explain(ex)); }
        finally { _busy = false; RefreshState(); }
    }

    private void Detect(bool showMessage)
    {
        var p = Profile;
        if (p == null) return;
        var found = p.AutoDetect();
        if (found != null) { _folder.Text = found; Log($"Игра найдена: {found}"); }
        else if (showMessage)
            MessageBox.Show(this, "Не удалось найти игру автоматически. Укажите папку игры кнопкой «Обзор...».",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void Browse()
    {
        using var dlg = new FolderBrowserDialog { Description = "Папка игры (где лежит eboot.bin и папка databigs)", UseDescriptionForTitle = true };
        if (Directory.Exists(_folder.Text)) dlg.InitialDirectory = _folder.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK) _folder.Text = dlg.SelectedPath;
    }

    private string? GameFolder => Profile?.ResolveGameFolder(_folder.Text.Trim());

    private void RefreshState()
    {
        var p = Profile;
        var game = GameFolder;
        if (p == null) return;
        if (game == null)
        {
            _status.Text = string.IsNullOrWhiteSpace(_folder.Text) ? "Укажите папку игры." : "В этой папке не найдена " + p.Name + ".";
            _status.ForeColor = Color.Firebrick;
        }
        else
        {
            var state = Installer.GetState(p, game);
            (_status.Text, _status.ForeColor) = state switch
            {
                Installer.State.Patched => ("Установлены иконки Xbox ✔", Color.ForestGreen),
                Installer.State.Partial => ("Иконки Xbox установлены частично — нажмите «Установить» ещё раз", Color.DarkOrange),
                _ => ("Сейчас в игре оригинальные иконки PlayStation", Color.FromArgb(40, 90, 160)),
            };
            if (!string.Equals(game, _folder.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                _status.Text += $"   ({game})";
        }
        SetButtons();
    }

    private void SetButtons()
    {
        var p = Profile;
        var game = GameFolder;
        bool ready = !_busy && p != null && game != null;
        _install.Enabled = ready;
        _restore.Enabled = ready && Installer.GetState(p!, game!) != Installer.State.NotPatched;
        _browse.Enabled = _detect.Enabled = _game.Enabled = _folder.Enabled = !_busy;
    }

    private void ShowPreview(GameProfile p)
    {
        _preview.SuspendLayout();
        foreach (Control c in _preview.Controls) { (c as PictureBox)?.Image?.Dispose(); c.Dispose(); }
        _preview.Controls.Clear();
        var tips = new ToolTip();
        int count = 0;
        foreach (var spec in p.Archives)
        foreach (var (path, png) in p.Replacements(spec))
        {
            try
            {
                var img = RgbaImage.Load(png);
                const int Thumb = 64;
                int big = Math.Max(img.Width, img.Height);
                if (big > Thumb) img = img.Resize(Math.Max(1, img.Width * Thumb / big), Math.Max(1, img.Height * Thumb / big));
                var flat = img.Flatten(45, 45, 48).ToBitmap();
                var box = new PictureBox { Image = flat, SizeMode = PictureBoxSizeMode.CenterImage, Size = new Size(Thumb + 8, Thumb + 8), Margin = new Padding(2) };
                tips.SetToolTip(box, path);
                _preview.Controls.Add(box);
                count++;
            }
            catch { }
        }
        _preview.ResumeLayout();
        Log($"{p.Name}: иконок для замены — {count}");
    }

    private async Task Run(bool install)
    {
        var p = Profile;
        var game = GameFolder;
        if (p == null || game == null) return;
        _busy = true;
        SetButtons();
        _progress.Value = 0;
        Log(install ? "Установка иконок Xbox..." : "Возврат оригинальных иконок...");
        try
        {
            List<string> warnings = new();
            await Task.Run(() =>
            {
                if (install)
                    warnings = Installer.Install(p, game, s => BeginInvoke(() => Log(s)),
                        (done, total) => BeginInvoke(() => { _progress.Maximum = Math.Max(1, total); _progress.Value = Math.Min(done, total); }));
                else
                    Installer.Restore(p, game, s => BeginInvoke(() => Log(s)));
            });
            foreach (var w in warnings) Log("Предупреждение: " + w);
            SaveSetting(p.Id, game);
            _progress.Value = _progress.Maximum;
            Log("Готово.");
            MessageBox.Show(this,
                install
                    ? "Иконки Xbox установлены. Запустите игру как обычно." + (warnings.Count > 0 ? $"\n\nПредупреждений: {warnings.Count} (см. журнал)." : "")
                    : "Оригинальные иконки возвращены.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("Ошибка: " + ex);
            MessageBox.Show(this, Installer.Explain(ex), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            RefreshState();
        }
    }

    private void Log(string s) => _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}{Environment.NewLine}");

    private static Dictionary<string, string> LoadSettings()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath)) ?? new(); }
        catch { return new(); }
    }

    private static void SaveSetting(string key, string value)
    {
        try
        {
            var s = LoadSettings();
            s[key] = value;
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
