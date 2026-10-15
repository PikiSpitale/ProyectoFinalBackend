namespace ProyectAPI.Services
{
    // Revisa cada minuto si hay torneos que ya llegaron a su fecha de inicio
    // y genera sus llaves automáticamente.
    public sealed class BracketSchedulerService : BackgroundService
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BracketSchedulerService> _logger;

        public BracketSchedulerService(IServiceScopeFactory scopeFactory, ILogger<BracketSchedulerService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(CheckInterval);

            try
            {
                do
                {
                    await RunOnceAsync(stoppingToken);
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException)
            {
                // La aplicación se está cerrando
            }
        }

        private async Task RunOnceAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Los servicios son scoped, así que se crea un scope por cada ejecución
                using var scope = _scopeFactory.CreateScope();
                var tournamentService = scope.ServiceProvider.GetRequiredService<ITournamentService>();

                var generated = await tournamentService.GenerateDueBracketsAsync(cancellationToken);
                if (generated > 0)
                {
                    _logger.LogInformation("Se generaron las llaves de {Count} torneo(s).", generated);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar las llaves automáticas de los torneos.");
            }
        }
    }
}