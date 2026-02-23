using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ModernContextMenuManager.Helpers;
using ModernContextMenuManager.ViewModels;

namespace ModernContextMenuManager;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        this.DataContext = VM;
        this.Loaded += MainWindow_Loaded;
        this.Closed += MainWindow_Closed;

        LayoutRoot.PropertyChanged += static (s, a) =>
        {
            if (s is Control sender
                && a.Property == Control.IsEnabledProperty
                && a.NewValue is true)
            {
                ((MainWindow)sender.Parent!).SearchBox.Focus();
            }
        };
    }

    public MainWindowViewModel VM => ViewModelLocator.Instance.MainWindowViewModel;

    private async void MainWindow_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await VM.SearchCommand.ExecuteAsync("");
    }

    private void SearchBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter)
        {
            ((ButtonAutomationPeer)ControlAutomationPeer.CreatePeerForElement(SearchButton)).Invoke();
        }
    }

    private void MainWindow_Closed(object? sender, System.EventArgs e)
    {
        PackagedComHelper.DeleteMCMMFolder();
    }

}