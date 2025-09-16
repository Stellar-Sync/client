using StellarSync.API.Data;
using StellarSync.FileCache;
using StellarSync.Services.CharaData.Models;

namespace StellarSync.Services.CharaData;

public sealed class StellarCharaFileDataFactory
{
    private readonly FileCacheManager _fileCacheManager;

    public StellarCharaFileDataFactory(FileCacheManager fileCacheManager)
    {
        _fileCacheManager = fileCacheManager;
    }

    public StellarCharaFileData Create(string description, CharacterData characterCacheDto)
    {
        return new StellarCharaFileData(_fileCacheManager, description, characterCacheDto);
    }
}