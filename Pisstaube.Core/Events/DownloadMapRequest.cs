namespace Pisstaube.Core.Events
{
    public class DownloadMapRequest
    {
        public int SetId { get; set; }
        public bool NoVideo { get; set; }
    }
    
    public class DownloadMapResponse
    {
        public string File { get; set; }
        public string IpfsHash { get; set; }
    }
}