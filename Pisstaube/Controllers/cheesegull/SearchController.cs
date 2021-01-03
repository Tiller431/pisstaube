using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using osu.Game.Beatmaps;
using Pisstaube.Allocation;
using Pisstaube.Database.Models;
using Pisstaube.Engine;
using Pisstaube.Utils;
using StatsdClient;

namespace Pisstaube.Controllers.cheesegull
{
    [Route("api/cheesegull/[controller]")]
    [ApiController]
    public class SearchController : ControllerBase
    {
        private readonly IBeatmapSearchEngineProvider _searchEngine;
        private readonly Cache _cache;

        public SearchController(IBeatmapSearchEngineProvider searchEngine, Cache cache)
        {
            _searchEngine = searchEngine;
            _cache = cache;
        }

        private bool GetTryFromQuery<T>(IEnumerable<string> keys, T def, out T val)
        {
            var rString = string.Empty;

            foreach (var k in keys)
            {
                if (!Request.Query.ContainsKey(k)) continue;

                Request.Query.TryGetValue(k, out var x);
                rString += x.FirstOrDefault();
            }

            if (string.IsNullOrEmpty(rString))
            {
                val = def;
                return false;
            }

            var converter = TypeDescriptor.GetConverter(typeof(T));
            if (!converter.IsValid(rString))
            {
                val = def;
                return false;
            }

            val = (T) converter.ConvertFromString(rString);
            return true;
        }

        // GET /api/cheesegull/search
        [HttpGet]
        public ActionResult<string> Get()
        {
            DogStatsd.Increment("beatmap.searches");
            if (!GlobalConfig.EnableSearch)
                return Unauthorized("Searches are currently Disabled!");

            var raw = Request.Query.ContainsKey("raw");
            var ruri = Request.Query.ContainsKey("ruri");

            GetTryFromQuery(new[] {"query", "q"}, string.Empty, out var query);
            GetTryFromQuery(new[] {"amount", "a"}, 100, out var amount);
            GetTryFromQuery(new[] {"offset", "o"}, 0, out var offset);
            GetTryFromQuery(new[] {"page", "p"}, 0, out var page);
            GetTryFromQuery(new[] {"mode", "m"}, (int) PlayMode.All, out var mode);
            GetTryFromQuery(new[] {"status", "r"}, null, out int? r);
            
            if (ruri && r.HasValue) {
                r = r switch {
                    4 => (int) BeatmapSetOnlineStatus.None,
                    0 => (int) BeatmapSetOnlineStatus.Ranked,
                    7 => (int) BeatmapSetOnlineStatus.Ranked,
                    8 => (int) BeatmapSetOnlineStatus.Loved,
                    3 => (int) BeatmapSetOnlineStatus.Qualified,
                    2 => (int) BeatmapSetOnlineStatus.Pending,
                    5 => (int) BeatmapSetOnlineStatus.Graveyard,
                    
                    _ => (int) BeatmapSetOnlineStatus.Graveyard,
                };
            }

            BeatmapSetOnlineStatus? status = null;
            if (r != null)
                status = (BeatmapSetOnlineStatus) r.Value;

            offset += 100 * page;

            if (query.ToLower().Equals("newest") ||
                query.ToLower().Equals("top rated") || // TODO: Implementing this
                query.ToLower().Equals("most played")) // and this
                query = "";

            var ha = query + amount + offset + status + mode + page + raw;

            if (_cache.TryGet(ha, out string ca))
                return ca;

            var result = _searchEngine.Search(query, amount, offset, status, (PlayMode) mode);
            
            var beatmapSets = result as BeatmapSet[] ?? result.ToArray();
            if (beatmapSets.Length == 0) result = null; // Cheesegull logic ^^,

            if (!raw)
            {
                ca = JsonUtil.Serialize(beatmapSets);
            }
            else
            {
                if (result == null)
                {
                    ca = "-1\nNo Beatmaps were found!";

                    goto Return;
                }

                ca = string.Empty;

                ca += beatmapSets.Length >= 100 ? "101" : beatmapSets.Length.ToString();

                ca += "\n";

                foreach (var set in beatmapSets) ca += set.ToDirect();
            }

            Return:
            _cache.Set(ha, ca, TimeSpan.FromMinutes(10));
            return ca;
        }
    }
}
