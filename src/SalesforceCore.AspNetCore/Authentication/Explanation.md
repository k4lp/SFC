# Authentication Directory Explanation

## 1. Overview
The `src/SalesforceCore.AspNetCore/Authentication` directory contains infrastructure for managing ASP.NET Core Authentication Sessions when using Salesforce.

## 2. Key Components

### `DistributedCacheTicketStore`
**Purpose**: Solves the "Cookie Too Large" problem.
**The Problem**: An OAuth Access Token, Refresh Token, and ID Token combined can easily exceed 4KB. Most browsers reject cookies larger than 4KB.
**The Solution**: This class implements `ITicketStore`. Instead of serializing the entire User Principal (with tokens) into the cookie, it:
1.  Generates a unique Session Key.
2.  Stores the tokens in a `IDistributedCache` (Redis, SQL, Memory) keyed by the Session Key.
3.  Stores only the Session Key in the browser cookie.

## 3. Design Decisions
- **Scalability**: By using `IDistributedCache`, this solution works in load-balanced environments (Web Farms) where user requests might hit different servers.
