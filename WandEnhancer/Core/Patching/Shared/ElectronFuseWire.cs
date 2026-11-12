using System;
using System.IO;
using System.Text;

namespace WandEnhancer.Core.Patching.Shared
{
    /// <summary>
    /// The Electron fuse wire embedded in every Electron executable:
    /// a 32-byte sentinel, a little-endian uint32 version, then one byte per fuse
    /// in the order declared by Electron's fuse table. '1' = enabled, '0' = disabled,
    /// 'r' = removed (the check always passes).
    /// </summary>
    internal static class ElectronFuseWire
    {
        public const int SentinelLength = 32;

        /// <summary>
        /// Electron writes a one-byte wire version and a one-byte fuse count before the fuses,
        /// not a uint32 version. Wand 12.71 ships <c>01 09</c> followed by nine state bytes.
        /// </summary>
        public const int HeaderLength = 2;

        /// <summary>Offset of the first fuse byte, counted from the sentinel.</summary>
        public const int FuseOffset = SentinelLength + HeaderLength;

        /// <summary>Wire layout version this code understands.</summary>
        public const byte WireVersion = 1;

        /// <summary>
        /// Position of <c>EnableEmbeddedAsarIntegrityValidation</c> in Electron's fuse table:
        /// RunAsNode, EnableCookieEncryption, EnableNodeOptionsEnvironmentVariable,
        /// EnableNodeCliInspectArguments, EnableEmbeddedAsarIntegrityValidation.
        /// </summary>
        public const int AsarIntegrityFuseIndex = 4;

        public const byte StateEnabled = (byte)'1';
        public const byte StateDisabled = (byte)'0';
        public const byte StateRemoved = (byte)'r';

        public static readonly byte[] Sentinel =
            Encoding.ASCII.GetBytes("dL7pKGdnNz796PbbjQWNKmHXBZaB9tsX");

        /// <summary>Distance from the sentinel to the ASAR integrity fuse byte.</summary>
        public static int StateFromSentinel
        {
            get { return FuseOffset + AsarIntegrityFuseIndex; }
        }

        /// <summary>
        /// How much of the wire to read when validating it. Covers the sentinel, the version
        /// and the fuses Electron ships today with room to spare.
        /// </summary>
        public static int MatchLength
        {
            get { return FuseOffset + 32; }
        }

        /// <summary>Number of fuses declared by the wire, or -1 when the block is not a fuse wire.</summary>
        public static int FuseCount(byte[] block, int offset)
        {
            if (block == null || offset < 0 || block.Length - offset < FuseOffset)
            {
                return -1;
            }

            return block[offset + SentinelLength];
        }

        /// <summary>True when the sentinel sits at <paramref name="offset"/> in the block.</summary>
        public static bool BlockLooksValid(byte[] block, int offset)
        {
            if (block == null || offset < 0 || block.Length - offset < SentinelLength)
            {
                return false;
            }

            for (int i = 0; i < SentinelLength; i++)
            {
                if (block[offset + i] != Sentinel[i])
                {
                    return false;
                }
            }

            if (block[offset + SentinelLength] != WireVersion)
            {
                return false;
            }

            int count = block[offset + SentinelLength + 1];
            return count > 0 && count <= 32;
        }

        /// <summary>File offset of the sentinel, or -1 when the executable has no fuse wire.</summary>
        public static long FindSentinelOffset(Stream stream)
        {
            if (stream == null || !stream.CanRead)
            {
                return -1;
            }

            const int chunkSize = 1 << 20;
            var buffer = new byte[chunkSize + SentinelLength];
            long position = 0;
            int carry = 0;

            while (true)
            {
                stream.Position = position;
                int read = stream.Read(buffer, carry, chunkSize);
                if (read <= 0)
                {
                    return -1;
                }

                int available = carry + read;
                for (int i = 0; i <= available - SentinelLength; i++)
                {
                    bool match = true;
                    for (int j = 0; j < SentinelLength; j++)
                    {
                        if (buffer[i + j] != Sentinel[j])
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match)
                    {
                        return position - carry + i;
                    }
                }

                // Keep the tail so a sentinel split across two chunks is still found.
                carry = Math.Min(SentinelLength - 1, available);
                Array.Copy(buffer, available - carry, buffer, 0, carry);
                position += read;
            }
        }

        /// <summary>RVA of the ASAR integrity fuse byte, or -1 when it cannot be located.</summary>
        public static long FindStateRva(string exePath)
        {
            using (var stream = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long sentinel = FindSentinelOffset(stream);
                if (sentinel < 0)
                {
                    return -1;
                }

                return PeImage.FileOffsetToRva(stream, sentinel + StateFromSentinel);
            }
        }
    }
}
