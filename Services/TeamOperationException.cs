namespace ProyectAPI.Services;

public sealed class TeamOperationException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
