using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.ServiceModel;
using System.ServiceModel.Description;
using System.Threading;
using System.Threading.Tasks;
using RevitServerNet.Tools;

namespace RevitServerNet.Enterprise
{
	internal sealed class RsModelExporterOptions
	{
		public string ServerHost { get; set; }
		public string ModelPipePath { get; set; }
		public string DestinationFile { get; set; }
		public string RevitVersion { get; set; }
		public string AssembliesPath { get; set; }
		public bool Overwrite { get; set; }
		public bool CreateLocal { get; set; }

		/// <summary>
		/// Folder in which the export creates its work folder (and removes old ones); null: <see cref="Path.GetTempPath"/>.
		/// </summary>
		public string WorkRoot { get; set; }
	}

	/// <summary>
	/// Endpoint and binding of the Revit Server ModelService (net.tcp, streamed).
	/// </summary>
	internal static class RsModelServiceEndpoint
	{
		/// <summary>Connecting to the server (Autodesk TcpStreamBinding: 60 s).</summary>
		public static readonly TimeSpan OpenTimeout = TimeSpan.FromMinutes(1);

		/// <summary>Closing the channel (Autodesk TcpStreamBinding: 60 s).</summary>
		public static readonly TimeSpan CloseTimeout = TimeSpan.FromMinutes(1);

		/// <summary>
		/// One call, until its reply starts to arrive (for DownloadFile: until the file stream starts).
		/// Reading a downloaded file's stream is not limited by it.
		/// </summary>
		public static readonly TimeSpan SendTimeout = TimeSpan.FromMinutes(10);

		/// <summary>Receive timeout of the binding (Autodesk TcpStreamBinding: 10 min).</summary>
		public static readonly TimeSpan ReceiveTimeout = TimeSpan.FromMinutes(10);

		/// <summary>
		/// Reply timeout of UnlockData after the export was cancelled, instead of <see cref="SendTimeout"/>: the caller cancelled,
		/// often because the server stopped answering, and should not wait 10 min for the unlock.
		/// </summary>
		public static readonly TimeSpan CancelledUnlockTimeout = TimeSpan.FromMinutes(1);

		/// <summary>Largest downloaded model data file: 5 GB.</summary>
		public const long MaxReceivedMessageSize = 5L * 1024 * 1024 * 1024;

		/// <summary>
		/// net.tcp://{serverHost}/ModelService{revitVersion}/ModelService.svc/tcpstream
		/// </summary>
		/// <param name="serverHost">Host name or IP address, optionally with :port (net.tcp default port is 808).</param>
		/// <param name="revitVersion">Revit Server year, four digits.</param>
		/// <exception cref="ArgumentException">Invalid host or version.</exception>
		public static Uri BuildUri(string serverHost, string revitVersion)
		{
			ValidateRevitVersion(revitVersion);
			if (string.IsNullOrWhiteSpace(serverHost)) throw new ArgumentException("ServerHost is required", "ServerHost");
			var host = serverHost.Trim();
			// A bare IPv6 address needs brackets in a URI.
			if (IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6 && !host.StartsWith("[", StringComparison.Ordinal))
				host = "[" + host + "]";
			var path = "/ModelService" + revitVersion + "/ModelService.svc/tcpstream";
			if (!Uri.TryCreate("net.tcp://" + host + path, UriKind.Absolute, out var uri)
				|| uri.AbsolutePath != path || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
				throw new ArgumentException($"ServerHost '{serverHost}' must be a host name or IP address, optionally with :port (no scheme or path).", "ServerHost");
			return uri;
		}

		/// <exception cref="ArgumentException"><paramref name="revitVersion"/> is not a four-digit year.</exception>
		public static void ValidateRevitVersion(string revitVersion)
		{
			if (revitVersion == null || revitVersion.Length != 4 || revitVersion.Any(c => c < '0' || c > '9'))
				throw new ArgumentException($"RevitVersion must be a four-digit year (for example \"2024\"), got '{revitVersion}'.", "RevitVersion");
		}

		/// <summary>
		/// NetTcpBinding without security, streamed transfer, reader quotas and buffer sizes of the Autodesk TcpStreamBinding,
		/// 5 GB message limit. PortSharingEnabled (set by Autodesk's binding) is not used: it does not exist in WCF for .NET Core,
		/// and the export works without it on both runtimes.
		/// </summary>
		public static NetTcpBinding CreateBinding()
		{
			var binding = new NetTcpBinding(SecurityMode.None)
			{
				TransferMode = TransferMode.Streamed,
				MaxReceivedMessageSize = MaxReceivedMessageSize,
				MaxBufferSize = 655360,
				MaxBufferPoolSize = 655360,
				OpenTimeout = OpenTimeout,
				CloseTimeout = CloseTimeout,
				SendTimeout = SendTimeout,
				ReceiveTimeout = ReceiveTimeout,
			};
			binding.ReaderQuotas.MaxArrayLength = 16384;
			binding.ReaderQuotas.MaxBytesPerRead = 4096;
			binding.ReaderQuotas.MaxDepth = 32;
			binding.ReaderQuotas.MaxNameTableCharCount = 16384;
			binding.ReaderQuotas.MaxStringContentLength = 30720;
			return binding;
		}
	}

	/// <summary>
	/// Retries of the lock calls. <see cref="Default"/> follows RevitServerTool (DataStorageToolClient.LockData/UnlockData:
	/// 5 attempts, 10 s apart).
	/// </summary>
	internal sealed class RsExportRetryPolicy
	{
		public static readonly RsExportRetryPolicy Default =
			new RsExportRetryPolicy(5, TimeSpan.FromSeconds(10), 5, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));

		public RsExportRetryPolicy(int lockAttempts, TimeSpan lockRetryDelay, int unlockAttempts, TimeSpan unlockRetryDelay, TimeSpan interruptedLockDelay)
		{
			if (lockAttempts < 1) throw new ArgumentOutOfRangeException(nameof(lockAttempts));
			if (unlockAttempts < 1) throw new ArgumentOutOfRangeException(nameof(unlockAttempts));
			LockAttempts = lockAttempts;
			LockRetryDelay = lockRetryDelay;
			UnlockAttempts = unlockAttempts;
			UnlockRetryDelay = unlockRetryDelay;
			InterruptedLockDelay = interruptedLockDelay;
		}

		/// <summary>LockData attempts while the server answers Busy or with a lock contention fault.</summary>
		public int LockAttempts { get; }

		/// <summary>Wait before a LockData retry (cancellable).</summary>
		public TimeSpan LockRetryDelay { get; }

		/// <summary>UnlockData attempts while it fails with a communication error.</summary>
		public int UnlockAttempts { get; }

		/// <summary>Wait before an UnlockData retry (not cancellable, like the unlock itself).</summary>
		public TimeSpan UnlockRetryDelay { get; }

		/// <summary>
		/// Wait before the second UnlockData when LockData was interrupted (aborted or timed out) and the first UnlockData found no lock:
		/// the server may still be processing that LockData (it retries each lock file for up to 2 s).
		/// </summary>
		public TimeSpan InterruptedLockDelay { get; }
	}

	/// <summary>
	/// Server side of one export: the ModelService calls of RevitServerTool createLocalRVT and the Autodesk Helper steps that build the RVT file.
	/// <see cref="RsModelExporter"/> decides the order, the lock lifecycle, the retries and the error reporting.
	/// </summary>
	internal interface IModelExportSession
	{
		/// <summary>IdentifyModel; throws when the server returns no valid model identity.</summary>
		void IdentifyModel(CancellationToken ct);

		/// <summary>
		/// LockData (non-exclusive read lock); returns the LockStatus name. Calls <paramref name="lockMayBeHeld"/> right before the request
		/// is sent: from then on, until the server answers, the lock may have been taken (for example when the call is aborted or times out).
		/// </summary>
		string LockData(Action lockMayBeHeld, CancellationToken ct);

		/// <summary>GetListOfModelDataFilesWithoutLocking; returns the ValidationStatus name.</summary>
		string GetListOfModelDataFiles(CancellationToken ct, out List<string> files);

		/// <summary>DownloadFile; the caller reads <paramref name="stream"/> and disposes the returned response.</summary>
		IDisposable DownloadFile(string file, CancellationToken ct, out Stream stream);

		/// <summary>ModelDataFormatVersion (the boxed DataFormatVersion).</summary>
		object ModelDataFormatVersion(CancellationToken ct);

		/// <summary>
		/// UnlockData; returns the LockStatus name. Not cancellable; when <paramref name="cancelled"/> is true, the reply is awaited for
		/// <see cref="RsModelServiceEndpoint.CancelledUnlockTimeout"/> at most.
		/// </summary>
		string UnlockData(bool cancelled);

		/// <summary>Aborts the connection: a waiting call fails at once, and the next call opens a new connection.</summary>
		void Abort();

		/// <summary>Closes the connection; no server call follows.</summary>
		void Close();

		/// <summary>Assembles the RVT file from the downloaded model data (Autodesk Helper).</summary>
		void GenerateRvtFile(string dataDirectory, object dataFormatVersion, string rvtPath);

		/// <summary>Rewrites the BasicFileInfo of the RVT file as a local copy of the server model (Autodesk Helper).</summary>
		void MakeCreatedLocal(string rvtPath, object dataFormatVersion);
	}

	/// <summary>
	/// Direct export of a Revit Server model to an RVT file over the ModelService, following RevitServerTool createLocalRVT:
	/// IdentifyModel, LockData (read lock), GetListOfModelDataFilesWithoutLocking, DownloadFile per file, ModelDataFormatVersion,
	/// UnlockData (always, once the lock may be held), then the RVT is assembled with the Autodesk Helper.
	/// </summary>
	internal sealed class RsModelExporter
	{
		/// <summary>Data key of the UnlockData failure attached to the exception that ended an export.</summary>
		internal const string UnlockErrorDataKey = "RevitServerNet.UnlockDataError";

		/// <summary>Data key of the temp folder cleanup failure attached to the exception that ended an export.</summary>
		internal const string TempCleanupErrorDataKey = "RevitServerNet.TempCleanupError";

		/// <summary>
		/// Data key of the destination file, set on the exception thrown when only releasing the read lock failed:
		/// the RVT file is complete at that path.
		/// </summary>
		internal const string ExportedFileDataKey = "RevitServerNet.ExportedFile";

		/// <summary>
		/// Data key of the error raised while closing a download that ended early; the download's own exception is the one thrown.
		/// </summary>
		internal const string DownloadCloseErrorDataKey = "RevitServerNet.DownloadCloseError";

		/// <summary>
		/// Data key of the warning that the read lock was already released when UnlockData ran, attached to the exception that ended an export.
		/// </summary>
		internal const string ReadLockLostDataKey = "RevitServerNet.ReadLockLost";

		/// <summary>Data key of the failure to delete the partly moved file next to the destination.</summary>
		internal const string PartialFileCleanupErrorDataKey = "RevitServerNet.PartialFileCleanupError";

		/// <summary>Prefix of the export's work folder, "{prefix}{guid:N}".</summary>
		internal const string WorkDirectoryPrefix = "RevitServerNet_";

		/// <summary>
		/// Age after which a work folder left behind (a failed cleanup or a killed process) is deleted by a later export,
		/// as RevitServerTool does with its own temp folders.
		/// </summary>
		internal static readonly TimeSpan StaleWorkDirectoryAge = TimeSpan.FromDays(7);

		/// <summary>
		/// Longest mutex name used as is; a longer name is replaced by a hash. 260 characters is the limit of a mutex name on .NET Framework
		/// (MAX_PATH), so every name RevitServerTool can create itself is used unchanged.
		/// </summary>
		internal const int MaxModelMutexNameLength = 260;

		/// <summary>
		/// Work folders of this version ("RevitServerNet_{guid}") and of versions before 1.3.0 ("RevitServerNet_ModelData_{guid}").
		/// </summary>
		private static readonly Regex WorkDirectoryName = new Regex("^RevitServerNet_(ModelData_)?[0-9a-f]{32}$", RegexOptions.CultureInvariant);

		/// <summary>
		/// User name of the export's session tokens; the server keeps the read lock under this name.
		/// Same name as before 1.3.0 (RevitServerTool pattern "RevitServerTool:{machine}:{n}", where RevitServerTool's n is the number
		/// of its installation folder on the machine, so only the installation with number 1 uses this name).
		/// </summary>
		internal static string ExportUserName => $"RevitServerTool:{Environment.MachineName}:1";

		public Task<string> ExportAsync(RsModelExporterOptions options, IProgress<long> bytesProgress = null, CancellationToken cancellationToken = default)
		{
			return ExportAsync(options, bytesProgress, cancellationToken, (o, endpoint, where) => new ModelServiceSession(o, endpoint, where), RsExportRetryPolicy.Default);
		}

		/// <summary>
		/// Export with the given server session and retry policy.
		/// </summary>
		/// <param name="sessionFactory">Creates the session from the options, the endpoint and the model description used in messages.</param>
		internal async Task<string> ExportAsync(RsModelExporterOptions options, IProgress<long> bytesProgress, CancellationToken cancellationToken,
			Func<RsModelExporterOptions, Uri, string, IModelExportSession> sessionFactory, RsExportRetryPolicy retryPolicy)
		{
			var endpoint = ValidateOptions(options);
			cancellationToken.ThrowIfCancellationRequested();
			PrepareDestination(options);
			// All service calls are synchronous WCF calls: run them off the caller's thread.
			return await Task.Run(() => Export(options, endpoint, bytesProgress, cancellationToken, sessionFactory, retryPolicy), CancellationToken.None).ConfigureAwait(false);
		}

		/// <summary>
		/// Checks the options without side effects and returns the ModelService endpoint.
		/// </summary>
		internal static Uri ValidateOptions(RsModelExporterOptions options)
		{
			if (options == null) throw new ArgumentNullException(nameof(options));
			if (string.IsNullOrWhiteSpace(options.ServerHost)) throw new ArgumentException("ServerHost is required", nameof(options.ServerHost));
			if (string.IsNullOrWhiteSpace(options.ModelPipePath)) throw new ArgumentException("ModelPipePath is required", nameof(options.ModelPipePath));
			if (string.IsNullOrWhiteSpace(options.DestinationFile)) throw new ArgumentException("DestinationFile is required", nameof(options.DestinationFile));
			if (string.IsNullOrWhiteSpace(options.RevitVersion)) throw new ArgumentException("RevitVersion is required", nameof(options.RevitVersion));
			if (NormalizeModelPath(options.ModelPipePath).Length == 0)
				throw new ArgumentException("ModelPipePath must name a model, not the root folder", nameof(options.ModelPipePath));
			string destination;
			try
			{
				destination = Path.GetFullPath(options.DestinationFile);
			}
			catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
			{
				throw new ArgumentException($"DestinationFile '{options.DestinationFile}' is not a valid file path: {ex.Message}", nameof(options.DestinationFile), ex);
			}
			var fileName = Path.GetFileName(destination);
			// A ':' is allowed only in the drive of the path (.NET Framework takes "dir:name" for a file name "name").
			if (fileName.Length == 0 || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || destination.IndexOf(':', Path.GetPathRoot(destination).Length) >= 0)
				throw new ArgumentException($"DestinationFile '{options.DestinationFile}' does not end with a valid file name.", nameof(options.DestinationFile));
			return RsModelServiceEndpoint.BuildUri(options.ServerHost, options.RevitVersion);
		}

		/// <summary>
		/// The model path as the ModelService takes it: segments separated by single backslashes, without a leading or trailing separator.
		/// The server rejects (ModelMissingFault) a path with a trailing, leading or doubled separator, while it accepts '/' as a separator;
		/// the REST client (<see cref="RevitServerApi.EncodePath"/>) accepts all of these spellings, so the export does too.
		/// </summary>
		internal static string NormalizeModelPath(string modelPath)
		{
			if (string.IsNullOrWhiteSpace(modelPath)) return string.Empty;
			return string.Join("\\", modelPath.Trim().Split(new[] { '|', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries));
		}

		/// <summary>
		/// Fails before anything is downloaded when the destination cannot be written: a directory in its place, a read-only or open file
		/// that would have to be replaced, a folder that cannot be written. Without it the failure comes only after the whole model has been
		/// downloaded, with the server read lock held all that time. Nothing is changed: the existing file is opened without truncating it.
		/// </summary>
		private static void PrepareDestination(RsModelExporterOptions options)
		{
			var destination = Path.GetFullPath(options.DestinationFile);
			var dir = Path.GetDirectoryName(destination);
			if (Directory.Exists(destination))
				throw new IOException($"Destination '{destination}' is an existing folder, not a file.");
			Directory.CreateDirectory(dir);
			if (File.Exists(destination))
			{
				if (!options.Overwrite)
					throw new IOException("Destination file already exists. Set Overwrite=true to replace.");
				// Replacing the file deletes it: needs write access (not read-only) and no other process holding it open.
				using (new FileStream(destination, FileMode.Open, FileAccess.Write, FileShare.None)) { }
			}
			// Same folder and name pattern as the file MoveToDestination creates.
			var probe = Path.Combine(dir, Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".partial");
			using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
		}

		private static string Export(RsModelExporterOptions options, Uri endpoint, IProgress<long> progress, CancellationToken ct,
			Func<RsModelExporterOptions, Uri, string, IModelExportSession> sessionFactory, RsExportRetryPolicy retryPolicy)
		{
			var serverHost = options.ServerHost.Trim();
			var modelPath = NormalizeModelPath(options.ModelPipePath);
			var where = $"model '{options.ModelPipePath}' at {endpoint}";
			var userName = ExportUserName;

			var workRoot = options.WorkRoot ?? Path.GetTempPath();
			var workDir = Path.Combine(workRoot, WorkDirectoryPrefix + Guid.NewGuid().ToString("N"));
			var dataDir = Path.Combine(workDir, "data");
			Exception failure = null;
			// UnlockData failure after an otherwise successful locked phase: reported once the file is in place.
			Exception unlockFailure = null;
			var unlockFailureThrown = false;
			try
			{
				var session = sessionFactory(options, endpoint, where);
				Directory.CreateDirectory(dataDir);

				object dataFormatVersion;
				try
				{
					using (ct.Register(session.Abort))
						Call("IdentifyModel", where, ct, () => RunUntilCancelled(ct, $"Export of {where} was cancelled during IdentifyModel.", () =>
						{
							session.IdentifyModel(ct);
							return true;
						}));

					var modelMutex = AcquireModelMutex(userName, modelPath, serverHost, where, ct);
					try
					{
						var lockMayBeHeld = false;
						// LockData returned (with any status); false while a LockData request may still be processed by the server.
						var lockDataReturned = false;
						string lockStatus = null;
						Exception lockedPhaseFailure = null;
						try
						{
							using (ct.Register(session.Abort))
							{
								lockStatus = LockWithRetry(() =>
								{
									lockDataReturned = false;
									string status;
									try
									{
										status = Call("LockData", where, ct, () => session.LockData(() => lockMayBeHeld = true, ct));
									}
									catch (InvalidOperationException ex) when (ex.InnerException is FaultException)
									{
										// The server answered with a fault: it did not lock.
										lockMayBeHeld = false;
										throw;
									}
									lockDataReturned = true;
									// Any status but Locked/WasLocked: the server did not lock.
									if (status != "Locked" && status != "WasLocked") lockMayBeHeld = false;
									return status;
								}, retryPolicy.LockAttempts, retryPolicy.LockRetryDelay, ct, out var lockAttempts);
								if (lockStatus != "Locked" && lockStatus != "WasLocked")
									throw new InvalidOperationException(
										$"LockData returned {lockStatus} for {where}{(lockAttempts > 1 ? $" after {lockAttempts} attempts" : string.Empty)}: {ExplainLockStatus(lockStatus)}");

								var listing = Call("GetListOfModelDataFilesWithoutLocking", where, ct, () =>
								{
									var status = session.GetListOfModelDataFiles(ct, out var list);
									return Tuple.Create(status, list);
								});
								if (listing.Item1 != "Success")
									throw new InvalidOperationException($"GetListOfModelDataFilesWithoutLocking returned {listing.Item1} for {where}: {ExplainValidationStatus(listing.Item1)}");
								var files = listing.Item2;
								if (files == null || files.Count == 0)
									throw new InvalidOperationException($"GetListOfModelDataFilesWithoutLocking returned no model data files for {where}.");

								foreach (var file in files)
								{
									ct.ThrowIfCancellationRequested();
									var fileName = Path.GetFileName(file);
									if (string.IsNullOrEmpty(fileName))
										throw new InvalidOperationException($"GetListOfModelDataFilesWithoutLocking returned an invalid file name '{file}' for {where}.");
									var target = Path.Combine(dataDir, fileName);
									Call("DownloadFile " + file, where, ct, () => Download(session, file, target, where, progress, ct));
								}

								dataFormatVersion = Call("ModelDataFormatVersion", where, ct, () => session.ModelDataFormatVersion(ct));
							}
						}
						catch (Exception ex)
						{
							lockedPhaseFailure = ex;
							throw;
						}
						finally
						{
							// Always release the read lock once it may be held (RevitServerTool does the same in a finally block),
							// also after cancellation: the unlock is not cancellable and uses a new channel if the old one was aborted.
							if (lockMayBeHeld)
							{
								var unlockError = ReleaseReadLock(session, where, lockDataReturned, ct.IsCancellationRequested, retryPolicy, out var lockAlreadyReleased);
								if (unlockError != null)
								{
									var description = DescribeUnlockFailure(unlockError, userName);
									if (lockedPhaseFailure != null) lockedPhaseFailure.Data[UnlockErrorDataKey] = description;
									else unlockFailure = new InvalidOperationException(description, unlockError);
								}
								else if (lockAlreadyReleased)
								{
									// Not an error: the file is usually consistent, and failing would turn a finished export into a failure.
									var warning = DescribeLostReadLock(userName, where, lockStatus);
									if (lockedPhaseFailure != null) lockedPhaseFailure.Data[ReadLockLostDataKey] = warning;
									else Trace.TraceWarning("RevitServerNet: " + warning);
								}
							}
						}
					}
					finally
					{
						modelMutex.ReleaseMutex();
						modelMutex.Dispose();
					}
				}
				finally
				{
					session.Close();
				}

				ct.ThrowIfCancellationRequested();
				var rvtPath = Path.Combine(workDir, "model.rvt");
				try
				{
					session.GenerateRvtFile(dataDir, dataFormatVersion, rvtPath);
				}
				catch (Exception ex)
				{
					throw new InvalidOperationException($"Assembling the RVT file from the downloaded data of {where} failed: {ex.Message}", ex);
				}
				if (options.CreateLocal)
				{
					try
					{
						session.MakeCreatedLocal(rvtPath, dataFormatVersion);
					}
					catch (NotSupportedException ex)
					{
						throw new NotSupportedException($"CreateLocal failed for {where}: {ex.Message}", ex);
					}
					catch (Exception ex)
					{
						throw new InvalidOperationException($"CreateLocal failed for {where}: {ex.Message}", ex);
					}
				}
				ct.ThrowIfCancellationRequested();
				MoveToDestination(rvtPath, options.DestinationFile, options.Overwrite);

				if (unlockFailure != null)
				{
					unlockFailureThrown = true;
					var exported = new InvalidOperationException($"The model was exported to '{options.DestinationFile}', but {unlockFailure.Message}", unlockFailure.InnerException);
					// The file is complete: callers can tell this case from a failed export without parsing the message.
					exported.Data[ExportedFileDataKey] = options.DestinationFile;
					exported.Data[UnlockErrorDataKey] = unlockFailure.Message;
					throw exported;
				}
				return options.DestinationFile;
			}
			catch (Exception ex)
			{
				failure = ex;
				if (unlockFailure != null && !unlockFailureThrown)
					ex.Data[UnlockErrorDataKey] = unlockFailure.Message;
				throw;
			}
			finally
			{
				DeleteWorkDirectory(workDir, failure);
				SweepStaleWorkDirectories(workRoot, DateTime.UtcNow);
			}
		}

		/// <summary>
		/// Name of the model's export mutex: "{user}_{model path with ':' instead of backslashes}_{server}", as RevitServerTool
		/// (DataStorageToolClient.CreateRvtFileFromCentralModel) builds it. A name longer than <see cref="MaxModelMutexNameLength"/>
		/// characters is replaced by a hash.
		/// </summary>
		internal static string GetModelMutexName(string userName, string modelPath, string serverHost)
		{
			var name = userName + "_" + modelPath.Replace('\\', ':') + "_" + serverHost;
			if (name.Length <= MaxModelMutexNameLength) return name;
			using (var sha = SHA256.Create())
				return "RevitServerNet_" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(name.ToUpperInvariant()))).Replace("-", string.Empty);
		}

		/// <summary>
		/// The server keeps one read lock per user name, so exports of the same model under the same user name must not overlap:
		/// the first one to finish would release the lock of the others. The export holds a named mutex (<see cref="GetModelMutexName"/>)
		/// from LockData to UnlockData. RevitServerTool builds the same name from its own user name (and takes the mutex earlier, before
		/// IdentifyModel), so exports by this library and by a RevitServerTool that uses the same user name (see <see cref="ExportUserName"/>)
		/// wait for each other in the same Windows session (other sessions have their own mutex namespace). The name is case-sensitive and
		/// uses the server host and model path as given: another spelling of the same model is another mutex.
		/// Must be released on the calling thread (the export runs on one thread).
		/// </summary>
		internal static Mutex AcquireModelMutex(string userName, string modelPath, string serverHost, string where, CancellationToken ct)
		{
			var name = GetModelMutexName(userName, modelPath, serverHost);
			Mutex mutex;
			try
			{
				mutex = new Mutex(false, name);
			}
			catch (UnauthorizedAccessException ex)
			{
				throw new InvalidOperationException(
					$"Another process on this machine is exporting {where} under user '{userName}', and its lock (mutex '{name}') cannot be opened by this account; try again when it has finished.", ex);
			}
			try
			{
				var acquired = false;
				try
				{
					acquired = !ct.CanBeCanceled
						? mutex.WaitOne()
						: WaitHandle.WaitAny(new[] { mutex, ct.WaitHandle }) == 0;
				}
				catch (AbandonedMutexException ex) when (ex.MutexIndex <= 0)
				{
					// The previous owner ended without releasing it; this thread owns the mutex now.
					acquired = true;
				}
				if (!acquired)
				{
					ct.ThrowIfCancellationRequested();
					throw new InvalidOperationException($"Waiting for the export mutex '{name}' ended without owning it.");
				}
				return mutex;
			}
			catch
			{
				mutex.Dispose();
				throw;
			}
		}

		/// <summary>
		/// Runs <paramref name="call"/> on its own thread-pool task and returns as soon as it completes or <paramref name="ct"/> is cancelled.
		/// For the first call of a session: WCF connects to the server inside it, and aborting the channel does not end a connect that is
		/// still pending (an unreachable host: the call ends only when the OS gives up on the connection, about 20 s), so waiting on the
		/// call itself would leave a cancelled export hanging. After a cancellation the call is left to end on its own (its channel was
		/// aborted by the caller); nothing is lost, as the server has not been asked for anything yet.
		/// </summary>
		private static T RunUntilCancelled<T>(CancellationToken ct, string cancelledMessage, Func<T> call)
		{
			if (!ct.CanBeCanceled) return call();
			var task = Task.Run(call, CancellationToken.None);
			WaitHandle.WaitAny(new[] { ((IAsyncResult)task).AsyncWaitHandle, ct.WaitHandle });
			if (!task.IsCompleted)
			{
				// Observe the orphaned call's failure: it is expected (the channel was aborted) and nobody awaits it.
				task.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
				throw new OperationCanceledException(cancelledMessage, ct);
			}
			return task.GetAwaiter().GetResult();
		}

		/// <summary>
		/// Runs one service call: cancellation-aware, WCF errors wrapped in <see cref="InvalidOperationException"/> with the operation and model.
		/// </summary>
		private static T Call<T>(string operation, string where, CancellationToken ct, Func<T> call)
		{
			ct.ThrowIfCancellationRequested();
			try
			{
				return call();
			}
			catch (Exception ex) when (ct.IsCancellationRequested && !(ex is OperationCanceledException))
			{
				// The channel was aborted by the cancellation callback.
				throw new OperationCanceledException($"Export of {where} was cancelled during {operation}.", ex, ct);
			}
			catch (EndpointNotFoundException ex)
			{
				throw new InvalidOperationException(
					$"{operation} failed for {where}: no ModelService endpoint responded ({ex.Message}). Check that the server is reachable on its net.tcp port (808 by default) "
					+ "and that RevitVersion is the year of this Revit Server.", ex);
			}
			catch (FaultException ex)
			{
				var detailType = ex.GetType().IsGenericType ? ex.GetType().GetGenericArguments()[0].Name : null;
				throw new InvalidOperationException($"{operation} failed for {where}: server fault{(detailType == null ? string.Empty : " " + detailType)}: {ex.Message}", ex);
			}
			catch (Exception ex) when (ex is CommunicationException || ex is TimeoutException)
			{
				throw new InvalidOperationException($"{operation} failed for {where}: {ex.Message}", ex);
			}
		}

		/// <summary>
		/// Runs <paramref name="lockData"/> up to <paramref name="maxAttempts"/> times while the server answers Busy or with a lock contention
		/// fault (DataLockContentionFault, PermissionLockContentionFault), waiting <paramref name="delay"/> before each retry; the wait is
		/// cancellable. RevitServerTool retries Busy the same way (5 attempts, 10 s apart). Returns the last status; a contention fault of the
		/// last attempt is thrown with the number of attempts.
		/// </summary>
		internal static string LockWithRetry(Func<string> lockData, int maxAttempts, TimeSpan delay, CancellationToken ct, out int attempts)
		{
			for (attempts = 1; ; attempts++)
			{
				try
				{
					var status = lockData();
					if (status != "Busy" || attempts >= maxAttempts) return status;
				}
				catch (InvalidOperationException ex) when (IsLockContentionFault(ex))
				{
					if (attempts >= maxAttempts)
						throw new InvalidOperationException($"{ex.Message} (after {attempts} attempts)", ex.InnerException);
				}
				if (delay > TimeSpan.Zero) ct.WaitHandle.WaitOne(delay);
				ct.ThrowIfCancellationRequested();
			}
		}

		private static bool IsLockContentionFault(InvalidOperationException ex)
		{
			var faultType = (ex.InnerException as FaultException)?.GetType();
			if (faultType == null || !faultType.IsGenericType) return false;
			var detail = faultType.GetGenericArguments()[0].Name;
			return detail == "DataLockContentionFault" || detail == "PermissionLockContentionFault";
		}

		/// <summary>
		/// Downloads one model data file. When the copy fails, the connection is aborted before the response is disposed, and the copy's own
		/// exception is thrown: disposing a partly read streamed reply makes WCF read the rest of it (for up to the close timeout), and the
		/// exception of that would replace the original one. A failure of the dispose is added to the exception's
		/// <see cref="DownloadCloseErrorDataKey"/>.
		/// </summary>
		private static string Download(IModelExportSession session, string file, string target, string where, IProgress<long> progress, CancellationToken ct)
		{
			var response = session.DownloadFile(file, ct, out var stream);
			try
			{
				if (stream == null) throw new InvalidOperationException($"DownloadFile returned no stream for '{file}' of {where}.");
				CopyToFile(stream, target, progress, ct);
			}
			catch (Exception copyError)
			{
				session.Abort();
				try
				{
					response.Dispose();
				}
				catch (Exception disposeError)
				{
					copyError.Data[DownloadCloseErrorDataKey] = $"{disposeError.GetType().Name}: {disposeError.Message}";
				}
				throw;
			}
			response.Dispose();
			return target;
		}

		/// <summary>
		/// UnlockData, not cancellable. Retried up to <see cref="RsExportRetryPolicy.UnlockAttempts"/> times,
		/// <see cref="RsExportRetryPolicy.UnlockRetryDelay"/> apart, while it fails with a communication error (RevitServerTool retries
		/// UnlockData too); a timeout is not retried, so that a server that stopped answering does not hold the export for several timeouts.
		/// When LockData did not return (aborted or timed out) and UnlockData finds no lock, the server may still apply that LockData:
		/// UnlockData is sent once more after <see cref="RsExportRetryPolicy.InterruptedLockDelay"/>.
		/// Returns the failure (exception or unexpected status) instead of throwing, so that it never replaces the export's own result.
		/// </summary>
		/// <param name="lockAlreadyReleased">
		/// True when LockData had returned and the first UnlockData answered WasNotLocked: the read lock was released before (another export
		/// under the same user name) or expired.
		/// </param>
		private static Exception ReleaseReadLock(IModelExportSession session, string where, bool lockDataReturned, bool cancelled, RsExportRetryPolicy retryPolicy, out bool lockAlreadyReleased)
		{
			lockAlreadyReleased = false;
			string status;
			var attempt = 1;
			while (true)
			{
				try
				{
					status = session.UnlockData(cancelled);
					break;
				}
				catch (Exception ex)
				{
					if (!(ex is CommunicationException) || attempt >= retryPolicy.UnlockAttempts) return ex;
				}
				attempt++;
				if (retryPolicy.UnlockRetryDelay > TimeSpan.Zero) Thread.Sleep(retryPolicy.UnlockRetryDelay);
			}

			if (status == "Unlocked") return null;
			if (status != "WasNotLocked") return new InvalidOperationException($"UnlockData returned {status} for {where}.");
			if (lockDataReturned)
			{
				// After a failed attempt, the lock may have been released by that attempt (its reply was lost).
				lockAlreadyReleased = attempt == 1;
				return null;
			}

			// LockData was interrupted: the server may apply it after this UnlockData. Ask once more when that is over.
			if (retryPolicy.InterruptedLockDelay > TimeSpan.Zero) Thread.Sleep(retryPolicy.InterruptedLockDelay);
			try
			{
				status = session.UnlockData(cancelled);
			}
			catch (Exception ex)
			{
				return ex;
			}
			return status == "Unlocked" || status == "WasNotLocked" ? null : new InvalidOperationException($"UnlockData returned {status} for {where}.");
		}

		private static string DescribeUnlockFailure(Exception unlockError, string userName)
		{
			return $"releasing the server read lock failed (UnlockData: {unlockError.GetType().Name}: {unlockError.Message}). "
				+ $"The read lock of user '{userName}' remains on the server until it expires (12 h) or until the next export of this model from this machine releases it.";
		}

		private static string DescribeLostReadLock(string userName, string where, string lockStatus)
		{
			return $"the read lock of user '{userName}' on {where} was already released when UnlockData ran (LockData returned {lockStatus}). "
				+ "Another export under the same user name (from another Windows session, or with another spelling of ServerHost or ModelPipePath) "
				+ "may have released it during the download, so the model may have changed while it was downloaded; or the lock expired (12 h).";
		}

		private static string ExplainLockStatus(string status)
		{
			switch (status)
			{
				case "Busy": return "the model is locked by another operation (for example a user synchronizing with central); try again later.";
				case "Missing": return "the model's lock data is missing on the server.";
				case "GaveUp": return "the server gave up acquiring the lock.";
				default: return "the server did not grant the read lock.";
			}
		}

		private static string ExplainValidationStatus(string status)
		{
			switch (status)
			{
				case "ModelDoesNotExist": return "the model does not exist on the server.";
				case "ModelInUse": return "the model is in use; try again later.";
				case "ModelCorrupt": return "the model is corrupt on the server.";
				default: return "the server could not list the model data files.";
			}
		}

		/// <summary>
		/// Copies a downloaded stream to a new file in 80 KB blocks; reports the bytes written so far for this file after every block.
		/// </summary>
		private static void CopyToFile(Stream source, string targetPath, IProgress<long> progress, CancellationToken ct)
		{
			var buffer = new byte[81920];
			long total = 0;
			using (var file = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				while (true)
				{
					ct.ThrowIfCancellationRequested();
					int filled = 0, read;
					while (filled < buffer.Length && (read = source.Read(buffer, filled, buffer.Length - filled)) > 0)
						filled += read;
					if (filled == 0) break;
					file.Write(buffer, 0, filled);
					total += filled;
					progress?.Report(total);
					if (filled < buffer.Length) break;
				}
			}
		}

		/// <summary>
		/// Moves the assembled file to <paramref name="destination"/>. It is first moved under a temporary name in the destination folder
		/// (when the temp folder is on another volume, this is where the file is copied), then the previous destination file is replaced by
		/// a rename in the same folder: a failure during the copy leaves the previous file untouched, and no partial file under the final name.
		/// The temporary file is deleted on failure (a failure to delete it is added to <see cref="PartialFileCleanupErrorDataKey"/>).
		/// </summary>
		internal static void MoveToDestination(string source, string destination, bool overwrite)
		{
			var dir = Path.GetDirectoryName(Path.GetFullPath(destination));
			Directory.CreateDirectory(dir);
			var partial = Path.Combine(dir, Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".partial");
			try
			{
				File.Move(source, partial);
				if (File.Exists(destination))
				{
					if (!overwrite) throw new IOException("Destination file already exists. Set Overwrite=true to replace.");
					File.Delete(destination);
				}
				File.Move(partial, destination);
			}
			catch (Exception ex)
			{
				try
				{
					if (File.Exists(partial)) File.Delete(partial);
				}
				catch (Exception cleanupError) when (cleanupError is IOException || cleanupError is UnauthorizedAccessException)
				{
					ex.Data[PartialFileCleanupErrorDataKey] = $"Could not delete '{partial}': {cleanupError.Message}";
				}
				throw;
			}
		}

		/// <summary>
		/// Deletes the temp folder, retrying a few times: a file that was just written can stay in a delete-pending state for a
		/// moment (for example while an antivirus scans it), and the folder cannot be removed until it is gone. A final failure
		/// does not replace the export's result: it is attached to the exception that ended the export
		/// (<see cref="TempCleanupErrorDataKey"/>), or written to <see cref="Trace"/> after a successful export.
		/// A folder left behind is removed by a later export (<see cref="SweepStaleWorkDirectories"/>).
		/// </summary>
		internal static void DeleteWorkDirectory(string workDir, Exception failure)
		{
			const int attempts = 5;
			for (var attempt = 1; Directory.Exists(workDir); attempt++)
			{
				try
				{
					Directory.Delete(workDir, true);
					return;
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
				{
					if (attempt < attempts)
					{
						Thread.Sleep(100 * attempt);
						continue;
					}
					var message = $"Could not delete the temp folder '{workDir}': {ex.Message}";
					if (failure != null) failure.Data[TempCleanupErrorDataKey] = message;
					else Trace.TraceWarning("RevitServerNet: " + message);
					return;
				}
			}
		}

		/// <summary>
		/// Deletes work folders directly under <paramref name="root"/> whose last write is older than <see cref="StaleWorkDirectoryAge"/>:
		/// left by an export whose cleanup failed or whose process was killed (also the "RevitServerNet_ModelData_{guid}" folders of
		/// versions before 1.3.0). The age keeps the folders of exports that are still running in other processes. Best effort: a failure
		/// is written with <see cref="Trace.TraceWarning(string)"/> and never affects the export.
		/// </summary>
		internal static void SweepStaleWorkDirectories(string root, DateTime utcNow)
		{
			List<string> directories;
			try
			{
				directories = Directory.EnumerateDirectories(root, WorkDirectoryPrefix + "*", SearchOption.TopDirectoryOnly).ToList();
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				Trace.TraceWarning($"RevitServerNet: could not look for old export temp folders in '{root}': {ex.Message}");
				return;
			}
			foreach (var dir in directories)
			{
				if (!WorkDirectoryName.IsMatch(Path.GetFileName(dir))) continue;
				try
				{
					if (utcNow - Directory.GetLastWriteTimeUtc(dir) <= StaleWorkDirectoryAge) continue;
					Directory.Delete(dir, true);
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
				{
					Trace.TraceWarning($"RevitServerNet: could not delete the old export temp folder '{dir}': {ex.Message}");
				}
			}
		}

		/// <summary>
		/// The export's session with the Revit Server ModelService, through the loaded Autodesk client assemblies.
		/// </summary>
		private sealed class ModelServiceSession : IModelExportSession
		{
			private readonly RsModelServiceApi _api;
			private readonly ModelServiceConnection _connection;
			private readonly string _serverHost;
			private readonly string _modelPath;
			private readonly string _where;
			private readonly string _userName = ExportUserName;
			private readonly string _machineName = Environment.MachineName;
			private readonly object _modelLocation;
			private object _identity;
			private Guid _identityGuid;
			private object _creationDate;

			public ModelServiceSession(RsModelExporterOptions options, Uri endpoint, string where)
			{
				var assemblies = RsAssemblyLoader.Load(options.RevitVersion, options.AssembliesPath);
				_api = RsModelServiceApi.For(assemblies);
				_serverHost = options.ServerHost.Trim();
				_modelPath = NormalizeModelPath(options.ModelPipePath);
				_where = where;
				_modelLocation = _api.NewModelLocation(_serverHost, _modelPath);
				_connection = new ModelServiceConnection(_api, endpoint);
			}

			public void IdentifyModel(CancellationToken ct)
			{
				var identity = _api.IdentifyModel(_connection.GetChannel(ct), _api.NewServiceSessionToken(_userName, _machineName), _modelPath);
				if (!_api.IsValidIdentity(identity))
					throw new InvalidOperationException($"IdentifyModel returned no valid model identity for {_where}.");
				_identity = identity;
				_identityGuid = _api.GetIdentityGuid(identity);
			}

			public string LockData(Action lockMayBeHeld, CancellationToken ct)
			{
				var channel = _connection.GetChannel(ct);
				var token = NewToken();
				lockMayBeHeld();
				var status = _api.LockData(channel, token, out var creationDate);
				_creationDate = creationDate;
				return status;
			}

			public string GetListOfModelDataFiles(CancellationToken ct, out List<string> files)
			{
				return _api.GetListOfModelDataFiles(_connection.GetChannel(ct), NewToken(), out files);
			}

			public IDisposable DownloadFile(string file, CancellationToken ct, out Stream stream)
			{
				var request = _api.NewDownloadRequest(NewToken(), _creationDate, Path.Combine(_identityGuid.ToString(), file));
				return _api.DownloadFile(_connection.GetChannel(ct), request, out stream);
			}

			public object ModelDataFormatVersion(CancellationToken ct)
			{
				return _api.ModelDataFormatVersion(_connection.GetChannel(ct), NewToken());
			}

			public string UnlockData(bool cancelled)
			{
				var channel = _connection.GetChannel();
				if (cancelled) ((IContextChannel)channel).OperationTimeout = RsModelServiceEndpoint.CancelledUnlockTimeout;
				return _api.UnlockData(channel, NewToken());
			}

			public void Abort() => _connection.Abort();

			public void Close() => _connection.Dispose();

			public void GenerateRvtFile(string dataDirectory, object dataFormatVersion, string rvtPath)
			{
				_api.GenerateRvtFile(dataDirectory, dataFormatVersion, rvtPath);
			}

			public void MakeCreatedLocal(string rvtPath, object dataFormatVersion)
			{
				_api.MakeCreatedLocal(rvtPath, dataFormatVersion, _identity, "RSN://" + _serverHost + "/" + _modelPath.Replace('\\', '/'));
			}

			/// <summary>
			/// New ServiceModelSessionToken for every call, as RevitServerTool does.
			/// </summary>
			private object NewToken() => _api.NewModelSessionToken(_identity, _userName, _machineName, _modelLocation);
		}

		/// <summary>
		/// One ChannelFactory and channel for IModelService. A new pair is created when the channel is missing or no longer open
		/// (after <see cref="Abort"/> or a communication failure).
		/// </summary>
		private sealed class ModelServiceConnection : IDisposable
		{
			private readonly object _gate = new object();
			private readonly RsModelServiceApi _api;
			private readonly Uri _endpoint;
			private ChannelFactory _factory;
			private ICommunicationObject _channel;

			public ModelServiceConnection(RsModelServiceApi api, Uri endpoint)
			{
				_api = api;
				_endpoint = endpoint;
			}

			/// <summary>
			/// <see cref="GetChannel()"/>, then throws when <paramref name="ct"/> is cancelled. A cancellation whose abort ran before the
			/// channel was created would otherwise leave the call on a new channel that nothing aborts; an abort that runs after this
			/// check reaches the new channel.
			/// </summary>
			public object GetChannel(CancellationToken ct)
			{
				// Also stops a call that was abandoned after the cancellation (see RunUntilCancelled) before it opens a new channel.
				ct.ThrowIfCancellationRequested();
				var channel = GetChannel();
				if (ct.IsCancellationRequested)
				{
					Abort();
					ct.ThrowIfCancellationRequested();
				}
				return channel;
			}

			public object GetChannel()
			{
				ICommunicationObject stale;
				lock (_gate)
				{
					if (_channel != null && _channel.State == CommunicationState.Opened) return _channel;
					stale = TakeAll(out var staleFactory);
					staleFactory?.Abort();
				}
				stale?.Abort();
				var factoryType = typeof(ChannelFactory<>).MakeGenericType(_api.ModelServiceType);
				ChannelFactory factory;
				try
				{
					factory = (ChannelFactory)CreateInstance(factoryType, RsModelServiceEndpoint.CreateBinding(), new EndpointAddress(_endpoint));
				}
				catch (Exception ex)
				{
					// For example a set built for .NET 8 (Revit 2025 and later) loaded on .NET Framework.
					throw new InvalidOperationException(
						$"The WCF client for IModelService could not be created from the Revit Server client assemblies in '{_api.Directory}'"
						+ $"{RsAssemblyLoader.DescribeSkipped(_api.SkippedCandidates)}: {ex.Message}", ex);
				}
				// As Autodesk's own client: no limit on the number of serialized objects per message.
				foreach (var operation in factory.Endpoint.Contract.Operations)
				{
					var serializer = operation.Behaviors.Find<DataContractSerializerOperationBehavior>();
					if (serializer != null) serializer.MaxItemsInObjectGraph = int.MaxValue;
				}
				// The gate is not held while connecting: Abort and Dispose (the cancellation) must not wait for a pending connect.
				// Each object is published before it is opened, so that Abort reaches it.
				lock (_gate) _factory = factory;
				factory.Open();
				var channel = (ICommunicationObject)InvokeMethod(factoryType.GetMethod("CreateChannel", Type.EmptyTypes), factory);
				bool aborted;
				lock (_gate)
				{
					aborted = _factory != factory;
					if (!aborted) _channel = channel;
				}
				if (aborted)
				{
					channel.Abort();
					throw new CommunicationObjectAbortedException("The connection was aborted while it was being created.");
				}
				channel.Open();
				return channel;
			}

			/// <summary>
			/// Aborts the channel and the factory (cancellation): a blocked call fails at once. Never waits for a connect in progress.
			/// </summary>
			public void Abort()
			{
				ICommunicationObject channel, factory;
				lock (_gate) { channel = TakeAll(out var f); factory = f; }
				channel?.Abort();
				factory?.Abort();
			}

			public void Dispose()
			{
				ICommunicationObject channel, factory;
				lock (_gate) { channel = TakeAll(out var f); factory = f; }
				CloseOrAbort(channel);
				CloseOrAbort(factory);
			}

			// Under the gate: takes the references, so that they are aborted or closed outside of it.
			private ICommunicationObject TakeAll(out ICommunicationObject factory)
			{
				var channel = _channel;
				factory = _factory;
				_channel = null;
				_factory = null;
				return channel;
			}

			private static void CloseOrAbort(ICommunicationObject obj)
			{
				if (obj == null) return;
				if (obj.State != CommunicationState.Opened)
				{
					obj.Abort();
					return;
				}
				try
				{
					obj.Close();
				}
				catch (Exception ex) when (ex is CommunicationException || ex is TimeoutException)
				{
					// The export's result is already decided; a channel that cannot be closed cleanly is aborted.
					obj.Abort();
				}
			}

			private static object CreateInstance(Type type, params object[] args)
			{
				try
				{
					return Activator.CreateInstance(type, args);
				}
				catch (TargetInvocationException ex) when (ex.InnerException != null)
				{
					System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
					throw;
				}
			}

			private static object InvokeMethod(MethodInfo method, object target)
			{
				try
				{
					return method.Invoke(target, null);
				}
				catch (TargetInvocationException ex) when (ex.InnerException != null)
				{
					System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
					throw;
				}
			}
		}
	}
}
