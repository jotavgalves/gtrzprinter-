using System.Diagnostics;
using System.Drawing.Printing;

namespace GTRZPrinter;

internal static class Ui
{
    public static readonly Color Bg = Color.FromArgb(8, 8, 10);
    public static readonly Color Side = Color.FromArgb(12, 12, 15);
    public static readonly Color Card = Color.FromArgb(18, 18, 22);
    public static readonly Color Card2 = Color.FromArgb(25, 25, 30);
    public static readonly Color Border = Color.FromArgb(42, 42, 49);
    public static readonly Color Text = Color.FromArgb(245, 245, 247);
    public static readonly Color Muted = Color.FromArgb(145, 145, 156);
    public static readonly Color Red = Color.FromArgb(195, 29, 35);
    public static readonly Color RedHot = Color.FromArgb(231, 36, 43);
    public static readonly Color Amber = Color.FromArgb(243, 178, 70);
    public static readonly Color Danger = Color.FromArgb(235, 78, 78);

    public static Font F(float s, FontStyle st = FontStyle.Regular) => new("Segoe UI", s, st);

    public static Label Label(string text, float size = 9.5f, Color? color = null, FontStyle style = FontStyle.Regular) =>
        new() { Text = text, AutoSize = true, ForeColor = color ?? Text, Font = F(size, style), BackColor = Color.Transparent };

    public static Button Button(string text, bool primary = false) => new()
    {
        Text = text,
        AutoSize = true,
        Height = 38,
        Padding = new Padding(14, 0, 14, 0),
        FlatStyle = FlatStyle.Flat,
        BackColor = primary ? Red : Card2,
        ForeColor = Text,
        Font = F(9.2f, FontStyle.Bold),
        Cursor = Cursors.Hand,
        FlatAppearance = { BorderSize = 0 }
    };

    public static TextBox TextBox(string value) => new()
    {
        Text = value,
        BackColor = Card2,
        ForeColor = Text,
        BorderStyle = BorderStyle.FixedSingle,
        Font = F(9.4f)
    };

    public static NumericUpDown Number(decimal value, decimal min, decimal max)
    {
        var n = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            BackColor = Card2,
            ForeColor = Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = F(9.4f),
            Width = 150
        };
        n.Value = Math.Max(min, Math.Min(max, value));
        return n;
    }
}

internal sealed class MainForm : Form
{
    readonly AppConfig C;
    readonly ClientRegistry Clients = new();
    readonly object PrintLock = new();
    readonly Dictionary<string, Panel> Pages = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Button> Nav = new(StringComparer.OrdinalIgnoreCase);

    IppServer Ipp;
    ApiServer Api;
    DiscoveryService Discovery;
    ClientAgent Client;
    CancellationTokenSource RuntimeCts;

    bool ServerMode;
    bool RuntimeReady;
    bool PrinterReady;
    string PrinterError = "";
    string LocalIp = "127.0.0.1";
    bool RealExit;

    Panel Content;
    Label HeaderTitle;
    Label ModeBadge;
    Label MainStatus;
    Label MainDetail;
    Label PrinterStat;
    Label NetworkStat;
    Label JobsStat;
    Label ClientsStat;
    DataGridView ClientsGrid;
    RichTextBox Logs;
    NotifyIcon Tray;
    System.Windows.Forms.Timer UiTimer;

    ComboBox ModeCombo;
    ComboBox QueueCombo;
    TextBox ServerText;
    TextBox NetworkNameText;
    NumericUpDown IppPort;
    NumericUpDown ApiPort;
    NumericUpDown DiscoveryPort;
    NumericUpDown PaperWidth;
    NumericUpDown PrintableWidth;
    NumericUpDown Dots;
    NumericUpDown Dpi;
    NumericUpDown Columns;
    NumericUpDown PageLength;
    NumericUpDown BottomMargin;
    NumericUpDown FeedLines;
    CheckBox CutCheck;
    CheckBox TrimCheck;
    CheckBox StartupCheck;
    CheckBox MinimizeCheck;
    CheckBox DiscoverCheck;

    public MainForm(AppConfig config)
    {
        C = config;
        Text = "GTRZ Printer";
        Size = new Size(1180, 760);
        MinimumSize = new Size(1040, 680);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Ui.Bg;
        ForeColor = Ui.Text;
        Font = Ui.F(9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        BuildShell();
        BuildOverview();
        BuildPrinter();
        BuildClients();
        BuildLogs();
        BuildSettings();
        BuildTray();
        ShowPage("overview", "Visão geral");

        Shown += async (_, _) =>
        {
            if (Environment.GetCommandLineArgs().Contains("--minimized", StringComparer.OrdinalIgnoreCase)) HideToTray();
            await RestartRuntimeAsync();
        };

        FormClosing += (_, e) =>
        {
            if (!RealExit && C.MinimizeOnClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            StopRuntime();
            UiTimer?.Stop();
            if (Tray != null) Tray.Visible = false;
        };

        UiTimer = new System.Windows.Forms.Timer { Interval = 800 };
        UiTimer.Tick += (_, _) => RefreshUi();
        UiTimer.Start();
    }

    void BuildShell()
    {
        var side = new Panel { Dock = DockStyle.Left, Width = 220, BackColor = Ui.Side, Padding = new Padding(14) };
        var brand = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Ui.Side };
        var logo = new PictureBox { Image = LogoData.CreateImage(), SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(0, 8), Size = new Size(60, 60) };
        brand.Controls.Add(logo);
        var gtrz = Ui.Label("GTRZ", 17, Ui.Text, FontStyle.Bold); gtrz.Location = new Point(68, 16); brand.Controls.Add(gtrz);
        var printer = Ui.Label("PRINTER", 8.5f, Ui.RedHot, FontStyle.Bold); printer.Location = new Point(70, 49); brand.Controls.Add(printer);
        side.Controls.Add(brand);

        var menu = new Panel { Dock = DockStyle.Top, Height = 290, BackColor = Ui.Side };
        AddNav(menu, "overview", "Visão geral", 0);
        AddNav(menu, "printer", "Impressora", 46);
        AddNav(menu, "clients", "PCs conectados", 92);
        AddNav(menu, "logs", "Logs", 138);
        AddNav(menu, "settings", "Configurações", 184);
        side.Controls.Add(menu);

        var quit = Ui.Button("Sair do GTRZ Printer");
        quit.Dock = DockStyle.Bottom; quit.ForeColor = Ui.Danger; quit.BackColor = Color.FromArgb(28, 15, 17);
        quit.Click += (_, _) => { RealExit = true; Close(); };
        side.Controls.Add(quit);

        var header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Ui.Bg };
        HeaderTitle = Ui.Label("Visão geral", 20, Ui.Text, FontStyle.Bold); HeaderTitle.Location = new Point(28, 21); header.Controls.Add(HeaderTitle);
        ModeBadge = new Label
        {
            Text = "INICIALIZANDO",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(160, 30),
            BackColor = Color.FromArgb(38, 31, 18),
            ForeColor = Ui.Amber,
            Font = Ui.F(8.5f, FontStyle.Bold),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        header.Controls.Add(ModeBadge);
        void PlaceBadge() => ModeBadge.Location = new Point(Math.Max(10, header.ClientSize.Width - 188), 21);
        header.Resize += (_, _) => PlaceBadge(); PlaceBadge();

        Content = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg, Padding = new Padding(28, 10, 28, 28) };
        Controls.Add(Content); Controls.Add(header); Controls.Add(side);
    }

    void AddNav(Panel parent, string key, string text, int y)
    {
        var b = new Button
        {
            Text = text,
            Location = new Point(0, y),
            Size = new Size(192, 40),
            FlatStyle = FlatStyle.Flat,
            BackColor = Ui.Side,
            ForeColor = Ui.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 0, 0),
            Font = Ui.F(9.7f),
            Cursor = Cursors.Hand,
            FlatAppearance = { BorderSize = 0 }
        };
        b.Click += (_, _) => ShowPage(key, text);
        Nav[key] = b; parent.Controls.Add(b);
    }

    Panel NewPage(string key)
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg, AutoScroll = true, Visible = false };
        Pages[key] = p; Content.Controls.Add(p); return p;
    }

    Panel Card(int x, int y, int w, int h)
    {
        var p = new Panel { Location = new Point(x, y), Size = new Size(w, h), BackColor = Ui.Card, BorderStyle = BorderStyle.FixedSingle };
        return p;
    }

    void ShowPage(string key, string title)
    {
        foreach (var p in Pages.Values) p.Visible = false;
        foreach (var b in Nav.Values) { b.BackColor = Ui.Side; b.ForeColor = Ui.Muted; }
        if (Pages.TryGetValue(key, out var page)) { page.Visible = true; page.BringToFront(); }
        if (Nav.TryGetValue(key, out var nav)) { nav.BackColor = Color.FromArgb(35, 17, 20); nav.ForeColor = Ui.Text; }
        HeaderTitle.Text = title;
    }

    void BuildOverview()
    {
        var p = NewPage("overview");
        var hero = Card(0, 0, 884, 150); hero.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        var cap = Ui.Label("ESTADO DO SISTEMA", 8, Ui.Muted, FontStyle.Bold); cap.Location = new Point(20, 18); hero.Controls.Add(cap);
        MainStatus = Ui.Label("Preparando o GTRZ Printer...", 20, Ui.Text, FontStyle.Bold); MainStatus.Location = new Point(20, 47); hero.Controls.Add(MainStatus);
        MainDetail = Ui.Label("Inicialização segura em segundo plano.", 9.4f, Ui.Muted); MainDetail.Location = new Point(22, 86); hero.Controls.Add(MainDetail);
        var test = Ui.Button("Imprimir teste", true); test.Location = new Point(705, 54); test.Anchor = AnchorStyles.Top | AnchorStyles.Right; test.Click += async (_, _) => await PrintTestAsync(); hero.Controls.Add(test);
        p.Controls.Add(hero);

        p.Controls.Add(StatCard("IMPRESSORA", out PrinterStat, 0, 172));
        p.Controls.Add(StatCard("REDE", out NetworkStat, 295, 172));
        p.Controls.Add(StatCard("ATIVIDADE", out JobsStat, 590, 172));

        var c = Card(0, 304, 884, 142); c.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        var cc = Ui.Label("CONEXÃO", 8, Ui.Muted, FontStyle.Bold); cc.Location = new Point(20, 18); c.Controls.Add(cc);
        ClientsStat = Ui.Label("Aguardando...", 12, Ui.Text, FontStyle.Bold); ClientsStat.Location = new Point(20, 47); c.Controls.Add(ClientsStat);
        var open = Ui.Button("Ver PCs conectados"); open.Location = new Point(20, 88); open.Click += (_, _) => ShowPage("clients", "PCs conectados"); c.Controls.Add(open);
        p.Controls.Add(c);
    }

    Panel StatCard(string title, out Label value, int x, int y)
    {
        var c = Card(x, y, 274, 110);
        var t = Ui.Label(title, 8, Ui.Muted, FontStyle.Bold); t.Location = new Point(18, 16); c.Controls.Add(t);
        value = new Label { Text = "—", Location = new Point(18, 44), Size = new Size(236, 48), ForeColor = Ui.Text, BackColor = Ui.Card, Font = Ui.F(12, FontStyle.Bold) };
        c.Controls.Add(value); return c;
    }

    void BuildPrinter()
    {
        var p = NewPage("printer");
        var card = Card(0, 0, 884, 490); card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; p.Controls.Add(card);
        var title = Ui.Label("Impressora física", 15, Ui.Text, FontStyle.Bold); title.Location = new Point(20, 18); card.Controls.Add(title);
        var sub = Ui.Label("Fila USB, papel, raster, corte e propriedades do driver.", 9, Ui.Muted); sub.Location = new Point(22, 46); card.Controls.Add(sub);

        QueueCombo = NewCombo(C.LocalPrinterQueue); foreach (string q in PrinterSettings.InstalledPrinters) QueueCombo.Items.Add(q); PlaceField(card, "Fila local", QueueCombo, 20, 80, 390);
        NetworkNameText = Ui.TextBox(C.NetworkPrinterName); PlaceField(card, "Nome na rede", NetworkNameText, 450, 80, 390);

        PaperWidth = Ui.Number(C.PaperWidthMm, 40, 120); PlaceField(card, "Papel (mm)", PaperWidth, 20, 155, 170);
        PrintableWidth = Ui.Number(C.PrintableWidthMm, 30, 120); PlaceField(card, "Área útil (mm)", PrintableWidth, 230, 155, 170);
        Dots = Ui.Number(C.PrintWidthDots, 200, 1200); PlaceField(card, "Largura (dots)", Dots, 450, 155, 170);
        Dpi = Ui.Number(C.Dpi, 100, 600); PlaceField(card, "DPI", Dpi, 660, 155, 170);

        Columns = Ui.Number(C.Columns, 16, 80); PlaceField(card, "Colunas", Columns, 20, 230, 170);
        PageLength = Ui.Number(C.PageLengthMm, 50, 1000); PlaceField(card, "Página IPP (mm)", PageLength, 230, 230, 170);
        BottomMargin = Ui.Number(C.BottomMarginDots, 0, 500); PlaceField(card, "Margem final", BottomMargin, 450, 230, 170);
        FeedLines = Ui.Number(C.FeedLines, 0, 20); PlaceField(card, "Avanço", FeedLines, 660, 230, 170);

        CutCheck = Check("Cortar automaticamente", C.CutAfterJob, 20, 318); card.Controls.Add(CutCheck);
        TrimCheck = Check("Remover branco final", C.TrimBlank, 230, 318); card.Controls.Add(TrimCheck);

        var save = Ui.Button("Salvar impressora", true); save.Location = new Point(20, 382); save.Click += async (_, _) => await SaveSettingsAsync(true); card.Controls.Add(save);
        var test = Ui.Button("Teste 80 mm"); test.Location = new Point(185, 382); test.Click += async (_, _) => await PrintTestAsync(); card.Controls.Add(test);
        var props = Ui.Button("Propriedades do driver"); props.Location = new Point(310, 382); props.Click += (_, _) => OpenNativePrinterProperties(); card.Controls.Add(props);
    }

    void BuildClients()
    {
        var p = NewPage("clients");
        var card = Card(0, 0, 884, 500); card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; p.Controls.Add(card);
        var title = Ui.Label("PCs conectados", 15, Ui.Text, FontStyle.Bold); title.Location = new Point(20, 18); card.Controls.Add(title);
        var sub = Ui.Label("Atividade detectada por heartbeat, IPP e API.", 9, Ui.Muted); sub.Location = new Point(22, 46); card.Controls.Add(sub);

        ClientsGrid = new DataGridView
        {
            Location = new Point(20, 82), Size = new Size(842, 390), BackgroundColor = Ui.Card, BorderStyle = BorderStyle.None,
            GridColor = Ui.Border, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, EnableHeadersVisualStyles = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        ClientsGrid.ColumnHeadersDefaultCellStyle.BackColor = Ui.Card2; ClientsGrid.ColumnHeadersDefaultCellStyle.ForeColor = Ui.Text; ClientsGrid.ColumnHeadersDefaultCellStyle.Font = Ui.F(9, FontStyle.Bold);
        ClientsGrid.DefaultCellStyle.BackColor = Ui.Card; ClientsGrid.DefaultCellStyle.ForeColor = Ui.Text; ClientsGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(54, 19, 23); ClientsGrid.DefaultCellStyle.SelectionForeColor = Ui.Text;
        ClientsGrid.Columns.Add("status", "Status"); ClientsGrid.Columns.Add("pc", "PC"); ClientsGrid.Columns.Add("ip", "IP"); ClientsGrid.Columns.Add("via", "Via"); ClientsGrid.Columns.Add("printer", "Impressora"); ClientsGrid.Columns.Add("seen", "Última atividade");
        card.Controls.Add(ClientsGrid);
    }

    void BuildLogs()
    {
        var p = NewPage("logs");
        var card = Card(0, 0, 884, 500); card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; p.Controls.Add(card);
        var title = Ui.Label("Logs", 15, Ui.Text, FontStyle.Bold); title.Location = new Point(20, 18); card.Controls.Add(title);
        Logs = new RichTextBox { Location = new Point(20, 64), Size = new Size(842, 370), BackColor = Color.FromArgb(6, 6, 8), ForeColor = Color.FromArgb(205, 205, 214), BorderStyle = BorderStyle.None, ReadOnly = true, Font = new Font("Consolas", 9f) };
        card.Controls.Add(Logs);
        var open = Ui.Button("Abrir log completo"); open.Location = new Point(20, 448); open.Click += (_, _) => { if (File.Exists(Log.FilePath)) Process.Start(new ProcessStartInfo("notepad.exe", "\"" + Log.FilePath + "\"") { UseShellExecute = true }); }; card.Controls.Add(open);
    }

    void BuildSettings()
    {
        var p = NewPage("settings");
        var card = Card(0, 0, 884, 500); card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; p.Controls.Add(card);
        var title = Ui.Label("Rede e operação", 15, Ui.Text, FontStyle.Bold); title.Location = new Point(20, 18); card.Controls.Add(title);
        var sub = Ui.Label("Servidor, cliente, portas e integração com o Windows.", 9, Ui.Muted); sub.Location = new Point(22, 46); card.Controls.Add(sub);

        ModeCombo = NewCombo(C.Mode); ModeCombo.Items.AddRange(["auto", "server", "client"]); PlaceField(card, "Modo deste PC", ModeCombo, 20, 82, 190);
        ServerText = Ui.TextBox(C.ServerAddress); PlaceField(card, "Servidor", ServerText, 245, 82, 330);
        var discover = Ui.Button("Descobrir"); discover.Location = new Point(600, 102); discover.Click += async (_, _) => await DiscoverServerAsync(true); card.Controls.Add(discover);

        IppPort = Ui.Number(C.IppPort, 1, 65535); PlaceField(card, "IPP", IppPort, 20, 160, 170);
        ApiPort = Ui.Number(C.ApiPort, 1, 65535); PlaceField(card, "API", ApiPort, 245, 160, 170);
        DiscoveryPort = Ui.Number(C.DiscoveryPort, 1, 65535); PlaceField(card, "Discovery UDP", DiscoveryPort, 470, 160, 170);

        StartupCheck = Check("Iniciar com o Windows", C.StartWithWindows, 20, 245); card.Controls.Add(StartupCheck);
        MinimizeCheck = Check("Fechar para a bandeja", C.MinimizeOnClose, 245, 245); card.Controls.Add(MinimizeCheck);
        DiscoverCheck = Check("Descobrir servidor automaticamente", C.AutoDiscover, 470, 245); card.Controls.Add(DiscoverCheck);

        var install = Ui.Button("Instalar GTRZ POS-80", true); install.Location = new Point(20, 310); install.Click += async (_, _) => await InstallIppAsync(); card.Controls.Add(install);
        var remove = Ui.Button("Remover impressora"); remove.Location = new Point(205, 310); remove.Click += async (_, _) => await RemoveIppAsync(); card.Controls.Add(remove);
        var save = Ui.Button("Salvar e reiniciar serviços", true); save.Location = new Point(20, 382); save.Click += async (_, _) => await SaveSettingsAsync(true); card.Controls.Add(save);

        var note = Ui.Label("Ao iniciar como servidor, o GTRZ Printer libera automaticamente as portas ocupadas por servidores GTRZ antigos. Processos não reconhecidos não são encerrados silenciosamente.", 8.8f, Ui.Muted);
        note.Location = new Point(20, 440); note.MaximumSize = new Size(830, 0); card.Controls.Add(note);
    }

    static ComboBox NewCombo(string value) => new() { Text = value, BackColor = Ui.Card2, ForeColor = Ui.Text, FlatStyle = FlatStyle.Flat, Font = Ui.F(9.4f), DropDownStyle = ComboBoxStyle.DropDown };

    static CheckBox Check(string text, bool value, int x, int y) => new() { Text = text, Checked = value, AutoSize = true, Location = new Point(x, y), ForeColor = Ui.Text, BackColor = Ui.Card, FlatStyle = FlatStyle.Flat, Font = Ui.F(9.2f) };

    static void PlaceField(Control parent, string label, Control field, int x, int y, int width)
    {
        var l = Ui.Label(label, 8.3f, Ui.Muted, FontStyle.Bold); l.Location = new Point(x, y); parent.Controls.Add(l);
        field.Location = new Point(x, y + 24); field.Width = width; parent.Controls.Add(field);
    }

    void BuildTray()
    {
        Tray = new NotifyIcon { Text = "GTRZ Printer", Icon = SystemIcons.Application, Visible = true };
        var menu = new ContextMenuStrip(); menu.Items.Add("Abrir", null, (_, _) => RestoreFromTray()); menu.Items.Add("Imprimir teste", null, async (_, _) => await PrintTestAsync()); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Sair", null, (_, _) => { RealExit = true; Close(); });
        Tray.ContextMenuStrip = menu; Tray.DoubleClick += (_, _) => RestoreFromTray();
    }

    public void RestoreFromTray() { Show(); ShowInTaskbar = true; WindowState = FormWindowState.Normal; Activate(); BringToFront(); }
    void HideToTray() { Hide(); ShowInTaskbar = false; }

    async Task RestartRuntimeAsync()
    {
        StopRuntime(); RuntimeReady = false; MainStatus.Text = "Preparando o GTRZ Printer..."; MainDetail.Text = "Inicialização segura em segundo plano.";
        RuntimeCts = new CancellationTokenSource(); var token = RuntimeCts.Token;
        try
        {
            LocalIp = await Task.Run(AppConfig.LocalIp, token);
            if (C.Mode.Equals("server", StringComparison.OrdinalIgnoreCase)) ServerMode = true;
            else if (C.Mode.Equals("client", StringComparison.OrdinalIgnoreCase)) ServerMode = false;
            else ServerMode = await Task.Run(() => RawPrinter.IsLocalUsbPrinter(C.LocalPrinterQueue, out _), token);

            if (ServerMode)
            {
                await Task.Run(() => { PortGuard.ReleaseLegacyListeners(C.IppPort, C.ApiPort, C.DiscoveryPort); Proc.Firewall(C); }, token);
                Discovery = new DiscoveryService(C); Discovery.Start();
                Ipp = new IppServer(C, Clients, PrintLock); Ipp.Start();
                Api = new ApiServer(C, Clients, PrintLock); Api.Start();
            }
            else
            {
                if (C.AutoDiscover && (string.IsNullOrWhiteSpace(C.ServerAddress) || C.ServerAddress == LocalIp)) await DiscoverServerAsync(false);
                Client = new ClientAgent(C); Client.Start();
            }

            RuntimeReady = true;
            _ = Task.Run(() => StatusLoop(token), token);
            _ = Task.Run(() => Proc.AutoStart(C.StartWithWindows), token);
            Log.Info("Runtime 2.0 pronto: " + (ServerMode ? "SERVIDOR" : "CLIENTE"));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Error("Inicialização: " + ex); MainStatus.Text = "Serviço requer atenção"; MainDetail.Text = ex.Message; }
    }

    async Task StatusLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (ServerMode)
                {
                    var state = await Task.Run(() => { bool ok = RawPrinter.CanOpen(C.LocalPrinterQueue, out string err); return (ok, err); }, token);
                    PrinterReady = state.ok; PrinterError = state.err;
                }
                else { PrinterReady = Client?.PrinterInstalled == true; PrinterError = ""; }
            }
            catch { }
            await Task.Delay(1500, token);
        }
    }

    void StopRuntime()
    {
        try { RuntimeCts?.Cancel(); } catch { }
        try { Client?.Dispose(); } catch { } try { Api?.Dispose(); } catch { } try { Ipp?.Dispose(); } catch { } try { Discovery?.Dispose(); } catch { }
        Client = null; Api = null; Ipp = null; Discovery = null;
    }

    void RefreshUi()
    {
        ModeBadge.Text = ServerMode ? "SERVIDOR" : "CLIENTE"; ModeBadge.ForeColor = RuntimeReady ? Ui.RedHot : Ui.Amber; ModeBadge.BackColor = RuntimeReady ? Color.FromArgb(40, 15, 18) : Color.FromArgb(38, 31, 18);
        if (RuntimeReady)
        {
            if (ServerMode)
            {
                bool online = Ipp?.Running == true && Api != null;
                MainStatus.Text = online && PrinterReady ? "Tudo funcionando" : "Atenção necessária";
                MainDetail.Text = online ? (PrinterReady ? "IPP, API e POS-80 prontos." : "Servidor online; fila física indisponível: " + PrinterError) : "Serviço de rede indisponível.";
                PrinterStat.Text = C.LocalPrinterQueue + "\r\n" + (PrinterReady ? "PRONTA" : "INDISPONÍVEL");
                NetworkStat.Text = LocalIp + "\r\nIPP :" + C.IppPort + "  API :" + C.ApiPort;
                JobsStat.Text = "IPP " + (Ipp?.Printed ?? 0) + "   API " + (Api?.Printed ?? 0) + "\r\nFalhas " + ((Ipp?.Failed ?? 0) + (Api?.Failed ?? 0));
                ClientsStat.Text = Clients.Snapshot().Count + " PC(s) conhecido(s) na rede.";
            }
            else
            {
                bool online = Client?.Online == true;
                MainStatus.Text = online ? "Conectado ao servidor" : "Servidor indisponível";
                MainDetail.Text = online ? "Conexão estável com " + C.ServerAddress + "." : "A descoberta automática continuará tentando localizar o servidor.";
                PrinterStat.Text = C.NetworkPrinterName + "\r\n" + (PrinterReady ? "INSTALADA" : "NÃO INSTALADA");
                NetworkStat.Text = C.ServerAddress + "\r\nLatência " + (Client?.Latency ?? 0) + " ms";
                JobsStat.Text = "Último contato\r\n" + (Client == null || Client.LastSuccess == DateTime.MinValue ? "—" : Client.LastSuccess.ToString("HH:mm:ss"));
                ClientsStat.Text = online ? "Este PC está online no servidor GTRZ Printer." : "Sem heartbeat com o servidor.";
            }
        }
        RefreshClientsGrid(); DrainLogs();
        try { Tray.Text = "GTRZ Printer - " + MainStatus.Text; } catch { }
    }

    void RefreshClientsGrid()
    {
        if (ClientsGrid == null || !ServerMode) return;
        var snapshot = Clients.Snapshot(); ClientsGrid.SuspendLayout(); ClientsGrid.Rows.Clear();
        foreach (var c in snapshot)
        {
            bool on = (DateTime.Now - c.LastSeen).TotalSeconds <= C.OfflineSeconds;
            ClientsGrid.Rows.Add(on ? "● Online" : "○ Offline", string.IsNullOrWhiteSpace(c.ComputerName) ? "(IPP/API)" : c.ComputerName, c.Ip, c.Via, c.PrinterInstalled ? "Instalada" : "—", c.LastSeen.ToString("HH:mm:ss"));
        }
        ClientsGrid.ResumeLayout();
    }

    void DrainLogs()
    {
        if (Logs == null) return; int count = 0;
        while (count < 40 && Log.Lines.TryDequeue(out var line)) { Logs.AppendText(line + Environment.NewLine); count++; }
        if (Logs.TextLength > 180000) { Logs.Select(0, 70000); Logs.SelectedText = ""; }
        if (count > 0) { Logs.SelectionStart = Logs.TextLength; Logs.ScrollToCaret(); }
    }

    async Task PrintTestAsync()
    {
        try
        {
            if (ServerMode)
            {
                bool ok = await Task.Run(() => { byte[] data = Receipt.TextBytes(Receipt.Test(C), C, C.CutAfterJob); lock (PrintLock) return RawPrinter.Send(C.LocalPrinterQueue, data, "GTRZ Printer - Teste", out PrinterError); });
                if (!ok) throw new InvalidOperationException(PrinterError); Toast("Teste enviado para " + C.LocalPrinterQueue + ".");
            }
            else
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) }; http.DefaultRequestHeaders.Add("X-GTRZ-Print-Key", C.ApiKey); http.DefaultRequestHeaders.Add("X-GTRZ-Job-Id", Environment.MachineName + "-TEST-" + Guid.NewGuid().ToString("N"));
                var response = await http.PostAsync("http://" + C.ServerAddress + ":" + C.ApiPort + "/print80", new StringContent(Receipt.Test(C))); response.EnsureSuccessStatusCode(); Toast("Teste enviado ao servidor.");
            }
        }
        catch (Exception ex) { Log.Error("Teste: " + ex.Message); MessageBox.Show(ex.Message, "Falha no teste", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    async Task DiscoverServerAsync(bool notify)
    {
        string found = await Task.Run(() => DiscoveryService.Discover(C.DiscoveryPort, 2200));
        if (string.IsNullOrWhiteSpace(found)) { if (notify) MessageBox.Show("Nenhum servidor GTRZ Printer respondeu na rede local.", "Descoberta", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        C.ServerAddress = found; C.Save(); if (ServerText != null) ServerText.Text = found; Log.Info("Servidor descoberto: " + found); if (notify) Toast("Servidor encontrado: " + found);
    }

    async Task InstallIppAsync()
    {
        try
        {
            await SaveSettingsAsync(false); if (ServerMode) { MessageBox.Show("Este PC está em modo servidor. Instale no PC cliente.", "GTRZ Printer"); return; }
            await DiscoverServerAsync(false); if (C.ServerAddress == LocalIp) throw new InvalidOperationException("O endereço do servidor aponta para este próprio PC.");
            bool open = await Task.Run(() => Proc.TcpOpen(C.ServerAddress, C.IppPort, 2500, out _)); if (!open) throw new InvalidOperationException("Servidor IPP não respondeu em " + C.ServerAddress + ":" + C.IppPort + ".");
            await Task.Run(() => Proc.InstallIpp(C)); if (Client != null) Client.LastPrinterCheck = DateTime.MinValue; Toast(C.NetworkPrinterName + " instalada no Windows.");
        }
        catch (Exception ex) { Log.Error("Instalação IPP: " + ex.Message); MessageBox.Show(ex.Message, "Falha ao instalar", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    async Task RemoveIppAsync()
    {
        if (MessageBox.Show("Remover " + C.NetworkPrinterName + " deste PC?", "GTRZ Printer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try { await Task.Run(() => Proc.RemoveIpp(C)); if (Client != null) Client.LastPrinterCheck = DateTime.MinValue; Toast("Impressora removida."); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "GTRZ Printer", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    async Task SaveSettingsAsync(bool restart)
    {
        if (ModeCombo != null) C.Mode = ModeCombo.Text.Trim().ToLowerInvariant(); if (QueueCombo != null && !string.IsNullOrWhiteSpace(QueueCombo.Text)) C.LocalPrinterQueue = QueueCombo.Text.Trim();
        if (NetworkNameText != null && !string.IsNullOrWhiteSpace(NetworkNameText.Text)) C.NetworkPrinterName = NetworkNameText.Text.Trim(); if (ServerText != null && !string.IsNullOrWhiteSpace(ServerText.Text)) C.ServerAddress = ServerText.Text.Trim();
        if (IppPort != null) C.IppPort = (int)IppPort.Value; if (ApiPort != null) C.ApiPort = (int)ApiPort.Value; if (DiscoveryPort != null) C.DiscoveryPort = (int)DiscoveryPort.Value;
        if (PaperWidth != null) C.PaperWidthMm = (int)PaperWidth.Value; if (PrintableWidth != null) C.PrintableWidthMm = (int)PrintableWidth.Value; if (Dots != null) C.PrintWidthDots = (int)Dots.Value; if (Dpi != null) C.Dpi = (int)Dpi.Value;
        if (Columns != null) C.Columns = (int)Columns.Value; if (PageLength != null) C.PageLengthMm = (int)PageLength.Value; if (BottomMargin != null) C.BottomMarginDots = (int)BottomMargin.Value; if (FeedLines != null) C.FeedLines = (int)FeedLines.Value;
        if (CutCheck != null) C.CutAfterJob = CutCheck.Checked; if (TrimCheck != null) C.TrimBlank = TrimCheck.Checked; if (StartupCheck != null) C.StartWithWindows = StartupCheck.Checked; if (MinimizeCheck != null) C.MinimizeOnClose = MinimizeCheck.Checked; if (DiscoverCheck != null) C.AutoDiscover = DiscoverCheck.Checked;
        C.Save(); await Task.Run(() => Proc.AutoStart(C.StartWithWindows)); if (restart) { await RestartRuntimeAsync(); Toast("Configurações salvas."); }
    }

    void OpenNativePrinterProperties()
    {
        try { string queue = string.IsNullOrWhiteSpace(QueueCombo?.Text) ? C.LocalPrinterQueue : QueueCombo.Text; Process.Start(new ProcessStartInfo("rundll32.exe", "printui.dll,PrintUIEntry /p /n \"" + queue + "\"") { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "GTRZ Printer", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    void Toast(string text) { Tray.BalloonTipTitle = "GTRZ Printer"; Tray.BalloonTipText = text; Tray.ShowBalloonTip(2200); }
}
