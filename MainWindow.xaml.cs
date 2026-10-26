using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DpiMusicControl.Core;
using DpiMusicControl.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace DpiMusicControl;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private readonly GlobalInputHook _inputHook = new();
    private readonly DispatcherTimer _captureTimeoutTimer;
    private AppSettings _settings = new();
    private Forms.NotifyIcon? _notifyIcon;
    private Forms.ToolStripMenuItem? _trayToggleMenuItem;
    private InputTrigger _selectedTrigger = InputTrigger.XButton1;
    private DateTime _lastTriggerUtc = DateTime.MinValue;
    private bool _captureArmed;
    private bool _isInitializing;
    private bool _isExiting;
    private bool _hasShownTrayHint;

    public MainWindow()
    {
        InitializeComponent();
        _inputHook.TriggerPressed += InputHook_TriggerPressed;
        _captureTimeoutTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(7)
        };
        _captureTimeoutTimer.Tick += CaptureTimeoutTimer_Tick;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = _settingsService.Load();
        _selectedTrigger = _settings.Trigger;

        _isInitializing = true;
        TriggerComboBox.ItemsSource = TriggerOption.All;
        TriggerComboBox.SelectedItem = TriggerOption.FromValue(_selectedTrigger);
        EnabledToggle.IsChecked = _settings.MappingEnabled;
        StartupToggle.IsChecked = StartupService.IsEnabled();
        _settings.StartWithWindows = StartupToggle.IsChecked == true;
        _isInitializing = false;

        _settingsService.Save(_settings);
        CreateTrayIcon();

        try
        {
            _inputHook.Start();
            UpdateMappingVisuals();
            SetCaptureMessage("Готово. Нажмите «Определить», затем кнопку DPI.", isWarning: false);
        }
        catch (Win32Exception)
        {
            EnabledToggle.IsChecked = false;
            EnabledToggle.IsEnabled = false;
            CaptureButton.IsEnabled = false;
            TriggerComboBox.IsEnabled = false;
            SetCaptureMessage("Windows не дала доступ к глобальному вводу. Перезапустите приложение.", isWarning: true);
            UpdateMappingVisuals();
        }
    }

    private void TriggerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || TriggerComboBox.SelectedItem is not TriggerOption option)
        {
            return;
        }

        _selectedTrigger = option.Value;
        _settings.Trigger = _selectedTrigger;
        SaveSettings();
        UpdateMappingVisuals();
    }

    private void EnabledToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        _settings.MappingEnabled = EnabledToggle.IsChecked == true;
        SaveSettings();
        UpdateMappingVisuals();
    }

    private void StartupToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        var enabled = StartupToggle.IsChecked == true;

        try
        {
            StartupService.SetEnabled(enabled);
            _settings.StartWithWindows = enabled;
            SaveSettings();
        }
        catch (Exception)
        {
            _isInitializing = true;
            StartupToggle.IsChecked = !enabled;
            _isInitializing = false;
            SetCaptureMessage("Не удалось изменить автозапуск Windows.", isWarning: true);
        }
    }

    private void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_inputHook.IsRunning)
        {
            return;
        }

        _captureArmed = true;
        CaptureButton.Content = "Слушаю…";
        CaptureButton.IsEnabled = false;
        _captureTimeoutTimer.Stop();
        _captureTimeoutTimer.Start();
        SetCaptureMessage("Слушаю. Нажмите DPI-кнопку или F13–F24. Esc — отмена.", isWarning: false);
    }

    private void TestButton_Click(object sender, RoutedEventArgs e)
    {
        var sent = MediaKeyService.SendPlayPause();
        SetCaptureMessage(
            sent ? "Команда «Воспроизвести / пауза» отправлена." : "Не удалось отправить системную медиа-команду.",
            isWarning: !sent);
    }

    private void InputHook_TriggerPressed(object? sender, TriggerPressedEventArgs e)
    {
        if (_captureArmed)
        {
            e.Suppress = true;
            _captureArmed = false;
            _captureTimeoutTimer.Stop();

            Dispatcher.BeginInvoke(() =>
            {
                _selectedTrigger = e.Trigger;
                _settings.Trigger = _selectedTrigger;
                TriggerComboBox.SelectedItem = TriggerOption.FromValue(_selectedTrigger);
                CaptureButton.Content = "Определить";
                CaptureButton.IsEnabled = true;
                LastInputText.Text = $"Обнаружено: {TriggerOption.FromValue(_selectedTrigger).Detail}";
                SetCaptureMessage($"Готово: {TriggerOption.FromValue(_selectedTrigger).Detail} назначена для Play / Pause.", isWarning: false);
                SaveSettings();
                UpdateMappingVisuals();
            });

            return;
        }

        if (!_settings.MappingEnabled || e.Trigger != _selectedTrigger)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _lastTriggerUtc < TimeSpan.FromMilliseconds(220))
        {
            e.Suppress = true;
            return;
        }

        _lastTriggerUtc = now;
        e.Suppress = true;
        Dispatcher.BeginInvoke(() =>
        {
            if (!_settings.MappingEnabled || e.Trigger != _selectedTrigger || !_inputHook.IsRunning)
            {
                return;
            }

            var sent = MediaKeyService.SendPlayPause();
            LastInputText.Text = $"Последний сигнал: {TriggerOption.FromValue(e.Trigger).Detail}";
            SetCaptureMessage(
                sent ? "Команда «Воспроизвести / пауза» отправлена." : "Не удалось отправить системную медиа-команду.",
                isWarning: !sent);
        });
    }

    private void UpdateMappingVisuals()
    {
        var enabled = _settings.MappingEnabled && _inputHook.IsRunning;
        var option = TriggerOption.FromValue(_selectedTrigger);

        HeroGlyph.Text = enabled ? "Ⅱ" : "▶";
        HeroDetailText.Text = enabled
            ? $"{option.Detail} → Воспроизвести / пауза"
            : $"{option.Detail} выбрана, сопоставление на паузе";
        FooterStatusText.Text = enabled ? "Сопоставление активно" : "Сопоставление на паузе";
        StatusTitle.Text = enabled ? "Активно" : "Пауза";
        StatusTitle.Foreground = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(enabled ? "#5DE4B2" : "#FFCF70"));
        StatusDot.Fill = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(enabled ? "#5DE4B2" : "#FFCF70"));
        StatusPill.Background = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(enabled ? "#203D38" : "#4A3C21"));

        if (_notifyIcon is not null)
        {
            _notifyIcon.Text = enabled ? "DPI Media — активно" : "DPI Media — на паузе";
        }

        if (_trayToggleMenuItem is not null)
        {
            _trayToggleMenuItem.Checked = enabled;
            _trayToggleMenuItem.Text = enabled ? "Выключить сопоставление" : "Включить сопоставление";
        }
    }

    private void SetCaptureMessage(string message, bool isWarning)
    {
        CaptureStatusText.Text = message;
        CaptureHintBorder.Background = new SolidColorBrush(
            (MediaColor)MediaColorConverter.ConvertFromString(isWarning ? "#3C3023" : "#202633"));
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !_captureArmed)
        {
            return;
        }

        _captureArmed = false;
        _captureTimeoutTimer.Stop();
        CaptureButton.Content = "Определить";
        CaptureButton.IsEnabled = true;
        SetCaptureMessage("Определение отменено.", isWarning: false);
        e.Handled = true;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => HideToTray();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => HideToTray();

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && IsVisible)
        {
            HideToTray();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _captureTimeoutTimer.Stop();
        _inputHook.Dispose();
        DisposeTrayIcon();
    }

    private void CaptureTimeoutTimer_Tick(object? sender, EventArgs e)
    {
        _captureTimeoutTimer.Stop();

        if (!_captureArmed)
        {
            return;
        }

        _captureArmed = false;
        CaptureButton.Content = "Определить";
        CaptureButton.IsEnabled = true;
        SetCaptureMessage(
            "DPI-кнопка не дала Windows сигнал. В ПО мыши назначьте ей F13 или Mouse 4/5 и повторите.",
            isWarning: true);
    }

    private void HideToTray()
    {
        Hide();
        WindowState = WindowState.Normal;

        try
        {
            _inputHook.Restart();
        }
        catch (Win32Exception)
        {
            SetCaptureMessage("Перехват кнопки не удалось восстановить. Откройте приложение и включите сопоставление снова.", isWarning: true);
        }

        UpdateMappingVisuals();

        if (!_hasShownTrayHint && _notifyIcon is not null)
        {
            _notifyIcon.ShowBalloonTip(
                1500,
                "DPI Media продолжает работать",
                "Откройте приложение двойным щелчком по значку в трее.",
                Forms.ToolTipIcon.Info);
            _hasShownTrayHint = true;
        }
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => Dispatcher.BeginInvoke(ShowFromTray));
        _trayToggleMenuItem = new Forms.ToolStripMenuItem();
        _trayToggleMenuItem.Click += (_, _) => Dispatcher.BeginInvoke(ToggleMappingFromTray);
        menu.Items.Add(_trayToggleMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выйти", null, (_, _) => Dispatcher.BeginInvoke(ExitApplication));

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = GetApplicationIcon(),
            Text = "DPI Media",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => Dispatcher.BeginInvoke(ShowFromTray);
    }

    private static Drawing.Icon GetApplicationIcon()
    {
        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var icon = Drawing.Icon.ExtractAssociatedIcon(executablePath);
            if (icon is not null)
            {
                return icon;
            }
        }

        return Drawing.SystemIcons.Application;
    }

    private void ToggleMappingFromTray()
    {
        EnabledToggle.IsChecked = !(_settings.MappingEnabled);
    }

    private void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        _inputHook.Dispose();
        DisposeTrayIcon();
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void DisposeTrayIcon()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _notifyIcon = null;
    }

    private void SaveSettings()
    {
        _settings.Trigger = _selectedTrigger;
        _settings.MappingEnabled = EnabledToggle.IsChecked == true;
        _settings.StartWithWindows = StartupToggle.IsChecked == true;
        _settingsService.Save(_settings);
    }
}
