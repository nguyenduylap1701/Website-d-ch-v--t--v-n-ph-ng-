using Microsoft.EntityFrameworkCore;
using TroTot.Web.Data;
using TroTot.Web.Models;

namespace TroTot.Web.Services;

public class DepositCleanupService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DepositCleanupService> _logger;

    public DepositCleanupService(IServiceProvider services, ILogger<DepositCleanupService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Deposit Cleanup Service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var scope = _services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    
                    var now = DateTime.UtcNow;
                    var expiredDeposits = await db.RoomDeposits
                        .Include(d => d.Room)
                        .Where(d => d.Status == "Pending" && d.ExpiresAt <= now)
                        .ToListAsync(stoppingToken);

                    if (expiredDeposits.Any())
                    {
                        foreach (var dep in expiredDeposits)
                        {
                            _logger.LogInformation($"Cleaning up expired deposit {dep.Id} for room {dep.RoomId}");
                            dep.Status = "Expired";
                            if (dep.Room?.AvailabilityStatus == RoomAvailability.PendingDeposit)
                            {
                                dep.Room.AvailabilityStatus = RoomAvailability.Available;
                            }
                        }
                        await db.SaveChangesAsync(stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while cleaning up deposits.");
            }

            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }

        _logger.LogInformation("Deposit Cleanup Service is stopping.");
    }
}
