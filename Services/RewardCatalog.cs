using ProyectAPI.Models;
namespace ProyectAPI.Services;

// Versioned application configuration. Changes require deployment and cannot be submitted by a client.
public sealed class RewardCatalog
{
    private readonly IReadOnlyDictionary<string, RewardProduct> _products;
    public RewardCatalog() : this(DefaultProducts()) { }
    public RewardCatalog(IEnumerable<RewardProduct> products) => _products = products.ToDictionary(x => x.Id, StringComparer.Ordinal);
    public IReadOnlyList<RewardProduct> Products() => _products.Values.OrderBy(x => x.Platform).ThenBy(x => x.FaceValue).ToArray();
    public RewardProduct? Find(string id) => _products.GetValueOrDefault(id);
    private static RewardProduct Card(string id, string platform, string name, int usd, string credit, string compatibility, string source) =>
        new(id, platform, name, "US", "USD", usd, checked(usd * 2000L), credit, true, compatibility, source);
    private static IEnumerable<RewardProduct> DefaultProducts() => new[]
    {
        Card("steam-usd-10","Steam","Steam Wallet gift card",10,"PlatformBalance","US USD Steam Wallet; wallet currency restrictions apply.","https://www.gamestop.com/products/valve-steam-wallet-card-10/11109954.html"),
        Card("steam-usd-20","Steam","Steam Wallet gift card",20,"PlatformBalance","US USD Steam Wallet; wallet currency restrictions apply.","https://www.bestbuy.com/product/valve-steam-wallet-20-gift-card/J33V32XTKG"),
        Card("epic-usd-15","Epic","Fortnite Gift Card / Epic account balance",15,"PlatformBalance","US USD Epic account balance. Uses Epic payment system; not a V-Bucks card.","https://www.fortnite.com/news/fortnite-gift-cards-a-new-way-to-buy-v-bucks-games-and-more?lang=en-US"),
        Card("ea-usd-25","EA","EA Wallet gift card",25,"PlatformBalance","US resident and US EA account; EA Wallet for PC purchases.","https://www.bestbuy.com/product/electronic-arts-ea-play-25-digital/J33V3286RR/sku/6454208"),
        Card("riot-rp-usd-10","Riot","League of Legends RP gift card",10,"RP","US purchase / NA server, PC. Face value USD; RP granted varies by region/rules.","https://support.riotgames.com/en-us/riot/events/prepaid-gift-cards"),
        Card("riot-vp-usd-10","Riot","VALORANT Points gift card",10,"ValorantPoints","US purchase / NA server, PC; VALORANT only. Face value USD; no fixed VP amount promised.","https://www.target.com/p/-/A-93306184"),
        Card("playstation-usd-10","PlayStation","PlayStation Store gift card",10,"PlatformBalance","US PlayStation account; digital PlayStation Store purchases.","https://www.bestbuy.com/product/sony-10-playstation-store-gift-card-digital/6414390"),
        Card("nintendo-usd-10","Nintendo","Nintendo eShop gift card",10,"PlatformBalance","US Nintendo Account / Nintendo eShop.","https://www.target.com/p/-/A-93562336"),
        Card("xbox-usd-10","Xbox","Xbox Digital Gift Card",10,"PlatformBalance","US Microsoft account and USD purchases; Microsoft Store and Xbox.","https://www.microsoft.com/en-us/p/xbox-gift-card-digital-code/cfq7ttc0k63h/002s")
    };
}
