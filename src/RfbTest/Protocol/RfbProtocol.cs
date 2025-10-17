namespace RfbTest.Protocol;

/// <summary>
/// RFB(Remote Framebuffer) �������� ���� �� ���
/// </summary>
public static class RfbProtocol
{
    // RFB �������� ����
    public const string ProtocolVersion38 = "RFB 003.008\n";
    public const string ProtocolVersion37 = "RFB 003.007\n";
    public const string ProtocolVersion33 = "RFB 003.003\n";

    // ���� Ÿ��
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

    // Ŭ���̾�Ʈ->���� �޽��� Ÿ��
    public enum ClientMessageType : byte
    {
        SetPixelFormat = 0,
        SetEncodings = 2,
        FramebufferUpdateRequest = 3,
        KeyEvent = 4,
        PointerEvent = 5,
        ClientCutText = 6,
        SetDesktopSize = 251  // ExtendedDesktopSize Ȯ��
    }

    // ����->Ŭ���̾�Ʈ �޽��� Ÿ��
    public enum ServerMessageType : byte
    {
        FramebufferUpdate = 0,
        SetColorMapEntries = 1,
        Bell = 2,
        ServerCutText = 3
    }

    // ���ڵ� Ÿ��
    public enum EncodingType : int
    {
        Raw = 0,
        CopyRect = 1,
        RRE = 2,
        Hextile = 5,
        ZRLE = 16,
        Cursor = -239,
        DesktopSize = -223,
        ExtendedDesktopSize = -308,  // ExtendedDesktopSize Ȯ�� ����
        LastRect = -224,
        PointerPos = -232,
        JPEG = 21,
        JPEG_QualityLevel0 = -32,
        JPEG_QualityLevel9 = -23,
        CompressionLevel0 = -256,
        CompressionLevel9 = -247
    }

    // �ȼ� ����
    public struct PixelFormat
    {
        public byte BitsPerPixel;      // �ȼ��� ��Ʈ ��
        public byte Depth;             // ���� ����
        public byte BigEndianFlag;     // �򿣵�� ����
        public byte TrueColorFlag;     // Ʈ���÷� ����
        public ushort RedMax;          // ���� �ִ밪
        public ushort GreenMax;        // ��� �ִ밪
        public ushort BlueMax;         // �Ķ� �ִ밪
        public byte RedShift;          // ���� ����Ʈ
        public byte GreenShift;        // ��� ����Ʈ
        public byte BlueShift;         // �Ķ� ����Ʈ
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
