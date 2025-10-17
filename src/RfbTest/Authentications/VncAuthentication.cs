using System.Security.Cryptography;
using System.Text;

namespace RfbTest.Authentications;

/// <summary>
/// VNC 인증 (DES 기반)
/// </summary>
public static class VncAuthentication
{
    /// <summary>
    /// VNC 비밀번호를 8바이트로 변환 (패딩)
    /// </summary>
    public static byte[] PreparePasswordKey(string password)
    {
        // VNC 비밀번호는 8자로 제한됨
        var key = new byte[8];
        var passwordBytes = Encoding.ASCII.GetBytes(password);
        
        // 8바이트로 복사 (부족하면 0으로 패딩)
        int copyLength = Math.Min(passwordBytes.Length, 8);
        Array.Copy(passwordBytes, key, copyLength);
        
        // VNC는 각 바이트의 비트 순서를 뒤집음
        for (int i = 0; i < 8; i++)
        {
            key[i] = ReverseBits(key[i]);
        }
        
        return key;
    }

    /// <summary>
    /// 바이트의 비트 순서를 뒤집음
    /// </summary>
    private static byte ReverseBits(byte b)
    {
        byte result = 0;
        for (int i = 0; i < 8; i++)
        {
            result <<= 1;
            result |= (byte)(b & 1);
            b >>= 1;
        }
        return result;
    }

    /// <summary>
    /// Challenge를 비밀번호로 암호화
    /// </summary>
    public static byte[] EncryptChallenge(byte[] challenge, string password)
    {
        if (challenge.Length != 16)
            throw new ArgumentException("Challenge must be 16 bytes", nameof(challenge));

        var key = PreparePasswordKey(password);
        var encrypted = new byte[16];

        // DES ECB 모드로 암호화
        using (var des = DES.Create())
        {
            des.Mode = CipherMode.ECB;
            des.Padding = PaddingMode.None;
            des.Key = key;

            using (var encryptor = des.CreateEncryptor())
            {
                encryptor.TransformBlock(challenge, 0, 16, encrypted, 0);
            }
        }

        return encrypted;
    }

    /// <summary>
    /// 16바이트 랜덤 Challenge 생성
    /// </summary>
    public static byte[] GenerateChallenge()
    {
        var challenge = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(challenge);
        }
        return challenge;
    }

    /// <summary>
    /// 응답 검증
    /// </summary>
    public static bool VerifyResponse(byte[] challenge, byte[] response, string password)
    {
        var expected = EncryptChallenge(challenge, password);
        
        if (expected.Length != response.Length)
            return false;

        for (int i = 0; i < expected.Length; i++)
        {
            if (expected[i] != response[i])
                return false;
        }

        return true;
    }
}
