using RfbTest.Authentications;
using RfbTest.Diagnostics;
using RfbTest.Encoders;
using System.Net.Sockets;
using System.Text;

namespace RfbTest.Protocol;

/// <summary>
/// RFB 클라이언트 연결을 처리하는 클래스
/// </summary>
public class RfbClient : IDisposable
{
    private readonly TcpClient _tcpClient;
    private readonly NetworkStream _stream;
    private RfbProtocol.PixelFormat _pixelFormat;
    private List<RfbProtocol.EncodingType> _supportedEncodings;
    private readonly object _sendLock = new object();
    private readonly string? _password; // VNC 비밀번호

    public string ClientName { get; private set; } = string.Empty;
    public ushort FramebufferWidth { get; set; }
    public ushort FramebufferHeight { get; set; }
    public bool IsConnected => _tcpClient?.Connected ?? false;
    
    /// <summary>
    /// 클라이언트가 ExtendedDesktopSize를 지원하는지 확인
    /// </summary>
    public bool SupportsExtendedDesktopSize => _supportedEncodings.Contains(RfbProtocol.EncodingType.ExtendedDesktopSize);
    
    /// <summary>
    /// 클라이언트가 Hextile 인코딩을 지원하는지 확인
    /// </summary>
    public bool SupportsHextile => _supportedEncodings.Contains(RfbProtocol.EncodingType.Hextile);
    
    /// <summary>
    /// 클라이언트가 CopyRect 인코딩을 지원하는지 확인
    /// </summary>
    public bool SupportsCopyRect => _supportedEncodings.Contains(RfbProtocol.EncodingType.CopyRect);
    
    /// <summary>
    /// 클라이언트가 선호하는 인코딩 가져오기
    /// </summary>
    public RfbProtocol.EncodingType GetPreferredEncoding()
    {
        // 우선순위: Hextile > Raw
        if (_supportedEncodings.Contains(RfbProtocol.EncodingType.Hextile))
            return RfbProtocol.EncodingType.Hextile;
        
        return RfbProtocol.EncodingType.Raw;
    }

    // FramebufferUpdateRequest 정보
    public class UpdateRequest
    {
        public bool Incremental { get; set; }
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public ushort Width { get; set; }
        public ushort Height { get; set; }
    }

    public RfbClient(TcpClient client, string? password = null)
    {
        _tcpClient = client;
        _tcpClient.NoDelay = true; // Nagle 알고리즘 비활성화 (즉시 전송)
        _stream = client.GetStream();
        _stream.ReadTimeout = 30000; // 30초 타임아웃
        _stream.WriteTimeout = 30000;
        _pixelFormat = RfbProtocol.PixelFormat.Default32Bpp;
        _supportedEncodings = new List<RfbProtocol.EncodingType>();
        _password = password;
    }

    /// <summary>
    /// RFB 핸드셰이크 수행
    /// </summary>
    public async Task<bool> PerformHandshakeAsync(ushort width, ushort height, string serverName = "RfbTest Server")
    {
        try
        {
            FramebufferWidth = width;
            FramebufferHeight = height;

            RfbLogger.Log($"[Handshake] Starting handshake... FB size: {width}x{height}");

            // 1. 프로토콜 버전 전송
            RfbLogger.Log("[Handshake] Step 1: Sending protocol version");
            await SendProtocolVersionAsync();
            RfbLogger.Log("[Handshake] Step 1: Protocol version sent and flushed");

            // 2. 클라이언트 버전 읽기
            RfbLogger.Log("[Handshake] Step 2: Reading client protocol version");
            var clientVersion = await ReadProtocolVersionAsync();
            RfbLogger.Log($"[Handshake] Step 2: Client version received: '{clientVersion.Trim()}'");

            // 클라이언트 버전 파싱
            bool isVersion38 = clientVersion.Contains("003.008");
            bool isVersion37 = clientVersion.Contains("003.007");
            bool isVersion33 = clientVersion.Contains("003.003");

            RfbLogger.Log($"[Handshake] Detected client version: 3.8={isVersion38}, 3.7={isVersion37}, 3.3={isVersion33}");

            // 3. 보안 협상 (버전에 따라 다른 방식)
            RfbLogger.Log("[Handshake] Step 3: Starting security negotiation");
            if (isVersion38)
            {
                await NegotiateSecurityV38Async();
            }
            else if (isVersion37)
            {
                await NegotiateSecurityV37Async();
            }
            else // RFB 3.3 or older
            {
                await NegotiateSecurityV33Async();
            }
            RfbLogger.Log("[Handshake] Step 3: Security negotiation completed");

            // 4. ClientInit 메시지 읽기
            RfbLogger.Log("[Handshake] Step 4: Reading ClientInit");
            var shareDesktop = await ReadClientInitAsync();
            RfbLogger.Log($"[Handshake] Step 4: ClientInit received - Share desktop: {shareDesktop}");

            // 5. ServerInit 메시지 전송
            RfbLogger.Log("[Handshake] Step 5: Sending ServerInit");
            await SendServerInitAsync(serverName);
            RfbLogger.Log("[Handshake] Step 5: ServerInit sent and flushed");

            RfbLogger.Log("[Handshake] ✓ Handshake completed successfully!");
            return true;
        }
        catch (Exception ex)
        {
            RfbLogger.LogError("[Handshake] ✗ Handshake failed", ex);
            return false;
        }
    }

    private async Task SendProtocolVersionAsync()
    {
        try
        {
            var version = Encoding.ASCII.GetBytes(RfbProtocol.ProtocolVersion38);
            RfbLogger.Log($"[Protocol] Sending: {Encoding.ASCII.GetString(version).Replace("\n", "\\n")}");
            await _stream.WriteAsync(version, 0, version.Length);
            await _stream.FlushAsync();
            RfbLogger.Log($"[Protocol] Sent {version.Length} bytes");
        }
        catch (Exception ex)
        {
            RfbLogger.LogError("[Protocol] Error sending version", ex);
            throw;
        }
    }

    private async Task<string> ReadProtocolVersionAsync()
    {
        try
        {
            var buffer = new byte[12];
            var totalRead = 0;
            RfbLogger.Log("[Protocol] Waiting to read 12 bytes from client...");
            
            while (totalRead < 12)
            {
                var bytesRead = await _stream.ReadAsync(buffer, totalRead, 12 - totalRead);
                if (bytesRead == 0)
                {
                    RfbLogger.Log($"[Protocol] Connection closed after reading {totalRead} bytes");
                    throw new Exception($"Connection closed while reading protocol version (got {totalRead}/12 bytes)");
                }
                totalRead += bytesRead;
                RfbLogger.Log($"[Protocol] Read {bytesRead} bytes, total: {totalRead}/12");
            }
            
            var version = Encoding.ASCII.GetString(buffer);
            RfbLogger.Log($"[Protocol] Received full version string: '{version.Replace("\n", "\\n")}'");
            return version;
        }
        catch (Exception ex)
        {
            RfbLogger.LogError("[Protocol] Error reading version", ex);
            throw;
        }
    }

    private async Task NegotiateSecurityV38Async()
    {
        try
        {
            // RFB 3.8: 보안 타입 리스트 전송
            RfbLogger.Log("[Security] Using RFB 3.8 security negotiation");
            
            // 비밀번호 사용 여부에 따라 보안 타입 결정
            bool usePassword = !string.IsNullOrEmpty(_password);
            
            if (usePassword)
            {
                // VNC Authentication만 지원
                RfbLogger.Log("[Security] Sending security type: VncAuthentication (password required)");
                await _stream.WriteAsync(new byte[] { 1 }, 0, 1); // 1개의 보안 타입
                await _stream.WriteAsync(new byte[] { 
                    (byte)RfbProtocol.SecurityType.VncAuthentication 
                }, 0, 1);
            }
            else
            {
                // None만 지원
                RfbLogger.Log("[Security] Sending security type: None (no password)");
                await _stream.WriteAsync(new byte[] { 1 }, 0, 1);
                await _stream.WriteAsync(new byte[] { (byte)RfbProtocol.SecurityType.None }, 0, 1);
            }
            
            await _stream.FlushAsync();
            RfbLogger.Log("[Security] Security types sent and flushed");

            // 클라이언트의 보안 타입 선택 읽기
            RfbLogger.Log("[Security] Waiting for client security type selection...");
            var selectedSecurity = new byte[1];
            var bytesRead = await _stream.ReadAsync(selectedSecurity, 0, 1);
            
            if (bytesRead == 0)
            {
                throw new Exception("Connection closed while reading security type selection");
            }
            
            var securityType = (RfbProtocol.SecurityType)selectedSecurity[0];
            RfbLogger.Log($"[Security] Client selected security type: {securityType} ({selectedSecurity[0]})");

            // 선택된 보안 타입 처리
            if (securityType == RfbProtocol.SecurityType.VncAuthentication)
            {
                if (!usePassword)
                {
                    RfbLogger.Log("[Security] ERROR: Client selected VncAuthentication but no password is set!");
                    throw new Exception("Client selected VncAuthentication but server has no password");
                }
                await PerformVncAuthenticationAsync();
            }
            else if (securityType == RfbProtocol.SecurityType.None)
            {
                if (usePassword)
                {
                    RfbLogger.Log("[Security] WARNING: Client selected None but password is required!");
                    throw new Exception("Client selected None but server requires password");
                }
                // RFB 3.8 스펙에서는 None일 때 SecurityResult를 보내지 않지만,
                // 일부 클라이언트(UltraVNC 등)와의 호환성을 위해 전송
                RfbLogger.Log("[Security] RFB 3.8 + None security: Sending SecurityResult for compatibility");
                await _stream.WriteAsync(new byte[] { 0, 0, 0, 0 }, 0, 4); // OK
                await _stream.FlushAsync();
                RfbLogger.Log("[Security] SecurityResult sent: OK");
            }
            else
            {
                throw new Exception($"Unsupported security type selected: {securityType}");
            }
        }
        catch (Exception ex)
        {
            RfbLogger.LogError("[Security] Error in RFB 3.8 security negotiation", ex);
            throw;
        }
    }

    private async Task NegotiateSecurityV37Async()
    {
        try
        {
            // RFB 3.7: 보안 타입 리스트 전송 (3.8과 동일)
            RfbLogger.Log("[Security] Using RFB 3.7 security negotiation");
            
            // 비밀번호 사용 여부에 따라 보안 타입 결정
            bool usePassword = !string.IsNullOrEmpty(_password);
            
            if (usePassword)
            {
                // VNC Authentication만 지원
                RfbLogger.Log("[Security] Sending security type: VncAuthentication (password required)");
                await _stream.WriteAsync(new byte[] { 1 }, 0, 1); // 1개의 보안 타입
                await _stream.WriteAsync(new byte[] { 
                    (byte)RfbProtocol.SecurityType.VncAuthentication 
                }, 0, 1);
            }
            else
            {
                // None만 지원
                RfbLogger.Log("[Security] Sending security type: None (no password)");
                await _stream.WriteAsync(new byte[] { 1 }, 0, 1);
                await _stream.WriteAsync(new byte[] { (byte)RfbProtocol.SecurityType.None }, 0, 1);
            }
            
            await _stream.FlushAsync();
            RfbLogger.Log("[Security] Security types sent and flushed");

            // 클라이언트의 보안 타입 선택 읽기
            RfbLogger.Log("[Security] Waiting for client security type selection...");
            var selectedSecurity = new byte[1];
            var bytesRead = await _stream.ReadAsync(selectedSecurity, 0, 1);
            
            if (bytesRead == 0)
            {
                throw new Exception("Connection closed while reading security type selection");
            }
            
            var securityType = (RfbProtocol.SecurityType)selectedSecurity[0];
            RfbLogger.Log($"[Security] Client selected security type: {securityType} ({selectedSecurity[0]})");

            // 선택된 보안 타입 처리
            if (securityType == RfbProtocol.SecurityType.VncAuthentication)
            {
                if (!usePassword)
                {
                    RfbLogger.Log("[Security] ERROR: Client selected VncAuthentication but no password is set!");
                    throw new Exception("Client selected VncAuthentication but server has no password");
                }
                await PerformVncAuthenticationAsync();
            }
            else if (securityType == RfbProtocol.SecurityType.None)
            {
                if (usePassword)
                {
                    RfbLogger.Log("[Security] WARNING: Client selected None but password is required!");
                    throw new Exception("Client selected None but server requires password");
                }
                // RFB 3.7: None 보안 타입이어도 SecurityResult를 전송해야 함!
                RfbLogger.Log("[Security] RFB 3.7 + None security: Sending SecurityResult (required in 3.7)");
                await _stream.WriteAsync(new byte[] { 0, 0, 0, 0 }, 0, 4); // OK
                await _stream.FlushAsync();
                RfbLogger.Log("[Security] SecurityResult sent: OK");
            }
            else
            {
                throw new Exception($"Unsupported security type selected: {securityType}");
            }
        }
        catch (Exception ex)
        {
            RfbLogger.LogError("[Security] Error in RFB 3.7 security negotiation", ex);
            throw;
        }
    }

    private async Task NegotiateSecurityV33Async()
    {
        try
        {
            // RFB 3.3: 서버가 보안 타입을 직접 지정 (4 bytes, big-endian)
            RfbLogger.Log("[Security] Using RFB 3.3 security negotiation");
            
            bool usePassword = !string.IsNullOrEmpty(_password);
            
            if (usePassword)
            {
                RfbLogger.Log("[Security] Sending security type: VncAuthentication (0x00000002)");
                await _stream.WriteAsync(new byte[] { 0, 0, 0, 2 }, 0, 4);
                await _stream.FlushAsync();
                
                await PerformVncAuthenticationAsync();
            }
            else
            {
                RfbLogger.Log("[Security] Sending security type: None (0x00000001)");
                await _stream.WriteAsync(new byte[] { 0, 0, 0, 1 }, 0, 4);
                await _stream.FlushAsync();
                
                // RFB 3.3 스펙에서는 None일 때 SecurityResult를 보내지 않지만,
                // 일부 클라이언트(UltraVNC 등)와의 호환성을 위해 전송
                RfbLogger.Log("[Security] RFB 3.3 + None: Sending SecurityResult for compatibility");
                await _stream.WriteAsync(new byte[] { 0, 0, 0, 0 }, 0, 4);
                await _stream.FlushAsync();
                RfbLogger.Log("[Security] SecurityResult sent: OK");
            }
        }
        catch (Exception ex)
        {
            RfbLogger.LogError("[Security] Error in RFB 3.3 security negotiation", ex);
            throw;
        }
    }

    /// <summary>
    /// VNC 인증 수행
    /// </summary>
    private async Task PerformVncAuthenticationAsync()
    {
        RfbLogger.Log("[Auth] Starting VNC authentication");

        // Challenge 생성 및 전송
        var challenge = VncAuthentication.GenerateChallenge();
        RfbLogger.LogHex("[Auth] Sending challenge", challenge, 16);
        await _stream.WriteAsync(challenge, 0, 16);
        await _stream.FlushAsync();

        // Response 읽기
        RfbLogger.Log("[Auth] Waiting for client response...");
        var response = new byte[16];
        var totalRead = 0;
        while (totalRead < 16)
        {
            var bytesRead = await _stream.ReadAsync(response, totalRead, 16 - totalRead);
            if (bytesRead == 0)
                throw new Exception("Connection closed while reading auth response");
            totalRead += bytesRead;
        }
        RfbLogger.LogHex("[Auth] Received response", response, 16);

        // 검증
        bool isValid = VncAuthentication.VerifyResponse(challenge, response, _password!);
        RfbLogger.Log($"[Auth] Authentication result: {(isValid ? "SUCCESS" : "FAILED")}");

        // SecurityResult 전송
        if (isValid)
        {
            // OK (0)
            await _stream.WriteAsync(new byte[] { 0, 0, 0, 0 }, 0, 4);
            await _stream.FlushAsync();
            RfbLogger.Log("[Auth] Sent SecurityResult: OK");
        }
        else
        {
            // Failed (1)
            await _stream.WriteAsync(new byte[] { 0, 0, 0, 1 }, 0, 4);
            await _stream.FlushAsync();
            RfbLogger.Log("[Auth] Sent SecurityResult: FAILED");
            
            // 실패 메시지 전송 (선택사항)
            var errorMsg = Encoding.UTF8.GetBytes("Authentication failed");
            var errorMsgLen = SwapBytes((uint)errorMsg.Length);
            await _stream.WriteAsync(BitConverter.GetBytes(errorMsgLen), 0, 4);
            await _stream.WriteAsync(errorMsg, 0, errorMsg.Length);
            await _stream.FlushAsync();
            
            throw new Exception("VNC authentication failed");
        }
    }

    private async Task<bool> ReadClientInitAsync()
    {
        try
        {
            RfbLogger.Log("[ClientInit] Waiting for ClientInit message (1 byte)...");
            var buffer = new byte[1];
            var bytesRead = await _stream.ReadAsync(buffer, 0, 1);
            
            if (bytesRead == 0)
            {
                throw new Exception("Connection closed while reading ClientInit");
            }
            
            var shareDesktop = buffer[0] != 0;
            RfbLogger.Log($"[ClientInit] Received: shared-flag={buffer[0]} (share={shareDesktop})");
            return shareDesktop;
        }
        catch (Exception ex)
        {
            RfbLogger.LogError("[ClientInit] Error reading ClientInit", ex);
            throw;
        }
    }

    private async Task SendServerInitAsync(string name)
    {
        try
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            RfbLogger.Log($"[ServerInit] Building ServerInit message:");
            
            // Framebuffer width (2 bytes, big-endian)
            var widthBytes = SwapBytes(FramebufferWidth);
            writer.Write(widthBytes);
            RfbLogger.Log($"[ServerInit]   Width: {FramebufferWidth} (0x{widthBytes:X4})");

            // Framebuffer height (2 bytes, big-endian)
            var heightBytes = SwapBytes(FramebufferHeight);
            writer.Write(heightBytes);
            RfbLogger.Log($"[ServerInit]   Height: {FramebufferHeight} (0x{heightBytes:X4})");

            // PixelFormat (16 bytes)
            RfbLogger.Log($"[ServerInit]   PixelFormat: {_pixelFormat.BitsPerPixel}bpp, depth={_pixelFormat.Depth}");
            WritePixelFormat(writer, _pixelFormat);

            // Name length (4 bytes, big-endian)
            var nameBytes = Encoding.UTF8.GetBytes(name);
            var nameLengthSwapped = SwapBytes((uint)nameBytes.Length);
            writer.Write(nameLengthSwapped);
            RfbLogger.Log($"[ServerInit]   Name length: {nameBytes.Length} (0x{nameLengthSwapped:X8})");

            // Name
            writer.Write(nameBytes);
            RfbLogger.Log($"[ServerInit]   Name: '{name}'");

            var data = ms.ToArray();
            RfbLogger.Log($"[ServerInit] Total ServerInit size: {data.Length} bytes");
            RfbLogger.LogHex("[ServerInit] First bytes", data, 32);
            
            await _stream.WriteAsync(data, 0, data.Length);
            await _stream.FlushAsync();
            RfbLogger.Log($"[ServerInit] ServerInit sent and flushed");
        }
        catch (Exception ex)
        {
            RfbLogger.LogError("[ServerInit] Error sending ServerInit", ex);
            throw;
        }
    }

    private void WritePixelFormat(BinaryWriter writer, RfbProtocol.PixelFormat pf)
    {
        writer.Write(pf.BitsPerPixel);
        writer.Write(pf.Depth);
        writer.Write(pf.BigEndianFlag);
        writer.Write(pf.TrueColorFlag);
        writer.Write(SwapBytes(pf.RedMax));
        writer.Write(SwapBytes(pf.GreenMax));
        writer.Write(SwapBytes(pf.BlueMax));
        writer.Write(pf.RedShift);
        writer.Write(pf.GreenShift);
        writer.Write(pf.BlueShift);
        writer.Write(pf.Padding1);
        writer.Write(pf.Padding2);
        writer.Write(pf.Padding3);
    }

    /// <summary>
    /// 클라이언트 메시지 처리
    /// </summary>
    public async Task<(RfbProtocol.ClientMessageType? MessageType, UpdateRequest? Request)> ReadClientMessageAsync()
    {
        var buffer = new byte[1];
        var bytesRead = await _stream.ReadAsync(buffer, 0, 1);
        
        if (bytesRead == 0)
            return (null, null);

        var messageType = (RfbProtocol.ClientMessageType)buffer[0];
        
        switch (messageType)
        {
            case RfbProtocol.ClientMessageType.SetPixelFormat:
                await HandleSetPixelFormatAsync();
                break;
            case RfbProtocol.ClientMessageType.SetEncodings:
                await HandleSetEncodingsAsync();
                break;
            case RfbProtocol.ClientMessageType.FramebufferUpdateRequest:
                var request = await ReadFramebufferUpdateRequestAsync();
                return (messageType, request);
            case RfbProtocol.ClientMessageType.KeyEvent:
                await HandleKeyEventAsync();
                break;
            case RfbProtocol.ClientMessageType.PointerEvent:
                await HandlePointerEventAsync();
                break;
            case RfbProtocol.ClientMessageType.SetDesktopSize:
                await HandleSetDesktopSizeAsync();
                break;
        }

        return (messageType, null);
    }

    /// <summary>
    /// FramebufferUpdateRequest 읽기
    /// </summary>
    private async Task<UpdateRequest> ReadFramebufferUpdateRequestAsync()
    {
        var buffer = new byte[9]; // incremental(1) + x(2) + y(2) + width(2) + height(2)
        await _stream.ReadAsync(buffer, 0, 9);

        return new UpdateRequest
        {
            Incremental = buffer[0] != 0,
            X = (ushort)((buffer[1] << 8) | buffer[2]),
            Y = (ushort)((buffer[3] << 8) | buffer[4]),
            Width = (ushort)((buffer[5] << 8) | buffer[6]),
            Height = (ushort)((buffer[7] << 8) | buffer[8])
        };
    }

    /// <summary>
    /// 프레임버퍼 업데이트 전송
    /// </summary>
    /// <param name="frameBuffer">프레임버퍼 데이터 (영역만 또는 전체)</param>
    /// <param name="x">X 좌표</param>
    /// <param name="y">Y 좌표</param>
    /// <param name="width">폭</param>
    /// <param name="height">높이</param>
    /// <param name="encoding">인코딩 타입 (null이면 자동 선택)</param>
    /// <param name="isFullBuffer">frameBuffer가 전체 화면인지 여부</param>
    public async Task SendFramebufferUpdateAsync(byte[] frameBuffer, ushort x, ushort y, ushort width, ushort height, 
        RfbProtocol.EncodingType? encoding = null, bool isFullBuffer = false)
    {
        if (!IsConnected) return;

        try
        {
            // 인코딩 타입이 지정되지 않으면 선호하는 인코딩 사용
            var selectedEncoding = encoding ?? GetPreferredEncoding();

            RfbLogger.Log($"╔══════════════════════════════════════════════════════════");
            RfbLogger.Log($"║ FRAMEBUFFER UPDATE");
            RfbLogger.Log($"╠══════════════════════════════════════════════════════════");
            RfbLogger.Log($"║ Region: ({x},{y}) {width}x{height}");
            RfbLogger.Log($"║ Encoding: {selectedEncoding}");
            RfbLogger.Log($"║ IsFullBuffer: {isFullBuffer}");
            RfbLogger.Log($"║ FrameBuffer Size: {frameBuffer.Length} bytes");
            RfbLogger.Log($"║ Client PixelFormat: {_pixelFormat.BitsPerPixel}bpp, BigEndian={_pixelFormat.BigEndianFlag}");
            
            // 원본 프레임버퍼 샘플 (처음 16바이트)
            if (frameBuffer.Length > 0)
            {
                var sampleSize = Math.Min(16, frameBuffer.Length);
                var sample = string.Join(" ", frameBuffer.Take(sampleSize).Select(b => b.ToString("X2")));
                RfbLogger.Log($"║ Source Buffer Sample (first {sampleSize} bytes): {sample}");
                
                // 첫 픽셀 상세 정보 (BGRA 32bpp 가정)
                if (frameBuffer.Length >= 4)
                {
                    RfbLogger.Log($"║ First Pixel (BGRA): B={frameBuffer[0]:X2}, G={frameBuffer[1]:X2}, R={frameBuffer[2]:X2}, A={frameBuffer[3]:X2}");
                }
            }
            RfbLogger.Log($"╚══════════════════════════════════════════════════════════");

            lock (_sendLock)
            {
                using var ms = new MemoryStream();
                using var writer = new BinaryWriter(ms);

                // FramebufferUpdate 메시지 헤더
                writer.Write((byte)RfbProtocol.ServerMessageType.FramebufferUpdate);
                writer.Write((byte)0); // padding
                writer.Write(SwapBytes((ushort)1)); // number-of-rectangles

                // Rectangle 헤더
                writer.Write(SwapBytes(x));
                writer.Write(SwapBytes(y));
                writer.Write(SwapBytes(width));
                writer.Write(SwapBytes(height));
                writer.Write(SwapBytes((int)selectedEncoding));

                // 인코딩에 따라 데이터 쓰기
                byte[] encodedData;
                
                if (selectedEncoding == RfbProtocol.EncodingType.Hextile)
                {
                    // Hextile 인코딩
                    if (isFullBuffer)
                    {
                        // 전체 프레임버퍼에서 영역 추출하여 인코딩
                        encodedData = HextileEncoder.EncodeRegion(frameBuffer, FramebufferWidth, FramebufferHeight, x, y, width, height);
                    }
                    else
                    {
                        // 이미 영역만 추출된 버퍼 - 임시로 0,0부터 인코딩
                        encodedData = HextileEncoder.EncodeRegion(frameBuffer, width, height, 0, 0, width, height);
                    }
                    RfbLogger.Log($"[Encoding] Hextile: {encodedData.Length} bytes for {width}x{height} region (compression: {(float)encodedData.Length / (width * height * 4):P1})");
                }
                else
                {
                    // Raw 인코딩
                    int totalPixels = width * height;
                    int expectedSize = totalPixels * 4;

                    if (isFullBuffer)
                    {
                        // 전체 프레임버퍼에서 영역 추출
                        RfbLogger.Log($"[Encoding] Raw: Extracting region from full buffer {FramebufferWidth}x{FramebufferHeight}");
                        encodedData = ExtractRegion(frameBuffer, FramebufferWidth, FramebufferHeight, x, y, width, height);
                        RfbLogger.Log($"[Encoding] Raw: Extracted {encodedData.Length} bytes (expected {expectedSize} bytes)");
                    }
                    else
                    {
                        // 이미 영역만 추출된 버퍼
                        encodedData = new byte[expectedSize];
                        if (frameBuffer.Length >= expectedSize)
                        {
                            Array.Copy(frameBuffer, 0, encodedData, 0, expectedSize);
                        }
                        else
                        {
                            Array.Copy(frameBuffer, 0, encodedData, 0, frameBuffer.Length);
                        }
                        RfbLogger.Log($"[Encoding] Raw: {encodedData.Length} bytes for {width}x{height} region");
                    }
                }

                // 인코딩된 데이터 샘플
                if (encodedData.Length > 0)
                {
                    var encodedSampleSize = Math.Min(32, encodedData.Length);
                    var encodedSample = string.Join(" ", encodedData.Take(encodedSampleSize).Select(b => b.ToString("X2")));
                    RfbLogger.Log($"[Encoding] Encoded Data Sample (first {encodedSampleSize} bytes): {encodedSample}");
                }

                writer.Write(encodedData);

                var data = ms.ToArray();
                
                RfbLogger.Log($"[Send] Total message size: {data.Length} bytes");
                RfbLogger.Log($"[Send] Header (12 bytes): {string.Join(" ", data.Take(12).Select(b => b.ToString("X2")))}");
                
                _stream.Write(data, 0, data.Length);
                _stream.Flush();
                
                RfbLogger.Log($"[Send] ✓ Framebuffer update sent and flushed");
            }
        }
        catch (Exception ex)
        {
            RfbLogger.LogError("Error sending framebuffer update", ex);
        }
    }

    /// <summary>
    /// 전체 프레임버퍼에서 특정 영역 추출
    /// </summary>
    private byte[] ExtractRegion(byte[] fullBuffer, int bufferWidth, int bufferHeight, 
        int x, int y, int width, int height)
    {
        int bytesPerPixel = 4;
        byte[] region = new byte[width * height * bytesPerPixel];

        for (int row = 0; row < height; row++)
        {
            int srcOffset = ((y + row) * bufferWidth + x) * bytesPerPixel;
            int dstOffset = row * width * bytesPerPixel;
            int rowBytes = width * bytesPerPixel;

            if (srcOffset + rowBytes <= fullBuffer.Length)
            {
                Array.Copy(fullBuffer, srcOffset, region, dstOffset, rowBytes);
            }
        }

        return region;
    }

    /// <summary>
    /// ExtendedDesktopSize 응답 전송 (크기 변경 결과)
    /// </summary>
    /// <param name="width">새로운 너비</param>
    /// <param name="height">새로운 높이</param>
    /// <param name="statusCode">상태 코드 (0=성공, 1=금지됨, 2=범위 밖, 3=메모리 부족)</param>
    public async Task SendExtendedDesktopSizeAsync(ushort width, ushort height, uint statusCode = 0)
    {
        if (!IsConnected || !SupportsExtendedDesktopSize) return;

        try
        {
            lock (_sendLock)
            {
                using var ms = new MemoryStream();
                using var writer = new BinaryWriter(ms);

                // FramebufferUpdate 헤더
                writer.Write((byte)RfbProtocol.ServerMessageType.FramebufferUpdate);
                writer.Write((byte)0); // padding
                writer.Write(SwapBytes((ushort)1)); // 1개의 rectangle

                // ExtendedDesktopSize pseudo-encoding rectangle
                writer.Write(SwapBytes((ushort)0)); // x = 0
                writer.Write(SwapBytes(statusCode)); // y = status code
                writer.Write(SwapBytes(width));
                writer.Write(SwapBytes(height));
                writer.Write(SwapBytes((int)RfbProtocol.EncodingType.ExtendedDesktopSize));

                // 스크린 정보 (1개의 스크린)
                writer.Write((byte)1); // number of screens
                writer.Write((byte)0); // padding
                writer.Write((byte)0); // padding
                writer.Write((byte)0); // padding

                // Screen #0
                writer.Write(SwapBytes((uint)0)); // screen id
                writer.Write(SwapBytes((ushort)0)); // x-position
                writer.Write(SwapBytes((ushort)0)); // y-position
                writer.Write(SwapBytes(width)); // width
                writer.Write(SwapBytes(height)); // height
                writer.Write(SwapBytes((uint)0)); // flags

                var data = ms.ToArray();
                _stream.Write(data, 0, data.Length);
                _stream.Flush();

                RfbLogger.Log($"[ExtendedDesktopSize] Sent desktop size change response: {width}x{height}, status={statusCode}");
            }
        }
        catch (Exception ex)
        {
            RfbLogger.LogError("Error sending ExtendedDesktopSize", ex);
        }
    }

    /// <summary>
    /// CopyRect 인코딩으로 프레임버퍼 업데이트 전송
    /// 화면의 한 영역을 다른 위치로 복사할 때 사용 (좌표만 전송하므로 매우 효율적)
    /// </summary>
    /// <param name="destX">대상 영역 X 좌표</param>
    /// <param name="destY">대상 영역 Y 좌표</param>
    /// <param name="width">영역 너비</param>
    /// <param name="height">영역 높이</param>
    /// <param name="srcX">원본 영역 X 좌표</param>
    /// <param name="srcY">원본 영역 Y 좌표</param>
    public async Task SendCopyRectUpdateAsync(ushort destX, ushort destY, ushort width, ushort height, 
        ushort srcX, ushort srcY)
    {
        if (!IsConnected || !SupportsCopyRect) return;

        try
        {
            RfbLogger.Log($"╔══════════════════════════════════════════════════════════");
            RfbLogger.Log($"║ COPYRECT UPDATE");
            RfbLogger.Log($"╠══════════════════════════════════════════════════════════");
            RfbLogger.Log($"║ Dest Region: ({destX},{destY}) {width}x{height}");
            RfbLogger.Log($"║ Source: ({srcX},{srcY})");
            RfbLogger.Log($"║ Savings: {width * height * 4} bytes (only 4 bytes sent)");
            RfbLogger.Log($"╚══════════════════════════════════════════════════════════");

            lock (_sendLock)
            {
                using var ms = new MemoryStream();
                using var writer = new BinaryWriter(ms);

                // FramebufferUpdate 메시지 헤더
                writer.Write((byte)RfbProtocol.ServerMessageType.FramebufferUpdate);
                writer.Write((byte)0); // padding
                writer.Write(SwapBytes((ushort)1)); // number-of-rectangles

                // Rectangle 헤더
                writer.Write(SwapBytes(destX));
                writer.Write(SwapBytes(destY));
                writer.Write(SwapBytes(width));
                writer.Write(SwapBytes(height));
                writer.Write(SwapBytes((int)RfbProtocol.EncodingType.CopyRect));

                // CopyRect 데이터 (원본 좌표만)
                var copyRectData = CopyRectEncoder.Encode(srcX, srcY);
                writer.Write(copyRectData);

                var data = ms.ToArray();
                
                RfbLogger.Log($"[Send] Total CopyRect message size: {data.Length} bytes (header: 12, data: 4)");
                
                _stream.Write(data, 0, data.Length);
                _stream.Flush();
                
                RfbLogger.Log($"[Send] ✓ CopyRect update sent and flushed");
            }
        }
        catch (Exception ex)
        {
            RfbLogger.LogError("Error sending CopyRect update", ex);
        }
    }
    
    private async Task HandleSetPixelFormatAsync()
    {
        var buffer = new byte[19]; // padding(3) + PixelFormat(16)
        await _stream.ReadAsync(buffer, 0, 19);
        
        // PixelFormat 파싱
        _pixelFormat = new RfbProtocol.PixelFormat
        {
            BitsPerPixel = buffer[3],
            Depth = buffer[4],
            BigEndianFlag = buffer[5],
            TrueColorFlag = buffer[6],
            RedMax = (ushort)((buffer[7] << 8) | buffer[8]),
            GreenMax = (ushort)((buffer[9] << 8) | buffer[10]),
            BlueMax = (ushort)((buffer[11] << 8) | buffer[12]),
            RedShift = buffer[13],
            GreenShift = buffer[14],
            BlueShift = buffer[15]
        };
        
        RfbLogger.Log($"╔══════════════════════════════════════════════════════════");
        RfbLogger.Log($"║ CLIENT PIXEL FORMAT DETAILS");
        RfbLogger.Log($"╠══════════════════════════════════════════════════════════");
        RfbLogger.Log($"║ BitsPerPixel: {_pixelFormat.BitsPerPixel}");
        RfbLogger.Log($"║ Depth: {_pixelFormat.Depth}");
        RfbLogger.Log($"║ BigEndian: {_pixelFormat.BigEndianFlag} (0=Little, 1=Big)");
        RfbLogger.Log($"║ TrueColor: {_pixelFormat.TrueColorFlag}");
        RfbLogger.Log($"║ RedMax: {_pixelFormat.RedMax}, Shift: {_pixelFormat.RedShift}");
        RfbLogger.Log($"║ GreenMax: {_pixelFormat.GreenMax}, Shift: {_pixelFormat.GreenShift}");
        RfbLogger.Log($"║ BlueMax: {_pixelFormat.BlueMax}, Shift: {_pixelFormat.BlueShift}");
        RfbLogger.Log($"╚══════════════════════════════════════════════════════════");
        
        RfbLogger.Log($"Client pixel format: {_pixelFormat.BitsPerPixel}bpp, Depth: {_pixelFormat.Depth}");
    }

    private async Task HandleSetEncodingsAsync()
    {
        var buffer = new byte[3]; // padding(1) + number-of-encodings(2)
        await _stream.ReadAsync(buffer, 0, 3);
        
        var numberOfEncodings = (ushort)((buffer[1] << 8) | buffer[2]);
        var encodings = new byte[numberOfEncodings * 4];
        await _stream.ReadAsync(encodings, 0, encodings.Length);

        _supportedEncodings.Clear();
        for (int i = 0; i < numberOfEncodings; i++)
        {
            var encodingBytes = new byte[4];
            Array.Copy(encodings, i * 4, encodingBytes, 0, 4);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(encodingBytes);
            
            var encoding = (RfbProtocol.EncodingType)BitConverter.ToInt32(encodingBytes, 0);
            _supportedEncodings.Add(encoding);
        }
        
        RfbLogger.Log($"Client supports {numberOfEncodings} encodings: {string.Join(", ", _supportedEncodings)}");
    }

    private async Task HandleKeyEventAsync()
    {
        var buffer = new byte[7]; // down-flag(1) + padding(2) + key(4)
        await _stream.ReadAsync(buffer, 0, 7);
        
        var downFlag = buffer[0] != 0;
        
        // 키 코드 읽기 (big-endian 4 bytes)
        var keyBytes = new byte[4];
        Array.Copy(buffer, 3, keyBytes, 0, 4);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(keyBytes);
        var key = BitConverter.ToUInt32(keyBytes, 0);
        
        RfbLogger.Log($"[Client] KeyEvent: {(downFlag ? "DOWN" : "UP")}, Key: 0x{key:X8}");
        
        // 이벤트 발생
        OnKeyEvent?.Invoke(downFlag, key);
    }

    private async Task HandlePointerEventAsync()
    {
        var buffer = new byte[5]; // button-mask(1) + x(2) + y(2)
        await _stream.ReadAsync(buffer, 0, 5);
        
        var buttonMask = buffer[0];
        var x = (ushort)((buffer[1] << 8) | buffer[2]);
        var y = (ushort)((buffer[3] << 8) | buffer[4]);
        
        RfbLogger.Log($"[Client] PointerEvent: ({x}, {y}), Buttons: 0x{buttonMask:X2}");
        
        // 이벤트 발생
        OnPointerEvent?.Invoke(buttonMask, x, y);
    }

    /// <summary>
    /// SetDesktopSize 메시지 처리 (ExtendedDesktopSize 확장)
    /// </summary>
    private async Task HandleSetDesktopSizeAsync()
    {
        try
        {
            var buffer = new byte[7]; // padding(1) + width(2) + height(2) + number-of-screens(1) + padding(1)
            await _stream.ReadAsync(buffer, 0, 7);
            
            var width = (ushort)((buffer[1] << 8) | buffer[2]);
            var height = (ushort)((buffer[3] << 8) | buffer[4]);
            var numScreens = buffer[5];
            
            RfbLogger.Log($"[SetDesktopSize] Client requested size change: {width}x{height}, screens: {numScreens}");
            
            // 각 스크린 정보 읽기
            for (int i = 0; i < numScreens; i++)
            {
                var screenBuffer = new byte[16]; // id(4) + x(2) + y(2) + width(2) + height(2) + flags(4)
                await _stream.ReadAsync(screenBuffer, 0, 16);
            }
            
            // 크기 변경 이벤트 발생
            OnDesktopSizeChangeRequested?.Invoke(width, height);
        }
        catch (Exception ex)
        {
            RfbLogger.LogError("[SetDesktopSize] Error handling SetDesktopSize", ex);
        }
    }

    /// <summary>
    /// 키보드 이벤트 발생 시 호출되는 이벤트
    /// </summary>
    public event Action<bool, uint>? OnKeyEvent;

    /// <summary>
    /// 마우스/포인터 이벤트 발생 시 호출되는 이벤트
    /// </summary>
    public event Action<byte, ushort, ushort>? OnPointerEvent;

    /// <summary>
    /// 데스크톱 크기 변경 요청 이벤트
    /// </summary>
    public event Action<ushort, ushort>? OnDesktopSizeChangeRequested;

    // 바이트 스왑 헬퍼 메서드 (네트워크 바이트 오더 = Big Endian)
    private ushort SwapBytes(ushort value)
    {
        if (BitConverter.IsLittleEndian)
            return (ushort)((value >> 8) | (value << 8));
        return value;
    }
    
    private uint SwapBytes(uint value)
    {
        if (BitConverter.IsLittleEndian)
            return (value >> 24) | ((value & 0x00FF0000) >> 8) | 
                   ((value & 0x0000FF00) << 8) | (value << 24);
        return value;
    }
    
    private int SwapBytes(int value)
    {
        if (BitConverter.IsLittleEndian)
        {
            var uval = (uint)value;
            return (int)((uval >> 24) | ((uval & 0x00FF0000) >> 8) | 
                        ((uval & 0x0000FF00) << 8) | (uval << 24));
        }
        return value;
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _tcpClient?.Dispose();
    }
}
