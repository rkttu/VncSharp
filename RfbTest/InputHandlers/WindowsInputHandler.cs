using RfbTest.Contracts;
using RfbTest.Diagnostics;
using System.Runtime.InteropServices;

namespace RfbTest.InputHandlers;

/// <summary>
/// Windows 플랫폼용 입력 처리 구현
/// </summary>
public class WindowsInputHandler : IInputHandler
{
    // Windows API 상수
    private const int INPUT_MOUSE = 0;
    private const int INPUT_KEYBOARD = 1;
    
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    // 마우스 버튼 상태 추적
    private byte _lastButtonMask = 0;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    /// <summary>
    /// 키보드 이벤트 처리
    /// </summary>
    public void HandleKeyEvent(bool downFlag, uint key)
    {
        try
        {
            RfbLogger.Log($"[Input] KeyEvent: {(downFlag ? "DOWN" : "UP")}, KeySym: 0x{key:X8}");

            // X11 KeySym을 Windows 가상 키 코드로 변환
            var vkCode = ConvertKeySymToVirtualKey(key);
            
            if (vkCode == 0)
            {
                RfbLogger.Log($"[Input] Unknown KeySym: 0x{key:X8}, skipping");
                return;
            }

            RfbLogger.Log($"[Input] Mapped to VK: 0x{vkCode:X2}");

            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vkCode,
                        wScan = 0,
                        dwFlags = downFlag ? 0 : KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            var result = SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
            
            if (result == 0)
            {
                RfbLogger.Log($"[Input] SendInput failed for key 0x{vkCode:X2}");
            }
        }
        catch (Exception ex)
        {
            RfbLogger.LogError("[Input] Error handling key event", ex);
        }
    }

    /// <summary>
    /// 마우스 이벤트 처리
    /// </summary>
    public void HandlePointerEvent(byte buttonMask, ushort x, ushort y)
    {
        try
        {
            RfbLogger.Log($"[Input] PointerEvent: ({x}, {y}), Buttons: 0x{buttonMask:X2}");

            // 1. 마우스 이동
            SetCursorPos(x, y);

            // 2. 버튼 변경 감지 및 처리
            var changedButtons = (byte)(_lastButtonMask ^ buttonMask);
            
            if (changedButtons != 0)
            {
                var inputs = new List<INPUT>();

                // 왼쪽 버튼 (bit 0)
                if ((changedButtons & 0x01) != 0)
                {
                    inputs.Add(CreateMouseInput(
                        (buttonMask & 0x01) != 0 ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP
                    ));
                    RfbLogger.Log($"[Input] Left button: {((buttonMask & 0x01) != 0 ? "DOWN" : "UP")}");
                }

                // 가운데 버튼 (bit 1)
                if ((changedButtons & 0x02) != 0)
                {
                    inputs.Add(CreateMouseInput(
                        (buttonMask & 0x02) != 0 ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP
                    ));
                    RfbLogger.Log($"[Input] Middle button: {((buttonMask & 0x02) != 0 ? "DOWN" : "UP")}");
                }

                // 오른쪽 버튼 (bit 2)
                if ((changedButtons & 0x04) != 0)
                {
                    inputs.Add(CreateMouseInput(
                        (buttonMask & 0x04) != 0 ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP
                    ));
                    RfbLogger.Log($"[Input] Right button: {((buttonMask & 0x04) != 0 ? "DOWN" : "UP")}");
                }

                // 스크롤 업 (bit 3)
                if ((changedButtons & 0x08) != 0 && (buttonMask & 0x08) != 0)
                {
                    inputs.Add(CreateMouseWheelInput(120));
                    RfbLogger.Log($"[Input] Scroll UP");
                }

                // 스크롤 다운 (bit 4)
                if ((changedButtons & 0x10) != 0 && (buttonMask & 0x10) != 0)
                {
                    inputs.Add(CreateMouseWheelInput(-120));
                    RfbLogger.Log($"[Input] Scroll DOWN");
                }

                if (inputs.Count > 0)
                {
                    var result = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
                    
                    if (result == 0)
                    {
                        RfbLogger.Log($"[Input] SendInput failed for mouse buttons");
                    }
                }
            }

            _lastButtonMask = buttonMask;
        }
        catch (Exception ex)
        {
            RfbLogger.LogError("[Input] Error handling pointer event", ex);
        }
    }

    private INPUT CreateMouseInput(uint flags)
    {
        return new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    private INPUT CreateMouseWheelInput(int delta)
    {
        return new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = (uint)delta,
                    dwFlags = MOUSEEVENTF_WHEEL,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    /// <summary>
    /// X11 KeySym을 Windows Virtual Key Code로 변환
    /// </summary>
    private ushort ConvertKeySymToVirtualKey(uint keySym)
    {
        // Latin-1 문자 (0x0020-0x007E)
        if (keySym >= 0x0020 && keySym <= 0x007E)
        {
            var ch = (char)keySym;
            
            // 영문자는 대문자로 변환 (VK 코드는 대문자 기준)
            if (ch >= 'a' && ch <= 'z')
            {
                return (ushort)(ch - 'a' + 'A');
            }
            
            // 숫자
            if (ch >= '0' && ch <= '9')
            {
                return (ushort)ch;
            }
            
            // 특수 문자 매핑
            return ch switch
            {
                ' ' => 0x20, // VK_SPACE
                _ => 0
            };
        }

        // 특수 키 매핑 (0xFF00-0xFFFF 범위)
        return keySym switch
        {
            // Function Keys
            0xFFBE => 0x70, // F1
            0xFFBF => 0x71, // F2
            0xFFC0 => 0x72, // F3
            0xFFC1 => 0x73, // F4
            0xFFC2 => 0x74, // F5
            0xFFC3 => 0x75, // F6
            0xFFC4 => 0x76, // F7
            0xFFC5 => 0x77, // F8
            0xFFC6 => 0x78, // F9
            0xFFC7 => 0x79, // F10
            0xFFC8 => 0x7A, // F11
            0xFFC9 => 0x7B, // F12

            // Cursor Control
            0xFF50 => 0x24, // Home
            0xFF51 => 0x25, // Left
            0xFF52 => 0x26, // Up
            0xFF53 => 0x27, // Right
            0xFF54 => 0x28, // Down
            0xFF55 => 0x21, // Page Up
            0xFF56 => 0x22, // Page Down
            0xFF57 => 0x23, // End

            // Editing
            0xFF63 => 0x2D, // Insert
            0xFFFF => 0x2E, // Delete
            0xFF08 => 0x08, // BackSpace
            0xFF09 => 0x09, // Tab
            0xFF0D => 0x0D, // Return (Enter)
            0xFF1B => 0x1B, // Escape

            // Modifiers
            0xFFE1 => 0xA0, // Left Shift
            0xFFE2 => 0xA1, // Right Shift
            0xFFE3 => 0xA2, // Left Control
            0xFFE4 => 0xA3, // Right Control
            0xFFE7 => 0xA4, // Left Meta (Windows)
            0xFFE8 => 0xA5, // Right Meta (Windows)
            0xFFE9 => 0xA4, // Left Alt
            0xFFEA => 0xA5, // Right Alt
            0xFFEB => 0x5B, // Left Windows
            0xFFEC => 0x5C, // Right Windows

            // Numpad
            0xFFB0 => 0x60, // Numpad 0
            0xFFB1 => 0x61, // Numpad 1
            0xFFB2 => 0x62, // Numpad 2
            0xFFB3 => 0x63, // Numpad 3
            0xFFB4 => 0x64, // Numpad 4
            0xFFB5 => 0x65, // Numpad 5
            0xFFB6 => 0x66, // Numpad 6
            0xFFB7 => 0x67, // Numpad 7
            0xFFB8 => 0x68, // Numpad 8
            0xFFB9 => 0x69, // Numpad 9
            0xFFAA => 0x6A, // Numpad *
            0xFFAB => 0x6B, // Numpad +
            0xFFAD => 0x6D, // Numpad -
            0xFFAE => 0x6E, // Numpad .
            0xFFAF => 0x6F, // Numpad /
            0xFF8D => 0x0D, // Numpad Enter

            // Print/Pause
            0xFF61 => 0x2C, // Print Screen
            0xFF13 => 0x13, // Pause
            0xFF14 => 0x91, // Scroll Lock
            0xFFE5 => 0x14, // Caps Lock

            _ => 0
        };
    }
}
