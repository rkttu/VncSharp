using RfbTest.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace RfbTest.Protocol;

/// <summary>
/// RFB 서버 구현
/// </summary>
public class RfbServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly List<RfbClient> _clients;
    private bool _isRunning;
    private ushort _framebufferWidth;
    private ushort _framebufferHeight;
    private readonly string _serverName;
    private byte[] _currentFrameBuffer;
    private readonly object _frameBufferLock = new object();
    private string? _password; // VNC 비밀번호
    private readonly object _passwordLock = new object(); // 비밀번호 변경 시 동기화
    private readonly Dictionary<RfbClient, FrameBufferDiff> _clientDiffs; // 클라이언트별 변경 감지

    public IPAddress Address { get; }
    public int Port { get; }
    public int ClientCount => _clients.Count;
    public bool IsRunning => _isRunning;
    
    /// <summary>
    /// 현재 프레임버퍼 크기
    /// </summary>
    public (ushort Width, ushort Height) FramebufferSize
    {
        get
        {
            lock (_frameBufferLock)
            {
                return (_framebufferWidth, _framebufferHeight);
            }
        }
    }
        
    /// <summary>
    /// VNC 비밀번호 (스레드 안전)
    /// </summary>
    public string? Password 
    { 
        get 
        { 
            lock (_passwordLock)
            {
                return _password;
            }
        }
        set 
        { 
            lock (_passwordLock)
            {
                var oldPassword = _password;
                _password = value;
                
                if (IsRunning)
                {
                    var authMode = string.IsNullOrEmpty(_password) ? "None" : "VNC Authentication";
                    RfbLogger.Log($"[Server] Password changed while running. New auth mode: {authMode}");
                }
            }
        }
    }

    // 이벤트: 프레임버퍼가 필요할 때 발생
    public event Func<byte[]>? FrameBufferRequested;
    
    // 이벤트: 프레임버퍼 크기 변경 요청
    public event Action<ushort, ushort>? FramebufferSizeChangeRequested;

    public RfbServer(IPAddress? address = default, int port = 5900, ushort width = 1024, ushort height = 768, string serverName = "RfbTest Server", string? password = null)
    {
        Address = address ?? IPAddress.Any;
        Port = port;
        _framebufferWidth = width;
        _framebufferHeight = height;
        _serverName = serverName;
        _password = password;
        _listener = new TcpListener(IPAddress.Any, port);
        _clients = new List<RfbClient>();
        _passwordLock = new object();
        _clientDiffs = new Dictionary<RfbClient, FrameBufferDiff>();
        
        // 초기 프레임버퍼 생성 (검은 화면)
        _currentFrameBuffer = new byte[width * height * 4];
    }

    /// <summary>
    /// 프레임버퍼 크기 변경
    /// </summary>
    public async Task ResizeFramebufferAsync(ushort newWidth, ushort newHeight)
    {
        lock (_frameBufferLock)
        {
            if (_framebufferWidth == newWidth && _framebufferHeight == newHeight)
            {
                RfbLogger.Log($"[Server] Framebuffer size unchanged: {newWidth}x{newHeight}");
                return;
            }

            RfbLogger.Log($"[Server] Resizing framebuffer: {_framebufferWidth}x{_framebufferHeight} → {newWidth}x{newHeight}");

            _framebufferWidth = newWidth;
            _framebufferHeight = newHeight;
            _currentFrameBuffer = new byte[newWidth * newHeight * 4];

            // 모든 클라이언트의 diff 리셋
            foreach (var diff in _clientDiffs.Values)
            {
                diff.Resize(newWidth, newHeight);
            }
        }

        // 모든 클라이언트에게 크기 변경 알림
        await BroadcastDesktopSizeChangeAsync(newWidth, newHeight);
    }

    /// <summary>
    /// 모든 클라이언트에게 데스크톱 크기 변경 알림
    /// </summary>
    private async Task BroadcastDesktopSizeChangeAsync(ushort width, ushort height)
    {
        RfbClient[] clientsCopy;
        lock (_clients)
        {
            clientsCopy = _clients.ToArray();
        }

        var tasks = new List<Task>();
        foreach (var client in clientsCopy)
        {
            try
            {
                if (client.IsConnected)
                {
                    // 크기 업데이트
                    client.FramebufferWidth = width;
                    client.FramebufferHeight = height;

                    // ExtendedDesktopSize 지원 클라이언트에게 알림
                    if (client.SupportsExtendedDesktopSize)
                    {
                        tasks.Add(client.SendExtendedDesktopSizeAsync(width, height, 0));
                    }
                }
            }
            catch (Exception ex)
            {
                RfbLogger.LogError("Error notifying client of size change", ex);
            }
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
            RfbLogger.Log($"[Server] Desktop size change notified to {tasks.Count} clients");
        }
    }

    /// <summary>
    /// 현재 프레임버퍼 설정
    /// </summary>
    public void SetFrameBuffer(byte[] frameBuffer)
    {
        lock (_frameBufferLock)
        {
            if (frameBuffer.Length == _currentFrameBuffer.Length)
            {
                Array.Copy(frameBuffer, _currentFrameBuffer, frameBuffer.Length);
            }
        }
    }

    /// <summary>
    /// 현재 프레임버퍼 가져오기
    /// </summary>
    private byte[] GetCurrentFrameBuffer()
    {
        lock (_frameBufferLock)
        {
            RfbLogger.Log($"[GetCurrentFrameBuffer] Called. FrameBufferRequested event null? {FrameBufferRequested == null}");
            
            // 이벤트가 있으면 최신 프레임버퍼 요청
            if (FrameBufferRequested != null)
            {
                var newBuffer = FrameBufferRequested.Invoke();
                RfbLogger.Log($"[GetCurrentFrameBuffer] Event returned buffer: {newBuffer?.Length ?? 0} bytes");
                
                if (newBuffer != null && newBuffer.Length == _currentFrameBuffer.Length)
                {
                    Array.Copy(newBuffer, _currentFrameBuffer, newBuffer.Length);
                    
                    // 처음 16바이트 로그 (픽셀 데이터 확인)
                    var sample = string.Join(" ", newBuffer.Take(16).Select(b => b.ToString("X2")));
                    RfbLogger.Log($"[GetCurrentFrameBuffer] First 16 bytes: {sample}");
                }
            }
            
            // 복사본 반환
            var buffer = new byte[_currentFrameBuffer.Length];
            Array.Copy(_currentFrameBuffer, buffer, _currentFrameBuffer.Length);
            RfbLogger.Log($"[GetCurrentFrameBuffer] Returning buffer: {buffer.Length} bytes");
            return buffer;
        }
    }

    /// <summary>
    /// 서버 시작
    /// </summary>
    public async Task StartAsync()
    {
        try
        {
            _listener.Start();
            _isRunning = true;
            
            RfbLogger.Log("============================================================");
            RfbLogger.Log($"RFB Server started on port {Port}");
            RfbLogger.Log($"Framebuffer size: {_framebufferWidth}x{_framebufferHeight}");
            RfbLogger.Log($"Server name: {_serverName}");
            RfbLogger.Log($"Authentication: {(string.IsNullOrEmpty(_password) ? "None (No Password)" : "VNC Authentication (Password Required)")}");
            RfbLogger.Log("============================================================");

            while (_isRunning)
            {
                try
                {
                    RfbLogger.Log($"[Server] Waiting for client connections on port {Port}...");
                    var tcpClient = await _listener.AcceptTcpClientAsync();
                    var remoteEndPoint = tcpClient.Client.RemoteEndPoint?.ToString() ?? "unknown";
                    RfbLogger.Log($"[Server] ? TCP connection accepted from {remoteEndPoint}");

                    // 클라이언트 처리를 별도 태스크로 실행
                    _ = Task.Run(() => HandleClientAsync(tcpClient));
                }
                catch (Exception ex) when (_isRunning)
                {
                    RfbLogger.LogError("[Server] Error accepting client", ex);
                }
            }
        }
        catch (Exception ex)
        {
            RfbLogger.LogError("[Server] Fatal error in server", ex);
            _isRunning = false;
        }
    }

    /// <summary>
    /// 클라이언트 연결 처리
    /// </summary>
    private async Task HandleClientAsync(TcpClient tcpClient)
    {
        RfbClient? client = null;
        var remoteEndPoint = tcpClient.Client.RemoteEndPoint?.ToString() ?? "unknown";
        
        try
        {
            RfbLogger.Log($"[Server] New client connection from {remoteEndPoint}");
            
            // 현재 비밀번호 스냅샷 가져오기 (스레드 안전)
            string? currentPassword;
            lock (_passwordLock)
            {
                currentPassword = _password;
            }
            
            var authMode = string.IsNullOrEmpty(currentPassword) ? "None" : "VNC Authentication";
            RfbLogger.Log($"[Server] Client will use authentication mode: {authMode}");
            
            client = new RfbClient(tcpClient, currentPassword); // 현재 비밀번호 전달
            
            // 데스크톱 크기 변경 요청 이벤트 연결
            client.OnDesktopSizeChangeRequested += async (width, height) =>
            {
                RfbLogger.Log($"[Server] Client {remoteEndPoint} requested desktop size: {width}x{height}");
                
                // 서버 이벤트 발생 (UI에서 처리)
                FramebufferSizeChangeRequested?.Invoke(width, height);
            };
            
            // 핸드셰이크 수행
            RfbLogger.Log($"[Server] Starting handshake with {remoteEndPoint}");
            var success = await client.PerformHandshakeAsync(_framebufferWidth, _framebufferHeight, _serverName);
            
            if (!success)
            {
                RfbLogger.Log($"[Server] Handshake failed for {remoteEndPoint}");
                return;
            }

            lock (_clients)
            {
                _clients.Add(client);
                // 클라이언트별 변경 감지 객체 생성
                _clientDiffs[client] = new FrameBufferDiff(_framebufferWidth, _framebufferHeight);
            }

            RfbLogger.Log($"[Server] Client {remoteEndPoint} handshake completed. Total clients: {ClientCount}");

            // 클라이언트 메시지 루프
            await ClientMessageLoopAsync(client);
        }
        catch (Exception ex)
        {
            RfbLogger.LogError($"[Server] Client {remoteEndPoint} error", ex);
        }
        finally
        {
            if (client != null)
            {
                lock (_clients)
                {
                    _clients.Remove(client);
                    _clientDiffs.Remove(client); // 변경 감지 객체 제거
                }
                client.Dispose();
                RfbLogger.Log($"[Server] Client {remoteEndPoint} disconnected. Total clients: {ClientCount}");
            }
        }
    }

    /// <summary>
    /// 클라이언트 메시지 처리 루프
    /// </summary>
    private async Task ClientMessageLoopAsync(RfbClient client)
    {
        RfbLogger.Log("[MessageLoop] Starting client message loop");
        RfbLogger.Log("[MessageLoop] Waiting for client's first FramebufferUpdateRequest...");

        while (client.IsConnected && _isRunning)
        {
            try
            {
                var result = await client.ReadClientMessageAsync();
                
                if (result.MessageType == null)
                {
                    RfbLogger.Log("[MessageLoop] Client disconnected (null message)");
                    break;
                }

                RfbLogger.Log($"[MessageLoop] Received message: {result.MessageType}");

                if (result.MessageType == RfbProtocol.ClientMessageType.FramebufferUpdateRequest && result.Request != null)
                {
                    RfbLogger.Log($"[MessageLoop] FramebufferUpdateRequest: Incremental={result.Request.Incremental}, " +
                                    $"Region=({result.Request.X},{result.Request.Y})-({result.Request.Width}x{result.Request.Height})");
                    // FramebufferUpdateRequest 처리
                    await HandleFramebufferUpdateRequestAsync(client, result.Request);
                }
            }
            catch (Exception ex)
            {
                RfbLogger.LogError("[MessageLoop] Error in message loop", ex);
                break;
            }
        }
        
        RfbLogger.Log("[MessageLoop] Client message loop ended");
    }

    /// <summary>
    /// FramebufferUpdateRequest 처리
    /// </summary>
    private async Task HandleFramebufferUpdateRequestAsync(RfbClient client, RfbClient.UpdateRequest request)
    {
        try
        {
            // 현재 프레임버퍼 가져오기
            var frameBuffer = GetCurrentFrameBuffer();
            
            RfbLogger.Log($"[UpdateRequest] GetCurrentFrameBuffer returned {frameBuffer.Length} bytes");

            // 클라이언트별 변경 감지 객체 가져오기
            FrameBufferDiff? diff = null;
            lock (_clients)
            {
                _clientDiffs.TryGetValue(client, out diff);
            }

            if (diff == null)
            {
                RfbLogger.Log($"[UpdateRequest] No diff tracker for client, sending full update");
                await client.SendFramebufferUpdateAsync(frameBuffer, 0, 0, _framebufferWidth, _framebufferHeight, 
                    isFullBuffer: true);
                return;
            }

            // Incremental 요청 처리
            if (request.Incremental)
            {
                RfbLogger.Log($"[UpdateRequest] Incremental update requested");
                
                // 변경된 영역 감지
                var dirtyRegion = diff.DetectChanges(frameBuffer);

                if (dirtyRegion.IsEmpty)
                {
                    // 변경 없음 - 아무것도 전송하지 않음
                    RfbLogger.Log($"[UpdateRequest] No changes detected, skipping update");
                    return;
                }

                RfbLogger.Log($"[UpdateRequest] Detected changes: {dirtyRegion}");

                // 전체 프레임버퍼를 전달하고 isFullBuffer=true
                // Hextile 인코더가 필요한 영역만 추출함
                await client.SendFramebufferUpdateAsync(
                    frameBuffer, 
                    dirtyRegion.X, 
                    dirtyRegion.Y, 
                    dirtyRegion.Width, 
                    dirtyRegion.Height,
                    isFullBuffer: true
                );

                RfbLogger.Log($"[UpdateRequest] Sent incremental update: {dirtyRegion.Width}x{dirtyRegion.Height} at ({dirtyRegion.X},{dirtyRegion.Y})");
            }
            else
            {
                // Full (non-incremental) 요청
                RfbLogger.Log($"[UpdateRequest] Full (non-incremental) update requested");
                
                // diff 리셋 (전체 화면을 새로 기준으로 설정)
                diff.ForceFullUpdate();
                
                var x = Math.Min(request.X, _framebufferWidth);
                var y = Math.Min(request.Y, _framebufferHeight);
                var width = Math.Min(request.Width, (ushort)(_framebufferWidth - x));
                var height = Math.Min(request.Height, (ushort)(_framebufferHeight - y));

                // 전체 프레임버퍼 전달
                await client.SendFramebufferUpdateAsync(frameBuffer, x, y, width, height, isFullBuffer: true);
                
                // diff 초기화 (현재 프레임을 기준으로)
                diff.DetectChanges(frameBuffer);

                RfbLogger.Log($"[UpdateRequest] Sent full update: {width}x{height} at ({x},{y})");
            }
        }
        catch (Exception ex)
        {
            RfbLogger.LogError("Error handling framebuffer update request", ex);
        }
    }

    /// <summary>
    /// 모든 클라이언트에게 프레임버퍼 업데이트 브로드캐스트
    /// </summary>
    public async Task BroadcastFramebufferUpdateAsync(byte[] frameBuffer)
    {
        // 프레임버퍼 업데이트
        SetFrameBuffer(frameBuffer);

        RfbClient[] clientsCopy;
        Dictionary<RfbClient, FrameBufferDiff> diffsCopy;
        
        lock (_clients)
        {
            clientsCopy = _clients.ToArray();
            diffsCopy = new Dictionary<RfbClient, FrameBufferDiff>(_clientDiffs);
        }

        if (clientsCopy.Length == 0)
        {
            return; // 클라이언트가 없으면 스킵
        }

        RfbLogger.Log($"[Broadcast] Broadcasting to {clientsCopy.Length} client(s), buffer size: {frameBuffer.Length} bytes");

        var tasks = new List<Task>();
        
        foreach (var client in clientsCopy)
        {
            try
            {
                if (client.IsConnected && diffsCopy.TryGetValue(client, out var diff))
                {
                    // 변경 감지
                    var dirtyRegion = diff.DetectChanges(frameBuffer);
                    
                    if (!dirtyRegion.IsEmpty)
                    {
                        RfbLogger.Log($"[Broadcast] Detected dirty region: {dirtyRegion}");
                        
                        // 전체 프레임버퍼 전달 (Hextile이 필요한 부분만 인코딩)
                        tasks.Add(client.SendFramebufferUpdateAsync(
                            frameBuffer,
                            dirtyRegion.X,
                            dirtyRegion.Y,
                            dirtyRegion.Width,
                            dirtyRegion.Height,
                            isFullBuffer: true
                        ));
                    }
                    else
                    {
                        RfbLogger.Log($"[Broadcast] No changes detected for client");
                    }
                }
                else
                {
                    RfbLogger.Log($"[Broadcast] Client not connected or no diff tracker");
                }
            }
            catch (Exception ex)
            {
                RfbLogger.LogError("[Broadcast] Error broadcasting to client", ex);
            }
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
            RfbLogger.Log($"[Broadcast] Broadcast completed to {tasks.Count} clients");
        }
        else
        {
            RfbLogger.Log($"[Broadcast] No updates sent (no changes detected)");
        }
    }

    /// <summary>
    /// 모든 클라이언트에게 전체 화면 강제 전송
    /// </summary>
    public async Task BroadcastFullUpdateAsync()
    {
        var frameBuffer = GetCurrentFrameBuffer();

        RfbClient[] clientsCopy;
        lock (_clients)
        {
            clientsCopy = _clients.ToArray();
            
            // 모든 클라이언트의 변경 감지 리셋
            foreach (var diff in _clientDiffs.Values)
            {
                diff.ForceFullUpdate();
            }
        }

        var tasks = new List<Task>();
        foreach (var client in clientsCopy)
        {
            try
            {
                if (client.IsConnected)
                {
                    tasks.Add(client.SendFramebufferUpdateAsync(frameBuffer, 0, 0, _framebufferWidth, _framebufferHeight, 
                        isFullBuffer: true));
                }
            }
            catch (Exception ex)
            {
                RfbLogger.LogError("Error sending full update to client", ex);
            }
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
            RfbLogger.Log($"[Broadcast] Full update sent to {tasks.Count} clients");
        }
    }

    /// <summary>
    /// 서버 중지
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
        {
            RfbLogger.Log("Server is already stopped");
            return;
        }

        RfbLogger.Log("Stopping RFB Server...");
        _isRunning = false;
        _listener.Stop();

        lock (_clients)
        {
            foreach (var client in _clients)
            {
                client.Dispose();
            }
            _clients.Clear();
            _clientDiffs.Clear();
        }

        RfbLogger.Log("RFB Server stopped");
    }

    public void Dispose()
    {
        Stop();
    }
}
