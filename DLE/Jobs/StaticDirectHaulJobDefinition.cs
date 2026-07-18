using DV.Booklets;
using DV.Logic.Job;
using DV.ThingTypes;
using System.Collections.Generic;
using System.Linq;

namespace DLE.Jobs
{
    /// <summary>
    /// A Direct Haul job. In 0.1 (non-finite mode) the cars spawn already loaded at the
    /// producer, so the job is a single unload at the destination warehouse (the haul is
    /// implicit: the player moves the loaded cars there). jobType is ComplexTransport, a
    /// value no vanilla definition claims, and the tasks are vanilla WarehouseTasks so the
    /// Multiplayer mod syncs the job to clients with no custom packets.
    ///
    /// Mode B (finite empty cars, player loads at the producer) will add a leading load
    /// task in 0.5; the booklet already renders whatever tasks are present.
    ///
    /// Direct Haul concept from Chump_the_Lump's SelfShunter, used with permission.
    /// </summary>
    public class StaticDirectHaulJobDefinition : StaticJobDefinition
    {
        public List<Car> carsToTransport;
        public WarehouseMachine loadMachine;   // used by Mode B (0.5); null in 0.1
        public WarehouseMachine unloadMachine;
        public CargoType transportedCargo;
        public List<float> cargoAmountPerCar;

        /// <summary>Whether to include a leading load task (finite/persistence mode).</summary>
        public bool includeLoadTask = false;

        /// <summary>How many cars this job wants (carless jobs have none attached yet).</summary>
        public int plannedCarCount;

        /// <summary>
        /// The real haul payment. Booklets are faux (the vanilla job pays 0), so this is
        /// paid into the wallet on the delivery unload instead, and only for the cargo the
        /// destination actually accepts (nothing when it is full).
        /// </summary>
        public float deliveryPayment;
        // Unpaid move: dispatch relocating imported goods; the booklet never pays.
        public bool unpaidMove;

        /// <summary>
        /// Carloads that were actually LOADED onto this job's cars (staff load, terminal
        /// load, or restored already-loaded). Turn-in pays against this, because empty
        /// cars at the destination cannot otherwise be told apart from a haul that was
        /// attached but never loaded: without it, delivering a never-loaded cut minted
        /// phantom stock and full pay.
        /// </summary>
        public int loadedCarloads;

        /// <summary>
        /// Car IDs the dispatcher reserved for this haul (plate IDs). Guidance for crews
        /// and preferred by the warehouse attach; not enforced.
        /// </summary>
        public List<string> reservedCarIds = new List<string>();

        /// <summary>Car snapshot for the booklet patches, built by the generator.</summary>
        public List<Car_data> displayCars;

        /// <summary>Where the consist was spawned (e.g. "SW-B3O"), shown on the booklet so
        /// the player knows where to pick the cars up.</summary>
        public string spawnTrackDisplay;

        /// <summary>Live definitions by job ID, so the booklet patches can find display data.</summary>
        public static readonly Dictionary<string, StaticDirectHaulJobDefinition> jobDefinitions =
            new Dictionary<string, StaticDirectHaulJobDefinition>();

        private string _registeredJobId;

        /// <summary>The generated Job (base field is protected; the HTTP API reads state).</summary>
        public Job LiveJob => job;

        protected override void GenerateJob(Station jobOriginStation, float timeLimit = 0f,
            float initialWage = 0f, string forcedJobId = null,
            JobLicenses requiredLicenses = JobLicenses.Basic)
        {
            if (!string.IsNullOrEmpty(forcedJobId))
            {
                jobDefinitions[forcedJobId] = this;
                _registeredJobId = forcedJobId;
            }

            // Cargo amount is UNITS, not car count: with cars attached (a restored job)
            // the machine moves capacity-sum units, the same figure CommitAttach writes.
            // A count here meant a restored consist "loaded" a few units into 45t cars.
            // Carless jobs put the PLANNED car count here: it is the only figure that
            // reaches DVMP clients (task cargoAmount syncs; the definition does not), so
            // their booklets can say "4 loads" instead of 0. CommitAttach rewrites it to
            // the real capacity sum the moment cars attach, so the machine still moves
            // capacity units, exactly as before.
            float cargoUnits = carsToTransport.Count > 0
                ? carsToTransport.Sum(c => c.capacity)
                : plannedCarCount;

            var tasks = new List<Task>();
            if (includeLoadTask)
                tasks.Add(new WarehouseTask(carsToTransport, WarehouseTaskType.Loading,
                    loadMachine, transportedCargo, cargoUnits));

            tasks.Add(new WarehouseTask(carsToTransport, WarehouseTaskType.Unloading,
                unloadMachine, transportedCargo, cargoUnits, (long)timeLimit, true));

            job = new Job(tasks, JobType.ComplexTransport, timeLimit, initialWage,
                chainData, forcedJobId, requiredLicenses);

            jobOriginStation.AddJobToStation(job);
        }

        private void OnDestroy()
        {
            if (_registeredJobId != null &&
                jobDefinitions.TryGetValue(_registeredJobId, out var current) &&
                ReferenceEquals(current, this))
            {
                jobDefinitions.Remove(_registeredJobId);

                // Booklet died before any cars attached (expired, abandoned, deleted):
                // the pre-allocated supply returns. Attached jobs consumed it already.
                if (carsToTransport == null || carsToTransport.Count == 0)
                    Economy.EconomyState.Instance.ReleaseReservation(_registeredJobId);
            }
        }

        public override List<TrackReservation> GetRequiredTrackReservations() =>
            new List<TrackReservation>();

        // DLE keeps its chains out of the vanilla save (see DirectHaulSaveFilterPatch).
        public override JobDefinitionDataBase GetJobDefinitionSaveData() => null;
    }
}
