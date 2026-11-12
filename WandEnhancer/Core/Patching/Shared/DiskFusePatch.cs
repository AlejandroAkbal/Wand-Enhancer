using System;
using System.IO;

namespace WandEnhancer.Core.Patching.Shared
{
    /// <summary>Clears or restores the ASAR integrity fuse in an executable on disk.</summary>
    internal static class DiskFusePatch
    {
        /// <summary>
        /// Marks the ASAR integrity fuse as removed so a repacked app.asar is accepted.
        /// Returns false and logs when the wire cannot be found or written.
        /// </summary>
        public static bool Apply(string exePath, Action<string, ELogType> log)
        {
            return SetState(exePath, ElectronFuseWire.StateRemoved, "cleared", log);
        }

        /// <summary>
        /// Puts the fuse back to the state a shipped executable carries, so a later patch
        /// starts from a pristine image instead of stacking one removal on another.
        /// </summary>
        public static bool Remove(string exePath, Action<string, ELogType> log)
        {
            return SetState(exePath, ElectronFuseWire.StateEnabled, "restored", log);
        }

        private static bool SetState(string exePath, byte state, string verb, Action<string, ELogType> log)
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                log?.Invoke($"[ENHANCER] Cannot update the fuse: {exePath} does not exist.", ELogType.Error);
                return false;
            }

            try
            {
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

                        if (current == state)
                        {
                            log?.Invoke($"[ENHANCER] ASAR integrity fuse already {verb}.", ELogType.Info);
                            return true;
                        }

                        stream.Position = position;
                        stream.WriteByte(state);
                        stream.Flush(true);
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
        }
    }
}
