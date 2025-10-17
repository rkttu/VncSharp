using RfbTest.Contracts;
using RfbTest.Diagnostics;
using System.Runtime.InteropServices;

namespace RfbTest.ScreenCaptures;

/// <summary>
/// 플랫폼에 맞는 ScreenCapture 구현체를 생성하는 팩토리
/// </summary>
public static class ScreenCaptureFactory
{
    /// <summary>
    /// 현재 플랫폼에 맞는 IScreenCapture 구현체를 생성합니다.
    /// Windows만 지원하며, 다른 플랫폼에서는 예외를 던집니다.
    /// </summary>
    /// <param name="outputIndex">캡처할 출력 디스플레이 인덱스 (기본값: 0)</param>
    /// <returns>Windows용 IScreenCapture 구현체</returns>
    /// <exception cref="PlatformNotSupportedException">Windows가 아닌 플랫폼일 경우</exception>
    public static IScreenCapture Create(int outputIndex = 0)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            RfbLogger.Log("[ScreenCaptureFactory] Creating Windows screen capture implementation");
            return new WindowsScreenCapture(outputIndex);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            throw new PlatformNotSupportedException(
                "Linux screen capture is not supported. " +
                "Only Windows is currently supported.");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            throw new PlatformNotSupportedException(
                "macOS screen capture is not supported. " +
                "Only Windows is currently supported.");
        }
        else
        {
            throw new PlatformNotSupportedException(
                $"Platform '{RuntimeInformation.OSDescription}' is not supported. " +
                "Only Windows is currently supported.");
        }
    }

    /// <summary>
    /// 현재 플랫폼에서 화면 캡처가 지원되는지 확인합니다.
    /// </summary>
    /// <returns>Windows 플랫폼이면 true, 그 외는 false</returns>
    public static bool IsSupported()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    }

    /// <summary>
    /// 현재 플랫폼 정보를 반환합니다.
    /// </summary>
    public static string GetPlatformInfo()
    {
        var platform = "Unknown";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) platform = "Windows";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) platform = "Linux";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) platform = "macOS";

        var supported = IsSupported() ? "Supported" : "Not Supported";
        
        return $"Platform: {platform}, " +
               $"OS: {RuntimeInformation.OSDescription}, " +
               $"Architecture: {RuntimeInformation.OSArchitecture}, " +
               $"Framework: {RuntimeInformation.FrameworkDescription}, " +
               $"Screen Capture: {supported}";
    }
}
