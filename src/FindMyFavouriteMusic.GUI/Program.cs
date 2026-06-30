using Avalonia;
using System;

namespace FindMyFavouriteMusic.GUI;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 全局异常处理
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[FATAL] 未处理异常: {ex}");
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        System.Diagnostics.Trace.WriteLine($"[ERROR] 未观察的任务异常: {e.Exception}");
        e.SetObserved();
    }
}
