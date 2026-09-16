// The read-only dedup slice spans four layers, and every file in these tests needs the Contracts
// layer plus whichever layer it exercises. Declaring the namespaces once as global usings keeps each
// test file focused on its own subject instead of repeating an identical import block.
global using System.Runtime.CompilerServices;
global using System.Text.Json;
global using AssetLibrary.Modules.AssetIdentity.Contracts;
global using AssetLibrary.Modules.AssetIdentity.Dedup;
global using AssetLibrary.Modules.AssetIdentity.Dedup.Application;
global using AssetLibrary.Modules.AssetIdentity.Dedup.Contracts;
global using AssetLibrary.Modules.AssetIdentity.Dedup.Domain;
global using AssetLibrary.Modules.AssetIdentity.Dedup.Infrastructure;
global using AssetLibrary.Modules.LibraryStorage.Contracts;
