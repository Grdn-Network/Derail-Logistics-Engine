using DV.ThingTypes;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace DLE.Economy
{
    /// <summary>Which servicing a station allows. Gates haul generation and dispatch load/unload.</summary>
    public enum ServiceRole { Both, Load, Unload }

    /// <summary>
    /// Brand bundles (#100): interchangeable cargo families. Recipes, machine checks and
    /// catalyst checks accept any member; loading ships whatever brand is in stock.
    /// Production still credits the vanilla-true domestic brand.
    /// </summary>
    public static class CargoCategories
    {
        public static readonly Dictionary<string, CargoType[]> Members =
            new Dictionary<string, CargoType[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tools"] = new[] { CargoType.ToolsIskar, CargoType.ToolsBrohm, CargoType.ToolsAAG, CargoType.ToolsNovae, CargoType.ToolsTraeg },
            ["Electronics"] = new[] { CargoType.ElectronicsIskar, CargoType.ElectronicsKrugmann, CargoType.ElectronicsAAG, CargoType.ElectronicsNovae, CargoType.ElectronicsTraeg },
            ["Clothing"] = new[] { CargoType.ClothingObco, CargoType.ClothingNeoGamma, CargoType.ClothingNovae, CargoType.ClothingTraeg },
            ["Chemicals"] = new[] { CargoType.ChemicalsIskar, CargoType.ChemicalsSperex },
            // Internal helper for GF's chemicals recipe: any harbor gas works.
            ["Gases"] = new[] { CargoType.CryoHydrogen, CargoType.Ammonia, CargoType.SodiumHydroxide },
        };

        public static bool TryGet(string name, out CargoType[] members) =>
            Members.TryGetValue(name ?? "", out members);
    }

    /// <summary>One recipe ingredient or product: either a concrete cargo, or a category
    /// name (any member brand satisfies and is consumed, biggest pile first).</summary>
    public class CargoStack
    {
        public CargoType Cargo;
        public float Amount;
        public string Category; // when set, Cargo is ignored on the input side

        public CargoStack() { }
        public CargoStack(CargoType cargo, float amount) { Cargo = cargo; Amount = amount; }
        public CargoStack(string category, float amount) { Category = category; Amount = amount; }

        public string Display => Category ?? Cargo.ToString();
    }

    /// <summary>A conversion: consume all inputs to produce all outputs, in carloads.</summary>
    public class RecipeDef
    {
        public List<CargoStack> Inputs = new List<CargoStack>();
        public List<CargoStack> Outputs = new List<CargoStack>();
    }

    /// <summary>
    /// One station's economy: what it converts, what equipment it needs, and how much it
    /// can stockpile. Derived from the game's cargo groups, then overlaid by economy.json.
    /// </summary>
    public class FacilityDef
    {
        public string YardId;
        public List<RecipeDef> Recipes = new List<RecipeDef>();

        // What this facility ships (its output cargo) and consumes (its input cargo),
        // independent of recipes. Used by generation (ship outputs) and demand (need inputs).
        public List<CargoType> Outputs = new List<CargoType>();
        public List<CargoType> Inputs = new List<CargoType>();

        // Storage is ONE shared pool per station (#92): every cargo pile at the yard
        // counts against the same total. Per-cargo caps return as a 1.0 idea.
        public float TotalCap = 100f;

        // MACHINES (#100): required equipment, one of EACH type present or the site
        // crawls at the tuning's crawl factor. Each machine unit is consumed after
        // machineLifeCarloads of production (divided by catalystWearFactor when a
        // catalyst is present). Machines only wear while the site actually produces.
        public List<CargoType> Machines = new List<CargoType>();

        // CATALYSTS (#100): cargo or category names. At sources they only slow machine
        // wear; at factories they double batch speed. A catalyst carload lasts
        // catalystLifeGameHours of actual production time.
        public List<string> Catalysts = new List<string>();

        // Living demand (#100): cities and the power plant consume their stock on the
        // clock, freeing room (perpetual demand) and feeding the global boost. Cities
        // also emit scrap back into the recycling loop.
        public bool ConsumesStock;
        public bool EmitsScrap;

        // The harbor (#100): restocks its import list in proportion to the exports it
        // receives; the 1.0 sailing schedule replaces the flat rate later.
        public bool IsImportHub;

        // Servicing config (economy.json). Role gates generation: a load-only station is
        // never a haul destination, an unload-only station is never an origin. The remote
        // flags say whether station staff will service cars parked anywhere in the yard,
        // and RemoteSecondsPerCar is the staff time cost per car; consumed by the servicing
        // feature. TotalCap doubles as the storage capacity that caps demand.
        public ServiceRole Role = ServiceRole.Both;
        public bool RemoteLoad = true;
        public bool RemoteUnload = true;

        // Source industries (economy.json "source": true) produce on the game clock with
        // no required recipe inputs; the base rate is one carload per game hour shared
        // round-robin across their outputs.
        public bool IsSource;
        public float RemoteSecondsPerCar = 45f;

        public bool Consumes(CargoType cargo) => Inputs.Contains(cargo);
        public bool Produces(CargoType cargo) => Outputs.Contains(cargo);

        public bool CanLoad => Role != ServiceRole.Unload;
        public bool CanUnload => Role != ServiceRole.Load;
    }

    // economy.json overlay shape (cargo names are CargoType enum names or category names).
    public class EconomyOverlay
    {
        public int configVersion;
        public TuningDef settings;
        public OverlayDefaults defaults;
        public Dictionary<string, StationOverlay> stations;
    }

    // World tuning from economy.json's "settings" block; absent keys keep these values.
    // Hot-reloadable through "Reload economy.json". All rates are in-game time (#100).
    public class TuningDef
    {
        public int initialStock = 6;
        public int directorTickSeconds = 120;
        public int poolTrackFillPercent = 90;
        public int maxPoolCars = 500;

        // The economy clock (#100). Base production: carloads per game hour at a source
        // (shared round-robin across its outputs). Factories run batchesPerGameHour base.
        public float sourceCarloadsPerGameHour = 1f;
        public float factoryBatchesPerGameHour = 1f;

        // Machines: carloads of production before one machine unit is consumed; catalyst
        // presence divides wear. Sites without their machines crawl at crawlFactor.
        public int machineLifeCarloads = 50;
        public float catalystWearFactor = 7f;
        public float crawlFactor = 0.25f;
        public int seedMachines = 2;

        // Catalysts: a carload lasts this long, burning only while the site produces.
        // At factories an active catalyst multiplies batch speed by factoryBoostFactor.
        public float catalystLifeGameHours = 24f;
        public float factoryBoostFactor = 2f;

        // Living demand: consumer stations eat this much per game hour; every
        // scrapPerConsumed consumed carloads at a scrap-emitting city yields one scrap.
        public float cityConsumptionPerHour = 1f;
        public float carloadsPerScrap = 4f;

        // Global boost: consumption across all consumers over the last 24 game hours,
        // divided by globalBoostFullAt, adds up to (globalBoostMax - 1) to every rate.
        public float globalBoostMax = 1.5f;
        public float globalBoostFullAt = 48f;

        // Harbor: imports gained per game hour = exports received over the last 24 game
        // hours / 24 * harborImportFactor, spread over its import list with tools rare.
        public float harborImportFactor = 0.5f;
        public int toolImportRarity = 8; // tools land once per this many import carloads

        // Cargo kept out of the economy entirely (no stock, no demand, no hauls).
        // Replace, not Auto: Json.NET's default would APPEND the file's list to these
        // built-ins, so a user who deleted an entry could never un-exclude it.
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> excludedCargos = new List<string>
        {
            "EmptySunOmni", "EmptyIskar", "EmptyObco", "EmptyGoorsk",
            "EmptyKrugmann", "EmptyBrohm", "EmptyAAG", "EmptySperex",
            "EmptyNovae", "EmptyTraeg", "EmptyChemlek", "EmptyNeoGamma",
        };
    }

    // Global servicing defaults applied to every station before per-station overrides.
    // defaultCap/caps are the pre-0.43 per-cargo storage keys: defaultCap still parses
    // (doubled into the station total, matching the #92 conversion), caps is ignored.
    public class OverlayDefaults
    {
        public string role;
        public bool? remoteLoad;
        public bool? remoteUnload;
        public float? remoteSecondsPerCar;
        public float? totalCap;
        public float? defaultCap;
    }

    public class StationOverlay
    {
        public float? totalCap;
        public float? defaultCap;
        public Dictionary<string, float> caps;
        public List<RecipeOverlay> recipes; // when present, replaces the derived recipes
        public string role;                 // "load" | "unload" | "both"
        public bool? remoteLoad;
        public bool? remoteUnload;
        public float? remoteSecondsPerCar;
        public bool? source;                // produces on the clock
        public bool? consumesStock;         // city or power plant: eats stock on the clock
        public bool? emitsScrap;            // consumption also yields ScrapMetal/ScrapWood
        public bool? importHub;             // harbor: imports scale to exports received
        public List<string> machines;       // required equipment cargo names
        public List<string> catalysts;      // cargo or category names
        public List<BoosterOverlay> boosters; // pre-0.44 key, ignored with a log pointer
    }

    public class RecipeOverlay
    {
        public Dictionary<string, float> inputs;
        public Dictionary<string, float> outputs;
    }

    public class BoosterOverlay
    {
        public List<string> cargo;
        public float? speedup;
        public float? consumedPerCarload;
    }
}
