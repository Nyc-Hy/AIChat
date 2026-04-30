namespace AIChat.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        // WPF starts here, then MainWindow builds the app's object graph.
        var window = new MainWindow();
        window.Show();
    }
}
