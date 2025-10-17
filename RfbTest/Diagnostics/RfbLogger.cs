using System.Diagnostics;

namespace RfbTest.Diagnostics;

/// <summary>
/// RFB 프로토콜 디버그 로거
/// </summary>
public static class RfbLogger
{
    public static void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        Debug.WriteLine($"[{timestamp}] {message}");
    }

    public static void LogError(string message, Exception? ex = null)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        Debug.WriteLine($"[{timestamp}] ERROR: {message}");
        if (ex != null)
        {
            Debug.WriteLine($"[{timestamp}] Exception: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
            {
                Debug.WriteLine($"[{timestamp}] Inner: {ex.InnerException.Message}");
            }
            Debug.WriteLine($"[{timestamp}] Stack: {ex.StackTrace}");
        }
    }

    public static void LogHex(string prefix, byte[] data, int maxBytes = 32)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var hexString = BitConverter.ToString(data, 0, Math.Min(maxBytes, data.Length));
        Debug.WriteLine($"[{timestamp}] {prefix}: {hexString}");
    }
}
