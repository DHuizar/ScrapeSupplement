using System.Globalization;
using System.Web;
using CsvHelper;
using CsvHelper.Configuration;
using HtmlAgilityPack;
using ScrapeSupplement;

const int querySize = 100;

const string usageMessage = """

        Usage: dotnet run -- <product> <sortType>

        product:
        c - creatine
        p - protein powder
        w - pre-workout

        sortType:
        ps - price per serving
        pr - price
        s - serving quantity

    """;

var flagToProductPath = new Dictionary<string, string>
{
    ["c"] = "muscle-builders/creatine/",
    ["p"] = "protein/protein-powder/",
    ["w"] = "performance/pre-workout-supplements/",
};

var flagToSort = new Dictionary<string, Comparison<Item>>
{
    ["ps"] = (Item x, Item y) => (x.Price / x.Servings).CompareTo(y.Price / y.Servings),
    ["pr"] = (Item x, Item y) => x.Price.CompareTo(y.Price),
    ["s"] = (Item x, Item y) => x.Servings.CompareTo(y.Servings),
};

if (args.Length < 2)
    throw new Exception(usageMessage);

if (!flagToProductPath.TryGetValue(args[0], out var path))
    throw new Exception(usageMessage);

if (!flagToSort.TryGetValue(args[1], out var sort))
    throw new Exception(usageMessage);

var uriBuilder = new UriBuilder($"https://www.gnc.com/{path}");

var query = HttpUtility.ParseQueryString(uriBuilder.Query);
query["start"] = "0";
query["sz"] = $"{querySize}";

uriBuilder.Query = query.ToString();
var uri = uriBuilder.ToString();
Console.WriteLine(uri);

var web = new HtmlWeb
{
    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"
};

var htmlDoc = web.Load(uri);
var priceNodes = htmlDoc.DocumentNode.QuerySelectorAll(".product-standard-price");
var servingNodes = htmlDoc.DocumentNode.QuerySelectorAll(".tile-product-name");

var list = new List<Item>();

// TODO encapsulate
for (int i = 0; i < priceNodes.Count; i++)
{
    var trimmedPriceStr = priceNodes[i].InnerText.Trim()[1..];
    var price = double.Parse(trimmedPriceStr);

    // GNC
    // productLine - someProduct (42 servings) -> ["GNC", "productLine - someProduct (42 servings)"]
    var lines = HttpUtility.HtmlDecode(servingNodes[i].InnerText.Trim()).Split("\n");

    // "GNC"
    var brand = lines[0];

    // "productLine - someProduct (42 servings)"
    var secondLine = lines[1];

    var parenIndex = secondLine.IndexOf('(') + 1;

    // "productLine - someProduct (42 servings)" -> "productLine - someProduct"
    var name = secondLine[..(parenIndex - 1)];

    // "productLine - someProduct (42 servings)" -> "42 servings)" -> 42
    int servingAmount = int.Parse(secondLine[parenIndex..].Split(" ")[0]);

    list.Add(new Item { Brand = brand, Name = name, Price = price, Servings = servingAmount });

}

list.Sort(sort);

var csvName = $"{path.Split("/")[1]}-{args[1]}.csv";
using (var writer = new StreamWriter(csvName))
using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
{
    csv.Context.RegisterClassMap<ItemMap>();
    csv.WriteRecords(list);
}

namespace ScrapeSupplement
{
    public class Item
    {
        public required string Brand { get; set; }
        public required string Name { get; set; }
        public double Price { get; set; }
        public int Servings { get; set; }
        public static double PricePerServing(double price, int serving) => price / serving;
        public override string ToString() => $"""
        (
            Brand:{Brand} 
            Name:{Name} 
            Price:{Price} 
            Servings:{Servings} 
            Price per Serving:{Price / Servings:0.##}
        )
        """;
    }

    public class ItemMap : ClassMap<Item>
    {
        public ItemMap()
        {
            Map(i => i.Brand).Index(0).Name("brand");
            Map(i => i.Name).Index(1).Name("name");
            Map(i => i.Price).Index(2).Name("price");
            Map(i => i.Servings).Index(3).Name("serving");
        }
    }
}