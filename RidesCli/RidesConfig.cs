namespace RidesCli;

/// <summary>
/// Configuration for the Rides CLI.
/// </summary>
public class RidesConfig
{
    /// <summary>Price per 100 rides in euros (e.g. 4.00, 24.50). Null if not configured.</summary>
    public decimal? PricePer100 { get; set; }
}
