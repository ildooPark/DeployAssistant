using System.Text.Json.Serialization;

namespace DeployAssistant.Model.V2
{
    /// <summary>
    /// Represents a single tracked change between two versions of the project.
    /// Replaces the V1 <c>ChangedFile</c> class.
    ///
    /// <para><b>V1 → V2 field translation table</b></para>
    /// <list type="table">
    ///   <listheader><term>V2 property</term><description>V1 property / note</description></listheader>
    ///   <item><term><see cref="Kind"/></term><description>ChangedFile.DataState (change-kind bits)</description></item>
    ///   <item><term><see cref="Before"/></term><description>ChangedFile.SrcFile — null when Kind == Added</description></item>
    ///   <item><term><see cref="After"/></term><description>ChangedFile.DstFile — null when Kind == Deleted</description></item>
    /// </list>
    ///
    /// <para>
    /// The V1 implicit null-contract (one of SrcFile/DstFile is null depending on state)
    /// is made explicit through the <see cref="Kind"/> discriminator and the named static
    /// factory methods.
    /// </para>
    /// </summary>
    public sealed class FileDiff
    {
        public ChangeKind   Kind   { get; init; }
        /// <summary>State before the change — <c>null</c> when <see cref="Kind"/> is <see cref="ChangeKind.Added"/>.</summary>
        public FileRecord?  Before { get; init; }
        /// <summary>State after the change — <c>null</c> when <see cref="Kind"/> is <see cref="ChangeKind.Deleted"/>.</summary>
        public FileRecord?  After  { get; init; }

        // ------------------------------------------------------------------ static factories

        /// <summary>A new file that did not exist before.</summary>
        public static FileDiff Added(FileRecord after)
        {
            if (after == null) throw new ArgumentNullException(nameof(after));
            return new FileDiff(ChangeKind.Added, null, after);
        }

        /// <summary>A file that has been removed.</summary>
        public static FileDiff Deleted(FileRecord before)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            return new FileDiff(ChangeKind.Deleted, before, null);
        }

        /// <summary>A file that exists in both versions but has changed content.</summary>
        public static FileDiff Modified(FileRecord before, FileRecord after)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (after  == null) throw new ArgumentNullException(nameof(after));
            return new FileDiff(ChangeKind.Modified, before, after);
        }

        /// <summary>A file that was previously deleted and is now restored.</summary>
        public static FileDiff Restored(FileRecord before, FileRecord after)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (after  == null) throw new ArgumentNullException(nameof(after));
            return new FileDiff(ChangeKind.Restored, before, after);
        }

        // ------------------------------------------------------------------ constructors

        /// <summary>
        /// JSON deserialisation constructor.  Prefer the static factory methods
        /// when constructing instances in code.
        /// </summary>
        [JsonConstructor]
        public FileDiff(ChangeKind Kind, FileRecord? Before, FileRecord? After)
        {
            this.Kind   = Kind;
            this.Before = Before;
            this.After  = After;
        }
    }
}
