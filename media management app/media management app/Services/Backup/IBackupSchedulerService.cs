namespace media_management_app.Services.Backup;

public interface IBackupSchedulerService : IDisposable
{
    void Start();
}
