using TroTot.Web.Models;

namespace TroTot.Web.Services;

internal static class RoomFileCleanup
{
    public static void Delete(Room room, IWebHostEnvironment environment, ILogger logger)
    {
        DeleteImages(room.Images, environment, logger);
        DeleteLegalDocuments(
            new[]
            {
                room.IdentityFrontDocument,
                room.IdentityBackDocument,
                room.PropertyDocument,
                room.RoomLegalDocument
            },
            environment,
            logger);
    }

    public static void DeleteImages(
        IEnumerable<RoomImage> images,
        IWebHostEnvironment environment,
        ILogger logger)
    {
        var directory = Path.GetFullPath(Path.Combine(environment.WebRootPath, "uploads", "rooms"));
        foreach (var image in images)
        {
            const string prefix = "/uploads/rooms/";
            if (!image.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var fileName = image.Path[prefix.Length..];
            if (!string.IsNullOrWhiteSpace(fileName) && Path.GetFileName(fileName) == fileName)
                DeleteFile(Path.Combine(directory, fileName), directory, logger);
        }
    }

    public static void DeleteLegalDocuments(
        IEnumerable<string?> fileNames,
        IWebHostEnvironment environment,
        ILogger logger)
    {
        var directory = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data", "verification-documents"));
        foreach (var fileName in fileNames)
        {
            if (!string.IsNullOrWhiteSpace(fileName) && Path.GetFileName(fileName) == fileName)
                DeleteFile(Path.Combine(directory, fileName), directory, logger);
        }
    }

    private static void DeleteFile(string path, string allowedDirectory, ILogger logger)
    {
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(allowedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Could not remove uploaded file {FilePath} after deleting a room listing.", fullPath);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(exception, "Access denied while removing uploaded file {FilePath} after deleting a room listing.", fullPath);
        }
    }
}
