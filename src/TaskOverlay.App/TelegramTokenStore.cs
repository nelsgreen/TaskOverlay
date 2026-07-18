using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TaskOverlay.App;

public sealed class TelegramTokenStore
{
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("TaskOverlay.V2.TelegramCapture.BotToken.v1");

    private readonly Action<string, Exception?>? _diagnostic;

    public TelegramTokenStore(
        string stateDirectory,
        Action<string, Exception?>? diagnostic = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        StateDirectory = stateDirectory;
        _diagnostic = diagnostic;
    }

    public string StateDirectory { get; }
    public string TokenPath => Path.Combine(StateDirectory, "telegramBotToken.dpapi");

    public bool HasToken() => File.Exists(TokenPath);

    public bool SaveToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            ClearToken();
            return true;
        }

        try
        {
            Directory.CreateDirectory(StateDirectory);
            var plaintext = Encoding.UTF8.GetBytes(token.Trim());
            var protectedBytes = ProtectedData.Protect(
                plaintext,
                Entropy,
                DataProtectionScope.CurrentUser);
            File.WriteAllBytes(TokenPath, protectedBytes);
            CryptographicOperations.ZeroMemory(plaintext);
            return true;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            CryptographicException)
        {
            Report("Telegram token save failed.", ex);
            return false;
        }
    }

    public string? LoadToken()
    {
        try
        {
            if (!File.Exists(TokenPath))
            {
                return null;
            }

            var protectedBytes = File.ReadAllBytes(TokenPath);
            var plaintext = ProtectedData.Unprotect(
                protectedBytes,
                Entropy,
                DataProtectionScope.CurrentUser);
            try
            {
                return Encoding.UTF8.GetString(plaintext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            CryptographicException)
        {
            Report("Telegram token load failed.", ex);
            return null;
        }
    }

    public bool ClearToken()
    {
        try
        {
            if (File.Exists(TokenPath))
            {
                File.Delete(TokenPath);
            }

            return true;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            Report("Telegram token clear failed.", ex);
            return false;
        }
    }

    private void Report(string message, Exception exception)
    {
        try
        {
            _diagnostic?.Invoke(message, exception);
        }
        catch
        {
            // Token diagnostics must never affect app behavior.
        }
    }
}
