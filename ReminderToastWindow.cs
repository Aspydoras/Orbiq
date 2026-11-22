using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Orbiq.Services;

namespace Orbiq;

public sealed class ReminderToastWindow : Window
{
    private readonly DispatcherTimer _closeTimer;

    public ReminderToastWindow(string title, string message, Action<int>? snoozeAction = null)
    {
        Title = LocalizationService.Translate("Orbiq — напоминание");
        Width = 360;
        SizeToContent = SizeToContent.Height;
        MinHeight = 150;
        MaxHeight = 360;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = true;

        Brush panel = GetResourceBrush("PanelBrush", "#121B2B");
        Brush card = GetResourceBrush("CardBrush", "#192438");
        Brush textBrush = GetResourceBrush("TextBrush", "#F3F6FC");
        Brush muted = GetResourceBrush("MutedBrush", "#AAB6CA");
        Brush accent = GetResourceBrush("AccentBrush", "#64D8CB");
        Brush border = GetResourceBrush("BorderBrush", "#35445A");

        var close = new Button
        {
            Content = "×", Width = 30, Height = 28, Padding = new Thickness(0),
            Foreground = textBrush, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            FontSize = 17, Cursor = Cursors.Hand, Focusable = false
        };
        close.Click += (_, _) => Close();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brand = new TextBlock
        {
            Text = LocalizationService.Translate("ORBIQ  •  НАПОМИНАНИЕ"), FontSize = 9, FontWeight = FontWeights.SemiBold,
            Foreground = muted, VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(brand);
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(new TextBlock
        {
            Text = LocalizationService.Translate(title), FontSize = 18, FontWeight = FontWeights.SemiBold,
            Foreground = textBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 6)
        });
        stack.Children.Add(new TextBlock
        {
            Text = message, FontSize = 13, Foreground = muted,
            TextWrapping = TextWrapping.Wrap, MaxHeight = 180
        });
        if (snoozeAction is not null)
        {
            stack.Children.Add(new TextBlock
            {
                Text = LocalizationService.Translate("Отложить напоминание"), FontSize = 10, Foreground = muted,
                Margin = new Thickness(0, 12, 0, 5)
            });
            var snoozeButtons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            foreach (int minutes in new[] { 5, 10, 30 })
            {
                int delay = minutes;
                var snoozeButton = new Button
                {
                    Content = $"{delay} {LocalizationService.Translate("мин")}", MinWidth = 62, Height = 29, Margin = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(8, 3, 8, 3), FontSize = 10, Cursor = Cursors.Hand,
                    Foreground = textBrush, Background = panel, BorderBrush = border, BorderThickness = new Thickness(1)
                };
                snoozeButton.Click += (_, _) =>
                {
                    snoozeAction(delay);
                    Close();
                };
                snoozeButtons.Children.Add(snoozeButton);
            }
            stack.Children.Add(snoozeButtons);
        }
        stack.Children.Add(new TextBlock
        {
            Text = LocalizationService.Translate("Нажми ×, чтобы закрыть"), FontSize = 10, Foreground = accent,
            Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Right
        });

        Content = new Border
        {
            Background = card,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(17),
            Child = stack,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 22, ShadowDepth = 5, Opacity = 0.35, Color = Colors.Black }
        };
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!IsInsideButton(e.OriginalSource as DependencyObject))
            {
                try { DragMove(); } catch (InvalidOperationException) { }
            }
        };

        Loaded += (_, _) =>
        {
            Rect area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 18;
            Top = area.Bottom - ActualHeight - 18;
        };

        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(snoozeAction is null ? 10 : 20) };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            Close();
        };
        Closed += (_, _) => _closeTimer.Stop();
        _closeTimer.Start();
    }

    private static bool IsInsideButton(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is Button) return true;
            try { current = System.Windows.Media.VisualTreeHelper.GetParent(current); }
            catch { current = LogicalTreeHelper.GetParent(current); }
        }
        return false;
    }

    private static Brush GetResourceBrush(string key, string fallback)
    {
        if (Application.Current?.Resources[key] is Brush brush) return brush;
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
    }
}
