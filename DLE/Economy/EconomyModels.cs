using DV.ThingTypes;
using System.Collections.Generic;

namespace DLE.Economy
{
    /// <summary>Which servicing a station allows. Gates haul generation and dispatch load/unload.</summary>
    public enum ServiceRole { Both, Load, Unload }

    /// <summary>One cargo and a carload amount (units are whole carloads).</summary>
    public class CargoStack
    {
        public CargoType Cargo;
        public float Amount;

        public CargoStack() { }
        public CargoStack(CargoType cargo, float amount) { Cargo = cargo; Amount = amount; }
    }

    /// <summary>A conversion: consume all inputs to produce all outputs, in carloads.</summary>
    public class RecipeDef
    {
        public List<CargoStack> Inputs = new List<CargoStack>();
        public List<CargoStack> Outputs = new List<CargoStack>();
    }

    /// <summary>
    /// One station's economy: what it converts and how much it can stockpile.
    /// Derived from the game's cargo groups, then overlaid by economy.json.
    /// </summary>
    public class FacilityDef
    {
        public string YardId;
        public List<RecipeDef> Recipes = new List<RecipeDef>();

        // What this facility ships (its output cargo) and consumes (its input cargo),
        // independent of recipes. Used by generation (ship outputs) and demand (need inputs).
        public List<CargoType> Outputs = new List<CargoType>();
        public List<CargoType> Inputs = new List<CargoType>();

        public Dictionary<CargoType, float> StorageCaps = new Dictionary<CargoType, float>();
        public float DefaultCap = 50f;

        // Servicing config (economy.json). Role gates generation: a load-only station is
        // never a haul destination, an unload-only station is never an origin. The remote
        // flags say whether station staff will service cars parked anywhere in the yard,
        // and RemoteSecondsPerCar is the staff time cost per car; consumed by the servicing
        // feature. StorageCaps/DefaultCap double as the storage capacity that caps demand.
        public ServiceRole Role = ServiceRole.Both;
        public bool RemoteLoad = true;
        public bool RemoteUnload = true;

        // Source industries (economy.json "source": true) produce on the clock with no
        // required inputs; their deliveries act through Boosters instead: any one cargo
        // of a booster entry in stock multiplies production speed and is slowly consumed
        // per carload produced. Multiple active boosters stack multiplicatively.
        public bool IsSource;
        public List<BoosterDef> Boosters = new List<BoosterDef>();
        public float RemoteSecondsPerCar = 45f;

        public bool Consumes(CargoType cargo) => Inputs.Contains(cargo);
        public bool Produces(CargoType cargo) => Outputs.Contains(cargo);

        public bool CanLoad => Role != ServiceRole.Unload;
        public bool CanUnload => Role != ServiceRole.Load;

        public float Cap(CargoType cargo) =>
            StorageCaps.TryGetValue(cargo, out var v) ? v : DefaultCap;
    }

    // economy.json overlay shape (cargo names are CargoType enum names).
    public class EconomyOverlay
    {
        public TuningDef settings;
        public OverlayDefaults defaults;
        public Dictionary<string, StationOverlay> stations;
    }

    // World tuning from economy.json's "settings" block; absent keys keep these values.
    // Hot-reloadable through "Reload economy.json".
    public class TuningDef
    {
        public int initialStock = 6;
        public int directorTickSeconds = 120;
        public int sourceProductionMinutes = 10;
        public int poolTrackFillPercent = 90;
        public int maxPoolCars = 500;
    }

    // Global servicing defaults applied to every station before per-station overrides.
    public class OverlayDefaults
    {
        public string role;
        public bool? remoteLoad;
        public bool? remoteUnload;
        public float? remoteSecondsPerCar;
        public float? defaultCap;
    }

    public class StationOverlay
    {
        public float? defaultCap;
        public Dictionary<string, float> caps;
        public List<RecipeOverlay> recipes; // when present, replaces the derived recipes
        public string role;                 // "load" | "unload" | "both"
        public bool? remoteLoad;
        public bool? remoteUnload;
        public float? remoteSecondsPerCar;
        public bool? source;                // produces on the clock, inputs become boosters
        public List<BoosterOverlay> boosters;
    }

    public class RecipeOverlay
    {
        public Dictionary<string, float> inputs;
        public Dictionary<string, float> outputs;
    }

    public class BoosterDef
    {
        public List<CargoType> Cargo = new List<CargoType>();
        public float Speedup = 2f;
        public float ConsumedPerCarload = 0.05f;
    }

    public class BoosterOverlay
    {
        public List<string> cargo;
        public float? speedup;
        public float? consumedPerCarload;
    }
}
