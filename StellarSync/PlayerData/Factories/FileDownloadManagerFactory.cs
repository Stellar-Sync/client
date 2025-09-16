using StellarSync.FileCache;
using StellarSync.Services.Mediator;
using StellarSync.WebAPI.Files;
using Microsoft.Extensions.Logging;

namespace StellarSync.PlayerData.Factories;

public class FileDownloadManagerFactory
{
    private readonly FileCacheManager _fileCacheManager;
    private readonly FileCompactor _fileCompactor;
    private readonly FileTransferOrchestrator _fileTransferOrchestrator;
    private readonly ILoggerFactory _loggerFactory;
    private readonly StellarMediator _stellarMediator;

    public FileDownloadManagerFactory(ILoggerFactory loggerFactory, StellarMediator stellarMediator, FileTransferOrchestrator fileTransferOrchestrator,
        FileCacheManager fileCacheManager, FileCompactor fileCompactor)
    {
        _loggerFactory = loggerFactory;
        _stellarMediator = stellarMediator;
        _fileTransferOrchestrator = fileTransferOrchestrator;
        _fileCacheManager = fileCacheManager;
        _fileCompactor = fileCompactor;
    }

    public FileDownloadManager Create()
    {
        return new FileDownloadManager(_loggerFactory.CreateLogger<FileDownloadManager>(), _stellarMediator, _fileTransferOrchestrator, _fileCacheManager, _fileCompactor);
    }
}