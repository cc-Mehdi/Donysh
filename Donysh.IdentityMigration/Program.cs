using Donysh.IdentityMigration;

var apply = args.Contains("--apply", StringComparer.Ordinal);
var source = Environment.GetEnvironmentVariable("SOURCE_CONNECTION_STRING");
var target = Environment.GetEnvironmentVariable("TARGET_CONNECTION_STRING");

if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
{
    Console.Error.WriteLine("SOURCE_CONNECTION_STRING and TARGET_CONNECTION_STRING are required.");
    return 2;
}

try
{
    var result = await LegacyIdentityMigrator.RunAsync(source, target, apply);
    Console.WriteLine(apply
        ? $"Migration completed safely. Added: {result.Added}; unchanged: {result.Unchanged}."
        : $"Dry run passed. Users ready to add: {result.Added}; already identical: {result.Unchanged}.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Identity migration stopped without exposing account data: {exception.Message}");
    return 1;
}
