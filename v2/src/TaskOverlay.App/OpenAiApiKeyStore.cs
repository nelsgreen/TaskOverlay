using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TaskOverlay.App;

public sealed class OpenAiApiKeyStore
{
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("TaskOverlay.V2.MeetingAssistant.OpenAI.ApiKey.v1");

    private readonly Action<string, Exception?>? _diagnostic;

    public OpenAiApiKeyStore(
        string stateDirectory,
        Action<string, Exception?>? diagnostic = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        StateDirectory = stateDirectory;
        _diagnostic = diagnostic;
    }

    public string StateDirectory { get; }
    public string KeyPath => Path.Combine(StateDirectory, "openAiApiKey.dpapi");

    public bool HasKey() => File.Exists(KeyPath);

    public bool SaveKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        byte[]? plaintext = null;
        try
        {
            Directory.CreateDirectory(StateDirectory);
            plaintext = Encoding.UTF8.GetBytes(apiKey.Trim());
            var protectedBytes = ProtectedData.Protect(
                plaintext,
                Entropy,
                DataProtectionScope.CurrentUser);
            File.WriteAllBytes(KeyPath, protectedBytes);
            return true;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            CryptographicException)
        {
            Report("OpenAI API key save failed.", ex);
            return false;
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    public string? LoadKey()
    {
        byte[]? plaintext = null;
        try
        {
            if (!File.Exists(KeyPath))
            {
                return null;
            }

            plaintext = ProtectedData.Unprotect(
                File.ReadAllBytes(KeyPath),
                Entropy,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            CryptographicException)
        {
            Report("OpenAI API key load failed.", ex);
            return null;
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    public bool ClearKey()
    {
        try
        {
            if (File.Exists(KeyPath))
            {
                File.Delete(KeyPath);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report("OpenAI API key clear failed.", ex);
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
            // Secret-storage diagnostics never include the secret value.
        }
    }
}
