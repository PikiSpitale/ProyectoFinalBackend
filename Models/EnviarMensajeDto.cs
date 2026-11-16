namespace ProyectoFinalBackend.Models
{
    public class EnviarMensajeDto
    {
        public string ReceiverId { get; set; } = string.Empty;
        public string Contenido { get; set; } = string.Empty;
    }
}