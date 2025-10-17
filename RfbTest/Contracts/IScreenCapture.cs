using System.Drawing;

namespace RfbTest.Contracts;

/// <summary>
/// ȭ�� ĸó�� ���� �÷��� ������ �������̽�
/// </summary>
public interface IScreenCapture : IDisposable
{
    /// <summary>
    /// ĸó�� ȭ���� �ʺ�
    /// </summary>
    int Width { get; }

    /// <summary>
    /// ĸó�� ȭ���� ����
    /// </summary>
    int Height { get; }

    /// <summary>
    /// ���� ȭ���� ĸó�Ͽ� byte[] �迭�� ��ȯ (BGRA ����)
    /// ������ ������ ������ �������� ��ȯ�� �� ����
    /// </summary>
    /// <returns>BGRA ������ ����Ʈ �迭, ���� �� null</returns>
    byte[]? CaptureFrame();

    /// <summary>
    /// ȭ���� ĸó�Ͽ� Bitmap���� ��ȯ
    /// </summary>
    /// <returns>ĸó�� Bitmap, ���� �� null</returns>
    Bitmap? CaptureBitmap();
}
