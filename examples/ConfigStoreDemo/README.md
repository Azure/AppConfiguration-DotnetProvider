# ConfigStoreDemo

This .NET 8 Razor Pages demo loads configuration and all unlabeled feature flags
from Azure App Configuration using `DefaultAzureCredential`. Set
`AppConfigurationEndpoint` and authenticate with an identity that has the
App Configuration Data Reader role.

## EasterEgg variants

Create a variant feature flag named `EasterEgg` in the connected store, with no
label. Configure these variants:

| Name | Configuration value |
| --- | --- |
| `NoEgg` | `null` |
| `EggOne` | `"https://pixabay.com/images/download/belladonna-easter-egg-1324878.png"` |
| `EggTwo` | `"https://pixabay.com/images/download/openclipart-vectors-easter-eggs-1297491_1920.png"` |

Use scalar string configuration values for the image URLs, not JSON objects.
Set the disabled default variant to `NoEgg`. Choose an enabled default variant
or configure percentile allocations to distribute the variants across visitors.
The homepage renders an image below the existing messages when the assigned
variant has a nonempty value. A missing flag or a null value renders no image.

Each visitor receives a random targeting ID in the `ConfigStoreDemo.VisitorId`
cookie. The cookie lasts one year, is HTTP-only, uses SameSite=Lax, and is secure
on HTTPS requests. It is an anonymous demo identity, not authentication. Clearing
cookies, changing browsers, or cookie expiration creates a new identity.
Allocation remains stable for the same ID while allocation rules and the seed
remain unchanged. Targeting groups are empty; this demo does not implement login
or group membership.

Configuration and feature flags use separate one-second minimum refresh
intervals. Refresh is triggered by incoming requests, not a background timer.
Reload the page to see updated configuration; a request that triggers refresh
may still render cached values while refresh completes. Message rotation in the
browser does not fetch configuration.

Run the demo using the launch profile in [launchSettings.json](Properties/launchSettings.json):

```powershell
dotnet run --project .\ConfigStoreDemo.csproj
```

Images are loaded directly by the browser from the configured external URLs.
Their availability depends on the image host. The supplied Pixabay download
URLs returned HTTP 403 with a Cloudflare challenge during command-line checks;
they may not support reliable image embedding. If images do not load, use
directly accessible image URLs in the variant values.
