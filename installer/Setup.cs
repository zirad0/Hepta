using System;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

internal class Setup : Form
{
    readonly ComboBox language = new ComboBox
    {
        DropDownStyle = ComboBoxStyle.DropDownList
    };
    readonly TextBox folder = new TextBox
    {
        Width = 330,
        Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeptaApp")
    };
    readonly CheckBox desktop = new CheckBox
    {
        AutoSize = true
    }, launch = new CheckBox
    {
        AutoSize = true,
        Checked = true
    };
    readonly Label location = new Label
    {
        AutoSize = true
    };
    readonly Button install = new Button
    {
        AutoSize = true
    };
    bool english, busy;
    string installed;
    Setup()
    {
        installed = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Zirad\Hepta", "InstallDir", null) as string;
        if (!string.IsNullOrWhiteSpace(installed) && File.Exists(Path.Combine(installed, "Hepta.exe")))
        {
            folder.Text = installed.TrimEnd(Path.DirectorySeparatorChar);
            folder.ReadOnly = true;
        }

        Text = "Hepta · 1.0.0";
        ClientSize = new Size(490, 339);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(23),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.FromArgb(37, 37, 40),
            ForeColor = Color.WhiteSmoke
        };
        Controls.Add(panel);
        panel.Controls.Add(new Label { Text = "Hepta   1.0.0", Font = new Font("Segoe UI", 22), ForeColor = Color.Wheat, AutoSize = true });
        language.Items.AddRange(new[] { "Türkçe", "English" });
        language.SelectedIndex = 0;
        panel.Controls.Add(language);
        panel.Controls.Add(location);
        var line = new FlowLayoutPanel
        {
            AutoSize = true
        };
        line.Controls.Add(folder);
        var browse = new Button
        {
            Text = "…",
            Width = 36
        };
        line.Controls.Add(browse);
        panel.Controls.Add(line);
        browse.Enabled = !folder.ReadOnly;
        browse.Click += (s, e) =>
        {
            using (var d = new FolderBrowserDialog())
            {
                if (d.ShowDialog() == DialogResult.OK)
                    folder.Text = d.SelectedPath;
            }
        };
        panel.Controls.Add(desktop);
        panel.Controls.Add(launch);
        panel.Controls.Add(install);
        install.BackColor = Color.Wheat;
        install.ForeColor = Color.Black;
        language.SelectedIndexChanged += (s, e) => UpdateLabels();
        UpdateLabels();
        install.Click += Install;
        FormClosing += (s, e) =>
        {
            if (busy)
                e.Cancel = true;
        };
    }

    void UpdateLabels()
    {
        english = language.SelectedIndex == 1;
        desktop.Text = english ? "Create desktop shortcut" : "Masaüstü kısayolu oluştur";
        launch.Text = english ? "Open Hepta after installation" : "Kurulumdan sonra Hepta’yı aç";
        location.Text = english ? "Destination folder" : "Hedef klasör";
        install.Text = english ? "Install" : "Kur";
    }

    async void Install(object sender, EventArgs args)
    {
        string temp = Path.Combine(Path.GetTempPath(), "Hepta-" + Guid.NewGuid().ToString("N"));
        try
        {
            string target = Path.GetFullPath(folder.Text);
            if (target.Contains("\"") || target.EndsWith("\\") || target.StartsWith(@"\\"))
                throw new ArgumentException("Destination");
            bool repair = folder.ReadOnly && string.Equals(target.TrimEnd(Path.DirectorySeparatorChar), installed.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
            if (!repair && Directory.Exists(target) && Directory.GetFileSystemEntries(target).Length != 0)
                throw new IOException(english ? "Select an empty destination folder." : "Boş bir hedef klasör seçin.");
            if (repair)
            {
                foreach (var process in Process.GetProcessesByName("Hepta"))
                    using (process)
                    {
                        try
                        {
                            if (string.Equals(process.MainModule.FileName, Path.Combine(target, "Hepta.exe"), StringComparison.OrdinalIgnoreCase))
                                throw new IOException(english ? "Finish active jobs and exit Hepta from the tray before updating." : "Güncellemeden önce işleri bitirin ve Hepta uygulamasını tepsiden kapatın.");
                        }
                        catch (System.ComponentModel.Win32Exception)
                        {
                        }
                    }
            }

            busy = true;
            Controls[0].Enabled = false;
            Directory.CreateDirectory(temp);
            string msi = Path.Combine(temp, "Hepta.msi");
            using (var src = Assembly.GetExecutingAssembly().GetManifestResourceStream("Hepta.msi"))
            using (var dest = File.Create(msi))
                src.CopyTo(dest);
            var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), "/i \"" + msi + "\" /qb /norestart INSTALLDIR=\"" + target + "\" DESKTOPLINK=" + (desktop.Checked ? "1" : "0") + (repair ? " REINSTALL=ALL REINSTALLMODE=vomus" : ""))
            {
                UseShellExecute = false
            };
            int code = await Task.Run(() =>
            {
                using (var process = Process.Start(info))
                {
                    process.WaitForExit();
                    return process.ExitCode;
                }
            });
            if (code != 0 && code != 3010)
                throw new Exception("Windows Installer · " + code);
            if (launch.Checked)
                Process.Start(Path.Combine(target, "Hepta.exe"));
            busy = false;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Hepta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            busy = false;
            Controls[0].Enabled = true;
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.Run(new Setup());
    }
}
