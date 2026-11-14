using Newtonsoft.Json.Linq;
using RevitServerNet.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace RevitServerNet.Extensions
{
    // Extensions for working with locks and lock operations.
    // The server answers all of them with 2xx and an empty body (DeleteDescendentLocksAsync: {"Path":..,"FailedItems":null});
    // failures are non-2xx responses, which RevitServerApi throws as RevitServerApiException.
    public static class LockingExtensions
    {
        // Locks a model or folder. Throws RevitServerApiException when the server refuses.
        public static async Task<OperationResult> LockItemAsync(this RevitServerApi api, string itemPath)
        {
            var encodedPath = RequirePath(itemPath, nameof(itemPath), allowRoot: false);
            await api.PutAsync($"{encodedPath}/lock").ConfigureAwait(false);
            return new OperationResult { Success = true, Message = "Item locked successfully" };
        }

        // Unlocks a model or folder. Throws RevitServerApiException when the server refuses.
        public static async Task<OperationResult> UnlockItemAsync(this RevitServerApi api, string itemPath)
        {
            var encodedPath = RequirePath(itemPath, nameof(itemPath), allowRoot: true);
            await api.DeleteAsync($"{encodedPath}/lock?objectMustExist=true").ConfigureAwait(false);
            return new OperationResult { Success = true, Message = "Item unlocked successfully" };
        }

        // Cancels an in-progress lock (the server answers 200 also when there is none)
        public static async Task<OperationResult> CancelLockAsync(this RevitServerApi api, string itemPath)
        {
            var encodedPath = RequirePath(itemPath, nameof(itemPath), allowRoot: true);
            try
            {
                await api.DeleteAsync($"{encodedPath}/inProgressLock").ConfigureAwait(false);
                return new OperationResult { Success = true, Message = "Lock cancelled" };
            }
            catch (RevitServerApiException ex)
            {
                return new OperationResult { Success = false, Message = $"Failed to cancel lock: {ex.Message}" };
            }
        }

        //Gets descendent locks
        public static async Task<string> GetDescendentLocksAsync(this RevitServerApi api, string folderPath)
        {
            try
            {
                var encodedPath = RevitServerApi.EncodePath(folderPath);
                var command = $"{encodedPath}/descendent/locks";
                return await api.GetAsync(command).ConfigureAwait(false);
            }
            catch (RevitServerApiException ex)
            {
                return ex.ToString();
            }
            catch (Exception ex)
            {
                return ex.ToString();
            }
        }

        // Deletes descendent locks. Success is false when the server reports FailedItems.
        public static async Task<OperationResult> DeleteDescendentLocksAsync(this RevitServerApi api, string folderPath)
        {
            var encodedPath = RequirePath(folderPath, nameof(folderPath), allowRoot: true);
            try
            {
                var json = await api.DeleteAsync($"{encodedPath}/descendent/locks").ConfigureAwait(false);

                var failed = string.IsNullOrWhiteSpace(json) ? null : JObject.Parse(json)["FailedItems"];
                if (failed == null || failed.Type == JTokenType.Null || (failed is JArray items && items.Count == 0))
                    return new OperationResult { Success = true, Message = "Descendent locks deleted" };

                var list = failed is JArray array ? string.Join(", ", array.Select(i => i.ToString())) : failed.ToString();
                return new OperationResult { Success = false, Message = $"Failed to delete descendent locks: {list}" };
            }
            catch (RevitServerApiException ex)
            {
                return new OperationResult { Success = false, Message = $"Failed to delete descendent locks: {ex.Message}" };
            }
        }

        // Copies a model or folder
        public static Task<OperationResult> CopyItemAsync(this RevitServerApi api, string sourcePath, string destinationPath, bool overwrite = false)
            => PasteAsync(api, sourcePath, destinationPath, overwrite, "Copy", "copied", "copy");

        // Moves a model or folder
        public static Task<OperationResult> MoveItemAsync(this RevitServerApi api, string sourcePath, string destinationPath, bool overwrite = false)
            => PasteAsync(api, sourcePath, destinationPath, overwrite, "Move", "moved", "move");

        private static async Task<OperationResult> PasteAsync(RevitServerApi api, string sourcePath, string destinationPath, bool overwrite, string pasteAction, string done, string verb)
        {
            var encodedSourcePath = RequirePath(sourcePath, nameof(sourcePath), allowRoot: false);
            var encodedDestPath = RequirePath(destinationPath, nameof(destinationPath), allowRoot: false);
            try
            {
                var command = $"{encodedSourcePath}?destinationObjectPath={encodedDestPath}&pasteAction={pasteAction}&replaceExisting={overwrite.ToString().ToLower()}";
                await api.PostAsync(command).ConfigureAwait(false);
                return new OperationResult { Success = true, Message = $"Item {done} successfully" };
            }
            catch (RevitServerApiException ex)
            {
                // Some destination names (for example "Ка.rvt") make the server answer 500 after it performed the operation,
                // and a timeout leaves the outcome open too. Verifiable: a move (source gone, destination there), and after a 500 a copy
                // without overwrite (an existing destination gives 409, so the destination is new). Not after a timeout: the server may
                // still be copying into a destination that already exists. A copy with overwrite cannot be told apart from a failure.
                var verifiable = pasteAction == "Move"
                    ? FolderExtensions.OutcomeUnknown(ex)
                    : FolderExtensions.IsServerError(ex) && !overwrite;
                if (verifiable && await FolderExtensions.VerifyAsync(async () =>
                        await FolderExtensions.ItemExistsAsync(api, destinationPath).ConfigureAwait(false)
                        && (pasteAction != "Move" || !await FolderExtensions.ItemExistsAsync(api, sourcePath).ConfigureAwait(false))).ConfigureAwait(false))
                    return new OperationResult { Success = true, Message = $"Item {done} ({FolderExtensions.Answer(ex)} but the outcome was verified)" };

                return new OperationResult { Success = false, Message = $"Failed to {verb} item: {ex.Message}" };
            }
        }

        // An empty path means the server root in EncodePath: never let a missing argument reach the server as the root.
        // The root itself has to be asked for explicitly ("|") and only where it makes sense.
        private static string RequirePath(string path, string paramName, bool allowRoot)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be null, empty or whitespace", paramName);

            var encoded = RevitServerApi.EncodePath(path);
            if (!allowRoot && encoded == "|")
                throw new ArgumentException("The server root is not allowed here", paramName);
            return encoded;
        }
    }
}
