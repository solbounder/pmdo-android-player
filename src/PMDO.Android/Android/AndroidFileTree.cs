using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Android.Content;
using Android.Database;
using Android.Provider;
using PMDO.Portable;
using AndroidUri = Android.Net.Uri;

namespace PMDO.Android
{
    public sealed class AndroidFileTree : IFileTree
    {
        private readonly ContentResolver resolver;
        private readonly Dictionary<string, AndroidUri> files = new Dictionary<string, AndroidUri>(StringComparer.Ordinal);
        public IReadOnlyList<string> Files { get; }

        public AndroidFileTree(ContentResolver resolver, AndroidUri treeUri)
        {
            this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            string rootId = DocumentsContract.GetTreeDocumentId(treeUri);
            Scan(treeUri, rootId, string.Empty);
            SafePaths.EnsureNoCaseCollisions(files.Keys);
            Files = files.Keys.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        }

        private void Scan(AndroidUri treeUri, string documentId, string relative)
        {
            AndroidUri children = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, documentId);
            string[] projection = { DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName, DocumentsContract.Document.ColumnMimeType };
            using ICursor cursor = resolver.Query(children, projection, null, null, null);
            if (cursor == null) throw new PortableImportException("Android document provider returned no cursor.");
            int idColumn = cursor.GetColumnIndexOrThrow(DocumentsContract.Document.ColumnDocumentId);
            int nameColumn = cursor.GetColumnIndexOrThrow(DocumentsContract.Document.ColumnDisplayName);
            int mimeColumn = cursor.GetColumnIndexOrThrow(DocumentsContract.Document.ColumnMimeType);
            while (cursor.MoveToNext())
            {
                string childId = cursor.GetString(idColumn);
                string name = cursor.GetString(nameColumn);
                string mime = cursor.GetString(mimeColumn);
                string path = SafePaths.Relative(string.IsNullOrEmpty(relative) ? name : relative + "/" + name);
                if (mime == DocumentsContract.Document.MimeTypeDir)
                    Scan(treeUri, childId, path);
                else
                    files.Add(path, DocumentsContract.BuildDocumentUriUsingTree(treeUri, childId));
            }
        }

        public Stream OpenRead(string relativePath) =>
            resolver.OpenInputStream(files[SafePaths.Relative(relativePath)]) ?? throw new FileNotFoundException(relativePath);

        public void Dispose() { }
    }
}
