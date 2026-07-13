using DV.ThingTypes;
using System.Collections.Generic;

namespace DLE.Economy
{
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

        public bool Consumes(CargoType cargo) => Inputs.Contains(cargo);
        public bool Produces(CargoType cargo) => Outputs.Contains(cargo);

        public float Cap(CargoType cargo) =>
            StorageCaps.TryGetValue(cargo, out var v) ? v : DefaultCap;
    }

    // economy.json overlay shape (cargo names are CargoType enum names).
    public class EconomyOverlay
    {
        public Dictionary<string, StationOverlay> stations;
    }

    public class StationOverlay
    {
        public float? defaultCap;
        public Dictionary<string, float> caps;
        public List<RecipeOverlay> recipes; // when present, replaces the derived recipes
    }

    public class RecipeOverlay
    {
        public Dictionary<string, float> inputs;
        public Dictionary<string, float> outputs;
    }
}
