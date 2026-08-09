namespace DLE.Economy
{
    /// <summary>
    /// Seam for how a facility turns delivered inputs into outputs. Since 0.44 the
    /// economy runs on the in-game clock (issue #100): deliveries just stock the
    /// warehouse and the hourly batch loop does the converting, so the default
    /// strategy is a no-op on delivery. (The pre-0.44 InstantConversion was deleted
    /// unreachable in #212; the seam stays for a future strategy.)
    /// </summary>
    public interface IConversionStrategy
    {
        void OnDelivered(EconomyState economy, string yardId);
    }

    /// <summary>0.44 behavior: the game-clock batch loop converts; delivery only stocks.</summary>
    public sealed class PacedConversion : IConversionStrategy
    {
        public void OnDelivered(EconomyState economy, string yardId) { }
    }

    public static class Conversion
    {
        public static IConversionStrategy Current { get; set; } = new PacedConversion();
    }
}
