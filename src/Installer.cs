using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SiragonBatteryGuardInstaller
{
    public class InstallerForm : Form
    {
        private CheckBox _chkDesktop;
        private CheckBox _chkStartMenu;
        private CheckBox _chkAutoStart;
        private CheckBox _chkLaunchNow;
        private Button _btnInstall;
        private Label _lblStatus;
        private Panel _progressBarFill;
        private string _installDir;

        public InstallerForm()
        {
            _installDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\SiragonBatteryGuard"
            );
            InitializeUI();
        }

        private Icon LoadEmbeddedIcon()
        {
            try
            {
                Icon assoc = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (assoc != null) return assoc;
            }
            catch { }
            return SystemIcons.Application;
        }

        private void InitializeUI()
        {
            Text = "Instalador — Síragon Battery Guard v2.0";
            Icon = LoadEmbeddedIcon();
            Size = new Size(500, 410);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(18, 20, 28);
            ForeColor = Color.FromArgb(235, 240, 250);
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            var header = new Panel
            {
                Location = new Point(20, 18),
                Size = new Size(444, 86),
                BackColor = Color.FromArgb(26, 30, 42)
            };

            var picIcon = new PictureBox
            {
                Location = new Point(16, 15),
                Size = new Size(56, 56),
                SizeMode = PictureBoxSizeMode.StretchImage
            };
            try
            {
                picIcon.Image = Icon.ToBitmap();
            }
            catch { }

            var lblTitle = new Label
            {
                Text = "Síragon Battery Guard v2.0",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = Color.FromArgb(74, 222, 128),
                Location = new Point(84, 14),
                Size = new Size(340, 28)
            };

            var lblSub = new Label
            {
                Text = "Limitador de carga y control de energía para Síragon UB-8000\n(ARN25C-140M — AMD Ryzen 7 7840U)",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(84, 42),
                Size = new Size(350, 36)
            };

            header.Controls.Add(picIcon);
            header.Controls.Add(lblTitle);
            header.Controls.Add(lblSub);
            Controls.Add(header);

            var optionsPanel = new Panel
            {
                Location = new Point(20, 118),
                Size = new Size(444, 165),
                BackColor = Color.FromArgb(26, 30, 42)
            };

            var lblPath = new Label
            {
                Text = "Carpeta de instalación: " + _installDir,
                Font = new Font("Segoe UI", 8f, FontStyle.Regular),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(16, 12),
                Size = new Size(412, 20)
            };

            _chkDesktop = new CheckBox
            {
                Text = "Crear acceso directo en el Escritorio",
                Checked = true,
                Location = new Point(18, 40),
                Size = new Size(380, 24)
            };

            _chkStartMenu = new CheckBox
            {
                Text = "Agregar al Menú Inicio de Windows",
                Checked = true,
                Location = new Point(18, 68),
                Size = new Size(380, 24)
            };

            _chkAutoStart = new CheckBox
            {
                Text = "Iniciar automáticamente con Windows (en bandeja)",
                Checked = true,
                Location = new Point(18, 96),
                Size = new Size(380, 24)
            };

            _chkLaunchNow = new CheckBox
            {
                Text = "Abrir Síragon Battery Guard al finalizar la instalación",
                Checked = true,
                Location = new Point(18, 124),
                Size = new Size(380, 24)
            };

            optionsPanel.Controls.Add(lblPath);
            optionsPanel.Controls.Add(_chkDesktop);
            optionsPanel.Controls.Add(_chkStartMenu);
            optionsPanel.Controls.Add(_chkAutoStart);
            optionsPanel.Controls.Add(_chkLaunchNow);
            Controls.Add(optionsPanel);

            var progressTrack = new Panel
            {
                Location = new Point(20, 296),
                Size = new Size(444, 10),
                BackColor = Color.FromArgb(14, 16, 22)
            };

            _progressBarFill = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(0, 10),
                BackColor = Color.FromArgb(34, 197, 94)
            };
            progressTrack.Controls.Add(_progressBarFill);
            Controls.Add(progressTrack);

            _lblStatus = new Label
            {
                Text = "Listo para instalar.",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Italic),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(20, 312),
                Size = new Size(280, 24)
            };
            Controls.Add(_lblStatus);

            _btnInstall = new Button
            {
                Text = "Instalar Ahora",
                Location = new Point(314, 318),
                Size = new Size(150, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(22, 163, 74),
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnInstall.FlatAppearance.BorderSize = 0;
            _btnInstall.Click += (s, e) => RunInstall();
            Controls.Add(_btnInstall);
        }

        private void SetProgress(int percent, string status)
        {
            _progressBarFill.Width = (int)Math.Round((percent / 100.0) * 444);
            _lblStatus.Text = status;
            Application.DoEvents();
        }

        private static void ExtractResource(string resourceName, string outPath)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (s == null) throw new Exception("Recurso no encontrado: " + resourceName);
                using (FileStream fs = File.Create(outPath))
                {
                    s.CopyTo(fs);
                }
            }
        }

        private static void CreateShortcut(string lnkPath, string targetPath, string workDir, string iconPath, string description)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(shellType);
                object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                Type scType = shortcut.GetType();
                scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
                scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workDir });
                scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { iconPath + ",0" });
                scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
                scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            catch { }
        }

        private void RunInstall()
        {
            try
            {
                _btnInstall.Enabled = false;
                SetProgress(15, "Cerrando instancias previas...");

                foreach (Process p in Process.GetProcessesByName("SiragonBatteryGuard"))
                {
                    try { p.Kill(); p.WaitForExit(1500); } catch { }
                }

                SetProgress(35, "Creando directorio y extrayendo archivos...");
                if (!Directory.Exists(_installDir))
                {
                    Directory.CreateDirectory(_installDir);
                }

                string exePath = Path.Combine(_installDir, "SiragonBatteryGuard.exe");
                string icoPath = Path.Combine(_installDir, "battery.ico");
                string uninstPath = Path.Combine(_installDir, "Uninstall.exe");

                ExtractResource("AppExe", exePath);
                ExtractResource("AppIcon", icoPath);
                File.Copy(Application.ExecutablePath, uninstPath, true);

                SetProgress(65, "Configurando accesos directos...");
                if (_chkDesktop.Checked)
                {
                    string deskLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Síragon Battery Guard.lnk");
                    CreateShortcut(deskLnk, exePath, _installDir, icoPath, "Limitador de carga y control de energía para Síragon UB-8000");
                }

                if (_chkStartMenu.Checked)
                {
                    string startMenuLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Síragon Battery Guard.lnk");
                    CreateShortcut(startMenuLnk, exePath, _installDir, icoPath, "Limitador de carga y control de energía para Síragon UB-8000");
                }

                SetProgress(85, "Registrando en Windows...");
                using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (runKey != null)
                    {
                        if (_chkAutoStart.Checked)
                            runKey.SetValue("SiragonBatteryGuard", "\"" + exePath + "\" --tray");
                        else
                            runKey.DeleteValue("SiragonBatteryGuard", false);
                    }
                }

                using (RegistryKey uninstKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SiragonBatteryGuard"))
                {
                    if (uninstKey != null)
                    {
                        uninstKey.SetValue("DisplayName", "Síragon Battery Guard (UB-8000)");
                        uninstKey.SetValue("DisplayVersion", "2.0.0");
                        uninstKey.SetValue("Publisher", "Síragon Community Tools");
                        uninstKey.SetValue("DisplayIcon", icoPath);
                        uninstKey.SetValue("InstallLocation", _installDir);
                        uninstKey.SetValue("UninstallString", "\"" + uninstPath + "\" --uninstall");
                        uninstKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
                        uninstKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    }
                }

                SetProgress(100, "¡Instalación completada con éxito!");
                _lblStatus.ForeColor = Color.FromArgb(74, 222, 128);

                if (_chkLaunchNow.Checked)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exePath,
                        WorkingDirectory = _installDir,
                        UseShellExecute = true
                    });
                }

                MessageBox.Show(
                    "Síragon Battery Guard v2.0 se instaló correctamente en tu equipo.",
                    "Instalación Exitosa",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                Close();
            }
            catch (Exception ex)
            {
                _btnInstall.Enabled = true;
                _lblStatus.ForeColor = Color.FromArgb(248, 113, 113);
                _lblStatus.Text = "Error: " + ex.Message;
                MessageBox.Show("Error durante la instalación:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public static void RunUninstall()
        {
            DialogResult dr = MessageBox.Show(
                "¿Deseas desinstalar Síragon Battery Guard de tu equipo?",
                "Desinstalar Síragon Battery Guard",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );
            if (dr != DialogResult.Yes) return;

            foreach (Process p in Process.GetProcessesByName("SiragonBatteryGuard"))
            {
                try { p.Kill(); p.WaitForExit(1500); } catch { }
            }

            try
            {
                string deskLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Síragon Battery Guard.lnk");
                if (File.Exists(deskLnk)) File.Delete(deskLnk);

                string startLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Síragon Battery Guard.lnk");
                if (File.Exists(startLnk)) File.Delete(startLnk);

                using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (runKey != null) runKey.DeleteValue("SiragonBatteryGuard", false);
                }

                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SiragonBatteryGuard", false);
            }
            catch { }

            MessageBox.Show(
                "Síragon Battery Guard ha sido desinstalado correctamente.",
                "Desinstalación Completa",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args != null && args.Length > 0 && args[0] == "--uninstall")
            {
                InstallerForm.RunUninstall();
                return;
            }
            Application.Run(new InstallerForm());
        }
    }
}
