using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PhoneAccounting.App.Infrastructure;

public static class WindowEffects
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private const int DWMWCP_ROUND = 2;

    private const int DWMSBT_TRANSIENTWINDOW = 2;
    private const int DWMSBT_MAINWINDOW = 3;

    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
    private const int WCA_ACCENT_POLICY = 19;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    /// <summary>يطبّق الزجاج (Acrylic) والحواف الدائرية على النافذة.</summary>
    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();

        // حواف دائرية (Windows 11)
        int corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        // خلفية زجاجية (Acrylic)
        if (TrySystemBackdrop(hwnd)) return;

        // بديل Windows 10
        TryAcrylicFallback(hwnd);
    }

    private static bool TrySystemBackdrop(IntPtr hwnd)
    {
        try
        {
            int backdrop = DWMSBT_MAINWINDOW;
            var result = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
            return result == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryAcrylicFallback(IntPtr hwnd)
    {
        try
        {
            var accent = new AccentPolicy
            {
                AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
                GradientColor = unchecked((int)0xCCF5F5F7) // AABBGGRR مع شفافية خفيفة
            };

            var size = Marshal.SizeOf(accent);
            var accentPtr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new WindowCompositionAttributeData
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = accentPtr,
                SizeOfData = size
            };

            var result = SetWindowCompositionAttribute(hwnd, ref data);
            Marshal.FreeHGlobal(accentPtr);
            return result == 0;
        }
        catch
        {
            return false;
        }
    }
}
