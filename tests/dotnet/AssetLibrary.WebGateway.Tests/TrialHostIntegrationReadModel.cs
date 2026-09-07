using AssetLibrary.Modules.GatewayAuth.Application;
using AssetLibrary.Modules.GatewayAuth.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace AssetLibrary.WebGateway.Tests;

internal static class TrialHostIntegrationReadModel
{
    public static async Task AssertEmptyAsync(IServiceProvider services)
    {
        var query = services.GetRequiredService<IAuthorizedReadModelQuery>();
        var libraries = await query.ListLibrariesAsync(
            new ListLibrariesQuery(new AuthenticatedSubject("local:trial-admin"), ReadPageOptions.Default), CancellationToken.None);
        Assert.IsEmpty(libraries.Items);
    }
}
