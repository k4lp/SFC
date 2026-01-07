using System;
using System.Collections.Generic;

namespace SalesforceCore.Utilities;

/// <summary>
/// Utility methods for handling bitmasks, particularly for Salesforce dependent picklists.
/// </summary>
public static class BitmaskUtils
{
    /// <summary>
    /// Decodes a base64-encoded bitfield string into a list of indices.
    /// Used for Salesforce dependent picklist 'validFor' bitmaps.
    /// </summary>
    /// <param name="validFor">The base64 encoded bitfield string.</param>
    /// <returns>A list of indices where the bit is set to 1.</returns>
    public static List<int> DecodeValidForBitmap(string validFor)
    {
        var result = new List<int>();

        if (string.IsNullOrEmpty(validFor))
        {
            return result;
        }

        try
        {
            var bytes = Convert.FromBase64String(validFor);
            for (int byteIdx = 0; byteIdx < bytes.Length; byteIdx++)
            {
                var b = bytes[byteIdx];
                for (int bit = 0; bit < 8; bit++)
                {
                    // Salesforce bitmaps are big-endian within the byte
                    if ((b & (1 << (7 - bit))) != 0)
                    {
                        result.Add(byteIdx * 8 + bit);
                    }
                }
            }
        }
        catch (FormatException)
        {
            // Invalid base64 string, return empty list or consider logging if context allows
        }

        return result;
    }
}
