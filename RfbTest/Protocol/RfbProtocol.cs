namespace RfbTest.Protocol;

/// <summary>
/// RFB(Remote Framebuffer) 프로토콜 정의 및 상수
/// </summary>
public static class RfbProtocol
{
    // RFB 프로토콜 버전
    public const string ProtocolVersion38 = "RFB 003.008\n";
    public const string ProtocolVersion37 = "RFB 003.007\n";
    public const string ProtocolVersion33 = "RFB 003.003\n";

    // 보안 타입
    public enum SecurityType : byte
    {
        Invalid = 0,
        None = 1,
        VncAuthentication = 2,
        RA2 = 5,
        RA2ne = 6,
        Tight = 16,
        Ultra = 17,
        TLS = 18,
        VeNCrypt = 19,
        SASL = 20,
        MD5 = 21,
        xvp = 22
    }

    // 클라이언트->서버 메시지 타입
    public enum ClientMessageType : byte
    {
        SetPixelFormat = 0,
        SetEncodings = 2,
        FramebufferUpdateRequest = 3,
        KeyEvent = 4,
        PointerEvent = 5,
        ClientCutText = 6,
        SetDesktopSize = 251  // ExtendedDesktopSize 확장
    }

    // 서버->클라이언트 메시지 타입
    public enum ServerMessageType : byte
    {
        FramebufferUpdate = 0,
        SetColorMapEntries = 1,
        Bell = 2,
        ServerCutText = 3
    }

    // 인코딩 타입
    public enum EncodingType : int
    {
        Raw = 0,
        CopyRect = 1,
        RRE = 2,
        Hextile = 5,
        ZRLE = 16,
        Cursor = -239,
        DesktopSize = -223,
        ExtendedDesktopSize = -308,  // ExtendedDesktopSize 확장 지원
        LastRect = -224,
        PointerPos = -232,
        JPEG = 21,
        JPEG_QualityLevel0 = -32,
        JPEG_QualityLevel9 = -23,
        CompressionLevel0 = -256,
        CompressionLevel9 = -247
    }

    // 픽셀 포맷
    public struct PixelFormat
    {
        public byte BitsPerPixel;      // 픽셀당 비트 수
        public byte Depth;             // 색상 깊이
        public byte BigEndianFlag;     // 빅엔디안 여부
        public byte TrueColorFlag;     // 트루컬러 여부
        public ushort RedMax;          // 빨강 최대값
        public ushort GreenMax;        // 녹색 최대값
        public ushort BlueMax;         // 파랑 최대값
        public byte RedShift;          // 빨강 시프트
        public byte GreenShift;        // 녹색 시프트
        public byte BlueShift;         // 파랑 시프트
        public byte Padding1;
        public byte Padding2;
        public byte Padding3;

        public static PixelFormat Default32Bpp => new PixelFormat
        {
            BitsPerPixel = 32,
            Depth = 24,
            BigEndianFlag = 0,
            TrueColorFlag = 1,
            RedMax = 255,
            GreenMax = 255,
            BlueMax = 255,
            RedShift = 16,
            GreenShift = 8,
            BlueShift = 0
        };
    }
}
