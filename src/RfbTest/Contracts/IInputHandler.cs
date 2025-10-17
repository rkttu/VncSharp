namespace RfbTest.Contracts;

/// <summary>
/// 키보드 및 마우스 입력 처리 인터페이스
/// </summary>
public interface IInputHandler
{
    /// <summary>
    /// 키보드 이벤트 처리
    /// </summary>
    /// <param name="downFlag">키가 눌렸는지 여부 (true=눌림, false=뗌)</param>
    /// <param name="key">X11 키심 코드</param>
    void HandleKeyEvent(bool downFlag, uint key);

    /// <summary>
    /// 마우스 이벤트 처리
    /// </summary>
    /// <param name="buttonMask">버튼 마스크 (비트 0=왼쪽, 1=가운데, 2=오른쪽, 3-4=스크롤)</param>
    /// <param name="x">X 좌표</param>
    /// <param name="y">Y 좌표</param>
    void HandlePointerEvent(byte buttonMask, ushort x, ushort y);
}
