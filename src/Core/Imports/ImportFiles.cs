using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Core.Imports
{
    /// <summary>
    /// What a set of chosen files is, as an import: which format each is in, what each
    /// declares, which go in and in what order, and which do not and why.
    ///
    /// **Pure over the files and never throws**, so every rule here is exercised with no TIA
    /// anywhere near it - the satellite that talks to TIA only asks what exists and imports.
    /// It is `Core`'s rather than the satellite's because the maintainer's next one,
    /// generating an object from a template, imports what it generates the same way.
    ///
    /// Decided with the maintainer on 2026-09-28:
    ///
    /// - **Each file is one unit.** A source declaring several objects goes in whole or not at
    ///   all, because that is how TIA generates one.
    /// - **A <c>.s7dcl</c> can be chosen alone or with its <c>.s7res</c>; a <c>.s7res</c> never
    ///   alone.** It is the second half of a pair, and <c>ImportFromDocuments</c> is given the
    ///   <c>.s7dcl</c>'s name.
    /// - **The same object chosen twice, in two formats, goes in twice**, the second over the
    ///   first. Which is meant is the operator's to know.
    /// - **Types, then tag tables, then FCs and FBs, then data blocks, then OBs** - see
    ///   <see cref="ImportRank"/>.
    /// </summary>
    public static class ImportFiles
    {
        public const string SimaticMlExtension = ".xml";
        public const string DocumentExtension = ".s7dcl";
        public const string ResourceExtension = ".s7res";

        /// <summary>The extensions TIA takes in as a source, each generating its blocks.</summary>
        public static readonly IReadOnlyList<string> SourceExtensions = new[] { ".scl", ".udt", ".db", ".awl" };

        /// <summary>
        /// Every extension the file chooser offers: the three formats, and the <c>.s7res</c> a
        /// <c>.s7dcl</c> may be chosen with.
        /// </summary>
        public static readonly IReadOnlyList<string> Extensions =
            new[] { SimaticMlExtension, DocumentExtension, ResourceExtension }.Concat(SourceExtensions).ToList();

        /// <summary>
        /// The format a file is imported in, or null for one that is not imported on its own -
        /// a <c>.s7res</c>, or anything else.
        /// </summary>
        public static ImportFormat? FormatOf(string path)
        {
            string extension = Extension(path);

            if (extension == SimaticMlExtension) return ImportFormat.SimaticMl;
            if (extension == DocumentExtension) return ImportFormat.Document;
            if (SourceExtensions.Contains(extension)) return ImportFormat.Source;
            return null;
        }

        /// <summary>
        /// One file, read for what it declares. Null for a file <see cref="FormatOf"/> does not
        /// import on its own; otherwise a unit, carrying the reason when what is inside could
        /// not be read.
        /// </summary>
        public static ImportFile Read(string path)
        {
            ImportFormat? format = FormatOf(path);
            if (format == null) return null;

            string problem;
            IReadOnlyList<DeclaredObject> objects = Declared(path, format.Value, out problem);
            string companion = format == ImportFormat.Document ? CompanionOf(path) : null;

            return new ImportFile(path, format.Value, companion, objects, problem);
        }

        /// <summary>
        /// The chosen files as an import: each unit read and put in its place in the order,
        /// and every file that is not a unit named with why.
        /// </summary>
        public static ImportSelection Choose(IEnumerable<string> paths)
        {
            List<string> chosen = (paths ?? Enumerable.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // A .s7res is absorbed into the .s7dcl of the same name when that was chosen too;
            // the pair is one unit, and the .s7dcl is what names it.
            HashSet<string> documents = new HashSet<string>(
                chosen.Where(p => Extension(p) == DocumentExtension).Select(p => Path.ChangeExtension(p, null)),
                StringComparer.OrdinalIgnoreCase);

            List<ImportFile> files = new List<ImportFile>();
            List<string> refused = new List<string>();

            foreach (string path in chosen)
            {
                string name = Path.GetFileName(path);
                string extension = Extension(path);

                if (extension == ResourceExtension)
                {
                    if (!documents.Contains(Path.ChangeExtension(path, null)))
                        refused.Add(name + ": a " + ResourceExtension + " is the second half of a " + DocumentExtension +
                                    " and is never imported alone - choose " + Path.GetFileNameWithoutExtension(path) +
                                    DocumentExtension + ".");
                    continue;
                }

                if (FormatOf(path) == null)
                {
                    refused.Add(name + ": not a format TIA Portal imports here (" + string.Join(", ", Imported()) + ").");
                    continue;
                }

                if (!File.Exists(path))
                {
                    refused.Add(name + ": it is no longer there.");
                    continue;
                }

                files.Add(Read(path));
            }

            List<ImportFile> ordered = files
                .OrderBy(f => f.Rank)
                .ThenBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.Path, StringComparer.Ordinal)
                .ToList();

            return new ImportSelection(ordered, refused);
        }

        private static IReadOnlyList<DeclaredObject> Declared(string path, ImportFormat format, out string problem)
        {
            problem = null;

            try
            {
                if (format == ImportFormat.SimaticMl)
                {
                    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        return SimaticMlObjects.Read(stream, out problem);
                }

                // Read the way StreamReader decides: a byte-order mark if there is one, UTF-8 if
                // not. The names are what is wanted, and TIA writes them in ASCII; a comment in
                // another code page costs nothing here.
                IReadOnlyList<DeclaredObject> declared;
                using (StreamReader reader = new StreamReader(
                           new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), true))
                    declared = SourceDeclarations.Read(reader.ReadToEnd());

                if (declared.Count == 0)
                    problem = "It declares no block, data type or data block.";

                return declared;
            }
            catch (Exception exception)
            {
                problem = "It could not be read: " + exception.Message;
                return new DeclaredObject[0];
            }
        }

        /// <summary>
        /// The <c>.s7res</c> beside a <c>.s7dcl</c>, whatever was ticked - it is what
        /// <c>ImportFromDocuments</c> will read. Null when there is none, or when the folder
        /// cannot be asked.
        /// </summary>
        private static string CompanionOf(string path)
        {
            try
            {
                string companion = Path.ChangeExtension(path, ResourceExtension);
                return File.Exists(companion) ? companion : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static IEnumerable<string> Imported() =>
            new[] { SimaticMlExtension, DocumentExtension }.Concat(SourceExtensions);

        private static string Extension(string path)
        {
            try
            {
                return (Path.GetExtension(path) ?? "").ToLowerInvariant();
            }
            catch (ArgumentException)
            {
                return "";
            }
        }
    }
}
