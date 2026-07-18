using System;

namespace DLE.Economy
{
    /// <summary>
    /// The economy's single clock (#100): in-game time read from the TOD sky cycle, so
    /// every rate in the mod scales together with the player's day-length setting.
    /// Sample() returns the in-game hours elapsed since the previous sample; sleeping
    /// or setting the clock forward advances the economy the same way it advances the
    /// world. Deltas are clamped so a save-load or clock rewind can never explode or
    /// rewind the economy: worst case a jump costs one 24h day of simulation.
    /// </summary>
    public static class GameClock
    {
        private static long _lastTicks;
        private static bool _primed;

        /// <summary>In-game hours since the last call; 0 when the sky is not up yet.</summary>
        public static float Sample()
        {
            var sky = TOD_Sky.Instance;
            if (sky == null || sky.Cycle == null) return 0f;
            long ticks = sky.Cycle.Ticks;
            if (!_primed)
            {
                _primed = true;
                _lastTicks = ticks;
                return 0f;
            }
            double hours = (ticks - _lastTicks) / (double)TimeSpan.TicksPerHour;
            _lastTicks = ticks;
            if (hours <= 0) return 0f;            // clock set backwards: resync, no rewind
            if (hours > 24.0) hours = 24.0;       // save-load or huge skip: cap the cost
            return (float)hours;
        }

        /// <summary>New world: forget the previous world's clock.</summary>
        public static void Reset() => _primed = false;
    }
}
