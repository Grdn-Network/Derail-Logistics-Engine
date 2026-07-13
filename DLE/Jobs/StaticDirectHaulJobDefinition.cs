using DV.Logic.Job;
using DV.ThingTypes;
using System.Collections.Generic;

namespace DLE.Jobs
{
    /// <summary>
    /// A single job that loads cars at the origin warehouse and unloads them at the
    /// destination warehouse (a "Direct Haul"), with no separate shunting jobs.
    ///
    /// The tasks are vanilla WarehouseTasks, so the Multiplayer mod serialises and syncs
    /// the job to clients with no custom packets (its Station.AddJobToStation prefix wraps
    /// any host-side job as a NetworkedJob). jobType is ComplexTransport, a value no
    /// vanilla job definition claims.
    ///
    /// Direct Haul concept from Chump_the_Lump's SelfShunter, used with permission.
    /// </summary>
    public class StaticDirectHaulJobDefinition : StaticJobDefinition
    {
        public List<Car> carsToTransport;
        public WarehouseMachine loadMachine;
        public WarehouseMachine unloadMachine;
        public CargoType transportedCargo;
        public List<float> cargoAmountPerCar;

        protected override void GenerateJob(Station jobOriginStation, float timeLimit = 0f,
            float initialWage = 0f, string forcedJobId = null,
            JobLicenses requiredLicenses = JobLicenses.Basic)
        {
            var load = new WarehouseTask(
                carsToTransport, WarehouseTaskType.Loading,
                loadMachine, transportedCargo, carsToTransport.Count);

            var unload = new WarehouseTask(
                carsToTransport, WarehouseTaskType.Unloading,
                unloadMachine, transportedCargo, carsToTransport.Count,
                (long)timeLimit, true);

            var tasks = new List<Task> { load, unload };

            job = new Job(tasks, JobType.ComplexTransport, timeLimit, initialWage,
                chainData, forcedJobId, requiredLicenses);

            jobOriginStation.AddJobToStation(job);
        }

        public override List<TrackReservation> GetRequiredTrackReservations() =>
            new List<TrackReservation>();

        // Phase 1 keeps DLE chains out of the vanilla save (see DirectHaulSaveFilterPatch);
        // DLE owns its own job persistence in a later commit. Returning null stops the
        // vanilla serialiser from writing a ComplexTransport chain it cannot reload.
        public override JobDefinitionDataBase GetJobDefinitionSaveData() => null;
    }
}
