namespace SalesforceCore.Models.Data;

/// <summary>
/// A geolocation compound field (<c>location</c> type, e.g. <c>Store__c.Location__c</c> or the location part of an
/// address) as REST returns it: <c>{"latitude": 37.79, "longitude": -122.39}</c>. Read-only: write
/// <c>Location__Latitude__s</c> / <c>Location__Longitude__s</c> instead (Object Reference, "Compound Fields").
/// </summary>
public sealed class SalesforceLocation
{
    /// <summary>Latitude in degrees.</summary>
    public decimal? Latitude { get; set; }
    /// <summary>Longitude in degrees.</summary>
    public decimal? Longitude { get; set; }
}

/// <summary>
/// An address compound field (<c>address</c> type, e.g. <c>Account.BillingAddress</c>) as REST returns it. Read-only:
/// write the component fields (<c>BillingStreet</c>, <c>BillingCity</c>, ...) instead.
/// </summary>
public sealed class SalesforceAddress
{
    /// <summary>Street (may contain newlines).</summary>
    public string? Street { get; set; }
    /// <summary>City.</summary>
    public string? City { get; set; }
    /// <summary>State or province.</summary>
    public string? State { get; set; }
    /// <summary>State code (State and Country picklists).</summary>
    public string? StateCode { get; set; }
    /// <summary>Postal code.</summary>
    public string? PostalCode { get; set; }
    /// <summary>Country.</summary>
    public string? Country { get; set; }
    /// <summary>Country code (State and Country picklists).</summary>
    public string? CountryCode { get; set; }
    /// <summary>Latitude, when geocoded.</summary>
    public decimal? Latitude { get; set; }
    /// <summary>Longitude, when geocoded.</summary>
    public decimal? Longitude { get; set; }
    /// <summary>Geocode accuracy (e.g. <c>Address</c>, <c>Zip</c>).</summary>
    public string? GeocodeAccuracy { get; set; }
}
