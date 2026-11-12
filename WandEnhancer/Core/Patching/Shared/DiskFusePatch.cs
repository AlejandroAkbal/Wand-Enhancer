using System;
using System.Globalization;
using System.IO;

namespace WandEnhancer.Core.Patching.Shared
{
    /// <summary>Clears or restores the ASAR integrity fuse in an executable on disk.</summary>
    internal static class DiskFusePatch
    {
        /// <summary>
        /// Sidecar that records the fuse byte the build shipped with. Writing back a fixed value
        /// would flip unrelated fuses: Wand ships fuse 6 disabled ('0') and Electron aborts with
        /// "Error loading V8 startup snapshot file" when that byte is set to '1'.
        /// </summary>
        private const string SidecarSuffix = ".fuse-original";

        /// <summary>
        /// Marks the ASAR integrity fuse as removed so a repacked app.asar is accepted.
        /// Returns false and logs when the wire cannot be found or written.
        /// </summary>
        public static bool Apply(string exePath, Action<string, ELogType> log)
        {
            return SetState(exePath, ElectronFuseWire.StateRemoved, true, "cleared", log);
        }

        /// <summary>
        /// Puts the fuse back to the value the build shipped with, so a later patch starts from a
        /// pristine image instead of stacking one removal on another.
        /// </summary>
        public static bool Remove(string exePath, Action<string, ELogType> log)
        {
            return SetState(exePath, ElectronFuseWire.StateEnabled, false, "restored", log);
        }

        private static bool SetState(string exePath, byte fallback, bool captureOriginal, string verb, Action<string, ELogType> log)
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                log?.Invoke($"[ENHANCER] Cannot update the fuse: {exePath} does not exist.", ELogType.Error);
                return false;
            }

            string sidecar = exePath + SidecarSuffix;
            bool restored = false;
            try
            {
                byte target = fallback;
                if (!captureOriginal)
                {
                    if (!File.Exists(sidecar))
                    {
                        log?.Invoke($"[ENHANCER] No recorded fuse state for {Path.GetFileName(exePath)}; leaving it untouched.", ELogType.Info);
                        return true;
                    }

                    string raw = File.ReadAllText(sidecar).Trim();
                    if (!byte.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out target))
                    {
                        log?.Invoke("[ENHANCER] The recorded fuse state is unreadable.", ELogType.Error);
                        return false;
                    }
                }

                var attributes = File.GetAttributes(exePath);
                bool wasReadOnly = (attributes & FileAttributes.ReadOnly) != 0;
                if (wasReadOnly)
                {
                    File.SetAttributes(exePath, attributes & ~FileAttributes.ReadOnly);
                }

                try
                {
                    using (var stream = new FileStream(exePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                    {
                        long sentinel = ElectronFuseWire.FindSentinelOffset(stream);
                        if (sentinel < 0)
                        {
                            log?.Invoke("[ENHANCER] The Electron fuse sentinel was not found.", ELogType.Error);
                            return false;
                        }

                        long position = sentinel + ElectronFuseWire.StateFromSentinel;
                        stream.Position = position;
                        int current = stream.ReadByte();
                        if (current < 0)
                        {
                            log?.Invoke("[ENHANCER] The Electron fuse wire is invalid.", ELogType.Error);
                            return false;
                        }

                        // Record what the build shipped before the first edit so Remove can undo it.
                        if (captureOriginal && !File.Exists(sidecar))
                        {
                            File.WriteAllText(sidecar, ((byte)current).ToString("X2"));
                        }

                        if (current == target)
                        {
                            restored = !captureOriginal;
                            log?.Invoke($"[ENHANCER] ASAR integrity fuse already {verb}.", ELogType.Info);
                            return true;
                        }

                        stream.Position = position;
                        stream.WriteByte(target);
                        stream.Flush(true);
                        restored = !captureOriginal;
                        log?.Invoke($"[ENHANCER] ASAR integrity fuse {verb}.", ELogType.Info);
                        return true;
                    }
                }
                finally
                {
                    if (wasReadOnly)
                    {
                        File.SetAttributes(exePath, File.GetAttributes(exePath) | FileAttributes.ReadOnly);
                    }
                }
            }
            catch (Exception e)
            {
                log?.Invoke($"[ENHANCER] Could not update the ASAR integrity fuse: {e.Message}", ELogType.Error);
                return false;
            }
            finally
            {
                if (restored && File.Exists(sidecar))
                {
                    try { File.Delete(sidecar); } catch { /* best effort */ }
                }
            }
        }
    }
}
