namespace ProyectAPI.Services
{
    // Reconstruye el ranking de los torneos ya jugados en la web la primera vez
    // que arranca esta versión. Corre en segundo plano para no frenar el inicio.
    public sealed class RankingBackfillService : BackgroundService
    {
        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(5);
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RankingBackfillService> _logger;

        public RankingBackfillService(
            IServiceScopeFactory scopeFactory,
            ILogger<RankingBackfillService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(InitialDelay, stoppingToken);

                using var scope = _scopeFactory.CreateScope();
                var rankingService = scope.ServiceProvider.GetRequiredService<RankingService>();
                var processed = await rankingService.BackfillAsync(stoppingToken);

                if (processed > 0)
                {
                    _logger.LogInformation(
                        "Ranking: se reconstruyeron {Count} torneo(s) existentes.",
                        processed);
                }
            }
            catch (OperationCanceledException)
            {
                // La aplicación se está cerrando.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ranking: falló el backfill inicial.");
            }
        }
    }
}
