using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;
using System.Web;
using System.Linq;
using System.Web;

namespace Analyst.Utils
{
    class Analyst
    {
        private readonly HttpClient _httpClient;

        public Analyst(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<int?> ParseMinPriceAsync(string productQuery)
        {
            string query = AnalystUtils.SimplifyQuery(productQuery);
            string url = $"https://halykmarket.kz/search?r46_search_query={HttpUtility.UrlEncode(query)}";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;

            var html = await response.Content.ReadAsStringAsync();

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var priceNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'h-product-card__price')]");
            if (priceNodes == null) return null;

            var prices = priceNodes
                .Select(node => node.InnerText.Replace("₸", "").Replace(" ", "").Trim())
                .Select(text => int.TryParse(text, out var price) ? price : (int?)null)
                .Where(p => p.HasValue)
                .Select(p => p.Value)
                .ToList();

            return prices.Any() ? prices.Min() : (int?)null;
        }
    }
}
