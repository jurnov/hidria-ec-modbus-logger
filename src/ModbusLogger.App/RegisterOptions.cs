namespace ModbusLogger.App;

/// <summary>
/// Statični seznami za DataGridComboBoxColumn v AddDeviceWindow. Vezani prek x:Static,
/// ker ItemsSource teh stolpcev prek ElementName/Binding na DataContext ni zanesljivo
/// razrešljiv — stolpci DataGrid niso del vizualnega drevesa na način, ki ga ta pričakuje.
/// </summary>
public static class RegisterOptions
{
    public static readonly string[] Functions = { "input", "holding", "constant" };
    public static readonly string[] Types = { "uint16", "int16", "uint32", "int32", "float32" };
    public static readonly string[] WordOrders = { "big", "little" };
}
