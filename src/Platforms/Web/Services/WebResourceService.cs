using System.Net.Http;
using System.Threading.Tasks;

namespace Ludots.Web.Services
{
    public interface IResourceService
    {
        Task<string> LoadStringAsync(string path);
        Task<byte[]> LoadBytesAsync(string path);
    }

    public class WebResourceService : IResourceService
    {
        private readonly HttpClient _httpClient;

        public WebResourceService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<string> LoadStringAsync(string path)
        {
            return await _httpClient.GetStringAsync(path);
        }

        public async Task<byte[]> LoadBytesAsync(string path)
        {
            return await _httpClient.GetByteArrayAsync(path);
        }
    }
}
