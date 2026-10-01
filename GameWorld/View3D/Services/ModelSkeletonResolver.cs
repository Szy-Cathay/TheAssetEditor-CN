using System;
using System.Collections.Generic;
using System.IO;
using Shared.ByteParsing;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.Vmd;
using Shared.GameFormats.WsModel;
using static Shared.GameFormats.Vmd.VariantMeshDefinition;

namespace GameWorld.Core.Services
{
    public class ModelSkeletonResolver(IPackFileService packFileService)
    {
        public Func<PackFile, bool> CreateFilter(string skeletonName)
        {
            var lookup = new ModelLookup(packFileService);
            var target = Path.GetFileNameWithoutExtension(skeletonName.Replace('/', '\\'));
            return file => !string.IsNullOrWhiteSpace(target) && string.Equals(
                lookup.GetSkeletonName(file), target, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class ModelLookup(IPackFileService packFileService)
        {
            private readonly Dictionary<PackFile, ModelNode?> _models = [];
            private readonly HashSet<PackFile> _reading = [];

            public string? GetSkeletonName(PackFile file)
            {
                try
                {
                    var root = ReadFile(file, 0);
                    if (root == null)
                        return null;

                    // Use the same breadth-first order as SceneNodeHelper.GetSkeletonName.
                    var queue = new Queue<ModelNode>();
                    queue.Enqueue(root);
                    while (queue.TryDequeue(out var node))
                    {
                        if (!string.IsNullOrWhiteSpace(node.SkeletonName))
                            return node.SkeletonName;
                        foreach (var child in node.Children)
                            queue.Enqueue(child);
                    }
                }
                catch (Exception)
                {
                    // Unreadable or incomplete definitions remain available without the filter.
                }
                return null;
            }

            private ModelNode? ReadFile(PackFile file, int depth)
            {
                if (_models.TryGetValue(file, out var cached))
                    return cached;
                if (depth > 64 || !_reading.Add(file))
                    throw new InvalidDataException("Cyclic or excessively nested model reference.");

                try
                {
                    ModelNode root;
                    switch (file.Extension.ToLowerInvariant())
                    {
                        case ".rigid_model_v2":
                            var modelSize = file.DataSource is PackedFileSource { IsCompressed: true } compressed
                                ? compressed.UncompressedSize : file.DataSource.Size;
                            if (modelSize < RmvFileHeader.HeaderSize)
                                throw new InvalidDataException("Truncated model header.");
                            var bytes = file.DataSource is PackedFileSource { IsCompressed: true }
                                ? file.DataSource.ReadData()
                                : file.DataSource.PeekData(RmvFileHeader.HeaderSize);
                            var header = ByteHelper.ByteArrayToStructure<RmvFileHeader>(bytes, 0);
                            if (header.FileType != "RMV2" || header.LodCount == 0)
                                throw new InvalidDataException("Invalid model header.");
                            root = new ModelNode(header.SkeletonName, true);
                            break;

                        case ".wsmodel":
                            root = new ModelNode();
                            var wsModel = new WsModelFile(file);
                            foreach (var path in wsModel.GeometryPaths)
                                AddReference(root, path, depth + 1);
                            break;

                        case ".variantmeshdefinition":
                            root = new ModelNode();
                            ReadVariantMesh(VariantMeshDefinitionLoader.Load(file), root, depth);
                            break;

                        default:
                            throw new InvalidDataException("Unsupported model format.");
                    }

                    _models[file] = root;
                    return root;
                }
                catch
                {
                    _models[file] = null;
                    throw;
                }
                finally
                {
                    _reading.Remove(file);
                }
            }

            private void ReadVariantMesh(VariantMesh mesh, ModelNode root, int depth)
            {
                if (depth > 64)
                    throw new InvalidDataException("Excessively nested variant mesh.");

                if (mesh.ChildSlots.Count != 0)
                {
                    var slots = new ModelNode();
                    root.Children.Add(slots);
                    root = slots;
                }

                if (!string.IsNullOrWhiteSpace(mesh.ModelReference))
                    AddReference(root, mesh.ModelReference, depth + 1);

                foreach (var slot in mesh.ChildSlots)
                {
                    var slotNode = new ModelNode();
                    root.Children.Add(slotNode);
                    foreach (var child in slot.ChildMeshes)
                    {
                        if (slotNode.HasGeometry)
                            break;
                        ReadVariantMesh(child, slotNode, depth + 2);
                    }
                    foreach (var reference in slot.ChildReferences)
                    {
                        if (slotNode.HasGeometry)
                            break;
                        AddReference(slotNode, reference.Reference, depth + 2);
                    }
                }
            }

            private void AddReference(ModelNode parent, string path, int depth)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;
                var file = packFileService.FindFile(path.Trim().Replace('/', '\\'))
                    ?? throw new FileNotFoundException("Referenced model not found.", path);
                var node = ReadFile(file, depth)
                    ?? throw new InvalidDataException("Unreadable model reference.");
                parent.Children.Add(node);
            }

            private sealed class ModelNode(string? skeletonName = null, bool hasGeometry = false)
            {
                public string? SkeletonName { get; } = skeletonName;
                public List<ModelNode> Children { get; } = [];
                public bool HasGeometry => hasGeometry || Children.Exists(child => child.HasGeometry);
            }
        }
    }
}
