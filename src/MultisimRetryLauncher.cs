using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Security.Principal;
using Microsoft.Win32;

// Built with the .NET Framework compiler supplied with Windows.
// Only processes created by this launcher are eligible for termination.
internal static class MultisimRetryLauncher
{
    private static volatile bool cancelled;
    private static StreamWriter log;

    private sealed class Options
    {
        public string Exe = @"C:\Program Files (x86)\National Instruments\Circuit Design Suite 14.3\multisim.exe";
        public int Attempts = 10;
        public int TimeoutSeconds = 75;
        public int StableSeconds = 8;
        public int DelayMilliseconds = 2000;
    }

    private sealed class WindowInfo
    {
        public IntPtr Handle;
        public IntPtr Owner;
        public string Title;
        public string ClassName;
        public bool Enabled;
        public bool HasButton;
        public string Text;
        public bool IsDialog { get { return ClassName == "#32770"; } }
        public bool IsSplash
        {
            get
            {
                return !IsMain && !HasButton && String.Equals(Title.Trim(), "Multisim", StringComparison.OrdinalIgnoreCase)
                    && Regex.IsMatch(Text, @"(?:version|版本)\s*\d+\.\d+", RegexOptions.IgnoreCase)
                    && ((Text.IndexOf("National Instruments", StringComparison.OrdinalIgnoreCase) >= 0
                        && Regex.IsMatch(Text, @"©|copyright|版权所有", RegexOptions.IgnoreCase))
                        || Regex.IsMatch(Text, @"(?:正在初始化|initializing|initialising)\s*[:：]", RegexOptions.IgnoreCase));
            }
        }
        public bool BlocksStartup
        {
            get { return !IsMain && !IsSplash && (IsDialog || (Owner != IntPtr.Zero && !String.IsNullOrWhiteSpace(Title))); }
        }
        public bool IsMain
        {
            get
            {
                return !IsDialog && Regex.IsMatch(Title,
                    @"^.+\s-\sMultisim(?:\s|$)|^Multisim\s*-\s*.+$", RegexOptions.IgnoreCase);
            }
        }
    }

    private enum Result { Ready, DatabaseError, Exited, Timeout, OtherDialog, Cancelled }

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            Options options = Parse(args);
            if (options == null) return 0;
            options.Exe = Path.GetFullPath(options.Exe);
            if (!File.Exists(options.Exe)) throw new FileNotFoundException("找不到 Multisim 程序。", options.Exe);
            if (RequiresAdministrator(options.Exe) && !IsAdministrator())
            {
                Console.WriteLine("你的 Multisim 已被设置为以管理员身份运行。启动器需要相同权限才能处理本轮失败实例。");
                Console.WriteLine("接下来会出现 Windows 权限提示；取消将停止，不更改任何设置。");
                ProcessStartInfo elevated = new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location);
                List<string> quoted = new List<string>();
                foreach (string arg in args) quoted.Add(QuoteArgument(arg));
                elevated.Arguments = String.Join(" ", quoted.ToArray());
                elevated.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                elevated.UseShellExecute = true;
                elevated.Verb = "runas";
                try
                {
                    using (Process child = Process.Start(elevated))
                    {
                        if (child == null) return 1;
                        child.WaitForExit();
                        return child.ExitCode;
                    }
                }
                catch (System.ComponentModel.Win32Exception error)
                {
                    if (error.NativeErrorCode == 1223) { Console.WriteLine("已取消 Windows 权限请求，停止启动。"); return 5; }
                    throw;
                }
            }
            OpenLog();
            Write("Multisim 自动重试启动器；最多 " + options.Attempts + " 次。按 Ctrl+C 可停止重试。");
            Write("程序：" + options.Exe);
            Write("仅检查启动阶段的数据库报错与主界面状态，不代表所有仿真功能均已验证。");
            Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                cancelled = true;
            };

            bool acquired = false;
            string identity = Convert.ToBase64String(Encoding.UTF8.GetBytes(options.Exe.ToLowerInvariant()))
                .Replace('/', '_').Replace('\\', '_');
            using (Mutex mutex = new Mutex(false, @"Local\MultisimRetry_" + identity))
            {
                try
                {
                    try { acquired = mutex.WaitOne(0); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) { Write("另一个重试启动器正在运行，本次不再启动。"); return 11; }
                    return Run(options);
                }
                finally { if (acquired) mutex.ReleaseMutex(); }
            }
        }
        catch (Exception error)
        {
            Write("停止：" + error.Message);
            return 1;
        }
        finally { if (log != null) log.Dispose(); }
    }

    private static int Run(Options options)
    {
        for (int attempt = 1; attempt <= options.Attempts; attempt++)
        {
            if (cancelled) return 130;
            // Conservative: even an inaccessible same-name process blocks a new launch.
            Process[] existing = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(options.Exe));
            try
            {
                if (existing.Length != 0)
                {
                    Write("已发现正在运行的 Multisim/同名进程。为保护现有电路，本次不启动，也不关闭它。");
                    return 10;
                }
            }
            finally { foreach (Process process in existing) process.Dispose(); }

            Write("第 " + attempt + "/" + options.Attempts + " 次启动……");
            ProcessStartInfo start = new ProcessStartInfo(options.Exe);
            start.WorkingDirectory = Path.GetDirectoryName(options.Exe);
            // ShellExecute keeps the GUI app from inheriting our console output pipe.
            start.UseShellExecute = true;
            using (Process process = Process.Start(start))
            {
                // Keep the original Process handle; never attach to a replacement PID.
                if (process == null) throw new InvalidOperationException("未取得启动进程。");
                int pid = process.Id;
                DateTime started = process.StartTime;
                Write("本次实例 PID=" + pid);
                string detail;
                Result result = Observe(process, options, out detail);
                Write("检测结果：" + result + (String.IsNullOrEmpty(detail) ? "" : "；" + detail));

                if (result == Result.Ready)
                {
                    Write("主界面已连续稳定 " + options.StableSeconds + " 秒，未检测到启动数据库错误。保留此实例，停止重试。");
                    BringToFront(process);
                    return 0;
                }
                if (result == Result.Cancelled)
                {
                    Write("已停止重试；当前实例保留，供你检查。");
                    return 130;
                }
                if (result == Result.OtherDialog)
                {
                    Write("遇到其他提示，无法自动判定。保留窗口并停止，不点击、不重启它。");
                    BringToFront(process);
                    return 12;
                }
                if (result == Result.DatabaseError || result == Result.Timeout)
                {
                    // Each attempt launches without document arguments. No existing instance is killed.
                    // Recheck ownership and blockers immediately before terminating a timed-out instance.
                    if (result == Result.Timeout && HasUnclassifiedDialog(process))
                    {
                        Write("超时后发现未识别提示。保留窗口并停止。");
                        return 12;
                    }
                    if (!StopOwnAttempt(process, pid, started)) return 13;
                }
                if (attempt < options.Attempts)
                {
                    Write("等待文件锁释放后重试……");
                    if (!Pause(options.DelayMilliseconds)) return 130;
                }
            }
        }
        Write("已达到重试上限，仍未得到正常启动状态。不会继续无限启动；请查看本次日志。");
        return 2;
    }

    private static Result Observe(Process process, Options options, out string detail)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        long stableSince = -1;
        long unknownSince = -1;
        detail = "";
        while (elapsed.Elapsed.TotalSeconds < options.TimeoutSeconds)
        {
            if (cancelled) return Result.Cancelled;
            if (process.HasExited) { detail = "程序已退出，退出码 " + process.ExitCode; return Result.Exited; }
            List<WindowInfo> windows = ReadWindows(process.Id);
            foreach (WindowInfo window in windows)
            {
                if (IsDatabaseError(window.Text))
                {
                    detail = Compact(window.Text);
                    return Result.DatabaseError;
                }
            }
            WindowInfo unknown = windows.Find(delegate(WindowInfo w) { return w.BlocksStartup; });
            if (unknown != null)
            {
                // Allow controls/text to populate before deciding that a popup is unknown.
                if (unknownSince < 0) unknownSince = elapsed.ElapsedMilliseconds;
                if (elapsed.ElapsedMilliseconds - unknownSince >= 1200)
                {
                    detail = Compact(unknown.Text);
                    return Result.OtherDialog;
                }
            }
            else { unknownSince = -1; }
            WindowInfo main = windows.Find(delegate(WindowInfo w) { return w.IsMain && w.Enabled; });
            bool blocked = unknown != null || windows.Exists(delegate(WindowInfo w) { return w.IsSplash; });
            if (main != null && !blocked)
            {
                if (stableSince < 0) stableSince = elapsed.ElapsedMilliseconds;
                if (elapsed.ElapsedMilliseconds - stableSince >= options.StableSeconds * 1000L)
                {
                    detail = main.Title;
                    return Result.Ready;
                }
            }
            else { stableSince = -1; }
            if (!Pause(350)) return Result.Cancelled;
        }
        detail = "等待启动超过 " + options.TimeoutSeconds + " 秒";
        return Result.Timeout;
    }

    private static bool IsDatabaseError(string text)
    {
        return Regex.IsMatch(text ?? "",
            @"problem\s*(?::|with)?\s*(?:accessing|opening)\s+(?:the\s+)?database"
            + @"|(?:master|user|corporate)\s+database\s+(?:cannot|can\s*not)\s+be\s+accessed"
            + @"|unable\s+to\s+initialize\s+(?:DAO|Jet)"
            + @"|(?:无法|不能|未能|无法正常)[^\r\n。]{0,45}(?:访问|打开|连接|初始化)[^\r\n。]{0,30}(?:数据库|DAO|Jet)"
            + @"|(?:数据库|DAO|Jet)[^\r\n。]{0,60}(?:无法访问|不能访问|不能被访问|无法被访问|访问失败|打开失败|连接失败|初始化失败)"
            + @"|访问数据库[^\r\n。]{0,20}(?:问题|失败|错误)",
            RegexOptions.IgnoreCase);
    }

    private static bool HasUnclassifiedDialog(Process process)
    {
        if (process.HasExited) return false;
        return ReadWindows(process.Id).Exists(delegate(WindowInfo w)
        {
            return w.BlocksStartup && !IsDatabaseError(w.Text);
        });
    }

    private static bool StopOwnAttempt(Process process, int pid, DateTime started)
    {
        try
        {
            if (process.HasExited) return true;
            if (process.Id != pid || process.StartTime != started)
            {
                Write("实例标识已变化，停止自动操作。");
                return false;
            }
            Write("关闭本轮新启动的失败实例 PID=" + pid + "。");
            process.Kill();
            if (!process.WaitForExit(10000))
            {
                Write("实例尚未退出。为避免并发占用数据库，停止重试。");
                return false;
            }
            return true;
        }
        catch (InvalidOperationException) { return true; }
        catch (Exception error) { Write("无法关闭本轮实例：" + error.Message); return false; }
    }

    private static void BringToFront(Process process)
    {
        try
        {
            if (process.HasExited) return;
            WindowInfo main = ReadWindows(process.Id).Find(delegate(WindowInfo w) { return w.IsMain; });
            if (main != null) { ShowWindow(main.Handle, 9); SetForegroundWindow(main.Handle); }
        }
        catch { /* Bringing the window forward is optional. */ }
    }

    private static bool Pause(int milliseconds)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < milliseconds)
        {
            if (cancelled) return false;
            Thread.Sleep(100);
        }
        return !cancelled;
    }

    private static bool IsAdministrator()
    {
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool RequiresAdministrator(string executable)
    {
        const string key = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
        foreach (RegistryKey hive in new RegistryKey[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using (RegistryKey layers = hive.OpenSubKey(key))
            {
                string flags = layers == null ? "" : Convert.ToString(layers.GetValue(executable, ""));
                if (flags.IndexOf("RUNASADMIN", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
        }
        return false;
    }

    private static string QuoteArgument(string argument)
    {
        // CommandLineToArgvW quoting, including trailing backslashes in paths.
        StringBuilder result = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char character in argument)
        {
            if (character == '\\') { backslashes++; continue; }
            if (character == '"') result.Append('\\', backslashes * 2 + 1).Append('"');
            else result.Append('\\', backslashes).Append(character);
            backslashes = 0;
        }
        return result.Append('\\', backslashes * 2).Append('"').ToString();
    }

    private static Options Parse(string[] args)
    {
        Options options = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            if (key == "--help")
            {
                Console.WriteLine("MultisimRetryLauncher.exe [--exe path] [--attempts 10] [--timeout 75] [--stable 8] [--delay-ms 2000]");
                return null;
            }
            if (i + 1 >= args.Length) throw new ArgumentException("参数缺少值：" + key);
            string value = args[++i];
            switch (key)
            {
                case "--exe": options.Exe = value; break;
                case "--attempts": options.Attempts = Number(value, 1, 30, key); break;
                case "--timeout": options.TimeoutSeconds = Number(value, 3, 180, key); break;
                case "--stable": options.StableSeconds = Number(value, 1, 30, key); break;
                case "--delay-ms": options.DelayMilliseconds = Number(value, 300, 15000, key); break;
                default: throw new ArgumentException("未知参数：" + key);
            }
        }
        if (options.TimeoutSeconds <= options.StableSeconds) throw new ArgumentException("启动超时必须大于稳定观察时间。");
        return options;
    }

    private static int Number(string value, int min, int max, string key)
    {
        int number;
        if (!Int32.TryParse(value, out number) || number < min || number > max)
            throw new ArgumentException(key + " 必须在 " + min + " 到 " + max + " 之间。");
        return number;
    }

    private static void OpenLog()
    {
        try
        {
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "launcher-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")
                + "-" + Process.GetCurrentProcess().Id + ".log");
            log = new StreamWriter(path, false, new UTF8Encoding(false));
            log.AutoFlush = true;
            Console.WriteLine("日志：" + path);
        }
        catch (Exception error) { Console.WriteLine("无法写入日志：" + error.Message); }
    }

    private static void Write(string message)
    {
        string line = DateTime.Now.ToString("HH:mm:ss") + "  " + message;
        Console.WriteLine(line);
        if (log != null) log.WriteLine(line);
    }

    private static string Compact(string text)
    {
        string value = Regex.Replace(text ?? "", @"\s+", " ").Trim();
        return value.Length <= 1200 ? value : value.Substring(0, 1200);
    }

    private static List<WindowInfo> ReadWindows(int pid)
    {
        List<WindowInfo> windows = new List<WindowInfo>();
        EnumWindows(delegate(IntPtr window, IntPtr unused)
        {
            uint ownerPid;
            GetWindowThreadProcessId(window, out ownerPid);
            if (ownerPid != (uint)pid || !IsWindowVisible(window)) return true;
            WindowInfo info = new WindowInfo();
            info.Handle = window;
            info.Owner = GetWindow(window, 4);
            info.Title = Title(window);
            StringBuilder className = new StringBuilder(256);
            GetClassName(window, className, className.Capacity);
            info.ClassName = className.ToString();
            info.Enabled = IsWindowEnabled(window);
            StringBuilder contents = new StringBuilder(info.Title);
            // Only startup popups need child text. Avoid walking the editor's large control tree.
            if (!info.IsMain)
            {
                int count = 0;
                EnumChildWindows(window, delegate(IntPtr child, IntPtr childUnused)
                {
                    if (++count > 100) return false;
                    if (!IsWindowVisible(child)) return true;
                    StringBuilder childClass = new StringBuilder(128);
                    GetClassName(child, childClass, childClass.Capacity);
                    if (String.Equals(childClass.ToString(), "Button", StringComparison.OrdinalIgnoreCase)) info.HasButton = true;
                    string text = ReadControlText(child);
                    if (!String.IsNullOrWhiteSpace(text)) contents.AppendLine().Append(text);
                    return true;
                }, IntPtr.Zero);
            }
            info.Text = contents.ToString();
            windows.Add(info);
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private static string Title(IntPtr window)
    {
        StringBuilder buffer = new StringBuilder(2048);
        GetWindowText(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string ReadControlText(IntPtr window)
    {
        StringBuilder buffer = new StringBuilder(8192);
        IntPtr result;
        SendMessageTimeout(window, 0x000D, new IntPtr(buffer.Capacity), buffer, 0x0002, 120, out result);
        return buffer.ToString();
    }

    private delegate bool EnumProc(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr window, EnumProc callback, IntPtr state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, StringBuilder lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
}
