using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using ThreeFingerDragOnWindows.utils;
using Application = Microsoft.UI.Xaml.Application;
using Utils = ThreeFingerDragOnWindows.utils.Utils;

namespace ThreeFingerDragOnWindows;

public class Program {
    [STAThread]
    static async Task<int> Main(string[] args){
        try{
            WinRT.ComWrappersSupport.InitializeComWrappers();

            (AppInstance existingInstance, bool existingInstanceIsAdmin) = FindExistingInstance();

            if(existingInstance != null){
                if(Utils.IsAppRunningAsAdministrator() && !existingInstanceIsAdmin && TerminateOldInstance(existingInstance.ProcessId)){
                    Logger.Log("Unelevated instance found and killed. Starting the app");
                    StartApp(args);
                } else{
                    Logger.Log("Instance found, redirecting activation.");
                    await RedirectActivation(existingInstance);
                }
            } else{
                Logger.Log("No instance found, starting the app.");
                StartApp(args);
            }

            return 0;
        } catch(Exception ex){
            ReportFatalStartupError(ex);
            return 1;
        }
    }

    private static async Task RedirectActivation(AppInstance instance){
        AppActivationArguments args = AppInstance.GetCurrent().GetActivatedEventArgs();
        await instance.RedirectActivationToAsync(args);
    }

    private static void StartApp(string[] args){
        AppInstance.FindOrRegisterForKey("ThreeFingerDragOnWindows-SingleInstance-" + (Utils.IsAppRunningAsAdministrator() ? "Admin" : "User"));
        AppInstance.GetCurrent().Activated += OnActivated;

        bool openSettingsOnLaunch = !args.Any(arg =>
            arg.Equals("--background", StringComparison.OrdinalIgnoreCase));

        Application.Start((p) => {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App(openSettingsOnLaunch);
        });
    }

    internal static void ReportFatalStartupError(Exception exception){
        string message = exception.ToString();

        try{
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ThreeFingerDragOnWindows");
            Directory.CreateDirectory(directory);

            string logPath = Path.Combine(directory, "startup-error.log");
            File.AppendAllText(
                logPath,
                $"[{DateTimeOffset.Now:O}] Fatal startup error{Environment.NewLine}{message}{Environment.NewLine}{Environment.NewLine}");
            message = $"ThreeFingerDragOnWindows could not start.\n\nDetails were saved to:\n{logPath}\n\n{exception.Message}";
        } catch{
            message = $"ThreeFingerDragOnWindows could not start.\n\n{message}";
        }

        MessageBox(IntPtr.Zero, message, "ThreeFingerDragOnWindows startup error", 0x00000010);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    private static (AppInstance, bool) FindExistingInstance(){
        foreach(AppInstance appInstance in AppInstance.GetInstances()){
            if(appInstance.IsCurrent) continue;
            if(appInstance.Key.Equals("ThreeFingerDragOnWindows-SingleInstance-User")){
                return (appInstance, false);
            }

            if(appInstance.Key.Equals("ThreeFingerDragOnWindows-SingleInstance-Admin")){
                return (appInstance, true);
            }
        }

        return (null, false);
    }

    private static void OnActivated(object sender, AppActivationArguments args){
        (Application.Current as App)?.DispatcherQueue.TryEnqueue(() => { (Application.Current as App)?.OpenSettingsWindow(); });
    }

    private static bool TerminateOldInstance(uint processId){
        try{
            Process oldInstance = Process.GetProcessById((int) processId);
            oldInstance.Kill();
        } catch(Exception ex){
            Logger.Log(ex.ToString());
            return false;
        }

        return true;
    }
}
