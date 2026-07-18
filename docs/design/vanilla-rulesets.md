# Vanilla station rulesets (raw rip, current map)

Extracted from the game's scene file `level521` (the current world; `level5` is the
legacy pre-update map also shipped in the assets). Unity typetrees were generated from
the shipped assemblies, so this is the byte-true `StationProceduralJobsRuleset` data.
Each OUTPUT group is a set of cargos plus the exact stations vanilla sends it to;
each INPUT group is a set of cargos plus the stations vanilla sources it from.
Verified: the flattened lists match the running game's derived economy exactly.
This is the routing truth DLE derives from, before any DLE processing.

## CME

**Ships out:**
- Coal -> SM, CP

**Receives:**
- Excavators, MiningTrucks, CraneParts <- MF, HB
- ToolsBrohm, ToolsAAG, ToolsNovae, ToolsTraeg <- HB
- ToolsIskar <- GF

## CMS

**Ships out:**
- Coal -> SM, CP

**Receives:**
- Excavators, MiningTrucks, CraneParts <- MF
- ToolsBrohm, ToolsAAG, ToolsNovae, ToolsTraeg <- HB
- ToolsIskar <- GF

## CP

**Ships out:**
- (nothing)

**Receives:**
- Coal <- CMS, CME
- ToolsBrohm, ToolsAAG, ToolsNovae, ToolsTraeg <- HB
- ToolsIskar <- GF

## CS

**Ships out:**
- EmptySunOmni -> FF
- EmptyIskar, EmptyObco, EmptyGoorsk -> GF
- EmptyKrugmann, EmptyBrohm, EmptyAAG, EmptySperex, EmptyNovae, EmptyTraeg, EmptyChemlek, EmptyNeoGamma -> HB
- ScrapMetal -> SM
- ScrapWood -> SW
- Fish -> FF, GF, MF, CW

**Receives:**
- Methane <- OWC
- NewCars <- MF
- CityBuses <- MF
- Bread, DairyProducts, MeatProducts, CannedFood, CatFood <- FF
- Diesel, Gasoline <- HB, OR
- ElectronicsKrugmann, ElectronicsAAG, ElectronicsNovae, ElectronicsTraeg, ToolsBrohm, ToolsAAG, ToolsNovae, ToolsTraeg, ChemicalsSperex, ClothingNeoGamma, ClothingNovae, ClothingTraeg, Medicine, ImportedNewCars, TropicalFruits, Fish <- HB
- ElectronicsIskar, ToolsIskar, ChemicalsIskar, Furniture, ClothingObco <- GF
- Eggs, TemperateFruits, Vegetables <- FM
- Gasoline <- OR
- Diesel <- OR

## CW

**Ships out:**
- EmptySunOmni -> FF
- EmptyIskar, EmptyObco, EmptyGoorsk -> GF
- EmptyKrugmann, EmptyBrohm, EmptyAAG, EmptySperex, EmptyNovae, EmptyTraeg, EmptyChemlek, EmptyNeoGamma -> HB
- ScrapMetal -> SM
- ScrapWood -> SW

**Receives:**
- Methane <- OWC, OWN
- NewCars <- MF
- CityBuses <- MF
- Bread, DairyProducts, MeatProducts, CannedFood, CatFood <- FF
- Diesel, Gasoline <- HB, OR
- ElectronicsKrugmann, ElectronicsAAG, ElectronicsNovae, ElectronicsTraeg, ToolsBrohm, ToolsAAG, ToolsNovae, ToolsTraeg, ChemicalsSperex, ClothingNeoGamma, ClothingNovae, ClothingTraeg, Medicine, ImportedNewCars, TropicalFruits, Fish <- HB
- ElectronicsIskar, ToolsIskar, ChemicalsIskar, Furniture, ClothingObco <- GF
- Eggs, TemperateFruits, Vegetables <- FM
- Fish <- CS
- Gasoline <- OR
- Diesel <- OR

## FF

**Ships out:**
- Bread, DairyProducts, MeatProducts, CannedFood, CatFood -> HB, GF, MF, CW, CS
- CannedFood, CatFood -> HB
- Alcohol -> HB, GF
- EmptyIskar, EmptyObco, EmptyGoorsk -> GF
- EmptyKrugmann, EmptyBrohm, EmptyAAG, EmptySperex, EmptyNovae, EmptyTraeg, EmptyChemlek, EmptyNeoGamma -> HB
- ScrapMetal -> SM
- ScrapWood -> SW

**Receives:**
- Methane <- OWC, OWN
- EmptySunOmni <- CW, MF, HB, GF, CS
- NewCars <- MF
- CityBuses <- MF
- Diesel, Gasoline <- HB, OR
- ElectronicsKrugmann, ElectronicsAAG, ElectronicsNovae, ElectronicsTraeg, ToolsBrohm, ToolsAAG, ToolsNovae, ToolsTraeg, ChemicalsSperex, ClothingNeoGamma, ClothingNovae, ClothingTraeg, Medicine, ImportedNewCars, TropicalFruits, Fish <- HB
- Nitrogen <- HB
- ElectronicsIskar, ToolsIskar, ChemicalsIskar, Furniture, ClothingObco <- GF
- Pigs, Cows, Poultry, Sheep, Goats, Wheat, Corn, SunflowerSeeds, TemperateFruits, Vegetables, Milk, Eggs, Flour <- FM
- Fish <- CS
- Gasoline <- OR
- Diesel <- OR

## FM

**Ships out:**
- Pigs, Cows, Poultry, Sheep, Goats, Wheat, Corn, SunflowerSeeds, TemperateFruits, Vegetables, Milk, Eggs, Flour -> FF
- Cotton, Wool, Eggs, TemperateFruits, Vegetables -> GF
- Eggs, TemperateFruits, Vegetables -> CW, MF, HB, CS
- Flour -> HB

**Receives:**
- Tractors <- MF
- ToolsBrohm, ToolsAAG, ToolsNovae, ToolsTraeg <- HB
- AmmoniumNitrate <- HB
- ToolsIskar <- GF

## FRC

**Ships out:**
- Logs -> SW

**Receives:**
- Tractors <- MF
- ForestryTrailers <- MF, HB
- ToolsBrohm, ToolsAAG, ToolsNovae, ToolsTraeg <- HB
- ToolsIskar <- GF

## FRS

**Ships out:**
- Logs -> SW

**Receives:**
- Tractors <- MF
- ForestryTrailers <- MF, HB
- ToolsBrohm, ToolsAAG, ToolsNovae, ToolsTraeg <- HB
- ToolsIskar <- GF

## GF

**Ships out:**
- ElectronicsIskar, ToolsIskar, ChemicalsIskar, Furniture, ClothingObco -> HB, FF, MF, CW, CS
- Pipes -> HB
- ElectronicsIskar, ToolsIskar, ChemicalsIskar -> MF
- EmptySunOmni -> FF
- EmptyKrugmann, EmptyBrohm, EmptyAAG, EmptySperex, EmptyNovae, EmptyTraeg, EmptyChemlek, EmptyNeoGamma -> HB
- EmptyKrugmann, EmptyBrohm, EmptyAAG, EmptySperex, EmptyNovae, EmptyTraeg -> HB
- ScrapMetal -> SM
- ScrapWood -> SW
- ToolsIskar -> CME, CMS, CP, FM, FRC, FRS, IME, IMW, OR, OWC, OWN, SM, SW

**Receives:**
- Methane <- OWC, OWN
- EmptyIskar, EmptyObco, EmptyGoorsk <- CW, MF, FF, HB, CS
- NewCars <- MF
- CityBuses <- MF
- Bread, DairyProducts, MeatProducts, CannedFood, CatFood <- FF
- Alcohol <- FF
- Diesel, Gasoline <- HB, OR
- ElectronicsKrugmann, ElectronicsAAG, ElectronicsNovae, ElectronicsTraeg, ToolsBrohm, ToolsAAG, ToolsNovae, ToolsTraeg, ChemicalsSperex <- HB
- ElectronicsKrugmann, ElectronicsAAG, ElectronicsNovae, ElectronicsTraeg, ToolsBrohm, ToolsAAG, ToolsNovae, ToolsTraeg, ChemicalsSperex, ClothingNeoGamma, ClothingNovae, ClothingTraeg, Medicine, ImportedNewCars, TropicalFruits, Fish <- HB
- CryoHydrogen, Ammonia, SodiumHydroxide <- HB
- Cotton, Wool, Eggs, TemperateFruits, Vegetables <- FM
- SteelRolls, SteelBillets, SteelSlabs, SteelBentPlates <- SM
- Fish <- CS
- Plywood, Boards <- SW
- WoodChips <- SW
- Gasoline <- OR
- Diesel <- OR

## HB

**Ships out:**
- Acetylene -> MF
- Diesel, Gasoline -> GF, FF, MF, CW, CS
- ToolsBrohm, ToolsAAG, ToolsNovae, ToolsTraeg -> CP, FM, FRC, FRS, OR, OWC, OWN, SM, SW, CME, CMS, IME, IMW
- ElectronicsKrugmann, ElectronicsAAG, ElectronicsNovae, ElectronicsTraeg, ToolsBrohm, ToolsAAG, ToolsNovae, ToolsTraeg, ChemicalsSperex -> GF, MF
- ElectronicsKrugmann, ElectronicsAAG, ElectronicsNovae, ElectronicsTraeg, ToolsBrohm, ToolsAAG, ToolsNovae, ToolsTraeg, ChemicalsSperex, ClothingNeoGamma, ClothingNovae, ClothingTraeg, Medicine, ImportedNewCars, TropicalFruits, Fish -> GF, FF, MF, CW, CS
- CryoHydrogen, Ammonia, SodiumHydroxide -> GF
- Argon -> SM
- CryoOxygen -> MF, SM
- Nitrogen -> FF
- EmptyIskar, EmptyObco, EmptyGoorsk -> GF
- EmptySunOmni -> FF
- ScrapMetal -> SM
- Excavators, MiningTrucks, CraneParts -> IME, IMW, CME
- ScrapWood -> SW
- ScrapContainers -> SM
- AmmoniumNitrate -> FM
- ForestryTrailers -> FRC, FRS, SW

**Receives:**
- CrudeOil <- OWC, OWN
- Methane <- OWC, OWN
- EmptyKrugmann, EmptyBrohm, EmptyAAG, EmptySperex, EmptyNovae, EmptyTraeg, EmptyChemlek, EmptyNeoGamma <- CW, MF, FF, GF, CS
- NewCars <- MF
- CityBuses <- MF
- Trams <- MF
- SemiTrailers <- MF
- Tractors <- MF
- Excavators, MiningTrucks, CraneParts <- MF
- ForestryTrailers <- MF
- Bread, DairyProducts, MeatProducts, CannedFood, CatFood <- FF
- CannedFood, CatFood <- FF
- Alcohol <- FF
- ElectronicsIskar, ToolsIskar, ChemicalsIskar, Furniture, ClothingObco <- GF
- Pipes <- GF
- EmptyKrugmann, EmptyBrohm, EmptyAAG, EmptySperex, EmptyNovae, EmptyTraeg <- GF
- Eggs, TemperateFruits, Vegetables <- FM
- Flour <- FM
- SteelRolls, SteelBillets, SteelSlabs, SteelBentPlates <- SM
- SteelRails <- SM
- Plywood, Boards <- SW
- Sleepers <- SW
- Gasoline <- OR
- Diesel <- OR
- Gasoline, Diesel <- OR

## HMB

**Ships out:**
- SpentNuclearFuel -> MB
- Ammunition -> MB, MFMB
- MilitaryTrucks -> MB, MFMB
- MilitarySupplies -> MB, MFMB
- AttackHelicopters -> MB, MFMB
- Missiles -> MB, MFMB

**Receives:**
- Ammunition <- MFMB
- Tanks <- MFMB
- MilitaryCars <- MFMB
- Biohazard <- MB

## IME

**Ships out:**
- IronOre -> SM

**Receives:**
- Excavators, MiningTrucks, CraneParts <- MF, HB
- ToolsBrohm, ToolsAAG, ToolsNovae, ToolsTraeg <- HB
- ToolsIskar <- GF

## IMW

**Ships out:**
- IronOre -> SM

**Receives:**
- Excavators, MiningTrucks, CraneParts <- MF, HB
- ToolsBrohm, ToolsAAG, ToolsNovae, ToolsTraeg <- HB
- ToolsIskar <- GF

## MB

**Ships out:**
- Biohazard -> HMB

**Receives:**
- Ammunition <- MFMB, HMB
- Tanks <- MFMB
- MilitaryCars <- MFMB
- SpentNuclearFuel <- HMB
- MilitaryTrucks <- HMB
- MilitarySupplies <- HMB
- AttackHelicopters <- HMB
- Missiles <- HMB

## MF

**Ships out:**
- NewCars -> HB, GF, FF, CW, CS
- CityBuses -> HB, GF, FF, CW, CS
- Trams -> HB
- SemiTrailers -> HB
- EmptySunOmni -> FF
- EmptyIskar, EmptyObco, EmptyGoorsk -> GF
- EmptyKrugmann, EmptyBrohm, EmptyAAG, EmptySperex, EmptyNovae, EmptyTraeg, EmptyChemlek, EmptyNeoGamma -> HB
- Tractors -> FM, HB, FRC, FRS
- Excavators, MiningTrucks, CraneParts -> HB, CME, IME, IMW, CMS
- ScrapMetal -> SM
- ScrapWood -> SW
- ForestryTrailers -> FRC, FRS, SW, HB

**Receives:**
- Methane <- OWC, OWN
- Bread, DairyProducts, MeatProducts, CannedFood, CatFood <- FF
- Acetylene <- HB
- Diesel, Gasoline <- HB, OR
- ElectronicsKrugmann, ElectronicsAAG, ElectronicsNovae, ElectronicsTraeg, ToolsBrohm, ToolsAAG, ToolsNovae, ToolsTraeg, ChemicalsSperex <- HB
- ElectronicsKrugmann, ElectronicsAAG, ElectronicsNovae, ElectronicsTraeg, ToolsBrohm, ToolsAAG, ToolsNovae, ToolsTraeg, ChemicalsSperex, ClothingNeoGamma, ClothingNovae, ClothingTraeg, Medicine, ImportedNewCars, TropicalFruits, Fish <- HB
- CryoOxygen <- HB
- ElectronicsIskar, ToolsIskar, ChemicalsIskar, Furniture, ClothingObco <- GF
- ElectronicsIskar, ToolsIskar, ChemicalsIskar <- GF
- Eggs, TemperateFruits, Vegetables <- FM
- SteelRolls, SteelBillets, SteelSlabs, SteelBentPlates <- SM
- Fish <- CS
- Boards <- SW
- Gasoline <- OR
- Diesel <- OR

## MFMB

**Ships out:**
- Ammunition -> MB, HMB
- Tanks -> MB, HMB
- MilitaryCars -> MB, HMB

**Receives:**
- Ammunition <- HMB
- MilitaryTrucks <- HMB
- MilitarySupplies <- HMB
- AttackHelicopters <- HMB
- Missiles <- HMB

## OR

**Ships out:**
- Gasoline -> CW, FF, GF, HB, MF, CS
- Diesel -> CW, FF, GF, HB, MF, CS
- Gasoline, Diesel -> CW, FF, GF, HB, MF, CS

**Receives:**
- CrudeOil <- OWC, OWN
- Methane <- OWC, OWN
- ToolsBrohm, ToolsAAG, ToolsNovae, ToolsTraeg <- HB
- ToolsIskar <- GF

## OWC

**Ships out:**
- CrudeOil -> HB, OR
- Methane -> HB, GF, FF, MF, CW, OR, CS

**Receives:**
- ToolsBrohm, ToolsAAG, ToolsNovae, ToolsTraeg <- HB
- ToolsIskar <- GF

## OWN

**Ships out:**
- CrudeOil -> HB, OR
- Methane -> HB, GF, FF, MF, CW, OR

**Receives:**
- ToolsBrohm, ToolsAAG, ToolsNovae, ToolsTraeg <- HB
- ToolsIskar <- GF

## SM

**Ships out:**
- SteelRolls, SteelBillets, SteelSlabs, SteelBentPlates -> GF, MF, HB
- SteelRails -> HB

**Receives:**
- Coal <- CMS, CME
- ScrapMetal <- CW, MF, FF, HB, GF, CS
- IronOre <- IME, IMW
- ToolsBrohm, ToolsAAG, ToolsNovae, ToolsTraeg <- HB
- Argon <- HB
- CryoOxygen <- HB
- ScrapContainers <- HB
- ToolsIskar <- GF

## SW

**Ships out:**
- Plywood, Boards -> GF, HB
- Boards -> MF
- Sleepers -> HB
- WoodChips -> GF

**Receives:**
- Logs <- FRS, FRC
- ScrapWood <- CW, MF, FF, HB, GF, CS
- ForestryTrailers <- MF, HB
- ToolsBrohm, ToolsAAG, ToolsNovae, ToolsTraeg <- HB
- ToolsIskar <- GF
