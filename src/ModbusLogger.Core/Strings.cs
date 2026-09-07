using System.Globalization;
using System.Resources;

namespace ModbusLogger.Core;

/// <summary>
/// Ročno pisan (ne auto-generiran) dostop do Strings.resx/Strings.{en,de,it,es}.resx.
/// Vsaka lastnost prebere trenutno vrednost glede na Thread.CurrentThread.CurrentUICulture,
/// zato mora klicna koda za spremembo jezika nastaviti to kulturo (glej App.SetLanguage).
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Manager =
        new("ModbusLogger.Core.Strings", typeof(Strings).Assembly);

    private static string Get(string name) =>
        Manager.GetString(name, CultureInfo.CurrentUICulture) ?? name;

    public static string Btn_Preklici => Get(nameof(Btn_Preklici));
    public static string Btn_Shrani => Get(nameof(Btn_Shrani));

    public static string App_CrashMessage => Get(nameof(App_CrashMessage));
    public static string App_CrashTitle => Get(nameof(App_CrashTitle));

    public static string Main_Title => Get(nameof(Main_Title));
    public static string Main_HeaderText => Get(nameof(Main_HeaderText));
    public static string Main_Btn_NalPro => Get(nameof(Main_Btn_NalPro));
    public static string Main_Btn_ShrPro => Get(nameof(Main_Btn_ShrPro));
    public static string Main_Btn_HexPromet => Get(nameof(Main_Btn_HexPromet));
    public static string Main_Btn_NastBel => Get(nameof(Main_Btn_NastBel));
    public static string Main_Group_Povezava => Get(nameof(Main_Group_Povezava));
    public static string Main_Label_NacinPovezave => Get(nameof(Main_Label_NacinPovezave));
    public static string Main_Label_ComPort => Get(nameof(Main_Label_ComPort));
    public static string Main_Tooltip_OsveziPorte => Get(nameof(Main_Tooltip_OsveziPorte));
    public static string Main_Label_Hitrost => Get(nameof(Main_Label_Hitrost));
    public static string Main_Label_Format => Get(nameof(Main_Label_Format));
    public static string Main_Label_IpNaslov => Get(nameof(Main_Label_IpNaslov));
    public static string Main_Label_TcpVrata => Get(nameof(Main_Label_TcpVrata));
    public static string Main_Label_Timeout => Get(nameof(Main_Label_Timeout));
    public static string Main_Label_Ponovitve => Get(nameof(Main_Label_Ponovitve));
    public static string Main_Label_CasVzorcenja => Get(nameof(Main_Label_CasVzorcenja));
    public static string Main_Tooltip_CasVzorcenja => Get(nameof(Main_Tooltip_CasVzorcenja));
    public static string Main_Hint_Povezava => Get(nameof(Main_Hint_Povezava));
    public static string Main_Group_Naprave => Get(nameof(Main_Group_Naprave));
    public static string Main_Btn_Dodaj => Get(nameof(Main_Btn_Dodaj));
    public static string Main_Btn_Uredi => Get(nameof(Main_Btn_Uredi));
    public static string Main_Btn_Odstrani => Get(nameof(Main_Btn_Odstrani));
    public static string Main_Tooltip_PremakniGor => Get(nameof(Main_Tooltip_PremakniGor));
    public static string Main_Tooltip_PremakniDol => Get(nameof(Main_Tooltip_PremakniDol));
    public static string Main_Group_Registri => Get(nameof(Main_Group_Registri));
    public static string Main_Col_Naslov => Get(nameof(Main_Col_Naslov));
    public static string Main_Col_Ime => Get(nameof(Main_Col_Ime));
    public static string Main_Col_Vrednost => Get(nameof(Main_Col_Vrednost));
    public static string Main_Col_Enota => Get(nameof(Main_Col_Enota));
    public static string Main_Tooltip_KonstantniNaslov => Get(nameof(Main_Tooltip_KonstantniNaslov));
    public static string Main_DnevnikDogodkov => Get(nameof(Main_DnevnikDogodkov));
    public static string Main_ConnMode_Serial => Get(nameof(Main_ConnMode_Serial));
    public static string Main_ConnMode_Tcp => Get(nameof(Main_ConnMode_Tcp));
    public static string Main_StartStop_Start => Get(nameof(Main_StartStop_Start));
    public static string Main_StartStop_Stop => Get(nameof(Main_StartStop_Stop));
    public static string Main_ConfigPathDisplay => Get(nameof(Main_ConfigPathDisplay));
    public static string Main_Lang_Label => Get(nameof(Main_Lang_Label));
    public static string Main_HelpButton_Tooltip => Get(nameof(Main_HelpButton_Tooltip));
    public static string Main_UpdateAvailable => Get(nameof(Main_UpdateAvailable));

    public static string AddDevice_Title_Add => Get(nameof(AddDevice_Title_Add));
    public static string AddDevice_Title_Edit => Get(nameof(AddDevice_Title_Edit));
    public static string AddDevice_Label_ImeNaprave => Get(nameof(AddDevice_Label_ImeNaprave));
    public static string AddDevice_Label_SlaveId => Get(nameof(AddDevice_Label_SlaveId));
    public static string AddDevice_Group_Profil => Get(nameof(AddDevice_Group_Profil));
    public static string AddDevice_Hint_Profil => Get(nameof(AddDevice_Hint_Profil));
    public static string AddDevice_Btn_NalPro => Get(nameof(AddDevice_Btn_NalPro));
    public static string AddDevice_Btn_IzbrisiPro => Get(nameof(AddDevice_Btn_IzbrisiPro));
    public static string AddDevice_Btn_ShrKotPro => Get(nameof(AddDevice_Btn_ShrKotPro));
    public static string AddDevice_Label_Registri => Get(nameof(AddDevice_Label_Registri));
    public static string AddDevice_Btn_OdstraniIzbranega => Get(nameof(AddDevice_Btn_OdstraniIzbranega));
    public static string AddDevice_Btn_PlusRegister => Get(nameof(AddDevice_Btn_PlusRegister));
    public static string AddDevice_Col_Naslov => Get(nameof(AddDevice_Col_Naslov));
    public static string AddDevice_Col_Funkcija => Get(nameof(AddDevice_Col_Funkcija));
    public static string AddDevice_Col_Ime => Get(nameof(AddDevice_Col_Ime));
    public static string AddDevice_Col_Enota => Get(nameof(AddDevice_Col_Enota));
    public static string AddDevice_Col_Tip => Get(nameof(AddDevice_Col_Tip));
    public static string AddDevice_Col_Skala => Get(nameof(AddDevice_Col_Skala));
    public static string AddDevice_Col_WordOrder => Get(nameof(AddDevice_Col_WordOrder));
    public static string AddDevice_Col_Konstanta => Get(nameof(AddDevice_Col_Konstanta));
    public static string AddDevice_Tooltip_KonstNaslov => Get(nameof(AddDevice_Tooltip_KonstNaslov));
    public static string AddDevice_Tooltip_KonstStolp => Get(nameof(AddDevice_Tooltip_KonstStolp));

    public static string ConfigPicker_Title_Save => Get(nameof(ConfigPicker_Title_Save));
    public static string ConfigPicker_Title_Load => Get(nameof(ConfigPicker_Title_Load));
    public static string ConfigPicker_ConfirmBtn_Save => Get(nameof(ConfigPicker_ConfirmBtn_Save));
    public static string ConfigPicker_ConfirmBtn_Load => Get(nameof(ConfigPicker_ConfirmBtn_Load));
    public static string ConfigPicker_Label_ImeProfila => Get(nameof(ConfigPicker_Label_ImeProfila));
    public static string ConfigPicker_Label_ShranjeniProfili => Get(nameof(ConfigPicker_Label_ShranjeniProfili));
    public static string ConfigPicker_Err_VpisiIme => Get(nameof(ConfigPicker_Err_VpisiIme));
    public static string ConfigPicker_Err_IzberiProfil => Get(nameof(ConfigPicker_Err_IzberiProfil));

    public static string LoggingSettings_Title => Get(nameof(LoggingSettings_Title));
    public static string LoggingSettings_RunningWarning => Get(nameof(LoggingSettings_RunningWarning));
    public static string LoggingSettings_Label_IntervalZapisa => Get(nameof(LoggingSettings_Label_IntervalZapisa));
    public static string LoggingSettings_Tooltip_IntervalZapisa => Get(nameof(LoggingSettings_Tooltip_IntervalZapisa));
    public static string LoggingSettings_Chk_BeleziCsv => Get(nameof(LoggingSettings_Chk_BeleziCsv));
    public static string LoggingSettings_Label_MapaCsv => Get(nameof(LoggingSettings_Label_MapaCsv));
    public static string LoggingSettings_Btn_Prebrskaj => Get(nameof(LoggingSettings_Btn_Prebrskaj));
    public static string LoggingSettings_Btn_OdpriMapo => Get(nameof(LoggingSettings_Btn_OdpriMapo));
    public static string LoggingSettings_Label_LociloStolpcev => Get(nameof(LoggingSettings_Label_LociloStolpcev));
    public static string LoggingSettings_Label_DecimalnoLocilo => Get(nameof(LoggingSettings_Label_DecimalnoLocilo));
    public static string LoggingSettings_Chk_BeleziMySql => Get(nameof(LoggingSettings_Chk_BeleziMySql));
    public static string LoggingSettings_Label_Gostitelj => Get(nameof(LoggingSettings_Label_Gostitelj));
    public static string LoggingSettings_Label_Vrata => Get(nameof(LoggingSettings_Label_Vrata));
    public static string LoggingSettings_Label_Baza => Get(nameof(LoggingSettings_Label_Baza));
    public static string LoggingSettings_Label_Uporabnik => Get(nameof(LoggingSettings_Label_Uporabnik));
    public static string LoggingSettings_Label_Geslo => Get(nameof(LoggingSettings_Label_Geslo));
    public static string LoggingSettings_Label_Tabela => Get(nameof(LoggingSettings_Label_Tabela));
    public static string LoggingSettings_Btn_PreberiStolpce => Get(nameof(LoggingSettings_Btn_PreberiStolpce));
    public static string LoggingSettings_Label_PovezavaStolpcev => Get(nameof(LoggingSettings_Label_PovezavaStolpcev));
    public static string LoggingSettings_Hint_TrajnoShrani => Get(nameof(LoggingSettings_Hint_TrajnoShrani));
    public static string LoggingSettings_Btn_Zapri => Get(nameof(LoggingSettings_Btn_Zapri));
    public static string LoggingSettings_Delim_Podpicje => Get(nameof(LoggingSettings_Delim_Podpicje));
    public static string LoggingSettings_Delim_Vejica => Get(nameof(LoggingSettings_Delim_Vejica));
    public static string LoggingSettings_Delim_Tab => Get(nameof(LoggingSettings_Delim_Tab));

    public static string Traffic_Title => Get(nameof(Traffic_Title));
    public static string Traffic_Hint => Get(nameof(Traffic_Hint));
    public static string Traffic_Col_CasPosiljanja => Get(nameof(Traffic_Col_CasPosiljanja));
    public static string Traffic_Col_Poslano => Get(nameof(Traffic_Col_Poslano));
    public static string Traffic_Col_CasPrejema => Get(nameof(Traffic_Col_CasPrejema));
    public static string Traffic_Col_Prejeto => Get(nameof(Traffic_Col_Prejeto));
    public static string Traffic_NoResponse => Get(nameof(Traffic_NoResponse));

    public static string MySqlField_NeUporabi => Get(nameof(MySqlField_NeUporabi));
    public static string MySqlField_CasMeritve => Get(nameof(MySqlField_CasMeritve));
    public static string MySqlField_NapravaIme => Get(nameof(MySqlField_NapravaIme));
    public static string MySqlField_SlaveId => Get(nameof(MySqlField_SlaveId));
    public static string MySqlField_StatusNapaka => Get(nameof(MySqlField_StatusNapaka));
    public static string MySqlField_CasOdziva => Get(nameof(MySqlField_CasOdziva));
    public static string MySqlField_NapakaKomunikacije => Get(nameof(MySqlField_NapakaKomunikacije));
    public static string MySqlField_Konstanta => Get(nameof(MySqlField_Konstanta));
    public static string MySqlField_Register => Get(nameof(MySqlField_Register));

    public static string Device_CakamNaPrvoBranje => Get(nameof(Device_CakamNaPrvoBranje));
    public static string Device_BrezPovezaveUstavljeno => Get(nameof(Device_BrezPovezaveUstavljeno));
    public static string Device_OkMs => Get(nameof(Device_OkMs));
    public static string Device_Napaka => Get(nameof(Device_Napaka));

    public static string St_NiNalozeneKonf => Get(nameof(St_NiNalozeneKonf));
    public static string St_DevicesJsonNiNajden => Get(nameof(St_DevicesJsonNiNajden));
    public static string Log_Opozorilo => Get(nameof(Log_Opozorilo));
    public static string Log_Napaka => Get(nameof(Log_Napaka));
    public static string St_KonfImaNapak => Get(nameof(St_KonfImaNapak));
    public static string St_Nalozeno => Get(nameof(St_Nalozeno));
    public static string St_VpisiIpNaslov => Get(nameof(St_VpisiIpNaslov));
    public static string St_IzberiComPort => Get(nameof(St_IzberiComPort));
    public static string Log_CsvBelezenjeV => Get(nameof(Log_CsvBelezenjeV));
    public static string Log_NapakaMapeCsv => Get(nameof(Log_NapakaMapeCsv));
    public static string Log_CsvIzklopljeno => Get(nameof(Log_CsvIzklopljeno));
    public static string Log_MySqlBelezenjeV => Get(nameof(Log_MySqlBelezenjeV));
    public static string Log_NapakaMySqlPovezave => Get(nameof(Log_NapakaMySqlPovezave));
    public static string St_BelezenjeTece => Get(nameof(St_BelezenjeTece));
    public static string Log_Zagnano => Get(nameof(Log_Zagnano));
    public static string St_Ustavljam => Get(nameof(St_Ustavljam));
    public static string Log_NapakaObUstavljanju => Get(nameof(Log_NapakaObUstavljanju));
    public static string St_Ustavljeno => Get(nameof(St_Ustavljeno));
    public static string Log_NalagamProfil => Get(nameof(Log_NalagamProfil));
    public static string Log_ProfilShranjenKot => Get(nameof(Log_ProfilShranjenKot));
    public static string Log_NapakaPriShranjevanjuProfila => Get(nameof(Log_NapakaPriShranjevanjuProfila));
    public static string Dlg_IzberiMapoCsv => Get(nameof(Dlg_IzberiMapoCsv));
    public static string Log_TabelaNeObstaja => Get(nameof(Log_TabelaNeObstaja));
    public static string Log_PrebranihStolpcev => Get(nameof(Log_PrebranihStolpcev));
    public static string Log_NapakaBranjaStolpcev => Get(nameof(Log_NapakaBranjaStolpcev));
    public static string Log_NapravaDodana => Get(nameof(Log_NapravaDodana));
    public static string Log_NapravaPosodobljena => Get(nameof(Log_NapravaPosodobljena));
    public static string Log_NapakaProfilNiNalozen => Get(nameof(Log_NapakaProfilNiNalozen));
    public static string Msg_OdstraniNapravo => Get(nameof(Msg_OdstraniNapravo));
    public static string Msg_OdstraniNapravoTitle => Get(nameof(Msg_OdstraniNapravoTitle));
    public static string Log_NapravaOdstranjena => Get(nameof(Log_NapravaOdstranjena));
    public static string Log_NapraveNiBiloMogoceNajti => Get(nameof(Log_NapraveNiBiloMogoceNajti));
    public static string Log_NapakaOdstranjevanjaNaprave => Get(nameof(Log_NapakaOdstranjevanjaNaprave));
    public static string Log_CasVzorcenjaSpremenjen => Get(nameof(Log_CasVzorcenjaSpremenjen));

    public static string AddDevice_ErrLoadProfil => Get(nameof(AddDevice_ErrLoadProfil));
    public static string AddDevice_ErrNiRegistrovZaShranjevanje => Get(nameof(AddDevice_ErrNiRegistrovZaShranjevanje));
    public static string AddDevice_SaveFileDialog_Title => Get(nameof(AddDevice_SaveFileDialog_Title));
    public static string AddDevice_SaveFileDialog_Filter => Get(nameof(AddDevice_SaveFileDialog_Filter));
    public static string AddDevice_DefaultProfileFileName => Get(nameof(AddDevice_DefaultProfileFileName));
    public static string AddDevice_ProfilShranjen => Get(nameof(AddDevice_ProfilShranjen));
    public static string AddDevice_NapakaShranjevanjaProfila => Get(nameof(AddDevice_NapakaShranjevanjaProfila));
    public static string AddDevice_OpozoriloProfilUporabljajo => Get(nameof(AddDevice_OpozoriloProfilUporabljajo));
    public static string AddDevice_MsgIzbrisemProfil => Get(nameof(AddDevice_MsgIzbrisemProfil));
    public static string AddDevice_MsgIzbrisiProfilTitle => Get(nameof(AddDevice_MsgIzbrisiProfilTitle));
    public static string AddDevice_ProfilIzbrisan => Get(nameof(AddDevice_ProfilIzbrisan));
    public static string AddDevice_NapakaBrisanjaProfila => Get(nameof(AddDevice_NapakaBrisanjaProfila));
    public static string AddDevice_ErrImeNapraveNeSmeBitiPrazno => Get(nameof(AddDevice_ErrImeNapraveNeSmeBitiPrazno));
    public static string AddDevice_ErrSlaveIdMoraBitiMed => Get(nameof(AddDevice_ErrSlaveIdMoraBitiMed));
    public static string AddDevice_ErrNapravaPotrebujeVsajEnRegister => Get(nameof(AddDevice_ErrNapravaPotrebujeVsajEnRegister));
    public static string AddDevice_NapakaShranjevanja => Get(nameof(AddDevice_NapakaShranjevanja));
    public static string AddDevice_ErrVsakRegisterPotrebujeIme => Get(nameof(AddDevice_ErrVsakRegisterPotrebujeIme));
    public static string AddDevice_ErrNeveljavnaKonstanta => Get(nameof(AddDevice_ErrNeveljavnaKonstanta));
    public static string AddDevice_ErrNeveljavenNaslov => Get(nameof(AddDevice_ErrNeveljavenNaslov));
    public static string AddDevice_ErrNeveljavnaSkala => Get(nameof(AddDevice_ErrNeveljavnaSkala));
    public static string AddDevice_DefaultPrivateName => Get(nameof(AddDevice_DefaultPrivateName));

    public static string Err_Timeout => Get(nameof(Err_Timeout));
    public static string Err_ModbusException => Get(nameof(Err_ModbusException));
    public static string Err_NapakaPovezave => Get(nameof(Err_NapakaPovezave));
    public static string Err_PovezavaNiUspela => Get(nameof(Err_PovezavaNiUspela));
    public static string Msg_PortOdprt => Get(nameof(Msg_PortOdprt));
    public static string Msg_PortaNiMogoceOdpreti => Get(nameof(Msg_PortaNiMogoceOdpreti));
    public static string Msg_PovezavaVzpostavljena => Get(nameof(Msg_PovezavaVzpostavljena));
    public static string Msg_PovezaveNiMogoceVzpostaviti => Get(nameof(Msg_PovezaveNiMogoceVzpostaviti));
    public static string Msg_PovezavaNiNaVoljo => Get(nameof(Msg_PovezavaNiNaVoljo));
    public static string Err_NepricakovanaNapaka => Get(nameof(Err_NepricakovanaNapaka));
    public static string Msg_PovezavaJeIzginila => Get(nameof(Msg_PovezavaJeIzginila));
    public static string Msg_NapakaPriZapisu => Get(nameof(Msg_NapakaPriZapisu));

    public static string Err_SerialPortNiNastavljen => Get(nameof(Err_SerialPortNiNastavljen));
    public static string Err_SerialBaudNiVeljaven => Get(nameof(Err_SerialBaudNiVeljaven));
    public static string Err_SerialParityMoraBiti => Get(nameof(Err_SerialParityMoraBiti));
    public static string Err_SerialStopBitsMoraBiti => Get(nameof(Err_SerialStopBitsMoraBiti));
    public static string Err_SerialTimeoutMoraBiti => Get(nameof(Err_SerialTimeoutMoraBiti));
    public static string Err_LoggingFolderPrazen => Get(nameof(Err_LoggingFolderPrazen));
    public static string Err_LoggingDelimiterEnZnak => Get(nameof(Err_LoggingDelimiterEnZnak));
    public static string Err_LoggingDecimalSep => Get(nameof(Err_LoggingDecimalSep));
    public static string Err_LoggingDelimiterEnakDecimal => Get(nameof(Err_LoggingDelimiterEnakDecimal));
    public static string Err_ProfilNimaPollGroup => Get(nameof(Err_ProfilNimaPollGroup));
    public static string Err_ProfilNimaRegistra => Get(nameof(Err_ProfilNimaRegistra));
    public static string Err_NeznanaFunkcijaPollGroup => Get(nameof(Err_NeznanaFunkcijaPollGroup));
    public static string Err_NeveljavenStartAddress => Get(nameof(Err_NeveljavenStartAddress));
    public static string Err_CountMoraBiti => Get(nameof(Err_CountMoraBiti));
    public static string Err_BlokPresegaNaslovniProstor => Get(nameof(Err_BlokPresegaNaslovniProstor));
    public static string Err_NeznanaFunkcijaRegister => Get(nameof(Err_NeznanaFunkcijaRegister));
    public static string Err_NeveljavenNaslovProfil => Get(nameof(Err_NeveljavenNaslovProfil));
    public static string Err_NeznanTip => Get(nameof(Err_NeznanTip));
    public static string Err_WordOrderMoraBiti => Get(nameof(Err_WordOrderMoraBiti));
    public static string Err_NaslovNiPokrit => Get(nameof(Err_NaslovNiPokrit));
    public static string Err_SampleIntervalMoraBiti => Get(nameof(Err_SampleIntervalMoraBiti));
    public static string Err_WriteIntervalMoraBiti => Get(nameof(Err_WriteIntervalMoraBiti));
    public static string Err_DevicesListPrazen => Get(nameof(Err_DevicesListPrazen));
    public static string Err_NapravaSlaveIdMoraBiti => Get(nameof(Err_NapravaSlaveIdMoraBiti));
    public static string Err_NapravaManjkaProfile => Get(nameof(Err_NapravaManjkaProfile));
    public static string Err_ProfilNeObstaja => Get(nameof(Err_ProfilNeObstaja));
    public static string Warn_SlaveIdUporabljenVecKrat => Get(nameof(Warn_SlaveIdUporabljenVecKrat));
    public static string Err_DatotekaPrazna => Get(nameof(Err_DatotekaPrazna));
    public static string Err_NeveljavenJson => Get(nameof(Err_NeveljavenJson));
    public static string Err_Who_Profil => Get(nameof(Err_Who_Profil));
    public static string Err_Who_Naprava => Get(nameof(Err_Who_Naprava));
    public static string Err_Who_Register => Get(nameof(Err_Who_Register));
}
