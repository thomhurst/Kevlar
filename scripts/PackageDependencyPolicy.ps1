function Assert-ShippedDependencyFloor
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$TargetFramework,

        [Parameter(Mandatory)]
        [string]$DependencyId,

        [Parameter(Mandatory)]
        [string]$DependencyVersion,

        [Parameter(Mandatory)]
        [string]$Context
    )

    if ($TargetFramework -notin @('net8.0', '.NETCoreApp8.0') -and
        $TargetFramework -notmatch '^(?:netstandard|\.NETStandard)\d+\.\d+$')
    {
        return
    }

    # Only these package families track the .NET platform version. Unrelated
    # dependencies such as gRPC and Reservoir have their own version schemes.
    if (-not $DependencyId.StartsWith('Microsoft.Extensions.', [StringComparison]::OrdinalIgnoreCase) -and
        $DependencyId -notin @('Microsoft.Bcl.AsyncInterfaces', 'Microsoft.Bcl.TimeProvider', 'System.Threading.RateLimiting'))
    {
        return
    }

    if ($DependencyVersion -notmatch '^[\[(]?\s*(?<major>\d+)\.\d+(?:\.\d+){0,2}(?:[-+][0-9A-Za-z.-]+)?(?:\s*[,\])]|$)')
    {
        throw "$Context dependency '$DependencyId' has an unrecognized minimum version '$DependencyVersion'."
    }

    if ([int]$Matches['major'] -gt 8)
    {
        throw (
            "$Context dependency '$DependencyId' requires '$DependencyVersion' on $TargetFramework; " +
            '.NET 8 and .NET Standard assets must keep .NET dependency floors at 8.x or earlier.')
    }
}
