using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SiragonBatteryGuard
{
    public class AppConfig
    {
        public int MaxChargeLimit { get; set; }
        public int ResumeChargeLimit { get; set; }
        public int LowBatteryAlert { get; set; }
        public bool EnableAlerts { get; set; }
        public bool EnableSound { get; set; }
        public bool AutoCoolOnCharge { get; set; }
        public int SelectedPowerMode { get; set; }

        public AppConfig()
        {
            MaxChargeLimit = 80;
            ResumeChargeLimit = 75;
            LowBatteryAlert = 20;
            EnableAlerts = true;
            EnableSound = true;
            AutoCoolOnCharge = false;
            SelectedPowerMode = -1;
        }

        private static string ConfigPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json"); }
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine(string.Format("  \"MaxChargeLimit\": {0},", MaxChargeLimit));
                sb.AppendLine(string.Format("  \"ResumeChargeLimit\": {0},", ResumeChargeLimit));
                sb.AppendLine(string.Format("  \"LowBatteryAlert\": {0},", LowBatteryAlert));
                sb.AppendLine(string.Format("  \"EnableAlerts\": {0},", EnableAlerts.ToString().ToLowerInvariant()));
                sb.AppendLine(string.Format("  \"EnableSound\": {0},", EnableSound.ToString().ToLowerInvariant()));
                sb.AppendLine(string.Format("  \"AutoCoolOnCharge\": {0},", AutoCoolOnCharge.ToString().ToLowerInvariant()));
                sb.AppendLine(string.Format("  \"SelectedPowerMode\": {0}", SelectedPowerMode));
                sb.AppendLine("}");
                File.WriteAllText(ConfigPath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public static AppConfig Load()
        {
            var cfg = new AppConfig();
            try
            {
                if (!File.Exists(ConfigPath)) return cfg;
                string json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                cfg.MaxChargeLimit = ParseInt(json, "MaxChargeLimit", 80);
                cfg.ResumeChargeLimit = ParseInt(json, "ResumeChargeLimit", 75);
                cfg.LowBatteryAlert = ParseInt(json, "LowBatteryAlert", 20);
                cfg.EnableAlerts = ParseBool(json, "EnableAlerts", true);
                cfg.EnableSound = ParseBool(json, "EnableSound", true);
                cfg.AutoCoolOnCharge = ParseBool(json, "AutoCoolOnCharge", false);
                cfg.SelectedPowerMode = ParseInt(json, "SelectedPowerMode", -1);
            }
            catch { }
            return cfg;
        }

        private static int ParseInt(string json, string key, int def)
        {
            int idx = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return def;
            int colon = json.IndexOf(':', idx);
            if (colon < 0) return def;
            int end = json.IndexOfAny(new[] { ',', '}', '\r', '\n' }, colon + 1);
            if (end < 0) return def;
            string raw = json.Substring(colon + 1, end - colon - 1).Trim();
            int val;
            return int.TryParse(raw, out val) ? val : def;
        }

        private static bool ParseBool(string json, string key, bool def)
        {
            int idx = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return def;
            int colon = json.IndexOf(':', idx);
            if (colon < 0) return def;
            int end = json.IndexOfAny(new[] { ',', '}', '\r', '\n' }, colon + 1);
            if (end < 0) return def;
            string raw = json.Substring(colon + 1, end - colon - 1).Trim();
            bool val;
            return bool.TryParse(raw, out val) ? val : def;
        }
    }

    public class BatteryTelemetry
    {
        public int Percentage { get; set; }
        public bool PowerOnline { get; set; }
        public bool Charging { get; set; }
        public bool Discharging { get; set; }
        public double RemainingWh { get; set; }
        public double FullChargeWh { get; set; }
        public double ChargeRateWatts { get; set; }
        public double Voltage { get; set; }
        public int CpuTempC { get; set; }
        public int ActivePowerMode { get; set; }

        public BatteryTelemetry()
        {
            CpuTempC = -1;
            ActivePowerMode = 1;
        }
    }

    public static class SiragonPowerController
    {
        [DllImport("powrprof.dll")]
        private static extern uint PowerGetEffectiveOverlayScheme(out Guid EffectiveOverlayGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveOverlayScheme(Guid OverlaySchemeGuid);

        // Windows 11 Modern Standby Overlay GUIDs
        public static readonly Guid GUID_ECO = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a");
        public static readonly Guid GUID_BALANCED = Guid.Empty;
        public static readonly Guid GUID_PERFORMANCE = new Guid("ded574b5-45a0-4f42-8737-46345c09c238");

        private static int _customModeOverride = -1;

        public static BatteryTelemetry ReadTelemetry()
        {
            var t = new BatteryTelemetry();
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM BatteryFullChargedCapacity"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        uint fullMwh = Convert.ToUInt32(obj["FullChargedCapacity"]);
                        t.FullChargeWh = Math.Round(fullMwh / 1000.0, 2);
                        break;
                    }
                }
            }
            catch { t.FullChargeWh = 60.0; }

            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM BatteryStatus"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        t.Charging = Convert.ToBoolean(obj["Charging"]);
                        t.Discharging = Convert.ToBoolean(obj["Discharging"]);
                        t.PowerOnline = Convert.ToBoolean(obj["PowerOnline"]);
                        uint remMwh = Convert.ToUInt32(obj["RemainingCapacity"]);
                        t.RemainingWh = Math.Round(remMwh / 1000.0, 2);

                        int chgRate = Convert.ToInt32(obj["ChargeRate"]);
                        int disRate = Convert.ToInt32(obj["DischargeRate"]);
                        int rateMw = t.Charging ? chgRate : (t.Discharging ? disRate : 0);
                        t.ChargeRateWatts = Math.Round(Math.Abs(rateMw) / 1000.0, 2);

                        uint mv = Convert.ToUInt32(obj["Voltage"]);
                        t.Voltage = Math.Round(mv / 1000.0, 2);

                        if (t.FullChargeWh > 0)
                        {
                            t.Percentage = (int)Math.Round((t.RemainingWh / t.FullChargeWh) * 100.0);
                            if (t.Percentage > 100) t.Percentage = 100;
                        }
                        break;
                    }
                }
            }
            catch
            {
                PowerStatus ps = SystemInformation.PowerStatus;
                t.Percentage = (int)Math.Round(ps.BatteryLifePercent * 100);
                t.PowerOnline = ps.PowerLineStatus == PowerLineStatus.Online;
            }

            // Read CPU/EC Temperature via ACPI ThermalZone TZ01 (reads EC0.CPUT offset 0x70 without needing Admin)
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT Temperature, HighPrecisionTemperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        uint kelvin = Convert.ToUInt32(obj["Temperature"]);
                        if (kelvin > 273 && kelvin < 400)
                        {
                            t.CpuTempC = (int)(kelvin - 273);
                            break;
                        }
                    }
                }
            }
            catch { }

            t.ActivePowerMode = GetCurrentPowerMode();
            return t;
        }

        public static int GetCurrentPowerMode()
        {
            if (_customModeOverride == 3) return 3;
            try
            {
                Guid active;
                if (PowerGetEffectiveOverlayScheme(out active) == 0)
                {
                    if (active == GUID_ECO) return 0;
                    if (active == GUID_BALANCED) return 1;
                    if (active == GUID_PERFORMANCE) return 2;
                }
            }
            catch { }
            return 1;
        }

        public static bool SetPowerMode(int mode, out string statusMsg)
        {
            // mode 0: Eco / Silencio
            // mode 1: Equilibrado
            // mode 2: Alto Rendimiento
            // mode 3: Carga Fría (limita CPU al 70% para enfriar batería mientras carga)
            try
            {
                Guid targetGuid = GUID_BALANCED;
                int maxCpuAc = 100;
                int maxCpuDc = 100;
                string modeLabel = "Equilibrado";

                if (mode == 0)
                {
                    targetGuid = GUID_ECO;
                    maxCpuAc = 85;
                    maxCpuDc = 75;
                    modeLabel = "Eco / Silencio (Ahorro y menor calor)";
                    _customModeOverride = -1;
                }
                else if (mode == 1)
                {
                    targetGuid = GUID_BALANCED;
                    maxCpuAc = 100;
                    maxCpuDc = 90;
                    modeLabel = "Equilibrado (Estándar)";
                    _customModeOverride = -1;
                }
                else if (mode == 2)
                {
                    targetGuid = GUID_PERFORMANCE;
                    maxCpuAc = 100;
                    maxCpuDc = 100;
                    modeLabel = "Alto Rendimiento / Gaming";
                    _customModeOverride = -1;
                }
                else if (mode == 3)
                {
                    targetGuid = GUID_ECO;
                    maxCpuAc = 65;
                    maxCpuDc = 65;
                    modeLabel = "Carga Fría (CPU limitado al 65% para proteger la batería)";
                    _customModeOverride = 3;
                }

                uint res = PowerSetActiveOverlayScheme(targetGuid);
                RunPowerCfg(string.Format("/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX {0}", maxCpuAc));
                RunPowerCfg(string.Format("/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX {0}", maxCpuDc));
                RunPowerCfg("/setactive SCHEME_CURRENT");

                if (res == 0)
                {
                    statusMsg = "Modo activado: " + modeLabel;
                    return true;
                }
                else
                {
                    statusMsg = "Modo aplicado mediante plan de energía: " + modeLabel;
                    return true;
                }
            }
            catch (Exception ex)
            {
                statusMsg = "Error al cambiar el modo: " + ex.Message;
                return false;
            }
        }

        private static void RunPowerCfg(string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process p = Process.Start(psi))
                {
                    if (p != null) p.WaitForExit(1500);
                }
            }
            catch { }
        }
    }

    public class MainForm : Form
    {
        private AppConfig _config;
        private Timer _pollTimer;
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private DateTime _lastHighAlertTime = DateTime.MinValue;
        private DateTime _lastLowAlertTime = DateTime.MinValue;
        private bool _allowExit = false;

        // UI Controls
        private Label _lblPercent;
        private Label _lblState;
        private Label _lblEta;
        private Panel _barTrack;
        private Panel _barFill;
        private Panel _barLimitMarker;
        private Label _lblPowerWatts;
        private Label _lblVoltage;
        private Label _lblCapacity;
        private Label _lblCpuTemp;

        private NumericUpDown _numMaxLimit;
        private NumericUpDown _numLowLimit;
        private CheckBox _chkAlerts;
        private CheckBox _chkSound;
        private CheckBox _chkAutoCool;
        private CheckBox _chkAutoStart;
        private Button _btnApplyLimit;
        private Button _btnTestAlert;
        private Label _lblStatusToast;

        private Button[] _modeButtons = new Button[4];

        public MainForm()
        {
            _config = AppConfig.Load();
            InitializeUI();
            InitializeTray();

            if (_config.SelectedPowerMode >= 0 && _config.SelectedPowerMode <= 3)
            {
                string msg;
                SiragonPowerController.SetPowerMode(_config.SelectedPowerMode, out msg);
            }

            _pollTimer = new Timer();
            _pollTimer.Interval = 3000;
            _pollTimer.Tick += (s, e) => RefreshTelemetry();
            _pollTimer.Start();

            RefreshTelemetry();
        }

        private Icon LoadAppIcon()
        {
            try
            {
                string icoFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "battery.ico");
                if (File.Exists(icoFile)) return new Icon(icoFile);
                Icon assoc = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (assoc != null) return assoc;
            }
            catch { }
            return SystemIcons.Shield;
        }

        private void InitializeUI()
        {
            Text = "Síragon Battery Guard — UB-8000 (ARN25C-140M)";
            Icon = LoadAppIcon();
            Size = new Size(540, 625);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(18, 20, 28);
            ForeColor = Color.FromArgb(235, 240, 250);
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            // Header Panel
            var header = new Panel
            {
                Location = new Point(20, 18),
                Size = new Size(485, 138),
                BackColor = Color.FromArgb(26, 30, 42)
            };

            _lblPercent = new Label
            {
                Text = "--%",
                Font = new Font("Segoe UI", 32f, FontStyle.Bold),
                ForeColor = Color.FromArgb(74, 222, 128),
                Location = new Point(16, 12),
                Size = new Size(165, 62),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _lblState = new Label
            {
                Text = "Monitoreando batería...",
                Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 210, 230),
                Location = new Point(185, 18),
                Size = new Size(290, 26)
            };

            _lblEta = new Label
            {
                Text = "Calculando tiempo estimado...",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(185, 46),
                Size = new Size(290, 24)
            };

            _barTrack = new Panel
            {
                Location = new Point(20, 90),
                Size = new Size(445, 24),
                BackColor = Color.FromArgb(14, 16, 22)
            };

            _barFill = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(180, 24),
                BackColor = Color.FromArgb(74, 222, 128)
            };

            _barLimitMarker = new Panel
            {
                Location = new Point(356, 0),
                Size = new Size(3, 24),
                BackColor = Color.FromArgb(251, 191, 36)
            };

            _barTrack.Controls.Add(_barLimitMarker);
            _barTrack.Controls.Add(_barFill);
            _barLimitMarker.BringToFront();

            header.Controls.Add(_lblPercent);
            header.Controls.Add(_lblState);
            header.Controls.Add(_lblEta);
            header.Controls.Add(_barTrack);
            Controls.Add(header);

            // Telemetry Grid (4 cards)
            _lblPowerWatts = CreateStatCard("Potencia", "-- W", 20, 168);
            _lblVoltage = CreateStatCard("Voltaje", "-- V", 145, 168);
            _lblCapacity = CreateStatCard("Energía (60 Wh)", "-- Wh", 270, 168);
            _lblCpuTemp = CreateStatCard("Temp CPU (EC)", "-- °C", 395, 168);

            // Charge Limit Section
            var limitPanel = new Panel
            {
                Location = new Point(20, 250),
                Size = new Size(485, 192),
                BackColor = Color.FromArgb(26, 30, 42)
            };

            var lblSection1 = new Label
            {
                Text = "⚡ Límite de Carga y Protección Térmica",
                Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(16, 10),
                Size = new Size(360, 24)
            };

            var lblMax = new Label
            {
                Text = "Límite máximo de carga (%):",
                Location = new Point(16, 42),
                Size = new Size(210, 24)
            };

            _numMaxLimit = new NumericUpDown
            {
                Minimum = 50,
                Maximum = 100,
                Value = _config.MaxChargeLimit,
                Location = new Point(230, 40),
                Size = new Size(70, 26),
                BackColor = Color.FromArgb(18, 20, 28),
                ForeColor = Color.White
            };
            _numMaxLimit.ValueChanged += (s, e) => UpdateLimitMarker();

            var btnPreset80 = new Button
            {
                Text = "80% (Recomendado)",
                Location = new Point(315, 38),
                Size = new Size(150, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnPreset80.FlatAppearance.BorderSize = 0;
            btnPreset80.Click += (s, e) =>
            {
                _numMaxLimit.Value = 80;
                _numLowLimit.Value = 20;
                SaveAndApplyLimit();
            };

            var lblLow = new Label
            {
                Text = "Avisar batería baja al llegar a (%):",
                Location = new Point(16, 74),
                Size = new Size(210, 24)
            };

            _numLowLimit = new NumericUpDown
            {
                Minimum = 10,
                Maximum = 45,
                Value = _config.LowBatteryAlert,
                Location = new Point(230, 72),
                Size = new Size(70, 26),
                BackColor = Color.FromArgb(18, 20, 28),
                ForeColor = Color.White
            };

            _chkAlerts = new CheckBox
            {
                Text = "Notificar al llegar al límite",
                Checked = _config.EnableAlerts,
                Location = new Point(18, 106),
                Size = new Size(210, 22)
            };
            _chkAlerts.CheckedChanged += (s, e) => SaveUserConfig();

            _chkSound = new CheckBox
            {
                Text = "Aviso sonoro",
                Checked = _config.EnableSound,
                Location = new Point(240, 106),
                Size = new Size(115, 22)
            };
            _chkSound.CheckedChanged += (s, e) => SaveUserConfig();

            _chkAutoCool = new CheckBox
            {
                Text = "Activar modo Carga Fría automáticamente al llegar al límite",
                Checked = _config.AutoCoolOnCharge,
                Location = new Point(18, 128),
                Size = new Size(440, 22)
            };
            _chkAutoCool.CheckedChanged += (s, e) => SaveUserConfig();

            _btnApplyLimit = new Button
            {
                Text = "Guardar Límite de Carga",
                Location = new Point(16, 154),
                Size = new Size(265, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(22, 163, 74),
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnApplyLimit.FlatAppearance.BorderSize = 0;
            _btnApplyLimit.Click += (s, e) => SaveAndApplyLimit();

            _btnTestAlert = new Button
            {
                Text = "Probar Notificación",
                Location = new Point(295, 154),
                Size = new Size(170, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(38, 44, 60),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnTestAlert.FlatAppearance.BorderSize = 0;
            _btnTestAlert.Click += (s, e) =>
            {
                _trayIcon.ShowBalloonTip(
                    5000,
                    string.Format("Prueba — Límite configurado al {0}%", (int)_numMaxLimit.Value),
                    "Síragon Battery Guard te avisará así cuando tu batería alcance el límite.",
                    ToolTipIcon.Info
                );
                if (_chkSound.Checked) SystemSounds.Asterisk.Play();
                _lblStatusToast.ForeColor = Color.FromArgb(56, 189, 248);
                _lblStatusToast.Text = "Notificación de prueba enviada correctamente.";
            };

            limitPanel.Controls.Add(lblSection1);
            limitPanel.Controls.Add(lblMax);
            limitPanel.Controls.Add(_numMaxLimit);
            limitPanel.Controls.Add(btnPreset80);
            limitPanel.Controls.Add(lblLow);
            limitPanel.Controls.Add(_numLowLimit);
            limitPanel.Controls.Add(_chkAlerts);
            limitPanel.Controls.Add(_chkSound);
            limitPanel.Controls.Add(_chkAutoCool);
            limitPanel.Controls.Add(_btnApplyLimit);
            limitPanel.Controls.Add(_btnTestAlert);
            Controls.Add(limitPanel);

            // Power Modes Section
            var powerPanel = new Panel
            {
                Location = new Point(20, 452),
                Size = new Size(485, 84),
                BackColor = Color.FromArgb(26, 30, 42)
            };

            var lblPowerTitle = new Label
            {
                Text = "🎛️ Modos de Energía y Temperatura (Ryzen 7 7840U)",
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(167, 139, 250),
                Location = new Point(16, 10),
                Size = new Size(440, 20)
            };
            powerPanel.Controls.Add(lblPowerTitle);

            string[] modeNames = { "🌿 Eco / Silencio", "⚖️ Equilibrado", "🚀 Rendimiento", "❄️ Carga Fría" };
            for (int i = 0; i < 4; i++)
            {
                int modeIdx = i;
                var btn = new Button
                {
                    Text = modeNames[i],
                    Location = new Point(14 + (i * 116), 38),
                    Size = new Size(110, 34),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(38, 44, 60),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                    Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderSize = 0;
                btn.Click += (s, e) =>
                {
                    string msg;
                    SiragonPowerController.SetPowerMode(modeIdx, out msg);
                    _config.SelectedPowerMode = modeIdx;
                    _config.Save();
                    _lblStatusToast.ForeColor = Color.FromArgb(74, 222, 128);
                    _lblStatusToast.Text = msg;
                    RefreshTelemetry();
                };
                _modeButtons[i] = btn;
                powerPanel.Controls.Add(btn);
            }
            Controls.Add(powerPanel);

            // Footer
            _chkAutoStart = new CheckBox
            {
                Text = "Iniciar automáticamente con Windows",
                Checked = IsStartupRegistered(),
                Location = new Point(22, 544),
                Size = new Size(260, 24)
            };
            _chkAutoStart.CheckedChanged += (s, e) => SetStartupRegistration(_chkAutoStart.Checked);

            var btnMinimizeTray = new Button
            {
                Text = "Ocultar en Bandeja",
                Location = new Point(365, 540),
                Size = new Size(140, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(38, 44, 60),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnMinimizeTray.FlatAppearance.BorderSize = 0;
            btnMinimizeTray.Click += (s, e) => HideToTray();

            _lblStatusToast = new Label
            {
                Text = "Listo — Monitoreando tu Síragon UB-8000 en tiempo real.",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Italic),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(22, 570),
                Size = new Size(480, 20)
            };

            Controls.Add(_chkAutoStart);
            Controls.Add(btnMinimizeTray);
            Controls.Add(_lblStatusToast);

            UpdateLimitMarker();
        }

        private void UpdateLimitMarker()
        {
            if (_barLimitMarker == null) return;
            int maxLim = (int)_numMaxLimit.Value;
            int x = (int)Math.Round((maxLim / 100.0) * 445) - 2;
            _barLimitMarker.Location = new Point(Math.Max(0, Math.Min(442, x)), 0);
        }

        private Label CreateStatCard(string title, string initialValue, int x, int y)
        {
            var card = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(110, 70),
                BackColor = Color.FromArgb(26, 30, 42)
            };

            var lblTitle = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 8f, FontStyle.Regular),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(8, 8),
                Size = new Size(96, 18)
            };

            var lblVal = new Label
            {
                Text = initialValue,
                Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(8, 30),
                Size = new Size(96, 30)
            };

            card.Controls.Add(lblTitle);
            card.Controls.Add(lblVal);
            Controls.Add(card);
            return lblVal;
        }

        private void InitializeTray()
        {
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("Abrir Síragon Battery Guard", null, (s, e) => ShowFromTray());
            _trayMenu.Items.Add("Fijar límite al 80%", null, (s, e) =>
            {
                _numMaxLimit.Value = 80;
                SaveAndApplyLimit();
            });
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Modo: 🌿 Eco / Silencio", null, (s, e) => SwitchModeFromTray(0));
            _trayMenu.Items.Add("Modo: ⚖️ Equilibrado", null, (s, e) => SwitchModeFromTray(1));
            _trayMenu.Items.Add("Modo: 🚀 Rendimiento", null, (s, e) => SwitchModeFromTray(2));
            _trayMenu.Items.Add("Modo: ❄️ Carga Fría", null, (s, e) => SwitchModeFromTray(3));
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Salir por completo", null, (s, e) =>
            {
                _allowExit = true;
                _trayIcon.Visible = false;
                Application.Exit();
            });

            _trayIcon = new NotifyIcon
            {
                Icon = this.Icon ?? LoadAppIcon(),
                Text = "Síragon Battery Guard",
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _trayIcon.DoubleClick += (s, e) => ShowFromTray();
        }

        private void SwitchModeFromTray(int modeIdx)
        {
            string msg;
            SiragonPowerController.SetPowerMode(modeIdx, out msg);
            _config.SelectedPowerMode = modeIdx;
            _config.Save();
            _lblStatusToast.Text = msg;
            RefreshTelemetry();
        }

        private void RefreshTelemetry()
        {
            BatteryTelemetry t = SiragonPowerController.ReadTelemetry();

            _lblPercent.Text = t.Percentage + "%";
            int barWidth = (int)Math.Round((t.Percentage / 100.0) * 445);
            _barFill.Width = Math.Max(6, Math.Min(445, barWidth));

            int maxLim = (int)_numMaxLimit.Value;
            int lowLim = (int)_numLowLimit.Value;

            if (t.Charging && t.Percentage >= maxLim)
            {
                _lblPercent.ForeColor = Color.FromArgb(251, 191, 36);
                _barFill.BackColor = Color.FromArgb(251, 191, 36);
                _lblState.Text = string.Format("⚠️ ¡Límite de {0}% alcanzado! Desconecta el cargador", maxLim);
                _lblEta.Text = "Meta alcanzada — Desenchufa para evitar desgaste químico.";
            }
            else if (t.Charging)
            {
                _lblPercent.ForeColor = Color.FromArgb(56, 189, 248);
                _barFill.BackColor = Color.FromArgb(56, 189, 248);
                _lblState.Text = string.Format("🔌 Cargando hacia el límite de {0}%", maxLim);

                double targetWh = (maxLim / 100.0) * t.FullChargeWh;
                double neededWh = targetWh - t.RemainingWh;
                if (neededWh > 0 && t.ChargeRateWatts > 1.0)
                {
                    int mins = (int)Math.Round((neededWh / t.ChargeRateWatts) * 60.0);
                    _lblEta.Text = string.Format("Tiempo estimado hasta {0}%: ~{1} min ({2:0.0} W)", maxLim, mins, t.ChargeRateWatts);
                }
                else
                {
                    _lblEta.Text = "Cargando batería...";
                }
            }
            else if (t.PowerOnline && !t.Charging)
            {
                _lblPercent.ForeColor = Color.FromArgb(74, 222, 128);
                _barFill.BackColor = Color.FromArgb(74, 222, 128);
                _lblState.Text = "✅ Conectado (Sin corriente de carga)";
                _lblEta.Text = "La batería no está recibiendo carga adicional.";
            }
            else
            {
                _lblPercent.ForeColor = t.Percentage <= lowLim
                    ? Color.FromArgb(248, 113, 113)
                    : Color.FromArgb(74, 222, 128);
                _barFill.BackColor = _lblPercent.ForeColor;
                _lblState.Text = "🔋 Usando batería (Desconectado)";

                if (t.ChargeRateWatts > 1.0 && t.RemainingWh > 0)
                {
                    double hrs = t.RemainingWh / t.ChargeRateWatts;
                    int h = (int)Math.Floor(hrs);
                    int m = (int)Math.Round((hrs - h) * 60);
                    _lblEta.Text = string.Format("Autonomía estimada al ritmo actual: ~{0}h {1}m", h, m);
                }
                else
                {
                    _lblEta.Text = string.Format("Límite configurado: {0}% máx | {1}% mín", maxLim, lowLim);
                }
            }

            string sign = t.Charging ? "+" : (t.Discharging ? "-" : "");
            _lblPowerWatts.Text = string.Format("{0}{1:0.0} W", sign, t.ChargeRateWatts);
            _lblVoltage.Text = string.Format("{0:0.00} V", t.Voltage);
            _lblCapacity.Text = string.Format("{0:0.0} Wh", t.RemainingWh);

            if (t.CpuTempC > 0)
            {
                _lblCpuTemp.Text = t.CpuTempC + " °C";
                _lblCpuTemp.ForeColor = t.CpuTempC >= 80
                    ? Color.FromArgb(248, 113, 113)
                    : (t.CpuTempC >= 68 ? Color.FromArgb(251, 191, 36) : Color.White);
            }
            else
            {
                _lblCpuTemp.Text = "-- °C";
            }

            for (int i = 0; i < 4; i++)
            {
                if (_modeButtons[i] != null)
                {
                    _modeButtons[i].BackColor = (t.ActivePowerMode == i)
                        ? Color.FromArgb(124, 58, 237)
                        : Color.FromArgb(38, 44, 60);
                }
            }

            string tooltip = string.Format("Síragon: {0}% ({1}{2:0.0}W) | CPU {3}°C | Límite {4}%",
                t.Percentage, sign, t.ChargeRateWatts, t.CpuTempC > 0 ? t.CpuTempC.ToString() : "--", maxLim);
            if (tooltip.Length > 63) tooltip = tooltip.Substring(0, 63);
            _trayIcon.Text = tooltip;

            // High charge alert & auto-cool
            if (t.PowerOnline && t.Charging && t.Percentage >= maxLim)
            {
                if (_chkAutoCool.Checked && t.ActivePowerMode != 3)
                {
                    string coolMsg;
                    SiragonPowerController.SetPowerMode(3, out coolMsg);
                    _lblStatusToast.Text = "Modo Carga Fría activado automáticamente para proteger la batería.";
                }

                if (_chkAlerts.Checked && (DateTime.Now - _lastHighAlertTime).TotalMinutes >= 4)
                {
                    _lastHighAlertTime = DateTime.Now;
                    _trayIcon.ShowBalloonTip(
                        6000,
                        string.Format("⚡ Batería al {0}% (Límite {1}%)", t.Percentage, maxLim),
                        "Tu Síragon UB-8000 ya alcanzó el límite de protección. Desconecta el cargador para cuidar la batería.",
                        ToolTipIcon.Warning
                    );
                    if (_chkSound.Checked)
                    {
                        SystemSounds.Asterisk.Play();
                    }
                }
            }

            // Low battery alert
            if (!t.PowerOnline && t.Discharging && t.Percentage <= lowLim)
            {
                if (_chkAlerts.Checked && (DateTime.Now - _lastLowAlertTime).TotalMinutes >= 5)
                {
                    _lastLowAlertTime = DateTime.Now;
                    _trayIcon.ShowBalloonTip(
                        6000,
                        string.Format("🔋 Batería baja ({0}%)", t.Percentage),
                        "Conecta tu cargador para evitar descargas profundas.",
                        ToolTipIcon.Warning
                    );
                    if (_chkSound.Checked)
                    {
                        SystemSounds.Exclamation.Play();
                    }
                }
            }
        }

        private void SaveUserConfig()
        {
            _config.MaxChargeLimit = (int)_numMaxLimit.Value;
            _config.LowBatteryAlert = (int)_numLowLimit.Value;
            _config.EnableAlerts = _chkAlerts.Checked;
            _config.EnableSound = _chkSound.Checked;
            _config.AutoCoolOnCharge = _chkAutoCool.Checked;
            _config.Save();
            UpdateLimitMarker();
        }

        private void SaveAndApplyLimit()
        {
            SaveUserConfig();
            _lblStatusToast.ForeColor = Color.FromArgb(74, 222, 128);
            _lblStatusToast.Text = string.Format("Límite guardado: Máx {0}% | Mín {1}%. Monitoreo activo.", _config.MaxChargeLimit, _config.LowBatteryAlert);
            RefreshTelemetry();
        }

        private bool IsStartupRegistered()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    return key != null && key.GetValue("SiragonBatteryGuard") != null;
                }
            }
            catch { return false; }
        }

        private void SetStartupRegistration(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) return;
                    if (enable)
                    {
                        key.SetValue("SiragonBatteryGuard", "\"" + Application.ExecutablePath + "\" --tray");
                        _lblStatusToast.Text = "Inicio automático con Windows activado.";
                    }
                    else
                    {
                        key.DeleteValue("SiragonBatteryGuard", false);
                        _lblStatusToast.Text = "Inicio automático con Windows desactivado.";
                    }
                }
            }
            catch (Exception ex)
            {
                _lblStatusToast.Text = "No se pudo cambiar el inicio: " + ex.Message;
            }
        }

        private void HideToTray()
        {
            Hide();
            _trayIcon.ShowBalloonTip(
                2500,
                "Síragon Battery Guard",
                "Ejecutándose en segundo plano junto al reloj.",
                ToolTipIcon.Info
            );
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            _trayIcon.Visible = false;
            base.OnFormClosing(e);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var form = new MainForm();
            if (args != null && args.Length > 0 && args[0] == "--tray")
            {
                form.WindowState = FormWindowState.Minimized;
                form.ShowInTaskbar = false;
            }
            Application.Run(form);
        }
    }
}
