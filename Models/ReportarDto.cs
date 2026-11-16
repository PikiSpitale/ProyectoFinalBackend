namespace ProyectoFinalBackend.Models
{
    public class ReportarDto
    {
        public string TargetId { get; set; } = string.Empty;
        public string? MessageId { get; set; }
        public string Reason { get; set; } = "Sin especificar";
    }
}