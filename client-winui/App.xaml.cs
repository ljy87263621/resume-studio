using Microsoft.UI.Xaml;

namespace ResumeStudio;
public partial class App : Application
{
    private MainWindow? window;
    private Mutex? instanceMutex;
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ResumeStudio");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "client-error.log"), $"{DateTimeOffset.Now}: {e.Exception}\n");
        };
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string data=Services.BackendClient.DefaultDataDirectory;
        string key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(data)))[..16];
        instanceMutex=new Mutex(true,"Local\\ResumeStudio-"+key,out bool first);
        if(!first) { Exit(); return; }
        window = new MainWindow(); window.Activate();
    }
}
