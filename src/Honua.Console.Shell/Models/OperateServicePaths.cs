namespace Honua.Console.Shell.Models;

// The public path a service is served at: its Folder name, then its public name (the same names the endpoint URLs use).
public static class OperateServicePaths
{
    public static string Path(OperateServiceDetail service)
    {
        var name = string.IsNullOrWhiteSpace(service.PublicName) ? service.Name : service.PublicName;
        return string.IsNullOrWhiteSpace(service.FolderName) ? name : $"{service.FolderName}/{name}";
    }
}
