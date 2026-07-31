using System.Windows;
using ModbusLogger.App.ViewModels;

namespace ModbusLogger.App;

public partial class AddDeviceWindow : Window
{
    public AddDeviceWindow(AddDeviceViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.RequestClose += success =>
        {
            DialogResult = success;
            Close();
        };
    }
}
