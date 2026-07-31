using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;
using ModbusLogger.App.ViewModels;

namespace ModbusLogger.App;

public partial class TrafficWindow : Window
{
    private bool _scrollPending;

    public TrafficWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.TrafficLog.CollectionChanged += TrafficLog_CollectionChanged;
    }

    private void TrafficLog_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Glej MainWindow.xaml.cs za razlog odloga: ScrollIntoView klican neposredno tu
        // bi se pri več hitrih vrsticah zaporedoma sprl z ItemContainerGenerator-jem.
        if (_scrollPending)
            return;
        _scrollPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _scrollPending = false;
            if (ExchangeGrid.Items.Count > 0)
                ExchangeGrid.ScrollIntoView(ExchangeGrid.Items[^1]);
        }, DispatcherPriority.Background);
    }
}
