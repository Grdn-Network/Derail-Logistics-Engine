namespace DLE.Economy
{
    /// <summary>
    /// Seam for the 1.0 living economy (issue #22): how a facility turns delivered inputs
    /// into outputs. Instant conversion is the 0.x behavior; a ticked strategy (recipes
    /// take an in-game day) replaces it at 1.0 without touching the delivery path.
    /// </summary>
    public interface IConversionStrategy
    {
        void OnDelivered(EconomyState economy, string yardId);
    }

    /// <summary>0.x behavior: convert everything possible the moment inputs arrive.</summary>
    public sealed class InstantConversion : IConversionStrategy
    {
        public void OnDelivered(EconomyState economy, string yardId) => economy.Convert(yardId);
    }

    public static class Conversion
    {
        public static IConversionStrategy Current { get; set; } = new InstantConversion();
    }
}
