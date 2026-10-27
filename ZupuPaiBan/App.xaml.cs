using System.Windows;
using SQLitePCL;
using ZupuPaiBan.Infrastructure;

namespace ZupuPaiBan;

public partial class App : Application
{
    private DataDirectoryLease? _dataDirectoryLease;

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            _dataDirectoryLease = DataDirectoryLease.Acquire(AppDataPaths.RootDirectory);
            AppDataPaths.EnsureDirectories();
            Batteries.Init();
        }
        catch (Exception ex)
        {
            _dataDirectoryLease?.Dispose();
            _dataDirectoryLease = null;
            MessageBox.Show($"无法启动谱匠：{ex.Message}", "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        DispatcherUnhandledException += (_, args) =>
            FileAppLogger.Write(AppLogLevel.Critical, "Unhandled WPF dispatcher exception.", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            FileAppLogger.Write(AppLogLevel.Critical, "Unhandled AppDomain exception.", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
            FileAppLogger.Write(AppLogLevel.Error, "Unobserved task exception.", args.Exception);
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _dataDirectoryLease?.Dispose();
        base.OnExit(e);
    }
}
