using DLE.Economy;

namespace DLE.Dispatch
{
    /// <summary>
    /// Autonomy levels for the 1.0 AI dispatcher. Off and Baseline exist today; Advisor
    /// proposes without acting; Dispatcher creates and assigns; FullCtc also lines
    /// switches (post-1.0).
    /// </summary>
    public enum DispatcherTier
    {
        Baseline,   // today's behavior: keep the board stocked, no assignments
        Advisor,    // 1.0: propose hauls and assignments for human approval
        Dispatcher, // 1.0 default when the AI toggle lands: create, assign, announce
        FullCtc,    // post-1.0: also line switches for assigned trains
    }

    /// <summary>
    /// Seam for the 1.0 AI dispatcher (issue #23). The director behaviour ticks whatever
    /// brain is current; today that is the baseline generator, and the AI tiers slot in
    /// here without touching the tick loop. A human dispatcher always outranks the brain:
    /// dispatcher-created hauls (POST /api/v1/hauls) are never touched by it.
    /// </summary>
    public interface IDispatcherBrain
    {
        DispatcherTier Tier { get; }

        /// <summary>One unit of work per director tick. Returns true if it did something.</summary>
        bool TickOnce();
    }

    /// <summary>Today's behavior: keep the board stocked from real surpluses.</summary>
    public sealed class BaselineDirectorBrain : IDispatcherBrain
    {
        public DispatcherTier Tier => DispatcherTier.Baseline;
        public bool TickOnce() => EconomyDirector.GenerateOne();
    }

    public static class DispatcherBrain
    {
        public static IDispatcherBrain Current { get; set; } = new BaselineDirectorBrain();
    }
}
