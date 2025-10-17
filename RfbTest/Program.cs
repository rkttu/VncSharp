using RfbTest.Contracts;
using RfbTest.Protocol;
using RfbTest.ScreenCaptures;

Console.WriteLine("==============================================");
Console.WriteLine("RFB Server - Console Mode (No UI Display)");
Console.WriteLine("==============================================");
Console.WriteLine();

// 플랫폼 정보 출력
Console.WriteLine($"[Platform] {ScreenCaptureFactory.GetPlatformInfo()}");

if (!ScreenCaptureFactory.IsSupported())
{
    Console.WriteLine();
    Console.WriteLine("❌ ERROR: This platform is not supported.");
    Console.WriteLine("   Only Windows is currently supported.");
    Console.WriteLine();
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey();
    return;
}

Console.WriteLine();

// 서버 설정
string? password = null;
ushort width = 1920;
ushort height = 1080;
int port = 5900;

Console.Write("비밀번호를 설정하시겠습니까? (Y/N): ");
var usePassword = Console.ReadLine()?.Trim().ToUpper() == "Y";

if (usePassword)
{
    Console.Write("VNC 비밀번호 (8자 이내): ");
    password = Console.ReadLine();
    if (!string.IsNullOrEmpty(password) && password.Length > 8)
    {
        password = password.Substring(0, 8);
        Console.WriteLine($"비밀번호가 8자로 잘렸습니다: {password}");
    }
}

Console.WriteLine();
Console.WriteLine($"포트: {port}");
Console.WriteLine($"인증: {(string.IsNullOrEmpty(password) ? "없음" : "VNC Authentication")}");
Console.WriteLine();

// 화면 캡처 초기화
IScreenCapture? screenCapture = null;
byte[]? currentFrameBuffer = null;
object frameBufferLock = new object();

Console.WriteLine("[Init] 화면 캡처 초기화 중...");
try
{
    screenCapture = ScreenCaptureFactory.Create();
    width = (ushort)screenCapture.Width;
    height = (ushort)screenCapture.Height;
    Console.WriteLine($"[Init] 화면 캡처 초기화 완료: {width}x{height}");
    
    // 초기 프레임 캡처 시도
    Console.WriteLine("[Init] 초기 화면 캡처 시도...");
    currentFrameBuffer = screenCapture.CaptureFrame();
    if (currentFrameBuffer == null)
    {
        Console.WriteLine("[Init] 초기 캡처 실패 - 검은 화면으로 시작");
        currentFrameBuffer = new byte[width * height * 4];
    }
    else
    {
        Console.WriteLine($"[Init] 초기 캡처 성공: {currentFrameBuffer.Length} bytes");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[Init] 화면 캡처 초기화 실패: {ex.Message}");
    currentFrameBuffer = new byte[width * height * 4];
}

// RFB 서버 생성
Console.WriteLine($"[Init] RFB 서버 생성 중... ({width}x{height})");
var server = new RfbServer(
    port: port,
    width: width,
    height: height,
    serverName: "RfbTest Console Server",
    password: password
);

// 프레임버퍼 요청 이벤트
int eventCallCount = 0;
server.FrameBufferRequested += () =>
{
    eventCallCount++;
    Console.WriteLine($"[Event] FrameBufferRequested called (#{eventCallCount})");
    
    lock (frameBufferLock)
    {
        if (currentFrameBuffer == null)
        {
            Console.WriteLine($"[Event] Returning empty buffer");
            return new byte[width * height * 4];
        }
        
        // 첫 16바이트 확인
        var sample = string.Join("-", currentFrameBuffer.Take(16).Select(b => b.ToString("X2")));
        Console.WriteLine($"[Event] Current buffer first 16 bytes: {sample}");
        Console.WriteLine($"[Event] Buffer size: {currentFrameBuffer.Length} bytes");
        
        byte[] copy = new byte[currentFrameBuffer.Length];
        Array.Copy(currentFrameBuffer, copy, currentFrameBuffer.Length);
        return copy;
    }
};

// 크기 변경 요청 이벤트
server.FramebufferSizeChangeRequested += (w, h) =>
{
    Console.WriteLine($"[Event] 클라이언트가 크기 변경 요청: {w}x{h} (거부됨)");
};

// 서버 시작
Console.WriteLine("[Init] 서버 시작 중...");
var serverTask = Task.Run(async () => await server.StartAsync());

// 잠시 대기 (서버 시작 확인)
await Task.Delay(500);

Console.WriteLine();
Console.WriteLine("✓ 서버가 시작되었습니다.");
Console.WriteLine($"  연결 주소: localhost:{port}");
Console.WriteLine($"  화면 크기: {width}x{height}");
Console.WriteLine();

// 화면 캡처 루프 시작
CancellationTokenSource? captureCts = null;
Task? captureTask = null;

if (screenCapture != null)
{
    Console.WriteLine("[CaptureLoop] 캡처 루프 시작 중...");
    captureCts = new CancellationTokenSource();
    var token = captureCts.Token;
    
    captureTask = Task.Run(async () =>
    {
        Console.WriteLine("[CaptureLoop] ✓ Task started!");
        int loopCount = 0;
        int changeCount = 0;
        var lastLog = DateTime.Now;
        
        while (!token.IsCancellationRequested && server.IsRunning)
        {
            try
            {
                var buffer = screenCapture.CaptureFrame();
                loopCount++;
                
                if (buffer != null)
                {
                    bool changed = false;
                    lock (frameBufferLock)
                    {
                        // 간단한 변경 감지 (첫 100바이트 비교)
                        if (currentFrameBuffer == null || currentFrameBuffer.Length != buffer.Length)
                        {
                            changed = true;
                        }
                        else
                        {
                            for (int i = 0; i < Math.Min(100, buffer.Length); i++)
                            {
                                if (currentFrameBuffer[i] != buffer[i])
                                {
                                    changed = true;
                                    break;
                                }
                            }
                        }
                        
                        currentFrameBuffer = buffer;
                    }
                    
                    if (changed)
                    {
                        changeCount++;
                        if (changeCount <= 3)
                        {
                            Console.WriteLine($"[CaptureLoop] ✓ Frame CHANGED! (#{changeCount})");
                        }
                    }
                }
                
                // 5초마다 상태 출력
                if ((DateTime.Now - lastLog).TotalSeconds >= 5.0)
                {
                    Console.WriteLine($"[CaptureLoop] Status - Loops: {loopCount}, Changes: {changeCount}, Clients: {server.ClientCount}");
                    loopCount = 0;
                    changeCount = 0;
                    lastLog = DateTime.Now;
                }
                
                await Task.Delay(33, token); // ~30 FPS
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CaptureLoop] Error: {ex.Message}");
                await Task.Delay(1000, token);
            }
        }
        
        Console.WriteLine("[CaptureLoop] Task ended");
    }, token);
    
    Console.WriteLine("[CaptureLoop] ✓ 캡처 루프 시작됨 (30 FPS)");
}

Console.WriteLine();
Console.WriteLine("명령어: status, clients, quit");
Console.WriteLine();

// 명령어 루프
bool shouldExit = false;
while (!shouldExit)
{
    Console.Write("> ");
    var command = Console.ReadLine()?.Trim().ToLower();
    
    switch (command)
    {
        case "status":
            Console.WriteLine($"  서버: {(server.IsRunning ? "실행 중" : "중지됨")}");
            Console.WriteLine($"  클라이언트: {server.ClientCount}");
            Console.WriteLine($"  화면: {server.FramebufferSize.Width}x{server.FramebufferSize.Height}");
            break;
            
        case "clients":
            Console.WriteLine($"  연결된 클라이언트: {server.ClientCount}");
            break;
            
        case "quit":
        case "exit":
        case "q":
            shouldExit = true;
            break;
            
        default:
            if (!string.IsNullOrEmpty(command))
            {
                Console.WriteLine("  명령어: status, clients, quit");
            }
            break;
    }
}

// 정리
Console.WriteLine();
Console.WriteLine("[Cleanup] 서버 종료 중...");

captureCts?.Cancel();
if (captureTask != null)
{
    await captureTask;
}

server.Stop();
server.Dispose();
screenCapture?.Dispose();

Console.WriteLine("[Cleanup] ✓ 종료 완료");

