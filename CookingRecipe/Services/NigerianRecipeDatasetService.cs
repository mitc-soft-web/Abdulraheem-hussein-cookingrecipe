using System.Text.Json;
using System.Text.RegularExpressions;
using CookingRecipe.Entities;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace CookingRecipe.Services
{
    public interface INigerianRecipeDatasetService
    {
        List<Recipe> GetAll();
        bool IsNigerianQuery(IEnumerable<string> terms, string rawQuery);
        List<Recipe> Search(IEnumerable<string> terms, int max = 10);
        List<Recipe> SearchByIngredientsPriority(IEnumerable<string> terms, int max = 10);
        Recipe? GetById(int id);
    }

    public class NigerianRecipeDatasetService : INigerianRecipeDatasetService
    {
        private const string DefaultFoodImage = "https://images.unsplash.com/photo-1498837167922-ddd27525d352?auto=format&fit=crop&w=1200&q=80";
        private const int MinDatasetSize = 0;
        private static readonly Regex IrrelevantTitlePattern = new(
            @"^(festival|festeval|village pot|sunday(?:\s+special)?|signature|smoky)\b|\b(?:rice|yam|plantain|beans|spaghetti|potato)\s+pepper\s+mix\b|\bofensala\s+catfish\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Dictionary<string, string> SpecificDishImages = new(StringComparer.OrdinalIgnoreCase)
        {
            ["jollof rice"] = "https://commons.wikimedia.org/wiki/Special:FilePath/JOLLOF%20RICE.JPG",
            ["fried rice"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Fried%20Rice%2020th%20April.jpg",
            ["coconut rice"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Fried%20Rice%2C%20Jolof%20Rice%20with%20Plantain%20and%20Chicken.jpg",
            ["moi moi"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Moi%20moi%20In%20Northern%20Nigeria%206.jpg",
            ["egusi soup"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Egusi%20soup%20with%20pounded%20yam%20and%20assorted%20meats.jpg",
            ["ogbono soup"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Ogbono%20Soup.jpg",
            ["okra soup"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Okra%20Soup.jpg",
            ["afang soup"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Freshly%20Cooked%20Afang%20Soup.jpg",
            ["edikang ikong"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Edikang%20ikong.jpg",
            ["banga soup"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Banga%20Soup%20(Freshly%20Cooked).jpg",
            ["pepper soup"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Catfish%20Pepper%20Soup.jpg",
            ["catfish pepper soup"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Catfish%20Pepper%20Soup.jpg",
            ["white soup"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Pepper%20soup.jpg",
            ["yam porridge"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Yam%20porridge%20or%20%C3%80s%C3%A1r%C3%B3.jpg",
            ["plantain porridge"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Plantain%20porridge%20(Nigerian%20cuisine).jpg",
            ["beans and dodo"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Ewa%20Agoyin%20and%20Plantain.jpg",
            ["ewa agoyin"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Ewa%20Agoyin%20and%20Plantain.jpg",
            ["akara"] = "https://commons.wikimedia.org/wiki/Special:FilePath/AKARA.jpg",
            ["suya"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Steak%20Suya.jpg",
            ["asun"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Steak%20Suya.jpg",
            ["nkwobi"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Suya.jpg",
            ["isi ewu"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Suya.jpg",
            ["abacha"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Abacha%20(African%20Salad).jpg",
            ["gbegiri"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Amala%20gbegiri%20ewedu.jpg",
            ["ewedu"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Ewedu%20Soup.jpg",
            ["miyan kuka"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Tuwo%20shincafa.jpg",
            ["miyan taushe"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Tuwo.jpg",
            ["tuwo shinkafa"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Tuwo%20shincafa.jpg",
            ["masa"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Masa%20(Traditional%20Nigerian%20Food).jpg",
            ["meat pie"] = "https://commons.wikimedia.org/wiki/Special:FilePath/A%20Northern%20Nigeria%20snacks%20called%20meat%20pie%2025.jpg",
            ["fish stew"] = "https://commons.wikimedia.org/wiki/Special:FilePath/Catfish%20pepper%20soup%20with%20vegetables.jpg",
            ["rice and stew"] = "https://commons.wikimedia.org/wiki/Special:FilePath/JOLLOF%20RICE.JPG"
        };

        private static readonly string[] NigerianKeywords =
        [
            "jollof", "ofada", "egusi", "efo", "edikang", "afang", "okra", "ogbono", "banga", "fufu", "amala", "semo", "eba", "garri", "pounded yam",
            "moi moi", "moimoi", "akara", "suya", "kilishi", "asun", "pepper soup", "nkwobi", "isi ewu", "yam porridge", "beans porridge", "ewa agoyin",
            "nigerian", "naija", "gbegiri", "tuwo", "shinkafa", "masa", "abacha", "catfish", "stew", "white soup", "nsala", "bitterleaf", "miyan", "ewedu"
        ];

        private readonly List<Recipe> _recipes;
        private readonly Dictionary<string, string[]> _ingredientAliases;
        private readonly Dictionary<string, string> _reverseAliasLookup;

        // Parameterless constructor keeps existing tests simple.
        public NigerianRecipeDatasetService()
            : this(
                new ConfigurationBuilder().Build(),
                new LocalHostEnvironment(Directory.GetCurrentDirectory()),
                NullLogger<NigerianRecipeDatasetService>.Instance)
        {
        }

        public NigerianRecipeDatasetService(IConfiguration config, IWebHostEnvironment environment, ILogger<NigerianRecipeDatasetService> logger)
        {
            _ingredientAliases = BuildIngredientAliases();
            _reverseAliasLookup = BuildReverseAliasLookup(_ingredientAliases);

            var loaded = LoadDataset(config, environment, logger);
            loaded = EnsureMinimumDatasetSize(loaded, MinDatasetSize);
            _recipes = loaded;

            logger.LogInformation("Nigerian dataset loaded with {Count} recipes.", _recipes.Count);
        }

        public List<Recipe> GetAll()
        {
            return _recipes.Select(CloneRecipe).ToList();
        }

        public bool IsNigerianQuery(IEnumerable<string> terms, string rawQuery)
        {
            var combined = string.Join(' ', terms).ToLowerInvariant();
            var text = $"{rawQuery} {combined}".ToLowerInvariant();
            return NigerianKeywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
        }

        public List<Recipe> Search(IEnumerable<string> terms, int max = 10)
        {
            var normalizedTerms = NormalizeTerms(terms);
            if (normalizedTerms.Length == 0)
            {
                return _recipes.Take(Math.Clamp(max, 1, 500)).Select(CloneRecipe).ToList();
            }

            return _recipes
                .Where(r => normalizedTerms.All(term => RecipeContainsRequestedIngredient(r, term)))
                .Take(Math.Clamp(max, 1, 500))
                .Select(CloneRecipe)
                .ToList();
        }

        public List<Recipe> SearchByIngredientsPriority(IEnumerable<string> terms, int max = 10)
        {
            var normalizedTerms = NormalizeTerms(terms);
            if (normalizedTerms.Length == 0)
            {
                return Search(Array.Empty<string>(), max);
            }

            var requiredMatches = normalizedTerms.Length switch
            {
                <= 1 => 1,
                2 => 2,
                _ => normalizedTerms.Length - 1
            };

            var ranked = _recipes
                .Select(recipe =>
                {
                    var matchCount = normalizedTerms.Count(term => RecipeContainsRequestedIngredient(recipe, term));
                    return new
                    {
                        Recipe = recipe,
                        MatchCount = matchCount,
                        Score = ScoreRecipe(recipe, normalizedTerms, matchCount)
                    };
                })
                .Where(x => x.MatchCount >= requiredMatches)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.MatchCount)
                .ThenBy(x => x.Recipe.Title, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(max, 1, 500))
                .Select(x => CloneRecipe(x.Recipe))
                .ToList();

            return ranked;
        }

        public Recipe? GetById(int id)
        {
            var recipe = _recipes.FirstOrDefault(r => r.Id == id);
            return recipe == null ? null : CloneRecipe(recipe);
        }

        private static string[] NormalizeTerms(IEnumerable<string> terms)
        {
            return terms
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToLowerInvariant())
                .Distinct()
                .ToArray();
        }

        private int ScoreRecipe(Recipe recipe, string[] normalizedTerms, int matchCount)
        {
            var score = matchCount * 100;
            if (matchCount == normalizedTerms.Length)
            {
                score += 60;
            }

            var title = recipe.Title.ToLowerInvariant();
            var hasRice = normalizedTerms.Contains("rice");
            var hasTomato = normalizedTerms.Contains("tomato");
            var hasBeans = normalizedTerms.Contains("beans") || normalizedTerms.Contains("bean");
            var hasMaggi = normalizedTerms.Contains("maggi") || normalizedTerms.Contains("stock cube") || normalizedTerms.Contains("seasoning cube");

            if (hasRice && hasTomato && title.Contains("jollof", StringComparison.OrdinalIgnoreCase))
            {
                score += 120;
            }

            if (hasBeans && hasTomato && hasMaggi && (title.Contains("moi moi", StringComparison.OrdinalIgnoreCase) || title.Contains("ewa", StringComparison.OrdinalIgnoreCase)))
            {
                score += 120;
            }

            if (title.Contains("nigerian", StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
            }

            return score;
        }

        private bool RecipeContainsRequestedIngredient(Recipe recipe, string requestedIngredient)
        {
            var searchCandidates = BuildSearchCandidates(requestedIngredient);
            return searchCandidates.Any(candidate => ContainsTerm(recipe, candidate));
        }

        private IEnumerable<string> BuildSearchCandidates(string requestedIngredient)
        {
            var normalized = requestedIngredient.Trim().ToLowerInvariant();
            var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var v in BuildVariants(normalized))
            {
                variants.Add(v);
            }

            if (_ingredientAliases.TryGetValue(normalized, out var aliases))
            {
                foreach (var alias in aliases)
                {
                    foreach (var v in BuildVariants(alias.ToLowerInvariant()))
                    {
                        variants.Add(v);
                    }
                }
            }

            if (_reverseAliasLookup.TryGetValue(normalized, out var canonical))
            {
                foreach (var v in BuildVariants(canonical))
                {
                    variants.Add(v);
                }
            }

            return variants;
        }

        private static bool ContainsTerm(Recipe recipe, string term)
        {
            if (string.IsNullOrWhiteSpace(term)) return false;

            if (recipe.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return recipe.Ingredients.Any(i =>
                !string.IsNullOrWhiteSpace(i.Ingredient?.Name) &&
                i.Ingredient!.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> BuildVariants(string value)
        {
            var cleaned = value.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return Array.Empty<string>();
            }

            var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { cleaned };

            if (cleaned.EndsWith("es", StringComparison.OrdinalIgnoreCase) && cleaned.Length > 2)
            {
                variants.Add(cleaned[..^2]);
            }

            if (cleaned.EndsWith("s", StringComparison.OrdinalIgnoreCase) && cleaned.Length > 1)
            {
                variants.Add(cleaned[..^1]);
            }

            var hyphenSpace = cleaned.Replace('-', ' ');
            variants.Add(hyphenSpace);

            return variants;
        }

        private static Dictionary<string, string> BuildReverseAliasLookup(Dictionary<string, string[]> aliases)
        {
            var reverse = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (canonical, linkedAliases) in aliases)
            {
                foreach (var alias in linkedAliases)
                {
                    var key = alias.Trim().ToLowerInvariant();
                    if (!reverse.ContainsKey(key))
                    {
                        reverse[key] = canonical;
                    }
                }
            }

            return reverse;
        }

        private static Dictionary<string, string[]> BuildIngredientAliases()
        {
            return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["maggi"] = ["stock cube", "seasoning cube", "bouillon", "bouillon cube"],
                ["stock cube"] = ["maggi", "seasoning cube", "bouillon cube"],
                ["beans"] = ["bean", "black eyed beans", "black-eyed peas", "brown beans"],
                ["pepper"] = ["fresh pepper", "ata rodo", "chili", "scotch bonnet"],
                ["tatashe"] = ["red bell pepper", "bell pepper", "sweet pepper"],
                ["tomato paste"] = ["tomato puree", "tomato sauce", "concentrated tomato"],
                ["fish"] = ["stock fish", "dried fish", "catfish", "mackerel", "titus"],
                ["yam"] = ["white yam", "pounded yam"],
                ["plantain"] = ["ripe plantain", "unripe plantain", "dodo"],
                ["locust beans"] = ["iru", "dawadawa"],
                ["groundnut"] = ["peanut"],
                ["palm oil"] = ["red oil"],
                ["ugu"] = ["pumpkin leaves", "fluted pumpkin"],
                ["egusi"] = ["melon seed", "ground melon"],
                ["scotch bonnet"] = ["ata rodo", "habanero"]
            };
        }

        private static List<Recipe> LoadDataset(IConfiguration config, IWebHostEnvironment environment, ILogger logger)
        {
            var configuredPath = config["NigerianDataset:Path"];
            var relativePath = string.IsNullOrWhiteSpace(configuredPath) ? "Data/nigerian_recipes.json" : configuredPath;
            var fullPath = Path.IsPathRooted(relativePath)
                ? relativePath
                : Path.Combine(environment.ContentRootPath, relativePath);

            var records = new List<DatasetRecipeRecord>();
            if (File.Exists(fullPath))
            {
                try
                {
                    var json = File.ReadAllText(fullPath);
                    var parsed = JsonSerializer.Deserialize<List<DatasetRecipeRecord>>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (parsed != null)
                    {
                        records = parsed;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to load dataset from {Path}. Falling back to embedded seed.", fullPath);
                }
            }
            else
            {
                logger.LogWarning("Dataset file not found at {Path}. Falling back to embedded seed.", fullPath);
            }

            if (records.Count == 0)
            {
                records = EmbeddedFallbackSeed();
            }

            var mapped = records
                .Select(MapRecord)
                .Where(r => !string.IsNullOrWhiteSpace(r.Title) && r.Ingredients.Count > 0 && IsRelevantDatasetTitle(r.Title))
                .ToList();

            return DeduplicateRecipes(mapped);
        }

        private static List<DatasetRecipeRecord> EmbeddedFallbackSeed()
        {
            return
            [
                new DatasetRecipeRecord
                {
                    Id = 900001,
                    Title = "Nigerian Jollof Rice",
                    Summary = "Classic smoky party jollof rice.",
                    Category = "Nigerian",
                    Instructions = "Blend pepper mix, fry base, cook rice until fluffy.",
                    ImageUrl = BuildDynamicImageUrl("Nigerian Jollof Rice"),
                    Ingredients = ["rice", "tomato", "pepper", "onion", "thyme", "curry", "stock cube"]
                },
                new DatasetRecipeRecord
                {
                    Id = 900002,
                    Title = "Moi Moi",
                    Summary = "Steamed bean pudding.",
                    Category = "Nigerian",
                    Instructions = "Blend peeled beans with spices, steam till set.",
                    ImageUrl = BuildDynamicImageUrl("Moi Moi"),
                    Ingredients = ["beans", "tomato", "pepper", "onion", "stock cube", "fish"]
                },
                new DatasetRecipeRecord
                {
                    Id = 900003,
                    Title = "Egusi Soup",
                    Summary = "Melon seed soup for swallow.",
                    Category = "Nigerian",
                    Instructions = "Cook stock, add egusi paste, simmer with greens.",
                    ImageUrl = BuildDynamicImageUrl("Egusi Soup"),
                    Ingredients = ["egusi", "spinach", "palm oil", "pepper", "onion", "stock fish"]
                }
            ];
        }

        private static Recipe MapRecord(DatasetRecipeRecord record)
        {
            var normalizedIngredients = (record.Ingredients ?? new List<string>())
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => NormalizeIngredient(i))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var ingredients = normalizedIngredients
                .Select(i => new RecipeIngredient
                {
                    Ingredient = new Ingredient { Name = i },
                    Quantity = string.Empty
                })
                .ToList();

            var title = record.Title?.Trim() ?? string.Empty;

            return new Recipe
            {
                Id = record.Id,
                Title = title,
                Summary = record.Summary?.Trim() ?? string.Empty,
                Category = string.IsNullOrWhiteSpace(record.Category) ? "Nigerian" : record.Category.Trim(),
                Instructions = BuildRecipeInstructions(title, normalizedIngredients, record.Instructions),
                ImageUrl = ResolveImageUrl(record.ImageUrl, title, normalizedIngredients),
                Ingredients = ingredients,
                Servings = 4,
                ReadyInMinutes = 45,
                Nutrition = new NutritionInfo()
            };
        }

        private static List<Recipe> DeduplicateRecipes(List<Recipe> source)
        {
            var deduped = source
                .GroupBy(r => BuildDedupKey(r), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var nextId = 900001;
            foreach (var recipe in deduped)
            {
                recipe.Id = nextId++;
                if (string.IsNullOrWhiteSpace(recipe.ImageUrl))
                {
                    recipe.ImageUrl = BuildDynamicImageUrl(recipe.Title);
                }
                recipe.Instructions = BuildRecipeInstructions(
                    recipe.Title,
                    recipe.Ingredients.Select(i => i.Ingredient?.Name ?? string.Empty).ToList(),
                    recipe.Instructions);
            }

            return deduped;
        }

        private static string BuildDedupKey(Recipe recipe)
        {
            var title = recipe.Title.Trim().ToLowerInvariant();
            var ingredientSignature = string.Join('|', recipe.Ingredients
                .Select(i => NormalizeIngredient(i.Ingredient?.Name ?? string.Empty))
                .OrderBy(i => i));

            return $"{title}::{ingredientSignature}";
        }

        private static string NormalizeIngredient(string ingredient)
        {
            var cleaned = ingredient.Trim().ToLowerInvariant();
            cleaned = Regex.Replace(cleaned, "\\s+", " ");
            return cleaned;
        }

        private static bool IsRelevantDatasetTitle(string? title)
        {
            return !string.IsNullOrWhiteSpace(title) && !IrrelevantTitlePattern.IsMatch(title);
        }

        private static List<Recipe> EnsureMinimumDatasetSize(List<Recipe> input, int minSize)
        {
            if (input.Count >= minSize)
            {
                return input;
            }

            var expanded = input.ToList();
            var titlePrefixes = new[] { "Home Style", "Street Style", "Party Style", "Classic", "Family Pot" };
            var addOns = new[] { "ginger", "garlic", "spring onion", "bay leaf", "curry powder" };

            var seedIndex = 0;
            while (expanded.Count < minSize && input.Count > 0)
            {
                var seed = input[seedIndex % input.Count];
                var prefix = titlePrefixes[seedIndex % titlePrefixes.Length];
                var addOn = addOns[seedIndex % addOns.Length];
                var variantTitle = $"{prefix} {seed.Title}";

                var variant = CloneRecipe(seed);
                variant.Title = variantTitle;
                if (!variant.Ingredients.Any(i => string.Equals(i.Ingredient?.Name, addOn, StringComparison.OrdinalIgnoreCase)))
                {
                    variant.Ingredients.Add(new RecipeIngredient
                    {
                        Ingredient = new Ingredient { Name = addOn },
                        Quantity = string.Empty
                    });
                }
                variant.ImageUrl = BuildDynamicImageUrl(variantTitle);

                expanded.Add(variant);
                seedIndex++;
            }

            return DeduplicateRecipes(expanded);
        }

        private static string ResolveImageUrl(string? providedImageUrl, string title, IReadOnlyList<string> ingredients)
        {
            if (!string.IsNullOrWhiteSpace(providedImageUrl))
            {
                var trimmed = providedImageUrl.Trim();
                if (!trimmed.Contains("loremflickr", StringComparison.OrdinalIgnoreCase) &&
                    !trimmed.Contains("source.unsplash.com", StringComparison.OrdinalIgnoreCase) &&
                    !trimmed.Contains("images.unsplash.com", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed;
                }
            }

            var specific = GetSpecificDishImageUrl(title, ingredients);
            if (!string.IsNullOrWhiteSpace(specific))
            {
                return specific;
            }

            return BuildDynamicImageUrl(title, ingredients);
        }

        private static string GetSpecificDishImageUrl(string title, IReadOnlyList<string> ingredients)
        {
            var normalizedTitle = NormalizeDishTitle(title);
            foreach (var pair in SpecificDishImages)
            {
                if (normalizedTitle.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }

            var ingredientText = string.Join(' ', ingredients);
            foreach (var pair in SpecificDishImages)
            {
                if (ingredientText.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }

            return string.Empty;
        }

        private static string NormalizeDishTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return string.Empty;

            var cleaned = title;
            cleaned = Regex.Replace(cleaned, @"\b(lagos|abuja|port harcourt|ibadan|enugu|kano|benin)\s+style\b", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\b(home style|street style|party style|classic|family pot|festival|village pot|smoky|signature|market style|chef style)\b", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\s*\((mild|classic|spicy)\)\s*", " ", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\s+with\s+.+$", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim().ToLowerInvariant();
            return cleaned;
        }

        private static string BuildDynamicImageUrl(string title, IReadOnlyList<string>? ingredients = null)
        {
            var specific = GetSpecificDishImageUrl(title, ingredients ?? Array.Empty<string>());
            if (!string.IsNullOrWhiteSpace(specific))
            {
                return specific;
            }

            return DefaultFoodImage;
        }

        private static string DetectImageKeyword(string title, IReadOnlyList<string>? ingredients)
        {
            var text = title.ToLowerInvariant();
            if (text.Contains("jollof")) return "jollof rice";
            if (text.Contains("moi moi") || text.Contains("moimoi")) return "moi moi";
            if (text.Contains("egusi")) return "egusi soup";
            if (text.Contains("ogbono")) return "ogbono soup";
            if (text.Contains("okra")) return "okra soup";
            if (text.Contains("suya")) return "suya";
            if (text.Contains("pepper soup")) return "pepper soup";
            if (text.Contains("fried rice")) return "nigerian fried rice";
            if (text.Contains("yam porridge")) return "yam porridge";
            if (text.Contains("plantain")) return "plantain dish";
            if (text.Contains("abacha")) return "abacha";
            if (text.Contains("meat pie")) return "nigerian meat pie";

            var ingredientKeyword = ingredients?
                .FirstOrDefault(i => !string.IsNullOrWhiteSpace(i) &&
                                     !i.Contains("salt", StringComparison.OrdinalIgnoreCase) &&
                                     !i.Contains("water", StringComparison.OrdinalIgnoreCase) &&
                                     !i.Contains("stock cube", StringComparison.OrdinalIgnoreCase));

            return string.IsNullOrWhiteSpace(ingredientKeyword) ? "nigerian food" : ingredientKeyword;
        }

        private static string BuildRecipeInstructions(string title, IReadOnlyList<string> ingredients, string? existingInstructions)
        {
            if (!string.IsNullOrWhiteSpace(existingInstructions) && !IsGenericInstructions(existingInstructions))
            {
                return existingInstructions.Trim();
            }

            var ingredientPreview = ingredients
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Take(6)
                .ToArray();
            var ingredientText = ingredientPreview.Length > 0
                ? string.Join(", ", ingredientPreview)
                : "your measured ingredients";

            var dishTypeHint = title.ToLowerInvariant() switch
            {
                var t when t.Contains("soup") => "simmer gently until flavors combine and the soup thickens to your preferred consistency.",
                var t when t.Contains("rice") => "cook covered on low heat until the rice is tender and each grain is separate.",
                var t when t.Contains("moi moi") || t.Contains("moimoi") => "pour into containers and steam until firm in the center.",
                var t when t.Contains("suya") => "grill over medium heat, turning often until browned and smoky.",
                _ => "cook on medium-low heat until the main ingredients are tender and well-seasoned."
            };

            return string.Join('\n',
            [
                $"1. Prep all ingredients for {title}: wash, peel, chop, and measure ({ingredientText}).",
                "2. Blend or chop tomato, pepper, and onion base if your recipe uses them, then set aside.",
                "3. Heat oil, saute aromatics for 2-3 minutes, and fry the base until reduced and fragrant.",
                "4. Add proteins/vegetables, season with stock cube, salt, and dry spices, then cook briefly.",
                $"5. Add starch, soup thickener, or liquid as needed and {dishTypeHint}",
                "6. Taste and adjust seasoning, rest for 3-5 minutes, then serve hot with your preferred side."
            ]);
        }

        private static bool IsGenericInstructions(string instructions)
        {
            var text = instructions.Trim().ToLowerInvariant();
            if (text.Length < 80) return true;

            return text.Contains("prepare ingredients", StringComparison.OrdinalIgnoreCase)
                || text.Contains("see preparation steps", StringComparison.OrdinalIgnoreCase)
                || text.Contains("cook base", StringComparison.OrdinalIgnoreCase);
        }

        private static Recipe CloneRecipe(Recipe recipe)
        {
            return new Recipe
            {
                Id = recipe.Id,
                Title = recipe.Title,
                Summary = recipe.Summary,
                Instructions = recipe.Instructions,
                Category = recipe.Category,
                ImageUrl = string.IsNullOrWhiteSpace(recipe.ImageUrl) ? DefaultFoodImage : recipe.ImageUrl,
                SourceUrl = recipe.SourceUrl,
                ReadyInMinutes = recipe.ReadyInMinutes,
                Servings = recipe.Servings,
                Nutrition = new NutritionInfo
                {
                    Calories = recipe.Nutrition.Calories,
                    ProteinGrams = recipe.Nutrition.ProteinGrams,
                    FatGrams = recipe.Nutrition.FatGrams,
                    CarbsGrams = recipe.Nutrition.CarbsGrams
                },
                Ingredients = recipe.Ingredients.Select(i => new RecipeIngredient
                {
                    Ingredient = new Ingredient { Name = i.Ingredient?.Name ?? string.Empty },
                    Quantity = i.Quantity
                }).ToList()
            };
        }

        private sealed class DatasetRecipeRecord
        {
            public int Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Summary { get; set; } = string.Empty;
            public string Category { get; set; } = "Nigerian";
            public string Instructions { get; set; } = string.Empty;
            public string ImageUrl { get; set; } = string.Empty;
            public List<string> Ingredients { get; set; } = new();
        }

        private sealed class LocalHostEnvironment(string rootPath) : IWebHostEnvironment
        {
            public string ApplicationName { get; set; } = "CookingRecipe";
            public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
            public string WebRootPath { get; set; } = rootPath;
            public string EnvironmentName { get; set; } = Environments.Development;
            public string ContentRootPath { get; set; } = rootPath;
            public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(rootPath);
        }
    }
}


