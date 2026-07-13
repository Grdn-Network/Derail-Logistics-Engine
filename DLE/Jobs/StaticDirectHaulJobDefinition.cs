using DV.Booklets;
using DV.Logic.Job;
using DV.ThingTypes;
using System.Collections.Generic;

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

            var tasks = new List<Task>();
            if (includeLoadTask)
                tasks.Add(new WarehouseTask(carsToTransport, WarehouseTaskType.Loading,
                    loadMachine, transportedCargo, carsToTransport.Count));

            tasks.Add(new WarehouseTask(carsToTransport, WarehouseTaskType.Unloading,
                unloadMachine, transportedCargo, carsToTransport.Count, (long)timeLimit, true));

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
            }
        }

        public override List<TrackReservation> GetRequiredTrackReservations() =>
            new List<TrackReservation>();

        // DLE keeps its chains out of the vanilla save (see DirectHaulSaveFilterPatch).
        public override JobDefinitionDataBase GetJobDefinitionSaveData() => null;
    }
}
