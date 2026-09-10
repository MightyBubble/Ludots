using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Ludots.Core.Navigation.NavMesh
{
    /// <summary>
    /// One tile to persist through the authoritative publish path. Identity is carried
    /// explicitly (layer/profile/coordinate) and cross-checked against the tile payload;
    /// the publisher derives the official artifact path from the context identity and
    /// <see cref="NavAssetPaths"/> — callers never hand in a path of their own.
    /// </summary>
    public sealed class NavArtifactPublishEntry
    {
        public NavTile Tile { get; set; } = null!;

        public int Layer { get; set; }

        public string ProfileId { get; set; } = string.Empty;

        public int ChunkX { get; set; }

        public int ChunkY { get; set; }
    }

    /// <summary>
    /// Authoritative .ntil + manifest publisher shared by every bake front end (CLI, Editor
    /// Bridge, acceptance tests).
    ///
    /// Contracts:
    ///  - the whole batch is validated up front (identity, duplicates, prior manifest scope,
    ///    path derivation collisions) before any file is touched;
    ///  - all tiles are staged to temp files first; every manifest checksum is read back from
    ///    the staged file on disk — never from a second in-memory serialization;
    ///  - the new manifest content is fully assembled and structure-validated before anything
    ///    is renamed into place;
    ///  - publication swaps tiles then the manifest, each with backup/rollback: if anything
    ///    fails mid-publish the previous batch is restored, so a reader never sees an old
    ///    manifest paired with partially new tiles;
    ///  - artifact paths are derived solely from (root + mapId + boardId + layer/profile/coord)
    ///    through NavAssetPaths, so a board A tile can never be written under a board B path;
    ///  - a dirty/partial bake (merge) requires a valid prior manifest whose map/board scope and
    ///    sourceRevision match this bake; otherwise it fails with an explicit full-rebake action
    ///    instead of stamping tiles that this bake never produced with its source.
    /// </summary>
    public static class NavArtifactPublisher
    {
        public sealed class Context
        {
            /// <summary>Absolute directory the official assets/Data/Nav tree lives under.</summary>
            public string RootDir { get; set; } = string.Empty;

            public string MapId { get; set; } = string.Empty;

            public string BoardId { get; set; } = string.Empty;

            /// <summary>Fingerprint of the actual bake source input (file hash, not a label).</summary>
            public string SourceRevision { get; set; } = string.Empty;

            public string Algorithm { get; set; } = string.Empty;

            public string Mode { get; set; } = string.Empty;

            public uint TileVersion { get; set; }

            public string WrittenUtc { get; set; } = string.Empty;
        }

        private sealed class StagedTile
        {
            public NavTileManifestEntry ManifestEntry { get; set; } = null!;

            public string TempFile { get; set; } = string.Empty;

            public string FinalFile { get; set; } = string.Empty;
        }

        /// <summary>
        /// Publishes a set of tiles plus the board manifest.
        /// </summary>
        /// <param name="replaceWholeManifest">True only when this bake produced the complete
        /// board set. False (merge) requires an existing prior manifest with the same
        /// map/board scope and source revision.</param>
        public static NavTileManifest Publish(Context ctx, IReadOnlyList<NavArtifactPublishEntry> entries, bool replaceWholeManifest)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            ValidateContext(ctx);

            // 1) Whole-batch validation: identity, duplicates, path-collisions and, for a merge,
            //    the prior manifest scope. Nothing on disk is touched before this passes.
            string manifestFinal = ResolveManifestPath(ctx);
            var staged = new List<StagedTile>(entries.Count);
            var seenIdentity = new HashSet<string>(StringComparer.Ordinal);
            var seenPhysicalPath = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < entries.Count; i++)
            {
                NavArtifactPublishEntry e = entries[i];
                ValidateEntry(ctx, e);

                string identity = NavTileManifest.ManifestEntryKey(new NavTileManifestEntry
                {
                    Layer = e.Layer,
                    ProfileId = e.ProfileId,
                    ChunkX = e.ChunkX,
                    ChunkY = e.ChunkY
                });
                if (!seenIdentity.Add(identity))
                {
                    throw new InvalidOperationException(
                        $"NavArtifactPublish for map '{ctx.MapId}' board '{ctx.BoardId}' contains duplicate tile identity '{identity}' in one batch; identities must be unique.");
                }

                string physical = ResolveTilePath(ctx, e);
                if (!seenPhysicalPath.Add(physical))
                {
                    throw new InvalidOperationException(
                        $"NavArtifactPublish for map '{ctx.MapId}' board '{ctx.BoardId}' maps two distinct identities onto the same artifact path '{physical}'; profile/board path segments must not collide.");
                }

                staged.Add(new StagedTile
                {
                    ManifestEntry = new NavTileManifestEntry
                    {
                        Layer = e.Layer,
                        ProfileId = e.ProfileId,
                        ChunkX = e.ChunkX,
                        ChunkY = e.ChunkY,
                        TileVersion = e.Tile.TileVersion,
                        TileChecksum = string.Empty // filled after staging, from disk
                    },
                    TempFile = string.Empty,
                    FinalFile = physical
                });
            }

            NavTileManifest? prior = null;
            if (!replaceWholeManifest)
            {
                prior = ReadRequiredPrior(ctx, manifestFinal);
            }

            // 2) Stage every tile to a temp file next to its target. Checksums are read back
            //    from these staged files on disk, then the temps are discarded/replaced later.
            string? dir = Path.GetDirectoryName(manifestFinal);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmpFiles = new List<string>();
            try
            {
                for (int i = 0; i < staged.Count; i++)
                {
                    StagedTile s = staged[i];
                    string tileDir = Path.GetDirectoryName(s.FinalFile);
                    if (!string.IsNullOrEmpty(tileDir)) Directory.CreateDirectory(tileDir);
                    string tmp = s.FinalFile + ".staging" + Guid.NewGuid().ToString("N");
                    tmpFiles.Add(tmp);
                    using (var fs = File.Create(tmp))
                    {
                        NavTileBinary.Write(fs, entries[i].Tile);
                    }

                    s.TempFile = tmp;
                }

                for (int i = 0; i < staged.Count; i++)
                {
                    StagedTile s = staged[i];
                    ulong diskChecksum;
                    using (var fs = File.OpenRead(s.TempFile))
                    {
                        diskChecksum = NavTileBinary.Read(fs).Checksum;
                    }

                    s.ManifestEntry.TileChecksum = "fnv1a64:" + diskChecksum.ToString("x16");
                }

                // 3) Assemble and validate the manifest content entirely in memory before publish.
                NavTileManifestEntry[] merged = BuildManifestEntries(ctx, prior, staged, replaceWholeManifest);
                var manifest = new NavTileManifest
                {
                    MapId = ctx.MapId,
                    BoardId = ctx.BoardId,
                    SourceRevision = ctx.SourceRevision,
                    Algorithm = ctx.Algorithm,
                    Mode = ctx.Mode,
                    TileVersion = ctx.TileVersion,
                    Tiles = merged,
                    WrittenUtc = ctx.WrittenUtc
                };
                manifest.ValidateStructure(); // fail before any rename if the batch is invalid
                manifest.BuildHash = manifest.ComputeBuildHash();

                // 4) Publish with rollback: tiles first, manifest last. Any failure restores the
                //    previous batch so an old manifest never pairs with partially new tiles.
                PublishWithRollback(staged, manifestFinal, manifest, tmpFiles);
                return manifest;
            }
            finally
            {
                foreach (string tmp in tmpFiles)
                {
                    try
                    {
                        if (File.Exists(tmp)) File.Delete(tmp);
                    }
                    catch
                    {
                        // best-effort cleanup
                    }
                }
            }
        }

        private static void ValidateContext(Context ctx)
        {
            if (string.IsNullOrWhiteSpace(ctx.RootDir) || !Directory.Exists(ctx.RootDir))
            {
                throw new InvalidOperationException("NavArtifactPublish requires an existing RootDir.");
            }

            if (string.IsNullOrWhiteSpace(ctx.MapId)) throw new InvalidOperationException("NavArtifactPublish requires a mapId.");
            if (ctx.BoardId == null) throw new InvalidOperationException("NavArtifactPublish boardId must be set (empty string for single-board maps).");
            if (string.IsNullOrWhiteSpace(ctx.SourceRevision))
            {
                throw new InvalidOperationException(
                    $"NavArtifactPublish for map '{ctx.MapId}' requires a real sourceRevision from the bake input; refusing to stamp a manifest without one.");
            }

            if (string.IsNullOrWhiteSpace(ctx.Algorithm) || string.IsNullOrWhiteSpace(ctx.Mode))
            {
                throw new InvalidOperationException($"NavArtifactPublish for map '{ctx.MapId}' requires algorithm and mode from the bake context.");
            }
        }

        private static void ValidateEntry(Context ctx, NavArtifactPublishEntry e)
        {
            if (e?.Tile == null) throw new ArgumentNullException(nameof(e), "Publish entry tile is null.");
            if (string.IsNullOrWhiteSpace(e.ProfileId))
            {
                throw new InvalidOperationException($"Publish entry for tile {e.Tile.TileId} has no profileId.");
            }

            if (e.Layer != e.Tile.TileId.Layer)
            {
                throw new InvalidOperationException(
                    $"Publish entry declares layer {e.Layer} but its tile payload identity is {e.Tile.TileId.Layer}; identity must come from one authority.");
            }

            if (e.ChunkX != e.Tile.TileId.ChunkX || e.ChunkY != e.Tile.TileId.ChunkY)
            {
                throw new InvalidOperationException(
                    $"Publish entry declares ({e.ChunkX},{e.ChunkY}) but its tile payload identity is {e.Tile.TileId}; identity must come from one authority.");
            }
        }

        private static string ResolveTilePath(Context ctx, NavArtifactPublishEntry e)
        {
            string rel = NavAssetPaths.GetNavTileRelativePath(
                ctx.MapId,
                string.IsNullOrEmpty(ctx.BoardId) ? null : ctx.BoardId,
                e.Layer,
                e.ProfileId,
                e.ChunkX,
                e.ChunkY);
            return Path.Combine(ctx.RootDir, rel.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string ResolveManifestPath(Context ctx)
        {
            string rel = NavAssetPaths.GetNavTileManifestRelativePath(
                ctx.MapId,
                string.IsNullOrEmpty(ctx.BoardId) ? null : ctx.BoardId);
            return Path.Combine(ctx.RootDir, rel.Replace('/', Path.DirectorySeparatorChar));
        }

        private static NavTileManifest ReadRequiredPrior(Context ctx, string manifestFinal)
        {
            if (!File.Exists(manifestFinal))
            {
                throw new InvalidOperationException(
                    $"NavArtifactPublish partial/merge for map '{ctx.MapId}' board '{ctx.BoardId}' has no prior manifest at '{manifestFinal}'. " +
                    "A partial bake cannot be the first publish for a board; run a full bake first.");
            }

            NavTileManifest prior;
            try
            {
                prior = NavTileManifestSerializer.Read(manifestFinal);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"NavArtifactPublish partial/merge for map '{ctx.MapId}' board '{ctx.BoardId}' found an unreadable prior manifest at '{manifestFinal}': {ex.Message}",
                    ex);
            }

            if (!string.Equals(prior.MapId, ctx.MapId, StringComparison.Ordinal) ||
                !string.Equals(prior.BoardId ?? string.Empty, ctx.BoardId ?? string.Empty, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"NavArtifactPublish partial/merge for map '{ctx.MapId}' board '{ctx.BoardId}' found a prior manifest for map '{prior.MapId}' board '{prior.BoardId}' at '{manifestFinal}'; scope mismatch.");
            }

            if (!string.Equals(prior.SourceRevision, ctx.SourceRevision, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"NavArtifactPublish partial/merge for map '{ctx.MapId}' board '{ctx.BoardId}' has a different source revision " +
                    $"('{ctx.SourceRevision}') than the prior manifest ('{prior.SourceRevision}'). " +
                    "A dirty bake must not stamp tiles that this bake never produced with a new source; run a full re-bake of the board so every tile shares one provenance.");
            }

            // Geometry-affecting configuration must also match: a dirty bake under different
            // bake config would make the untouched tiles' provenance ambiguous.
            if (!string.Equals(prior.Algorithm, ctx.Algorithm, StringComparison.Ordinal) ||
                !string.Equals(prior.Mode, ctx.Mode, StringComparison.Ordinal) ||
                prior.TileVersion != ctx.TileVersion)
            {
                throw new InvalidOperationException(
                    $"NavArtifactPublish partial/merge for map '{ctx.MapId}' board '{ctx.BoardId}' has a different bake configuration " +
                    $"(prior algorithm={prior.Algorithm}, mode={prior.Mode}, tileVersion={prior.TileVersion}; this bake algorithm={ctx.Algorithm}, mode={ctx.Mode}, tileVersion={ctx.TileVersion}) " +
                    "than the prior manifest. Geometry-affecting config cannot change in a partial bake; run a full re-bake of the board.");
            }

            return prior;
        }

        private static NavTileManifestEntry[] BuildManifestEntries(
            Context ctx,
            NavTileManifest? prior,
            List<StagedTile> staged,
            bool replaceWholeManifest)
        {
            var published = new NavTileManifestEntry[staged.Count];
            for (int i = 0; i < staged.Count; i++)
            {
                published[i] = staged[i].ManifestEntry;
            }

            if (replaceWholeManifest || prior == null)
            {
                return published;
            }

            var merged = new List<NavTileManifestEntry>(prior.Tiles!.Length + staged.Count);
            var replaced = new HashSet<string>(StringComparer.Ordinal);
            foreach (NavTileManifestEntry p in published)
            {
                replaced.Add(NavTileManifest.ManifestEntryKey(p));
            }

            foreach (NavTileManifestEntry existing in prior.Tiles!)
            {
                if (!replaced.Contains(NavTileManifest.ManifestEntryKey(existing)))
                {
                    merged.Add(existing);
                }
            }

            merged.AddRange(published);
            return merged.ToArray();
        }

        private static void PublishWithRollback(
            List<StagedTile> staged,
            string manifestFinal,
            NavTileManifest manifest,
            List<string> tmpFiles)
        {
            // Ordering invariants (manifest handled explicitly, never through the generic
            // target list):
            //   1. The old manifest is moved aside FIRST. From that instant until the new
            //      manifest is put back LAST, no reader can observe a valid whole group, so a
            //      running reader can never treat an old manifest as authoritative while tiles
            //      are being swapped underneath it.
            //   2. Tiles are backed up, then new tiles are published, then the manifest is
            //      published last. The manifest only returns when the new tile set is complete.
            //   3. On failure we undo only what this publish actually did: remove newly
            //      published tiles, restore backed-up tiles, and ONLY THEN restore the manifest
            //      (last). If any tile restore fails, the manifest is deliberately NOT
            //      restored, leaving the group non-loadable rather than serving an old
            //      manifest over a partial tile set.
            string manifestTemp = manifestFinal + ".staging" + Guid.NewGuid().ToString("N");
            NavTileManifestSerializer.Write(manifestTemp, manifest);
            tmpFiles.Add(manifestTemp);

            string? manifestBackup = null;
            var tileBackups = new List<(string Backup, string Final)>();
            var publishedTiles = new List<string>();

            try
            {
                // Backup phase: manifest first, then tiles.
                if (File.Exists(manifestFinal))
                {
                    manifestBackup = manifestFinal + ".bak" + Guid.NewGuid().ToString("N");
                    File.Move(manifestFinal, manifestBackup);
                }

                foreach (StagedTile s in staged)
                {
                    if (!File.Exists(s.FinalFile)) continue;
                    string backup = s.FinalFile + ".bak" + Guid.NewGuid().ToString("N");
                    File.Move(s.FinalFile, backup);
                    tileBackups.Add((backup, s.FinalFile));
                }

                // Publish phase: new tiles, then the manifest last.
                foreach (StagedTile s in staged)
                {
                    File.Move(s.TempFile, s.FinalFile, overwrite: true);
                    publishedTiles.Add(s.FinalFile);
                }

                File.Move(manifestTemp, manifestFinal, overwrite: true);
            }
            catch (Exception publishEx)
            {
                IOException? rollbackError = Rollback(tileBackups, publishedTiles, manifestBackup, manifestFinal);
                if (rollbackError != null)
                {
                    throw new IOException(
                        "NavArtifactPublish failed and rollback could not restore the previous batch; the group is " +
                        "intentionally left non-loadable (the manifest is not served). Manual recovery backups are " +
                        $"next to the targets as *.bak*. Original error: {publishEx.Message}; rollback error: {rollbackError.Message}",
                        rollbackError);
                }

                throw new IOException(
                    $"NavArtifactPublish aborted mid-publish and restored the previous batch: {publishEx.Message}",
                    publishEx);
            }

            // Success: drop backups (only copies of the previous batch remain).
            if (manifestBackup != null)
            {
                try
                {
                    if (File.Exists(manifestBackup)) File.Delete(manifestBackup);
                }
                catch
                {
                    // best-effort
                }
            }

            foreach ((string backup, string final) in tileBackups)
            {
                try
                {
                    if (File.Exists(backup)) File.Delete(backup);
                }
                catch
                {
                    // best-effort
                }
            }
        }

        /// <summary>
        /// Undoes only what this publish actually did. Returns null on full restore; otherwise
        /// returns an error after leaving the group non-loadable (manifest not restored).
        /// </summary>
        private static IOException? Rollback(
            List<(string Backup, string Final)> tileBackups,
            List<string> publishedTiles,
            string? manifestBackup,
            string manifestFinal)
        {
            try
            {
                // Remove newly published tiles (only the ones this publish placed).
                for (int i = publishedTiles.Count - 1; i >= 0; i--)
                {
                    if (File.Exists(publishedTiles[i])) File.Delete(publishedTiles[i]);
                }

                // Restore backed-up tiles.
                for (int i = tileBackups.Count - 1; i >= 0; i--)
                {
                    (string backup, string final) = tileBackups[i];
                    if (File.Exists(final)) File.Delete(final);
                    if (File.Exists(backup)) File.Move(backup, final);
                }

                // Restore the manifest LAST, only after every tile is back. If we reach here,
                // the old whole group is intact again.
                if (manifestBackup != null && File.Exists(manifestBackup))
                {
                    if (File.Exists(manifestFinal)) File.Delete(manifestFinal);
                    File.Move(manifestBackup, manifestFinal);
                }

                return null;
            }
            catch (Exception ex)
            {
                // Any tile restore failure means the manifest must NOT come back: an old
                // manifest over a partially-restored tile set would look valid to a reader.
                return new IOException($"could not restore all tiles; the manifest is left out and the group is non-loadable: {ex.Message}", ex);
            }
        }
    }
}
