using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;

namespace KCAS.Admin.Data;

internal static class EmployeeTransferEncryption
{
    private const string Magic = "KCAS-EMPLOYEE-1";
    internal const int MaximumBytes = 50 * 1024 * 1024;
    private const int Iterations = 300_000;
    public static void ValidatePassphrase(string passphrase)
    {
        if (passphrase.Length < 7 || passphrase.Length > 1024)
            throw new ValidationException("Use a package passphrase of at least 7 characters (maximum 1024).");
    }

    public static byte[] Encrypt(byte[] plaintext, string passphrase)
    {
        ValidatePassphrase(passphrase);
        if (plaintext.Length > MaximumBytes) throw new ValidationException("The employee package exceeds 50 MB. Select fewer employees.");
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Iterations, HashAlgorithmName.SHA256, 32);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[16];
        try { using var aes = new AesGcm(key, 16); aes.Encrypt(nonce, plaintext, cipher, tag, Encoding.UTF8.GetBytes(Magic)); }
        finally { CryptographicOperations.ZeroMemory(key); }
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(Magic); writer.Write(Iterations);
        writer.Write(salt); writer.Write(nonce); writer.Write(tag); writer.Write(cipher.Length); writer.Write(cipher);
        return stream.ToArray();
    }

    public static byte[] Decrypt(byte[] encrypted, string passphrase)
    {
        ValidatePassphrase(passphrase);
        if (encrypted.Length > MaximumBytes + 1024) throw new ValidationException("The employee package exceeds 50 MB.");
        try
        {
            using var stream = new MemoryStream(encrypted);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            if (reader.ReadString() != Magic || reader.ReadInt32() != Iterations) throw new ValidationException("Not a supported KCAS employee package.");
            var salt = Read(reader, 16); var nonce = Read(reader, 12); var tag = Read(reader, 16);
            var length = reader.ReadInt32();
            if (length < 1 || length > MaximumBytes || length != stream.Length - stream.Position) throw new ValidationException("Invalid employee package length.");
            var cipher = Read(reader, length);
            var plain = new byte[length];
            var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Iterations, HashAlgorithmName.SHA256, 32);
            try { using var aes = new AesGcm(key, 16); aes.Decrypt(nonce, cipher, tag, plain, Encoding.UTF8.GetBytes(Magic)); }
            finally { CryptographicOperations.ZeroMemory(key); }
            return plain;
        }
        catch (CryptographicException) { throw new ValidationException("The package could not be decrypted. Check its passphrase and integrity."); }
        catch (IOException) { throw new ValidationException("The employee package is truncated or invalid."); }
        catch (FormatException) { throw new ValidationException("The employee package header is invalid."); }
    }
    private static byte[] Read(BinaryReader reader, int length)
    {
        var value = reader.ReadBytes(length);
        if (value.Length != length) throw new ValidationException("The employee package is truncated.");
        return value;
    }
}
