namespace Singulink.Net.IPLocation;

/// <summary>
/// Provides the credit that the IP location data's license, Creative Commons Attribution 4.0 International (CC BY 4.0), requires wherever results are
/// shown: a link with the text <see cref="Text"/> to <see cref="Url"/>, and the license, which CC BY 4.0 asks to be named with a link to it where that is
/// practical (<see cref="LicenseName"/> and <see cref="LicenseUrl"/>).
/// </summary>
public static class DataAttribution
{
    /// <summary>
    /// The credit text.
    /// </summary>
    public const string Text = "IP Geolocation by DB-IP";

    /// <summary>
    /// The address that the credit links to.
    /// </summary>
    public const string Url = "https://db-ip.com";

    /// <summary>
    /// The short name of the license of the data.
    /// </summary>
    public const string LicenseName = "CC BY 4.0";

    /// <summary>
    /// The address of the license of the data.
    /// </summary>
    public const string LicenseUrl = "https://creativecommons.org/licenses/by/4.0/";
}
