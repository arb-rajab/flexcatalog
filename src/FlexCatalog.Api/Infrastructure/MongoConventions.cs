using MongoDB.Bson.Serialization.Conventions;

namespace FlexCatalog.Api.Infrastructure;

/// <summary>
/// Registers a camelCase element-name convention for any class that isn't
/// explicitly mapped with [BsonElement] -- this is what makes
/// ElectronicsAttributes.WarrantyMonths serialize as "warrantyMonths" in
/// Mongo, matching the camelCase property names System.Text.Json produces
/// for the same type over the wire. Must run once, before the driver
/// touches any of our types.
/// </summary>
public static class MongoConventions
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        var pack = new ConventionPack { new CamelCaseElementNameConvention() };
        ConventionRegistry.Register("flexcatalog-camel-case", pack, t => t.Namespace == "FlexCatalog.Api.Domain");
        _registered = true;
    }
}
