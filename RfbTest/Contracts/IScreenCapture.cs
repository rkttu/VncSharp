using System.Drawing;

namespace RfbTest.Contracts;

/// <summary>
/// 화면 캡처를 위한 플랫폼 독립적 인터페이스
/// </summary>
public interface IScreenCapture : IDisposable
{
    /// <summary>
    /// 캡처된 화면의 너비
    /// </summary>
    int Width { get; }

    /// <summary>
    /// 캡처된 화면의 높이
    /// </summary>
    int Height { get; }

    /// <summary>
    /// 현재 화면을 캡처하여 byte[] 배열로 반환 (BGRA 포맷)
    /// 변경이 없으면 마지막 프레임을 반환할 수 있음
    /// </summary>
    /// <returns>BGRA 포맷의 바이트 배열, 실패 시 null</returns>
    byte[]? CaptureFrame();

    /// <summary>
    /// 화면을 캡처하여 Bitmap으로 반환
    /// </summary>
    /// <returns>캡처된 Bitmap, 실패 시 null</returns>
    Bitmap? CaptureBitmap();
}
