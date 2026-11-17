namespace ProyectAPI.Services;
public sealed class WalletOperationException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
