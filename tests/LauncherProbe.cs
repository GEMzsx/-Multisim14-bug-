using System;
using System.IO;
using System.Windows.Forms;

internal static class LauncherProbe
{
    [STAThread]
    private static void Main()
    {
        string counter = Environment.GetEnvironmentVariable("MULTISIM_PROBE_COUNTER");
        string mode = Environment.GetEnvironmentVariable("MULTISIM_PROBE_MODE") ?? "ready";
        int attempt = File.Exists(counter) ? Int32.Parse(File.ReadAllText(counter)) + 1 : 1;
        File.WriteAllText(counter, attempt.ToString());
        if (mode == "timeout") { System.Threading.Thread.Sleep(60000); return; }
        Form main = new Form();
        main.Text = "Design1 - Multisim - [Launcher verification]";
        main.Width = 640;
        main.Height = 300;
        Label note = new Label();
        note.Text = "Launcher verification only; this is not NI Multisim.";
        note.AutoSize = true;
        main.Controls.Add(note);
        main.Shown += delegate
        {
            if (mode == "splash-ready" || mode == "splash-fail" || mode == "init-ready" || mode == "init-fail")
            {
                using (Form splash = new Form())
                {
                    splash.Text = "Multisim";
                    splash.ControlBox = false;
                    Label version = new Label();
                    version.AutoSize = true;
                    version.Text = mode.StartsWith("init-")
                        ? "Multisim 版本 14.3.0 正在初始化：commands (69%) ..."
                        : "Multisim Power Pro Edition 版本 14.3.0 ©2012 National Instruments 版权所有。";
                    splash.Controls.Add(version);
                    System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
                    timer.Interval = 1800;
                    timer.Tick += delegate { timer.Stop(); splash.Close(); };
                    timer.Start();
                    splash.ShowDialog(main);
                    timer.Dispose();
                }
            }
            if (mode == "always-fail" || mode == "splash-fail" || mode == "init-fail" || (mode == "fail-twice" && attempt <= 2))
                MessageBox.Show(main, "Problem accessing the database:\r\nThe User Database cannot be accessed. Features using the User Database will not be available.", "Multisim", MessageBoxButtons.OK, MessageBoxIcon.Error);
            if (mode == "chinese-fail")
                MessageBox.Show(main, "访问数据库时出现问题：用户数据库无法访问。", "Multisim", MessageBoxButtons.OK, MessageBoxIcon.Error);
            if (mode == "unknown")
                MessageBox.Show(main, "Activation is required. This is a verification dialog.", "Multisim", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        Application.Run(main);
    }
}
