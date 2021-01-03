using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pisstaube.Database;
using Pisstaube.Database.Models;
using Pisstaube.Utils;
using StatsdClient;

namespace Pisstaube.Controllers.cheesegull
{
    [Route("api/cheesegull")]
    [ApiController]
    public class BeatmapController : ControllerBase
    {
        private readonly PisstaubeDbContext _dbContext;
        private object _dbContextLock = new();

        public BeatmapController(PisstaubeDbContext dbContext) => _dbContext = dbContext;

        [HttpGet]
        public ActionResult<List<BeatmapSet>> Get() => null;

        // GET /api/cheesegull/b/:BeatmapId
        [HttpGet("b/{beatmapId:int}")]
        public ActionResult<string> GetBeatmap(int beatmapId)
        {
            DogStatsd.Increment("beatmap.request");

            lock (_dbContextLock) {
                var raw = Request.Query.ContainsKey("raw");
                if (!raw)
                    return JsonUtil.Serialize(_dbContext.Beatmaps.FirstOrDefault(cb => cb.BeatmapId == beatmapId));

                // TODO: tried to make both in one query, results in crash. have to take a closer look in the near future.
                var beatmap = _dbContext.Beatmaps.FirstOrDefault(bm => bm.BeatmapId == beatmapId);
                var set = _dbContext.BeatmapSet.FirstOrDefault(s => s.SetId == beatmap.ParentSetId);
                
                if (set == null)
                    return "0";
                
                return $"{set.SetId}.osz|" +
                       $"{set.Artist}|" +
                       $"{set.Title}|" +
                       $"{set.Creator}|" +
                       $"{(int) set.RankedStatus}|" +
                       "10.00|" +
                       $"{set.LastUpdate}|" +
                       $"{set.SetId}|" +
                       $"{set.SetId}|" +
                       $"{Convert.ToInt32(set.HasVideo)}|" +
                       "0|" +
                       "1234|" +
                       $"{Convert.ToInt32(set.HasVideo) * 4321}\r\n";
            }
        }

        // GET /api/cheesegull/b/:BeatmapIds
        [HttpGet("b/{beatmapIds}")]
        public ActionResult<string> GetBeatmap(string beatmapIds)
        {
            DogStatsd.Increment("beatmap.request");
            
            var raw = Request.Query.ContainsKey("raw");
            if (raw)
                return "raw is not supported!";

            try
            {
                var bms = beatmapIds.Split(";");
                var bmIds = Array.ConvertAll(bms, int.Parse);

                lock (_dbContextLock)
                    return JsonUtil.Serialize(
                        _dbContext.Beatmaps
                                .Where(cb => bmIds.Any(x => cb.BeatmapId == x)));
            }
            catch (FormatException)
            {
                return "parameter MUST be an int array! E.G 983680;983692;983896";
            }
        }

        // GET /api/cheesegull/s/:BeatmapSetId
        [HttpGet("s/{beatmapSetId:int}")]
        public async Task<ActionResult<string>> GetSet(int beatmapSetId)
        {
            DogStatsd.Increment("beatmap.set.request");
            
            lock (_dbContextLock)
            {
                var raw = Request.Query.ContainsKey("raw");
                var set =
                    _dbContext.BeatmapSet
                        .Where(s => s.SetId == beatmapSetId)
                        .Include(x => x.ChildrenBeatmaps)
                        .FirstOrDefault();

                if (!raw)
                    return JsonUtil.Serialize(set);

                if (set == null)
                    return "0";

                return $"{set.SetId}.osz|" +
                       $"{set.Artist}|" +
                       $"{set.Title}|" +
                       $"{set.Creator}|" +
                       $"{(int) set.RankedStatus}|" +
                       "10.00|" +
                       $"{set.LastUpdate}|" +
                       $"{set.SetId}|" +
                       $"{set.SetId}|" +
                       $"{Convert.ToInt32(set.HasVideo)}|" +
                       "0|" +
                       "1234|" +
                       $"{Convert.ToInt32(set.HasVideo) * 4321}\r\n";
            }
        }

        // GET /api/cheesegull/s/:BeatmapSetIds
        [HttpGet("s/{beatmapSetIds}")]
        public ActionResult<string> GetSet(string beatmapSetIds)
        {
            DogStatsd.Increment("beatmap.set.request");
            
            var raw = Request.Query.ContainsKey("raw");
            if (raw)
                return "raw is not supported!";

            try
            {
                var bms = beatmapSetIds.Split(";");
                var bmsIds = Array.ConvertAll(bms, int.Parse);

                lock (_dbContextLock)
                {
                    return JsonUtil.Serialize(
                        _dbContext.BeatmapSet.Where(set => bmsIds.Any(s => set.SetId == s))
                            .Include(x => x.ChildrenBeatmaps)
                    );
                }
            }
            catch (FormatException)
            {
                return "parameter MUST be an int array! E.G 1;16";
            }
        }
    }
}