$shippedDependencyFloorExceptions = @(
    'Grpc.Core.Api'
    'Grpc.Net.ClientFactory'
    'Reservoir'
)

function Assert-ShippedDependencyFloor
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$DependencyId,

        [Parameter(Mandatory)]
        [string]$DependencyVersion,

        [Parameter(Mandatory)]
        [string]$Context
    )

    if ($DependencyId.StartsWith('Kevlar', [StringComparison]::Ordinal) -or
        $DependencyId -in $shippedDependencyFloorExceptions)
    {
        return
    }

    if ($DependencyVersion -notmatch '^[\[(]?\s*(?<minimum>\d+(?:\.\d+){1,3})')
    {
        throw "$Context dependency '$DependencyId' has an unrecognized version '$DependencyVersion'."
    }

    $minimumVersion = [Version]$Matches['minimum']
    if ($minimumVersion.Major -gt 8)
    {
        throw (
            "$Context dependency '$DependencyId' has minimum version '$DependencyVersion'; " +
            'shipped dependency floors must remain at major version 8 or earlier.')
    }
}
