using System.Windows;
using SQLitePCL;
using ZupuPaiBan.Infrastructure;

namespace ZupuPaiBan;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppDataPaths.EnsureDirectories();
        // 初始化 SQLite native 库
        Batteries.Init();
        DispatcherUnhandledException += (_, args) =>
            FileAppLogger.Write(AppLogLevel.Critical, "Unhandled WPF dispatcher exception.", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            FileAppLogger.Write(AppLogLevel.Critical, "Unhandled AppDomain exception.", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
            FileAppLogger.Write(AppLogLevel.Error, "Unobserved task exception.", args.Exception);
    }
}
