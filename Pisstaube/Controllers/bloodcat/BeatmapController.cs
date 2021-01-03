using System;
using System.IO;
using System.Linq;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.AspNetCore.Mvc;
using osu.Framework.Platform;
using osu.Game.IO;
using osu.Game.Online.API;
using osu.Game.Scoring.Legacy;
using Pisstaube.CacheDb;
using Pisstaube.Database;
using Pisstaube.Online;
using Pisstaube.Utils;

namespace Pisstaube.Controllers.bloodcat
{
    [Route("api/bloodcat/")]
    [ApiController]
    public class BeatmapController : ControllerBase
    {
        private readonly Storage _fileStorage;
        private readonly PisstaubeDbContext _dbContext;
        private readonly object _dbContextLock = new();
        private readonly PisstaubeCacheDbContextFactory _cache;
        private readonly BeatmapDownloader _downloader;
        private readonly SetDownloader _setDownloader;

        public BeatmapController(IAPIProvider apiProvider,
            Storage storage,
            PisstaubeCacheDbContextFactory cache,
            BeatmapDownloader downloader,
            SetDownloader setDownloader,
            PisstaubeDbContext dbContext,
            FileStore fileStore)
        {
            _cache = cache;
            _downloader = downloader;
            _setDownloader = setDownloader;
            _dbContext = dbContext;
            _fileStorage = storage.GetStorageForDirectory("files");
        }
        
        [HttpGet("a/{beatmapId}")]
        public ActionResult GetMapAudio(int beatmapId)
        { 
            int setId;
            string beatmapFileName;
            lock (_dbContextLock)
            {
                var bm = _dbContext
                    .Beatmaps
                    .FirstOrDefault(bm => bm.BeatmapId == beatmapId);
                
                setId = bm?.ParentSetId ?? -1;
                beatmapFileName = bm?.File;
            }
            
            var hash = _cache.Get()
                .CacheBeatmaps.Where(bm => bm.BeatmapId == beatmapId)
                .Select(bm => bm.Hash)
                .FirstOrDefault();


            if (setId == -1)
                return NotFound("Beatmap doesn't exists!");

            osu.Game.IO.FileInfo info = null;
            // Make sure that our file exists
            if (beatmapFileName == null)
            {
                lock (_dbContextLock)
                {
                    foreach (var map in _dbContext.Beatmaps.Where(bm => bm.File == beatmapFileName))
                    {
                        var (fileInfo, pFileMd5) = _downloader.Download(map);

                        map.FileMd5 = pFileMd5;
                        info = fileInfo;
                    }

                    if (info == null) {
                        var (fileInfo, _) = _downloader.Download(beatmapId.ToString());

                        info = fileInfo;
                    }
                }
            }
            else
                info = new osu.Game.IO.FileInfo {Hash = hash};


            if (info == null)
                return NotFound("Beatmap not Found!");
            
            var beatmapExtractor = new BeatmapExtractor(setId);
            
            return Ok(beatmapExtractor.GrabAudio(_fileStorage.GetStream(info.StoragePath)));
        }

        [HttpGet("i/{beatmapId}")]
        public ActionResult GetMapThumbnail(int beatmapId)
        {
            int setId;
            string beatmapFileName;
            lock (_dbContextLock)
            {
                var bm = _dbContext
                    .Beatmaps
                    .FirstOrDefault(bm => bm.BeatmapId == beatmapId);
                
                setId = bm?.ParentSetId ?? -1;
                beatmapFileName = bm?.File;
            }
            
            var hash = _cache.Get()
                .CacheBeatmaps.Where(bm => bm.BeatmapId == beatmapId)
                .Select(bm => bm.Hash)
                .FirstOrDefault();


            if (setId == -1)
                return NotFound("Beatmap doesn't exists!");

            osu.Game.IO.FileInfo info = null;
            // Make sure that our file exists
            if (beatmapFileName == null)
            {
                lock (_dbContextLock)
                {
                    foreach (var map in _dbContext.Beatmaps.Where(bm => bm.File == beatmapFileName))
                    {
                        var (fileInfo, pFileMd5) = _downloader.Download(map);

                        map.FileMd5 = pFileMd5;
                        info = fileInfo;
                    }

                    if (info == null) {
                        var (fileInfo, _) = _downloader.Download(beatmapId.ToString());

                        info = fileInfo;
                    }
                }
            }
            else
                info = new osu.Game.IO.FileInfo {Hash = hash};


            if (info == null)
                return NotFound("Beatmap not Found!");
            
            var beatmapExtractor = new BeatmapExtractor(setId);
            
            return Ok(beatmapExtractor.GrabThumbnail(_fileStorage.GetStream(info.StoragePath)));
        }
        
        [HttpPost("archive")]
        public ActionResult PostSetsArchive()
        {
            var memory = new MemoryStream();
            var archive = new ZipOutputStream(memory);
            
            archive.SetLevel(0);

            var setIds = Request.Form["set_ids"];
            foreach (var setIdS in setIds)
            {
                if (!int.TryParse(setIdS, out var setId))
                    continue;
                
                SetDownloader.DownloadMapResponse r;
                try
                {
                    r = _setDownloader.DownloadMap(setId);
                }
                catch (UnauthorizedAccessException)
                {
                    return StatusCode(503, "Osu! API is not available.");
                }
                catch (LegacyScoreDecoder.BeatmapNotFoundException)
                {
                    return StatusCode(404, "Beatmap not Found!");
                }
                catch (IOException)
                {
                    return StatusCode(500, "Storage Full!");
                }
                catch (NotSupportedException)
                {
                    return StatusCode(404, "Beatmap got DMCA'd!");
                }

                var entry = new ZipEntry(setId + ".osz")
                {
                    DateTime = DateTime.UtcNow,
                    Size = r.FileStream.Length
                };

                archive.PutNextEntry(entry);
                r.FileStream.CopyTo(archive);
                archive.CloseEntry();
            }
            
            archive.Finish();
            memory.Position = 0;
            
            // TODO: Cache the result

            return File(memory, "application/zip", $"osu!BeatmapMirror-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.zip");
        }
    }
}