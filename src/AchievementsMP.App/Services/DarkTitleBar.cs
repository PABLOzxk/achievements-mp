using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AchievementsMP.App.Services;

/// <summary>Pede ao Windows 10 (20H1+) / 11 a barra de título escura. Em versões antigas, simplesmente não tem efeito.</summary>
internal static partial class DarkTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var enabled = 1;
            _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }
}
