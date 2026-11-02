using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("流氓软件克星")]
[assembly: AssemblyDescription("扫描和清理 Windows 流氓右键菜单、自启动、计划任务、服务、浏览器插件和文件关联残留")]
[assembly: AssemblyCompany("aakk007")]
[assembly: AssemblyProduct("流氓软件克星")]
[assembly: AssemblyCopyright("Copyright (c) 2026 aakk007")]
[assembly: AssemblyVersion("2.0.16.0")]
[assembly: AssemblyFileVersion("2.0.16.0")]

namespace RogueCleanerV2
{
    internal static class AppMeta
    {
        public const string ProductName = "流氓软件克星";
        public const string Version = "2.0.16";
        public const string AuthorName = "aakk007";
        public const string Author52PojieUrl = "https://www.52pojie.cn/home.php?mod=space&uid=286924";
        public const string AuthorGitHubUrl = "https://github.com/aakk007";
        public const string Repository = "https://github.com/aakk007/RogueCleaner";
        public const string ReleasesUrl = "https://github.com/aakk007/RogueCleaner/releases";
        public const string LatestApiUrl = "https://api.github.com/repos/aakk007/RogueCleaner/releases/latest";
        public const string DataDirName = "流氓软件克星数据";
        public const string DotNetDownloadUrl = "https://dotnet.microsoft.com/download/dotnet-framework";
    }

    internal static class Program
    {
        private const string SingleInstanceMutexName = @"Local\RogueCleanerV2.SingleInstance.4F7A1E02-3C0E-4F75-9C15-9E7A7E1D8A26";
        private const int SW_RESTORE = 9;
        private static Mutex singleInstanceMutex;
        private static bool singleInstanceLockOwned;

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls;

            int copyPathIndex = Array.FindIndex(args, delegate(string arg) { return string.Equals(arg, "--copy-path", StringComparison.OrdinalIgnoreCase); });
            if (copyPathIndex >= 0)
            {
                if (copyPathIndex + 1 >= args.Length || string.IsNullOrWhiteSpace(args[copyPathIndex + 1])) return 8;
                try { Clipboard.SetText(args[copyPathIndex + 1]); return 0; }
                catch (Exception ex) { MessageBox.Show("复制路径失败：" + ex.Message, AppMeta.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error); return 8; }
            }

            int titleProbeIndex = Array.FindIndex(args, delegate(string arg) { return string.Equals(arg, "--context-title-probe", StringComparison.OrdinalIgnoreCase); });
            if (titleProbeIndex >= 0)
            {
                if (titleProbeIndex + 4 >= args.Length) return 10;
                return ContextCommandTitleProbe.RunChild(args[titleProbeIndex + 1], args[titleProbeIndex + 2], args[titleProbeIndex + 3], args[titleProbeIndex + 4]);
            }

            DataStore store = DataStore.CreateForExecutable(Application.ExecutablePath);
            store.Ensure();
            Logger.Initialize(store);
            if (CliMode.IsCliArg(args))
            {
                Environment.ExitCode = CliMode.Run(args, store);
                return Environment.ExitCode;
            }
            bool smoke = HasArg(args, "--scan-smoke");
            bool vendorReviewSmoke = HasArg(args, "--vendor-review-smoke");
            if (vendorReviewSmoke)
            {
                string review = VendorReviewWriter.Write(store, Application.ExecutablePath);
                Environment.ExitCode = File.Exists(review) ? 0 : 11;
                return Environment.ExitCode;
            }
            bool identitySmoke = HasArg(args, "--identity-smoke");
            bool feedbackSmoke = HasArg(args, "--feedback-smoke");
            bool uiSmoke = HasArg(args, "--ui-smoke");
            bool interactiveGuiLaunch = IsInteractiveGuiLaunch(args);
#if VALIDATION
            bool acceptance = HasArg(args, "--acceptance-test");
            bool permissionSmoke = HasArg(args, "--permission-smoke");
            bool contextMenuSmoke = HasArg(args, "--context-menu-smoke");
            bool specialMenuSmoke = HasArg(args, "--special-menu-smoke");
            bool advancedMenuSmoke = HasArg(args, "--advanced-menu-smoke");
#endif

            if (interactiveGuiLaunch && !TryEnterSingleInstance(HasArg(args, "--elevation-resume")))
            {
                Environment.ExitCode = 0;
                return 0;
            }

            try
            {
#if VALIDATION
                if (acceptance)
                {
                    int exitCode = ValidationRunner.Run(store);
                    Environment.ExitCode = exitCode;
                    return exitCode;
                }
                if (permissionSmoke)
                {
                    List<string> failures = PermissionRegression.Run();
                    CleanerEngine.WriteJson(Path.Combine(store.Reports, "permission-smoke-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), failures);
                    Environment.ExitCode = failures.Count == 0 ? 0 : 5;
                    return Environment.ExitCode;
                }
                if (contextMenuSmoke)
                {
                    List<string> failures = ContextMenuManagementRegression.Run(store);
                    CleanerEngine.WriteJson(Path.Combine(store.Reports, "context-menu-smoke-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), failures);
                    Environment.ExitCode = failures.Count == 0 ? 0 : 6;
                    return Environment.ExitCode;
                }
                if (specialMenuSmoke)
                {
                    List<string> failures = SpecialContextMenuRegression.Run(store);
                    CleanerEngine.WriteJson(Path.Combine(store.Reports, "special-menu-smoke-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), failures);
                    Environment.ExitCode = failures.Count == 0 ? 0 : 7;
                    return Environment.ExitCode;
                }
                if (advancedMenuSmoke)
                {
                    List<string> failures = AdvancedContextMenuRegression.Run(store);
                    CleanerEngine.WriteJson(Path.Combine(store.Reports, "advanced-menu-smoke-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), failures);
                    Environment.ExitCode = failures.Count == 0 ? 0 : 9;
                    return Environment.ExitCode;
                }
#endif
                if (identitySmoke)
                {
                    List<string> failures = RuleCatalog.RunIdentitySelfTests();
                    failures.AddRange(ElevationResumeState.RunSelfTests());
                    failures.AddRange(ScannerEngine.RunContextMenuNameSelfTests());
                    failures.AddRange(SoftwarePresentationRegression.Run());
                    failures.AddRange(ContextCommandTitleProbe.RunSelfTests());
                    CleanerEngine.WriteJson(Path.Combine(store.Reports, "identity-smoke-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), failures);
                    Environment.ExitCode = failures.Count == 0 ? 0 : 2;
                    return Environment.ExitCode;
                }
                if (feedbackSmoke)
                {
                    List<string> failures = FeedbackService.RunSelfTests(store);
                    CleanerEngine.WriteJson(Path.Combine(store.Reports, "feedback-smoke-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), failures);
                    Environment.ExitCode = failures.Count == 0 ? 0 : 3;
                    return Environment.ExitCode;
                }
                if (uiSmoke)
                {
                    List<string> failures = UiRegression.Run(store);
                    CleanerEngine.WriteJson(Path.Combine(store.Reports, "ui-smoke-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), failures);
                    Environment.ExitCode = failures.Count == 0 ? 0 : 4;
                    return Environment.ExitCode;
                }
                if (smoke)
                {
                    ScannerEngine scanner = new ScannerEngine();
                    List<Finding> findings = scanner.ScanAll(null);
                    List<ScanWarning> scanWarnings = scanner.Warnings;
                    CleanerEngine.WriteJson(Path.Combine(store.Reports, "scan-smoke-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), new ScanEvidenceReport
                    {
                        ScannedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        ProductVersion = AppMeta.Version,
                        FindingCount = findings.Count,
                        WarningCount = scanWarnings.Count,
                        Findings = findings,
                        Warnings = scanWarnings
                    });
                    Environment.ExitCode = 0;
                    return 0;
                }
                ElevationResumeState elevationResume = ElevationResumeState.LoadFromArguments(args, store);
                Application.Run(new MainForm(store, true, null, elevationResume));
                Environment.ExitCode = 0;
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error("启动失败", ex);
                if (!smoke && !identitySmoke && !feedbackSmoke && !uiSmoke) MessageBox.Show("启动失败：" + ex.Message, AppMeta.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.ExitCode = 1;
                return 1;
            }
            finally
            {
                if (interactiveGuiLaunch) ReleaseSingleInstanceLock();
            }
        }

        private static bool HasArg(string[] args, string name)
        {
            if (args == null) return false;
            foreach (string arg in args)
            {
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool IsInteractiveGuiLaunch(string[] args)
        {
            string[] workerArgs = new string[]
            {
                "--copy-path",
                "--context-title-probe",
                "--scan-smoke",
                "--vendor-review-smoke",
                "--identity-smoke",
                "--feedback-smoke",
                "--ui-smoke",
                "--acceptance-test",
                "--permission-smoke",
                "--context-menu-smoke",
                "--special-menu-smoke",
                "--advanced-menu-smoke"
            };
            foreach (string workerArg in workerArgs)
            {
                if (HasArg(args, workerArg)) return false;
            }
            return true;
        }

        private static bool TryEnterSingleInstance(bool waitForRelaunch)
        {
            try
            {
                bool createdNew;
                singleInstanceMutex = new Mutex(false, SingleInstanceMutexName, out createdNew);
                TimeSpan wait = waitForRelaunch ? TimeSpan.FromSeconds(8) : TimeSpan.Zero;
                if (WaitForSingleInstanceLock(createdNew ? TimeSpan.Zero : wait))
                {
                    singleInstanceLockOwned = true;
                    return true;
                }
            }
            catch (UnauthorizedAccessException)
            {
                ActivateExistingMainWindow();
                return false;
            }
            catch
            {
                ReleaseSingleInstanceLock();
                return !ActivateExistingMainWindow();
            }

            ReleaseSingleInstanceLock();
            ActivateExistingMainWindow();
            return false;
        }

        private static bool WaitForSingleInstanceLock(TimeSpan wait)
        {
            try
            {
                return singleInstanceMutex != null && singleInstanceMutex.WaitOne(wait);
            }
            catch (AbandonedMutexException)
            {
                return true;
            }
        }

        internal static void ReleaseSingleInstanceLock()
        {
            try
            {
                if (singleInstanceMutex != null && singleInstanceLockOwned) singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
            finally
            {
                singleInstanceLockOwned = false;
                if (singleInstanceMutex != null)
                {
                    singleInstanceMutex.Dispose();
                    singleInstanceMutex = null;
                }
            }
        }

        private static bool ActivateExistingMainWindow()
        {
            string currentPath = string.Empty;
            try { currentPath = Path.GetFullPath(Application.ExecutablePath); } catch { }
            string processName = string.Empty;
            try { processName = Process.GetCurrentProcess().ProcessName; } catch { }
            if (string.IsNullOrWhiteSpace(processName)) return false;

            for (int attempt = 0; attempt < 20; attempt++)
            {
                Process[] processes = null;
                try
                {
                    processes = Process.GetProcessesByName(processName);
                    foreach (Process process in processes)
                    {
                        if (!IsExistingInteractiveInstance(process, currentPath)) continue;
                        IntPtr handle = process.MainWindowHandle;
                        if (handle == IntPtr.Zero) continue;
                        ShowWindowAsync(handle, SW_RESTORE);
                        SetForegroundWindow(handle);
                        return true;
                    }
                }
                catch
                {
                }
                finally
                {
                    if (processes != null)
                    {
                        foreach (Process process in processes)
                        {
                            try { process.Dispose(); } catch { }
                        }
                    }
                }
                Thread.Sleep(100);
            }
            return false;
        }

        private static bool IsExistingInteractiveInstance(Process process, string currentPath)
        {
            try
            {
                if (process == null || process.Id == Process.GetCurrentProcess().Id || process.HasExited) return false;
                string title = process.MainWindowTitle;
                if (!string.IsNullOrWhiteSpace(title) && title.IndexOf(AppMeta.ProductName, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                string otherPath = null;
                try { otherPath = Path.GetFullPath(process.MainModule.FileName); } catch { }
                return !string.IsNullOrWhiteSpace(currentPath) && !string.IsNullOrWhiteSpace(otherPath) &&
                    string.Equals(currentPath, otherPath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLinkComObject
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    internal interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    internal interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    internal sealed class DataStore
    {
        public string Root { get; private set; }
        public string Backups { get; private set; }
        public string Reports { get; private set; }
        public string Logs { get; private set; }
        public string Updates { get; private set; }
        public string Quarantine { get; private set; }
        public string State { get; private set; }
        public string Feedbacks { get; private set; }

        public static DataStore CreateForExecutable(string exePath)
        {
            string exeDir = Path.GetDirectoryName(Path.GetFullPath(exePath));
            string root = Path.Combine(exeDir, AppMeta.DataDirName);
            return new DataStore
            {
                Root = root,
                Backups = Path.Combine(root, "backups"),
                Reports = Path.Combine(root, "reports"),
                Logs = Path.Combine(root, "logs"),
                Updates = Path.Combine(root, "updates"),
                Quarantine = Path.Combine(root, "quarantine"),
                State = Path.Combine(root, "state"),
                Feedbacks = Path.Combine(root, "feedback")
            };
        }

        public void Ensure()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Backups);
            Directory.CreateDirectory(Reports);
            Directory.CreateDirectory(Logs);
            Directory.CreateDirectory(Updates);
            Directory.CreateDirectory(Quarantine);
            Directory.CreateDirectory(State);
            Directory.CreateDirectory(Feedbacks);
        }

        public string StateFile(string name)
        {
            return Path.Combine(State, name);
        }

        public string Timestamp()
        {
            return DateTime.Now.ToString("yyyyMMdd-HHmmss");
        }
    }

    internal static class Logger
    {
        private static DataStore store;

        public static void Initialize(DataStore dataStore)
        {
            store = dataStore;
        }

        public static void Error(string message, Exception ex)
        {
            try
            {
                if (store == null) return;
                string path = Path.Combine(store.Logs, "error-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine + ex + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
            }
        }
    }

    internal sealed class Finding : INotifyPropertyChanged
    {
        private bool selected;

        public event PropertyChangedEventHandler PropertyChanged;

        public bool Selected
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value;
                OnPropertyChanged("Selected");
            }
        }

        public int Id { get; set; }
        public string Risk { get; set; }
        public int Score { get; set; }
        public string Vendor { get; set; }
        public string Category { get; set; }
        public string UserVisibleName { get; set; }
        public string UserImpact { get; set; }
        public string TechnicalLocation { get; set; }
        public string ActionKind { get; set; }
        public ActionTarget Target { get; set; }
        public bool RequiresAdmin { get; set; }
        public bool CanRestore { get; set; }
        public string Evidence { get; set; }
        public string Status { get; set; }

        [ScriptIgnore]
        public Image SoftwareIcon { get; set; }
        [ScriptIgnore]
        public string SoftwareName { get; set; }
        [ScriptIgnore]
        public string IdentityConfidence { get; set; }
        [ScriptIgnore]
        public string IconSource { get; set; }
        [ScriptIgnore]
        public string IdentityExplanation { get; set; }

        public SoftwarePresentationEvidence PresentationEvidence()
        {
            ActionTarget target = Target ?? new ActionTarget();
            return new SoftwarePresentationEvidence
            {
                DeclaredName = UserVisibleName,
                DeclaredVendor = Vendor,
                IconValue = target.IconValue,
                FilePath = target.FilePath,
                Command = !string.IsNullOrEmpty(target.PresentationCommand) ? target.PresentationCommand : (!string.IsNullOrEmpty(target.UninstallCommand) ? target.UninstallCommand : Evidence),
                ServiceName = target.ServiceName,
                Clsid = target.Clsid,
                TechnicalLocation = TechnicalLocation
            };
        }

        public void ApplyPresentation(SoftwarePresentation presentation)
        {
            if (presentation == null) return;
            SoftwareIcon = presentation.Icon;
            SoftwareName = presentation.SoftwareName;
            IdentityConfidence = presentation.Confidence;
            IconSource = presentation.IconSource;
            IdentityExplanation = presentation.Explanation;
            if ((string.IsNullOrWhiteSpace(Vendor) || Vendor == "未知第三方" || Vendor == "未知") && presentation.Confidence != "Unknown") Vendor = presentation.Vendor;
        }

        public bool CanClean
        {
            get { return !string.Equals(ActionKind, "ReportOnly", StringComparison.OrdinalIgnoreCase); }
        }

        public string RiskDisplay
        {
            get { return CanClean ? Risk : "仅提示"; }
        }

        public bool BulkSelectable
        {
            get { return CanClean && !string.Equals(ActionKind, "InvokeUninstaller", StringComparison.OrdinalIgnoreCase); }
        }

        public string SelectionHint
        {
            get
            {
                if (CanClean && RequiresAdmin && !AdminUtil.IsAdministrator()) return "可勾选：处理时会请求 Windows 管理员权限；没有管理员凭据时仍可扫描和导出报告。";
                if (CanClean) return "可勾选：工具会先备份，再按“工具会怎么处理”执行。";
                return "不可勾选：" + ReportOnlyActionText();
            }
        }

        public string ActionText
        {
            get
            {
                if (string.Equals(ActionKind, "DeleteRegistryKey", StringComparison.OrdinalIgnoreCase)) return "备份后删除这条注册表项";
                if (string.Equals(ActionKind, "DeleteRegistryValue", StringComparison.OrdinalIgnoreCase)) return "备份后删除这条注册表值";
                if (string.Equals(ActionKind, "DisableShellExtension", StringComparison.OrdinalIgnoreCase)) return "备份状态后禁用右键扩展";
                if (string.Equals(ActionKind, "MoveFileToBackup", StringComparison.OrdinalIgnoreCase)) return "移动到恢复中心";
                if (string.Equals(ActionKind, "DisableService", StringComparison.OrdinalIgnoreCase)) return "备份状态后禁用服务";
                if (string.Equals(ActionKind, "DisableScheduledTask", StringComparison.OrdinalIgnoreCase)) return "备份状态后禁用计划任务";
                if (string.Equals(ActionKind, "InvokeUninstaller", StringComparison.OrdinalIgnoreCase)) return "只打开这个附带产品的卸载器，不卸载主程序";
                return ReportOnlyActionText();
            }
        }

        [ScriptIgnore]
        public string CompactTitle
        {
            get
            {
                string title = UserVisibleName ?? string.Empty;
                if (title.IndexOf("：会出现", StringComparison.Ordinal) >= 0 || title.IndexOf("：疑似会出现", StringComparison.Ordinal) >= 0)
                {
                    int open = title.IndexOf('“');
                    int close = title.LastIndexOf('”');
                    if (open >= 0 && close > open) return title.Substring(open + 1, close - open - 1).Trim();
                }
                return title;
            }
        }

        [ScriptIgnore]
        public string CompactLocation
        {
            get
            {
                string title = UserVisibleName ?? string.Empty;
                if (title.StartsWith("普通文件右键", StringComparison.Ordinal)) return "文件右键";
                if (title.StartsWith("文件夹右键", StringComparison.Ordinal)) return "文件夹右键";
                if (title.StartsWith("桌面/文件夹空白处右键", StringComparison.Ordinal)) return "空白处右键";
                if (title.StartsWith("磁盘盘符右键", StringComparison.Ordinal)) return "磁盘右键";
                if (title.StartsWith("快捷方式右键", StringComparison.Ordinal)) return "快捷方式";
                string category = Category ?? string.Empty;
                if (category.IndexOf("右键菜单", StringComparison.OrdinalIgnoreCase) >= 0) return "右键菜单";
                if (category.IndexOf("后台服务", StringComparison.OrdinalIgnoreCase) >= 0) return "后台服务";
                if (category.IndexOf("启动", StringComparison.OrdinalIgnoreCase) >= 0) return "开机启动";
                if (category.IndexOf("计划任务", StringComparison.OrdinalIgnoreCase) >= 0) return "计划任务";
                if (category.IndexOf("文件关联", StringComparison.OrdinalIgnoreCase) >= 0) return "文件关联";
                if (category.IndexOf("浏览器", StringComparison.OrdinalIgnoreCase) >= 0) return "浏览器";
                if (category.IndexOf("正在运行", StringComparison.OrdinalIgnoreCase) >= 0) return "正在运行";
                if (category.IndexOf("此电脑", StringComparison.OrdinalIgnoreCase) >= 0 || category.IndexOf("资源管理器", StringComparison.OrdinalIgnoreCase) >= 0) return "资源管理器";
                if (category.IndexOf("卸载", StringComparison.OrdinalIgnoreCase) >= 0 || category.IndexOf("弹窗", StringComparison.OrdinalIgnoreCase) >= 0 || category.IndexOf("捆绑", StringComparison.OrdinalIgnoreCase) >= 0) return "组件诊断";
                if (category.IndexOf("附带产品", StringComparison.OrdinalIgnoreCase) >= 0) return "附带产品";
                return category;
            }
        }

        [ScriptIgnore]
        public string CompactImpact
        {
            get
            {
                string category = Category ?? string.Empty;
                if (category.IndexOf("右键菜单", StringComparison.OrdinalIgnoreCase) >= 0) return "右键入口";
                if (category.IndexOf("后台服务", StringComparison.OrdinalIgnoreCase) >= 0) return "后台常驻";
                if (category.IndexOf("启动", StringComparison.OrdinalIgnoreCase) >= 0) return "开机启动";
                if (category.IndexOf("计划任务", StringComparison.OrdinalIgnoreCase) >= 0) return "定时运行";
                if (category.IndexOf("文件关联", StringComparison.OrdinalIgnoreCase) >= 0) return "打开方式";
                if (category.IndexOf("浏览器", StringComparison.OrdinalIgnoreCase) >= 0) return "浏览器组件";
                if (category.IndexOf("正在运行", StringComparison.OrdinalIgnoreCase) >= 0) return "正在运行";
                if (category.IndexOf("此电脑", StringComparison.OrdinalIgnoreCase) >= 0 || category.IndexOf("资源管理器", StringComparison.OrdinalIgnoreCase) >= 0) return "资源管理器入口";
                if (category.IndexOf("卸载", StringComparison.OrdinalIgnoreCase) >= 0) return "原厂卸载";
                if (category.IndexOf("附带产品", StringComparison.OrdinalIgnoreCase) >= 0) return "独立安装";
                if (category.IndexOf("弹窗", StringComparison.OrdinalIgnoreCase) >= 0 || category.IndexOf("捆绑", StringComparison.OrdinalIgnoreCase) >= 0 || category.IndexOf("守护", StringComparison.OrdinalIgnoreCase) >= 0) return "异常组件";
                return ShortDisplayText(UserImpact, 12);
            }
        }

        [ScriptIgnore]
        public string CompactAction
        {
            get
            {
                if (string.Equals(ActionKind, "DeleteRegistryKey", StringComparison.OrdinalIgnoreCase) || string.Equals(ActionKind, "DeleteRegistryValue", StringComparison.OrdinalIgnoreCase)) return "备份删除";
                if (string.Equals(ActionKind, "DisableShellExtension", StringComparison.OrdinalIgnoreCase)) return "备份禁用";
                if (string.Equals(ActionKind, "MoveFileToBackup", StringComparison.OrdinalIgnoreCase)) return "移入恢复";
                if (string.Equals(ActionKind, "DisableService", StringComparison.OrdinalIgnoreCase) || string.Equals(ActionKind, "DisableScheduledTask", StringComparison.OrdinalIgnoreCase)) return "备份禁用";
                if (string.Equals(ActionKind, "InvokeUninstaller", StringComparison.OrdinalIgnoreCase)) return "定向卸载";
                return "仅提示";
            }
        }

        private static string ShortDisplayText(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return "查看详情";
            string text = value.Trim();
            int sentence = text.IndexOfAny(new char[] { '。', '；', ';', '\r', '\n' });
            if (sentence > 0) text = text.Substring(0, sentence);
            return text.Length <= maxLength ? text : text.Substring(0, maxLength - 1) + "…";
        }

        private string ReportOnlyActionText()
        {
            string category = Category ?? string.Empty;
            if (category.IndexOf("默认打开程序", StringComparison.OrdinalIgnoreCase) >= 0) return "仅提示：这是双击默认打开方式，不替用户改默认应用";
            if (category.IndexOf("卸载入口", StringComparison.OrdinalIgnoreCase) >= 0) return "仅提示：没有可靠卸载命令，不硬删主程序";
            if (category.IndexOf("正在运行", StringComparison.OrdinalIgnoreCase) >= 0) return "仅提示：不强杀正在运行的进程";
            return "仅提示：为避免误伤，不参与一键清理";
        }

        private void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    internal sealed class UserWhitelistEntry
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string AddedAt { get; set; }
    }

    internal static class UserWhitelistStore
    {
        private const string FileName = "user-whitelist.json";

        public static string KeyFor(Finding finding)
        {
            if (finding == null) return string.Empty;
            ActionTarget target = finding.Target ?? new ActionTarget();
            return string.Join("|", new string[] { target.Kind, target.Hive, target.View, target.SubKey, target.ValueName, target.FilePath, target.ServiceName, target.TaskName, target.Clsid, finding.UserVisibleName })
                .ToLowerInvariant();
        }

        public static List<UserWhitelistEntry> Load(DataStore store)
        {
            try
            {
                string path = store.StateFile(FileName);
                if (!File.Exists(path)) return new List<UserWhitelistEntry>();
                List<UserWhitelistEntry> entries = new JavaScriptSerializer().Deserialize<List<UserWhitelistEntry>>(File.ReadAllText(path, Encoding.UTF8));
                return entries == null ? new List<UserWhitelistEntry>() : entries.Where(delegate(UserWhitelistEntry entry) { return entry != null && !string.IsNullOrWhiteSpace(entry.Key); }).ToList();
            }
            catch (Exception ex) { Logger.Error("读取用户白名单失败", ex); return new List<UserWhitelistEntry>(); }
        }

        public static void Save(DataStore store, List<UserWhitelistEntry> entries)
        {
            Directory.CreateDirectory(store.State);
            File.WriteAllText(store.StateFile(FileName), new JavaScriptSerializer().Serialize(entries ?? new List<UserWhitelistEntry>()), new UTF8Encoding(false));
        }

        public static bool Add(DataStore store, Finding finding)
        {
            string key = KeyFor(finding);
            if (string.IsNullOrWhiteSpace(key)) return false;
            List<UserWhitelistEntry> entries = Load(store);
            if (entries.Any(delegate(UserWhitelistEntry entry) { return string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase); })) return false;
            entries.Add(new UserWhitelistEntry { Key = key, Name = finding.UserVisibleName, AddedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
            Save(store, entries);
            return true;
        }

        public static bool Remove(DataStore store, Finding finding)
        {
            string key = KeyFor(finding);
            List<UserWhitelistEntry> entries = Load(store);
            int removed = entries.RemoveAll(delegate(UserWhitelistEntry entry) { return string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase); });
            if (removed > 0) Save(store, entries);
            return removed > 0;
        }

        public static void Apply(DataStore store, IEnumerable<Finding> findings)
        {
            HashSet<string> keys = new HashSet<string>(Load(store).Select(delegate(UserWhitelistEntry entry) { return entry.Key; }), StringComparer.OrdinalIgnoreCase);
            foreach (Finding finding in findings)
            {
                if (!keys.Contains(KeyFor(finding))) continue;
                finding.Selected = false;
                finding.Risk = "低";
                finding.Status = "已白名单";
                finding.UserImpact = "用户已主动加入本地白名单；本次仍保留证据展示，不建议处理。";
                finding.ActionKind = "ReportOnly";
            }
        }
    }

    internal static class VendorReviewWriter
    {
        public static string Write(DataStore store, string executablePath)
        {
            string hash;
            using (SHA256 sha = SHA256.Create()) using (FileStream stream = File.OpenRead(executablePath)) hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
            FileVersionInfo version = FileVersionInfo.GetVersionInfo(executablePath);
            string signer = "未检测到有效签名";
            try { X509Certificate certificate = X509Certificate.CreateFromSignedFile(executablePath); if (certificate != null) signer = certificate.Subject; } catch { }
            string path = Path.Combine(store.Reports, "vendor-review-" + store.Timestamp() + ".md");
            string body = "# 安全软件误报复核材料\n\n- 产品：" + AppMeta.ProductName + "\n- 版本：" + AppMeta.Version + "\n- 文件名：" + Path.GetFileName(executablePath) + "\n- SHA-256：`" + hash + "`\n- 签名：" + signer + "\n- 生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n\n该材料仅用于向安全软件厂商申请复核；程序不包含规避、绕过或对抗安全软件的功能。\n";
            File.WriteAllText(path, body, new UTF8Encoding(false));
            return path;
        }
    }

    internal sealed class ActionTarget
    {
        public string Kind { get; set; }
        public string Hive { get; set; }
        public string View { get; set; }
        public string SubKey { get; set; }
        public string ValueName { get; set; }
        public string FilePath { get; set; }
        public string ServiceName { get; set; }
        public string TaskName { get; set; }
        public string UninstallCommand { get; set; }
        public string IconValue { get; set; }
        public string PresentationCommand { get; set; }
        public string Clsid { get; set; }
        public string SourceSubKey { get; set; }
        public string ExpectedProductName { get; set; }
        public string ExpectedPublisher { get; set; }
        public string ExpectedUninstallCommand { get; set; }
    }

    internal enum ProductRemovalDisposition
    {
        Ignore,
        ReportComponentOnly,
        TargetIndependentProduct
    }

    internal static class ProductRemovalPolicy
    {
        private static readonly string[] StrongIndependentProductMarkers = new string[]
        {
            "360desktop", "desktoplite", "360桌面", "小鸟壁纸", "birdwallpaper", "wallpaper", "壁纸", "画报", "屏保",
            "桌面助手", "桌面整理", "hotnews", "热点资讯", "minipage", "迷你页", "popup", "adcomponent", "adservice",
            "gamecenter", "gamehall", "游戏中心", "游戏大厅", "推广组件", "广告组件"
        };

        private static readonly string[] WeakIndependentProductMarkers = new string[]
        {
            "softmgr", "软件管家", "browser", "浏览器", "tips", "资讯"
        };

        private static readonly string[] AbnormalPersistenceMarkers = new string[]
        {
            "watchdog", "guard", "keeper", "daemon", "popup", "adservice", "adpush", "hotnews", "newsfeed", "minipage",
            "守护", "自动恢复", "弹窗", "广告", "热点", "资讯", "推送"
        };

        public static ProductRemovalDisposition Classify(string displayName, string childName, string installLocation, string displayIcon, string uninstallCommand, bool hidden, bool adOrGuard, bool badComponent)
        {
            string text = string.Join(" ", new string[] { displayName, childName, installLocation, displayIcon }.Where(delegate(string value) { return !string.IsNullOrWhiteSpace(value); }).ToArray()).ToLowerInvariant();
            bool strongIndependentProduct = StrongIndependentProductMarkers.Any(delegate(string marker) { return text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0; });
            bool weakIndependentProduct = WeakIndependentProductMarkers.Any(delegate(string marker) { return text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0; });
            bool independentProduct = strongIndependentProduct || (weakIndependentProduct && (hidden || adOrGuard || badComponent));
            bool hasNamedUninstaller = !string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(uninstallCommand);
            if (independentProduct && hasNamedUninstaller) return ProductRemovalDisposition.TargetIndependentProduct;
            if (hidden && (adOrGuard || badComponent)) return ProductRemovalDisposition.ReportComponentOnly;
            return ProductRemovalDisposition.Ignore;
        }

        public static bool IsAbnormalPersistence(string name, string executablePath, bool badComponent)
        {
            if (badComponent) return true;
            string text = ((name ?? string.Empty) + " " + (executablePath ?? string.Empty)).ToLowerInvariant();
            return AbnormalPersistenceMarkers.Any(delegate(string marker) { return text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0; });
        }
    }

    internal enum ContextMenuDiagnosisDisposition
    {
        Ignore,
        Governed,
        ReportOnly,
        SystemProtected,
        ActionableExtension,
        ActionableCommand
    }

    internal static class ContextMenuDiagnosisPolicy
    {
        public static ContextMenuDiagnosisDisposition Classify(ContextMenuEntry entry, VendorIdentityResult identity)
        {
            if (entry == null || identity == null || !identity.Confirmed || identity.Conflicted) return ContextMenuDiagnosisDisposition.Ignore;
            if (string.Equals(entry.Scene, "命令仓库", StringComparison.OrdinalIgnoreCase)) return ContextMenuDiagnosisDisposition.Ignore;
            bool extension = string.Equals(entry.Type, "Shell 扩展", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.Type, "现代右键扩展", StringComparison.OrdinalIgnoreCase);
            if (!extension && IsCoreFileTypeVerb(entry.SubKey)) return ContextMenuDiagnosisDisposition.Ignore;
            if (!extension && IsSystemCommand(entry)) return ContextMenuDiagnosisDisposition.SystemProtected;
            if (!entry.Enabled) return ContextMenuDiagnosisDisposition.Governed;
            if (entry.ReadOnly || (extension && string.IsNullOrWhiteSpace(entry.Clsid))) return ContextMenuDiagnosisDisposition.ReportOnly;
            return extension ? ContextMenuDiagnosisDisposition.ActionableExtension : ContextMenuDiagnosisDisposition.ActionableCommand;
        }

        private static bool IsCoreFileTypeVerb(string subKey)
        {
            string value = (subKey ?? string.Empty).TrimEnd('\\');
            int slash = value.LastIndexOf('\\');
            string verb = (slash < 0 ? value : value.Substring(slash + 1)).Trim().ToLowerInvariant();
            return verb == "open" || verb == "edit" || verb == "print" || verb == "printto" || verb == "new" ||
                verb == "runas" || verb == "runasuser" || verb == "play" || verb == "preview";
        }

        private static bool IsSystemCommand(ContextMenuEntry entry)
        {
            string command = (entry == null ? string.Empty : entry.Command ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(command)) return false;
            string lowered = Environment.ExpandEnvironmentVariables(command).ToLowerInvariant();
            string[] systemTokens = new string[]
            {
                @"\windows\system32\",
                @"\windows\syswow64\",
                @"\windows\system\",
                @"\windows\explorer.exe"
            };
            foreach (string token in systemTokens)
            {
                if (lowered.IndexOf(token, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }
    }

    internal sealed class CleanupResult
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Vendor { get; set; }
        public string Category { get; set; }
        public string ActionKind { get; set; }
        public string TechnicalLocation { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public string Backup { get; set; }
        public ActionTarget Target { get; set; }

        [ScriptIgnore]
        public Image SoftwareIcon { get; set; }
        [ScriptIgnore]
        public string SoftwareName { get; set; }
        [ScriptIgnore]
        public string IdentityConfidence { get; set; }
        [ScriptIgnore]
        public string IconSource { get; set; }
        [ScriptIgnore]
        public string IdentityExplanation { get; set; }

        public SoftwarePresentationEvidence PresentationEvidence()
        {
            ActionTarget target = Target ?? new ActionTarget();
            return new SoftwarePresentationEvidence
            {
                DeclaredName = Title,
                DeclaredVendor = Vendor,
                IconValue = target.IconValue,
                FilePath = target.FilePath,
                Command = !string.IsNullOrEmpty(target.PresentationCommand) ? target.PresentationCommand : target.UninstallCommand,
                ServiceName = target.ServiceName,
                Clsid = target.Clsid,
                TechnicalLocation = TechnicalLocation
            };
        }

        public void ApplyPresentation(SoftwarePresentation presentation)
        {
            if (presentation == null) return;
            SoftwareIcon = presentation.Icon;
            SoftwareName = presentation.SoftwareName;
            IdentityConfidence = presentation.Confidence;
            IconSource = presentation.IconSource;
            IdentityExplanation = presentation.Explanation;
        }
    }

    internal sealed class CleanupBatch
    {
        public string Id { get; set; }
        public string CreatedAt { get; set; }
        public string Path { get; set; }
        public List<CleanupResult> Results { get; set; }
    }

    internal static class ChineseDisplayText
    {
        public static string CleanupStatus(string status)
        {
            if (string.Equals(status, "Done", StringComparison.OrdinalIgnoreCase)) return "已处理";
            if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase)) return "失败";
            if (string.Equals(status, "RestoreFailed", StringComparison.OrdinalIgnoreCase)) return "恢复失败";
            if (string.Equals(status, "Launched", StringComparison.OrdinalIgnoreCase)) return "已打开卸载窗口";
            if (string.Equals(status, "Skipped", StringComparison.OrdinalIgnoreCase)) return "已跳过";
            return string.IsNullOrWhiteSpace(status) ? "未知" : status;
        }

        public static string ContextMenuType(string type)
        {
            if (string.Equals(type, "Shell 命令", StringComparison.OrdinalIgnoreCase)) return "右键命令";
            if (string.Equals(type, "Shell 扩展", StringComparison.OrdinalIgnoreCase)) return "右键扩展";
            if (string.Equals(type, "现代右键扩展", StringComparison.OrdinalIgnoreCase)) return "现代右键扩展";
            if (string.Equals(type, "CommandStore", StringComparison.OrdinalIgnoreCase)) return "命令仓库";
            return string.IsNullOrWhiteSpace(type) ? "未知类型" : type;
        }

        public static string ContextMenuName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            string text = value.Trim();
            if (text.Equals("Open Folder as PyCharm Project", StringComparison.OrdinalIgnoreCase)) return "作为 PyCharm 项目打开文件夹";
            if (text.Equals("Open Folder as Project", StringComparison.OrdinalIgnoreCase)) return "作为项目打开文件夹";
            if (text.Equals("Open Git Bash here", StringComparison.OrdinalIgnoreCase)) return "在此处打开 Git Bash";
            if (text.Equals("Open Git GUI Here", StringComparison.OrdinalIgnoreCase)) return "在此处打开 Git 图形界面";
            if (text.Equals("Open in Windows Terminal", StringComparison.OrdinalIgnoreCase)) return "在 Windows 终端中打开";
            if (text.Equals("Open PowerShell window here", StringComparison.OrdinalIgnoreCase)) return "在此处打开 PowerShell 窗口";
            if (text.Equals("Scan with Microsoft Defender...", StringComparison.OrdinalIgnoreCase) || text.Equals("Scan with Microsoft Defender…", StringComparison.OrdinalIgnoreCase)) return "使用 Microsoft Defender 扫描";
            if (text.Equals("Pin to Quick access", StringComparison.OrdinalIgnoreCase)) return "固定到快速访问";
            if (text.Equals("Unpin from Quick access", StringComparison.OrdinalIgnoreCase)) return "从快速访问取消固定";
            if (text.Equals("Open", StringComparison.OrdinalIgnoreCase)) return "打开";
            if (text.Equals("Edit", StringComparison.OrdinalIgnoreCase)) return "编辑";
            if (text.Equals("Print", StringComparison.OrdinalIgnoreCase)) return "打印";
            if (text.Equals("Share", StringComparison.OrdinalIgnoreCase)) return "共享";
            Match editWith = Regex.Match(text, @"^Edit\s+with\s+(?<app>.+)$", RegexOptions.IgnoreCase);
            if (editWith.Success) return "使用 " + TrimEnglishDecoration(editWith.Groups["app"].Value) + " 编辑";
            Match contextMenu = Regex.Match(text, @"^(?<app>.+?)\s+Context\s+menu$", RegexOptions.IgnoreCase);
            if (contextMenu.Success) return TrimEnglishDecoration(contextMenu.Groups["app"].Value) + " 右键菜单";
            Match openIn = Regex.Match(text, @"^Open\s+(?:Folder\s+)?in\s+(?<app>.+)$", RegexOptions.IgnoreCase);
            if (openIn.Success) return "在 " + TrimEnglishDecoration(openIn.Groups["app"].Value) + " 中打开";
            if (text.StartsWith("Open with ", StringComparison.OrdinalIgnoreCase)) return "使用 " + text.Substring(10).Trim() + " 打开";
            Match scanWith = Regex.Match(text, @"^Scan\s+with\s+(?<app>.+)$", RegexOptions.IgnoreCase);
            if (scanWith.Success) return "使用 " + TrimEnglishDecoration(scanWith.Groups["app"].Value) + " 扫描";
            Match compareWith = Regex.Match(text, @"^Compare\s+with\s+(?<app>.+)$", RegexOptions.IgnoreCase);
            if (compareWith.Success) return "使用 " + TrimEnglishDecoration(compareWith.Groups["app"].Value) + " 比较";
            Match uploadTo = Regex.Match(text, @"^Upload\s+to\s+(?<app>.+)$", RegexOptions.IgnoreCase);
            if (uploadTo.Success) return "上传到 " + TrimEnglishDecoration(uploadTo.Groups["app"].Value);
            Match addTo = Regex.Match(text, @"^Add\s+to\s+(?<target>.+)$", RegexOptions.IgnoreCase);
            if (addTo.Success) return "添加到 " + TrimEnglishDecoration(addTo.Groups["target"].Value);
            Match sendTo = Regex.Match(text, @"^Send\s+to\s+(?<target>.+)$", RegexOptions.IgnoreCase);
            if (sendTo.Success) return "发送到 " + TrimEnglishDecoration(sendTo.Groups["target"].Value);
            return text;
        }

        public static string EnsureChineseContextMenuName(string value, string softwareName, string scene)
        {
            string translated = ContextMenuName(value);
            if (HasChinese(translated)) return translated;
            string software = SoftwareName(softwareName);
            if (!string.IsNullOrWhiteSpace(software) && software != "来源未确认" && software != "正在识别…") return software + "右键菜单";
            string location = string.IsNullOrWhiteSpace(scene) ? "" : scene.Trim();
            return (HasChinese(location) ? location : "第三方软件") + "右键菜单";
        }

        public static string SoftwareName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "来源未确认";
            string text = value.Trim();
            if (HasChinese(text)) return text;
            if (text.IndexOf("WPS Office", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Kingsoft", StringComparison.OrdinalIgnoreCase) >= 0) return "WPS / 金山";
            if (text.IndexOf("PyCharm", StringComparison.OrdinalIgnoreCase) >= 0) return "PyCharm 开发工具";
            if (text.IndexOf("Notepad++", StringComparison.OrdinalIgnoreCase) >= 0) return "Notepad++ 文本编辑器";
            if (text.IndexOf("WinRAR", StringComparison.OrdinalIgnoreCase) >= 0) return "WinRAR 压缩软件";
            if (text.IndexOf("Beyond Compare", StringComparison.OrdinalIgnoreCase) >= 0) return "Beyond Compare 文件比较工具";
            if (text.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0 && text.IndexOf("Operating System", StringComparison.OrdinalIgnoreCase) >= 0) return "Windows 系统组件";
            if (text.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0) return "AMD Radeon 显卡软件";
            if (text.Equals("Git", StringComparison.OrdinalIgnoreCase)) return "Git 版本管理工具";
            if (text.Equals("Source", StringComparison.OrdinalIgnoreCase) || text.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) return "来源未确认";
            return text + " 软件";
        }

        public static bool HasChinese(string value)
        {
            return !string.IsNullOrEmpty(value) && value.Any(delegate(char character) { return character >= '\u3400' && character <= '\u9fff'; });
        }

        private static string TrimEnglishDecoration(string value)
        {
            return (value ?? string.Empty).Trim().TrimEnd('.', '…').Trim();
        }

        public static string RegistryView(string view)
        {
            if (string.Equals(view, "Registry32", StringComparison.OrdinalIgnoreCase)) return "32 位注册表";
            if (string.Equals(view, "Registry64", StringComparison.OrdinalIgnoreCase)) return "64 位注册表";
            if (string.Equals(view, "Default", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            return view ?? string.Empty;
        }

        public static string SystemShortcutName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            Match match = Regex.Match(value, @"^(?<prefix>\d+[a-z]?(?:-\d+)?\s*-\s*)?(?<name>.+)$", RegexOptions.IgnoreCase);
            string prefix = match.Success ? match.Groups["prefix"].Value : string.Empty;
            string name = match.Success ? match.Groups["name"].Value.Trim() : value.Trim();
            Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Desktop", "桌面" }, { "Run", "运行" }, { "Search", "搜索" }, { "Windows Explorer", "文件资源管理器" },
                { "Control Panel", "控制面板" }, { "Task Manager", "任务管理器" }, { "Computer Management", "计算机管理" },
                { "Disk Management", "磁盘管理" }, { "NetworkStatus", "网络状态" }, { "Network Connections", "网络连接" },
                { "Programs and Features", "程序和功能" }, { "Mobility Center", "移动中心" }, { "Event Viewer", "事件查看器" },
                { "Device Manager", "设备管理器" }, { "Command Prompt", "命令提示符" }
            };
            string translated;
            return names.TryGetValue(name, out translated) ? prefix + translated : value;
        }

        public static string GroupName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            Match match = Regex.Match(value, @"^Group(?<number>\d+)$", RegexOptions.IgnoreCase);
            return match.Success ? "第" + match.Groups["number"].Value + "组" : value;
        }
    }

    internal sealed class ScanErrorReport
    {
        public string StartedAt { get; set; }
        public string FailedAt { get; set; }
        public string ProductVersion { get; set; }
        public string ExecutablePath { get; set; }
        public string ExecutableDirectory { get; set; }
        public string DataDirectory { get; set; }
        public string ErrorType { get; set; }
        public string ErrorMessage { get; set; }
        public string StackTrace { get; set; }
    }

    internal sealed class ScanWarning
    {
        public string Stage { get; set; }
        public string TechnicalLocation { get; set; }
        public string ErrorType { get; set; }
        public string Message { get; set; }
    }

    internal sealed class ScanEvidenceReport
    {
        public string ScannedAt { get; set; }
        public string ProductVersion { get; set; }
        public int FindingCount { get; set; }
        public int WarningCount { get; set; }
        public List<Finding> Findings { get; set; }
        public List<ScanWarning> Warnings { get; set; }
    }

    internal sealed class RestoreBatchResult
    {
        public int Total { get; set; }
        public int Succeeded { get; set; }
        public int Failed { get; set; }
        public List<string> Messages { get; set; }

        public bool AllSucceeded
        {
            get { return Failed == 0; }
        }
    }

    internal static class AdminUtil
    {
        public static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static bool RelaunchAsAdmin(IWin32Window owner, DataStore store, ElevationResumeState resume)
        {
            if (IsAdministrator()) return true;
            string resumePath = null;
            try
            {
                if (store != null && resume != null) resumePath = resume.Save(store);
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Application.ExecutablePath;
                psi.WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                if (!string.IsNullOrWhiteSpace(resumePath)) psi.Arguments = "--elevation-resume " + QuoteArgument(resumePath);
                Process.Start(psi);
                Application.Exit();
                return true;
            }
            catch (Win32Exception ex)
            {
                DeleteResumeFile(resumePath);
                if (ex.NativeErrorCode == 1223)
                {
                    MessageBox.Show(owner, "已取消管理员权限请求。\n\n普通模式仍可继续扫描、查看结果和导出证据报告；系统级项目暂不处理。", "继续使用普通模式", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(owner, "Windows 没有授予管理员权限。\n\n如果这是单位电脑或当前账户没有管理员凭据，请把证据报告交给管理员处理。\n\n系统返回：" + ex.Message, "无法获得管理员权限", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return false;
            }
            catch (Exception ex)
            {
                DeleteResumeFile(resumePath);
                MessageBox.Show(owner, "请求管理员权限失败。普通模式仍可继续使用。\n\n" + ex.Message, "无法获得管理员权限", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private static void DeleteResumeFile(string path)
        {
            try { if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    internal sealed class ElevationResumeState
    {
        public string Page { get; set; }
        public bool ScanAfterLaunch { get; set; }
        public bool OpenContextMenu { get; set; }
        public bool OpenRecoveryCenter { get; set; }
        public string RecoveryBatchId { get; set; }
        public List<string> SelectedFindingKeys { get; set; }
        public string CreatedAt { get; set; }

        public ElevationResumeState()
        {
            SelectedFindingKeys = new List<string>();
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public string Save(DataStore store)
        {
            if (store == null) throw new ArgumentNullException("store");
            Directory.CreateDirectory(store.State);
            string path = Path.Combine(store.State, "elevation-resume-" + Guid.NewGuid().ToString("N") + ".json");
            string json = new JavaScriptSerializer().Serialize(this);
            File.WriteAllText(path, json, new UTF8Encoding(false));
            return path;
        }

        public static ElevationResumeState LoadFromArguments(string[] args, DataStore store)
        {
            int index = Array.FindIndex(args ?? new string[0], delegate(string arg) { return string.Equals(arg, "--elevation-resume", StringComparison.OrdinalIgnoreCase); });
            if (index < 0 || index + 1 >= args.Length) return null;
            string path = args[index + 1];
            bool trustedPath = false;
            try
            {
                if (store == null || string.IsNullOrWhiteSpace(path)) return null;
                string fullPath = Path.GetFullPath(path);
                string stateRoot = Path.GetFullPath(store.State).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                trustedPath = fullPath.StartsWith(stateRoot, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(fullPath).StartsWith("elevation-resume-", StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase);
                if (!trustedPath || !File.Exists(fullPath)) return null;
                path = fullPath;
                ElevationResumeState state = new JavaScriptSerializer().Deserialize<ElevationResumeState>(File.ReadAllText(path, Encoding.UTF8));
                DateTime created;
                if (state == null || !DateTime.TryParse(state.CreatedAt, out created) || DateTime.Now.Subtract(created).TotalMinutes > 10) return null;
                if (state.SelectedFindingKeys == null) state.SelectedFindingKeys = new List<string>();
                return state;
            }
            catch (Exception ex)
            {
                Logger.Error("读取管理员重启状态失败", ex);
                return null;
            }
            finally
            {
                try { if (trustedPath && !string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        public static string FindingKey(Finding finding)
        {
            if (finding == null) return string.Empty;
            ActionTarget target = finding.Target ?? new ActionTarget();
            return string.Join("|", new string[]
            {
                finding.ActionKind, target.Kind, target.Hive, target.View, target.SubKey, target.ValueName,
                target.FilePath, target.ServiceName, target.TaskName, target.Clsid, target.SourceSubKey
            }.Select(delegate(string value) { return (value ?? string.Empty).Trim().ToLowerInvariant(); }).ToArray());
        }

        public static List<string> RunSelfTests()
        {
            List<string> failures = new List<string>();
            Finding first = new Finding { ActionKind = "DisableService", Target = new ActionTarget { Kind = "DisableService", ServiceName = "DemoSvc" } };
            Finding same = new Finding { ActionKind = "disableservice", Target = new ActionTarget { Kind = "DisableService", ServiceName = "demosvc" } };
            Finding other = new Finding { ActionKind = "DisableService", Target = new ActionTarget { Kind = "DisableService", ServiceName = "OtherSvc" } };
            if (FindingKey(first) != FindingKey(same)) failures.Add("管理员恢复状态：同一目标的稳定标识不一致");
            if (FindingKey(first) == FindingKey(other)) failures.Add("管理员恢复状态：不同目标的稳定标识发生冲突");
            string lab = Path.Combine(Path.GetTempPath(), "RogueCleanerElevationRegression-" + Guid.NewGuid().ToString("N"));
            try
            {
                DataStore store = DataStore.CreateForExecutable(Path.Combine(lab, "验证程序.exe"));
                store.Ensure();
                ElevationResumeState expected = new ElevationResumeState { Page = "诊断", ScanAfterLaunch = true, SelectedFindingKeys = new List<string> { FindingKey(first) } };
                string path = expected.Save(store);
                ElevationResumeState actual = LoadFromArguments(new string[] { "--elevation-resume", path }, store);
                if (actual == null || actual.Page != "诊断" || !actual.ScanAfterLaunch || actual.SelectedFindingKeys.Count != 1) failures.Add("管理员恢复状态：状态文件往返失败");
                if (File.Exists(path)) failures.Add("管理员恢复状态：已消费的临时状态文件没有删除");
                string outside = Path.Combine(lab, "elevation-resume-outside.json");
                File.WriteAllText(outside, "{}", Encoding.UTF8);
                if (LoadFromArguments(new string[] { "--elevation-resume", outside }, store) != null) failures.Add("管理员恢复状态：接受了状态目录外的文件");
                if (!File.Exists(outside)) failures.Add("管理员恢复状态：错误删除了状态目录外的文件");
            }
            catch (Exception ex) { failures.Add("管理员恢复状态回归异常：" + ex.GetType().Name + "：" + ex.Message); }
            finally { try { if (Directory.Exists(lab)) Directory.Delete(lab, true); } catch { } }
            return failures;
        }
    }

    internal interface IProgressSink
    {
        void Stage(string text);
        void Finding(Finding finding);
    }

    internal sealed class VendorEvidence
    {
        public readonly List<string> HumanTexts = new List<string>();
        public readonly List<string> Publishers = new List<string>();
        public readonly List<string> ProductNames = new List<string>();
        public readonly List<string> TechnicalIdentifiers = new List<string>();
        public readonly List<string> Commands = new List<string>();
        public readonly List<string> FilePaths = new List<string>();
        public readonly List<string> MsiProductCodes = new List<string>();
        public readonly List<string> OpaqueValues = new List<string>();

        public VendorEvidence AddHuman(params string[] values) { Add(HumanTexts, values); return this; }
        public VendorEvidence AddPublisher(params string[] values) { Add(Publishers, values); return this; }
        public VendorEvidence AddProduct(params string[] values) { Add(ProductNames, values); return this; }
        public VendorEvidence AddTechnical(params string[] values) { Add(TechnicalIdentifiers, values); return this; }
        public VendorEvidence AddCommand(params string[] values) { Add(Commands, values); return this; }
        public VendorEvidence AddFile(params string[] values) { Add(FilePaths, values); return this; }
        public VendorEvidence AddMsi(params string[] values) { Add(MsiProductCodes, values); return this; }
        public VendorEvidence AddOpaque(params string[] values) { Add(OpaqueValues, values); return this; }

        private static void Add(List<string> target, IEnumerable<string> values)
        {
            if (values == null) return;
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (!target.Contains(value, StringComparer.OrdinalIgnoreCase)) target.Add(value.Trim());
            }
        }
    }

    internal sealed class VendorIdentityResult
    {
        public string Vendor { get; set; }
        public int Confidence { get; set; }
        public bool Confirmed { get; set; }
        public bool Conflicted { get; set; }
        public string EvidenceSummary { get; set; }
    }

    internal static class RuleCatalog
    {
        private sealed class VendorRule
        {
            public string Name;
            public string Snark;
            public int Boost;
            public bool BehaviorOnly;
            public string[] Patterns;
            public string[] BadComponents;
        }

        private static readonly VendorRule[] Vendors = new VendorRule[]
        {
            new VendorRule { Name = "360 系列", Snark = "右键桌面不够，还想住进开机启动。", Boost = 25, Patterns = new [] { "Qihoo", "Qihu", "奇虎", "360.cn", "360Safe", "360sd", "360rp", "360se", "360Chrome", "360zip", "360Desktop", "360DesktopLite", "360Wallpaper", "360AlbumViewer", "360AI图片", "360AI", "360Pic", "360KanPic", "360Image", "Safe360Ext", "SoftMgrExt", "AblumViewer", "AlbumViewer", "shell360ext", "QHActiveDefense", "ZhuDongFangYu", "QHWatchdog", "QHProtected", "QHWebProtection", "QHSafeTray", "360软件管家", "360安全卫士", "360压缩", "360浏览器", "360极速浏览器", "360看图" }, BadComponents = new [] { "Safe360Ext", "SoftMgrExt", "AblumViewerMenuExt", "AlbumViewerMenuExt", "ShellExt64.dll", "shell360ext64.dll", "360AI图片", "QHWatchdog", "QHProtected" } },
            new VendorRule { Name = "WPS / 金山", Snark = "文档软件顺手也想接管图片、云文档和右键。", Boost = 18, Patterns = new [] { "WPS Office", "WPS.", "WPS_", "WPS-", "Kingsoft", "金山", "Zhuhai Kingsoft", "kwps", "qingshell", "qingnse", "kdesktop", "kdocs", "photolaunch", "wpscloud", "WpsDrive", "WPS.PIC", "WPSPic", "WPSPhoto", "WPS图片", "QingNseContextMenu", "kwpsshellext", "qingshellext", "kdesktopshellext", "qkdesktopshellext", "WPSAI", "WPS AI", "KingsoftAI", "AiWPS", "WPS灵犀", "wpsLingxi", "lingxi", "旺仔", "Wangzai", "wpscenter", "wpsupdate", "WpsUpdateTask", "WPS Office Cloud Service", "wpscloudsvr", "ksomisc" }, BadComponents = new [] { "kwpsshellext", "qingshellext", "QingNseContextMenu", "kdesktopshellext", "qkdesktopshellext", "WPS.PIC", "WPSPic", "photolaunch.exe", "Wangzai", "wpscloudsvr" } },
            new VendorRule { Name = "百度 / 百度网盘", Snark = "网盘不只同步文件，还喜欢同步到右键菜单。", Boost = 18, Patterns = new [] { "Baidu", "百度", "BaiduNetdisk", "BaiduNetdiskUnite", "BaiduNetdiskImageViewer", "BaiduNetdiskImageView", "BaiduNetdiskDesktopSync", "BaiduNetdiskSync", "BaiduNetdiskUtility", "BaiduNetdiskService", "BaiduNetdiskHost", "BaiduYun", "BaiduYunDetect", "YunShell", "YunShellExt", "YunDetectService", "cloudpic", "百度网盘看图", "百度网盘同步", "北京度友" }, BadComponents = new [] { "YunShellExt", "YunShellExplorerCommand", "BaiduNetdiskImageViewer", "BaiduNetdiskImageView", "BaiduNetdiskUtility", "BaiduNetdiskService", "cloudpic.dll", "imageviewer" } },
            new VendorRule { Name = "夸克 / 夸克网盘", Snark = "网盘上传和 PDF 转换也来抢右键，至少别披成迅雷的马甲。", Boost = 18, Patterns = new [] { "QuarkCloudDrive", "QuarkCloudDrive.upload", "QuarkCloudDrive.backup", "QuarkNetdisk", "QuarkDisk", "QuarkPan", "QuarkPDF", "QuarkConvert", "QuarkPC", "quark.cn", "pan.quark.cn", "vt.quark.cn", "quark-pc", "external_rclick", "夸克", "夸克网盘", "夸克浏览器", "上传到夸克网盘", "夸克网盘上传" }, BadComponents = new [] { "QuarkCloudDrive.upload", "QuarkCloudDrive.backup", "QuarkPDF", "QuarkConvert", "quark-pc", "external_rclick", "上传到夸克网盘", "PDF转换", "图片转PDF", "万能转换" } },
            new VendorRule { Name = "搜狗", Snark = "输入法可以输入字，但没必要输入到开机项里。", Boost = 16, Patterns = new [] { "Sogou", "搜狗", "SogouInput", "SogouPY", "SogouExplorer", "SogouCloud", "SogouIme", "SogouImeBroker", "SogouImeMgr", "SogouFlash", "SogouTips", "SogouNews", "SogouPopup", "SogouSvc", "SGImeGuard", "SogouInputPop", "SogouAd", "SogouUpdate", "SogouComMgr", "PinyinUp" }, BadComponents = new [] { "SogouImeBroker", "SogouExplorer", "SogouFlash", "SogouTips", "SogouAd", "SogouInputPop", "SogouPopup", "SogouNews", "SGImeGuard" } },
            new VendorRule { Name = "迅雷", Snark = "下载器最爱给自己安排开机打卡。", Boost = 20, Patterns = new [] { "Xunlei", "Thunder", "迅雷", "Thunder Network", "XLService", "XLServicePlatform", "ThunderPlatform", "ThunderAgent", "ThunderStart", "ThunderBrowser", "XunleiBHO", "XunleiDownload", "XunleiMedia", "Xunlei.XLB", "XLLiveUD", "XLGameBox", "TBCrash", "迅雷下载助手" }, BadComponents = new [] { "XLService", "XLServicePlatform", "ThunderPlatform", "Xunlei.XLB", "ThunderBrowser", "ThunderStart", "XunleiBHO" } },
            new VendorRule { Name = "钉钉", Snark = "办公协作可以，文件右键也要塞上传入口就过界了。", Boost = 14, Patterns = new [] { "DingTalk", "Dingtalk", "dingtalk", "DingDing", "钉钉", "DingTalkShellExt", "DingTalkContextMenu", "DingTalkUpload", "DingTalkDrive", "DingTalkDocs", "DingTalkFile", "DingTalkOffice", "DingTalkLite", "AliDingTalk", "com.alibaba.dingtalk", "上传钉钉并打开", "上传到钉钉", "钉钉并打开", "钉盘" }, BadComponents = new [] { "DingTalkShellExt", "DingTalkContextMenu", "DingTalkUpload", "上传钉钉并打开", "上传到钉钉", "DingTalkDrive" } },
            new VendorRule { Name = "腾讯系", Snark = "聊天归聊天，别顺手接管浏览器和启动项。", Boost = 12, Patterns = new [] { "Tencent", "腾讯", "QQBrowser", "QQPCMgr", "QQPCMGR", "QQProtect", "QQPCRTP", "QQRepair", "QQShellExt", "TXShell", "TIM.exe", "TIM\\", "WeChat", "微信", "企业微信", "WXWork", "TencentDocs", "腾讯文档", "QQLive", "QQMusic", "QBCore", "QBUpdate", "电脑管家" }, BadComponents = new [] { "QQPCMgr", "QQBrowser", "QQProtect", "QQPCRTP", "QQShellExt", "TXShell", "QBUpdate" } },
            new VendorRule { Name = "2345 系列", Snark = "名字像门牌号，行为像钉子户。", Boost = 25, Patterns = new [] { "2345Explorer", "2345Soft", "2345SoftMgr", "2345Pic", "2345PicViewer", "2345Kantuwang", "2345Zip", "2345Safe", "2345Protect", "2345Svc", "2345MiniPage", "2345Browser", "2345看图王", "2345好压", "王牌" }, BadComponents = new [] { "2345Explorer", "2345Soft", "2345SoftMgr", "2345Pic", "2345Zip", "2345Protect", "2345MiniPage" } },
            new VendorRule { Name = "猎豹 / 金山毒霸", Snark = "安全软件当然能安全，问题是别把自己藏成常驻钉子。", Boost = 18, Patterns = new [] { "Cheetah", "猎豹", "Liebao", "Kingsoft Internet Security", "金山毒霸", "KSafe", "KSafeSvc", "KWatch", "kismain", "kavsrv", "KSafeTray", "KMailMon", "KSoft" }, BadComponents = new [] { "KSafeSvc", "KWatch", "kavsrv", "KSafeTray", "Cheetah" } },
            new VendorRule { Name = "驱动/硬件检测工具", Snark = "修驱动可以，常驻当监工就过分了。", Boost = 18, Patterns = new [] { "DriverGenius", "DriverLife", "DriveTheLife", "驱动精灵", "驱动人生", "MyDrivers", "DrvMgr", "DGDaemon", "DTLService", "LuDaShi", "鲁大师", "MasterLu", "LdsLite", "LdsSvc", "LdsDaemon", "ComputerZ", "HardwareProtect" }, BadComponents = new [] { "DriverGenius", "DriverLife", "DriveTheLife", "LuDaShi", "MasterLu", "LdsSvc", "LdsDaemon" } },
            new VendorRule { Name = "Bandisoft 看图/压缩工具", Snark = "看图软件也要在右键菜单刷存在感。", Boost = 12, Patterns = new [] { "Bandisoft", "BandiView", "BandiView.exe", "Bandiview", "Honeyview", "HoneyView", "Bandizip", "BandiZip", "BandizipShellext", "BandizipShell", "BandiViewShell", "BandiViewExt", "BandiViewShellExt", "Open with BandiView", "Browse with BandiView", "用 BandiView", "使用 BandiView" }, BadComponents = new [] { "BandiViewShell", "BandiViewExt", "BandiViewShellExt", "BandizipShellext", "BandizipShell" } },
            new VendorRule { Name = "国产压缩/看图工具", Snark = "压缩包还没打开，右键先被挤爆了。", Boost = 12, Patterns = new [] { "KuaiZip", "快压", "Kuaizip", "HaoZip", "好压", "2345Zip", "360压缩", "360zip", "2345Pic", "2345看图王", "XnViewShell", "KanPic", "看图王", "极速看图", "JisuPic" }, BadComponents = new [] { "KuaiZip", "Kuaizip", "HaoZip", "2345Zip", "360zip", "2345Pic" } },
            new VendorRule { Name = "国产浏览器/导航", Snark = "浏览器自己跑就行，别把下载、主页和启动项全包了。", Boost = 16, Patterns = new [] { "SogouExplorer", "搜狗高速浏览器", "QQBrowser", "360se", "360Chrome", "2345Explorer", "2345Browser", "Liebao", "猎豹浏览器", "CheetahBrowser", "Maxthon", "傲游", "UCBrowser", "UCBrowser", "TheWorld", "世界之窗", "BaiduBrowser", "百度浏览器" }, BadComponents = new [] { "SogouExplorer", "QQBrowser", "2345Explorer", "CheetahBrowser", "UCService", "BaiduBrowser" } },
            new VendorRule { Name = "Flash 中国特供组件", Snark = "Flash 都退役了，助手还想在后台上班。", Boost = 22, Patterns = new [] { "FlashHelperService", "Flash Center", "FlashCenter", "Flash大厅", "FlashHelper", "FlashRepair", "FlashService", "flash.cn" }, BadComponents = new [] { "FlashHelperService", "FlashCenter", "FlashHelper" } },
            new VendorRule { Name = "手机助手/设备助手", Snark = "连一次手机，后台服务倒是记住一辈子。", Boost = 12, Patterns = new [] { "i4Tools", "爱思助手", "Aisi", "PP助手", "PPAssistant", "91助手", "91Assistant", "Wandoujia", "豌豆荚", "BaiduMobile", "TencentMobileManager", "应用宝", "HiSuite", "华为手机助手", "MiPhoneAssistant", "小米助手" }, BadComponents = new [] { "i4Tools", "PPAssistant", "91Assistant", "Wandoujia", "TencentMobileManager" } },
            new VendorRule { Name = "国产影音/游戏大厅", Snark = "看个视频玩个游戏，不需要抢文件关联和开机席位。", Boost = 10, Patterns = new [] { "iQIYI", "爱奇艺", "Qiyi", "Youku", "优酷", "Kugou", "酷狗", "Kuwo", "酷我", "PPTV", "暴风", "Baofeng", "QQLive", "TencentVideo", "腾讯视频", "XunleiMedia", "Bilibili", "芒果TV", "MangoTV", "WeGame", "SteamChina" }, BadComponents = new [] { "iQIYI", "Qiyi", "Youku", "Kugou", "Kuwo", "PPTV", "Baofeng", "QQLive", "TencentVideo" } },
            new VendorRule { Name = "PDF/办公捆绑工具", Snark = "读个 PDF，也别顺手接管全系统打开方式。", Boost = 10, Patterns = new [] { "JisuPDF", "极速PDF", "SwiftPDF", "迅捷PDF", "Foxit", "福昕", "CAJViewer", "PDFReader", "PDFSuite", "PDFMaster", "嗨格式", "HiFormat" }, BadComponents = new [] { "JisuPDF", "SwiftPDF", "PDFMaster", "HiFormat" } },
            new VendorRule { Name = "预装管家/厂商助手", Snark = "出厂自带不等于可以偷偷常驻。", Boost = 8, Patterns = new [] { "LenovoUtility", "LenovoVantage", "联想电脑管家", "LenovoPcManager", "Huawei PC Manager", "华为电脑管家", "HonorPCManager", "荣耀电脑管家", "MiService", "小米电脑管家", "MyASUS", "华硕电脑管家", "AcerCare", "Dell SupportAssist" }, BadComponents = new [] { "LenovoPcManager", "Huawei PC Manager", "HonorPCManager", "MiService", "SupportAssist" } },
            new VendorRule { Name = "弹窗广告/推广组件", Snark = "关掉没一会儿又弹，这类小广告最会装死。", Boost = 22, BehaviorOnly = true, Patterns = new [] { "SogouNews", "SogouPopup", "SogouTips", "SogouAd", "SogouInputPop", "2345MiniPage", "MiniNews", "HotNews", "NewsPop", "PopNews", "PopWnd", "AdPop", "AdService", "AdPush", "WpsNotify", "KNotify", "BaiduTips", "BaiduNews", "QQBrowserMini", "KugouTips", "KuwoNews", "QiyiNews", "YoukuNews", "LuDaShiNews", "MasterLuMini", "DriverGeniusNews", "KuaiZipNews", "HaoZipMiniPage", "今日热点", "每日热点", "热点资讯", "迷你页", "推荐弹窗", "广告弹窗" }, BadComponents = new [] { "SogouNews", "SogouPopup", "2345MiniPage", "AdPop", "AdService", "WpsNotify", "BaiduTips", "LuDaShiNews", "KuaiZipNews" } },
            new VendorRule { Name = "守护/自动恢复组件", Snark = "你关它一次，它守护进程能把自己续上三回。", Boost = 20, BehaviorOnly = true, Patterns = new [] { "QHWatchdog", "QHProtected", "QHActiveDefense", "SGImeGuard", "SogouImeBroker", "XLServicePlatform", "ThunderPlatform", "BaiduYunDetect", "YunDetectService", "BaiduNetdiskUtility", "QQProtect", "QQPCRTP", "2345Protect", "2345Svc", "KSafeSvc", "KWatch", "LdsDaemon", "LdsSvc", "FlashHelperService", "FlashCenter", "DriverGeniusDaemon", "DTLService", "LuDaShiDaemon" }, BadComponents = new [] { "QHWatchdog", "QHProtected", "SGImeGuard", "XLServicePlatform", "BaiduYunDetect", "QQProtect", "2345Protect", "KSafeSvc", "LdsDaemon", "FlashHelperService" } }
        };

        private sealed class CandidateScore
        {
            public VendorRule Rule;
            public int Score;
            public bool Strong;
            public readonly HashSet<string> Sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Reasons = new List<string>();
        }

        private sealed class FileIdentity
        {
            public string Path;
            public string Company;
            public string Product;
            public string Description;
            public string Signer;
            public bool SignatureValid;
        }

        private sealed class MsiIdentity
        {
            public string ProductName;
            public string Publisher;
            public string InstallLocation;
            public string LocalPackage;
        }

        private sealed class InstalledOwner
        {
            public string Root;
            public string Publisher;
            public string ProductName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint StructSize;
            [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
            public IntPtr FileHandle;
            public IntPtr KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustData
        {
            public uint StructSize;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInfo;
            public uint StateAction;
            public IntPtr StateData;
            [MarshalAs(UnmanagedType.LPWStr)] public string UrlReference;
            public uint ProviderFlags;
            public uint UiContext;
        }

        private static readonly object IdentityCacheGate = new object();
        private static readonly Dictionary<string, FileIdentity> FileIdentityCache = new Dictionary<string, FileIdentity>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, MsiIdentity> MsiIdentityCache = new Dictionary<string, MsiIdentity>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<InstalledOwner> InstalledOwners = new List<InstalledOwner>();
        private static bool InstalledOwnersLoaded;
        private static readonly HashSet<string> SystemHosts = new HashSet<string>(new string[]
        {
            "msiexec.exe", "rundll32.exe", "regsvr32.exe", "svchost.exe", "explorer.exe", "cmd.exe",
            "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe"
        }, StringComparer.OrdinalIgnoreCase);

        private static readonly Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId, IntPtr trustData);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern uint MsiGetProductInfo(string product, string property, StringBuilder value, ref int length);

        public static VendorIdentityResult ResolveIdentity(VendorEvidence evidence)
        {
            if (evidence == null) evidence = new VendorEvidence();
            EnrichEvidence(evidence);
            List<CandidateScore> candidates = new List<CandidateScore>();
            foreach (VendorRule rule in Vendors)
            {
                CandidateScore candidate = ScoreRule(rule, evidence);
                if (candidate.Score > 0) candidates.Add(candidate);
            }
            candidates = candidates.Where(delegate(CandidateScore item) { return !item.Rule.BehaviorOnly; }).ToList();
            candidates = candidates.OrderByDescending(delegate(CandidateScore item) { return item.Strong; })
                .ThenByDescending(delegate(CandidateScore item) { return item.Score; }).ToList();
            if (candidates.Count == 0)
            {
                return UnknownIdentity(false, "没有可信厂商证据");
            }

            CandidateScore best = candidates[0];
            bool confirmed = best.Strong || (best.Score >= 70 && best.Sources.Count >= 2);
            CandidateScore conflict = candidates.Skip(1).FirstOrDefault(delegate(CandidateScore item)
            {
                bool otherConfirmed = item.Strong || (item.Score >= 70 && item.Sources.Count >= 2);
                if (!otherConfirmed) return false;
                if (best.Strong && item.Strong) return true;
                return Math.Abs(best.Score - item.Score) < 25;
            });
            if (conflict != null)
            {
                return UnknownIdentity(true, "强证据冲突：" + best.Rule.Name + " / " + conflict.Rule.Name);
            }
            if (!confirmed)
            {
                return UnknownIdentity(false, "证据不足：" + string.Join("，", best.Reasons.Take(3).ToArray()));
            }
            return new VendorIdentityResult
            {
                Vendor = best.Rule.Name,
                Confidence = Math.Min(100, best.Strong ? Math.Max(95, best.Score) : best.Score),
                Confirmed = true,
                Conflicted = false,
                EvidenceSummary = string.Join("，", best.Reasons.Distinct().Take(5).ToArray())
            };
        }

        public static bool HasBadComponent(VendorEvidence evidence, VendorIdentityResult identity)
        {
            if (evidence == null || identity == null || !identity.Confirmed) return false;
            VendorRule rule = Vendors.FirstOrDefault(delegate(VendorRule item) { return item.Name == identity.Vendor; });
            if (rule == null) return false;
            IEnumerable<string> values = evidence.HumanTexts.Concat(evidence.ProductNames).Concat(evidence.TechnicalIdentifiers)
                .Concat(evidence.FilePaths.Select(delegate(string value) { return SafePathFileName(value); }));
            foreach (string value in values)
            {
                foreach (string pattern in rule.BadComponents)
                {
                    if (SafePatternMatch(value, pattern, true)) return true;
                }
            }
            return false;
        }

        public static int VendorBoost(VendorIdentityResult identity, bool badComponent)
        {
            if (identity == null || !identity.Confirmed) return 0;
            VendorRule rule = Vendors.FirstOrDefault(delegate(VendorRule item) { return item.Name == identity.Vendor; });
            if (rule == null) return 0;
            return 35 + rule.Boost + (badComponent ? 30 : 0);
        }

        private static CandidateScore ScoreRule(VendorRule rule, VendorEvidence evidence)
        {
            CandidateScore candidate = new CandidateScore { Rule = rule };
            ScoreValues(candidate, rule, evidence.Publishers, "Publisher", 60, false, false);
            ScoreValues(candidate, rule, evidence.ProductNames, "Product", 45, false, false);
            ScoreValues(candidate, rule, evidence.HumanTexts, "Human", 40, false, false);
            ScoreValues(candidate, rule, evidence.TechnicalIdentifiers, "Technical", 40, true, false);
            ScoreValues(candidate, rule, evidence.FilePaths, "Path", 30, true, false);
            foreach (string path in evidence.FilePaths)
            {
                FileIdentity file = GetFileIdentity(path);
                if (file == null) continue;
                if (file.SignatureValid) ScoreValues(candidate, rule, new string[] { file.Signer }, "Signature:" + file.Path, 100, false, true);
                ScoreValues(candidate, rule, new string[] { file.Company }, "Company:" + file.Path, 60, false, false);
                ScoreValues(candidate, rule, new string[] { file.Product, file.Description }, "FileProduct:" + file.Path, 45, false, false);
            }
            return candidate;
        }

        private static void ScoreValues(CandidateScore candidate, VendorRule rule, IEnumerable<string> values, string source, int score, bool technical, bool strong)
        {
            if (values == null) return;
            int index = 0;
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value)) { index++; continue; }
                string pattern = MatchingPattern(rule, value, technical);
                if (!string.IsNullOrEmpty(pattern))
                {
                    string sourceKey = source + ":" + index;
                    if (candidate.Sources.Add(sourceKey)) candidate.Score += score;
                    candidate.Strong = candidate.Strong || strong;
                    candidate.Reasons.Add(source.Split(':')[0] + "=" + pattern);
                }
                index++;
            }
        }

        private static string MatchingPattern(VendorRule rule, string value, bool technical)
        {
            foreach (string pattern in rule.Patterns)
            {
                if (technical && pattern.Length < 5 && pattern.All(delegate(char c) { return c < 128; })) continue;
                bool distinctive = rule.BadComponents.Any(delegate(string item) { return item.Equals(pattern, StringComparison.OrdinalIgnoreCase); });
                if (SafePatternMatch(value, pattern, technical && !distinctive)) return pattern;
            }
            return null;
        }

        private static bool SafePatternMatch(string text, string pattern, bool technical)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(pattern)) return false;
            int start = 0;
            while (true)
            {
                int index = text.IndexOf(pattern, start, StringComparison.OrdinalIgnoreCase);
                if (index < 0) return false;
                bool asciiAlphaNumeric = pattern.All(delegate(char c) { return c < 128 && char.IsLetterOrDigit(c); });
                bool boundaryRequired = asciiAlphaNumeric && (technical || pattern.Length <= 4 || pattern.All(char.IsDigit));
                if (!boundaryRequired) return true;
                int end = index + pattern.Length;
                bool leftBoundary = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
                bool rightBoundary = end >= text.Length || !char.IsLetterOrDigit(text[end]);
                if (leftBoundary && rightBoundary) return true;
                start = index + 1;
            }
        }

        private static void EnrichEvidence(VendorEvidence evidence)
        {
            foreach (string command in evidence.Commands.ToArray())
            {
                string file = ExtractTargetFile(command);
                if (!string.IsNullOrEmpty(file)) evidence.AddFile(file);
                string productCode = ExtractProductCode(command);
                if (!string.IsNullOrEmpty(productCode)) evidence.AddMsi(productCode);
            }
            foreach (string value in evidence.MsiProductCodes.ToArray())
            {
                string productCode = ExtractProductCode(value);
                if (string.IsNullOrEmpty(productCode)) continue;
                MsiIdentity msi = GetMsiIdentity(productCode);
                if (msi == null) continue;
                evidence.AddPublisher(msi.Publisher).AddProduct(msi.ProductName).AddFile(msi.LocalPackage);
                if (!string.IsNullOrWhiteSpace(msi.InstallLocation)) evidence.AddFile(msi.InstallLocation);
            }
            foreach (string value in evidence.FilePaths.ToArray())
            {
                string file = NormalizeCandidateFile(value);
                if (!string.IsNullOrEmpty(file)) evidence.AddFile(file);
            }
            EnrichInstalledOwnership(evidence);
        }

        private static void EnrichInstalledOwnership(VendorEvidence evidence)
        {
            EnsureInstalledOwners();
            foreach (string value in evidence.FilePaths.ToArray())
            {
                string normalized;
                try { normalized = Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim().Trim('\"'))).TrimEnd('\\') + "\\"; }
                catch { continue; }
                List<InstalledOwner> owners;
                lock (IdentityCacheGate) owners = InstalledOwners.ToList();
                foreach (InstalledOwner owner in owners)
                {
                    if (!normalized.StartsWith(owner.Root, StringComparison.OrdinalIgnoreCase)) continue;
                    evidence.AddPublisher(owner.Publisher).AddProduct(owner.ProductName);
                }
            }
        }

        private static void EnsureInstalledOwners()
        {
            lock (IdentityCacheGate)
            {
                if (InstalledOwnersLoaded) return;
            }
            List<InstalledOwner> loaded = new List<InstalledOwner>();
            foreach (RegistryHive hive in new RegistryHive[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                foreach (RegistryView view in new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    try
                    {
                        using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                        {
                            if (root == null) continue;
                            foreach (string childName in root.GetSubKeyNames())
                            {
                                try
                                {
                                    using (RegistryKey child = root.OpenSubKey(childName))
                                    {
                                        if (child == null) continue;
                                        string product = Convert.ToString(child.GetValue("DisplayName", ""));
                                        string publisher = Convert.ToString(child.GetValue("Publisher", ""));
                                        string installLocation = Convert.ToString(child.GetValue("InstallLocation", ""));
                                        string displayIcon = Convert.ToString(child.GetValue("DisplayIcon", ""));
                                        string ownerRoot = NormalizeInstallRoot(installLocation, displayIcon);
                                        if (string.IsNullOrEmpty(ownerRoot) || (string.IsNullOrWhiteSpace(product) && string.IsNullOrWhiteSpace(publisher))) continue;
                                        loaded.Add(new InstalledOwner { Root = ownerRoot, Publisher = publisher, ProductName = product });
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            }
            lock (IdentityCacheGate)
            {
                if (InstalledOwnersLoaded) return;
                foreach (InstalledOwner owner in loaded.OrderByDescending(delegate(InstalledOwner item) { return item.Root.Length; }))
                {
                    if (!InstalledOwners.Any(delegate(InstalledOwner item) { return item.Root.Equals(owner.Root, StringComparison.OrdinalIgnoreCase) && item.ProductName.Equals(owner.ProductName, StringComparison.OrdinalIgnoreCase); }))
                        InstalledOwners.Add(owner);
                }
                InstalledOwnersLoaded = true;
            }
        }

        private static string NormalizeInstallRoot(string installLocation, string displayIcon)
        {
            string value = installLocation;
            if (string.IsNullOrWhiteSpace(value))
            {
                string icon = NormalizeCandidateFile(displayIcon);
                if (!string.IsNullOrEmpty(icon)) value = Path.GetDirectoryName(icon);
            }
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim().Trim('\"'))).TrimEnd('\\') + "\\"; }
            catch { return string.Empty; }
        }

        private static string ExtractTargetFile(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return string.Empty;
            string expanded = Environment.ExpandEnvironmentVariables(command.Trim());
            string first = ExtractFirstPath(expanded);
            string host = SafePathFileName(first);
            if (!SystemHosts.Contains(host)) return first;
            if (host.Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase) || host.Equals("regsvr32.exe", StringComparison.OrdinalIgnoreCase))
            {
                string remainder = expanded.Substring(Math.Min(expanded.Length, expanded.IndexOf(first, StringComparison.OrdinalIgnoreCase) + first.Length)).Trim().TrimStart(',');
                string target = ExtractFirstPath(remainder);
                int comma = target.IndexOf(',');
                return comma > 0 ? target.Substring(0, comma) : target;
            }
            return string.Empty;
        }

        private static string ExtractFirstPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            value = value.Trim();
            if (value.StartsWith("\"", StringComparison.Ordinal))
            {
                int close = value.IndexOf('\"', 1);
                if (close > 1) return value.Substring(1, close - 1);
            }
            int exe = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe >= 0) return value.Substring(0, exe + 4).Trim().Trim('\"');
            int dll = value.IndexOf(".dll", StringComparison.OrdinalIgnoreCase);
            if (dll >= 0) return value.Substring(0, dll + 4).Trim().Trim('\"');
            int comma = value.IndexOf(',');
            if (comma > 0) return value.Substring(0, comma).Trim().Trim('\"');
            return value.Trim('\"');
        }

        private static string NormalizeCandidateFile(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string path = ExtractFirstPath(Environment.ExpandEnvironmentVariables(value));
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try
            {
                if (Directory.Exists(path)) return string.Empty;
                return Path.GetFullPath(path);
            }
            catch { return string.Empty; }
        }

        private static string SafePathFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            try { return Path.GetFileName(value.Trim().Trim('"')); }
            catch { return string.Empty; }
        }

        private static string ExtractProductCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            int start = value.IndexOf('{');
            while (start >= 0)
            {
                int end = value.IndexOf('}', start + 1);
                if (end < 0) return string.Empty;
                string candidate = value.Substring(start, end - start + 1);
                Guid parsed;
                if (Guid.TryParse(candidate, out parsed)) return parsed.ToString("B").ToUpperInvariant();
                start = value.IndexOf('{', end + 1);
            }
            Guid direct;
            return Guid.TryParse(value.Trim(), out direct) ? direct.ToString("B").ToUpperInvariant() : string.Empty;
        }

        private static FileIdentity GetFileIdentity(string value)
        {
            string path = NormalizeCandidateFile(value);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            lock (IdentityCacheGate)
            {
                FileIdentity cached;
                if (FileIdentityCache.TryGetValue(path, out cached)) return cached;
            }
            FileIdentity identity = new FileIdentity { Path = path };
            try
            {
                FileVersionInfo version = FileVersionInfo.GetVersionInfo(path);
                identity.Company = version.CompanyName;
                identity.Product = version.ProductName;
                identity.Description = version.FileDescription;
            }
            catch { }
            identity.SignatureValid = IsTrustedFile(path);
            if (identity.SignatureValid)
            {
                try
                {
                    using (X509Certificate2 certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)))
                    {
                        identity.Signer = certificate.Subject;
                    }
                }
                catch { identity.SignatureValid = false; }
            }
            lock (IdentityCacheGate) FileIdentityCache[path] = identity;
            return identity;
        }

        private static bool IsTrustedFile(string path)
        {
            IntPtr filePointer = IntPtr.Zero;
            IntPtr dataPointer = IntPtr.Zero;
            try
            {
                WinTrustFileInfo file = new WinTrustFileInfo
                {
                    StructSize = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo)),
                    FilePath = path,
                    FileHandle = IntPtr.Zero,
                    KnownSubject = IntPtr.Zero
                };
                filePointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustFileInfo)));
                Marshal.StructureToPtr(file, filePointer, false);
                WinTrustData data = new WinTrustData
                {
                    StructSize = (uint)Marshal.SizeOf(typeof(WinTrustData)),
                    UiChoice = 2,
                    RevocationChecks = 0,
                    UnionChoice = 1,
                    FileInfo = filePointer,
                    StateAction = 0,
                    ProviderFlags = 0x00001000,
                    UiContext = 0
                };
                dataPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustData)));
                Marshal.StructureToPtr(data, dataPointer, false);
                return WinVerifyTrust(new IntPtr(-1), GenericVerifyV2, dataPointer) == 0;
            }
            catch { return false; }
            finally
            {
                if (dataPointer != IntPtr.Zero) Marshal.FreeHGlobal(dataPointer);
                if (filePointer != IntPtr.Zero) Marshal.FreeHGlobal(filePointer);
            }
        }

        private static MsiIdentity GetMsiIdentity(string productCode)
        {
            lock (IdentityCacheGate)
            {
                MsiIdentity cached;
                if (MsiIdentityCache.TryGetValue(productCode, out cached)) return cached;
            }
            MsiIdentity identity = new MsiIdentity
            {
                ProductName = MsiProperty(productCode, "ProductName"),
                Publisher = MsiProperty(productCode, "Publisher"),
                InstallLocation = MsiProperty(productCode, "InstallLocation"),
                LocalPackage = MsiProperty(productCode, "LocalPackage")
            };
            lock (IdentityCacheGate) MsiIdentityCache[productCode] = identity;
            return identity;
        }

        private static string MsiProperty(string productCode, string property)
        {
            try
            {
                int length = 0;
                uint first = MsiGetProductInfo(productCode, property, null, ref length);
                if (first != 0 && first != 234) return string.Empty;
                StringBuilder value = new StringBuilder(length + 1);
                uint result = MsiGetProductInfo(productCode, property, value, ref length);
                return result == 0 ? value.ToString() : string.Empty;
            }
            catch { return string.Empty; }
        }

        private static VendorIdentityResult UnknownIdentity(bool conflicted, string reason)
        {
            return new VendorIdentityResult { Vendor = "未知第三方", Confidence = 0, Confirmed = false, Conflicted = conflicted, EvidenceSummary = reason };
        }

        public static List<string> RunIdentitySelfTests()
        {
            List<string> failures = new List<string>();
            AssertUnknown(failures, "Corel GUID 不得命中 2345", new VendorEvidence()
                .AddHuman("CorelDRAW Graphics Suite 2021 - IPM Content BR (x64)")
                .AddPublisher("Corel Corporation").AddMsi("{3D6825D1-5843-4585-B915-A9F234554C2C}")
                .AddOpaque(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{3D6825D1-5843-4585-B915-A9F234554C2C}"));
            AssertUnknown(failures, "裸 2345 不能确认厂商", new VendorEvidence().AddHuman("2345").AddTechnical("A9F234554C2C"));
            AssertUnknown(failures, "系统 MSI 宿主不能成为厂商证据", new VendorEvidence()
                .AddCommand(@"C:\Windows\System32\msiexec.exe /I{3D6825D1-5843-4585-B915-A9F234554C2C}")
                .AddOpaque("{3D6825D1-5843-4585-B915-A9F234554C2C}"));
            AssertUnknown(failures, "Thunderbird 不能命中迅雷", new VendorEvidence()
                .AddHuman("Mozilla Thunderbird").AddTechnical("Thunderbird").AddFile(@"C:\Program Files\Mozilla Thunderbird\thunderbird.exe"));
            AssertUnknown(failures, "普通 TBS/XMP/KAV/CAJ 缩写不能确认厂商", new VendorEvidence()
                .AddHuman("TBS XMP KAV CAJ").AddTechnical("TBS_XMP_KAV_CAJ"));

            VendorIdentityResult sogou = ResolveIdentity(new VendorEvidence().AddHuman("搜狗输入法").AddPublisher("Sogou.com").AddProduct("Sogou Input Method"));
            if (!sogou.Confirmed || sogou.Vendor != "搜狗") failures.Add("明确 Publisher+产品名未识别为搜狗");
            VendorIdentityResult sogouPopup = ResolveIdentity(new VendorEvidence().AddHuman("CodexRogueCleanerTest_SogouInputPop").AddCommand(@"C:\CodexRogueCleanerTest\Sogou\SogouInputPop.exe"));
            if (!sogouPopup.Confirmed || sogouPopup.Vendor != "搜狗") failures.Add("搜狗弹窗组件被通用行为标签阻断厂商识别");
            AssertUnknown(failures, "通用弹窗行为不能冒充厂商", new VendorEvidence().AddHuman("HotNews").AddCommand(@"C:\Unknown\HotNews.exe"));

            VendorIdentityResult conflict = ResolveIdentity(new VendorEvidence()
                .AddPublisher("Sogou.com", "Thunder Network Technologies")
                .AddProduct("Sogou Input Method", "Xunlei Thunder Download"));
            if (!conflict.Conflicted || conflict.Confirmed) failures.Add("相互冲突的强组合证据未被阻断");

            foreach (VendorRule rule in Vendors)
            {
                foreach (string pattern in rule.Patterns)
                {
                    VendorIdentityResult opaque = ResolveIdentity(new VendorEvidence()
                        .AddOpaque("GUID-{A9F" + pattern + "55C2C}", @"HKLM\Software\Classes\" + pattern));
                    if (opaque.Confirmed) failures.Add("不透明字段误命中：" + rule.Name + " / " + pattern);

                    VendorIdentityResult pathOnly = ResolveIdentity(new VendorEvidence()
                        .AddFile(@"C:\Unrelated\A9F" + pattern + @"55C2C\tool.exe"));
                    if (pathOnly.Confirmed) failures.Add("单一路径片段误命中：" + rule.Name + " / " + pattern);
                }
            }
            return failures;
        }

        private static void AssertUnknown(List<string> failures, string name, VendorEvidence evidence)
        {
            VendorIdentityResult result = ResolveIdentity(evidence);
            if (result.Confirmed || result.Vendor != "未知第三方") failures.Add(name + "：实际为 " + result.Vendor + "，" + result.EvidenceSummary);
        }

        public static string ResolveVendor(string text)
        {
            return ResolveIdentity(new VendorEvidence().AddHuman(text)).Vendor;
        }

        public static int VendorBoost(string text)
        {
            VendorIdentityResult identity = ResolveIdentity(new VendorEvidence().AddHuman(text));
            return VendorBoost(identity, false);
        }

        public static bool IsKnownVendor(string text)
        {
            return ResolveIdentity(new VendorEvidence().AddHuman(text)).Confirmed;
        }

        public static bool HasBadComponent(string text)
        {
            VendorEvidence evidence = new VendorEvidence().AddHuman(text).AddTechnical(text);
            VendorIdentityResult identity = ResolveIdentity(evidence);
            return HasBadComponent(evidence, identity);
        }
    }

    internal static class RegistryHelper
    {
#if VALIDATION
        internal static Func<ActionTarget, bool, Exception> TestOpenFailureInjector;
#endif

        public static string NativePath(ActionTarget target)
        {
            string hive = target.Hive == "HKLM" ? "HKLM" : "HKCU";
            return hive + "\\" + target.SubKey;
        }

        public static RegistryKey OpenBase(string hive, string view, bool writable)
        {
            RegistryHive h = hive == "HKLM" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
            RegistryView v = RegistryView.Default;
            if (view == "Registry64") v = RegistryView.Registry64;
            if (view == "Registry32") v = RegistryView.Registry32;
            return RegistryKey.OpenBaseKey(h, v);
        }

        public static RegistryKey OpenSubKey(ActionTarget target, bool writable)
        {
#if VALIDATION
            Func<ActionTarget, bool, Exception> injector = TestOpenFailureInjector;
            if (injector != null)
            {
                Exception injected = injector(target, writable);
                if (injected != null) throw injected;
            }
#endif
            using (RegistryKey root = OpenBase(target.Hive, target.View, writable))
            {
                return root.OpenSubKey(target.SubKey, writable);
            }
        }

        public static bool KeyExists(ActionTarget target)
        {
            using (RegistryKey key = OpenSubKey(target, false))
            {
                return key != null;
            }
        }

        public static bool ValueExists(ActionTarget target)
        {
            using (RegistryKey key = OpenSubKey(target, false))
            {
                if (key == null) return false;
                return key.GetValueNames().Any(delegate(string n) { return string.Equals(n, target.ValueName ?? string.Empty, StringComparison.OrdinalIgnoreCase); });
            }
        }

        public static void DeleteKey(ActionTarget target)
        {
            using (RegistryKey root = OpenBase(target.Hive, target.View, true))
            {
                root.DeleteSubKeyTree(target.SubKey, false);
            }
        }

        public static void DeleteValue(ActionTarget target)
        {
            using (RegistryKey key = OpenSubKey(target, true))
            {
                if (key != null) key.DeleteValue(target.ValueName ?? string.Empty, false);
            }
        }
    }

    internal static class WindowsTaskApi
    {
        public static bool TryGetEnabled(string taskPath, out bool enabled)
        {
            enabled = false;
            try { dynamic task = GetTask(taskPath); enabled = Convert.ToBoolean(task.Enabled); return true; }
            catch { return false; }
        }

        public static bool SetEnabled(string taskPath, bool enabled)
        {
            dynamic task = GetTask(taskPath); task.Enabled = enabled; bool actual; return TryGetEnabled(taskPath, out actual) && actual == enabled;
        }

        public static string GetXml(string taskPath)
        {
            try { dynamic task = GetTask(taskPath); return Convert.ToString(task.Xml); }
            catch { return string.Empty; }
        }

        public static bool RegisterFromXml(string taskPath, string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return false; string folderPath, name; Split(taskPath, out folderPath, out name);
            dynamic service = Connect(); dynamic folder = service.GetFolder(folderPath);
            folder.RegisterTask(name, xml, 6, null, null, 3, null);
            bool enabled; return TryGetEnabled(taskPath, out enabled);
        }

        public static bool CreateValidationTask(string taskPath, string executable)
        {
            string folderPath, name; Split(taskPath, out folderPath, out name); dynamic service = Connect(); dynamic folder = service.GetFolder(folderPath); dynamic definition = service.NewTask(0);
            definition.RegistrationInfo.Description = "RogueCleaner validation task"; definition.Settings.Enabled = true; definition.Settings.StartWhenAvailable = true;
            definition.Principal.UserId = WindowsIdentity.GetCurrent().Name; definition.Principal.LogonType = 3; definition.Principal.RunLevel = 1;
            dynamic trigger = definition.Triggers.Create(2); trigger.StartBoundary = DateTime.Now.AddMinutes(10).ToString("s"); trigger.DaysInterval = 1;
            dynamic action = definition.Actions.Create(0); action.Path = executable;
            folder.RegisterTaskDefinition(name, definition, 6, null, null, 3, null);
            bool enabled; return TryGetEnabled(taskPath, out enabled) && enabled;
        }

        public static bool Delete(string taskPath)
        {
            try { string folderPath, name; Split(taskPath, out folderPath, out name); dynamic service = Connect(); dynamic folder = service.GetFolder(folderPath); folder.DeleteTask(name, 0); bool enabled; return !TryGetEnabled(taskPath, out enabled); }
            catch { bool enabled; return !TryGetEnabled(taskPath, out enabled); }
        }

        private static dynamic GetTask(string taskPath) { string folderPath, name; Split(taskPath, out folderPath, out name); dynamic service = Connect(); dynamic folder = service.GetFolder(folderPath); return folder.GetTask(name); }
        private static dynamic Connect() { Type type = Type.GetTypeFromProgID("Schedule.Service"); if (type == null) throw new InvalidOperationException("系统未提供任务计划 COM 服务。"); dynamic service = Activator.CreateInstance(type); service.Connect(); return service; }
        private static void Split(string taskPath, out string folderPath, out string name) { string normalized = (taskPath ?? string.Empty).Trim(); if (!normalized.StartsWith("\\", StringComparison.Ordinal)) normalized = "\\" + normalized; int slash = normalized.LastIndexOf('\\'); folderPath = slash <= 0 ? "\\" : normalized.Substring(0, slash); name = normalized.Substring(slash + 1); if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("计划任务路径无效。"); }
    }

    internal sealed class ScannerEngine
    {
        private readonly object warningGate = new object();
        private readonly List<ScanWarning> warnings = new List<ScanWarning>();
        private readonly HashSet<string> warningKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public List<ScanWarning> Warnings
        {
            get
            {
                lock (warningGate) return new List<ScanWarning>(warnings);
            }
        }

        private static readonly string[] ContextRoots = new string[]
        {
            @"Software\Classes\*\shell",
            @"Software\Classes\*\shellex\ContextMenuHandlers",
            @"Software\Classes\AllFilesystemObjects\shell",
            @"Software\Classes\AllFilesystemObjects\shellex\ContextMenuHandlers",
            @"Software\Classes\Directory\shell",
            @"Software\Classes\Directory\shellex\ContextMenuHandlers",
            @"Software\Classes\Directory\Background\shell",
            @"Software\Classes\Directory\Background\shellex\ContextMenuHandlers",
            @"Software\Classes\Drive\shell",
            @"Software\Classes\Drive\shellex\ContextMenuHandlers",
            @"Software\Classes\Drive\shellex\DragDropHandlers",
            @"Software\Classes\Folder\shell",
            @"Software\Classes\Folder\shellex\ContextMenuHandlers",
            @"Software\Classes\Folder\shellex\DragDropHandlers",
            @"Software\Classes\DesktopBackground\shell",
            @"Software\Classes\DesktopBackground\shellex\ContextMenuHandlers",
            @"Software\Classes\lnkfile\shell",
            @"Software\Classes\lnkfile\shellex\ContextMenuHandlers",
            @"Software\Classes\exefile\shell",
            @"Software\Classes\exefile\shellex\ContextMenuHandlers",
            @"Software\Classes\Unknown\shell",
            @"Software\Classes\SystemFileAssociations\image\shell",
            @"Software\Classes\SystemFileAssociations\image\shellex\ContextMenuHandlers",
            @"Software\Classes\SystemFileAssociations\video\shell",
            @"Software\Classes\SystemFileAssociations\video\shellex\ContextMenuHandlers",
            @"Software\Classes\SystemFileAssociations\audio\shell",
            @"Software\Classes\SystemFileAssociations\audio\shellex\ContextMenuHandlers",
            @"Software\Classes\SystemFileAssociations\text\shell",
            @"Software\Classes\SystemFileAssociations\text\shellex\ContextMenuHandlers",
            @"Software\Classes\CompressedFolder\shell",
            @"Software\Classes\CompressedFolder\shellex\ContextMenuHandlers"
        };

        private static readonly string[] StartupRoots = new string[]
        {
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\RunOnce"
        };

        private static readonly string[] BrowserRoots = new string[]
        {
            @"Software\Google\Chrome\Extensions",
            @"Software\Microsoft\Edge\Extensions",
            @"Software\Google\Chrome\NativeMessagingHosts",
            @"Software\Microsoft\Edge\NativeMessagingHosts",
            @"Software\Mozilla\NativeMessagingHosts",
            @"Software\Policies\Google\Chrome\ExtensionInstallForcelist",
            @"Software\Policies\Microsoft\Edge\ExtensionInstallForcelist",
            @"Software\Policies\Google\Chrome\ExtensionSettings",
            @"Software\Policies\Microsoft\Edge\ExtensionSettings"
        };

        private static readonly string[] InstalledRoots = new string[]
        {
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
            @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        private static readonly string[] ExplorerNamespaceRoots = new string[]
        {
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\NetworkNeighborhood\NameSpace"
        };

        private static readonly string[] ExplorerNamespaceClsidRoots = new string[]
        {
            @"Software\Classes\CLSID"
        };

        private static readonly string[] FileExtensions = new string[]
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic", ".tif", ".tiff", ".svg", ".psd", ".ico",
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".mp3", ".flac", ".wav",
            ".zip", ".rar", ".7z", ".torrent", ".xlb", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx"
        };

        public List<Finding> ScanAll(IProgressSink sink)
        {
            lock (warningGate)
            {
                warnings.Clear();
                warningKeys.Clear();
            }
            List<Finding> all = new List<Finding>();
            object gate = new object();
            List<Action> scanners = new List<Action>();

            scanners.Add(delegate { RunScanner(all, gate, sink, "右键菜单", ScanContextMenus); });
            scanners.Add(delegate { RunScanner(all, gate, sink, "此电脑入口", ScanExplorerNamespaces); });
            scanners.Add(delegate { RunScanner(all, gate, sink, "网盘虚拟盘", ScanCloudVirtualDrives); });
            scanners.Add(delegate { RunScanner(all, gate, sink, "开机启动", ScanStartupRegistry); });
            scanners.Add(delegate { RunScanner(all, gate, sink, "启动文件夹", ScanStartupFolders); });
            scanners.Add(delegate { RunScanner(all, gate, sink, "后台服务", ScanServices); });
            scanners.Add(delegate { RunScanner(all, gate, sink, "浏览器插件", ScanBrowserExtensions); });
            scanners.Add(delegate { RunScanner(all, gate, sink, "文件关联", ScanFileAssociations); });
            scanners.Add(delegate { RunScanner(all, gate, sink, "计划任务", ScanScheduledTasks); });
            scanners.Add(delegate { RunScanner(all, gate, sink, "隐藏卸载入口", ScanHiddenInstalledComponents); });
            scanners.Add(delegate { RunScanner(all, gate, sink, "正在运行的弹窗/守护", ScanRunningAdAndGuardProcesses); });

            Parallel.Invoke(new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(2, Math.Min(4, Environment.ProcessorCount))
            }, scanners.ToArray());

            List<Finding> sorted = all
                .GroupBy(delegate(Finding f) { return f.Category + "|" + f.TechnicalLocation + "|" + f.UserVisibleName; })
                .Select(delegate(IGrouping<string, Finding> g) { return g.First(); })
                .OrderBy(delegate(Finding f) { return RiskRank(f.Risk); })
                .ThenBy(delegate(Finding f) { return f.Vendor; })
                .ThenBy(delegate(Finding f) { return f.Category; })
                .ThenBy(delegate(Finding f) { return f.UserVisibleName; })
                .ToList();
            for (int i = 0; i < sorted.Count; i++) sorted[i].Id = i + 1;
            return sorted;
        }

        private void RunScanner(List<Finding> all, object gate, IProgressSink sink, string stage, Func<List<Finding>> scanner)
        {
            try
            {
                AddRange(all, gate, sink, stage, scanner());
            }
            catch (SecurityException ex)
            {
                RecordWarning(stage, null, ex);
                if (sink != null) sink.Stage("扫描：" + stage + "，部分受保护位置无法读取，已继续");
            }
            catch (UnauthorizedAccessException ex)
            {
                RecordWarning(stage, null, ex);
                if (sink != null) sink.Stage("扫描：" + stage + "，部分受保护位置无法读取，已继续");
            }
        }

        private RegistryKey OpenForScan(ActionTarget target, string stage)
        {
            try
            {
                return RegistryHelper.OpenSubKey(target, false);
            }
            catch (SecurityException ex)
            {
                RecordWarning(stage, target, ex);
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                RecordWarning(stage, target, ex);
                return null;
            }
        }

        private void RecordWarning(string stage, ActionTarget target, Exception ex)
        {
            string location = target == null ? "未定位到具体子项" : RegistryHelper.NativePath(target) + (string.IsNullOrWhiteSpace(target.View) || target.View == "Default" ? string.Empty : " (" + target.View + ")");
            string key = stage + "|" + location + "|" + ex.GetType().FullName;
            lock (warningGate)
            {
                if (!warningKeys.Add(key)) return;
                warnings.Add(new ScanWarning
                {
                    Stage = stage,
                    TechnicalLocation = location,
                    ErrorType = ex.GetType().FullName,
                    Message = ex is SecurityException || ex is UnauthorizedAccessException
                        ? "访问被系统拒绝，已跳过该位置并继续扫描。"
                        : "读取该位置时发生异常，已跳过并继续扫描：" + ex.Message
                });
            }
        }

        private static void AddRange(List<Finding> all, object gate, IProgressSink sink, string stage, List<Finding> findings)
        {
            if (sink != null) sink.Stage("扫描：" + stage + "，发现 " + findings.Count + " 项");
            lock (gate)
            {
                foreach (Finding finding in findings)
                {
                    all.Add(finding);
                    if (sink != null) sink.Finding(finding);
                }
            }
        }

        private List<Finding> ScanContextMenus()
        {
            List<Finding> list = new List<Finding>();
            HashSet<string> actions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DataStore store = DataStore.CreateForExecutable(Application.ExecutablePath);
            ContextMenuInventory inventory = new ContextMenuDiscoveryService(store).Enumerate(false);
            MergeContextMenuWarnings(inventory.Warnings);
            foreach (ContextMenuEntry entry in inventory.Entries)
            {
                if (entry == null) continue;
                bool extension = string.Equals(entry.Type, "Shell 扩展", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(entry.Type, "现代右键扩展", StringComparison.OrdinalIgnoreCase);
                string clsidText = extension ? ResolveClsidRegistration(entry.Clsid) : string.Empty;
                string text = Join(entry.Name, entry.RawName, entry.DeclaredVendor, entry.Command, entry.Icon, entry.Clsid, clsidText, entry.Scene, entry.Scope, entry.SubKey);
                VendorEvidence evidence = new VendorEvidence().AddPublisher(entry.DeclaredVendor).AddHuman(entry.Name, entry.RawName)
                    .AddTechnical(entry.Clsid, clsidText).AddCommand(entry.Command, clsidText).AddFile(entry.Icon, entry.Command)
                    .AddOpaque(entry.Scene, entry.Scope, entry.SubKey);
                VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                ContextMenuDiagnosisDisposition disposition = ContextMenuDiagnosisPolicy.Classify(entry, identity);
                if (disposition == ContextMenuDiagnosisDisposition.Ignore) continue;
                bool badComponent = RuleCatalog.HasBadComponent(evidence, identity);
                string actionKey = extension && !string.IsNullOrWhiteSpace(entry.Clsid)
                    ? entry.Hive + "|" + entry.View + "|" + entry.Clsid
                    : entry.Id;
                if (!actions.Add(actionKey)) continue;

                ActionTarget target = new ActionTarget
                {
                    Hive = entry.Hive,
                    View = entry.View,
                    SubKey = entry.SubKey,
                    IconValue = entry.Icon,
                    PresentationCommand = entry.Command,
                    Clsid = entry.Clsid,
                    SourceSubKey = entry.SubKey
                };
                string title = string.IsNullOrWhiteSpace(entry.Name) ? "第三方软件右键插件" : entry.Name;
                if (disposition == ContextMenuDiagnosisDisposition.Governed)
                {
                    target.Kind = "ReportOnly";
                    Finding governed = NewFinding("已治理的右键插件", title, "这个右键插件仍有注册信息，但当前已经禁用。软件更新或重装后如果重新启用，下次扫描会再次列为可处理项。", target, text, 4, identity, badComponent);
                    governed.Status = "已治理";
                    governed.Risk = "低";
                    list.Add(governed);
                    continue;
                }
                if (disposition == ContextMenuDiagnosisDisposition.ReportOnly)
                {
                    target.Kind = "ReportOnly";
                    Finding readOnly = NewFinding("右键插件边界待确认", title, "检测到第三方右键插件，但缺少可安全禁用的组件编号。只提示，不删除注册信息。", target, text, 5, identity, badComponent);
                    readOnly.Risk = "低";
                    list.Add(readOnly);
                    continue;
                }
                if (disposition == ContextMenuDiagnosisDisposition.SystemProtected)
                {
                    target.Kind = "ReportOnly";
                    Finding protectedSystem = NewFinding("系统右键命令（保护）", title, "这是 Windows 系统自带的右键命令，命令路径指向系统目录。自动清理可能破坏右键菜单或系统设置，只提示不删除。", target, text, 5, identity, badComponent);
                    protectedSystem.Risk = "低";
                    list.Add(protectedSystem);
                    continue;
                }

                if (disposition == ContextMenuDiagnosisDisposition.ActionableExtension)
                {
                    target.Kind = "DisableShellExtension";
                    target.SubKey = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
                    target.ValueName = entry.Clsid;
                }
                else
                {
                    target.Kind = "DeleteRegistryKey";
                }
                string impact = "检测到“" + title + "”注入" + entry.Scene + "。只备份并禁用这个右键入口，不卸载“" + identity.Vendor + "”主程序；软件更新后若重新写回，下次扫描会再次发现。";
                list.Add(NewFinding("第三方右键插件", title, impact, target, text, 18, identity, badComponent));
            }
            return list;
        }

        private void MergeContextMenuWarnings(IEnumerable<ScanWarning> source)
        {
            if (source == null) return;
            lock (warningGate)
            {
                foreach (ScanWarning warning in source)
                {
                    if (warning == null) continue;
                    string key = "右键菜单|" + warning.TechnicalLocation + "|" + warning.ErrorType;
                    if (warningKeys.Add(key)) warnings.Add(warning);
                }
            }
        }

        private List<Finding> ScanStartupRegistry()
        {
            List<Finding> list = new List<Finding>();
            foreach (ActionTarget root in RegistryTargets(StartupRoots, true, true))
            {
                using (RegistryKey key = OpenForScan(root, "开机启动"))
                {
                    if (key == null) continue;
                    foreach (string valueName in SafeValueNames(key))
                    {
                        string value = Convert.ToString(key.GetValue(valueName, ""));
                        string text = Join(valueName, value, root.SubKey);
                        VendorEvidence evidence = new VendorEvidence().AddHuman(valueName).AddCommand(value).AddOpaque(root.SubKey);
                        VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                        if (!identity.Confirmed) continue;
                        ActionTarget target = CopyTarget(root);
                        target.Kind = "DeleteRegistryValue";
                        target.ValueName = valueName;
                        string title = FriendlyStartupTitle(text, valueName, value, identity.Vendor);
                        list.Add(NewFinding("开机启动", title, "开机后会自动启动：" + title, target, text, 28, identity, RuleCatalog.HasBadComponent(evidence, identity)));
                    }
                }
            }
            return list;
        }

        private List<Finding> ScanStartupFolders()
        {
            List<Finding> list = new List<Finding>();
            string[] folders = new string[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
            };
            foreach (string folder in folders)
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
                foreach (string file in Directory.GetFiles(folder))
                {
                    string shortcut = ResolveShortcutText(file);
                    string text = Join(file, shortcut);
                    VendorEvidence evidence = new VendorEvidence().AddHuman(Path.GetFileNameWithoutExtension(file)).AddCommand(shortcut).AddOpaque(file);
                    VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                    if (!identity.Confirmed) continue;
                    ActionTarget target = new ActionTarget { Kind = "MoveFileToBackup", FilePath = file };
                    list.Add(NewFinding("启动文件夹", Path.GetFileName(file), "开机后会从启动文件夹拉起：" + Join(Path.GetFileName(file), shortcut), target, text, 28, identity, RuleCatalog.HasBadComponent(evidence, identity)));
                }
            }
            return list;
        }

        private List<Finding> ScanBrowserExtensions()
        {
            List<Finding> list = new List<Finding>();
            foreach (ActionTarget root in RegistryTargets(BrowserRoots, true, true))
            {
                using (RegistryKey key = OpenForScan(root, "浏览器插件"))
                {
                    if (key == null) continue;
                    foreach (string valueName in SafeValueNames(key))
                    {
                        string value = Convert.ToString(key.GetValue(valueName, ""));
                        string text = Join(valueName, value, root.SubKey);
                        VendorEvidence evidence = new VendorEvidence().AddHuman(valueName).AddTechnical(valueName).AddCommand(value).AddFile(value).AddOpaque(root.SubKey);
                        VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                        if (!identity.Confirmed) continue;
                        ActionTarget target = CopyTarget(root);
                        target.Kind = "DeleteRegistryValue";
                        target.ValueName = valueName;
                        string title = FriendlyBrowserTitle(text, valueName, identity.Vendor);
                        list.Add(NewFinding("浏览器插件/外部宿主", title, "浏览器可能会加载：" + title, target, text, 35, identity, RuleCatalog.HasBadComponent(evidence, identity)));
                    }
                    foreach (string childName in SafeSubKeyNames(key))
                    {
                        ActionTarget target = CopyTarget(root);
                        target.Kind = "DeleteRegistryKey";
                        target.SubKey = root.SubKey + "\\" + childName;
                        string childDefault;
                        using (RegistryKey child = OpenForScan(target, "浏览器插件"))
                        {
                            childDefault = ReadString(child, "");
                        }
                        string text = Join(childName, childDefault, root.SubKey);
                        VendorEvidence evidence = new VendorEvidence().AddHuman(childName).AddTechnical(childName).AddCommand(childDefault).AddFile(childDefault).AddOpaque(root.SubKey);
                        VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                        if (!identity.Confirmed) continue;
                        string title = FriendlyBrowserTitle(text, childName, identity.Vendor);
                        list.Add(NewFinding("浏览器插件/外部宿主", title, "浏览器可能会加载：" + title, target, text, 35, identity, RuleCatalog.HasBadComponent(evidence, identity)));
                    }
                }
            }
            return list;
        }

        private List<Finding> ScanCloudVirtualDrives()
        {
            List<Finding> list = new List<Finding>();
            string[] tokens = new string[] { "网盘", "云盘", "netdisk", "cloud", "baidu", "quark", "aliyun", "onedrive", "dropbox", "115" };
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    string label = drive.IsReady ? drive.VolumeLabel : string.Empty;
                    string evidence = Join(drive.Name, label, drive.DriveFormat, drive.DriveType.ToString());
                    bool namedCloud = tokens.Any(delegate(string token) { return evidence.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0; });
                    if (!namedCloud) continue;
                    ActionTarget target = new ActionTarget { Kind = "ReportOnly", FilePath = drive.Name };
                    VendorIdentityResult identity = RuleCatalog.ResolveIdentity(new VendorEvidence().AddHuman(label).AddTechnical(evidence));
                    Finding finding = NewFinding("网盘虚拟盘（只读诊断）", string.IsNullOrWhiteSpace(label) ? drive.Name : label + "（" + drive.Name + "）", "检测到可能由网盘创建的盘符。仅展示诊断证据，不修改设备、驱动器、盘符或网盘客户端。", target, evidence, 5, identity, false);
                    finding.Status = "仅提示";
                    list.Add(finding);
                }
                catch (Exception ex) { Logger.Error("读取网盘虚拟盘信息失败", ex); }
            }
            return list;
        }

        private List<Finding> ScanExplorerNamespaces()
        {
            List<Finding> list = new List<Finding>();
            foreach (ActionTarget root in RegistryTargets(ExplorerNamespaceRoots, true, true))
            {
                using (RegistryKey key = OpenForScan(root, "此电脑入口"))
                {
                    if (key == null) continue;
                    foreach (string childName in SafeSubKeyNames(key))
                    {
                        ActionTarget target = CopyTarget(root);
                        target.Kind = "DeleteRegistryKey";
                        target.SubKey = root.SubKey + "\\" + childName;
                        using (RegistryKey child = OpenForScan(target, "此电脑入口"))
                        {
                            string display = ReadString(child, "");
                            string localized = ReadString(child, "LocalizedString");
                            string itemName = ReadString(child, "System.ItemNameDisplay");
                            string targetFolder = ReadString(child, "TargetFolderPath");
                            string clsidText = ResolveClsidRegistration(childName, display, localized, itemName);
                            string text = Join(childName, display, localized, itemName, targetFolder, ReadString(child, "CodexMarker"), clsidText, target.SubKey);
                            VendorEvidence evidence = new VendorEvidence().AddHuman(display, localized, itemName).AddTechnical(clsidText)
                                .AddCommand(clsidText).AddFile(targetFolder).AddOpaque(childName, target.SubKey);
                            VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                            if (!identity.Confirmed) continue;
                            string title = FriendlyExplorerNamespaceTitle(target.SubKey, childName, display, localized, itemName, clsidText);
                            list.Add(NewFinding("此电脑/资源管理器入口", title, "会在“此电脑”、资源管理器导航栏或网络位置里显示入口：" + title + "。清理只移除入口注册表，不卸载主程序。", target, text, 22, identity, RuleCatalog.HasBadComponent(evidence, identity)));
                        }
                    }
                }
            }

            foreach (ActionTarget root in RegistryTargets(ExplorerNamespaceClsidRoots, true, true))
            {
                using (RegistryKey key = OpenForScan(root, "此电脑入口"))
                {
                    if (key == null) continue;
                    foreach (string childName in SafeSubKeyNames(key))
                    {
                        ActionTarget clsidTarget = CopyTarget(root);
                        clsidTarget.SubKey = root.SubKey + "\\" + childName;
                        using (RegistryKey child = OpenForScan(clsidTarget, "此电脑入口"))
                        {
                            string pinned = ReadString(child, "System.IsPinnedToNameSpaceTree");
                            if (!IsTruthy(pinned)) continue;
                            string display = ReadString(child, "");
                            string localized = ReadString(child, "LocalizedString");
                            string itemName = ReadString(child, "System.ItemNameDisplay");
                            string infoTip = ReadString(child, "InfoTip");
                            string icon = ReadChildDefault(clsidTarget, "DefaultIcon");
                            string server = Join(ReadChildDefault(clsidTarget, "InprocServer32"), ReadChildDefault(clsidTarget, "LocalServer32"));
                            string targetFolder = ReadChildValue(clsidTarget, @"Instance\InitPropertyBag", "TargetFolderPath");
                            string text = Join(childName, display, localized, itemName, infoTip, pinned, icon, server, targetFolder, ReadString(child, "CodexMarker"), clsidTarget.SubKey);
                            VendorEvidence evidence = new VendorEvidence().AddHuman(display, localized, itemName, infoTip).AddTechnical(server)
                                .AddCommand(server).AddFile(icon, targetFolder).AddOpaque(childName, pinned, clsidTarget.SubKey);
                            VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                            if (!identity.Confirmed) continue;
                            ActionTarget valueTarget = CopyTarget(clsidTarget);
                            valueTarget.Kind = "DeleteRegistryValue";
                            valueTarget.ValueName = "System.IsPinnedToNameSpaceTree";
                            string title = FriendlyExplorerNamespaceTitle(clsidTarget.SubKey, childName, display, localized, itemName, text);
                            list.Add(NewFinding("此电脑/资源管理器入口", title, "会把入口固定到资源管理器导航栏或“此电脑”附近：" + title + "。清理只取消固定入口，不卸载主程序。", valueTarget, text, 18, identity, RuleCatalog.HasBadComponent(evidence, identity)));
                        }
                    }
                }
            }
            AddPackagedContextMenuRisks(list);
            return list;
        }

        private void AddPackagedContextMenuRisks(List<Finding> list)
        {
            try
            {
                DataStore store = DataStore.CreateForExecutable(Application.ExecutablePath);
                AdvancedMenuInventory inventory = new AdvancedMenuInventoryService(store).EnumeratePackagedOnly(false);
                foreach (AdvancedMenuEntry entry in inventory.Entries)
                {
                    string text = Join(entry.Name, entry.PackageName, entry.PublisherName, entry.FilePath, entry.ValueName, entry.ItemType, entry.Detail);
                    if ((entry.PublisherName ?? string.Empty).IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    VendorEvidence evidence = new VendorEvidence().AddHuman(entry.Name, entry.PackageName, entry.PublisherName)
                        .AddTechnical(entry.ValueName, entry.ItemType).AddFile(entry.FilePath).AddOpaque(entry.PackageName, entry.Detail);
                    VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                    bool badComponent = RuleCatalog.HasBadComponent(evidence, identity);
                    bool abnormalBehavior = LooksLikeAdOrGuard(text);
                    // 快速风险扫描不会启动动态标题/组件探针；空路径表示“本轮未解析”，不能当作文件缺失。
                    bool missingComponent = !string.IsNullOrWhiteSpace(entry.FilePath) && !File.Exists(entry.FilePath);
                    if (!badComponent && !abnormalBehavior && !missingComponent) continue;

                    ActionTarget target = new ActionTarget
                    {
                        Kind = identity.Confirmed && !missingComponent ? "DisableShellExtension" : "ReportOnly",
                        Hive = "HKCU",
                        View = entry.View,
                        SubKey = entry.SubKey,
                        ValueName = entry.ValueName,
                        SourceSubKey = "应用包：" + entry.PackageName,
                        FilePath = entry.FilePath,
                        PresentationCommand = entry.FilePath,
                        IconValue = entry.CommandIcon,
                        Clsid = entry.ValueName
                    };
                    string title = (string.IsNullOrWhiteSpace(entry.Name) ? entry.PackageName + " 动态右键扩展" : entry.Name);
                    string reason = missingComponent ? "应用包声明的右键组件文件缺失" : (badComponent ? "命中已知异常组件特征" : "命中弹窗、守护或推广行为特征");
                    Finding finding = NewFinding("Windows 11 右键菜单", title, reason + "。正常的打包右键菜单只在右键管理中显示，不会进入风险结果。", target, text, 16, identity, badComponent);
                    if (!identity.Confirmed || missingComponent) finding.Risk = "低";
                    list.Add(finding);
                }
            }
            catch (Exception ex)
            {
                RecordWarning("Windows 11 右键菜单", null, ex);
            }
        }

        private List<Finding> ScanHiddenInstalledComponents()
        {
            List<Finding> list = new List<Finding>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ActionTarget root in RegistryTargets(InstalledRoots, true, true))
            {
                using (RegistryKey key = OpenForScan(root, "隐藏卸载入口"))
                {
                    if (key == null) continue;
                    foreach (string childName in SafeSubKeyNames(key))
                    {
                        ActionTarget target = CopyTarget(root);
                        target.SubKey = root.SubKey + "\\" + childName;
                        using (RegistryKey child = OpenForScan(target, "隐藏卸载入口"))
                        {
                            if (child == null) continue;
                            string display = ReadString(child, "DisplayName");
                            string publisher = ReadString(child, "Publisher");
                            string installLocation = ReadString(child, "InstallLocation");
                            string displayIcon = ReadString(child, "DisplayIcon");
                            string uninstall = ReadString(child, "UninstallString");
                            string quietUninstall = ReadString(child, "QuietUninstallString");
                            string systemComponent = ReadString(child, "SystemComponent");
                            string noRemove = ReadString(child, "NoRemove");
                            string parentKey = ReadString(child, "ParentKeyName");
                            string releaseType = ReadString(child, "ReleaseType");
                            string text = Join(childName, display, publisher, installLocation, displayIcon, uninstall, quietUninstall, systemComponent, noRemove, parentKey, releaseType, target.SubKey);
                            VendorEvidence evidence = new VendorEvidence().AddPublisher(publisher).AddProduct(display)
                                .AddFile(installLocation, displayIcon).AddCommand(uninstall, quietUninstall).AddMsi(childName, uninstall, quietUninstall)
                                .AddOpaque(childName, systemComponent, noRemove, parentKey, releaseType, target.SubKey);
                            VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                            bool hidden = IsTruthy(systemComponent) ||
                                IsTruthy(noRemove) ||
                                string.IsNullOrWhiteSpace(display) ||
                                string.IsNullOrWhiteSpace(uninstall) ||
                                !string.IsNullOrWhiteSpace(parentKey);
                            string behaviorText = Join(display, publisher, SafePathFileName(installLocation), SafePathFileName(displayIcon));
                            bool adOrGuard = LooksLikeAdOrGuard(behaviorText);
                            bool badComponent = RuleCatalog.HasBadComponent(evidence, identity);
                            ProductRemovalDisposition disposition = ProductRemovalPolicy.Classify(display, childName, installLocation, displayIcon, uninstall, hidden, adOrGuard, badComponent);
                            if (disposition == ProductRemovalDisposition.Ignore) continue;
                            string name = string.IsNullOrWhiteSpace(display) ? childName : display;
                            string dedupeKey = Join(name, uninstall, installLocation);
                            if (!seen.Add(dedupeKey)) continue;
                            string reason = HiddenInstallReason(display, uninstall, systemComponent, noRemove, parentKey, hidden, adOrGuard, badComponent);
                            if (disposition == ProductRemovalDisposition.TargetIndependentProduct && identity.Confirmed && !identity.Conflicted)
                            {
                                target.Kind = "InvokeUninstaller";
                                target.UninstallCommand = uninstall;
                                target.FilePath = installLocation;
                                target.ExpectedProductName = display;
                                target.ExpectedPublisher = publisher;
                                target.ExpectedUninstallCommand = uninstall;
                                Finding finding = NewFinding("独立附带产品", name, "检测到独立安装的附带产品：" + reason + "。只会打开“" + name + "”自己的卸载器，不会卸载其来源主程序；是否卸载仍由用户确认。", target, text, 16, identity, badComponent);
                                finding.Risk = badComponent || adOrGuard ? "中" : "低";
                                list.Add(finding);
                            }
                            else
                            {
                                target.Kind = "ReportOnly";
                                string vendorNote = identity.Conflicted ? "厂商强证据冲突，" : (!identity.Confirmed ? "厂商身份无法可靠确认，" : string.Empty);
                                Finding finding = NewFinding("组件卸载边界待确认", name, vendorNote + "检测到组件异常线索：" + reason + "，但无法证明它是可独立卸载的附带产品。只提示，不打开主程序卸载器。", target, text, 5, identity, badComponent);
                                finding.Risk = "低";
                                list.Add(finding);
                            }
                        }
                    }
                }
            }
            return list;
        }

        private List<Finding> ScanRunningAdAndGuardProcesses()
        {
            List<Finding> list = new List<Finding>();
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT ProcessId,Name,ExecutablePath,CommandLine FROM Win32_Process"))
                {
                    foreach (ManagementObject process in searcher.Get())
                    {
                        string pid = Convert.ToString(process["ProcessId"]);
                        string name = Convert.ToString(process["Name"]);
                        string path = Convert.ToString(process["ExecutablePath"]);
                        string command = Convert.ToString(process["CommandLine"]);
                        string identity = Join(name, path);
                        string text = Join(pid, name, path, command);
                        if (!LooksLikeAdOrGuard(identity)) continue;
                        VendorEvidence evidence = new VendorEvidence().AddHuman(name).AddTechnical(name).AddFile(path).AddCommand(command).AddOpaque(pid);
                        VendorIdentityResult vendorIdentity = RuleCatalog.ResolveIdentity(evidence);
                        ActionTarget target = new ActionTarget { Kind = "ReportOnly", FilePath = Join(name, path, "PID=" + pid) };
                        Finding finding = NewFinding("正在运行/疑似弹窗守护", name, "后台正在运行，像是弹窗、推广、守护或自动恢复组件：" + Join(name, path), target, text, 12, vendorIdentity, RuleCatalog.HasBadComponent(evidence, vendorIdentity));
                        finding.Risk = "低";
                        list.Add(finding);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("扫描运行进程失败", ex);
            }
            return list;
        }

        private List<Finding> ScanFileAssociations()
        {
            List<Finding> list = new List<Finding>();
            foreach (string ext in FileExtensions)
            {
                foreach (ActionTarget extTarget in RegistryTargets(new string[] { @"Software\Classes\" + ext }, true, true))
                {
                    using (RegistryKey extKey = OpenForScan(extTarget, "文件关联"))
                    {
                        if (extKey == null) continue;
                        string defaultProgId = ReadString(extKey, "");
                        if (!string.IsNullOrEmpty(defaultProgId))
                        {
                            ActionTarget classTarget = CopyTarget(extTarget);
                            classTarget.Kind = "DeleteRegistryKey";
                            classTarget.SubKey = @"Software\Classes\" + defaultProgId;
                            using (RegistryKey classKey = OpenForScan(classTarget, "文件关联"))
                            {
                                string command = ReadDefault(classTarget, @"shell\open\command");
                                string text = Join(ext, defaultProgId, command);
                                VendorEvidence evidence = new VendorEvidence().AddTechnical(defaultProgId).AddCommand(command).AddOpaque(ext, classTarget.SubKey);
                                VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                                if (classKey != null && identity.Confirmed)
                                {
                                    classTarget.Kind = "ReportOnly";
                                    string title = ext + " 默认打开：" + FriendlyHandler(defaultProgId);
                                    list.Add(NewFinding("文件关联/默认打开程序", title, "双击/打开 " + ext + " 现在会交给：" + FriendlyHandler(defaultProgId) + "。这类属于主打开方式，只提示，不一键改。", classTarget, text, 8, identity, RuleCatalog.HasBadComponent(evidence, identity)));
                                }
                            }
                        }
                        foreach (string sub in new string[] { "OpenWithList", "OpenWithProgids" })
                        {
                            ActionTarget subTarget = CopyTarget(extTarget);
                            subTarget.SubKey = extTarget.SubKey + "\\" + sub;
                            using (RegistryKey subKey = OpenForScan(subTarget, "文件关联"))
                            {
                                if (subKey == null) continue;
                                foreach (string valueName in SafeValueNames(subKey))
                                {
                                    if (string.Equals(valueName, "MRUList", StringComparison.OrdinalIgnoreCase)) continue;
                                    ActionTarget progTarget = CopyTarget(extTarget);
                                    progTarget.SubKey = @"Software\Classes\" + valueName;
                                    string command = ReadDefault(progTarget, @"shell\open\command");
                                    string text = Join(ext, valueName, command, subTarget.SubKey);
                                    VendorEvidence evidence = new VendorEvidence().AddTechnical(valueName).AddCommand(command).AddOpaque(ext, subTarget.SubKey);
                                    VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                                    if (!identity.Confirmed) continue;
                                    ActionTarget valueTarget = CopyTarget(subTarget);
                                    valueTarget.Kind = "DeleteRegistryValue";
                                    valueTarget.ValueName = valueName;
                                    string title = ext + " 打开方式：" + FriendlyHandler(valueName);
                                    list.Add(NewFinding("文件关联/打开方式", title, "右键“打开方式”里会出现：" + FriendlyHandler(valueName) + "（影响 " + ext + " 文件）", valueTarget, text, 22, identity, RuleCatalog.HasBadComponent(evidence, identity)));
                                }
                            }
                        }
                    }
                }
            }
            return list;
        }

        private List<Finding> ScanServices()
        {
            List<Finding> list = new List<Finding>();
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name,DisplayName,PathName,Description,StartMode FROM Win32_Service"))
                {
                    foreach (ManagementObject svc in searcher.Get())
                    {
                        string name = Convert.ToString(svc["Name"]);
                        string display = Convert.ToString(svc["DisplayName"]);
                        string path = Convert.ToString(svc["PathName"]);
                        string desc = Convert.ToString(svc["Description"]);
                        string mode = Convert.ToString(svc["StartMode"]);
                        if (mode.Equals("Disabled", StringComparison.OrdinalIgnoreCase)) continue;
                        if (IsWindowsNativeService(name, display, path, desc)) continue;
                        string text = Join(name, display, path, desc, mode);
                        VendorEvidence evidence = new VendorEvidence().AddHuman(display, desc).AddTechnical(name).AddCommand(path).AddOpaque(mode);
                        VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                        if (!identity.Confirmed) continue;
                        bool badComponent = RuleCatalog.HasBadComponent(evidence, identity);
                        if (!ProductRemovalPolicy.IsAbnormalPersistence(name, path, badComponent)) continue;
                        ActionTarget target = new ActionTarget { Kind = "DisableService", ServiceName = name };
                        string title = FriendlyServiceTitle(text, name, display, identity.Vendor);
                        Finding finding = NewFinding("异常后台服务", title, "这个服务的名称或执行文件明确命中弹窗、广告、守护或自动恢复特征：" + title + "。只禁用服务“" + name + "”，不卸载所属主程序。", target, text, 42, identity, badComponent);
                        finding.RequiresAdmin = true;
                        list.Add(finding);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("扫描服务失败", ex);
            }
            return list;
        }

        private static bool IsWindowsNativeService(string name, string display, string path, string desc)
        {
            string lowerPath = (Environment.ExpandEnvironmentVariables(path ?? string.Empty)).Trim().Trim('"').ToLowerInvariant();
            if (lowerPath.IndexOf(@"\windows\system32\svchost.exe", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (lowerPath.IndexOf(@"\windows\syswow64\svchost.exe", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (lowerPath.StartsWith("svchost.exe", StringComparison.OrdinalIgnoreCase)) return true;

            string text = Join(name, display, desc).ToLowerInvariant();
            bool windowsName = text.IndexOf("windows ") >= 0 || text.IndexOf("microsoft ") >= 0 || text.IndexOf("windows ") >= 0;
            bool systemPath = lowerPath.IndexOf(@"\windows\system32\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lowerPath.IndexOf(@"\windows\syswow64\", StringComparison.OrdinalIgnoreCase) >= 0;
            return systemPath && windowsName;
        }

        private List<Finding> ScanScheduledTasks()
        {
            List<Finding> list = new List<Finding>();
            try
            {
                Type serviceType = Type.GetTypeFromProgID("Schedule.Service");
                if (serviceType == null) return list;
                dynamic service = Activator.CreateInstance(serviceType);
                service.Connect();
                ScanTaskFolder(service.GetFolder("\\"), list);
            }
            catch (Exception ex)
            {
                Logger.Error("扫描计划任务失败", ex);
            }
            return list;
        }

        private void ScanTaskFolder(dynamic folder, List<Finding> list)
        {
            foreach (dynamic task in folder.GetTasks(1))
            {
                bool enabled = true;
                try { enabled = Convert.ToBoolean(task.Enabled); } catch { }
                if (!enabled) continue;
                string name = Convert.ToString(task.Name);
                string path = Convert.ToString(task.Path);
                string text = path;
                string description = string.Empty;
                try { description = Convert.ToString(task.Definition.RegistrationInfo.Description); text = Join(text, description); } catch { }
                VendorEvidence evidence = new VendorEvidence().AddHuman(description).AddTechnical(name).AddOpaque(path);
                try
                {
                    foreach (dynamic action in task.Definition.Actions)
                    {
                        try
                        {
                            string actionPath = Convert.ToString(action.Path);
                            string arguments = Convert.ToString(action.Arguments);
                            text = Join(text, actionPath, arguments);
                            evidence.AddFile(actionPath).AddCommand(Join(actionPath, arguments));
                        }
                        catch { }
                    }
                }
                catch { }
                VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                bool badComponent = RuleCatalog.HasBadComponent(evidence, identity);
                if (identity.Confirmed && ProductRemovalPolicy.IsAbnormalPersistence(name, text, badComponent))
                {
                    ActionTarget target = new ActionTarget { Kind = "DisableScheduledTask", TaskName = path };
                    string title = FriendlyTaskTitle(text, name, identity.Vendor);
                    Finding finding = NewFinding("异常计划任务/定时拉起", title, "任务名称或执行文件明确命中弹窗、广告、守护或自动恢复特征：" + title + "。只禁用任务“" + name + "”，不卸载所属主程序。", target, text, 30, identity, badComponent);
                    finding.RequiresAdmin = true;
                    list.Add(finding);
                }
            }
            foreach (dynamic child in folder.GetFolders(0))
            {
                ScanTaskFolder(child, list);
            }
        }

        private Finding NewFinding(string category, string title, string impact, ActionTarget target, string text, int baseScore, VendorIdentityResult identity, bool badComponent)
        {
            int score = baseScore + RuleCatalog.VendorBoost(identity, badComponent);
            Finding finding = new Finding();
            finding.Selected = false;
            bool reportOnly = string.Equals(target.Kind, "ReportOnly", StringComparison.OrdinalIgnoreCase);
            finding.Risk = reportOnly ? "低" : (score >= 80 ? "高" : (score >= 55 ? "中" : "低"));
            finding.Score = reportOnly ? Math.Min(score, 20) : score;
            finding.Vendor = identity == null ? "未知第三方" : identity.Vendor;
            finding.Category = category;
            finding.UserVisibleName = Clean(title);
            finding.UserImpact = impact;
            finding.TechnicalLocation = DescribeTarget(target);
            finding.ActionKind = target.Kind;
            finding.Target = target;
            finding.RequiresAdmin = target.Hive == "HKLM" || target.Kind == "DisableService" || target.Kind == "DisableScheduledTask";
            finding.CanRestore = true;
            finding.Evidence = Join(text, identity == null ? string.Empty : "身份依据：" + identity.EvidenceSummary);
            finding.Status = "待处理";
            return finding;
        }

        private static IEnumerable<ActionTarget> RegistryTargets(string[] subKeys, bool includeHkcu, bool includeHklm)
        {
            foreach (string subKey in subKeys)
            {
                if (includeHkcu) yield return new ActionTarget { Kind = "Registry", Hive = "HKCU", View = "Default", SubKey = subKey };
                if (includeHklm)
                {
                    yield return new ActionTarget { Kind = "Registry", Hive = "HKLM", View = "Registry64", SubKey = subKey };
                    yield return new ActionTarget { Kind = "Registry", Hive = "HKLM", View = "Registry32", SubKey = subKey };
                }
            }
        }

        private static ActionTarget CopyTarget(ActionTarget source)
        {
            return new ActionTarget { Kind = source.Kind, Hive = source.Hive, View = source.View, SubKey = source.SubKey, ValueName = source.ValueName, FilePath = source.FilePath, ServiceName = source.ServiceName, TaskName = source.TaskName, UninstallCommand = source.UninstallCommand, IconValue = source.IconValue, PresentationCommand = source.PresentationCommand, Clsid = source.Clsid, SourceSubKey = source.SourceSubKey, ExpectedProductName = source.ExpectedProductName, ExpectedPublisher = source.ExpectedPublisher, ExpectedUninstallCommand = source.ExpectedUninstallCommand };
        }

        private static bool IsShellExtensionBlocked(string hive, string view, string clsid)
        {
            if (string.IsNullOrWhiteSpace(clsid)) return false;
            ActionTarget target = new ActionTarget { Hive = hive, View = view, SubKey = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked", ValueName = clsid };
            try { return RegistryHelper.ValueExists(target); }
            catch (SecurityException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static string FirstClsid(params string[] values)
        {
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                Match match = Regex.Match(value, @"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}");
                if (match.Success) return match.Value;
            }
            return string.Empty;
        }

        private static string[] SafeSubKeyNames(RegistryKey key)
        {
            try { return key.GetSubKeyNames(); } catch { return new string[0]; }
        }

        private static string[] SafeValueNames(RegistryKey key)
        {
            try { return key.GetValueNames(); } catch { return new string[0]; }
        }

        private static string ReadString(RegistryKey key, string name)
        {
            if (key == null) return string.Empty;
            try { return Convert.ToString(key.GetValue(name, "")); } catch { return string.Empty; }
        }

        private string ReadDefault(ActionTarget target, string child)
        {
            ActionTarget t = CopyTarget(target);
            t.SubKey = target.SubKey + "\\" + child;
            using (RegistryKey key = OpenForScan(t, "右键菜单"))
            {
                return ReadString(key, "");
            }
        }

        private string ResolveClsidRegistration(params string[] values)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string value in values)
            {
                foreach (string clsid in ExtractClsids(value))
                {
                    string info = ReadClsidInfo(clsid);
                    if (string.IsNullOrEmpty(info)) continue;
                    if (sb.Length > 0) sb.Append(" ");
                    sb.Append(info);
                }
            }
            return sb.ToString();
        }

        private static IEnumerable<string> ExtractClsids(string value)
        {
            if (string.IsNullOrEmpty(value)) yield break;
            int start = 0;
            while (start < value.Length)
            {
                int open = value.IndexOf('{', start);
                if (open < 0) yield break;
                int close = value.IndexOf('}', open + 1);
                if (close < 0) yield break;
                string clsid = value.Substring(open, close - open + 1);
                if (clsid.Length >= 38) yield return clsid;
                start = close + 1;
            }
        }

        private string ReadClsidInfo(string clsid)
        {
            List<string> parts = new List<string>();
            string subKey = @"Software\Classes\CLSID\" + clsid;
            foreach (ActionTarget target in RegistryTargets(new string[] { subKey }, true, true))
            {
                using (RegistryKey key = OpenForScan(target, "CLSID 解析"))
                {
                    if (key == null) continue;
                    parts.Add(ReadString(key, ""));
                    parts.Add(ReadChildDefault(target, "InprocServer32"));
                    parts.Add(ReadChildDefault(target, "LocalServer32"));
                    parts.Add(ReadChildDefault(target, "ProgID"));
                }
            }
            return Join(parts.ToArray());
        }

        private string ReadChildDefault(ActionTarget target, string child)
        {
            ActionTarget childTarget = CopyTarget(target);
            childTarget.SubKey = target.SubKey + "\\" + child;
            using (RegistryKey key = OpenForScan(childTarget, "注册表子项"))
            {
                return ReadString(key, "");
            }
        }

        private string ReadChildValue(ActionTarget target, string child, string valueName)
        {
            ActionTarget childTarget = CopyTarget(target);
            childTarget.SubKey = target.SubKey + "\\" + child;
            using (RegistryKey key = OpenForScan(childTarget, "注册表子项"))
            {
                return ReadString(key, valueName);
            }
        }

        private static string ResolveShortcutText(string file)
        {
            try
            {
                if (!file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return string.Empty;
                IShellLinkW link = (IShellLinkW)new ShellLinkComObject();
                ((IPersistFile)link).Load(file, 0);
                StringBuilder target = new StringBuilder(1024);
                StringBuilder args = new StringBuilder(1024);
                StringBuilder workingDirectory = new StringBuilder(1024);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
                link.GetArguments(args, args.Capacity);
                link.GetWorkingDirectory(workingDirectory, workingDirectory.Capacity);
                return Join(target.ToString(), args.ToString(), workingDirectory.ToString());
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsTruthy(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            value = value.Trim();
            return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string HiddenInstallReason(string display, string uninstall, string systemComponent, string noRemove, string parentKey, bool hidden, bool adOrGuard, bool badComponent)
        {
            List<string> reasons = new List<string>();
            if (string.IsNullOrWhiteSpace(display)) reasons.Add("没有显示名称");
            if (string.IsNullOrWhiteSpace(uninstall)) reasons.Add("没有卸载命令");
            if (IsTruthy(systemComponent)) reasons.Add("标记为系统组件，控制面板可能隐藏");
            if (IsTruthy(noRemove)) reasons.Add("标记为不可移除");
            if (!string.IsNullOrWhiteSpace(parentKey)) reasons.Add("挂在其它组件下面");
            if (!hidden && adOrGuard) reasons.Add("命中弹窗/守护特征");
            if (!hidden && badComponent) reasons.Add("命中已知捆绑组件特征");
            return reasons.Count == 0 ? "疑似捆绑组件" : string.Join("，", reasons.ToArray());
        }

        private static int RiskRank(string risk)
        {
            if (risk == "高") return 0;
            if (risk == "中") return 1;
            return 2;
        }

        private static string DescribeTarget(ActionTarget target)
        {
            if (target.Kind == "MoveFileToBackup") return target.FilePath;
            if (target.Kind == "DisableService") return "服务：" + target.ServiceName;
            if (target.Kind == "DisableScheduledTask") return "计划任务：" + target.TaskName;
            if (target.Kind == "ReportOnly" && !string.IsNullOrWhiteSpace(target.FilePath)) return target.FilePath;
            if (target.Kind == "ReportOnly" && string.IsNullOrWhiteSpace(target.SubKey)) return "只报告";
            string path = !string.IsNullOrWhiteSpace(target.SourceSubKey) ? (target.Hive == "HKLM" ? "HKLM\\" : "HKCU\\") + target.SourceSubKey : RegistryHelper.NativePath(target);
            if (!string.IsNullOrEmpty(target.ValueName)) path += "::" + target.ValueName;
            if (!string.IsNullOrEmpty(target.View) && target.View != "Default") path += " (" + target.View + ")";
            return path;
        }

        private static bool LooksLikeAdOrGuard(string text)
        {
            string[] tokens = new string[]
            {
                "popup", "adpopup", "adservice", "adpush", "advert", "hotnews", "newsfeed", "notifycenter", "pushservice", "minipage",
                "watchdog", "daemon", "guardservice", "protectservice", "keeper", "serviceplatform",
                "弹窗", "广告", "热点", "资讯", "推荐", "迷你页", "守护", "保护", "修复", "恢复", "推送"
            };
            foreach (string token in tokens)
            {
                if (ContainsBehaviorToken(text, token)) return true;
            }
            return false;
        }

        private static string SafePathFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            try { return Path.GetFileName(value.Trim().Trim('"')); }
            catch { return string.Empty; }
        }

        private static bool ContainsBehaviorToken(string text, string token)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token)) return false;
            int start = 0;
            while (true)
            {
                int index = text.IndexOf(token, start, StringComparison.OrdinalIgnoreCase);
                if (index < 0) return false;
                bool ascii = token.All(delegate(char c) { return c < 128; });
                if (!ascii || token.Length >= 7) return true;
                int end = index + token.Length;
                bool left = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
                bool right = end >= text.Length || !char.IsLetterOrDigit(text[end]);
                if (left && right) return true;
                start = index + 1;
            }
        }

        private static string FriendlyContextMenuTitle(string subKey, string childName, string display, string mui, string explorerHandler, string commandStateHandler, string clsidText)
        {
            string where = ContextWhereShort(subKey);
            string candidate = FirstHumanText(display, mui);
            if (!string.IsNullOrEmpty(candidate))
            {
                return where + "：会出现“" + candidate + "”";
            }

            string evidence = Join(childName, display, mui, explorerHandler, commandStateHandler, clsidText);
            string feature = FriendlyContextMenuFeature(evidence);
            return where + "：疑似会出现“" + feature + "”";
        }

        private static string FriendlyContextMenuFeature(string evidence)
        {
            string lower = (evidence ?? string.Empty).ToLowerInvariant();
            if (lower.IndexOf("softmgrext") >= 0) return "360 软件管家右键菜单";
            if (lower.IndexOf("safe360ext") >= 0) return "360 安全/扫描右键菜单";
            if (lower.IndexOf("360ai") >= 0) return "360AI 图片右键菜单";
            if (lower.IndexOf("360alb") >= 0 || lower.IndexOf("albumviewer") >= 0 || lower.IndexOf("ablumviewer") >= 0) return "360 看图右键菜单";
            if (lower.IndexOf("qingshellext") >= 0 || lower.IndexOf("67f4d210-bfc2-4add-9a2a-c9b9e1f42c4f") >= 0) return "上传到 WPS 云文档";
            if (lower.IndexOf("qingnsecontextmenu") >= 0 || lower.IndexOf("aa147ffb-0b1f-4bb1-9b1e-8d062b35c146") >= 0) return "WPS 云文档操作菜单";
            if (lower.IndexOf("kpdf2wordshellext") >= 0) return "WPS PDF 转 Word";
            if (lower.IndexOf("kingsoftofficepdf.contextmenu") >= 0) return "WPS PDF 操作菜单";
            if (lower.IndexOf("knewdocshellext") >= 0) return "新建 WPS 文档菜单";
            if (lower.IndexOf("kwpsshellext") >= 0 || lower.IndexOf("kwpsshell") >= 0) return "WPS Office 文档操作菜单";
            if (lower.IndexOf("qingnse") >= 0) return "WPS 云文档操作菜单";
            if (lower.IndexOf("kdesktop") >= 0 || lower.IndexOf("qkdesktop") >= 0 || lower.IndexOf("wpsdrive") >= 0) return "WPS 云文档/云盘入口";
            if (lower.IndexOf("baidunetdisk") >= 0 || lower.IndexOf("baiduyun") >= 0 || lower.IndexOf("yunshell") >= 0) return "百度网盘右键菜单";
            bool quarkEvidence = lower.IndexOf("quark") >= 0 || lower.IndexOf("夸克") >= 0 || lower.IndexOf("vt.quark.cn") >= 0 || lower.IndexOf("external_rclick") >= 0;
            if (lower.IndexOf("quarkclouddrive.upload") >= 0 || lower.IndexOf("上传到夸克") >= 0) return "夸克网盘上传右键菜单";
            if (lower.IndexOf("quarkclouddrive.backup") >= 0) return "夸克网盘备份右键菜单";
            if (quarkEvidence && (lower.IndexOf("quarkpdf") >= 0 || lower.IndexOf("quarkconvert") >= 0 || lower.IndexOf("pdf转换") >= 0 || lower.IndexOf("图片转pdf") >= 0 || lower.IndexOf("万能转换") >= 0 || lower.IndexOf("external_rclick") >= 0 || lower.IndexOf("vt.quark.cn") >= 0)) return "夸克 PDF/万能转换右键菜单";
            if (quarkEvidence) return "夸克右键菜单";
            if (lower.IndexOf("sogou") >= 0) return "搜狗右键菜单";
            if (lower.IndexOf("xunlei") >= 0 || lower.IndexOf("thunder") >= 0) return "迅雷右键菜单";
            if (lower.IndexOf("dingtalk") >= 0 || lower.IndexOf("钉钉") >= 0 || lower.IndexOf("钉盘") >= 0) return "钉钉文件上传右键菜单";
            if (lower.IndexOf("bandiview") >= 0 || lower.IndexOf("honeyview") >= 0) return "BandiView/Honeyview 看图右键菜单";
            if (lower.IndexOf("bandizip") >= 0 || lower.IndexOf("bandisoft") >= 0) return "Bandisoft 右键菜单";
            string vendor = ShortVendorName(evidence);
            if (string.IsNullOrWhiteSpace(vendor) || vendor == "第三方软件" || vendor == "未知第三方") return "未识别的右键扩展";
            return vendor + "右键扩展（具体功能未识别）";
        }

        internal static List<string> RunContextMenuNameSelfTests()
        {
            List<string> failures = new List<string>();
            AssertContextMenuName(failures, "Open With qingshellext {67F4D210-BFC2-4ADD-9A2A-C9B9E1F42C4F}", "上传到 WPS 云文档");
            AssertContextMenuName(failures, "QingNseContextMenu {AA147FFB-0B1F-4BB1-9B1E-8D062B35C146}", "WPS 云文档操作菜单");
            AssertContextMenuName(failures, "kwpsshellext", "WPS Office 文档操作菜单");
            AssertContextMenuName(failures, "knewdocshellext", "新建 WPS 文档菜单");
            AssertContextMenuName(failures, "KingsoftOfficePDF.ContextMenu", "WPS PDF 操作菜单");
            AssertContextMenuName(failures, "kpdf2wordshellext", "WPS PDF 转 Word");
            string fallback = FriendlyContextMenuFeature("WPS unknown shell extension");
            if (fallback.IndexOf("相关", StringComparison.OrdinalIgnoreCase) >= 0) failures.Add("右键名称回归：未知 WPS 扩展仍使用‘相关’泛称");
            return failures;
        }

        private static void AssertContextMenuName(List<string> failures, string evidence, string expected)
        {
            string actual = FriendlyContextMenuFeature(evidence);
            if (!string.Equals(actual, expected, StringComparison.Ordinal)) failures.Add("右键名称回归：" + evidence + " 应为‘" + expected + "’，实际为‘" + actual + "’");
        }

        private static string FriendlyStartupTitle(string evidence, string name, string command, string vendor)
        {
            string lower = Join(evidence, name, command).ToLowerInvariant();
            if (lower.IndexOf("360safetray") >= 0) return "360 安全卫士托盘/防护入口";
            if (lower.IndexOf("baiduyundetect") >= 0) return "百度网盘检测/同步启动项";
            if (lower.IndexOf("sogou") >= 0 && LooksLikeAdOrGuard(lower)) return "搜狗弹窗/守护启动项";
            if (lower.IndexOf("thunder") >= 0 || lower.IndexOf("xunlei") >= 0) return "迅雷开机启动项";
            if (lower.IndexOf("dingtalk") >= 0 || lower.IndexOf("钉钉") >= 0) return "钉钉开机启动项";
            string human = FirstHumanText(name, Path.GetFileNameWithoutExtension(ExtractExecutableName(command)));
            return ShortVendorName(vendor, evidence) + "开机启动：" + (string.IsNullOrEmpty(human) ? "启动项" : human);
        }

        private static string FriendlyBrowserTitle(string evidence, string rawName, string vendor)
        {
            string lower = Join(evidence, rawName).ToLowerInvariant();
            if (lower.IndexOf("kingsoft") >= 0 || lower.IndexOf("wps") >= 0) return "WPS/金山浏览器扩展宿主";
            if (lower.IndexOf("baidunetdisk") >= 0) return "百度网盘浏览器扩展宿主";
            if (lower.IndexOf("quark") >= 0 || lower.IndexOf("夸克") >= 0) return "夸克浏览器/网盘外部宿主";
            if (lower.IndexOf("sogou") >= 0) return "搜狗浏览器扩展/策略";
            if (lower.IndexOf("xunlei") >= 0 || lower.IndexOf("thunder") >= 0) return "迅雷浏览器下载助手";
            if (lower.IndexOf("dingtalk") >= 0 || lower.IndexOf("钉钉") >= 0) return "钉钉浏览器扩展/外部宿主";
            if (lower.IndexOf("360") >= 0 || lower.IndexOf("qihoo") >= 0) return "360 浏览器扩展/策略";
            if (lower.IndexOf("bandisoft") >= 0 || lower.IndexOf("bandiview") >= 0 || lower.IndexOf("bandizip") >= 0) return "Bandisoft 浏览器/外部宿主";
            return ShortVendorName(vendor, evidence) + "浏览器扩展/宿主";
        }

        private static string FriendlyExplorerNamespaceTitle(string subKey, string childName, string display, string localized, string itemName, string evidence)
        {
            string where = ExplorerNamespaceWhereShort(subKey);
            string human = FirstHumanText(display, localized, itemName);
            if (string.IsNullOrWhiteSpace(human)) human = FriendlyExplorerNamespaceFeature(Join(childName, evidence));
            return where + "：会出现“" + human + "”";
        }

        private static string FriendlyExplorerNamespaceFeature(string evidence)
        {
            string lower = (evidence ?? string.Empty).ToLowerInvariant();
            if (lower.IndexOf("baidunetdisk") >= 0 || lower.IndexOf("baiduyun") >= 0 || lower.IndexOf("yunshell") >= 0) return "百度网盘入口";
            if (lower.IndexOf("quark") >= 0 || lower.IndexOf("夸克") >= 0) return "夸克网盘入口";
            if (lower.IndexOf("wps") >= 0 || lower.IndexOf("kingsoft") >= 0 || lower.IndexOf("金山") >= 0) return "WPS/金山云盘入口";
            if (lower.IndexOf("xunlei") >= 0 || lower.IndexOf("thunder") >= 0 || lower.IndexOf("迅雷") >= 0) return "迅雷云盘/下载入口";
            if (lower.IndexOf("dingtalk") >= 0 || lower.IndexOf("钉钉") >= 0 || lower.IndexOf("钉盘") >= 0) return "钉钉/钉盘入口";
            if (lower.IndexOf("tencent") >= 0 || lower.IndexOf("qq") >= 0 || lower.IndexOf("腾讯") >= 0) return "腾讯系云盘入口";
            if (lower.IndexOf("360") >= 0 || lower.IndexOf("qihoo") >= 0 || lower.IndexOf("奇虎") >= 0) return "360 云盘/同步入口";
            return ShortVendorName(evidence) + "入口";
        }

        private static string ExplorerNamespaceWhereShort(string subKey)
        {
            string lower = (subKey ?? string.Empty).ToLowerInvariant();
            if (lower.IndexOf(@"\mycomputer\namespace") >= 0) return "此电脑";
            if (lower.IndexOf(@"\networkneighborhood\namespace") >= 0) return "网络位置";
            if (lower.IndexOf(@"\desktop\namespace") >= 0) return "桌面/导航栏";
            if (lower.IndexOf(@"\classes\clsid\") >= 0) return "资源管理器导航栏";
            return "资源管理器";
        }

        private static string FriendlyServiceTitle(string evidence, string name, string display, string vendor)
        {
            string lower = Join(evidence, name, display).ToLowerInvariant();
            if (lower.IndexOf("q360amppl") >= 0) return "360 安全防护后台服务";
            if (lower.IndexOf("zhudongfangyu") >= 0 || lower.IndexOf("主动防御") >= 0 || lower.IndexOf("qhactivedefense") >= 0) return "360 主动防御后台服务";
            if (lower.IndexOf("baidunetdiskutility") >= 0 || lower.IndexOf("baiduyundetect") >= 0) return "百度网盘检测/同步后台服务";
            if (lower.IndexOf("quark") >= 0 || lower.IndexOf("夸克") >= 0) return "夸克网盘后台服务";
            if (lower.IndexOf("wps office cloud service") >= 0 || lower.IndexOf("wpscloud") >= 0) return "WPS 云文档后台服务";
            if (lower.IndexOf("sogousvc") >= 0 || lower.IndexOf("sgimeguard") >= 0) return "搜狗输入法守护/更新服务";
            if (lower.IndexOf("xlservice") >= 0 || lower.IndexOf("thunder") >= 0 || lower.IndexOf("xunlei") >= 0) return "迅雷后台/更新服务";
            if (lower.IndexOf("dingtalk") >= 0 || lower.IndexOf("钉钉") >= 0) return "钉钉后台/更新服务";
            string human = FirstHumanText(display, name);
            return ShortVendorName(vendor, evidence) + "后台服务" + (string.IsNullOrEmpty(human) ? string.Empty : "：" + human);
        }

        private static string FriendlyTaskTitle(string evidence, string name, string vendor)
        {
            string lower = Join(evidence, name).ToLowerInvariant();
            if (lower.IndexOf("wpsupdate") >= 0 || lower.IndexOf("wpswake") >= 0) return "WPS 更新/唤醒计划任务";
            if (lower.IndexOf("getword") >= 0 || lower.IndexOf("wordsearch") >= 0 || lower.IndexOf("searchfetch") >= 0) return "360 划词/搜索计划任务";
            if (lower.IndexOf("qihoo") >= 0 || lower.IndexOf("360") >= 0) return "360 定时扫描/拉起计划任务";
            if (lower.IndexOf("baiduyun") >= 0 || lower.IndexOf("baidunetdisk") >= 0) return "百度网盘检测/同步计划任务";
            if (lower.IndexOf("quark") >= 0 || lower.IndexOf("夸克") >= 0) return "夸克网盘更新/拉起计划任务";
            if (lower.IndexOf("sogou") >= 0) return "搜狗更新/弹窗计划任务";
            if (lower.IndexOf("thunder") >= 0 || lower.IndexOf("xunlei") >= 0) return "迅雷更新/拉起计划任务";
            if (lower.IndexOf("dingtalk") >= 0 || lower.IndexOf("钉钉") >= 0) return "钉钉更新/拉起计划任务";
            string human = FirstHumanText(name);
            return ShortVendorName(vendor, evidence) + "计划任务" + (string.IsNullOrEmpty(human) ? string.Empty : "：" + human);
        }

        private static string FirstHumanText(params string[] values)
        {
            foreach (string value in values)
            {
                string cleaned = Clean(value);
                if (string.IsNullOrWhiteSpace(cleaned)) continue;
                if (LooksTechnicalName(cleaned)) continue;
                return cleaned;
            }
            return string.Empty;
        }

        private static bool LooksTechnicalName(string value)
        {
            string lower = (value ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(lower)) return true;
            if (lower.IndexOf("{") >= 0 || lower.IndexOf("}") >= 0) return true;
            if (lower.IndexOf(".dll") >= 0 || lower.IndexOf(".exe") >= 0 || lower.IndexOf("\\") >= 0 || lower.IndexOf("/") >= 0) return true;
            string[] tokens = new string[] { "shellext", "safe360ext", "softmgrext", "contextmenu", "qingshell", "qingnse", "clsid", "com.", "native", "handler", "class" };
            foreach (string token in tokens)
            {
                if (lower.IndexOf(token) >= 0) return true;
            }
            bool hasLetter = false;
            bool hasChinese = false;
            int digits = 0;
            foreach (char c in value)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) hasLetter = true;
                if (c >= 0x4e00 && c <= 0x9fff) hasChinese = true;
                if (char.IsDigit(c)) digits++;
            }
            return hasLetter && !hasChinese && digits >= 3 && value.Length >= 8;
        }

        private static string ShortVendorName(string evidence)
        {
            string vendor = RuleCatalog.ResolveVendor(evidence);
            if (vendor == "未知第三方") return "第三方软件";
            return vendor.Replace(" 系列", string.Empty).Replace(" / ", "/");
        }

        private static string ShortVendorName(string vendor, string evidence)
        {
            if (string.IsNullOrWhiteSpace(vendor) || vendor == "未知第三方") return ShortVendorName(evidence);
            return vendor.Replace(" 系列", string.Empty).Replace(" / ", "/");
        }

        private static string ExtractExecutableName(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return string.Empty;
            command = Environment.ExpandEnvironmentVariables(command.Trim().Trim('"'));
            int exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe >= 0) return command.Substring(0, exe + 4).Trim('"');
            int split = command.IndexOf(' ');
            return split > 0 ? command.Substring(0, split).Trim('"') : command.Trim('"');
        }

        private static string ContextWhereShort(string subKey)
        {
            string lower = subKey.ToLowerInvariant();
            if (lower.IndexOf("\\desktopbackground\\") >= 0 || lower.IndexOf("\\directory\\background\\") >= 0) return "桌面/文件夹空白处右键";
            if (lower.IndexOf("\\drive\\") >= 0) return "磁盘盘符右键";
            if (lower.IndexOf("\\directory\\") >= 0) return "文件夹右键";
            if (lower.IndexOf("\\lnkfile\\") >= 0) return "快捷方式右键";
            if (lower.IndexOf("\\*\\") >= 0) return "普通文件右键";
            return "资源管理器右键";
        }

        private static string DescribeContextMenu(string subKey, string title)
        {
            return Clean(title);
        }

        private static string FriendlyProgram(string name, string command)
        {
            if (!string.IsNullOrEmpty(name)) return name;
            return command;
        }

        private static string FriendlyHandler(string value)
        {
            string lower = (value ?? string.Empty).ToLowerInvariant();
            if (lower.IndexOf("baidunetdisk") >= 0) return "百度网盘";
            if (lower.IndexOf("quarkclouddrive") >= 0 || lower.IndexOf("quark") >= 0 || lower.IndexOf("夸克") >= 0) return "夸克网盘";
            if (lower.IndexOf("bandiview") >= 0) return "BandiView 看图";
            if (lower.IndexOf("honeyview") >= 0) return "Honeyview 看图";
            if (lower.IndexOf("bandizip") >= 0) return "Bandizip 压缩";
            if (lower.IndexOf("wps.doc") >= 0 || lower.IndexOf("wps.docx") >= 0) return "WPS 文字";
            if (lower.IndexOf("kwps.pdf") >= 0) return "WPS PDF";
            if (lower.IndexOf("wpp.ppt") >= 0) return "WPS 演示";
            if (lower.IndexOf("et.xls") >= 0) return "WPS 表格";
            if (lower.IndexOf("xunlei") >= 0) return "迅雷";
            return value;
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static string Join(params string[] parts)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string part in parts)
            {
                if (string.IsNullOrWhiteSpace(part)) continue;
                if (sb.Length > 0) sb.Append(" ");
                sb.Append(part.Trim());
            }
            return sb.ToString();
        }
    }

    internal sealed class CleanerEngine
    {
        private readonly DataStore store;

        public CleanerEngine(DataStore store)
        {
            this.store = store;
        }

        public CleanupBatch Clean(IEnumerable<Finding> findings)
        {
            string id = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string batchPath = Path.Combine(store.Backups, id);
            Directory.CreateDirectory(batchPath);
            Directory.CreateDirectory(Path.Combine(batchPath, "registry"));
            Directory.CreateDirectory(Path.Combine(batchPath, "files"));
            Directory.CreateDirectory(Path.Combine(batchPath, "services"));
            Directory.CreateDirectory(Path.Combine(batchPath, "tasks"));
            List<CleanupResult> results = new List<CleanupResult>();

            foreach (Finding finding in findings.Where(delegate(Finding f) { return f.Selected && f.CanClean; }))
            {
                CleanupResult result = CleanOne(finding, batchPath);
                results.Add(result);
            }

            CleanupBatch batch = new CleanupBatch { Id = id, CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Path = batchPath, Results = results };
            WriteJson(Path.Combine(batchPath, "manifest.json"), batch);
            WriteJson(Path.Combine(store.Reports, "cleanup-" + id + ".json"), results);
            return batch;
        }

        private CleanupResult CleanOne(Finding finding, string batchPath)
        {
            CleanupResult result = new CleanupResult();
            result.Id = finding.Id;
            result.Title = finding.UserVisibleName;
            result.Vendor = finding.Vendor;
            result.Category = finding.Category;
            result.ActionKind = finding.ActionKind;
            result.TechnicalLocation = finding.TechnicalLocation;
            result.Target = finding.Target;
            result.Status = "Skipped";
            result.Message = "未执行。";

            try
            {
                ActionTarget target = finding.Target;
                if (target == null || string.IsNullOrEmpty(target.Kind)) throw new InvalidOperationException("缺少清理目标。");
                if (target.Kind == "DeleteRegistryKey")
                {
                    result.Backup = BackupRegistry(batchPath, target);
                    if (string.IsNullOrEmpty(result.Backup))
                    {
                        result.Status = "Failed";
                        result.Message = "注册表备份失败，已取消删除，避免右键菜单或系统设置无法恢复。";
                    }
                    else
                    {
                        RegistryHelper.DeleteKey(target);
                        result.Status = VerifyApplied(target) ? "Done" : "Failed";
                        result.Message = result.Status == "Done" ? "注册表键已删除。" : "复核失败：注册表键仍然存在。";
                    }
                }
                else if (target.Kind == "DeleteRegistryValue")
                {
                    result.Backup = BackupRegistry(batchPath, target);
                    if (string.IsNullOrEmpty(result.Backup))
                    {
                        result.Status = "Failed";
                        result.Message = "注册表备份失败，已取消删除，避免右键菜单或系统设置无法恢复。";
                    }
                    else
                    {
                        RegistryHelper.DeleteValue(target);
                        result.Status = VerifyApplied(target) ? "Done" : "Failed";
                        result.Message = result.Status == "Done" ? "注册表值已删除。" : "复核失败：注册表值仍然存在。";
                    }
                }
                else if (target.Kind == "DisableShellExtension")
                {
                    string backupPath = Path.Combine(Path.Combine(batchPath, "registry"), "shell-extension-" + SafeFileName(target.ValueName) + "-" + SafeFileName(target.View) + ".json");
                    ContextMenuToggleBackup backup = ContextMenuMutationService.CaptureValue(target, "ShellExBlocked");
                    WriteJson(backupPath, backup);
                    result.Backup = backupPath;
                    if (!ContextMenuMutationService.SetShellExtensionBlocked(target, true)) throw new InvalidOperationException("写入 Windows Shell 扩展屏蔽列表后复核失败。");
                    result.Status = VerifyApplied(target) ? "Done" : "Failed";
                    result.Message = result.Status == "Done" ? "右键扩展已通过 Windows 屏蔽列表禁用。" : "复核失败：右键扩展仍未被屏蔽。";
                }
                else if (target.Kind == "MoveFileToBackup")
                {
                    string src = Environment.ExpandEnvironmentVariables(target.FilePath ?? string.Empty);
                    if (!File.Exists(src))
                    {
                        result.Status = "Failed";
                        result.Message = "要移动的源文件不存在，已跳过，避免误报成功。";
                    }
                    else
                    {
                        string dest = Path.Combine(Path.Combine(batchPath, "files"), Path.GetFileName(src));
                        File.Move(src, dest);
                        result.Backup = dest;
                        result.Status = VerifyApplied(target) ? "Done" : "Failed";
                        result.Message = result.Status == "Done" ? "文件已移动到备份。" : "复核失败：文件仍然存在。";
                    }
                }
                else if (target.Kind == "DisableService")
                {
                    string serviceFile = Path.Combine(Path.Combine(batchPath, "services"), SafeFileName(target.ServiceName) + ".json");
                    WriteText(serviceFile, GetServiceState(target.ServiceName));
                    result.Backup = serviceFile;
                    RunHidden("sc.exe", "config \"" + target.ServiceName + "\" start= disabled");
                    result.Status = VerifyApplied(target) ? "Done" : "Failed";
                    result.Message = result.Status == "Done" ? "服务已禁用。" : "复核失败：服务仍未禁用。";
                }
                else if (target.Kind == "DisableScheduledTask")
                {
                    string taskDir = Path.Combine(Path.Combine(batchPath, "tasks"), SafeFileName(target.TaskName));
                    Directory.CreateDirectory(taskDir);
                    WriteText(Path.Combine(taskDir, "task.xml"), QueryTaskXml(target.TaskName));
                    bool wasEnabled;
                    WriteText(Path.Combine(taskDir, "state.txt"), TryGetScheduledTaskEnabled(target.TaskName, out wasEnabled) && wasEnabled ? "Enabled" : "Disabled");
                    result.Backup = taskDir;
                    if (!WindowsTaskApi.SetEnabled(target.TaskName, false)) throw new InvalidOperationException("计划任务禁用失败。");
                    result.Status = VerifyApplied(target) ? "Done" : "Failed";
                    result.Message = result.Status == "Done" ? "计划任务已禁用。" : "复核失败：计划任务仍未禁用。";
                }
                else if (target.Kind == "InvokeUninstaller")
                {
                    ValidateTargetedUninstaller(target);
                    LaunchUninstaller(target.UninstallCommand);
                    result.Status = "Launched";
                    result.Message = "已打开独立附带产品“" + target.ExpectedProductName + "”的卸载器。没有卸载来源主程序；请确认产品名称后再决定，完成后重新扫描。";
                }
                else
                {
                    result.Status = "Skipped";
                    result.Message = "只报告，不自动清理。";
                }
            }
            catch (Exception ex)
            {
                result.Status = "Failed";
                result.Message = ex.Message;
                Logger.Error("清理失败：" + finding.UserVisibleName, ex);
            }
            return result;
        }

        public bool VerifyApplied(ActionTarget target)
        {
            if (target.Kind == "DeleteRegistryKey") return !RegistryHelper.KeyExists(target);
            if (target.Kind == "DeleteRegistryValue") return !RegistryHelper.ValueExists(target);
            if (target.Kind == "DisableShellExtension") return RegistryHelper.ValueExists(target);
            if (target.Kind == "MoveFileToBackup") return string.IsNullOrEmpty(target.FilePath) || !File.Exists(Environment.ExpandEnvironmentVariables(target.FilePath));
            if (target.Kind == "DisableService") return IsServiceDisabled(target.ServiceName);
            if (target.Kind == "InvokeUninstaller") return true;
            if (target.Kind == "DisableScheduledTask")
            {
                bool enabled;
                return TryGetScheduledTaskEnabled(target.TaskName, out enabled) && !enabled;
            }
            return true;
        }

        private string BackupRegistry(string batchPath, ActionTarget target)
        {
            string native = RegistryHelper.NativePath(target);
            string path = Path.Combine(Path.Combine(batchPath, "registry"), RegistryBackupFileName(target));
            int exitCode = RunHidden("reg.exe", "export \"" + native + "\" \"" + path + "\" /y" + RegistryViewArg(target));
            if (exitCode != 0) Logger.Error("注册表备份失败：" + native, new InvalidOperationException("reg export 退出码 " + exitCode));
            return File.Exists(path) ? path : null;
        }

        private static string RegistryViewArg(ActionTarget target)
        {
            if (target == null) return string.Empty;
            if (string.Equals(target.View, "Registry32", StringComparison.OrdinalIgnoreCase)) return " /reg:32";
            if (string.Equals(target.View, "Registry64", StringComparison.OrdinalIgnoreCase)) return " /reg:64";
            return string.Empty;
        }

        public RestoreBatchResult RestoreBatch(CleanupBatch batch)
        {
            RestoreBatchResult summary = new RestoreBatchResult
            {
                Messages = new List<string>()
            };
            if (batch == null || batch.Results == null) return summary;
            foreach (CleanupResult result in batch.Results)
            {
                summary.Total++;
                string message;
                bool ok = RestoreResult(batch, result, out message);
                if (ok)
                {
                    summary.Succeeded++;
                    if (result != null)
                    {
                        result.Status = "Restored";
                        result.Message = message;
                    }
                }
                else
                {
                    summary.Failed++;
                    if (result != null)
                    {
                        result.Status = "RestoreFailed";
                        result.Message = message;
                    }
                }
                if (!string.IsNullOrWhiteSpace(message)) summary.Messages.Add(message);
            }
            return summary;
        }

        public void RewriteBatchManifest(CleanupBatch batch)
        {
            if (batch == null || string.IsNullOrWhiteSpace(batch.Path) || batch.Results == null) return;
            batch.Results = batch.Results.Where(delegate(CleanupResult r)
            {
                return r == null || !string.Equals(r.Status, "Restored", StringComparison.OrdinalIgnoreCase);
            }).ToList();
            if (batch.Results.Count == 0)
            {
                DeleteBatchRecord(batch);
                return;
            }
            WriteJson(Path.Combine(batch.Path, "manifest.json"), batch);
        }

        public bool RestoreResult(CleanupResult result, out string message)
        {
            return RestoreResult(null, result, out message);
        }

        private bool RestoreResult(CleanupBatch batch, CleanupResult result, out string message)
        {
            message = string.Empty;
            if (result == null)
            {
                message = "空恢复项，已跳过。";
                return true;
            }
            if (!string.Equals(result.Status, "Done", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(result.Status, "RestoreFailed", StringComparison.OrdinalIgnoreCase))
            {
                message = result.Title + "：原清理结果为 " + result.Status + "，无需恢复。";
                return true;
            }
            try
            {
                if (result.Target == null || string.IsNullOrEmpty(result.Target.Kind))
                {
                    message = result.Title + "：缺少恢复目标。";
                    return false;
                }

                ActionTarget target = result.Target;
                string backup = ResolveExistingBackupPath(batch, result);
                if (target.Kind == "RestoreContextMenuToggle" && !string.IsNullOrEmpty(backup) && File.Exists(backup))
                {
                    bool restored = ContextMenuMutationService.Restore(backup);
                    message = result.Title + "：" + (restored ? "右键菜单状态已恢复。" : "右键菜单恢复后复核失败。");
                    return restored;
                }
                if (target.Kind == "DisableShellExtension" && !string.IsNullOrEmpty(backup) && File.Exists(backup))
                {
                    bool restored = ContextMenuMutationService.Restore(backup);
                    message = result.Title + "：" + (restored ? "右键扩展屏蔽状态已恢复。" : "右键扩展恢复后复核失败。");
                    return restored;
                }
                if (target.Kind == "RestoreContextMenuTree" && !string.IsNullOrEmpty(backup) && File.Exists(backup))
                {
                    bool restored = ContextMenuMutationService.RestoreTree(backup);
                    message = result.Title + "：" + (restored ? "右键菜单配置已恢复。" : "右键菜单配置恢复后复核失败。");
                    return restored;
                }
                if (target.Kind == "RestoreSpecialMenu" && !string.IsNullOrEmpty(backup) && File.Exists(backup))
                {
                    bool restored = SpecialContextMenuMutationService.Restore(backup);
                    message = result.Title + "：" + (restored ? "专用菜单配置已恢复。" : "专用菜单配置恢复后复核失败。");
                    return restored;
                }
                if (target.Kind == "RestoreAdvancedMenu" && !string.IsNullOrEmpty(backup) && File.Exists(backup))
                {
                    bool restored = AdvancedContextMenuMutationService.Restore(backup);
                    message = result.Title + "：" + (restored ? "高级菜单配置已恢复。" : "高级菜单恢复后复核失败。");
                    return restored;
                }
                if ((target.Kind == "DeleteRegistryKey" || target.Kind == "DeleteRegistryValue") &&
                    target != null)
                {
                    string registryBackup = ResolveRegistryBackupPath(batch, result, target);
                    if (string.IsNullOrEmpty(registryBackup))
                    {
                        message = result.Title + "：旧版清理记录没有找到注册表备份文件，无法完整恢复。";
                        return false;
                    }
                    int exitCode = RunHidden("reg.exe", "import \"" + registryBackup + "\"" + RegistryViewArg(target));
                    bool restored = target.Kind == "DeleteRegistryKey" ? RegistryHelper.KeyExists(target) : RegistryHelper.ValueExists(target);
                    message = result.Title + "：" + (restored ? "注册表已恢复。" : "注册表恢复后复核失败。reg import 退出码 " + exitCode);
                    return exitCode == 0 && restored;
                }
                if (target.Kind == "MoveFileToBackup" && !string.IsNullOrEmpty(backup) && File.Exists(backup))
                {
                    string dest = Environment.ExpandEnvironmentVariables(target.FilePath);
                    string parent = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    if (File.Exists(dest))
                    {
                        message = result.Title + "：原位置已经有同名文件，备份已保留，没有覆盖。";
                        return false;
                    }
                    File.Move(backup, dest);
                    bool restored = File.Exists(dest);
                    message = result.Title + "：" + (restored ? "文件已移回原位置。" : "文件恢复后复核失败。");
                    return restored;
                }
                if (target.Kind == "DisableService" && !string.IsNullOrEmpty(backup) && File.Exists(backup))
                {
                    string state = File.ReadAllText(backup, Encoding.UTF8);
                    string start = state.IndexOf("Auto", StringComparison.OrdinalIgnoreCase) >= 0 ? "auto" : (state.IndexOf("Disabled", StringComparison.OrdinalIgnoreCase) >= 0 ? "disabled" : "demand");
                    int exitCode = RunHidden("sc.exe", "config \"" + target.ServiceName + "\" start= " + start);
                    string restoredState = GetServiceState(target.ServiceName);
                    bool restored = start == "auto"
                        ? restoredState.Equals("Auto", StringComparison.OrdinalIgnoreCase)
                        : (start == "disabled" ? restoredState.Equals("Disabled", StringComparison.OrdinalIgnoreCase) : restoredState.Equals("Manual", StringComparison.OrdinalIgnoreCase));
                    message = result.Title + "：" + (restored ? "服务启动状态已恢复。" : "服务恢复后复核失败，当前状态 " + restoredState + "，命令退出码 " + exitCode);
                    return exitCode == 0 && restored;
                }
                if (target.Kind == "DisableScheduledTask" && !string.IsNullOrEmpty(backup) && Directory.Exists(backup))
                {
                    string xml = Path.Combine(backup, "task.xml");
                    string stateFile = Path.Combine(backup, "state.txt");
                    if (!ScheduledTaskExists(target.TaskName) && File.Exists(xml))
                    {
                        bool created = WindowsTaskApi.RegisterFromXml(target.TaskName, File.ReadAllText(xml));
                        if (!created)
                        {
                            message = result.Title + "：计划任务重建失败。";
                            return false;
                        }
                    }
                    string state = File.Exists(stateFile) ? File.ReadAllText(stateFile, Encoding.UTF8) : "Enabled";
                    bool shouldDisable = state.IndexOf("Disabled", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool changed = WindowsTaskApi.SetEnabled(target.TaskName, !shouldDisable);
                    bool enabled;
                    bool exists = TryGetScheduledTaskEnabled(target.TaskName, out enabled);
                    bool restored = exists && (shouldDisable ? !enabled : enabled);
                    message = result.Title + "：" + (restored ? "计划任务状态已恢复。" : "计划任务恢复后复核失败。");
                    return changed && restored;
                }

                message = result.Title + "：没有可用备份，无法恢复。";
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error("恢复失败：" + result.Title, ex);
                message = result.Title + "：" + ex.Message;
                return false;
            }
        }

        private string ResolveExistingBackupPath(CleanupBatch batch, CleanupResult result)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.Backup)) return null;
            string backup = Environment.ExpandEnvironmentVariables(result.Backup);
            if (File.Exists(backup) || Directory.Exists(backup)) return backup;
            if (batch != null && !string.IsNullOrWhiteSpace(batch.Path) && !Path.IsPathRooted(backup))
            {
                string combined = Path.Combine(batch.Path, backup);
                if (File.Exists(combined) || Directory.Exists(combined)) return combined;
            }
            if (batch != null && !string.IsNullOrWhiteSpace(batch.Path) && Directory.Exists(batch.Path))
            {
                string name = Path.GetFileName(backup);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    foreach (string candidate in Directory.GetFiles(batch.Path, name, SearchOption.AllDirectories))
                    {
                        if (File.Exists(candidate)) return candidate;
                    }
                    foreach (string candidate in Directory.GetDirectories(batch.Path, name, SearchOption.AllDirectories))
                    {
                        if (Directory.Exists(candidate)) return candidate;
                    }
                }
            }
            return null;
        }

        private string ResolveRegistryBackupPath(CleanupBatch batch, CleanupResult result, ActionTarget target)
        {
            string direct = ResolveExistingBackupPath(batch, result);
            if (!string.IsNullOrEmpty(direct) && direct.EndsWith(".reg", StringComparison.OrdinalIgnoreCase) && File.Exists(direct)) return direct;
            if (batch == null || string.IsNullOrWhiteSpace(batch.Path) || target == null) return null;

            string registryDir = Path.Combine(batch.Path, "registry");
            if (!Directory.Exists(registryDir)) return null;

            string currentName = RegistryBackupFileName(target);
            string currentPath = Path.Combine(registryDir, currentName);
            if (File.Exists(currentPath)) return currentPath;

            string legacyPath = Path.Combine(registryDir, LegacyRegistryBackupFileName(target));
            if (File.Exists(legacyPath)) return legacyPath;

            string needle = RegistryFileNeedle(target);
            if (!string.IsNullOrEmpty(needle))
            {
                foreach (string file in Directory.GetFiles(registryDir, "*.reg", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        string text = File.ReadAllText(file);
                        if (text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return file;
                    }
                    catch { }
                }
            }
            return null;
        }

        private static string RegistryBackupFileName(ActionTarget target)
        {
            return CompactBackupFileName(RegistryBackupRawName(target), ".reg");
        }

        private static string LegacyRegistryBackupFileName(ActionTarget target)
        {
            return SafeFileName(RegistryBackupRawName(target)) + ".reg";
        }

        private static string RegistryBackupRawName(ActionTarget target)
        {
            string backupName = RegistryHelper.NativePath(target);
            if (!string.IsNullOrEmpty(target.ValueName)) backupName += "__value__" + target.ValueName;
            return backupName;
        }

        private static string RegistryFileNeedle(ActionTarget target)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.SubKey)) return string.Empty;
            string hive = string.Equals(target.Hive, "HKLM", StringComparison.OrdinalIgnoreCase) ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER";
            return "[" + hive + "\\" + target.SubKey + "]";
        }

        private static string CompactBackupFileName(string raw, string extension)
        {
            string safe = SafeFileName(raw);
            if (safe.Length <= 120) return safe + extension;
            string prefix = safe.Substring(0, Math.Min(56, safe.Length));
            string suffix = safe.Substring(Math.Max(0, safe.Length - 44));
            return prefix + "__" + ShortHash(raw) + "__" + suffix + extension;
        }

        private static string ShortHash(string value)
        {
            using (SHA1 sha = SHA1.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length && builder.Length < 12; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        public long GetBatchStorageBytes(CleanupBatch batch)
        {
            if (batch == null) return 0;
            long total = DirectoryBytes(batch.Path);
            foreach (string report in BatchReportPaths(batch))
            {
                try { if (File.Exists(report)) total += new FileInfo(report).Length; } catch { }
            }
            return total;
        }

        public List<CleanupBatch> FindOldBatchRecords(IEnumerable<CleanupBatch> source, DateTime now, int keepLatest, int keepDays)
        {
            List<CleanupBatch> batches = (source ?? Enumerable.Empty<CleanupBatch>()).Where(delegate(CleanupBatch batch) { return batch != null; }).ToList();
            Dictionary<CleanupBatch, DateTime?> created = batches.ToDictionary(delegate(CleanupBatch batch) { return batch; }, ParseBatchCreatedAt);
            List<CleanupBatch> ordered = batches.OrderByDescending(delegate(CleanupBatch batch) { return created[batch] ?? DateTime.MaxValue; }).ThenByDescending(delegate(CleanupBatch batch) { return batch.Id; }).ToList();
            HashSet<CleanupBatch> newest = new HashSet<CleanupBatch>(ordered.Take(Math.Max(0, keepLatest)));
            DateTime cutoff = now.AddDays(-Math.Max(0, keepDays));
            return ordered.Where(delegate(CleanupBatch batch)
            {
                DateTime? date = created[batch];
                return !newest.Contains(batch) && date.HasValue && date.Value < cutoff;
            }).ToList();
        }

        public void DeleteBatchRecord(CleanupBatch batch)
        {
            if (batch == null || string.IsNullOrWhiteSpace(batch.Path)) return;
            string backupRootPath = Path.GetFullPath(store.Backups).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string backupRoot = backupRootPath + Path.DirectorySeparatorChar;
            string batchPath = Path.GetFullPath(batch.Path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(batchPath, backupRootPath, StringComparison.OrdinalIgnoreCase) || !batchPath.StartsWith(backupRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("恢复记录路径不在备份目录下，拒绝删除：" + batchPath);
            }
            List<string> reports = BatchReportPaths(batch);
            if (Directory.Exists(batchPath)) Directory.Delete(batchPath, true);
            foreach (string report in reports) if (File.Exists(report)) File.Delete(report);
            if (Directory.Exists(batchPath) || reports.Any(File.Exists)) throw new IOException("恢复记录删除后复核失败：" + batch.Id);
        }

        private DateTime? ParseBatchCreatedAt(CleanupBatch batch)
        {
            DateTime created;
            if (batch != null && DateTime.TryParse(batch.CreatedAt, out created)) return created;
            if (batch != null && DateTime.TryParseExact(batch.Id, "yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out created)) return created;
            try { if (batch != null && Directory.Exists(batch.Path)) return Directory.GetCreationTime(batch.Path); } catch { }
            return null;
        }

        private List<string> BatchReportPaths(CleanupBatch batch)
        {
            List<string> paths = new List<string>();
            if (batch == null || string.IsNullOrWhiteSpace(batch.Id)) return paths;
            string id = batch.Id.Trim();
            if (!string.Equals(Path.GetFileName(id), id, StringComparison.Ordinal) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidOperationException("恢复记录编号格式异常，拒绝删除关联报告：" + id);
            }
            string reportRootPath = Path.GetFullPath(store.Reports).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (string prefix in new string[] { "cleanup-", "context-menu-" })
            {
                string path = Path.GetFullPath(Path.Combine(reportRootPath, prefix + id + ".json"));
                if (!path.StartsWith(reportRootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("报告路径越界，拒绝删除：" + path);
                paths.Add(path);
            }
            return paths;
        }

        private static long DirectoryBytes(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return 0;
            long total = 0;
            try
            {
                foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(file).Length; } catch { }
                }
            }
            catch { }
            return total;
        }

        public List<CleanupBatch> LoadBatches()
        {
            List<CleanupBatch> list = new List<CleanupBatch>();
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            foreach (string manifest in Directory.GetFiles(store.Backups, "manifest.json", SearchOption.AllDirectories))
            {
                try
                {
                    CleanupBatch batch = serializer.Deserialize<CleanupBatch>(File.ReadAllText(manifest, Encoding.UTF8));
                    if (batch != null) list.Add(batch);
                }
                catch (Exception ex)
                {
                    Logger.Error("读取恢复清单失败：" + manifest, ex);
                }
            }
            return list.OrderByDescending(delegate(CleanupBatch b) { return b.Id; }).ToList();
        }

        private static string GetServiceState(string serviceName)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name,StartMode FROM Win32_Service WHERE Name='" + serviceName.Replace("'", "''") + "'"))
                {
                    foreach (ManagementObject obj in searcher.Get()) return Convert.ToString(obj["StartMode"]);
                }
            }
            catch { }
            return "Unknown";
        }

        private static bool IsServiceDisabled(string serviceName)
        {
            return string.Equals(GetServiceState(serviceName), "Disabled", StringComparison.OrdinalIgnoreCase);
        }

        private static string QueryTaskXml(string taskName)
        {
            string xml = WindowsTaskApi.GetXml(taskName); if (string.IsNullOrWhiteSpace(xml)) Logger.Error("备份计划任务失败：" + taskName, new InvalidOperationException("任务 XML 为空。")); return xml;
        }

        private static bool ScheduledTaskExists(string taskName)
        {
            bool enabled;
            return TryGetScheduledTaskEnabled(taskName, out enabled);
        }

        private static bool TryGetScheduledTaskEnabled(string taskName, out bool enabled)
        {
            return WindowsTaskApi.TryGetEnabled(taskName, out enabled);
        }

        private static bool TryGetScheduledTaskEnabledFromXml(string taskName, out bool enabled)
        {
            enabled = false;
            string xml = QueryTaskXml(taskName);
            if (string.IsNullOrWhiteSpace(xml) || xml.IndexOf("<Task", StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (xml.IndexOf("<Enabled>false</Enabled>", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                enabled = false;
                return true;
            }
            enabled = true;
            return true;
        }

        internal static void WriteJson(string path, object value)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            WriteText(path, serializer.Serialize(value));
        }

        private static void WriteText(string path, string text)
        {
            string fullPath = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tempPath = Path.Combine(dir, Path.GetFileName(fullPath) + ".tmp-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(tempPath, text ?? string.Empty, new UTF8Encoding(true));
            if (File.Exists(fullPath))
            {
                string backupPath = tempPath + ".bak";
                File.Replace(tempPath, fullPath, backupPath, true);
                try { File.Delete(backupPath); } catch { }
            }
            else
            {
                File.Move(tempPath, fullPath);
            }
        }

        private static int RunHidden(string file, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process process = Process.Start(psi))
                {
                    if (!process.WaitForExit(60000))
                    {
                        try { process.Kill(); } catch { }
                        Logger.Error("命令执行超时：" + file + " " + args, new TimeoutException("等待 60 秒仍未退出。"));
                        return -1;
                    }
                    return process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("命令执行失败：" + file + " " + args, ex);
                return -1;
            }
        }

        private static void LaunchUninstaller(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) throw new InvalidOperationException("没有卸载命令。");
            string file;
            string args;
            SplitCommandLine(Environment.ExpandEnvironmentVariables(command), out file, out args);
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = file;
            psi.Arguments = args;
            psi.UseShellExecute = true;
            Process.Start(psi);
        }

        private static void ValidateTargetedUninstaller(ActionTarget target)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.ExpectedProductName) || string.IsNullOrWhiteSpace(target.ExpectedUninstallCommand))
                throw new InvalidOperationException("缺少独立产品校验信息，拒绝打开卸载器；请重新扫描。");
            using (RegistryKey key = RegistryHelper.OpenSubKey(target, false))
            {
                if (key == null) throw new InvalidOperationException("对应附带产品的卸载项已不存在，请重新扫描。");
                string currentName = Convert.ToString(key.GetValue("DisplayName", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames));
                string currentPublisher = Convert.ToString(key.GetValue("Publisher", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames));
                string currentCommand = Convert.ToString(key.GetValue("UninstallString", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames));
                if (!string.Equals((currentName ?? string.Empty).Trim(), target.ExpectedProductName.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("卸载项产品名称已经变化，拒绝打开，避免卸载错软件；请重新扫描。");
                if (!string.IsNullOrWhiteSpace(target.ExpectedPublisher) && !string.Equals((currentPublisher ?? string.Empty).Trim(), target.ExpectedPublisher.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("卸载项厂商已经变化，拒绝打开，避免卸载错软件；请重新扫描。");
                if (!string.Equals((currentCommand ?? string.Empty).Trim(), target.ExpectedUninstallCommand.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("卸载命令已经变化，拒绝打开，避免卸载错软件；请重新扫描。");
            }
        }

        private static void SplitCommandLine(string command, out string file, out string args)
        {
            command = (command ?? string.Empty).Trim();
            file = command;
            args = string.Empty;
            if (command.Length == 0) return;
            if (command[0] == '"')
            {
                int close = command.IndexOf('"', 1);
                if (close > 0)
                {
                    file = command.Substring(1, close - 1);
                    args = command.Substring(close + 1).Trim();
                    return;
                }
            }
            foreach (string extension in new string[] { ".exe", ".cmd", ".bat", ".com" })
            {
                int exeEnd = command.IndexOf(extension, StringComparison.OrdinalIgnoreCase);
                if (exeEnd > 0)
                {
                    exeEnd += extension.Length;
                    file = command.Substring(0, exeEnd).Trim();
                    args = command.Substring(exeEnd).Trim();
                    return;
                }
            }
            int split = command.IndexOf(' ');
            if (split > 0)
            {
                file = command.Substring(0, split);
                args = command.Substring(split + 1).Trim();
            }
        }

        private static string SafeFileName(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return value.Replace('\\', '_').Replace('/', '_').Replace(':', '_');
        }
    }

#if VALIDATION
    internal sealed class ValidationReport
    {
        public string StartedAt { get; set; }
        public string CompletedAt { get; set; }
        public string ExecutableDirectory { get; set; }
        public bool IsAdministrator { get; set; }
        public bool AllRunnableCasesPassed { get; set; }
        public bool HasAdminSkippedCases { get; set; }
        public string Summary { get; set; }
        public List<ValidationCaseResult> Cases { get; set; }
    }

    internal sealed class ValidationCaseResult
    {
        public string Name { get; set; }
        public string Vendor { get; set; }
        public string Area { get; set; }
        public string Needle { get; set; }
        public bool RequiresAdmin { get; set; }
        public bool SetupSucceeded { get; set; }
        public string SetupMessage { get; set; }
        public bool DetectedBeforeClean { get; set; }
        public bool CleanVerified { get; set; }
        public bool RestoreVerified { get; set; }
        public string CleanupStatus { get; set; }
        public string Result { get; set; }
        public string Message { get; set; }
    }

    internal sealed class ValidationCase
    {
        public string Name;
        public string Vendor;
        public string Area;
        public string Needle;
        public bool RequiresAdmin;
        public bool ExpectPresentAfterCleanScan;
        public bool SetupSucceeded;
        public string SetupMessage;
        public Action Create;
        public Func<bool> Exists;
        public Func<bool> Cleaned;
        public Func<bool> Restored;
    }

    internal static class PermissionRegression
    {
        private const string DeniedSubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace";

        public static List<string> Run()
        {
            List<string> failures = new List<string>();
            try
            {
                RegistryHelper.TestOpenFailureInjector = delegate(ActionTarget target, bool writable)
                {
                    if (!writable && target != null && string.Equals(target.SubKey, DeniedSubKey, StringComparison.OrdinalIgnoreCase))
                    {
                        return new SecurityException("模拟受保护注册表拒绝读取。");
                    }
                    return null;
                };

                ScannerEngine engine = new ScannerEngine();
                engine.ScanAll(null);
                List<ScanWarning> scanWarnings = engine.Warnings;
                if (!scanWarnings.Any(delegate(ScanWarning warning)
                {
                    return string.Equals(warning.ErrorType, typeof(SecurityException).FullName, StringComparison.Ordinal) &&
                        warning.TechnicalLocation.IndexOf(DeniedSubKey, StringComparison.OrdinalIgnoreCase) >= 0;
                }))
                {
                    failures.Add("注入 SecurityException 后没有生成对应的跳过警告。");
                }
            }
            catch (AggregateException ex)
            {
                failures.Add("权限异常仍被 Parallel.Invoke 包装并终止整次扫描：" + ex);
            }
            catch (Exception ex)
            {
                failures.Add("权限回归验证异常：" + ex);
            }
            finally
            {
                RegistryHelper.TestOpenFailureInjector = null;
            }
            return failures;
        }
    }

    internal static class Issue8RegressionChecks
    {
        public static List<ValidationCaseResult> Run(DataStore store)
        {
            List<ValidationCaseResult> results = new List<ValidationCaseResult>();
            results.Add(CheckBackupFailureAbortsDelete(store));
            results.Add(CheckPartialRestoreRewritesManifest(store));
            results.Add(CheckCoreVerbAndSystemCommandProtection());
            return results;
        }

        private static ValidationCaseResult NewResult(string name, string area, string needle, bool pass, string message)
        {
            return new ValidationCaseResult
            {
                Name = name,
                Vendor = "Issue8",
                Area = area,
                Needle = needle,
                SetupSucceeded = true,
                SetupMessage = "纯内存/临时批次回归，不依赖模拟工件。",
                Result = pass ? "Pass" : "Fail",
                Message = message
            };
        }

        private static ValidationCaseResult CheckBackupFailureAbortsDelete(DataStore store)
        {
            try
            {
                string missingKey = @"Software\CodexRogueCleanerTest\MissingBackupKeyGuard_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                Finding finding = new Finding
                {
                    Id = 9001,
                    UserVisibleName = "备份失败保护测试",
                    Vendor = "Issue8",
                    Category = "回归测试",
                    ActionKind = "DeleteRegistryKey",
                    TechnicalLocation = @"HKCU\" + missingKey,
                    Target = new ActionTarget { Kind = "DeleteRegistryKey", Hive = "HKCU", SubKey = missingKey },
                    Selected = true
                };
                CleanerEngine cleaner = new CleanerEngine(store);
                CleanupBatch batch = cleaner.Clean(new List<Finding> { finding });
                CleanupResult result = batch.Results.Count > 0 ? batch.Results[0] : null;
                bool pass = result != null &&
                    string.Equals(result.Status, "Failed", StringComparison.OrdinalIgnoreCase) &&
                    (result.Message ?? string.Empty).IndexOf("备份失败", StringComparison.Ordinal) >= 0;
                try { cleaner.DeleteBatchRecord(batch); } catch { }
                return NewResult("注册表备份失败时必须取消删除", "恢复保护", "BackupFailAbort",
                    pass,
                    pass ? "reg export 失败（目标键不存在）时，清理结果为 Failed 且消息提示备份失败，未执行删除。"
                         : "备份失败仍继续删除：Status=" + (result == null ? "null" : result.Status) + ", Message=" + (result == null ? "null" : result.Message));
            }
            catch (Exception ex)
            {
                return NewResult("注册表备份失败时必须取消删除", "恢复保护", "BackupFailAbort", false, "异常：" + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static ValidationCaseResult CheckPartialRestoreRewritesManifest(DataStore store)
        {
            try
            {
                string id = "issue8-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                string batchPath = Path.Combine(store.Backups, id);
                Directory.CreateDirectory(batchPath);
                CleanupBatch batch = new CleanupBatch
                {
                    Id = id,
                    CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Path = batchPath,
                    Results = new List<CleanupResult>
                    {
                        new CleanupResult { Id = 1, Title = "无需恢复项", Status = "Skipped", Message = "未执行。", Target = new ActionTarget { Kind = "ReportOnly" } },
                        new CleanupResult { Id = 2, Title = "无备份失败项", Status = "Done", Message = "注册表键已删除。", Target = new ActionTarget { Kind = "DeleteRegistryKey", Hive = "HKCU", SubKey = @"Software\CodexRogueCleanerTest\MissingRestoreBackupGuard_" + Guid.NewGuid().ToString("N").Substring(0, 8) } }
                    }
                };
                CleanerEngine.WriteJson(Path.Combine(batchPath, "manifest.json"), batch);
                CleanerEngine cleaner = new CleanerEngine(store);
                RestoreBatchResult summary = cleaner.RestoreBatch(batch);
                bool summaryOk = summary.Total == 2 && summary.Succeeded == 1 && summary.Failed == 1;
                cleaner.RewriteBatchManifest(batch);
                bool memoryOk = batch.Results != null && batch.Results.Count == 1 &&
                    string.Equals(batch.Results[0].Status, "RestoreFailed", StringComparison.OrdinalIgnoreCase);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = int.MaxValue;
                CleanupBatch onDisk = serializer.Deserialize<CleanupBatch>(File.ReadAllText(Path.Combine(batchPath, "manifest.json"), Encoding.UTF8));
                bool diskOk = onDisk != null && onDisk.Results != null && onDisk.Results.Count == 1 &&
                    string.Equals(onDisk.Results[0].Status, "RestoreFailed", StringComparison.OrdinalIgnoreCase);
                bool pass = summaryOk && memoryOk && diskOk;
                try { cleaner.DeleteBatchRecord(batch); } catch { }
                return NewResult("恢复部分失败时回写清单并只保留失败项", "恢复中心", "PartialRestoreManifest",
                    pass,
                    pass ? "2 项批次恢复 1 成功 1 失败，manifest 回写后仅保留 1 个 RestoreFailed 项，可再次尝试。"
                         : "Summary=" + summary.Total + "/" + summary.Succeeded + "/" + summary.Failed + ", MemoryCount=" + (batch.Results == null ? -1 : batch.Results.Count) + ", DiskCount=" + (onDisk == null || onDisk.Results == null ? -1 : onDisk.Results.Count));
            }
            catch (Exception ex)
            {
                return NewResult("恢复部分失败时回写清单并只保留失败项", "恢复中心", "PartialRestoreManifest", false, "异常：" + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static ValidationCaseResult CheckCoreVerbAndSystemCommandProtection()
        {
            try
            {
                VendorIdentityResult confirmed = new VendorIdentityResult { Confirmed = true, Conflicted = false, Vendor = "Issue8" };
                ContextMenuEntry coreVerb = new ContextMenuEntry { Scene = "所有文件", Type = "Shell 命令", SubKey = @"Software\Classes\*\shell\open", AdvancedOnly = false, Enabled = true, Command = "\"%1\"" };
                ContextMenuDiagnosisDisposition coreDisp = ContextMenuDiagnosisPolicy.Classify(coreVerb, confirmed);
                bool coreProtected = coreDisp == ContextMenuDiagnosisDisposition.Ignore;
                ContextMenuEntry systemCmd = new ContextMenuEntry { Scene = "文件夹背景", Type = "Shell 命令", SubKey = @"Software\Classes\Directory\Background\shell\cmd", AdvancedOnly = false, Enabled = true, Command = @"%SystemRoot%\System32\cmd.exe /s /k pushd %V" };
                ContextMenuDiagnosisDisposition sysDisp = ContextMenuDiagnosisPolicy.Classify(systemCmd, confirmed);
                bool sysProtected = sysDisp == ContextMenuDiagnosisDisposition.SystemProtected;
                ContextMenuEntry thirdParty = new ContextMenuEntry { Scene = "所有文件", Type = "Shell 命令", SubKey = @"Software\Classes\*\shell\CodexRogueCleanerTest_WPS", AdvancedOnly = false, Enabled = true, Command = @"C:\Program Files\Kingsoft\WPS Office\wps.exe" };
                ContextMenuDiagnosisDisposition thirdDisp = ContextMenuDiagnosisPolicy.Classify(thirdParty, confirmed);
                bool thirdActionable = thirdDisp == ContextMenuDiagnosisDisposition.ActionableCommand;
                bool pass = coreProtected && sysProtected && thirdActionable;
                return NewResult("核心动词与系统命令保护", "右键菜单", "CoreVerbSystemGuard",
                    pass,
                    pass ? "open 核心动词被 Ignore，system32 命令为 SystemProtected，第三方命令仍可 ActionableCommand。"
                         : "core=" + coreDisp + ", sys=" + sysDisp + ", third=" + thirdDisp);
            }
            catch (Exception ex)
            {
                return NewResult("核心动词与系统命令保护", "右键菜单", "CoreVerbSystemGuard", false, "异常：" + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }

    internal static class ValidationRunner
    {
        private const string Marker = "CodexRogueCleanerTest";
        private const string TaskName = "\\CodexRogueCleanerTest_360Safe_Task";
        private const string ServiceName = "CodexRogueCleanerTest360Svc";
        private const string ShellExtensionClsid = "{C0DE2026-0806-4A20-8A00-50A0B10C0001}";
        private const string ShellExtensionBlockedKey = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
        private static readonly string[] TestKeys = new string[]
        {
            @"Software\Classes\Directory\Background\shell\CodexRogueCleanerTest_360Safe_RightMenu",
            @"Software\Classes\*\shell\CodexRogueCleanerTest_WPSPic_RightMenu",
            @"Software\Classes\Drive\shell\CodexRogueCleanerTest_kdesktop_WPSDisk",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace\{C0DE2026-0721-4A20-8A00-BA1D0E7D15C0}",
            @"Software\Google\Chrome\NativeMessagingHosts\com.codex.roguecleaner.BaiduNetdiskImageViewer",
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexRogueCleanerTest_SogouAdComponent",
            @"Software\Classes\*\shell\CodexRogueCleanerTest_DingTalkUpload",
            @"Software\Classes\CodexRogueCleanerTest.BaiduNetdiskImageViewer.open",
            @"Software\Classes\Directory\Background\shellex\ContextMenuHandlers\CodexRogueCleanerTest_SogouShellExt",
            @"Software\Classes\CLSID\{C0DE2026-0806-4A20-8A00-50A0B10C0001}"
        };

        public static int Run(DataStore store)
        {
            ValidationReport report = new ValidationReport();
            report.StartedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            report.ExecutableDirectory = Path.GetDirectoryName(Application.ExecutablePath);
            report.IsAdministrator = AdminUtil.IsAdministrator();
            List<ValidationCase> testCases = BuildCases(report.IsAdministrator);
            report.Cases = new List<ValidationCaseResult>();

            try
            {
                CleanupArtifacts(report.IsAdministrator);
                foreach (ValidationCase testCase in testCases)
                {
                    if (testCase.RequiresAdmin && !report.IsAdministrator)
                    {
                        testCase.SetupSucceeded = false;
                        testCase.SetupMessage = "需要管理员权限，当前未创建模拟工件。";
                        continue;
                    }
                    try
                    {
                        testCase.Create();
                        testCase.SetupSucceeded = testCase.Exists();
                        testCase.SetupMessage = testCase.SetupSucceeded ? "模拟工件创建成功。" : "创建命令返回后未回读到模拟工件。";
                    }
                    catch (Exception ex)
                    {
                        testCase.SetupSucceeded = false;
                        testCase.SetupMessage = ex.GetType().Name + ": " + ex.Message;
                    }
                }

                List<Finding> findings = new ScannerEngine().ScanAll(null);
                List<Finding> matched = findings
                    .Where(delegate(Finding f) { return ContainsTestMarker(f); })
                    .ToList();
                foreach (Finding finding in matched)
                {
                    finding.Selected = finding.CanClean;
                }

                CleanerEngine cleaner = new CleanerEngine(store);
                CleanupBatch batch = cleaner.Clean(matched);
                List<Finding> afterClean = new ScannerEngine().ScanAll(null);
                Dictionary<string, bool> cleanedStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, bool> absentAfterCleanScanStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                foreach (ValidationCase testCase in testCases)
                {
                    if (testCase.SetupSucceeded)
                    {
                        cleanedStates[testCase.Needle] = testCase.Cleaned();
                        absentAfterCleanScanStates[testCase.Needle] = !afterClean.Any(delegate(Finding f) { return Contains(f, testCase.Needle); });
                    }
                }
                cleaner.RestoreBatch(batch);
                List<Finding> afterRestore = new ScannerEngine().ScanAll(null);

                foreach (ValidationCase testCase in testCases)
                {
                    ValidationCaseResult result = BuildCaseResult(testCase);
                    if (testCase.RequiresAdmin && !report.IsAdministrator)
                    {
                        result.Result = "Skipped";
                        result.Message = "需要管理员权限创建和禁用模拟服务，本次非管理员运行，未执行。";
                        report.HasAdminSkippedCases = true;
                    }
                    else if (!testCase.SetupSucceeded)
                    {
                        result.Result = "SetupFailed";
                        result.Message = "模拟工件创建失败，未进入清理验证：" + testCase.SetupMessage;
                    }
                    else
                    {
                        result.DetectedBeforeClean = matched.Any(delegate(Finding f) { return Contains(f, testCase.Needle); });
                        CleanupResult cleanup = batch.Results.FirstOrDefault(delegate(CleanupResult r) { return Contains(r, testCase.Needle); });
                        result.CleanupStatus = cleanup == null ? "MissingCleanupResult" : cleanup.Status + ": " + cleanup.Message;
                        result.CleanVerified = cleanedStates.ContainsKey(testCase.Needle) && cleanedStates[testCase.Needle];
                        result.RestoreVerified = testCase.Restored();
                        bool absentAfterCleanScan = absentAfterCleanScanStates.ContainsKey(testCase.Needle) && absentAfterCleanScanStates[testCase.Needle];
                        bool presentAfterRestoreScan = afterRestore.Any(delegate(Finding f) { return Contains(f, testCase.Needle); });
                        bool cleanScanOk = testCase.ExpectPresentAfterCleanScan ? !absentAfterCleanScan : absentAfterCleanScan;
                        bool pass = result.DetectedBeforeClean && result.CleanVerified && result.RestoreVerified && cleanScanOk && presentAfterRestoreScan;
                        result.Result = pass ? "Pass" : "Fail";
                        result.Message = pass
                            ? (testCase.ExpectPresentAfterCleanScan ? "扫描命中、卸载器已启动、条目保留给用户卸载确认。" : "扫描命中、清理后回读消失、恢复后回读出现。")
                            : "验收失败：Detected=" + result.DetectedBeforeClean + ", Cleaned=" + result.CleanVerified + ", Restored=" + result.RestoreVerified + ", ScanAbsentAfterClean=" + absentAfterCleanScan + ", ExpectedPresentAfterCleanScan=" + testCase.ExpectPresentAfterCleanScan + ", ScanPresentAfterRestore=" + presentAfterRestoreScan;
                    }
                    report.Cases.Add(result);
                }

                foreach (ValidationCaseResult policyResult in Issue8RegressionChecks.Run(store))
                {
                    report.Cases.Add(policyResult);
                }

                report.AllRunnableCasesPassed = report.Cases.All(delegate(ValidationCaseResult c) { return c.Result == "Pass" || c.Result == "Skipped"; });
                report.Summary = "RunnableCases=" + report.Cases.Count(delegate(ValidationCaseResult c) { return c.Result != "Skipped"; }) +
                    ", Passed=" + report.Cases.Count(delegate(ValidationCaseResult c) { return c.Result == "Pass"; }) +
                    ", Failed=" + report.Cases.Count(delegate(ValidationCaseResult c) { return c.Result == "Fail"; }) +
                    ", SetupFailed=" + report.Cases.Count(delegate(ValidationCaseResult c) { return c.Result == "SetupFailed"; }) +
                    ", Skipped=" + report.Cases.Count(delegate(ValidationCaseResult c) { return c.Result == "Skipped"; });
                return report.Cases.Any(delegate(ValidationCaseResult c) { return c.Result == "Fail" || c.Result == "SetupFailed"; }) ? 1 : (report.HasAdminSkippedCases ? 2 : 0);
            }
            catch (Exception ex)
            {
                report.AllRunnableCasesPassed = false;
                report.Summary = "验收异常：" + ex.Message;
                Logger.Error("自动验收失败", ex);
                return 1;
            }
            finally
            {
                report.CompletedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string path = Path.Combine(store.Reports, "acceptance-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                CleanerEngine.WriteJson(path, report);
                CleanupArtifacts(report.IsAdministrator);
            }
        }

        private static List<ValidationCase> BuildCases(bool isAdmin)
        {
            List<ValidationCase> cases = new List<ValidationCase>();
            cases.Add(new ValidationCase
            {
                Name = "普通文件夹空白处右键：使用360测试右键菜单",
                Vendor = "360 系列",
                Area = "右键菜单",
                Needle = "CodexRogueCleanerTest_360Safe_RightMenu",
                Create = delegate { CreateRegistryKey(TestKeys[0], "使用360测试右键菜单", @"C:\CodexRogueCleanerTest\360Safe\360tray.exe"); },
                Exists = delegate { return RegistryKeyExists(TestKeys[0]); },
                Cleaned = delegate { return !RegistryKeyExists(TestKeys[0]); },
                Restored = delegate { return RegistryKeyExists(TestKeys[0]); }
            });
            cases.Add(new ValidationCase
            {
                Name = "普通文件右键：WPS 图片测试右键菜单",
                Vendor = "WPS / 金山",
                Area = "右键菜单",
                Needle = "CodexRogueCleanerTest_WPSPic_RightMenu",
                Create = delegate { CreateRegistryKey(TestKeys[1], "WPS图片测试右键菜单", @"C:\CodexRogueCleanerTest\Kingsoft\WPS Office\WPSPic.exe"); },
                Exists = delegate { return RegistryKeyExists(TestKeys[1]); },
                Cleaned = delegate { return !RegistryKeyExists(TestKeys[1]); },
                Restored = delegate { return RegistryKeyExists(TestKeys[1]); }
            });
            cases.Add(new ValidationCase
            {
                Name = "普通文件右键：上传钉钉并打开",
                Vendor = "钉钉",
                Area = "右键菜单",
                Needle = "CodexRogueCleanerTest_DingTalkUpload",
                Create = delegate { CreateRegistryKey(TestKeys[6], "上传钉钉并打开", @"cmd.exe /c exit 0"); },
                Exists = delegate { return RegistryKeyExists(TestKeys[6]); },
                Cleaned = delegate { return !RegistryKeyExists(TestKeys[6]); },
                Restored = delegate { return RegistryKeyExists(TestKeys[6]); }
            });
            cases.Add(new ValidationCase
            {
                Name = "Shell 扩展：受保护搜狗右键测试项",
                Vendor = "搜狗",
                Area = "右键菜单",
                Needle = "CodexRogueCleanerTest_SogouShellExt",
                Create = delegate { CreateShellExtension(); },
                Exists = delegate { return RegistryKeyExists(TestKeys[8]) && !RegistryValueExists(ShellExtensionBlockedKey, ShellExtensionClsid); },
                Cleaned = delegate { return RegistryKeyExists(TestKeys[8]) && RegistryValueExists(ShellExtensionBlockedKey, ShellExtensionClsid); },
                Restored = delegate { return RegistryKeyExists(TestKeys[8]) && !RegistryValueExists(ShellExtensionBlockedKey, ShellExtensionClsid); }
            });
            cases.Add(new ValidationCase
            {
                Name = "磁盘盘符右键：WPS 云盘/磁盘入口测试",
                Vendor = "WPS / 金山",
                Area = "右键菜单",
                Needle = "CodexRogueCleanerTest_kdesktop_WPSDisk",
                Create = delegate { CreateRegistryKey(TestKeys[2], "WPS云盘/磁盘入口测试", @"C:\CodexRogueCleanerTest\Kingsoft\WPS Office\kdesktop.exe"); },
                Exists = delegate { return RegistryKeyExists(TestKeys[2]); },
                Cleaned = delegate { return !RegistryKeyExists(TestKeys[2]); },
                Restored = delegate { return RegistryKeyExists(TestKeys[2]); }
            });
            cases.Add(new ValidationCase
            {
                Name = "此电脑入口：百度网盘测试图标",
                Vendor = "百度 / 百度网盘",
                Area = "此电脑/资源管理器入口",
                Needle = "CodexRogueCleanerTest_BaiduNetdisk_ThisPC",
                Create = delegate { CreateExplorerNamespaceKey(TestKeys[3], "百度网盘同步入口 " + Marker + "_BaiduNetdisk_ThisPC"); },
                Exists = delegate { return RegistryKeyExists(TestKeys[3]); },
                Cleaned = delegate { return !RegistryKeyExists(TestKeys[3]); },
                Restored = delegate { return RegistryKeyExists(TestKeys[3]); }
            });
            cases.Add(new ValidationCase
            {
                Name = "开机启动：搜狗弹窗测试项",
                Vendor = "搜狗",
                Area = "开机启动",
                Needle = "CodexRogueCleanerTest_SogouInputPop",
                Create = delegate { SetRegistryValue(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", "CodexRogueCleanerTest_SogouInputPop", @"C:\CodexRogueCleanerTest\Sogou\SogouInputPop.exe"); },
                Exists = delegate { return RegistryValueExists(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", "CodexRogueCleanerTest_SogouInputPop"); },
                Cleaned = delegate { return !RegistryValueExists(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", "CodexRogueCleanerTest_SogouInputPop"); },
                Restored = delegate { return RegistryValueExists(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", "CodexRogueCleanerTest_SogouInputPop"); }
            });
            cases.Add(new ValidationCase
            {
                Name = "开机启动：迅雷自启测试项",
                Vendor = "迅雷",
                Area = "开机启动",
                Needle = "CodexRogueCleanerTest_ThunderStart",
                Create = delegate { SetRegistryValue(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", "CodexRogueCleanerTest_ThunderStart", @"C:\CodexRogueCleanerTest\Xunlei\ThunderStart.exe"); },
                Exists = delegate { return RegistryValueExists(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", "CodexRogueCleanerTest_ThunderStart"); },
                Cleaned = delegate { return !RegistryValueExists(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", "CodexRogueCleanerTest_ThunderStart"); },
                Restored = delegate { return RegistryValueExists(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", "CodexRogueCleanerTest_ThunderStart"); }
            });
            cases.Add(new ValidationCase
            {
                Name = "浏览器插件/外部宿主：百度网盘看图测试项",
                Vendor = "百度 / 百度网盘",
                Area = "浏览器插件/外部宿主",
                Needle = "BaiduNetdiskImageViewer",
                Create = delegate { CreateNativeHostKey(TestKeys[4]); },
                Exists = delegate { return RegistryKeyExists(TestKeys[4]); },
                Cleaned = delegate { return !RegistryKeyExists(TestKeys[4]); },
                Restored = delegate { return RegistryKeyExists(TestKeys[4]); }
            });
            cases.Add(new ValidationCase
            {
                Name = "疑似捆绑组件：不能静默时弹出原厂卸载器",
                Vendor = "搜狗",
                Area = "疑似捆绑/弹窗组件",
                Needle = "CodexRogueCleanerTest_SogouAdComponent",
                ExpectPresentAfterCleanScan = true,
                Create = delegate { CreateUninstallEntry(TestKeys[5]); },
                Exists = delegate { return RegistryKeyExists(TestKeys[5]); },
                Cleaned = delegate { return WaitForFile(UninstallerMarkerPath(), 5000); },
                Restored = delegate { return RegistryKeyExists(TestKeys[5]); }
            });
            cases.Add(new ValidationCase
            {
                Name = ".png 打开方式：百度网盘看图测试项",
                Vendor = "百度 / 百度网盘",
                Area = "文件关联/打开方式",
                Needle = "CodexRogueCleanerTest.BaiduNetdiskImageViewer.open",
                Create = delegate { CreateOpenWithProgId(); },
                Exists = delegate { return RegistryValueExists(@"Software\Classes\.png\OpenWithProgids", "CodexRogueCleanerTest.BaiduNetdiskImageViewer.open"); },
                Cleaned = delegate { return !RegistryValueExists(@"Software\Classes\.png\OpenWithProgids", "CodexRogueCleanerTest.BaiduNetdiskImageViewer.open"); },
                Restored = delegate { return RegistryValueExists(@"Software\Classes\.png\OpenWithProgids", "CodexRogueCleanerTest.BaiduNetdiskImageViewer.open"); }
            });
            cases.Add(new ValidationCase
            {
                Name = "计划任务：360 定时拉起测试项",
                Vendor = "360 系列",
                Area = "计划任务/定时拉起",
                Needle = "CodexRogueCleanerTest_360Safe_Task",
                RequiresAdmin = true,
                Create = delegate { CreateScheduledTask(); },
                Exists = delegate { bool enabled; return TryGetScheduledTaskEnabled(TaskName, out enabled); },
                Cleaned = delegate { bool enabled; return TryGetScheduledTaskEnabled(TaskName, out enabled) && !enabled; },
                Restored = delegate { bool enabled; return TryGetScheduledTaskEnabled(TaskName, out enabled) && enabled; }
            });
            cases.Add(new ValidationCase
            {
                Name = "后台服务：360 服务测试项",
                Vendor = "360 系列",
                Area = "后台服务",
                Needle = ServiceName,
                RequiresAdmin = true,
                Create = delegate { CreateService(); },
                Exists = delegate { return ServiceExists(ServiceName); },
                Cleaned = delegate { return ServiceStartMode(ServiceName).Equals("Disabled", StringComparison.OrdinalIgnoreCase); },
                Restored = delegate { return ServiceStartMode(ServiceName).Equals("Manual", StringComparison.OrdinalIgnoreCase); }
            });
            return cases;
        }

        private static ValidationCaseResult BuildCaseResult(ValidationCase testCase)
        {
            return new ValidationCaseResult
            {
                Name = testCase.Name,
                Vendor = testCase.Vendor,
                Area = testCase.Area,
                Needle = testCase.Needle,
                RequiresAdmin = testCase.RequiresAdmin,
                SetupSucceeded = testCase.SetupSucceeded,
                SetupMessage = testCase.SetupMessage,
                Result = "Pending"
            };
        }

        private static void CleanupArtifacts(bool includeService)
        {
            foreach (string key in TestKeys) DeleteRegistryKey(key);
            DeleteRegistryValue(@"Software\Microsoft\Windows\CurrentVersion\Run", "CodexRogueCleanerTest_SogouInputPop");
            DeleteRegistryValue(@"Software\Microsoft\Windows\CurrentVersion\Run", "CodexRogueCleanerTest_ThunderStart");
            DeleteRegistryValue(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", "CodexRogueCleanerTest_SogouInputPop");
            DeleteRegistryValue(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", "CodexRogueCleanerTest_ThunderStart");
            DeleteRegistryValue(@"Software\Classes\.png\OpenWithProgids", "CodexRogueCleanerTest.BaiduNetdiskImageViewer.open");
            DeleteRegistryValue(ShellExtensionBlockedKey, ShellExtensionClsid);
            try { File.Delete(UninstallerMarkerPath()); } catch { }
            WindowsTaskApi.Delete(TaskName);
            WaitUntil(delegate { bool enabled; return !TryGetScheduledTaskEnabled(TaskName, out enabled); }, 5000);
            if (includeService)
            {
                RunProcess("sc.exe", "delete \"" + ServiceName + "\"");
                WaitUntil(delegate { return !ServiceExists(ServiceName); }, 5000);
            }
            try
            {
                string validationRoot = Path.Combine(Path.GetTempPath(), Marker);
                if (Directory.Exists(validationRoot)) Directory.Delete(validationRoot, true);
            }
            catch { }
        }

        private static bool ContainsTestMarker(Finding finding)
        {
            return Contains(finding, Marker) ||
                Contains(finding, "BaiduNetdiskImageViewer") ||
                Contains(finding, "SogouInputPop") ||
                Contains(finding, "ThunderStart");
        }

        private static bool Contains(Finding finding, string value)
        {
            if (finding == null) return false;
            string text = string.Join(" ", new string[] { finding.UserVisibleName, finding.UserImpact, finding.TechnicalLocation, finding.Evidence, finding.ActionKind });
            return text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool Contains(CleanupResult result, string value)
        {
            if (result == null) return false;
            string text = string.Join(" ", new string[] { result.Title, result.Category, result.TechnicalLocation, result.Message, result.ActionKind });
            if (result.Target != null) text = text + " " + result.Target.SubKey + " " + result.Target.ValueName + " " + result.Target.TaskName + " " + result.Target.ServiceName;
            return text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void CreateRegistryKey(string keyPath, string title, string command)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath))
            {
                key.SetValue("", title);
                key.SetValue("MUIVerb", title);
                key.SetValue("CodexMarker", Marker);
            }
            using (RegistryKey commandKey = Registry.CurrentUser.CreateSubKey(keyPath + "\\command"))
            {
                commandKey.SetValue("", command);
            }
        }

        private static void CreateNativeHostKey(string keyPath)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath))
            {
                key.SetValue("", @"C:\CodexRogueCleanerTest\BaiduNetdiskImageViewer.json");
                key.SetValue("Description", Marker + " BaiduNetdiskImageViewer");
            }
        }

        private static void CreateShellExtension()
        {
            using (RegistryKey handler = Registry.CurrentUser.CreateSubKey(TestKeys[8]))
            {
                handler.SetValue("", ShellExtensionClsid);
            }
            using (RegistryKey clsid = Registry.CurrentUser.CreateSubKey(TestKeys[9]))
            {
                clsid.SetValue("", "搜狗右键扩展 " + Marker);
            }
            using (RegistryKey server = Registry.CurrentUser.CreateSubKey(TestKeys[9] + @"\InprocServer32"))
            {
                server.SetValue("", @"C:\CodexRogueCleanerTest\Sogou\SogouShellExt.dll");
            }
        }

        private static void CreateOpenWithProgId()
        {
            SetRegistryValue(@"Software\Classes\.png\OpenWithProgids", "CodexRogueCleanerTest.BaiduNetdiskImageViewer.open", string.Empty);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(TestKeys[7] + @"\shell\open\command"))
            {
                key.SetValue("", @"C:\CodexRogueCleanerTest\BaiduNetdisk\BaiduNetdiskImageViewer.exe ""%1""");
            }
        }

        private static void CreateExplorerNamespaceKey(string keyPath, string title)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath))
            {
                key.SetValue("", title);
                key.SetValue("System.ItemNameDisplay", title);
                key.SetValue("TargetFolderPath", @"C:\CodexRogueCleanerTest\BaiduNetdisk");
                key.SetValue("CodexMarker", Marker);
            }
        }

        private static void CreateUninstallEntry(string keyPath)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath))
            {
                key.SetValue("DisplayName", "搜狗弹窗组件 " + Marker + "_SogouAdComponent");
                key.SetValue("Publisher", "搜狗");
                key.SetValue("SystemComponent", 1, RegistryValueKind.DWord);
                key.SetValue("InstallLocation", Path.Combine(Path.GetTempPath(), Marker));
                key.SetValue("DisplayIcon", Path.Combine(Path.GetTempPath(), Marker, "Sogou", "SogouAd.exe"));
                key.SetValue("UninstallString", "cmd.exe /c echo CodexRogueCleanerTest_Uninstaller_Launched> \"" + UninstallerMarkerPath() + "\"");
            }
        }

        private static string UninstallerMarkerPath()
        {
            return Path.Combine(Path.GetTempPath(), "CodexRogueCleanerTest_UninstallerLaunched.txt");
        }

        private static bool WaitForFile(string path, int timeoutMs)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (File.Exists(path)) return true;
                Thread.Sleep(100);
            }
            return File.Exists(path);
        }

        private static void SetRegistryValue(string keyPath, string name, string value)
        {
            try
            {
                Registry.SetValue(@"HKEY_CURRENT_USER\" + keyPath, name, value ?? string.Empty, RegistryValueKind.String);
            }
            catch (UnauthorizedAccessException)
            {
                string args = "add \"HKCU\\" + keyPath + "\" /v \"" + name + "\" /t REG_SZ /d \"" + (value ?? string.Empty) + "\" /f";
                int exitCode = RunProcess("reg.exe", args);
                if (exitCode != 0) throw;
            }
        }

        private static void DeleteRegistryKey(string keyPath)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(keyPath, false); } catch { }
        }

        private static void DeleteRegistryValue(string keyPath, string name)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath, true))
                {
                    if (key != null) key.DeleteValue(name, false);
                }
            }
            catch { }
        }

        private static bool RegistryKeyExists(string keyPath)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath, false))
            {
                return key != null;
            }
        }

        private static bool RegistryValueExists(string keyPath, string name)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath, false))
            {
                return key != null && key.GetValueNames().Any(delegate(string n) { return string.Equals(n, name, StringComparison.OrdinalIgnoreCase); });
            }
        }

        private static void CreateScheduledTask()
        {
            string executable = CreateValidationExecutable("360Safe", "360SafeTask.exe");
            if (!WindowsTaskApi.CreateValidationTask(TaskName, executable)) throw new InvalidOperationException("任务计划 COM 创建失败。");
            WaitUntil(delegate
            {
                bool enabled;
                return TryGetScheduledTaskEnabled(TaskName, out enabled);
            }, 5000);
        }

        private static bool WaitUntil(Func<bool> condition, int timeoutMs)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (condition()) return true;
                Thread.Sleep(100);
            }
            return condition();
        }

        private static string BenignWindowsExecutable()
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return Path.Combine(windows, "System32\\notepad.exe");
        }

        private static string CreateValidationExecutable(string vendorFolder, string fileName)
        {
            string path = Path.Combine(Path.GetTempPath(), Marker, vendorFolder, fileName);
            string parent = Path.GetDirectoryName(path);
            if (!Directory.Exists(parent)) Directory.CreateDirectory(parent);
            File.Copy(BenignWindowsExecutable(), path, true);
            return path;
        }

        private static void CreateService()
        {
            int exitCode = RunProcess("sc.exe", "create \"" + ServiceName + "\" binPath= \"cmd.exe /c exit 0\" DisplayName= \"360Safe CodexRogueCleanerTest Service\" start= demand");
            if (exitCode != 0) throw new InvalidOperationException("sc create 创建失败，退出码 " + exitCode);
            WaitUntil(delegate { return ServiceExists(ServiceName); }, 5000);
        }

        private static bool ServiceExists(string serviceName)
        {
            return !string.Equals(ServiceStartMode(serviceName), "Missing", StringComparison.OrdinalIgnoreCase);
        }

        private static string ServiceStartMode(string serviceName)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name,StartMode FROM Win32_Service WHERE Name='" + serviceName.Replace("'", "''") + "'"))
                {
                    foreach (ManagementObject obj in searcher.Get()) return Convert.ToString(obj["StartMode"]);
                }
            }
            catch { }
            return "Missing";
        }

        private static bool TryGetScheduledTaskEnabled(string taskName, out bool enabled)
        {
            return WindowsTaskApi.TryGetEnabled(taskName, out enabled);
        }

        private static string RunProcessWithOutput(string file, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(60000))
                    {
                        try { process.Kill(); } catch { }
                        return string.Empty;
                    }
                    return string.IsNullOrWhiteSpace(output) ? error : output;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static int RunProcess(string file, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process process = Process.Start(psi))
                {
                    process.WaitForExit(60000);
                    return process.ExitCode;
                }
            }
            catch
            {
                return -1;
            }
        }
    }

#endif

    internal sealed class MainForm : Form, IProgressSink
    {
        private readonly DataStore store;
        private readonly BindingList<Finding> rows = new BindingList<Finding>();
        private readonly DataGridView grid = new BufferedDataGridView();
        private readonly Label summaryLabel = new Label();
        private readonly Label statusLabel = new Label();
        private readonly Label versionLabel = new Label();
        private readonly Label authorLabel = new Label();
        private readonly LinkLabel author52PojieLink = new LinkLabel();
        private readonly LinkLabel authorGitHubLink = new LinkLabel();
        private readonly ToolTip authorLinkToolTip = new ToolTip();
        private readonly Dictionary<string, long> authorLinkLastOpened = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly Action<string> externalUrlLauncher;
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Button scanButton = new Button();
        private readonly Button cleanButton = new Button();
        private readonly Button restoreButton = new Button();
        private readonly Button reportButton = new Button();
        private readonly Button selectAllButton = new Button();
        private readonly Button lowButton = new Button();
        private readonly Button updateButton = new Button();
        private readonly Button adminButton = new Button();
        private readonly Button feedbackButton = new Button();
        private readonly Button aboutButton = new Button();
        private readonly Button overviewNavButton = new Button();
        private readonly Button startupNavButton = new Button();
        private readonly Button contextNavButton = new Button();
        private readonly Button diagnoseNavButton = new Button();
        private readonly Button recoveryNavButton = new Button();
        private readonly Label totalCardValue = new Label();
        private readonly Label suggestionCardValue = new Label();
        private readonly Label manageableCardValue = new Label();
        private readonly Label unknownCardValue = new Label();
        private readonly Label detailTitleLabel = new Label();
        private readonly Label detailMetaLabel = new Label();
        private readonly Label detailIdentityLabel = new Label();
        private readonly Label detailBehaviorLabel = new Label();
        private readonly Label detailReasonLabel = new Label();
        private readonly Label detailImpactLabel = new Label();
        private readonly TextBox detailLocationBox = new TextBox();
        private int gridDataErrorCount;
        private readonly Button copyDetailButton = new Button();
        private readonly SplitContainer contentSplit = new SplitContainer();
        private readonly TableLayoutPanel rootLayout = new TableLayoutPanel();
        private readonly TableLayoutPanel headerLayout = new TableLayoutPanel();
        private readonly FlowLayoutPanel headerActionsLayout = new FlowLayoutPanel();
        private readonly TableLayoutPanel commandLayout = new TableLayoutPanel();
        private readonly FlowLayoutPanel primaryActionsLayout = new FlowLayoutPanel();
        private readonly TableLayoutPanel footerLayout = new TableLayoutPanel();
        private readonly TableLayoutPanel authorAreaLayout = new TableLayoutPanel();
        private readonly TableLayoutPanel summaryCardsLayout = new TableLayoutPanel();
        private readonly TableLayoutPanel contentLayout = new TableLayoutPanel();
        private readonly Control[] summaryCards = new Control[4];
        private string latestEvidenceReportPath;
        private bool isBusy;
        private bool applyingResponsiveLayout;
        private string activeCategoryFilter = "总览";
        private ElevationResumeState startupResume;

        public MainForm(DataStore store)
            : this(store, true, null, null)
        {
        }

        internal MainForm(DataStore store, bool checkUpdates)
            : this(store, checkUpdates, null, null)
        {
        }

        internal MainForm(DataStore store, bool checkUpdates, Action<string> externalUrlLauncher)
            : this(store, checkUpdates, externalUrlLauncher, null)
        {
        }

        internal MainForm(DataStore store, bool checkUpdates, Action<string> externalUrlLauncher, ElevationResumeState startupResume)
        {
            this.store = store;
            this.externalUrlLauncher = externalUrlLauncher ?? LaunchExternalUrl;
            this.startupResume = startupResume;
            BuildUi();
            if (checkUpdates) UpdateChecker.CheckOnStartup(store, this);
            if (startupResume != null) Shown += delegate { BeginInvoke((MethodInvoker)ApplyStartupResume); };
        }

        private void BuildUi()
        {
            Text = AppMeta.ProductName + " " + AppMeta.Version;
            StartPosition = FormStartPosition.CenterScreen;
            // 900 x 500 logical pixels fits a 1920 x 1080 screen at 200% scaling;
            // the responsive layout keeps every primary operation visible there.
            MinimumSize = new Size(900, 500);
            Size = new Size(1360, 820);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiTheme.Canvas;
            Font = UiTheme.Font(9F, FontStyle.Regular);
            UiTheme.ApplyWindowIdentity(this);

            rootLayout.Dock = DockStyle.Fill;
            rootLayout.RowCount = 4;
            rootLayout.ColumnCount = 1;
            rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            rootLayout.Margin = new Padding(0);
            rootLayout.Padding = new Padding(0);
            Controls.Add(rootLayout);

            headerLayout.Dock = DockStyle.Fill;
            headerLayout.BackColor = UiTheme.Surface;
            headerLayout.ColumnCount = 2;
            headerLayout.RowCount = 1;
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            headerLayout.Padding = new Padding(22, 8, 18, 7);
            rootLayout.Controls.Add(headerLayout, 0, 0);

            FlowLayoutPanel brand = new FlowLayoutPanel();
            brand.Dock = DockStyle.Fill;
            brand.WrapContents = false;
            brand.AutoScroll = false;
            brand.FlowDirection = FlowDirection.LeftToRight;
            brand.Margin = new Padding(0);
            PictureBox brandIcon = new PictureBox { Size = new Size(30, 30), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 2, 10, 0) };
            try { using (Icon appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)) brandIcon.Image = appIcon == null ? null : appIcon.ToBitmap(); } catch { }
            brand.Controls.Add(brandIcon);
            Label title = new Label { Text = "流氓软件克星", AutoSize = true, ForeColor = UiTheme.Text, Font = UiTheme.Font(17F, FontStyle.Bold), Margin = new Padding(0, 4, 12, 0) };
            brand.Controls.Add(title);
            versionLabel.Text = "v" + AppMeta.Version;
            versionLabel.ForeColor = Color.White;
            versionLabel.BackColor = UiTheme.Primary;
            versionLabel.Font = UiTheme.Font(8.5F, FontStyle.Bold);
            versionLabel.TextAlign = ContentAlignment.MiddleCenter;
            versionLabel.AutoSize = false;
            versionLabel.Size = new Size(66, 24);
            versionLabel.Margin = new Padding(0, 7, 0, 0);
            brand.Controls.Add(versionLabel);
            headerLayout.Controls.Add(brand, 0, 0);

            headerActionsLayout.Dock = DockStyle.Fill;
            headerActionsLayout.AutoSize = false;
            headerActionsLayout.WrapContents = false;
            headerActionsLayout.AutoScroll = false;
            headerActionsLayout.FlowDirection = FlowDirection.RightToLeft;
            headerActionsLayout.Margin = new Padding(0);
            UiTheme.HeaderButton(adminButton, AdminUtil.IsAdministrator() ? "管理员模式" : "请求管理员权限");
            UiTheme.HeaderButton(feedbackButton, "反馈");
            UiTheme.HeaderButton(aboutButton, "关于");
            UiTheme.HeaderButton(updateButton, "检查更新");
            adminButton.Margin = feedbackButton.Margin = aboutButton.Margin = updateButton.Margin = new Padding(4, 4, 0, 0);
            headerActionsLayout.Controls.Add(adminButton);
            headerActionsLayout.Controls.Add(feedbackButton);
            headerActionsLayout.Controls.Add(aboutButton);
            headerActionsLayout.Controls.Add(updateButton);
            headerLayout.Controls.Add(headerActionsLayout, 1, 0);
            adminButton.Enabled = !AdminUtil.IsAdministrator();
            feedbackButton.Enabled = true;
            latestEvidenceReportPath = FindLatestEvidenceReport();

            commandLayout.Dock = DockStyle.Fill;
            commandLayout.BackColor = UiTheme.Surface;
            commandLayout.RowCount = 2;
            commandLayout.ColumnCount = 1;
            commandLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            commandLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            commandLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 3));
            commandLayout.Padding = new Padding(22, 11, 18, 0);
            rootLayout.Controls.Add(commandLayout, 0, 1);
            primaryActionsLayout.Dock = DockStyle.Fill;
            primaryActionsLayout.WrapContents = true;
            primaryActionsLayout.AutoScroll = false;
            primaryActionsLayout.Margin = new Padding(0);
            primaryActionsLayout.Padding = new Padding(0);
            UiTheme.HighlightButton(scanButton, "开始扫描");
            UiTheme.OutlineButton(cleanButton, "清理勾选", UiTheme.Danger);
            UiTheme.OutlineButton(selectAllButton, "勾选可清理", UiTheme.Primary);
            UiTheme.OutlineButton(lowButton, "只勾低风险", UiTheme.Success);
            UiTheme.OutlineButton(restoreButton, "恢复中心", Color.FromArgb(79, 70, 229));
            UiTheme.OutlineButton(reportButton, "证据报告", UiTheme.Info);
            foreach (Button actionButton in new Button[] { scanButton, cleanButton, selectAllButton, lowButton, restoreButton, reportButton })
            {
                actionButton.AutoSize = false;
                actionButton.Size = new Size(128, 40);
                actionButton.MinimumSize = new Size(128, 40);
                actionButton.MaximumSize = new Size(128, 40);
                actionButton.Margin = new Padding(0, 0, 8, 0);
                actionButton.Padding = new Padding(10, 0, 10, 0);
                actionButton.TextAlign = ContentAlignment.MiddleCenter;
                actionButton.ImageAlign = ContentAlignment.MiddleLeft;
            }
            primaryActionsLayout.Controls.Add(scanButton);
            primaryActionsLayout.Controls.Add(cleanButton);
            primaryActionsLayout.Controls.Add(selectAllButton);
            primaryActionsLayout.Controls.Add(lowButton);
            primaryActionsLayout.Controls.Add(restoreButton);
            primaryActionsLayout.Controls.Add(reportButton);
            commandLayout.Controls.Add(primaryActionsLayout, 0, 0);
            progress.Dock = DockStyle.Fill;
            progress.Margin = new Padding(0);
            progress.Style = ProgressBarStyle.Continuous;
            progress.Visible = false;
            commandLayout.Controls.Add(progress, 0, 1);

            TableLayoutPanel workspace = new TableLayoutPanel();
            workspace.Dock = DockStyle.Fill;
            workspace.ColumnCount = 2;
            workspace.RowCount = 1;
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            workspace.Margin = new Padding(0);
            rootLayout.Controls.Add(workspace, 0, 2);
            workspace.Controls.Add(BuildNavigation(), 0, 0);

            contentLayout.Dock = DockStyle.Fill;
            contentLayout.ColumnCount = 1;
            contentLayout.RowCount = 3;
            contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            contentLayout.Padding = new Padding(14, 12, 14, 12);
            contentLayout.Margin = new Padding(0);
            workspace.Controls.Add(contentLayout, 1, 0);

            summaryCardsLayout.Dock = DockStyle.Fill;
            summaryCardsLayout.ColumnCount = 4;
            summaryCardsLayout.RowCount = 1;
            summaryCardsLayout.Margin = new Padding(0, 0, 0, 4);
            summaryCards[0] = CreateSummaryCard("发现项目", totalCardValue, UiTheme.Info, "本次扫描总数");
            summaryCards[1] = CreateSummaryCard("建议处理", suggestionCardValue, UiTheme.Danger, "存在可恢复处理动作");
            summaryCards[2] = CreateSummaryCard("可管理", manageableCardValue, UiTheme.Success, "正常第三方或低风险项");
            summaryCards[3] = CreateSummaryCard("仅提示 / 未知", unknownCardValue, UiTheme.Muted, "不进入批量清理");
            foreach (Control card in summaryCards) summaryCardsLayout.Controls.Add(card);
            contentLayout.Controls.Add(summaryCardsLayout, 0, 0);

            TableLayoutPanel filterBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1, BackColor = UiTheme.Canvas, Margin = new Padding(0) };
            filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            summaryLabel.Dock = DockStyle.Fill;
            summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
            summaryLabel.ForeColor = UiTheme.Muted;
            summaryLabel.Text = "未扫描。";
            filterBar.Controls.Add(summaryLabel, 0, 0);
            contentLayout.Controls.Add(filterBar, 0, 1);

            grid.Dock = DockStyle.Fill;
            grid.AutoGenerateColumns = false;
            grid.DataSource = rows;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.ReadOnly = true;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = true;
            grid.EditMode = DataGridViewEditMode.EditProgrammatically;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            grid.RowTemplate.Height = Math.Max(36, GridRowHeight());
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = Math.Max(40, GridHeaderHeight());
            grid.BackgroundColor = UiTheme.Surface;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = UiTheme.Border;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.Text;
            grid.ColumnHeadersDefaultCellStyle.Font = UiTheme.Font(9F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.DefaultCellStyle.BackColor = UiTheme.Surface;
            grid.DefaultCellStyle.ForeColor = UiTheme.Text;
            grid.DefaultCellStyle.SelectionBackColor = UiTheme.PrimarySoft;
            grid.DefaultCellStyle.SelectionForeColor = UiTheme.Text;
            grid.DefaultCellStyle.Padding = new Padding(5, 0, 5, 0);
            grid.ShowCellToolTips = true;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "Selected", HeaderText = string.Empty, Width = 34, MinimumWidth = 34, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, TrueValue = true, FalseValue = false, ThreeState = false, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewImageColumn { DataPropertyName = "SoftwareIcon", HeaderText = string.Empty, Width = 30, MinimumWidth = 30, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, ImageLayout = DataGridViewImageCellLayout.Normal, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { NullValue = SoftwarePresentationResolver.PlaceholderIcon } });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "RiskDisplay", HeaderText = "风险", Width = 64, MinimumWidth = 64, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "CompactTitle", HeaderText = "项目", FillWeight = 220, MinimumWidth = 110, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Vendor", HeaderText = "软件", FillWeight = 115, MinimumWidth = 65, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "CompactLocation", HeaderText = "位置", FillWeight = 95, MinimumWidth = 54, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "CompactImpact", HeaderText = "影响", FillWeight = 146, MinimumWidth = 62, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "CompactAction", HeaderText = "处理", Width = 78, MinimumWidth = 74, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, ReadOnly = true });
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }

            contentSplit.Dock = DockStyle.Fill;
            contentSplit.Orientation = Orientation.Vertical;
            contentSplit.FixedPanel = FixedPanel.Panel2;
            contentSplit.SplitterWidth = 8;
            contentSplit.BackColor = UiTheme.Canvas;
            CardPanel gridCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = new Padding(0) };
            UiTheme.AttachModernScrollBar(gridCard, grid);
            contentSplit.Panel1.Controls.Add(gridCard);
            contentSplit.Panel2.Controls.Add(BuildDetailPanel());
            contentLayout.Controls.Add(contentSplit, 0, 2);

            footerLayout.Dock = DockStyle.Fill;
            footerLayout.BackColor = Color.FromArgb(238, 242, 247);
            footerLayout.ColumnCount = 2;
            footerLayout.RowCount = 1;
            footerLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            footerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            footerLayout.Margin = new Padding(0);
            rootLayout.Controls.Add(footerLayout, 0, 3);
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.Margin = new Padding(0);
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Padding = new Padding(14, 0, 0, 0);
            statusLabel.ForeColor = UiTheme.Muted;
            statusLabel.AutoEllipsis = true;
            statusLabel.Text = "就绪。数据目录：" + store.Root;
            footerLayout.Controls.Add(statusLabel, 0, 0);

            authorAreaLayout.Dock = DockStyle.Fill;
            authorAreaLayout.ColumnCount = 3;
            authorAreaLayout.RowCount = 1;
            authorAreaLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            authorAreaLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            authorAreaLayout.Margin = new Padding(0);
            authorAreaLayout.Padding = new Padding(0, 2, 10, 2);
            authorAreaLayout.BackColor = footerLayout.BackColor;
            authorAreaLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
            authorAreaLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
            authorAreaLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            authorLabel.Dock = DockStyle.Fill;
            authorLabel.AutoSize = false;
            authorLabel.Text = "作者：" + AppMeta.AuthorName;
            authorLabel.TextAlign = ContentAlignment.MiddleRight;
            authorLabel.ForeColor = UiTheme.Muted;
            authorLabel.AutoEllipsis = true;
            authorLabel.Cursor = Cursors.Default;
            ConfigureAuthorDestination(author52PojieLink, "吾爱破解", "RogueCleanerV2.Assets.52PojieFavicon", "打开作者的吾爱破解主页");
            ConfigureAuthorDestination(authorGitHubLink, "GitHub", "RogueCleanerV2.Assets.GitHubFavicon", "打开作者的 GitHub 主页");
            authorAreaLayout.Controls.Add(authorLabel, 0, 0);
            authorAreaLayout.Controls.Add(author52PojieLink, 1, 0);
            authorAreaLayout.Controls.Add(authorGitHubLink, 2, 0);
            footerLayout.Controls.Add(authorAreaLayout, 1, 0);

            scanButton.Click += delegate { StartScan(); };
            cleanButton.Click += delegate { StartClean(); };
            selectAllButton.Click += delegate { SetAll(true); };
            lowButton.Click += delegate { SelectLowRisk(); };
            restoreButton.Click += delegate { new RecoveryCenterForm(store).ShowDialog(this); };
            reportButton.Click += delegate { OpenEvidenceReport(); };
            updateButton.Click += delegate { UpdateChecker.CheckNow(store, this, true); };
            adminButton.Click += delegate
            {
                AdminUtil.RelaunchAsAdmin(this, store, new ElevationResumeState { Page = activeCategoryFilter });
            };
            feedbackButton.Click += delegate { ShowFeedbackForCurrentRow(); };
            aboutButton.Click += delegate { using (AboutForm form = new AboutForm()) form.ShowDialog(this); };
            rows.ListChanged += delegate { UpdateSummary(); };
            grid.DataError += GridDataError;
            grid.CellToolTipTextNeeded += GridCellToolTipTextNeeded;
            grid.CellFormatting += GridCellFormatting;
            grid.CellClick += GridCellClick;
            grid.MouseDown += GridMouseDown;
            grid.SelectionChanged += delegate { feedbackButton.Enabled = !isBusy; UpdateDetails(); };
            grid.KeyDown += GridKeyDown;
            author52PojieLink.Click += delegate { OpenAuthorDestination(AppMeta.Author52PojieUrl); };
            authorGitHubLink.Click += delegate { OpenAuthorDestination(AppMeta.AuthorGitHubUrl); };
            copyDetailButton.Click += delegate { CopyCurrentDetails(); };
            SizeChanged += delegate { ApplyResponsiveLayout(); };
            summaryCardsLayout.SizeChanged += delegate { ApplyResponsiveLayout(); };
            contentSplit.SizeChanged += delegate { ApplyResponsiveLayout(); };
            Shown += delegate
            {
                ApplyResponsiveLayout();
                UpdateDetails();
            };
        }

        private void ApplyResponsiveLayout()
        {
            if (applyingResponsiveLayout || summaryCardsLayout.IsDisposed || contentLayout.IsDisposed || contentSplit.IsDisposed || rootLayout.IsDisposed) return;
            applyingResponsiveLayout = true;
            try
            {
                int logicalWidth = UiTheme.LogicalPixels(this, Math.Max(1, ClientSize.Width));
                int logicalHeight = UiTheme.LogicalPixels(this, Math.Max(1, ClientSize.Height));
                bool twoRows = logicalWidth < 1120;
                bool compactHeight = logicalHeight < 570;
                int columnCount = twoRows ? 2 : 4;
                int rowCount = twoRows ? 2 : 1;
                int summaryHeight = UiTheme.DpiPixels(this, twoRows ? 112 : 62);

                // At 200% a 1080p display offers roughly 960 x 540 logical pixels.
                // Keep navigation and all six primary actions on screen instead of
                // relying on hidden horizontal/vertical scrolling.
                // The compact header must still accommodate a 40 px header action
                // plus its vertical margin.  Derive the FlowLayoutPanel height from
                // the actual table client area below so border/non-client rounding
                // cannot push it outside its parent at high DPI.
                rootLayout.RowStyles[0].Height = UiTheme.DpiPixels(this, compactHeight ? 60 : 64);
                rootLayout.RowStyles[1].Height = UiTheme.DpiPixels(this, compactHeight ? 64 : 66);
                rootLayout.RowStyles[3].Height = UiTheme.DpiPixels(this, compactHeight ? 30 : 34);
                // At compact logical widths the four header actions need a little
                // more than 63% once their 2x-DPI button margins are included.
                // Keep the brand complete, but give the action bar 67% so its
                // left-most update button never falls outside the RTL flow panel.
                headerLayout.ColumnStyles[0].Width = logicalWidth < 1000 ? 33F : 48F;
                headerLayout.ColumnStyles[1].Width = logicalWidth < 1000 ? 67F : 52F;
                bool compactFooter = logicalWidth < 1000;
                footerLayout.ColumnStyles[0].Width = compactFooter ? 100F : 65F;
                footerLayout.ColumnStyles[1].Width = compactFooter ? 0F : 35F;
                authorAreaLayout.Visible = !compactFooter;
                headerLayout.Padding = new Padding(UiTheme.DpiPixels(this, compactHeight ? 14 : 22), UiTheme.DpiPixels(this, compactHeight ? 5 : 8), UiTheme.DpiPixels(this, compactHeight ? 12 : 18), UiTheme.DpiPixels(this, compactHeight ? 4 : 7));
                commandLayout.Padding = new Padding(UiTheme.DpiPixels(this, compactHeight ? 14 : 22), UiTheme.DpiPixels(this, compactHeight ? 7 : 11), UiTheme.DpiPixels(this, compactHeight ? 12 : 18), 0);
                commandLayout.RowStyles[1].Height = UiTheme.DpiPixels(this, 3);
                rootLayout.PerformLayout();
                int headerContentHeight = Math.Max(1, headerLayout.ClientSize.Height - headerLayout.Padding.Vertical);
                headerActionsLayout.MinimumSize = Size.Empty;
                headerActionsLayout.MaximumSize = new Size(0, headerContentHeight);
                headerActionsLayout.Height = headerContentHeight;
                int commandContentHeight = Math.Max(1, commandLayout.ClientSize.Height - commandLayout.Padding.Top - (int)Math.Round(commandLayout.RowStyles[1].Height));
                primaryActionsLayout.MinimumSize = Size.Empty;
                primaryActionsLayout.MaximumSize = new Size(0, commandContentHeight);
                primaryActionsLayout.Height = commandContentHeight;
                contentLayout.Padding = new Padding(UiTheme.DpiPixels(this, compactHeight ? 10 : 14), UiTheme.DpiPixels(this, compactHeight ? 8 : 12), UiTheme.DpiPixels(this, compactHeight ? 10 : 14), UiTheme.DpiPixels(this, compactHeight ? 8 : 12));

                summaryCardsLayout.MinimumSize = new Size(0, summaryHeight);
                summaryCardsLayout.Height = summaryHeight;
                summaryCardsLayout.SuspendLayout();
                summaryCardsLayout.ColumnStyles.Clear();
                summaryCardsLayout.RowStyles.Clear();
                summaryCardsLayout.ColumnCount = columnCount;
                summaryCardsLayout.RowCount = rowCount;
                for (int column = 0; column < columnCount; column++) summaryCardsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / columnCount));
                for (int row = 0; row < rowCount; row++) summaryCardsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / rowCount));
                for (int index = 0; index < summaryCards.Length; index++)
                {
                    summaryCardsLayout.SetColumn(summaryCards[index], index % columnCount);
                    summaryCardsLayout.SetRow(summaryCards[index], index / columnCount);
                }
                summaryCardsLayout.ResumeLayout(true);
                contentLayout.SuspendLayout();
                contentLayout.RowStyles.Clear();
                contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, summaryHeight));
                contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, UiTheme.DpiPixels(this, compactHeight ? 38 : 46)));
                contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                contentLayout.ResumeLayout(true);
                contentLayout.PerformLayout();
                summaryCardsLayout.PerformLayout();
                if (contentLayout.Parent != null) contentLayout.Parent.PerformLayout();

                int available = Math.Max(1, contentSplit.Width - contentSplit.SplitterWidth);
                int availableLogical = UiTheme.LogicalPixels(this, available);
                if (availableLogical < 480) return;
                int splitterLogical = UiTheme.LogicalPixels(this, contentSplit.SplitterWidth);
                int detailWidth = availableLogical < 760 ? 190 : Math.Min(245, Math.Max(190, availableLogical / 3));
                int panel2Minimum = detailWidth;
                int panel1Minimum = Math.Min(500, Math.Max(280, availableLogical - detailWidth - splitterLogical));
                contentSplit.Panel2MinSize = UiTheme.DpiPixels(this, panel2Minimum);
                contentSplit.Panel1MinSize = UiTheme.DpiPixels(this, panel1Minimum);
                int minimum = contentSplit.Panel1MinSize;
                int maximum = Math.Max(minimum, available - contentSplit.Panel2MinSize);
                int preferred = Math.Max(minimum, available - UiTheme.DpiPixels(this, detailWidth));
                contentSplit.SplitterDistance = Math.Max(minimum, Math.Min(maximum, preferred));
            }
            finally { applyingResponsiveLayout = false; }
        }

        private Control BuildNavigation()
        {
            CardPanel nav = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(0), Margin = new Padding(0) };
            TableLayoutPanel items = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(0, 16, 0, 0), Margin = new Padding(0) };
            items.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 5; i++) items.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            items.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            UiTheme.NavButton(overviewNavButton, "总览");
            UiTheme.NavButton(startupNavButton, "启动项管理");
            UiTheme.NavButton(contextNavButton, "右键管理");
            UiTheme.NavButton(diagnoseNavButton, "弹窗 / 流氓诊断");
            UiTheme.NavButton(recoveryNavButton, "恢复中心");
            foreach (Button button in new Button[] { overviewNavButton, startupNavButton, contextNavButton, diagnoseNavButton, recoveryNavButton }) button.Dock = DockStyle.Fill;
            items.Controls.Add(overviewNavButton, 0, 0);
            items.Controls.Add(startupNavButton, 0, 1);
            items.Controls.Add(contextNavButton, 0, 2);
            items.Controls.Add(diagnoseNavButton, 0, 3);
            items.Controls.Add(recoveryNavButton, 0, 4);
            nav.Controls.Add(items);
            SetNavigation("总览");
            overviewNavButton.Click += delegate { SetNavigation("总览"); };
            startupNavButton.Click += delegate { SetNavigation("启动项"); };
            contextNavButton.Click += delegate
            {
                SetNavigation("右键");
                using (ContextMenuManagerForm form = new ContextMenuManagerForm(store)) form.ShowDialog(this);
            };
            diagnoseNavButton.Click += delegate { SetNavigation("诊断"); };
            recoveryNavButton.Click += delegate { new RecoveryCenterForm(store).ShowDialog(this); };
            return nav;
        }

        private Control CreateSummaryCard(string title, Label valueLabel, Color color, string note)
        {
            SummaryCardPanel card = new SummaryCardPanel { Dock = DockStyle.Fill, MinimumSize = new Size(0, 44), Margin = new Padding(0, 0, 10, 0), Padding = new Padding(0) };
            valueLabel.Text = "0";
            valueLabel.AutoSize = false;
            valueLabel.BackColor = Color.Transparent;
            valueLabel.ForeColor = color;
            valueLabel.Font = UiTheme.Font(13F, FontStyle.Bold);
            valueLabel.TextAlign = ContentAlignment.TopCenter;
            valueLabel.Margin = new Padding(0);
            Label titleLabel = new Label { Text = title, BackColor = Color.Transparent, ForeColor = UiTheme.Text, Font = UiTheme.Font(8F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            Label noteLabel = new Label { Text = note, BackColor = Color.Transparent, ForeColor = UiTheme.Muted, Font = UiTheme.Font(7F, FontStyle.Regular), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            card.Controls.Add(valueLabel);
            card.Controls.Add(titleLabel);
            card.Controls.Add(noteLabel);
            Action layout = delegate
            {
                int width = Math.Max(1, card.ClientSize.Width);
                int height = Math.Max(1, card.ClientSize.Height);
                int left = UiTheme.DpiPixels(this, 8);
                int valueWidth = UiTheme.DpiPixels(this, 42);
                int textLeft = UiTheme.DpiPixels(this, 56);
                int top = UiTheme.DpiPixels(this, 7);
                int titleHeight = UiTheme.DpiPixels(this, 18);
                valueLabel.SetBounds(left, top, valueWidth, Math.Max(UiTheme.DpiPixels(this, 24), height - UiTheme.DpiPixels(this, 9)));
                titleLabel.SetBounds(textLeft, UiTheme.DpiPixels(this, 1), Math.Max(1, width - UiTheme.DpiPixels(this, 62)), titleHeight);
                noteLabel.SetBounds(textLeft, UiTheme.DpiPixels(this, 20), Math.Max(1, width - UiTheme.DpiPixels(this, 62)), titleHeight);
            };
            card.SizeChanged += delegate { layout(); };
            layout();
            return card;
        }

        private Control BuildDetailPanel()
        {
            CardPanel panel = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(0), Margin = new Padding(0) };
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            detailTitleLabel.Dock = DockStyle.Top;
            detailTitleLabel.Height = 26;
            detailTitleLabel.Font = UiTheme.Font(10.5F, FontStyle.Bold);
            detailTitleLabel.ForeColor = UiTheme.Text;
            detailTitleLabel.AutoEllipsis = true;
            detailMetaLabel.Dock = DockStyle.Fill;
            detailMetaLabel.ForeColor = UiTheme.Muted;
            detailMetaLabel.AutoEllipsis = true;
            Panel detailHead = new Panel { Dock = DockStyle.Fill };
            detailHead.Controls.Add(detailMetaLabel);
            detailHead.Controls.Add(detailTitleLabel);
            layout.Controls.Add(detailHead, 0, 0);

            ModernScrollPanel sections = new ModernScrollPanel { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0) };
            TableLayoutPanel sectionStack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = false, ColumnCount = 1, RowCount = 5, Margin = new Padding(0), Padding = new Padding(0), Height = 530 };
            sectionStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sectionStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
            sectionStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            sectionStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));
            sectionStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));
            sectionStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 106));
            Control identityCard = CreateDetailCard("身份依据", detailIdentityLabel, 106);
            Control behaviorCard = CreateDetailCard("行为事实", detailBehaviorLabel, 102);
            Control reasonCard = CreateDetailCard("建议原因", detailReasonLabel, 88);
            Control impactCard = CreateDetailCard("处理影响", detailImpactLabel, 88);
            foreach (Control card in new Control[] { identityCard, behaviorCard, reasonCard, impactCard }) card.Dock = DockStyle.Fill;
            sectionStack.Controls.Add(identityCard, 0, 0);
            sectionStack.Controls.Add(behaviorCard, 0, 1);
            sectionStack.Controls.Add(reasonCard, 0, 2);
            sectionStack.Controls.Add(impactCard, 0, 3);
            CardPanel locationCard = new CardPanel { Dock = DockStyle.Fill, Height = 96, Margin = new Padding(0, 0, 0, 8), Padding = new Padding(10, 7, 10, 8) };
            Label locationTitle = new Label { Text = "技术位置", Dock = DockStyle.Top, Height = 21, ForeColor = UiTheme.Primary, Font = UiTheme.Font(8.5F, FontStyle.Bold) };
            detailLocationBox.Dock = DockStyle.Fill;
            detailLocationBox.Multiline = true;
            detailLocationBox.ReadOnly = true;
            detailLocationBox.BorderStyle = BorderStyle.None;
            detailLocationBox.BackColor = UiTheme.Surface;
            detailLocationBox.ForeColor = UiTheme.Muted;
            detailLocationBox.ScrollBars = ScrollBars.Vertical;
            locationCard.Controls.Add(detailLocationBox);
            locationCard.Controls.Add(locationTitle);
            sectionStack.Controls.Add(locationCard, 0, 4);
            sections.SetContent(sectionStack);
            layout.Controls.Add(sections, 0, 1);
            UiTheme.OutlineButton(copyDetailButton, "复制详情", UiTheme.Primary);
            copyDetailButton.Dock = DockStyle.Fill;
            copyDetailButton.Margin = new Padding(0, 6, 0, 0);
            layout.Controls.Add(copyDetailButton, 0, 2);
            panel.Controls.Add(layout);
            sections.SizeChanged += delegate
            {
                int width = Math.Max(210, sections.ContentWidth);
                sectionStack.Width = width;
            };
            return panel;
        }

        private static Control CreateDetailCard(string title, Label body, int height)
        {
            CardPanel card = new CardPanel { Width = 250, Height = height, Margin = new Padding(0, 0, 0, 8), Padding = new Padding(10, 7, 10, 8) };
            Label titleLabel = new Label { Text = title, Dock = DockStyle.Top, Height = 21, ForeColor = UiTheme.Primary, Font = UiTheme.Font(8.5F, FontStyle.Bold) };
            body.Dock = DockStyle.Fill;
            body.ForeColor = UiTheme.Text;
            body.Font = UiTheme.Font(8.5F, FontStyle.Regular);
            body.AutoEllipsis = true;
            body.TextAlign = ContentAlignment.TopLeft;
            card.Controls.Add(body);
            card.Controls.Add(titleLabel);
            return card;
        }

        private void GridCellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (e.ColumnIndex == 0) ToggleRowSelection(e.RowIndex);
        }

        private void GridMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            DataGridView.HitTestInfo hit = grid.HitTest(e.X, e.Y);
            if (hit.RowIndex < 0 || hit.RowIndex >= grid.Rows.Count) return;
            grid.ClearSelection();
            grid.Rows[hit.RowIndex].Selected = true;
            grid.CurrentCell = grid.Rows[hit.RowIndex].Cells[Math.Max(0, hit.ColumnIndex)];
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("加入/移除本地白名单", null, delegate { ToggleCurrentWhitelist(); });
            menu.Show(grid, new Point(e.X, e.Y));
        }

        private void ShowFeedbackForRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count) return;
            Finding finding = grid.Rows[rowIndex].DataBoundItem as Finding;
            if (finding == null) return;
            using (FeedbackForm form = new FeedbackForm(store, finding)) form.ShowDialog(this);
        }

        private void ToggleCurrentWhitelist()
        {
            Finding finding = grid.CurrentRow == null ? null : grid.CurrentRow.DataBoundItem as Finding;
            if (finding == null)
            {
                int count = UserWhitelistStore.Load(store).Count;
                MessageBox.Show(this, "当前白名单共有 " + count + " 项。请在结果表中右键项目后选择“加入/移除本地白名单”。", "本地白名单", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            bool listed = UserWhitelistStore.Load(store).Any(delegate(UserWhitelistEntry entry) { return string.Equals(entry.Key, UserWhitelistStore.KeyFor(finding), StringComparison.OrdinalIgnoreCase); });
            if (listed)
            {
                if (MessageBox.Show(this, "移除“" + finding.UserVisibleName + "”的本地白名单？下次扫描将按普通规则重新判断。", "移除白名单", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                UserWhitelistStore.Remove(store, finding);
                statusLabel.Text = "已移除白名单；请重新扫描以恢复正常判断。";
                return;
            }
            if (MessageBox.Show(this, "将“" + finding.UserVisibleName + "”加入本地白名单？\n\n程序仍会在下次扫描中保留证据展示，但不会建议处理或参与批量清理。", "加入白名单", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            if (UserWhitelistStore.Add(store, finding))
            {
                UserWhitelistStore.Apply(store, new Finding[] { finding });
                grid.Refresh();
                UpdateSummary();
                UpdateDetails();
                statusLabel.Text = "已加入本地白名单；证据保留，项目不会参与清理。";
            }
        }

        private void ShowFeedbackForCurrentRow()
        {
            if (grid.CurrentRow != null)
            {
                ShowFeedbackForRow(grid.CurrentRow.Index);
                return;
            }
            Finding empty = new Finding
            {
                Risk = "未判断",
                Vendor = "未知",
                Category = "未扫描到的项目",
                UserVisibleName = "未提供",
                UserImpact = "请在反馈说明中描述遗漏的启动项、右键项或组件。",
                Evidence = "没有关联现有扫描结果。",
                ActionKind = "ReportOnly",
                Target = new ActionTarget { Kind = "ReportOnly" }
            };
            using (FeedbackForm form = new FeedbackForm(store, empty)) form.ShowDialog(this);
        }

        private void GridKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Space || grid.CurrentCell == null || grid.CurrentCell.RowIndex < 0) return;
            ToggleRowSelection(grid.CurrentCell.RowIndex);
            e.Handled = true;
        }

        private void ToggleRowSelection(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count) return;
            Finding finding = grid.Rows[rowIndex].DataBoundItem as Finding;
            if (finding == null) return;
            if (!finding.CanClean)
            {
                finding.Selected = false;
                statusLabel.Text = finding.SelectionHint + " 鼠标悬停这一行可以看完整原因。";
                grid.InvalidateRow(rowIndex);
                UpdateSummary();
                return;
            }
            finding.Selected = !finding.Selected;
            statusLabel.Text = (finding.Selected ? "已勾选：" : "已取消：") + finding.UserVisibleName;
            grid.InvalidateRow(rowIndex);
            UpdateSummary();
        }

        private void GridCellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
            Finding finding = grid.Rows[e.RowIndex].DataBoundItem as Finding;
            if (finding == null) return;
            e.ToolTipText =
                WrapTooltipLine("勾选：", finding.SelectionHint) + Environment.NewLine +
                WrapTooltipLine("用户会看到：", finding.UserVisibleName) + Environment.NewLine +
                WrapTooltipLine("影响：", finding.UserImpact) + Environment.NewLine +
                WrapTooltipLine("处理：", finding.ActionText) + Environment.NewLine +
                WrapTooltipLine("位置：", finding.TechnicalLocation) + Environment.NewLine +
                WrapTooltipLine("证据：", finding.Evidence);
        }

        private void GridCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count || e.ColumnIndex < 0 || e.ColumnIndex >= grid.Columns.Count) return;
            Finding finding = grid.Rows[e.RowIndex].DataBoundItem as Finding;
            DataGridViewColumn column = grid.Columns[e.ColumnIndex];
            if (finding != null && !finding.CanClean)
            {
                e.CellStyle.BackColor = Color.FromArgb(248, 250, 252);
                e.CellStyle.ForeColor = Color.FromArgb(100, 116, 139);
                e.CellStyle.SelectionBackColor = Color.FromArgb(226, 232, 240);
                e.CellStyle.SelectionForeColor = Color.FromArgb(51, 65, 85);
            }
            if (!string.Equals(column.DataPropertyName, "RiskDisplay", StringComparison.OrdinalIgnoreCase))
            {
                e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                return;
            }
            string risk = Convert.ToString(e.Value);
            if (risk == "仅提示")
            {
                e.CellStyle.BackColor = Color.FromArgb(241, 245, 249);
                e.CellStyle.ForeColor = Color.FromArgb(71, 85, 105);
            }
            else if (risk == "高")
            {
                e.CellStyle.BackColor = Color.FromArgb(254, 226, 226);
                e.CellStyle.ForeColor = Color.FromArgb(185, 28, 28);
            }
            else if (risk == "中")
            {
                e.CellStyle.BackColor = Color.FromArgb(255, 237, 213);
                e.CellStyle.ForeColor = Color.FromArgb(194, 65, 12);
            }
            else if (risk == "低")
            {
                e.CellStyle.BackColor = Color.FromArgb(220, 252, 231);
                e.CellStyle.ForeColor = Color.FromArgb(21, 128, 61);
            }
            e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }

        private void GridDataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            // BindingList 批量刷新时 CurrencyManager 可能在一个消息循环内仍保留旧行索引。
            // 该状态会在绑定重置完成后自行恢复，不应弹出 WinForms 默认异常对话框。
            gridDataErrorCount++;
            e.ThrowException = false;
            e.Cancel = true;
        }

        private static string WrapTooltipLine(string label, string value)
        {
            return WrapTooltipText(label + (value ?? string.Empty), 86);
        }

        private static string WrapTooltipText(string text, int width)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            StringBuilder builder = new StringBuilder();
            int line = 0;
            foreach (char c in text.Replace("\r", string.Empty))
            {
                if (c == '\n')
                {
                    builder.AppendLine();
                    line = 0;
                    continue;
                }
                if (line >= width)
                {
                    builder.AppendLine();
                    line = 0;
                }
                builder.Append(c);
                line++;
                if (line >= 52 && (c == '\\' || c == '/' || c == ';' || c == '；' || c == '，' || c == '。'))
                {
                    builder.AppendLine();
                    line = 0;
                }
            }
            return builder.ToString().TrimEnd();
        }

        private void ConfigureAuthorDestination(LinkLabel link, string text, string resourceName, string toolTip)
        {
            link.Dock = DockStyle.Fill;
            link.AutoSize = false;
            link.Text = text;
            link.TextAlign = ContentAlignment.MiddleCenter;
            link.ImageAlign = ContentAlignment.MiddleLeft;
            link.Padding = new Padding(26, 0, 6, 0);
            link.Margin = new Padding(4, 0, 0, 0);
            link.LinkColor = UiTheme.Info;
            link.ActiveLinkColor = Color.FromArgb(29, 78, 216);
            link.VisitedLinkColor = UiTheme.Info;
            link.LinkBehavior = LinkBehavior.HoverUnderline;
            link.Cursor = Cursors.Hand;
            link.AutoEllipsis = true;
            link.Image = LoadEmbeddedAuthorIcon(resourceName);
            authorLinkToolTip.SetToolTip(link, toolTip);
        }

        private static Image LoadEmbeddedAuthorIcon(string resourceName)
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return null;
                    try
                    {
                        using (Icon icon = new Icon(stream, new Size(20, 20))) return icon.ToBitmap();
                    }
                    catch
                    {
                        stream.Position = 0;
                        using (Image image = Image.FromStream(stream)) return new Bitmap(image, new Size(20, 20));
                    }
                }
            }
            catch { return null; }
        }

        private void OpenAuthorDestination(string url)
        {
            long now = Stopwatch.GetTimestamp();
            long previous;
            if (authorLinkLastOpened.TryGetValue(url, out previous))
            {
                double elapsedMilliseconds = (now - previous) * 1000D / Stopwatch.Frequency;
                if (elapsedMilliseconds < 800D) return;
            }
            authorLinkLastOpened[url] = now;
            try
            {
                externalUrlLauncher(url);
            }
            catch
            {
                authorLinkLastOpened.Remove(url);
            }
        }

        private static void LaunchExternalUrl(string url)
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                authorLinkToolTip.Dispose();
                if (author52PojieLink.Image != null) author52PojieLink.Image.Dispose();
                if (authorGitHubLink.Image != null) authorGitHubLink.Image.Dispose();
            }
            base.Dispose(disposing);
        }

        private int GridRowHeight()
        {
            return Math.Max(34, TextRenderer.MeasureText("国", grid.Font).Height + 16);
        }

        private int GridHeaderHeight()
        {
            return Math.Max(40, TextRenderer.MeasureText("国", grid.ColumnHeadersDefaultCellStyle.Font ?? Font).Height + 18);
        }

        private void StartScan(Action afterScan = null)
        {
            SetBusy(true, "扫描中：多线程翻注册表、服务、计划任务和浏览器角落。");
            string scanStartedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Stopwatch scanWatch = Stopwatch.StartNew();
            ReplaceRows(new Finding[0]);
            Task.Factory.StartNew(delegate
            {
                try
                {
                    ScannerEngine engine = new ScannerEngine();
                    List<Finding> result = engine.ScanAll(this);
                    List<ScanWarning> scanWarnings = engine.Warnings;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        scanWatch.Stop();
                        ReplaceRows(result);
                        latestEvidenceReportPath = CleanupEngineWriteScanReport(result, scanWarnings);
                        string warningText = scanWarnings.Count == 0 ? string.Empty : "；另有 " + scanWarnings.Count + " 个受保护位置无法读取";
                        SetBusy(false, "扫描完成，用时 " + scanWatch.Elapsed.TotalSeconds.ToString("0.0") + " 秒。发现 " + result.Count + " 项" + warningText + "。证据报告：" + Path.GetFileName(latestEvidenceReportPath));
                        if (afterScan != null) afterScan();
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error("扫描失败", ex);
                    string errorReport = null;
                    try
                    {
                        errorReport = WriteScanErrorReport(ex, scanStartedAt);
                        latestEvidenceReportPath = errorReport;
                    }
                    catch (Exception reportEx)
                    {
                        Logger.Error("写入扫描失败报告失败", reportEx);
                    }
                    BeginInvoke((MethodInvoker)delegate
                    {
                        scanWatch.Stop();
                        string suffix = string.IsNullOrWhiteSpace(errorReport) ? string.Empty : "\n\n已写入失败报告：\n" + errorReport;
                        SetBusy(false, "扫描失败：" + ex.Message + (string.IsNullOrWhiteSpace(errorReport) ? string.Empty : "。失败报告：" + Path.GetFileName(errorReport)));
                        MessageBox.Show(ex.Message + suffix + "\n\n也可以点击“打开证据报告”查看最近一次报告。", "扫描失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    });
                }
            });
        }

        private void StartClean()
        {
            grid.EndEdit();
            int reportOnly = rows.Count(delegate(Finding f) { return f.Selected && !f.CanClean; });
            List<Finding> selected = rows.Where(delegate(Finding f) { return f.Selected && f.CanClean; }).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(reportOnly > 0 ? "你勾到的是“仅提示”项目。默认打开程序、无可靠卸载命令或正在运行的进程不会参与一键清理，避免误改默认应用或硬删主程序。" : "还没勾选任何可清理项目。", AppMeta.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (selected.Any(delegate(Finding f) { return f.RequiresAdmin; }) && !AdminUtil.IsAdministrator())
            {
                DialogResult elevate = MessageBox.Show("你勾选的项目里有后台服务、系统注册表或计划任务，需要管理员权限。\n\n是否请求 Windows 管理员权限？重启后会自动重新扫描并恢复这些勾选，但不会自动执行清理。\n\n如果没有管理员账号或单位策略禁止，可以选择“否”，继续使用普通模式并导出证据报告。", "需要管理员权限", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (elevate == DialogResult.Yes)
                {
                    AdminUtil.RelaunchAsAdmin(this, store, new ElevationResumeState
                    {
                        Page = activeCategoryFilter,
                        ScanAfterLaunch = true,
                        SelectedFindingKeys = selected.Select(ElevationResumeState.FindingKey).Where(delegate(string key) { return !string.IsNullOrWhiteSpace(key); }).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                    });
                }
                return;
            }
            int high = selected.Count(delegate(Finding f) { return f.Risk == "高"; });
            int uninstallers = selected.Count(delegate(Finding f) { return f.ActionKind == "InvokeUninstaller"; });
            string uninstallNote = uninstallers > 0 ? "\n\n其中 " + uninstallers + " 项会在产品名、厂商和卸载命令复核一致后，只打开该独立附带产品自己的卸载器；不会打开来源主程序卸载器，也不会自动确认卸载。" : string.Empty;
            DialogResult answer = MessageBox.Show("准备处理 " + selected.Count + " 项，高风险 " + high + " 项。" + uninstallNote + "\n\n会先备份、再处理、最后复核和复扫。继续？", "确认处理", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            SetBusy(true, "处理中：先备份，再动手，最后复核。");
            Task.Factory.StartNew(delegate
            {
                try
                {
                    CleanerEngine cleaner = new CleanerEngine(store);
                    CleanupBatch batch = cleaner.Clean(selected);
                    ScannerEngine scanner = new ScannerEngine();
                    List<Finding> refreshed = scanner.ScanAll(null);
                    List<ScanWarning> scanWarnings = scanner.Warnings;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        // 清理后的自动复扫必须与手动扫描共用同一绑定入口，确保身份和图标重新异步解析。
                        ReplaceRows(refreshed);
                        int failed = batch.Results.Count(delegate(CleanupResult r) { return r.Status == "Failed" || r.Status == "RestoreFailed"; });
                        int launched = batch.Results.Count(delegate(CleanupResult r) { return r.Status == "Launched"; });
                        string warningText = scanWarnings.Count == 0 ? string.Empty : "；" + scanWarnings.Count + " 个受保护位置未读取";
                        SetBusy(false, failed > 0 ? "处理后复核发现残留：" + failed + " 项" + warningText + "。" : "处理完成，已自动复扫" + warningText + "。");
                        MessageBox.Show("成功清理：" + batch.Results.Count(delegate(CleanupResult r) { return r.Status == "Done"; }) + " 项\n已弹出卸载器：" + launched + " 项\n失败/残留：" + failed + " 项\n跳过：" + batch.Results.Count(delegate(CleanupResult r) { return r.Status == "Skipped"; }) + " 项\n\n备份目录：" + batch.Path, failed > 0 ? "处理后仍有残留" : "处理完成", MessageBoxButtons.OK, failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error("清理失败", ex);
                    BeginInvoke((MethodInvoker)delegate { SetBusy(false, "清理失败：" + ex.Message); MessageBox.Show(ex.Message, "清理失败", MessageBoxButtons.OK, MessageBoxIcon.Error); });
                }
            });
        }

        public void Stage(string text)
        {
            if (!IsHandleCreated) return;
            try { BeginInvoke((MethodInvoker)delegate { statusLabel.Text = text; }); } catch { }
        }

        public void Finding(Finding finding)
        {
            // 扫描线程只报告阶段；结果完成后一次性绑定，避免扫描期间反复重绘整张表。
        }

        private void ReplaceRows(IEnumerable<Finding> findings)
        {
            List<Finding> materialized = findings == null ? new List<Finding>() : findings.ToList();
            UserWhitelistStore.Apply(store, materialized);
            foreach (Finding finding in materialized)
            {
                if (finding.SoftwareIcon == null) finding.SoftwareIcon = SoftwarePresentationResolver.PlaceholderIcon;
                if (string.IsNullOrEmpty(finding.SoftwareName)) finding.SoftwareName = "正在识别…";
            }
            rows.RaiseListChangedEvents = false;
            try
            {
                rows.Clear();
                foreach (Finding finding in materialized) rows.Add(finding);
            }
            finally
            {
                rows.RaiseListChangedEvents = true;
                rows.ResetBindings();
            }
            UpdateSummary();
            UpdateDetails();
            SoftwarePresentationQueue.Hydrate(this, materialized, delegate { grid.Invalidate(); UpdateDetails(); });
        }

        private void SetBusy(bool busy, string status)
        {
            isBusy = busy;
            scanButton.Enabled = !busy;
            cleanButton.Enabled = !busy;
            restoreButton.Enabled = !busy;
            selectAllButton.Enabled = !busy;
            lowButton.Enabled = !busy;
            reportButton.Enabled = !busy;
            updateButton.Enabled = !busy;
            adminButton.Enabled = !busy && !AdminUtil.IsAdministrator();
            feedbackButton.Enabled = !busy;
            progress.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            progress.MarqueeAnimationSpeed = busy ? 25 : 0;
            progress.Visible = busy;
            progress.Value = busy ? 0 : 0;
            Cursor = Cursors.Default;
            UseWaitCursor = false;
            statusLabel.Text = status;
        }

        private void UpdateSummary()
        {
            int selected = rows.Count(delegate(Finding f) { return f.Selected && f.CanClean; });
            int high = rows.Count(delegate(Finding f) { return f.CanClean && f.Risk == "高"; });
            int medium = rows.Count(delegate(Finding f) { return f.CanClean && f.Risk == "中"; });
            int low = rows.Count(delegate(Finding f) { return f.CanClean && f.Risk == "低"; });
            int reportOnly = rows.Count(delegate(Finding f) { return !f.CanClean; });
            int cleanable = high + medium + low;
            int manageable = Math.Max(0, rows.Count - cleanable - reportOnly);
            totalCardValue.Text = rows.Count.ToString();
            suggestionCardValue.Text = cleanable.ToString();
            manageableCardValue.Text = manageable.ToString();
            unknownCardValue.Text = reportOnly.ToString();
            summaryLabel.Text = "共 " + rows.Count + " 项  ·  可清理 " + cleanable + "  ·  已勾选 " + selected + "  ·  高 " + high + " / 中 " + medium + " / 低 " + low + "  ·  仅提示 " + reportOnly;
        }

        private void SetAll(bool value)
        {
            foreach (Finding finding in rows) finding.Selected = value && finding.BulkSelectable;
            grid.Refresh();
            UpdateSummary();
        }

        private void SelectLowRisk()
        {
            foreach (Finding finding in rows) finding.Selected = finding.Risk == "低" && finding.BulkSelectable;
            grid.Refresh();
            UpdateSummary();
        }

        private void ApplyFilter()
        {
            CurrencyManager manager = (CurrencyManager)BindingContext[rows];
            manager.SuspendBinding();
            grid.CurrentCell = null;
            foreach (DataGridViewRow row in grid.Rows)
            {
                Finding finding = row.DataBoundItem as Finding;
                if (finding == null) continue;
                row.Visible = CategoryMatches(finding, activeCategoryFilter);
            }
            manager.ResumeBinding();
            UpdateDetails();
        }

        private void SetNavigation(string filter)
        {
            activeCategoryFilter = filter;
            UiTheme.SetNavActive(overviewNavButton, filter == "总览");
            UiTheme.SetNavActive(startupNavButton, filter == "启动项");
            UiTheme.SetNavActive(contextNavButton, filter == "右键");
            UiTheme.SetNavActive(diagnoseNavButton, filter == "诊断");
            UiTheme.SetNavActive(recoveryNavButton, false);
            if (IsHandleCreated) ApplyFilter();
        }

        private void ApplyStartupResume()
        {
            ElevationResumeState resume = startupResume;
            startupResume = null;
            if (resume == null) return;

            string page = string.IsNullOrWhiteSpace(resume.Page) ? "总览" : resume.Page;
            if (page == "启动项" || page == "诊断" || page == "右键" || page == "总览") SetNavigation(page);

            if (resume.OpenContextMenu)
            {
                SetNavigation("右键");
                using (ContextMenuManagerForm form = new ContextMenuManagerForm(store)) form.ShowDialog(this);
                return;
            }
            if (resume.OpenRecoveryCenter)
            {
                using (RecoveryCenterForm form = new RecoveryCenterForm(store, resume.RecoveryBatchId)) form.ShowDialog(this);
                return;
            }
            if (!resume.ScanAfterLaunch && resume.SelectedFindingKeys.Count == 0)
            {
                statusLabel.Text = "已获得管理员权限，可以处理系统级项目。";
                return;
            }

            StartScan(delegate
            {
                HashSet<string> selectedKeys = new HashSet<string>(resume.SelectedFindingKeys ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                int restored = 0;
                foreach (Finding finding in rows)
                {
                    if (selectedKeys.Contains(ElevationResumeState.FindingKey(finding)) && finding.CanClean)
                    {
                        finding.Selected = true;
                        restored++;
                    }
                }
                SetNavigation(page);
                grid.Invalidate();
                UpdateSummary();
                statusLabel.Text = "管理员模式已重新扫描，恢复勾选 " + restored + " 项；请确认后再点击“清理勾选”。";
                MessageBox.Show(this, "管理员权限已获得，并重新扫描完成。\n\n已恢复勾选 " + restored + " 项。为避免误操作，工具不会自动清理，请确认列表后再点击“清理勾选”。", "已恢复待处理项目", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
        }

        private static bool CategoryMatches(Finding finding, string filter)
        {
            if (finding == null || string.IsNullOrEmpty(filter) || filter == "总览") return true;
            string category = finding.Category ?? string.Empty;
            if (filter == "启动项") return ContainsAny(category, "启动", "后台服务", "计划任务", "定时拉起");
            if (filter == "右键") return ContainsAny(category, "右键", "资源管理器", "文件关联", "打开方式", "此电脑");
            if (filter == "诊断") return ContainsAny(category, "弹窗", "捆绑", "守护", "正在运行", "浏览器插件", "卸载入口");
            return true;
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            foreach (string needle in needles)
            {
                if (value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private void UpdateDetails()
        {
            Finding finding = null;
            try
            {
                DataGridViewRow currentRow = grid.CurrentRow;
                if (currentRow != null && currentRow.Index >= 0 && currentRow.Index < grid.Rows.Count)
                {
                    finding = currentRow.DataBoundItem as Finding;
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                // 增量绑定正在切换 CurrencyManager 位置，下一次 SelectionChanged 会刷新详情。
            }
            catch (IndexOutOfRangeException)
            {
                // 同上：忽略一个消息循环内的瞬时旧索引。
            }
            catch (InvalidOperationException)
            {
                // 表格正在重置绑定，暂时显示空详情。
            }
            if (finding == null)
            {
                detailTitleLabel.Text = "选择一个扫描项目";
                detailMetaLabel.Text = "这里会显示身份、行为、建议和处理影响。";
                detailIdentityLabel.Text = "尚未选择项目。";
                detailBehaviorLabel.Text = "尚未选择项目。";
                detailReasonLabel.Text = "尚未选择项目。";
                detailImpactLabel.Text = "尚未选择项目。";
                detailLocationBox.Text = string.Empty;
                copyDetailButton.Enabled = false;
                return;
            }
            detailTitleLabel.Text = finding.UserVisibleName;
            detailMetaLabel.Text = finding.Vendor + "  ·  " + finding.Category + "  ·  " + finding.RiskDisplay;
            detailIdentityLabel.Text = "关联软件：" + (string.IsNullOrEmpty(finding.SoftwareName) ? "来源未确认" : finding.SoftwareName) + Environment.NewLine + "厂商：" + finding.Vendor + Environment.NewLine + "识别依据：" + ShortDetail(string.IsNullOrEmpty(finding.IdentityExplanation) ? finding.Evidence : finding.IdentityExplanation, 210);
            detailBehaviorLabel.Text = ShortDetail(finding.UserImpact, 260);
            detailReasonLabel.Text = finding.CanClean ? "当前结果提供可恢复的处理动作；请结合证据确认后再勾选。" : "当前结果仅作提示，不参与一键清理。";
            detailImpactLabel.Text = finding.ActionText + Environment.NewLine + finding.SelectionHint;
            detailLocationBox.Text = finding.TechnicalLocation ?? string.Empty;
            copyDetailButton.Enabled = true;
        }

        private static string ShortDetail(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return "未读取到更多信息。";
            string text = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "…";
        }

        private void CopyCurrentDetails()
        {
            Finding finding = grid.CurrentRow == null ? null : grid.CurrentRow.DataBoundItem as Finding;
            if (finding == null) return;
            string text = "名称：" + finding.UserVisibleName + Environment.NewLine +
                "厂商：" + finding.Vendor + Environment.NewLine +
                "来源：" + finding.Category + Environment.NewLine +
                "风险：" + finding.RiskDisplay + Environment.NewLine +
                "影响：" + finding.UserImpact + Environment.NewLine +
                "处理：" + finding.ActionText + Environment.NewLine +
                "位置：" + finding.TechnicalLocation + Environment.NewLine +
                "证据：" + finding.Evidence;
            try
            {
                Clipboard.SetText(text);
                statusLabel.Text = "已复制当前项目详情。";
            }
            catch (Exception ex)
            {
                Logger.Error("复制项目详情失败", ex);
                MessageBox.Show(this, "复制失败：" + ex.Message, "复制详情", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private string CleanupEngineWriteScanReport(List<Finding> findings, List<ScanWarning> scanWarnings)
        {
            string path = Path.Combine(store.Reports, "scan-" + store.Timestamp() + ".json");
            CleanerEngine.WriteJson(path, new ScanEvidenceReport
            {
                ScannedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ProductVersion = AppMeta.Version,
                FindingCount = findings == null ? 0 : findings.Count,
                WarningCount = scanWarnings == null ? 0 : scanWarnings.Count,
                Findings = findings ?? new List<Finding>(),
                Warnings = scanWarnings ?? new List<ScanWarning>()
            });
            return path;
        }

        private string WriteScanErrorReport(Exception ex, string startedAt)
        {
            string path = Path.Combine(store.Reports, "scan-error-" + store.Timestamp() + ".json");
            ScanErrorReport report = new ScanErrorReport
            {
                StartedAt = startedAt,
                FailedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ProductVersion = AppMeta.Version,
                ExecutablePath = Application.ExecutablePath,
                ExecutableDirectory = Path.GetDirectoryName(Application.ExecutablePath),
                DataDirectory = store.Root,
                ErrorType = ex.GetType().FullName,
                ErrorMessage = ex.Message,
                StackTrace = ex.ToString()
            };
            CleanerEngine.WriteJson(path, report);
            return path;
        }

        private void OpenEvidenceReport()
        {
            string path = HasEvidenceReport() ? latestEvidenceReportPath : FindLatestEvidenceReport();
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                latestEvidenceReportPath = path;
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                    statusLabel.Text = "已打开证据报告：" + path;
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Error("打开证据报告失败", ex);
                    MessageBox.Show("证据报告已生成，但打开失败：\n\n" + ex.Message + "\n\n路径：" + path, "打开证据报告失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            try
            {
                Directory.CreateDirectory(store.Reports);
                Process.Start(new ProcessStartInfo { FileName = store.Reports, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Error("打开报告目录失败", ex);
            }
            MessageBox.Show("还没有扫描证据报告。\n\n请先点击“开始扫描”。报告会保存到：\n" + store.Reports, "没有证据报告", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private bool HasEvidenceReport()
        {
            if (string.IsNullOrWhiteSpace(latestEvidenceReportPath) || !File.Exists(latestEvidenceReportPath))
            {
                latestEvidenceReportPath = FindLatestEvidenceReport();
            }
            return !string.IsNullOrWhiteSpace(latestEvidenceReportPath) && File.Exists(latestEvidenceReportPath);
        }

        private string FindLatestEvidenceReport()
        {
            try
            {
                if (!Directory.Exists(store.Reports)) return null;
                DirectoryInfo dir = new DirectoryInfo(store.Reports);
                FileInfo latest = dir.GetFiles("scan-*.json")
                    .Where(delegate(FileInfo file) { return !file.Name.StartsWith("scan-smoke-", StringComparison.OrdinalIgnoreCase); })
                    .OrderByDescending(delegate(FileInfo file) { return file.LastWriteTimeUtc; })
                    .FirstOrDefault();
                return latest == null ? null : latest.FullName;
            }
            catch
            {
                return null;
            }
        }
    }

    internal sealed class RecoveryCenterForm : Form
    {
        private readonly DataStore store;
        private readonly string initialBatchId;
        private readonly ListBox batchList = new ListBox();
        private readonly ModernListHost batchListHost;
        private readonly DataGridView grid = new BufferedDataGridView();
        private readonly Button restoreBatchButton = new Button();
        private readonly Button deleteBatchButton = new Button();
        private readonly Button cleanupOldButton = new Button();
        private readonly Button closeButton = new Button();
        private readonly Label summaryLabel = new Label();
        private readonly Label statusLabel = new Label();
        private readonly Label emptyLabel = new Label();
        private List<CleanupBatch> batches = new List<CleanupBatch>();

        public RecoveryCenterForm(DataStore store)
            : this(store, null)
        {
        }

        internal RecoveryCenterForm(DataStore store, string initialBatchId)
        {
            this.store = store;
            this.initialBatchId = initialBatchId;
            batchListHost = new ModernListHost(batchList);
            UiTheme.ApplyWindowIdentity(this);
            BuildUi();
            LoadBatches();
        }

        private void BuildUi()
        {
            Text = "恢复中心";
            Size = new Size(1060, 680);
            // Keep the recovery workflow usable on a 1080p screen at 200%.
            // The existing body and footer still retain a useful list/grid area at
            // this logical size, instead of Windows forcing the bottom off screen.
            MinimumSize = new Size(900, 500);
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Microsoft YaHei UI", 9F);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            Controls.Add(root);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            header.BackColor = UiTheme.Surface;
            root.Controls.Add(header, 0, 0);

            Label title = new Label();
            title.Text = "恢复中心";
            title.ForeColor = UiTheme.Text;
            title.BackColor = Color.Transparent;
            title.Font = UiTheme.Font(20F, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(28, 15);
            header.Controls.Add(title);

            Label version = new Label();
            version.Text = "v" + AppMeta.Version;
            version.ForeColor = Color.White;
            version.BackColor = UiTheme.Primary;
            version.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            version.TextAlign = ContentAlignment.MiddleCenter;
            version.AutoSize = false;
            version.Size = new Size(78, 28);
            version.Location = new Point(154, 20);
            header.Controls.Add(version);

            Label sub = new Label();
            sub.Text = "这里放的是清理前备份。恢复前看清批次，恢复后建议重新扫描一次。";
            sub.ForeColor = UiTheme.Muted;
            sub.BackColor = Color.Transparent;
            sub.AutoSize = true;
            sub.Location = new Point(30, 58);
            header.Controls.Add(sub);

            Panel headerAccent = new Panel { Dock = DockStyle.Bottom, Height = 3, BackColor = UiTheme.Primary };
            header.Controls.Add(headerAccent);

            TableLayoutPanel body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.ColumnCount = 2;
            body.RowCount = 1;
            body.Padding = new Padding(18, 14, 18, 12);
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.Controls.Add(body, 0, 1);

            CardPanel leftPanel = new CardPanel();
            leftPanel.Dock = DockStyle.Fill;
            leftPanel.BackColor = Color.White;
            leftPanel.Padding = new Padding(14, 12, 14, 14);
            body.Controls.Add(leftPanel, 0, 0);

            TableLayoutPanel leftLayout = new TableLayoutPanel();
            leftLayout.Dock = DockStyle.Fill;
            leftLayout.RowCount = 3;
            leftLayout.ColumnCount = 1;
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            leftPanel.Controls.Add(leftLayout);

            Label batchTitle = new Label();
            batchTitle.Text = "备份批次";
            batchTitle.Dock = DockStyle.Fill;
            batchTitle.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            batchTitle.ForeColor = Color.FromArgb(15, 23, 42);
            batchTitle.TextAlign = ContentAlignment.MiddleLeft;
            leftLayout.Controls.Add(batchTitle, 0, 0);

            Label batchHint = new Label();
            batchHint.Text = "选中一个批次，右侧看具体恢复内容。";
            batchHint.Dock = DockStyle.Fill;
            batchHint.ForeColor = Color.FromArgb(71, 85, 105);
            batchHint.TextAlign = ContentAlignment.MiddleLeft;
            leftLayout.Controls.Add(batchHint, 0, 1);

            batchList.Dock = DockStyle.Fill;
            batchList.BorderStyle = BorderStyle.None;
            batchList.BackColor = Color.White;
            batchList.ForeColor = Color.FromArgb(15, 23, 42);
            batchList.DrawMode = DrawMode.OwnerDrawFixed;
            batchList.ItemHeight = 58;
            batchList.IntegralHeight = false;
            batchListHost.Dock = DockStyle.Fill;
            leftLayout.Controls.Add(batchListHost, 0, 2);

            TableLayoutPanel rightLayout = new TableLayoutPanel();
            rightLayout.Dock = DockStyle.Fill;
            rightLayout.RowCount = 2;
            rightLayout.ColumnCount = 1;
            rightLayout.Margin = new Padding(12, 0, 0, 0);
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.Controls.Add(rightLayout, 1, 0);

            summaryLabel.Dock = DockStyle.Fill;
            summaryLabel.BackColor = UiTheme.PrimarySoft;
            summaryLabel.ForeColor = Color.FromArgb(15, 23, 42);
            summaryLabel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            summaryLabel.Padding = new Padding(14, 0, 0, 0);
            summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
            summaryLabel.Text = "正在读取备份批次...";
            rightLayout.Controls.Add(summaryLabel, 0, 0);

            CardPanel gridPanel = new CardPanel();
            gridPanel.Dock = DockStyle.Fill;
            gridPanel.BackColor = Color.White;
            rightLayout.Controls.Add(gridPanel, 0, 1);

            grid.Dock = DockStyle.Fill;
            grid.AutoGenerateColumns = false;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AllowUserToResizeColumns = false;
            grid.AllowUserToResizeRows = false;
            grid.ScrollBars = ScrollBars.Vertical;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            grid.RowTemplate.Height = 34;
            grid.ColumnHeadersHeight = 38;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(15, 118, 110);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(204, 251, 241);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(15, 23, 42);
            grid.ShowCellToolTips = true;
            grid.Columns.Add(new DataGridViewImageColumn { DataPropertyName = "SoftwareIcon", HeaderText = "", Width = 42, MinimumWidth = 42, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, ImageLayout = DataGridViewImageCellLayout.Normal, DefaultCellStyle = new DataGridViewCellStyle { NullValue = SoftwarePresentationResolver.PlaceholderIcon } });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Status", HeaderText = "结果", FillWeight = 72, MinimumWidth = 58 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "SoftwareName", HeaderText = "关联软件", FillWeight = 125, MinimumWidth = 100 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Vendor", HeaderText = "厂商", FillWeight = 105, MinimumWidth = 80 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Category", HeaderText = "来源", FillWeight = 130, MinimumWidth = 100 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Title", HeaderText = "恢复对象", FillWeight = 220, MinimumWidth = 150 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Message", HeaderText = "当时处理结果", FillWeight = 230, MinimumWidth = 150 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "TechnicalLocation", HeaderText = "技术位置", FillWeight = 230, MinimumWidth = 150 });
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
            UiTheme.AttachModernScrollBar(gridPanel, grid);

            emptyLabel.Dock = DockStyle.Fill;
            emptyLabel.BackColor = Color.White;
            emptyLabel.ForeColor = Color.FromArgb(71, 85, 105);
            emptyLabel.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            emptyLabel.TextAlign = ContentAlignment.MiddleCenter;
            emptyLabel.Text = "暂时没有备份批次。\n清理过项目以后，这里会出现可恢复记录。";
            emptyLabel.Visible = false;
            gridPanel.Controls.Add(emptyLabel);
            emptyLabel.BringToFront();

            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.BackColor = UiTheme.Canvas;
            footer.ColumnCount = 5;
            footer.RowCount = 1;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
            root.Controls.Add(footer, 0, 2);

            statusLabel.Dock = DockStyle.Fill;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Padding = new Padding(18, 0, 0, 0);
            statusLabel.Text = "就绪。";
            footer.Controls.Add(statusLabel, 0, 0);

            UiTheme.ActionButton(cleanupOldButton, "清理旧记录", ActionButtonRole.Warning);
            cleanupOldButton.Dock = DockStyle.Fill;
            cleanupOldButton.Margin = new Padding(0, 9, 8, 9);
            footer.Controls.Add(cleanupOldButton, 1, 0);

            UiTheme.ActionButton(deleteBatchButton, "删除当前批次", ActionButtonRole.Danger);
            deleteBatchButton.Dock = DockStyle.Fill;
            deleteBatchButton.Margin = new Padding(0, 9, 8, 9);
            footer.Controls.Add(deleteBatchButton, 2, 0);

            UiTheme.ActionButton(restoreBatchButton, "恢复当前批次", ActionButtonRole.Primary);
            restoreBatchButton.Dock = DockStyle.Fill;
            restoreBatchButton.Margin = new Padding(0, 9, 8, 9);
            footer.Controls.Add(restoreBatchButton, 3, 0);

            UiTheme.ActionButton(closeButton, "关闭", ActionButtonRole.Close);
            closeButton.Dock = DockStyle.Fill;
            closeButton.Margin = new Padding(0, 9, 18, 9);
            footer.Controls.Add(closeButton, 4, 0);

            batchList.SelectedIndexChanged += delegate { ShowSelectedBatch(); };
            batchList.DrawItem += BatchListDrawItem;
            grid.CellFormatting += GridCellFormatting;
            grid.CellToolTipTextNeeded += GridCellToolTipTextNeeded;
            restoreBatchButton.Click += delegate { RestoreSelectedBatch(); };
            deleteBatchButton.Click += delegate { DeleteSelectedBatch(); };
            cleanupOldButton.Click += delegate { CleanupOldBatches(); };
            closeButton.Click += delegate { Close(); };
        }

        private void LoadBatches()
        {
            batches = new CleanerEngine(store).LoadBatches();
            batchList.Items.Clear();
            restoreBatchButton.Enabled = false;
            deleteBatchButton.Enabled = false;
            cleanupOldButton.Enabled = batches.Count > 0;
            foreach (CleanupBatch batch in batches)
            {
                int failed = batch.Results == null ? 0 : batch.Results.Count(delegate(CleanupResult r) { return r.Status == "Failed" || r.Status == "RestoreFailed"; });
                int done = batch.Results == null ? 0 : batch.Results.Count(delegate(CleanupResult r) { return r.Status == "Done"; });
                int launched = batch.Results == null ? 0 : batch.Results.Count(delegate(CleanupResult r) { return r.Status == "Launched"; });
                int total = batch.Results == null ? 0 : batch.Results.Count;
                batchList.Items.Add(new BatchListItem(batch, "批次 " + batch.Id, "共 " + total + " 项，成功 " + done + "，弹窗 " + launched + "，失败 " + failed));
            }
            batchListHost.RefreshMetrics();
            if (batchList.Items.Count > 0)
            {
                int selectedIndex = string.IsNullOrWhiteSpace(initialBatchId) ? 0 : batches.FindIndex(delegate(CleanupBatch batch) { return string.Equals(batch.Id, initialBatchId, StringComparison.OrdinalIgnoreCase); });
                batchList.SelectedIndex = selectedIndex < 0 ? 0 : selectedIndex;
            }
            else
            {
                summaryLabel.Text = "没有备份批次。";
                statusLabel.Text = "还没有清理记录，所以恢复中心是空的。";
                restoreBatchButton.Enabled = false;
                deleteBatchButton.Enabled = false;
                cleanupOldButton.Enabled = false;
                grid.DataSource = null;
                grid.Visible = false;
                emptyLabel.Visible = true;
                emptyLabel.BringToFront();
            }
        }

        private void ShowSelectedBatch()
        {
            if (batchList.SelectedIndex < 0 || batchList.SelectedIndex >= batches.Count) return;
            CleanupBatch batch = batches[batchList.SelectedIndex];
            List<CleanupResult> results = batch.Results ?? new List<CleanupResult>();
            int done = results.Count(delegate(CleanupResult r) { return r.Status == "Done"; });
            int failed = results.Count(delegate(CleanupResult r) { return r.Status == "Failed" || r.Status == "RestoreFailed"; });
            int launched = results.Count(delegate(CleanupResult r) { return r.Status == "Launched"; });
            int skipped = results.Count(delegate(CleanupResult r) { return r.Status == "Skipped"; });
            foreach (CleanupResult result in results) { if (result.SoftwareIcon == null) result.SoftwareIcon = SoftwarePresentationResolver.PlaceholderIcon; if (string.IsNullOrEmpty(result.SoftwareName)) result.SoftwareName = "正在识别…"; }
            grid.DataSource = new BindingList<CleanupResult>(results);
            SoftwarePresentationQueue.Hydrate(this, results, delegate { grid.Invalidate(); });
            summaryLabel.Text = "批次 " + batch.Id + "    时间 " + batch.CreatedAt + "    成功 " + done + "，弹窗 " + launched + "，失败 " + failed + "，跳过 " + skipped;
            statusLabel.Text = "备份目录：" + batch.Path;
            restoreBatchButton.Enabled = results.Count > 0;
            deleteBatchButton.Enabled = true;
            grid.Visible = results.Count > 0;
            emptyLabel.Visible = results.Count == 0;
            if (emptyLabel.Visible) emptyLabel.BringToFront();
        }

        private void DeleteSelectedBatch()
        {
            if (batchList.SelectedIndex < 0 || batchList.SelectedIndex >= batches.Count) return;
            CleanupBatch batch = batches[batchList.SelectedIndex];
            CleanerEngine cleaner = new CleanerEngine(store);
            string size = FormatBytes(cleaner.GetBatchStorageBytes(batch));
            DialogResult answer = MessageBox.Show(
                "确定永久删除当前恢复批次？\n\n批次：" + batch.Id + "\n占用空间：" + size + "\n\n删除后不能再用这个批次恢复。",
                "删除恢复记录",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
            try
            {
                cleaner.DeleteBatchRecord(batch);
                LoadBatches();
                statusLabel.Text = "已删除恢复批次 " + batch.Id + "，并完成删除复核。";
            }
            catch (Exception ex)
            {
                Logger.Error("删除恢复记录失败", ex);
                MessageBox.Show(this, "删除失败：" + ex.Message + "\n\n未删除的内容会继续保留。", "删除恢复记录失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CleanupOldBatches()
        {
            CleanerEngine cleaner = new CleanerEngine(store);
            List<CleanupBatch> candidates = cleaner.FindOldBatchRecords(batches, DateTime.Now, 20, 30);
            if (candidates.Count == 0)
            {
                MessageBox.Show(this, "没有需要清理的旧记录。\n\n工具会保留最近 30 天的记录，并且始终保留最新 20 个批次。", "无需清理", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            long bytes = candidates.Sum(delegate(CleanupBatch batch) { return cleaner.GetBatchStorageBytes(batch); });
            DialogResult answer = MessageBox.Show(
                "准备永久删除 " + candidates.Count + " 个旧恢复批次，预计释放 " + FormatBytes(bytes) + "。\n\n保留规则：最近 30 天全部保留，并始终保留最新 20 个批次。\n\n删除后不能再用这些旧批次恢复，是否继续？",
                "清理旧恢复记录",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
            List<string> failures = new List<string>();
            int deleted = 0;
            foreach (CleanupBatch batch in candidates)
            {
                try { cleaner.DeleteBatchRecord(batch); deleted++; }
                catch (Exception ex) { failures.Add(batch.Id + "：" + ex.Message); Logger.Error("清理旧恢复记录失败：" + batch.Id, ex); }
            }
            LoadBatches();
            statusLabel.Text = "旧记录清理完成：已删除 " + deleted + " 个，失败 " + failures.Count + " 个。";
            if (failures.Count > 0)
            {
                MessageBox.Show(this, "已删除 " + deleted + " 个批次，" + failures.Count + " 个未能删除。\n\n" + string.Join("\n", failures.Take(6).ToArray()), "部分记录未删除", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show(this, "已删除 " + deleted + " 个旧恢复批次，预计释放 " + FormatBytes(bytes) + "。", "清理完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " 字节";
            if (bytes < 1024L * 1024L) return (bytes / 1024D).ToString("0.0") + " 千字节";
            if (bytes < 1024L * 1024L * 1024L) return (bytes / 1024D / 1024D).ToString("0.0") + " 兆字节";
            return (bytes / 1024D / 1024D / 1024D).ToString("0.00") + " 吉字节";
        }

        private void RestoreSelectedBatch()
        {
            if (batchList.SelectedIndex < 0 || batchList.SelectedIndex >= batches.Count) return;
            CleanupBatch batch = batches[batchList.SelectedIndex];
            if (BatchNeedsAdmin(batch) && !AdminUtil.IsAdministrator())
            {
                DialogResult elevate = MessageBox.Show("这个批次里有系统注册表、后台服务或计划任务，恢复需要管理员权限。\n\n是否请求 Windows 管理员权限？重启后会重新打开恢复中心并定位到当前批次，不会自动恢复。", "需要管理员权限", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (elevate == DialogResult.Yes) AdminUtil.RelaunchAsAdmin(this, store, new ElevationResumeState { Page = "恢复中心", OpenRecoveryCenter = true, RecoveryBatchId = batch.Id });
                return;
            }
            DialogResult answer = MessageBox.Show("恢复批次 " + batch.Id + "？\n\n恢复会导入备份注册表或移回被隔离文件。", "确认恢复", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
            try
            {
                CleanerEngine cleaner = new CleanerEngine(store);
                RestoreBatchResult result = cleaner.RestoreBatch(batch);
                string detail = string.Join(Environment.NewLine, result.Messages.Take(8).ToArray());
                if (result.AllSucceeded)
                {
                    cleaner.DeleteBatchRecord(batch);
                    LoadBatches();
                    statusLabel.Text = "恢复成功，已删除该批次恢复记录。建议重新扫描确认。";
                    MessageBox.Show("恢复成功：" + result.Succeeded + "/" + result.Total + " 项。\n\n该批次记录已从恢复中心删除。\n\n建议重新扫描确认。", "恢复完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    cleaner.RewriteBatchManifest(batch);
                    LoadBatches();
                    statusLabel.Text = "恢复未完全成功：成功项已从批次移除，失败项已保留，可再次尝试。";
                    MessageBox.Show("恢复未完全成功。\n\n成功：" + result.Succeeded + " 项（已从恢复批次移除）\n失败：" + result.Failed + " 项（已保留在恢复中心，可再次尝试）\n\n" + detail, "恢复失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("恢复失败", ex);
                MessageBox.Show(ex.Message, "恢复失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool BatchNeedsAdmin(CleanupBatch batch)
        {
            if (batch == null || batch.Results == null) return false;
            foreach (CleanupResult result in batch.Results)
            {
                ActionTarget target = result == null ? null : result.Target;
                if (target == null) continue;
                if (string.Equals(target.Hive, "HKLM", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(target.Kind, "DisableService", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(target.Kind, "DisableScheduledTask", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void BatchListDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color back = selected ? Color.FromArgb(204, 251, 241) : Color.White;
            Color titleColor = selected ? Color.FromArgb(15, 118, 110) : Color.FromArgb(15, 23, 42);
            Color subColor = Color.FromArgb(71, 85, 105);
            using (SolidBrush brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, e.Bounds);
            if (selected)
            {
                using (SolidBrush accent = new SolidBrush(Color.FromArgb(15, 118, 110)))
                {
                    e.Graphics.FillRectangle(accent, new Rectangle(e.Bounds.Left, e.Bounds.Top + 6, 4, e.Bounds.Height - 12));
                }
            }
            BatchListItem item = batchList.Items[e.Index] as BatchListItem;
            string title = item == null ? Convert.ToString(batchList.Items[e.Index]) : item.Title;
            string subtitle = item == null ? string.Empty : item.Subtitle;
            Rectangle titleRect = new Rectangle(e.Bounds.Left + 14, e.Bounds.Top + 8, e.Bounds.Width - 20, 22);
            Rectangle subRect = new Rectangle(e.Bounds.Left + 14, e.Bounds.Top + 32, e.Bounds.Width - 20, 20);
            TextRenderer.DrawText(e.Graphics, title, new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), titleRect, titleColor, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, subtitle, Font, subRect, subColor, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            using (Pen line = new Pen(Color.FromArgb(226, 232, 240)))
            {
                e.Graphics.DrawLine(line, e.Bounds.Left + 10, e.Bounds.Bottom - 1, e.Bounds.Right - 10, e.Bounds.Bottom - 1);
            }
        }

        private void GridCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            DataGridViewColumn column = grid.Columns[e.ColumnIndex];
            if (!string.Equals(column.DataPropertyName, "Status", StringComparison.OrdinalIgnoreCase)) return;
            string status = Convert.ToString(e.Value);
            e.Value = ChineseDisplayText.CleanupStatus(status);
            if (status == "Done")
            {
                e.CellStyle.BackColor = Color.FromArgb(220, 252, 231);
                e.CellStyle.ForeColor = Color.FromArgb(21, 128, 61);
            }
            else if (status == "Failed" || status == "RestoreFailed")
            {
                e.CellStyle.BackColor = Color.FromArgb(254, 226, 226);
                e.CellStyle.ForeColor = Color.FromArgb(185, 28, 28);
            }
            else if (status == "Launched")
            {
                e.CellStyle.BackColor = Color.FromArgb(255, 237, 213);
                e.CellStyle.ForeColor = Color.FromArgb(194, 65, 12);
            }
            else if (status == "Skipped")
            {
                e.CellStyle.BackColor = Color.FromArgb(226, 232, 240);
                e.CellStyle.ForeColor = Color.FromArgb(71, 85, 105);
            }
            e.FormattingApplied = true;
        }

        private void GridCellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
            CleanupResult result = grid.Rows[e.RowIndex].DataBoundItem as CleanupResult;
            if (result == null) return;
            e.ToolTipText =
                "恢复对象：" + result.Title + Environment.NewLine +
                "处理结果：" + result.Message + Environment.NewLine +
                "技术位置：" + result.TechnicalLocation + Environment.NewLine +
                "备份文件：" + result.Backup;
        }

        private sealed class BatchListItem
        {
            public readonly CleanupBatch Batch;
            public readonly string Title;
            public readonly string Subtitle;

            public BatchListItem(CleanupBatch batch, string title, string subtitle)
            {
                Batch = batch;
                Title = title;
                Subtitle = subtitle;
            }

            public override string ToString()
            {
                return Title;
            }
        }
    }

    internal sealed class GitHubReleaseInfo
    {
        public string tag_name { get; set; }
        public string body { get; set; }
        public string html_url { get; set; }
        public List<GitHubReleaseAsset> assets { get; set; }
    }

    internal sealed class GitHubReleaseAsset
    {
        public string name { get; set; }
        public string browser_download_url { get; set; }
        public long size { get; set; }
    }

    internal static class UpdateChecker
    {
        public static void CheckOnStartup(DataStore store, IWin32Window owner)
        {
            string marker = store.StateFile("last-update-check.txt");
            try
            {
                if (File.Exists(marker))
                {
                    DateTime last;
                    if (DateTime.TryParse(File.ReadAllText(marker), out last) && (DateTime.Now - last).TotalHours < 24) return;
                }
                File.WriteAllText(marker, DateTime.Now.ToString("o"), Encoding.UTF8);
                Task.Factory.StartNew(delegate { CheckNow(store, owner, false); });
            }
            catch (Exception ex)
            {
                Logger.Error("启动更新检查失败", ex);
            }
        }

        public static void CheckNow(DataStore store, IWin32Window owner, bool showNoUpdate)
        {
            try
            {
                if (store == null) throw new ArgumentNullException("store");
                using (WebClient client = new WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    client.Headers.Add("User-Agent", "RogueCleaner/" + AppMeta.Version);
                    client.Headers.Add("Accept", "application/vnd.github+json");
                    GitHubReleaseInfo release = LoadLatestRelease(client);
                    string tag = release == null ? string.Empty : release.tag_name;
                    string body = release == null ? string.Empty : release.body;
                    if (string.IsNullOrWhiteSpace(tag))
                    {
                        throw new InvalidDataException("GitHub Release 信息缺少版本号。");
                    }
                    string latest = tag.TrimStart('v', 'V');
                    if (IsNewer(latest, AppMeta.Version))
                    {
                        GitHubReleaseAsset asset = FindExeAsset(release);
                        if (asset == null)
                        {
                            throw new InvalidDataException("这个版本没有可自动更新的 exe 资产。发布时需要附带 RogueCleaner-*.exe。");
                        }

                        DialogResult answer = MessageBox.Show(owner,
                            "发现新版本：" + tag +
                            "\n当前版本：" + AppMeta.Version +
                            "\n\n" + TrimBody(body) +
                            "\n\n是否现在下载并自动重启更新？",
                            "发现更新",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Information,
                            MessageBoxDefaultButton.Button1);
                        if (answer == DialogResult.Yes)
                        {
                            DownloadAndRestart(store, client, asset, tag, owner);
                        }
                    }
                    else if (showNoUpdate)
                    {
                        MessageBox.Show(owner, "当前已经是最新版本。", "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("检查更新失败", ex);
                if (showNoUpdate)
                {
                    MessageBox.Show(owner, "检查更新失败。\n\n可能是系统 TLS、代理、GitHub API 限制，或者本次发布缺少直出 exe 更新包。\n\n错误：" + ConciseError(ex), "检查更新失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private static GitHubReleaseInfo LoadLatestRelease(WebClient client)
        {
            string json = client.DownloadString(AppMeta.LatestApiUrl);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            return serializer.Deserialize<GitHubReleaseInfo>(json);
        }

        private static GitHubReleaseAsset FindExeAsset(GitHubReleaseInfo release)
        {
            if (release == null || release.assets == null) return null;
            GitHubReleaseAsset fallback = null;
            foreach (GitHubReleaseAsset asset in release.assets)
            {
                if (asset == null || string.IsNullOrWhiteSpace(asset.name) || string.IsNullOrWhiteSpace(asset.browser_download_url)) continue;
                if (!asset.name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (fallback == null) fallback = asset;
                if (asset.name.IndexOf("RogueCleaner", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    asset.name.IndexOf(AppMeta.ProductName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return asset;
                }
            }
            return fallback;
        }

        private static void DownloadAndRestart(DataStore store, WebClient client, GitHubReleaseAsset asset, string tag, IWin32Window owner)
        {
            if (!CanWriteToExecutableDirectory())
            {
                MessageBox.Show(owner,
                    "当前目录没有覆盖主程序的权限。\n\n请把软件放到桌面或普通文件夹，或者以管理员身份启动后再检查更新。",
                    "无法自动更新",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            Directory.CreateDirectory(store.Updates);
            string safeTag = SafeFileName(tag.TrimStart('v', 'V'));
            string downloadPath = Path.Combine(store.Updates, "RogueCleaner-update-" + safeTag + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".exe");
            try
            {
                if (File.Exists(downloadPath)) File.Delete(downloadPath);
                client.Headers["Accept"] = "application/octet-stream";
                client.DownloadFile(asset.browser_download_url, downloadPath);
                ValidateDownloadedExe(downloadPath);
                MessageBox.Show(owner,
                    "新版本已下载完成。\n\n点确定后软件会关闭，自动替换主程序并重新打开。",
                    "准备重启更新",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                StartHiddenUpdater(store, downloadPath);
                Environment.Exit(0);
            }
            catch
            {
                try { if (File.Exists(downloadPath)) File.Delete(downloadPath); } catch { }
                throw;
            }
        }

        private static bool CanWriteToExecutableDirectory()
        {
            try
            {
                string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
                string probe = Path.Combine(exeDir, ".roguecleaner-update-write-test-" + Process.GetCurrentProcess().Id + ".tmp");
                File.WriteAllText(probe, DateTime.Now.ToString("o"), Encoding.UTF8);
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ValidateDownloadedExe(string path)
        {
            FileInfo file = new FileInfo(path);
            if (!file.Exists || file.Length < 65536) throw new InvalidDataException("下载到的文件太小，不像完整 exe。");
            using (FileStream stream = File.OpenRead(path))
            {
                int b1 = stream.ReadByte();
                int b2 = stream.ReadByte();
                if (b1 != 'M' || b2 != 'Z') throw new InvalidDataException("下载到的不是 Windows exe 文件。");
            }
        }

        private static void StartHiddenUpdater(DataStore store, string downloadedExe)
        {
            string currentExe = Application.ExecutablePath;
            string safeVersion = SafeFileName(AppMeta.Version);
            string scriptPath = Path.Combine(store.Updates, "apply-update-from-" + safeVersion + ".cmd");
            string logPath = Path.Combine(store.Updates, "update-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            string script =
                "@echo off\r\n" +
                "setlocal\r\n" +
                "set \"SRC=" + downloadedExe + "\"\r\n" +
                "set \"DST=" + currentExe + "\"\r\n" +
                "set \"LOG=" + logPath + "\"\r\n" +
                "set TRY=0\r\n" +
                ":copyagain\r\n" +
                "copy /y \"%SRC%\" \"%DST%\" >nul 2>>\"%LOG%\"\r\n" +
                "if not errorlevel 1 goto launch\r\n" +
                "set /a TRY+=1\r\n" +
                "if %TRY% GEQ 90 goto fail\r\n" +
                "timeout /t 1 /nobreak >nul\r\n" +
                "goto copyagain\r\n" +
                ":launch\r\n" +
                "start \"\" \"%DST%\"\r\n" +
                "del \"%SRC%\" >nul 2>nul\r\n" +
                "del \"%~f0\" >nul 2>nul\r\n" +
                "exit /b 0\r\n" +
                ":fail\r\n" +
                "echo %date% %time% 更新失败，无法覆盖主程序。>>\"%LOG%\"\r\n" +
                "start \"\" \"%DST%\"\r\n" +
                "exit /b 1\r\n";
            File.WriteAllText(scriptPath, script, Encoding.Default);

            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + scriptPath + "\"");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            Process.Start(psi);
        }

        private static bool IsNewer(string latest, string current)
        {
            Version a, b;
            if (!Version.TryParse(latest, out a)) return false;
            if (!Version.TryParse(current, out b)) return false;
            return a > b;
        }

        private static string TrimBody(string body)
        {
            if (string.IsNullOrEmpty(body)) return string.Empty;
            body = body.Replace("\r", "").Trim();
            return body.Length > 300 ? body.Substring(0, 300) + "..." : body;
        }

        private static string SafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "update";
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return value.Replace('\\', '_').Replace('/', '_').Replace(':', '_');
        }

        private static string ConciseError(Exception ex)
        {
            string message = ex == null ? string.Empty : ex.Message;
            WebException web = ex as WebException;
            if (web != null && web.Status != WebExceptionStatus.UnknownError)
            {
                message = "网络请求失败：" + web.Status;
            }
            message = (message ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (message.IndexOf("\"url\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("\"assets_url\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("\"tag_name\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("{", StringComparison.Ordinal) >= 0)
            {
                message = "GitHub 返回内容解析失败，详情已写入日志。";
            }
            if (string.IsNullOrWhiteSpace(message)) message = "未知错误，详情已写入日志。";
            return message.Length > 160 ? message.Substring(0, 160) + "..." : message;
        }
    }

}
