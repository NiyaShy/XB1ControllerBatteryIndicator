using System.Linq;
using System.Threading;
using SharpDX.XInput;
using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;
using System.Collections.Generic;
using System;
using System.Management;
using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Media;
using XB1ControllerBatteryIndicator.ShellHelpers;
using MS.WindowsAPICodePack.Internal;
using Microsoft.WindowsAPICodePack.Shell.PropertySystem;
using XB1ControllerBatteryIndicator.Localization;
using XB1ControllerBatteryIndicator.Properties;
using System.Security.Principal;
using Microsoft.Win32;

namespace XB1ControllerBatteryIndicator
{
    public class SystemTrayViewModel : Caliburn.Micro.Screen
    {
        private string _activeIcon;
        private Controller _controller;
        private string _tooltipText;
        private const string APP_ID = "NiyaShy.XB1ControllerBatteryIndicator";
        private bool[] toast_shown = new bool[5];
        private Dictionary<string, int> numdict = new Dictionary<string, int>();
        private const string ThemeRegKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string ThemeRegValueName = "SystemUsesLightTheme";

        private SoundPlayer _soundPlayer;

        public SystemTrayViewModel()
        {
            GetAvailableLanguages();
            TranslationManager.CurrentLanguageChangedEvent += (sender, args) => GetAvailableLanguages();
            UpdateNotificationSound();

            ActiveIcon = $"Resources/battery_unknown{LightTheme()}.ico";
            numdict["One"] = 1;
            numdict["Two"] = 2;
            numdict["Three"] = 3;
            numdict["Four"] = 4;
            TryCreateShortcut();
            Thread th = new Thread(RefreshControllerState);
            th.IsBackground = true;
            th.Start();
        }

        public string ActiveIcon
        {
            get { return _activeIcon; }
            set { Set(ref _activeIcon, value); }
        }

        public string TooltipText
        {
            get { return _tooltipText; }
            set { Set(ref _tooltipText, value); }
        }

        public ObservableCollection<CultureInfo> AvailableLanguages { get; } = new ObservableCollection<CultureInfo>();

        private void RefreshControllerState()
        {
            bool lowBatteryWarningSoundPlayed = false;

            while(true)
            {
                //Initialize controllers
                var controllers = new[]
                {
                    new Controller(UserIndex.One), new Controller(UserIndex.Two), new Controller(UserIndex.Three),
                    new Controller(UserIndex.Four)
                };
                var connectedControllers = controllers
                    .Where(controller => controller.IsConnected)
                    .Select(controller => (controller, battery: controller.GetBatteryInformation(BatteryDeviceType.Gamepad)));
                TooltipText = string.Join("\n", connectedControllers.Select(controller =>
                {
                    var controllerIndexCaption = GetControllerIndexCaption(controller.controller.UserIndex);
                    switch (controller.battery.BatteryType)
                    {
                        case BatteryType.Wired:
                            return string.Format(Strings.ToolTip_Wired, controllerIndexCaption);
                        case BatteryType.Disconnected:
                            return string.Format(Strings.ToolTip_WaitingForData, controllerIndexCaption);
                        case BatteryType.Alkaline:
                        case BatteryType.Nimh:
                            var batteryLevelCaption = GetBatteryLevelCaption(controller.battery.BatteryLevel);
                            return string.Format(Strings.ToolTip_Wireless, controllerIndexCaption, batteryLevelCaption);
                        default:
                            return string.Format(Strings.ToolTip_Unknown, controllerIndexCaption);
                    }
                }));
                foreach (var (controller, batteryInfo) in connectedControllers)
                {
                    //check if toast was already triggered and battery is no longer empty...
                    if (batteryInfo.BatteryLevel != BatteryLevel.Empty)
                    {
                        if (toast_shown[numdict[$"{controller.UserIndex}"]] == true)
                        {
                            //...reset the notification
                            toast_shown[numdict[$"{controller.UserIndex}"]] = false;
                            ToastNotificationManager.History.Remove($"Controller{controller.UserIndex}", "ControllerToast", APP_ID);
                        }
                    }
                }
                var shownControllers = connectedControllers.Where(connectedController =>
                    (connectedController.battery.BatteryType == BatteryType.Wired && Settings.Default.ShowWiredControllers) ||
                    (connectedController.battery.BatteryType == BatteryType.Disconnected && Settings.Default.ShowWirelessControllersWithUnknownBatteryLevel) ||
                    (connectedController.battery.BatteryType != BatteryType.Wired && Settings.Default.ShowWirelessControllersWithKnownBatteryLevel) ||
                    connectedController.battery.BatteryType == BatteryType.Unknown
                );
                foreach (var (currentController, batteryInfo) in shownControllers)
                {
                    switch (batteryInfo.BatteryType)
                    {
                        case BatteryType.Wired:
                            ActiveIcon = $"Resources/battery_wired_{currentController.UserIndex.ToString().ToLower() + LightTheme()}.ico";
                            break;
                        case BatteryType.Disconnected: // a controller that was detected but hasn't sent battery data yet
                        case BatteryType.Unknown: // this should never happen
                            ActiveIcon = $"Resources/battery_disconnected_{currentController.UserIndex.ToString().ToLower() + LightTheme()}.ico";
                            break;
                        default: // a battery level was detected
                            ActiveIcon = $"Resources/battery_{batteryInfo.BatteryLevel.ToString().ToLower()}_{currentController.UserIndex.ToString().ToLower() + LightTheme()}.ico";
                            break;
                    }
                    //when "empty" state is detected...
                    if (batteryInfo.BatteryLevel == BatteryLevel.Empty)
                    {
                        //check if toast (notification) for current controller was already triggered
                        if (toast_shown[numdict[$"{currentController.UserIndex}"]] == false)
                        {
                            //if not, trigger it
                            toast_shown[numdict[$"{currentController.UserIndex}"]] = true;
                            ShowToast(currentController.UserIndex);
                        }
                        //check if notification sound is enabled
                        if (Settings.Default.LowBatteryWarningSound_Enabled)
                        {
                            if (Settings.Default.LowBatteryWarningSound_Loop_Enabled || !lowBatteryWarningSoundPlayed)
                            {
                                //Necessary to avoid crashing if the .wav file is missing
                                try
                                {
                                    _soundPlayer?.Play();
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine(ex);
                                }
                                lowBatteryWarningSoundPlayed = true;
                            }
                        }
                    }
                    Thread.Sleep(5000);
                }
                if (shownControllers.Count() == 0)
                {
                    ActiveIcon = $"Resources/battery_unknown{LightTheme()}.ico";
                    if (connectedControllers.Count() == 0)
                    {
                        TooltipText = Strings.ToolTip_NoController;
                    }
                }
                Thread.Sleep(1000);
            }
        }

        //try to create a start menu shortcut (required for sending toasts)
        private bool TryCreateShortcut()
        {
            String shortcutPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\Microsoft\\Windows\\Start Menu\\Programs\\XB1ControllerBatteryIndicator.lnk";
            if (!File.Exists(shortcutPath))
            {
                InstallShortcut(shortcutPath);
                return true;
            }
            return false;
        }
        //create the shortcut
        private void InstallShortcut(String shortcutPath)
        {
            // Find the path to the current executable 
            String exePath = Process.GetCurrentProcess().MainModule.FileName;
            IShellLinkW newShortcut = (IShellLinkW)new CShellLink();

            // Create a shortcut to the exe 
            ErrorHelper.VerifySucceeded(newShortcut.SetPath(exePath));
            ErrorHelper.VerifySucceeded(newShortcut.SetArguments(""));

            // Open the shortcut property store, set the AppUserModelId property 
            IPropertyStore newShortcutProperties = (IPropertyStore)newShortcut;

            using (PropVariant appId = new PropVariant(APP_ID))
            {
                ErrorHelper.VerifySucceeded(newShortcutProperties.SetValue(SystemProperties.System.AppUserModel.ID, appId));
                ErrorHelper.VerifySucceeded(newShortcutProperties.Commit());
            }

            // Commit the shortcut to disk 
            IPersistFile newShortcutSave = (IPersistFile)newShortcut;

            ErrorHelper.VerifySucceeded(newShortcutSave.Save(shortcutPath, true));
        }
        //send a toast
        private void ShowToast(UserIndex controllerIndex)
        {
            int controllerId = numdict[$"{controllerIndex}"];
            var controllerIndexCaption = GetControllerIndexCaption(controllerIndex);
            string argsDismiss = $"dismissed";
            string argsLaunch = $"{controllerId}";
            //how the content gets arranged
            string toastVisual =
                $@"<visual>
                        <binding template='ToastGeneric'>
                            <text>{string.Format(Strings.Toast_Title, controllerIndexCaption)}</text>
                            <text>{string.Format(Strings.Toast_Text, controllerIndexCaption)}</text>
                            <text>{Strings.Toast_Text2}</text>
                        </binding>
                    </visual>";
            //Button on the toast
            string toastActions =
                $@"<actions>
                        <action content='{Strings.Toast_Dismiss}' arguments='{argsDismiss}'/>
                   </actions>";
            //combine content and button
            string toastXmlString =
                $@"<toast scenario='reminder' launch='{argsLaunch}'>
                        {toastVisual}
                        {toastActions}
                   </toast>";

            XmlDocument toastXml = new XmlDocument();
            toastXml.LoadXml(toastXmlString);
            //create the toast
            var toast = new ToastNotification(toastXml);
            toast.Activated += ToastActivated;
            toast.Dismissed += ToastDismissed;
            toast.Tag = $"Controller{controllerIndex}";
            toast.Group = "ControllerToast";
            //..and send it
            ToastNotificationManager.CreateToastNotifier(APP_ID).Show(toast);

        }
        //react to click on toast or button
        private void ToastActivated(ToastNotification sender, object e)
        {
            var toastArgs = e as ToastActivatedEventArgs;
            int controllerId = 0;
            //if the return value contains a controller ID
            if (Int32.TryParse(toastArgs.Arguments, out controllerId))
            {
                //reset the toast warning (it will trigger again if battery level is still empty)
                toast_shown[controllerId] = false;
            }
            //otherwise, do nothing
        }
        private void ToastDismissed(ToastNotification sender, object e)
        {
            //do nothing
        }

        public void ExitApplication()
        {
            System.Windows.Application.Current.Shutdown();
        }

        private string GetBatteryLevelCaption(BatteryLevel batteryLevel)
        {
            switch (batteryLevel)
            {
                case BatteryLevel.Empty:
                    return Strings.BatteryLevel_Empty;
                case BatteryLevel.Low:
                    return Strings.BatteryLevel_Low;
                case BatteryLevel.Medium:
                    return Strings.BatteryLevel_Medium;
                case BatteryLevel.Full:
                    return Strings.BatteryLevel_Full;
                default:
                    throw new ArgumentOutOfRangeException(nameof(batteryLevel), batteryLevel, null);
            }
        }

        private string GetControllerIndexCaption(UserIndex index)
        {
            switch (index)
            {
                case UserIndex.One:
                    return Strings.ControllerIndex_One;
                case UserIndex.Two:
                    return Strings.ControllerIndex_Two;
                case UserIndex.Three:
                    return Strings.ControllerIndex_Three;
                case UserIndex.Four:
                    return Strings.ControllerIndex_Four;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index), index, null);
            }
        }

        private void GetAvailableLanguages()
        {
            AvailableLanguages.Clear();
            foreach (var language in TranslationManager.AvailableLanguages)
            {
                AvailableLanguages.Add(language);
            }
        }

        public void UpdateNotificationSound()
        {
            _soundPlayer = File.Exists(Settings.Default.wavFile) ? new SoundPlayer(Settings.Default.wavFile) : null;
        }
        public void WatchTheme()
        {
            var currentUser = WindowsIdentity.GetCurrent();
            string query = string.Format(
                CultureInfo.InvariantCulture,
                @"SELECT * FROM RegistryValueChangeEvent WHERE Hive = 'HKEY_USERS' AND KeyPath = '{0}\\{1}' AND ValueName = '{2}'",
                currentUser.User.Value,
                ThemeRegKeyPath.Replace(@"\", @"\\"),
                ThemeRegValueName);

            try
            {
                var watcher = new ManagementEventWatcher(query);
                watcher.EventArrived += (sender, args) =>
                {
                    LightTheme();
                    
                };

                // Start listening for events
                watcher.Start();
            }
            catch (Exception)
            {
                // This can fail on Windows 7
            }

            LightTheme();
        }

        private string LightTheme()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(ThemeRegKeyPath))
            {
                object registryValueObject = key?.GetValue(ThemeRegValueName);
                if (registryValueObject == null)
                {
                    return "";
                }

                int registryValue = (int)registryValueObject;

                return registryValue > 0 ? "-black" : "";
            }
        }
    }
}