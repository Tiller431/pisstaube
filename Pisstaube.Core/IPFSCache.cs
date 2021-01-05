using System;
using System.Threading.Tasks;
using Ipfs.Http;
using osu.Framework.Logging;
using osu.Framework.Platform; 

namespace Pisstaube.Allocation
{
    public class IpfsCache
    {
        private readonly Storage _storage;
        private readonly IpfsClient _ipfs = new(Environment.GetEnvironmentVariable("IPFS_HOST"));
 
        public IpfsCache(Storage storage)
        {
            _storage = storage;
        }
        
        public async Task<string> CacheFile(string path)
        {
            try
            {
                var fileInfo = await _ipfs.FileSystem.AddFileAsync(_storage.GetFullPath(path));
             
                return fileInfo?.Id.Hash.ToBase58();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to upload file to interplanetary FileSystem (IPFS)");
                return "";
            }
        } 
    } 
}
